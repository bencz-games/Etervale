using System.Buffers.Binary;
using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Progression;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Units;

namespace PristonTale.Client.Net.Online;

// Rebuilds the neutral presence model from the server's messages. Entries name entities by this connection's handles: a
// spawn binds a handle to an entity id and its removal frees it. The server pushes a description ahead of the entry that
// introduces it or its revision; an object whose description did not come (or a shared template's of another revision)
// is asked with DescribeEntity. The object is published once its description is known. Event lists name entities by
// the same handles.
internal sealed class PresenceDecoder : IEventHandleReader
{
    // A description not answered is asked for again after this.
    public const long DescriptionRetryMs = 1000;
    // A description whose entity never spawned (one asked for that left meanwhile) is dropped after this long.
    private const long UnspawnedDescriptionMs = 10_000;
    // The receive loop decodes; the connection's timer retries descriptions.
    private readonly object _gate = new();
    private readonly Dictionary<ulong, Tracked> _objects = new();
    private readonly Dictionary<uint, ulong> _handles = new();
    // Monster and NPC descriptions are shared by template (PresenceWire.TemplateKey), like the original client's enemy cache.
    private readonly Dictionary<uint, Description> _templates = new();
    private readonly WorldRenderCatalog _catalog;
    private readonly Action<ulong> _requestDescription;
    private readonly Dictionary<int, InventoryItemEntry[]> _inventoryParts = new();
    private readonly OwnerItemDecoder _ownerItems = new();
    private OwnerStatusPacket? _status;
    private long _inventoryRevision = -1;
    private long _partsRevision = -1;
    private IReadOnlyList<OwnedItemView> _items = [];
    private long _skillsRevision = -1;
    private IReadOnlyList<OwnedSkillView> _skills = [];
    private long _tick;
    private double? _clockMs;
    private IReadOnlyList<ItemDetailEntry> _tradeDetails = [];
    // The states of a presence message are written into a list the decoder lends to its update; the consumer gives it
    // back once applied (Recycle), and only the update it was lent to can. Once MaxLent loans are outstanding (a consumer
    // that never gives them back) the decoder forgets them. The pool has its own lock, so the game thread's Recycle never
    // waits on a decode.
    private const int MaxLent = 1024;
    private readonly object _poolGate = new();
    private readonly Dictionary<List<EntityStateInfo>, PresenceUpdate> _lentStates = new();
    private readonly Stack<List<EntityStateInfo>> _freeStates = new();
    private readonly PresenceEntriesReader.EntryHandler _onEntry;
    private List<EntitySpawnInfo>? _entrySpawns;
    private List<EntityStateInfo>? _entryStates;

    public PresenceDecoder(WorldRenderCatalog catalog, Action<ulong> requestDescription)
    {
        _catalog = catalog;
        _requestDescription = requestDescription;
        _onEntry = OnEntry;
    }

    public ulong SelfEntityId { get; set; }

    public int? FieldId { get; private set; }

    bool IEventHandleReader.TryGetEntity(uint handle, out ulong entityId) => _handles.TryGetValue(handle, out entityId);

    // Decodes one presence packet; returns false for opcodes that are not part of presence.
    public bool TryHandle(ReadOnlySpan<byte> packet, out PresenceUpdate? update)
    {
        lock (_gate)
        {
            return TryHandleLocked(packet, out update);
        }
    }

    // Asks again for the descriptions whose answer did not come within a second, and drops descriptions no spawn followed.
    // The connection calls it about once a second: an object that stands still sends no entries that would trigger the
    // request again.
    public void RetryDescriptions()
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;
            List<ulong>? unspawned = null;
            foreach (var (entityId, tracked) in _objects)
            {
                if (!tracked.HasState && now - tracked.DescribedAtMs > UnspawnedDescriptionMs)
                {
                    (unspawned ??= []).Add(entityId);
                }
                else if (tracked.HasState && (tracked.Description is null || tracked.Description.Revision != tracked.Revision))
                {
                    RequestDescription(entityId, tracked, tracked.Revision);
                }
            }

            foreach (var entityId in unspawned ?? [])
            {
                _objects.Remove(entityId);
            }
        }
    }

    private bool TryHandleLocked(ReadOnlySpan<byte> packet, out PresenceUpdate? update)
    {
        var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        var presence = true;
        update = code switch
        {
            EtervaleOpcodes.PresenceEntries => OnPresenceEntries(packet),
            EtervaleOpcodes.EntityDescription => OnDescription(EntityDescriptionPacket.Read(packet)),
            EtervaleOpcodes.RemoveObjects => OnRemoved(RemoveObjectList.Read(packet)),
            EtervaleOpcodes.CombatEvents => OnCombatEvents(CombatEventList.Read(packet, this)),
            EtervaleOpcodes.GroundItems => OnGroundItems(GroundItemList.Read(packet)),
            EtervaleOpcodes.GroundItemsRemoved => new PresenceUpdate(_tick, [], [], [], [], FieldId) { GroundRemoved = GroundItemRemovals.Read(packet) },
            EtervaleOpcodes.FieldWarp => OnFieldWarp(FieldWarpPacket.Read(packet)),
            EtervaleOpcodes.OwnerStatus => OnOwnerStatus(OwnerStatusPacket.Read(packet)),
            EtervaleOpcodes.Inventory => OnInventory(InventoryPartPacket.Read(packet)),
            EtervaleOpcodes.Skills => OnSkills(SkillsPacket.Read(packet)),
            EtervaleOpcodes.PresenceClock => OnPresenceClock(PresenceClockPacket.Read(packet)),
            EtervaleOpcodes.SkillEvents => OnSkillEvents(SkillEventList.Read(packet, this)),
            EtervaleOpcodes.StatusEffects => OnStatusEffects(StatusEffectList.Read(packet, this)),
            EtervaleOpcodes.SkillCooldowns => OnSkillCooldowns(SkillCooldownsPacket.Read(packet)),
            EtervaleOpcodes.SkillCastResult => OnSkillCastResult(SkillCastResultPacket.Read(packet)),
            EtervaleOpcodes.ItemDetails => OnItemDetails(ItemDetailsPartPacket.Read(packet)),
            EtervaleOpcodes.OwnerItemStatus => OnOwnerItemStatus(OwnerItemStatusPacket.Read(packet)),
            EtervaleOpcodes.AgingProgress => OnAgingProgress(AgingProgressPacket.Read(packet)),
            EtervaleOpcodes.PremiumTimers => OnPremiumTimers(PremiumTimersPacket.Read(packet)),
            EtervaleOpcodes.ItemEffects => OnItemEffects(ItemEffectsPacket.Read(packet)),
            EtervaleOpcodes.RestartOffer => OnRestartOffer(RestartOfferPacket.Read(packet)),
            EtervaleOpcodes.QuestJournal => new PresenceUpdate(_tick, [], [], [], [], FieldId) { Quest = QuestWireReader.ToView(QuestJournalPacket.Read(packet)) },
            EtervaleOpcodes.QuestNotices => OnQuestNotices(QuestNoticesPacket.Read(packet)),
            EtervaleOpcodes.ChatLine => OnChat(ChatLinePacket.Read(packet)),
            EtervaleOpcodes.CloseRequest => OnCloseRequest(packet),
            EtervaleOpcodes.PartyMembers => OnPartyMembers(PartyMembersPacket.Read(packet)),
            EtervaleOpcodes.PartyRequest => OnPartyRequest(PartyRequestPacket.Read(packet)),
            EtervaleOpcodes.ClanState => new PresenceUpdate(_tick, [], [], [], [], FieldId) { Clan = Clan(ClanStatePacket.Read(packet)) },
            EtervaleOpcodes.CastleState => new PresenceUpdate(_tick, [], [], [], [], FieldId) { Castle = Castle(CastleStatePacket.Read(packet)) },
            EtervaleOpcodes.CastleOwnerState => new PresenceUpdate(_tick, [], [], [], [], FieldId) { CastleOwner = CastleOwner(CastleOwnerStatePacket.Read(packet)) },
            EtervaleOpcodes.BellatraBoard => new PresenceUpdate(_tick, [], [], [], [], FieldId) { Bellatra = Bellatra(BellatraBoardPacket.Read(packet)) },
            EtervaleOpcodes.BellatraResult => new PresenceUpdate(_tick, [], [], [], [], FieldId) { BellatraResult = BellatraResult(BellatraResultPacket.Read(packet)) },
            EtervaleOpcodes.BellatraClanState => new PresenceUpdate(_tick, [], [], [], [], FieldId) { BellatraClan = BellatraClan(BellatraClanStatePacket.Read(packet)) },
            EtervaleOpcodes.BellatraClanResult => new PresenceUpdate(_tick, [], [], [], [], FieldId)
            {
                BellatraClanResult = BellatraClanResultPacket.Read(packet) is var bellatra ? new BellatraClanResultInfo(bellatra.Action, (Contracts.Social.BellatraClanStatus)bellatra.Status) : null
            },
            EtervaleOpcodes.CastleOwnerResult => new PresenceUpdate(_tick, [], [], [], [], FieldId)
            {
                CastleOwnerResult = CastleOwnerResultPacket.Read(packet) is var result
                    ? new CastleOwnerResultInfo(result.Action, (Simulation.Social.Castle.CastleOwnerStatus)result.Status)
                    : null
            },
            EtervaleOpcodes.ClanInvite => new PresenceUpdate(_tick, [], [], [], [], FieldId)
            {
                ClanInvitation = ClanInvitePacket.Read(packet) is var invite ? new ClanInvitationInfo(invite.RecruiterId, invite.ClanName, invite.ChiefName) : null
            },
            EtervaleOpcodes.ClanResult => new PresenceUpdate(_tick, [], [], [], [], FieldId)
            {
                ClanResult = ClanResultPacket.Read(packet) is var result ? new ClanResultInfo((byte)result.Action, (Simulation.Social.Clans.ClanStatus)result.Status) : null
            },
            EtervaleOpcodes.TradeDetails => OnTradeDetails(TradeDetailsPacket.Read(packet)),
            EtervaleOpcodes.TradeState => OnTradeState(TradeStatePacket.Read(packet)),
            EtervaleOpcodes.TradeAsked => OnTradeAsked(TradeAskedPacket.Read(packet)),
            EtervaleOpcodes.ContactList => OnContactList(ContactListPacket.Read(packet)),
            EtervaleOpcodes.Weather => new PresenceUpdate(_tick, [], [], [], [], FieldId) { Weather = WeatherPacket.Read(packet).Weather },
            _ => NotPresence(out presence)
        };
        return presence;

        // Another stream's message: no update, and not handled here.
        static PresenceUpdate? NotPresence(out bool presence)
        {
            presence = false;
            return null;
        }
    }

    private PresenceUpdate OnContactList(ContactListPacket packet) =>
        new PresenceUpdate(_tick, [], [], [], [], FieldId) { Contacts = new ContactListInfo(packet.List, packet.Entries.Select(static entry => (entry.Name, entry.Online)).ToArray()) };

    // The partner items' values arrive just before the window they belong to.
    private PresenceUpdate? OnTradeDetails(TradeDetailsPacket packet)
    {
        _tradeDetails = packet.Items;
        return null;
    }

    private PresenceUpdate OnTradeState(TradeStatePacket packet)
    {
        if (packet.PartnerId == 0)
        {
            return new PresenceUpdate(_tick, [], [], [], [], FieldId) { Trade = TradeInfo.None };
        }

        var items = new OwnedItemView[packet.PartnerItems.Count];
        for (var index = 0; index < items.Length; index++)
        {
            var view = StoreItems.View(packet.PartnerItems[index]);
            foreach (var detail in _tradeDetails)
            {
                if (detail.InstanceId == view.InstanceId)
                {
                    view = view with { Details = OwnerItemDecoder.View(detail) };
                    break;
                }
            }

            items[index] = view;
        }

        var own = new Simulation.Trades.TradeEntry[packet.Own.Count];
        for (var index = 0; index < own.Length; index++)
        {
            own[index] = new Simulation.Trades.TradeEntry(packet.Own[index].ItemId, packet.Own[index].Cell);
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId)
        {
            Trade = new TradeInfo(packet.PartnerId, packet.PartnerName, new Simulation.Trades.TradeOffer(own, packet.OwnMoney),
                (packet.Flags & TradeStatePacket.OwnLocked) != 0, items, packet.PartnerMoney, (packet.Flags & TradeStatePacket.PartnerLocked) != 0)
        };
    }

    private PresenceUpdate OnTradeAsked(TradeAskedPacket packet) =>
        new PresenceUpdate(_tick, [], [], [], [], FieldId) { TradeRequest = new TradeRequestInfo(packet.FromId, packet.FromName) };

    private PresenceUpdate OnPartyMembers(PartyMembersPacket packet)
    {
        var members = new PartyMemberInfo[packet.Members.Count];
        for (var index = 0; index < members.Length; index++)
        {
            var member = packet.Members[index];
            members[index] = new PartyMemberInfo(member.EntityId, member.Name, member.Level, member.Life, member.MaxLife, new Vector2(member.X, member.Z));
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { Party = new PartyInfo(members) };
    }

    private static ClanInfo Clan(ClanStatePacket packet) => !packet.HasClan ? ClanInfo.None
        : new ClanInfo((long)packet.ClanId, packet.Name, (Simulation.Social.Clans.ClanState)packet.State, packet.Deadline, (Simulation.Social.Clans.ClanRank)packet.Rank,
            packet.Capacity, packet.Intro, packet.MarkVersion, packet.Members.Select(static member => new ClanMemberInfo(member.Name, member.Class, member.Level,
                (Simulation.Social.Clans.ClanRank)member.Rank, member.Online)).ToArray());

    private static CastleInfo Castle(CastleStatePacket packet) =>
        new((long)packet.OwnerClanId, packet.OwnerName, packet.Tax, (Simulation.Social.Castle.SiegeState)packet.Siege, packet.League, packet.SecondsLeft,
            packet.DoorDown, packet.Crystals, [.. packet.Top.Select(static clan => new CastleClanInfo((long)clan.ClanId, clan.Name, (long)clan.Damage))],
            (long)packet.TotalDamage);

    private static BellatraBoardInfo Bellatra(BellatraBoardPacket packet) => !packet.Shown ? BellatraBoardInfo.Hidden
        : new BellatraBoardInfo(true, packet.Round, packet.Team, (long)Math.Min(packet.Score, long.MaxValue),
            [.. packet.TeamScores.Select(static score => (long)Math.Min(score, long.MaxValue))], (Contracts.Social.BellatraOutcome)packet.Outcome,
            packet.StageTeams, packet.GoingOn);

    private static BellatraResultInfo BellatraResult(BellatraResultPacket packet) =>
        new(packet.Final, [.. packet.Lines.Select(static line => new BellatraResultLineInfo(line.Name, (Contracts.Characters.PlayerClass)line.Class,
            (long)Math.Min(line.Score, long.MaxValue), (int)Math.Min(line.Kills, int.MaxValue)))]);

    private static BellatraClanInfo BellatraClan(BellatraClanStatePacket packet) => !packet.HasView ? BellatraClanInfo.None
        : new BellatraClanInfo(true, (long)Math.Min(packet.OperatingClanId, long.MaxValue), packet.OperatingName, packet.Chief, packet.Tax,
            (long)Math.Min(packet.Balance, long.MaxValue), packet.CanRedeem,
            [.. packet.Top.Select(static clan => new BellatraClanRankInfo(clan.Name, (long)Math.Min(clan.Points, long.MaxValue)))]);

    private static CastleOwnerInfo CastleOwner(CastleOwnerStatePacket packet) => !packet.HasView ? CastleOwnerInfo.None
        : new CastleOwnerInfo(true, (long)Math.Min(packet.Treasury, long.MaxValue), packet.Tax,
            new Simulation.Social.Castle.CastleSetup(packet.NextTax, (Simulation.Social.Castle.CastleClanSkill)packet.Skill,
                [.. packet.Towers.Select(static tower => (Simulation.Social.Castle.CastleTower)tower)], [.. packet.Mercenaries.Select(static count => (int)count)]),
            (Simulation.Social.Castle.CastleClanSkill)packet.SkillInForce, packet.CanEdit);

    private PresenceUpdate OnPartyRequest(PartyRequestPacket packet) =>
        new PresenceUpdate(_tick, [], [], [], [], FieldId) { PartyRequest = new PartyRequestInfo(packet.FromId, packet.FromName, packet.Recommended) };

    private PresenceUpdate OnChat(ChatLinePacket packet) =>
        new PresenceUpdate(_tick, [], [], [], [], FieldId) { Chat = [new ChatLineInfo(packet.Kind, packet.Speaker, packet.Text, packet.Line)] };

    private PresenceUpdate OnQuestNotices(QuestNoticesPacket packet) =>
        new PresenceUpdate(_tick, [], [], [], [], FieldId) { QuestNotices = QuestWireReader.ToNotices(packet.Notices), QuestPrompt = QuestWireReader.ToPrompt(packet.Prompt) };

    public PresenceUpdate OnSkillEvents(SkillEventEntry[] entries)
    {
        var events = new SkillEventInfo[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            events[index] = new SkillEventInfo(entry.Caster, entry.Target, entry.SkillCode, entry.Phase, entry.Rank, entry.Flags, entry.DurationMs,
                entry.HitAtMs, Position(entry.X, entry.Y, entry.Z), GameUnits.FromAngle(entry.Angle), entry.TargetCount, entry.Radius);
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { SkillEvents = events, ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnStatusEffects(StatusEffectEntry[] entries)
    {
        var statuses = new StatusEffectInfo[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            statuses[index] = new StatusEffectInfo(entry.Entity, entry.Source, entry.StatusCode, entry.Op, entry.Stacks, entry.SourceKind,
                entry.Control, entry.Flags, entry.DurationMs, entry.RemainingMs, entry.Value);
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { StatusEffects = statuses, ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnSkillCooldowns(SkillCooldownsPacket packet)
    {
        var cooldowns = new SkillCooldownInfo[packet.Cooldowns.Count];
        for (var index = 0; index < cooldowns.Length; index++)
        {
            var entry = packet.Cooldowns[index];
            cooldowns[index] = new SkillCooldownInfo(entry.SkillCode, (int)entry.RemainingMs, (int)entry.DurationMs);
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { Cooldowns = new SkillCooldownSnapshot(_clockMs, cooldowns) };
    }

    public PresenceUpdate OnSkillCastResult(SkillCastResultPacket result) => new PresenceUpdate(_tick, [], [], [], [], FieldId)
    {
        CastResult = new SkillCastResultInfo(result.Sequence, result.SkillCode, result.Status, result.Rank, (int)result.CooldownMs, (int)result.RemainingMs,
            result.Mana, result.Stamina)
    };

    public PresenceUpdate OnPremiumTimers(PremiumTimersPacket packet)
    {
        var timers = new List<PremiumTimerInfo>(packet.Timers.Count);
        foreach (var entry in packet.Timers)
        {
            if (Enum.IsDefined((PremiumEffectKind)entry.Kind))
            {
                timers.Add(new PremiumTimerInfo((PremiumEffectKind)entry.Kind, entry.Value, entry.RemainingSeconds, entry.DurationSeconds));
            }
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { PremiumTimers = new PremiumTimerSnapshot(packet.Revision, timers) };
    }

    public PresenceUpdate OnItemEffects(ItemEffectsPacket packet)
    {
        var effects = new ItemEffectInfo[packet.Effects.Count];
        for (var index = 0; index < effects.Length; index++)
        {
            var entry = packet.Effects[index];
            effects[index] = new ItemEffectInfo(entry.Entity, entry.ItemCode, (ItemEffectKind)entry.Effect, Position(entry.X, entry.Y, entry.Z), entry.Variant,
                entry.Target);
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { ItemEffects = effects, ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnRestartOffer(RestartOfferPacket packet) =>
        new PresenceUpdate(_tick, [], [], [], [], FieldId) { RestartOffer = new RestartOffer(packet.FieldOpen, packet.Scroll, packet.ExperienceShare, packet.GoldShare) };

    public double? ServerTimeMs => _clockMs;

    // A server without the extension leaves the time unset and the client falls back to arrival time.
    public PresenceUpdate? OnPresenceClock(PresenceClockPacket clock)
    {
        _clockMs = clock.ServerTimeMs;
        return null;
    }

    private PresenceUpdate? OnPresenceEntries(ReadOnlySpan<byte> packet)
    {
        // The message sets the presence clock its samples are taken at.
        var clock = PresenceEntriesReader.Clock(packet);
        _tick = clock.ServerTick;
        _clockMs = clock.ServerTimeMs;
        _entrySpawns = null;
        lock (_poolGate)
        {
            _entryStates = _freeStates.TryPop(out var free) ? free : new List<EntityStateInfo>(PresenceEntriesReader.Count(packet));
        }

        PresenceEntriesReader.Read(packet, _onEntry);
        var (spawns, states) = (_entrySpawns, _entryStates);
        _entrySpawns = null;
        _entryStates = null;
        lock (_poolGate)
        {
            if (spawns is null && states.Count == 0)
            {
                _freeStates.Push(states);
                return null;
            }

            if (_lentStates.Count >= MaxLent)
            {
                _lentStates.Clear();
            }

            var update = new PresenceUpdate(_tick, (IReadOnlyList<EntitySpawnInfo>?)spawns ?? [], states, [], [], FieldId) { ServerTimeMs = _clockMs };
            _lentStates.Add(states, update);
            return update;
        }
    }

    // Gives back the states list of an update the consumer is done with; another update may then reuse it.
    public void Recycle(PresenceUpdate update)
    {
        if (update.States is not List<EntityStateInfo> states)
        {
            return;
        }

        lock (_poolGate)
        {
            if (_lentStates.TryGetValue(states, out var holder) && ReferenceEquals(holder, update))
            {
                _lentStates.Remove(states);
                states.Clear();
                _freeStates.Push(states);
            }
        }
    }

    private void OnEntry(in PresenceEntry entry)
    {
        Tracked? tracked;
        ulong entityId;
        var states = _entryStates!;
        if (entry.Spawn)
        {
            entityId = entry.EntityId;
            if (!_objects.TryGetValue(entityId, out tracked))
            {
                tracked = new Tracked();
                _objects.Add(entityId, tracked);
            }
            else if (tracked.Handle != entry.Handle && _handles.TryGetValue(tracked.Handle, out var bound) && bound == entityId)
            {
                _handles.Remove(tracked.Handle);
            }

            tracked.Handle = entry.Handle;
            _handles[entry.Handle] = entityId;
        }
        else if (!_handles.TryGetValue(entry.Handle, out entityId) || !_objects.TryGetValue(entityId, out tracked) || !tracked.HasState)
        {
            // Only a spawn introduces an object.
            return;
        }

        tracked.Point = entry.Absolute ? entry.Position : tracked.Point + entry.Position;
        tracked.Position = tracked.Point.ToWorld();
        tracked.Facing = entry.FacingRadians;
        tracked.Motion = entry.Motion;
        tracked.SampleTimeMs = _clockMs;
        if (!entry.Spawn && entry.Fields == PresenceFields.None)
        {
            if (tracked.Published)
            {
                states.Add(State(entityId, tracked));
            }

            return;
        }

        var fields = entry.Fields;
        var described = tracked.Description;
        if ((fields & PresenceFields.FieldCode) != 0 && entityId == SelfEntityId && entry.FieldCode >= 0)
        {
            FieldId = entry.FieldCode;
        }

        tracked.Life = (fields & PresenceFields.Life) != 0 ? entry.Life : tracked.Life;
        tracked.MaxLife = (fields & PresenceFields.MaxLife) != 0 ? entry.MaxLife : tracked.MaxLife;
        tracked.Target = (fields & PresenceFields.Target) != 0 ? entry.Target : tracked.Target;
        tracked.Sequence = (fields & PresenceFields.AttackSequence) != 0 ? entry.AttackSequence : tracked.Sequence;
        tracked.Template = (fields & PresenceFields.Template) != 0 ? entry.Template : tracked.Template;
        tracked.Revision = (fields & PresenceFields.Revision) != 0 ? entry.Revision : tracked.Revision;
        tracked.HasState = true;
        if ((tracked.Description is null || tracked.Description.Revision != tracked.Revision) && tracked.Template != 0
            && _templates.TryGetValue(tracked.Template, out var shared) && shared.Revision == tracked.Revision)
        {
            tracked.Description = shared;
        }

        if (tracked.Description is null || tracked.Description.Revision != tracked.Revision)
        {
            RequestDescription(entityId, tracked, tracked.Revision);
        }

        // What a state does not carry (max life, the description) reaches a shown object as a refresh, which the scene
        // applies as a spawn of a known id.
        if (tracked.Published && tracked.Description is not null
            && ((fields & (PresenceFields.MaxLife | PresenceFields.Template | PresenceFields.Revision)) != 0 || !ReferenceEquals(described, tracked.Description)))
        {
            (_entrySpawns ??= []).Add(Spawn(entityId, tracked));
            return;
        }

        Publish(entityId, tracked, states);
    }

    // One DescribeEntity request per revision, repeated after a second when its answer does not come: the server drops requests
    // beyond its rate bucket (a crowd entering view at once), and an object without a description is never shown.
    private void RequestDescription(ulong entityId, Tracked tracked, int revision)
    {
        var now = Environment.TickCount64;
        if (tracked.RequestedRevision == revision && now - tracked.RequestedAtMs < DescriptionRetryMs)
        {
            return;
        }

        tracked.RequestedRevision = revision;
        tracked.RequestedAtMs = now;
        _requestDescription(entityId);
    }

    private void Publish(ulong entityId, Tracked tracked, List<EntityStateInfo> states)
    {
        if (tracked.Description is null)
        {
            return;
        }

        if (!tracked.Published)
        {
            tracked.Published = true;
            (_entrySpawns ??= []).Add(Spawn(entityId, tracked));
        }
        else
        {
            states.Add(State(entityId, tracked));
        }
    }

    public PresenceUpdate? OnDescription(EntityDescriptionPacket packet)
    {
        var entityId = packet.EntityId;
        if (!_objects.TryGetValue(entityId, out var tracked))
        {
            // Pushed ahead of the spawn it describes.
            tracked = new Tracked { DescribedAtMs = Environment.TickCount64 };
            _objects.Add(entityId, tracked);
        }

        var description = new Description(packet.Revision, packet.Kind, packet);
        tracked.Description = description;
        if (packet.Template is { } template)
        {
            // A summon's description names its owner, so it never stands for the wild monsters of its template.
            if (template.Template != 0 && template.Owner == 0)
            {
                _templates[PresenceWire.TemplateKey(description.Kind, template.Template)] = description;
            }

            if (description.Kind == EntityKind.Monster)
            {
                _catalog.Register(new MonsterRenderInfo(template.Template, packet.Model, template.TemplateScale, packet.Name, packet.Level,
                    (MonsterBrood)template.Brood, template.PortraitY, template.PortraitZ, (int)template.SoundCode, template.Class)
                {
                    ShadowSize = template.ShadowSize
                });
            }
            else if (description.Kind == EntityKind.Npc)
            {
                _catalog.Register(new NpcRenderInfo(template.Template, packet.Model, template.TemplateScale, packet.Name,
                    new NpcServiceSet(template.NpcServices), (int)template.SoundCode, template.PortraitY, template.PortraitZ)
                {
                    ShadowSize = template.ShadowSize
                });
            }
        }

        // A description pushed ahead of the entry that brings its revision waits for that entry, which refreshes the object.
        if (!tracked.HasState || tracked.Published && description.Revision != tracked.Revision)
        {
            return null;
        }

        // A new description is a full refresh, which the scene applies as a spawn of a known id.
        tracked.Published = true;
        return Update([Spawn(entityId, tracked)], []);
    }

    public PresenceUpdate OnRemoved(ulong[] entityIds)
    {
        var removed = new List<ulong>(entityIds.Length);
        foreach (var entityId in entityIds)
        {
            if (!_objects.Remove(entityId, out var tracked))
            {
                continue;
            }

            if (_handles.TryGetValue(tracked.Handle, out var bound) && bound == entityId)
            {
                _handles.Remove(tracked.Handle);
            }

            if (tracked.Published)
            {
                removed.Add(entityId);
            }
        }

        return Update([], [], removed);
    }

    public PresenceUpdate OnCombatEvents(CombatEventEntry[] entries)
    {
        var events = new List<CombatEventInfo>(entries.Length);
        foreach (var entry in entries)
        {
            events.Add(new CombatEventInfo(entry.Attacker, entry.Target, entry.Kind, entry.DurationMs, entry.HitAtMs,
                entry.Damage, (entry.Flags & CombatEventFlags.Critical) != 0, (entry.Flags & CombatEventFlags.Killed) != 0, entry.Resources,
                entry.SoundCode, Position(entry.X, entry.Y, entry.Z), GameUnits.FromAngle(entry.Angle))
            {
                Clip = entry.Clip,
                Skill = (entry.Flags & CombatEventFlags.Skill) != 0
            });
        }

        return new PresenceUpdate(_tick, [], [], [], events, FieldId) { ServerTimeMs = _clockMs };
    }

    public PresenceUpdate OnGroundItems(GroundItemEntry[] items)
    {
        var added = new List<GroundDropView>(items.Length);
        foreach (var item in items)
        {
            var key = item.Gold ? ItemCodes.GoldKey : ItemCodes.KeyOf(item.ItemCode);
            added.Add(new GroundDropView(item.Id, item.ItemCode, key, item.Name, item.Amount, item.Gold, Position(item.X, item.Y, item.Z)));
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { GroundAdded = added };
    }

    public PresenceUpdate OnFieldWarp(FieldWarpPacket warp)
    {
        FieldId = warp.Field;
        _objects.TryGetValue(SelfEntityId, out var self);
        var position = new Vector3(warp.X, warp.Y, warp.Z);
        if (self is not null)
        {
            self.Position = position;
        }

        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { Warp = new WarpInfo(warp.Field, position, warp.Forced) };
    }

    private PresenceUpdate OnCloseRequest(ReadOnlySpan<byte> packet)
    {
        CloseRequestPacket.Read(packet);
        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { CloseRequested = true };
    }

    public PresenceUpdate? OnOwnerStatus(OwnerStatusPacket status)
    {
        _status = status;
        return OwnerUpdate();
    }

    public PresenceUpdate? OnItemDetails(ItemDetailsPartPacket part)
    {
        _ownerItems.OnDetails(part);
        return null;
    }

    public PresenceUpdate? OnOwnerItemStatus(OwnerItemStatusPacket status)
    {
        _ownerItems.OnStatus(status);
        return null;
    }

    public PresenceUpdate? OnAgingProgress(AgingProgressPacket progress)
    {
        _ownerItems.OnAging(progress);
        return OwnerUpdate();
    }

    public PresenceUpdate? OnInventory(InventoryPartPacket part)
    {
        if (part.Revision != _partsRevision)
        {
            _inventoryParts.Clear();
            _partsRevision = part.Revision;
        }

        _inventoryParts[part.Part] = part.Items.ToArray();
        if (_inventoryParts.Count < part.Parts)
        {
            return null;
        }

        _items = _ownerItems.Attach(part.Revision, _inventoryParts.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).Select(Item).ToArray());
        _inventoryRevision = part.Revision;
        _inventoryParts.Clear();
        return OwnerUpdate();
    }

    public PresenceUpdate? OnSkills(SkillsPacket skills)
    {
        _skills = skills.Skills.Select(skill => new OwnedSkillView(skill.Code, skill.Rank, skill.Uses)).ToArray();
        _skillsRevision = skills.Revision;
        return OwnerUpdate();
    }

    private PresenceUpdate? OwnerUpdate()
    {
        var status = _status;
        if (status is null || status.InventoryRevision != _inventoryRevision || status.SkillsRevision != _skillsRevision)
        {
            return null;
        }

        var owner = new OwnerState(status.Level, status.Experience, status.LevelStart, status.NextLevel, status.Money, status.Life, status.MaxLife,
            status.Mana, status.MaxMana, status.Stamina, status.MaxStamina, status.StatPoints, status.SkillPoints, status.Strength, status.Spirit, status.Talent,
            status.Agility, status.Health, status.DamageMin, status.DamageMax, status.Defence, status.Absorption, status.InventoryRevision, _items)
        {
            AttackRange = status.AttackRange,
            AttackRating = status.AttackRating,
            MoveSpeed = status.MoveSpeed,
            ProgressionRevision = status.ProgressionRevision,
            SkillsRevision = status.SkillsRevision,
            Tier = status.Tier,
            EliteSkillPoints = status.EliteSkillPoints,
            BagPage = status.BagPage,
            WeaponSet = status.WeaponSet,
            Skills = _skills
        };
        return new PresenceUpdate(_tick, [], [], [], [], FieldId) { Owner = _ownerItems.Apply(owner) };
    }

    private static OwnedItemView Item(InventoryItemEntry entry) => new(entry.InstanceId, entry.Code, entry.Name, (ItemKind)entry.Kind, entry.Count,
        entry.Anchor, entry.Slot, entry.Width, entry.Height, ItemCodes.KeyOf(entry.Code)) { Link = entry.Link };

    private PresenceUpdate Update(IReadOnlyList<EntitySpawnInfo> spawns, IReadOnlyList<EntityStateInfo> states, IReadOnlyList<ulong>? removed = null,
        IReadOnlyList<CombatEventInfo>? events = null) =>
        new(_tick, spawns, states, removed ?? [], events ?? [], FieldId);

    private static Simulation.Items.HeldItemGlow Glow(in DescribedGlow glow) =>
        new(glow.R, glow.G, glow.B, glow.A, glow.Blink, glow.Scale, glow.Mix, glow.MixScroll, glow.ClassUp);

    private EntitySpawnInfo Spawn(ulong entityId, Tracked tracked)
    {
        var description = tracked.Description!;
        var packet = description.Packet;
        PlayerAppearanceInfo? player = null;
        if (packet.Player is { } looks)
        {
            player = new PlayerAppearanceInfo(packet.Name, looks.Class, looks.Tier, looks.Face, looks.Hair, packet.Model, looks.HeadModel, packet.Level,
                ItemCodes.KeyOf(looks.Weapon), ItemCodes.KeyOf(looks.Armor), ItemCodes.KeyOf(looks.OffHand), looks.InVillage)
            {
                HelperPet = looks.HelperPet,
                Phoenix = looks.Phoenix,
                BigHead = looks.BigHead,
                WeaponGlow = Glow(looks.WeaponGlow),
                OffHandGlow = Glow(looks.OffHandGlow),
                ShopSign = looks.ShopSign,
                ClanId = (long)looks.ClanId,
                ClanName = looks.ClanName,
                ClanMark = looks.ClanMark
            };
        }

        var template = packet.Template;
        var owner = template is { Owner: not 0 } ? template.Owner : 0UL;
        return new EntitySpawnInfo(entityId, description.Kind, entityId == SelfEntityId, tracked.Position, tracked.Facing, tracked.Motion, tracked.Life,
            tracked.MaxLife, player, template?.Template ?? 0, packet.MoveSpeed, AttackRate(packet), tracked.Target, tracked.Sequence, tracked.SampleTimeMs,
            packet.Name, packet.Level)
        {
            OwnerId = owner,
            ModelScale = owner != 0 ? template!.InstanceScale : 0f
        };
    }

    private static EntityStateInfo State(ulong entityId, Tracked tracked)
    {
        var packet = tracked.Description!.Packet;
        return new EntityStateInfo(entityId, tracked.Position, tracked.Facing, tracked.Motion, tracked.Life, packet.MoveSpeed, AttackRate(packet),
            tracked.Target, tracked.Sequence, tracked.SampleTimeMs);
    }

    private static float AttackRate(EntityDescriptionPacket packet) =>
        float.IsFinite(packet.AttackRate) && packet.AttackRate > 0f ? packet.AttackRate : 1f;

    private static Vector3 Position(int x, int y, int z) => new(GameUnits.FromFixed(x), GameUnits.FromFixed(y), GameUnits.FromFixed(z));


    private sealed record Description(byte Revision, EntityKind Kind, EntityDescriptionPacket Packet);

    private sealed class Tracked
    {
        public Description? Description { get; set; }
        public int RequestedRevision { get; set; } = -1;
        public long RequestedAtMs { get; set; }
        public byte Revision { get; set; }
        public uint Handle { get; set; }
        public long DescribedAtMs { get; init; }
        public uint Template { get; set; }
        public PresencePoint Point { get; set; }
        public bool HasState { get; set; }
        public bool Published { get; set; }
        public Vector3 Position { get; set; }
        public float Facing { get; set; }
        public MotionState Motion { get; set; }
        public int Life { get; set; }
        public int MaxLife { get; set; }
        public ulong Target { get; set; }
        public ulong Sequence { get; set; }
        public double? SampleTimeMs { get; set; }
    }
}
