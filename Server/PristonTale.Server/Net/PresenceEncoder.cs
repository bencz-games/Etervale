using System.Numerics;
using System.Runtime.InteropServices;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Loot;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;

namespace PristonTale.Server.Net;

// Turns one subscriber's share of a field snapshot into presence messages. It keeps the original's view ranges and
// distance-weighted detail, but queries the field grid instead of scanning every slot, sends an entity only when it
// changed and only what changed (a spawn names it under a handle of this receiver's and carries every value; a later
// entry carries the values that changed and its position as a change from the last one sent), pushes its description
// ahead of its spawn, and lets the near ring update faster than the far ring.
internal sealed partial class PresenceEncoder : IEventHandleWriter
{
    // The view ranges: connect and high detail (ViewRanges), ground items 0xC8000 squared world units.
    public static readonly float ViewRadius = ViewRanges.Connect;
    private static readonly float ForgetRadius = ViewRadius * VisibleSet.KeepBand;
    private static readonly float NearRadius = ViewRanges.Near;
    private static readonly float ItemRadius = MathF.Sqrt(0xC8000);
    // 5 Hz inside the high-detail range and 2 Hz out to the connect range; the original relayed players at about 2.2 Hz and monsters at 1.1 Hz.
    private const uint NearIntervalTicks = 4;
    // Routing pays a binary search per known entity; scanning pays two lookups per event of the field.
    private const int RouteThreshold = 2;
    private const uint FarIntervalTicks = 10;
    // Like the original per-receiver user limit, a crowd cannot exceed a per-receiver movement budget (about 40 KiB/s at 20 Hz).
    // Spawns and discrete changes always go out; position-only updates are ranked by how overdue they are.
    private const int MovementBudgetBytes = 2048;
    private const ushort TickIntervalMs = ServerTick.IntervalMs;

    private readonly ulong _self;
    private readonly WorldDataHolder _data;
    private readonly WorldDataLanguagesHolder _localizedData;
    private readonly string _language;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, Known> _known = new();
    private readonly Dictionary<Guid, GroundDropView> _drops = new();
    private readonly List<int> _candidates = new();
    // The entities a receiver may show at once, players at most MaxVisiblePlayers of them: the nearest win in a denser
    // crowd, players first, and monsters and NPCs fill the rest. A client draws a crowd of players at a cost a crowd of
    // monsters does not have.
    public const int MaxVisible = 256;
    public const int MaxVisiblePlayers = 100;
    private readonly VisibleSet _visiblePlayers = new();
    private readonly VisibleSet _visible = new();
    private readonly List<int> _related = new();
    private readonly HashSet<int> _essential = new();
    private readonly Func<int, bool> _isShown;
    private readonly MotionCandidates _pending = new(MovementBudgetBytes / PresenceWire.MinEntrySize);
    // Handles of removed entities. An entity is found removed only after every spawn of its encode, and its removal goes
    // out before the next encode, so the client never reads a spawn under a handle before the removal that freed it.
    private readonly Stack<uint> _freeHandles = new();
    private uint _nextHandle;
    // The description revision this receiver last got for each shared template, as its client keeps them.
    private readonly Dictionary<uint, byte> _sentTemplates = new();
    private readonly HashSet<Guid> _visibleDrops = new();
    private readonly List<Guid> _goneDrops = new();
    private readonly List<ulong> _removed = new();
    // Entities this receiver started seeing in the snapshot being encoded (every entity shown when its events were lost);
    // their running statuses follow.
    private readonly List<ulong> _spawned = new();
    private readonly List<CombatEventEntry> _events = new();
    private readonly List<SkillEventEntry> _skillEvents = new();
    private readonly List<StatusEffectEntry> _statusEvents = new();
    // The events routed to this receiver this frame: (kind << 32 | index), then the indices of each kind in order.
    private readonly List<long> _routes = new();
    private readonly List<int> _routedCombat = new();
    private readonly List<int> _routedSkills = new();
    private readonly List<int> _routedStatuses = new();
    private readonly List<int> _routedItems = new();
    private readonly List<GroundItemEntry> _newDrops = new();
    private readonly byte[] _scratch = new byte[FrameCodec.MaxFrameSize];
    private readonly byte[] _group = new byte[FrameCodec.MaxFrameSize];
    private int? _field;
    private OwnerState? _owner;
    private long _inventoryRevision = -1;
    private long _skillsRevision = -1;
    private long _agingRevision = -1;
    private uint _clockTick;
    private bool _clockPending;
    private uint _pass;
    private SharedPresenceWire? _wire;
    private Simulation.Progression.RestartOffer? _restartOffer;
    private PartyView? _party;
    // The weather last sent; null until the first snapshot sends it.
    private WeatherState? _weather;
    private PartyRequest? _partyRequest;
    private BellatraView? _bellatra;
    private TradeView? _trade;
    private TradeRequest? _tradeRequest;
    private PresenceFrame? _heldFrame;
    private EntityView[] _describeViews = [];
    private EntityGrid? _describeGrid;
    private EntityView[] _entities = [];

    public PresenceEncoder(ulong self, WorldDataHolder data, WorldDataLanguagesHolder? localizedData = null, string language = CatalogLanguages.English)
    {
        _self = self;
        _data = data;
        _localizedData = localizedData ?? new WorldDataLanguagesHolder();
        _language = CatalogLanguages.Normalize(language);
        _isShown = index => _known.ContainsKey(_entities[index].EntityId);
    }

    private WorldData LocalizedData => _localizedData.Get(_language, _data.Current);

    // The frame's shared descriptions carry the names of the authoritative catalog; a session reading another language's
    // names encodes its own.
    private bool SharesDescriptions => ReferenceEquals(LocalizedData, _data.Current);

    public EntityKind? KindOf(ulong entityId)
    {
        lock (_gate)
        {
            return _known.TryGetValue(entityId, out var known) ? known.Kind : null;
        }
    }

    public Vector3? SelfPosition
    {
        get
        {
            lock (_gate)
            {
                return _known.TryGetValue(_self, out var known) ? known.Position : null;
            }
        }
    }

    // Answers DescribeEntity with the entity's description from the last encoded snapshot (its frame stays pinned until
    // the next one); false when the entityId is not in this subscriber's view. It is sent under the encoder's lock, so it
    // reaches the client in the order of the encodes that note which template descriptions the client holds. Every
    // receiver asking about an entity of the same frame gets the same bytes, encoded once per frame.
    public bool SendRequestedDescription(ulong entityId, IPacketSink connection)
    {
        lock (_gate)
        {
            if (_describeGrid is null || !_known.TryGetValue(entityId, out var known) || !_describeGrid.TryGetIndex(entityId, out var index))
            {
                return false;
            }

            ref readonly var view = ref _describeViews[index];
            if (_heldFrame is { } frame && view.Revision != 0 && ReferenceEquals(frame.Views, _describeViews) && SharesDescriptions)
            {
                NoteTemplate(view, view.Revision);
                connection.Send(frame.Attachment(SharedPresenceWire.Create).Description(index, view, LocalizedData));
                return true;
            }

            NoteTemplate(view, known.Revision);
            connection.Send(_scratch.AsSpan(0, DescriptionPacket(view, known.Revision, LocalizedData).Write(_scratch)));
            return true;
        }
    }

    internal static EntityDescriptionPacket DescriptionPacket(in EntityView view, byte revision, WorldData data) => WireMapping.Describe(view, revision, data);

    // The connection ended: the last snapshot's frame is no longer needed for descriptions.
    public void Close()
    {
        lock (_gate)
        {
            HoldForDescriptions(null, [], null);
        }
    }

    private void HoldForDescriptions(PresenceFrame? frame, EntityView[] views, EntityGrid? grid)
    {
        _heldFrame?.Release(_self);
        _heldFrame = frame;
        _describeViews = views;
        _describeGrid = grid;
    }

    public void Encode(in WorldSnapshot snapshot, IPacketSink connection)
    {
        lock (_gate)
        {
            EncodeLocked(snapshot, connection);
        }
    }

    private void EncodeLocked(in WorldSnapshot snapshot, IPacketSink connection)
    {
        var grid = snapshot.Grid ?? EntityGrid.Build(snapshot.Entities);
        if (!grid.TryGetIndex(_self, out var selfIndex))
        {
            // No own entity (a snapshot whose frame the field already reused): its reliable events still go out.
            SendEventsOnly(snapshot, connection);
            return;
        }

        var self = snapshot.Entities[selfIndex];
        var field = snapshot.FieldId ?? 0;
        // A frame is pinned again for the descriptions asked about this snapshot; a hand-built snapshot is kept as is.
        if (snapshot.Frame is not { } described || described.TryPin(snapshot.FrameGeneration, _self))
        {
            HoldForDescriptions(snapshot.Frame, snapshot.Entities, grid);
        }

        if (_field != field)
        {
            if (_field is not null)
            {
                ForgetAll(connection);
            }

            _field = field;
            // Confirms the authoritative field and position.
            connection.Send(_scratch.AsSpan(0, new FieldWarpPacket(field, self.Position.X, self.Position.Y, self.Position.Z, Forced: false).Write(_scratch)));
        }

        var tick = snapshot.ServerTick;
        var combat = snapshot.Events.AsSpan();
        var skills = snapshot.SkillEvents.AsSpan();
        var statuses = snapshot.StatusEvents.AsSpan();
        var items = snapshot.ItemEffects.AsSpan();
        // Events of earlier ticks this receiver fell behind on go first, each on its own tick of the timeline.
        foreach (var segment in snapshot.EventSegments ?? [])
        {
            if (segment.Tick == tick)
            {
                break;
            }

            _clockTick = segment.Tick;
            _clockPending = true;
            SendEvents(combat[..segment.Combat], connection);
            SendSkillEvents(skills[..segment.Skills], connection);
            SendStatusEvents(statuses[..segment.Statuses], snapshot, connection, withSpawned: false);
            SendItemEffects(items[..segment.Items], connection);
            combat = combat[segment.Combat..];
            skills = skills[segment.Skills..];
            statuses = statuses[segment.Statuses..];
            items = items[segment.Items..];
        }

        _clockTick = tick;
        _clockPending = true;
        // Descriptions every receiver of this frame shares are encoded once by whichever encoder needs them first.
        _wire = snapshot.Frame is { } frame && ReferenceEquals(frame.Views, snapshot.Entities) && SharesDescriptions
            ? frame.Attachment(SharedPresenceWire.Create)
            : null;
        _entities = snapshot.Entities;
        _pass++;
        var group = new PresenceEntriesWriter(_group, tick, TickIntervalMs);
        _removed.Clear();
        _spawned.Clear();
        WriteEntity(ref group, connection, selfIndex, tick, field, near: true);

        _candidates.Clear();
        _pending.Clear();
        _visiblePlayers.Clear();
        _visible.Clear();
        _essential.Clear();
        // Entities a receiver must not lose in a crowd: NPCs, its attack target, whoever targets it, its summons and its
        // party.
        _related.Clear();
        _related.AddRange(grid.Npcs);
        grid.Related(_self, _related);
        if (self.AttackTargetId != 0 && grid.TryGetIndex(self.AttackTargetId, out var targetIndex))
        {
            _related.Add(targetIndex);
        }

        var members = snapshot.Party?.Members ?? [];
        for (var index = 0; index < members.Count; index++)
        {
            if (grid.TryGetIndex(members[index].EntityId, out var memberIndex))
            {
                _related.Add(memberIndex);
            }
        }

        foreach (var index in _related)
        {
            if (_essential.Add(index))
            {
                ConsiderVisible(snapshot, field, index, self.Position, essential: true);
            }
        }

        grid.Query(self.Position, CrowdRadius(grid, self.Position), _candidates);
        foreach (var index in _candidates)
        {
            if (!_essential.Contains(index))
            {
                ConsiderVisible(snapshot, field, index, self.Position, essential: false);
            }
        }

        _visiblePlayers.Trim(MaxVisiblePlayers, _isShown);
        _visible.Trim(MaxVisible - _visiblePlayers.Count, _isShown);
        WriteVisible(ref group, connection, _visiblePlayers, snapshot, self.Position, tick, field);
        WriteVisible(ref group, connection, _visible, snapshot, self.Position, tick, field);

        var budget = MovementBudgetBytes;
        foreach (var (index, _) in _pending.Ordered())
        {
            var size = MotionSize(index);
            if (budget < size)
            {
                break;
            }

            budget -= size;
            WriteMotion(ref group, connection, index, tick);
        }

        foreach (var (entityId, known) in _known)
        {
            if (known.Pass != _pass)
            {
                _removed.Add(entityId);
                _freeHandles.Push(known.Handle);
            }
        }

        foreach (var entityId in _removed)
        {
            _known.Remove(entityId);
        }

        FlushGroup(ref group, connection);
        _wire = null;
        _entities = [];
        SendRemovals(connection);
        if (snapshot.EventsLost)
        {
            // The statuses that started on entities already shown were among the events lost.
            _spawned.Clear();
            _spawned.AddRange(_known.Keys);
        }

        var routed = RouteEvents(snapshot);
        SendEvents(combat, connection, routed ? _routedCombat : null);
        SendSkillEvents(skills, connection, routed ? _routedSkills : null);
        SendStatusEvents(statuses, snapshot, connection, withSpawned: true, routed ? _routedStatuses : null);
        SendItemEffects(items, connection, routed ? _routedItems : null);
        SendDrops(snapshot, self.Position, connection);
        SendOwner(snapshot.Owner, connection);
        SendPremium(snapshot.Owner, connection);
        SendCooldowns(snapshot, connection);
        SendRestartOffer(snapshot.Restart, connection);
        SendWeather(snapshot.Weather, connection);
        SendParty(snapshot, connection);
        SendTrade(snapshot, connection);
        SendBellatra(snapshot, connection);
        if (snapshot.Quest is not null)
        {
            SendQuest(snapshot.Quest, connection);
        }
    }

    // The receiver's Bellatra board whenever it changes, hidden once it leaves the arena.
    private void SendBellatra(in WorldSnapshot snapshot, IPacketSink connection)
    {
        if (ReferenceEquals(snapshot.Bellatra, _bellatra))
        {
            return;
        }

        _bellatra = snapshot.Bellatra;
        var packet = BellatraBoardPacket.Hidden;
        if (_bellatra is { } board)
        {
            var teams = new ulong[board.TeamScores.Count];
            for (var team = 0; team < teams.Length; team++)
            {
                teams[team] = (ulong)Math.Max(0, board.TeamScores[team]);
            }

            packet = new BellatraBoardPacket(true, (byte)board.Round, (byte)board.Team, (byte)board.Outcome, (byte)board.StageTeams, (byte)board.GoingOn,
                (ulong)Math.Max(0, board.Score), teams);
        }
        connection.Send(_scratch.AsSpan(0, packet.Write(_scratch)));
    }

    // The party's members whenever they change (none once the receiver leaves it), and each new request to join one.
    private void SendParty(in WorldSnapshot snapshot, IPacketSink connection)
    {
        if (!ReferenceEquals(snapshot.Party, _party))
        {
            _party = snapshot.Party;
            var members = _party?.Members ?? [];
            var wire = new PartyMemberWire[members.Count];
            for (var index = 0; index < wire.Length; index++)
            {
                var member = members[index];
                wire[index] = new PartyMemberWire(member.EntityId, member.Name, member.Level, member.Life, member.MaxLife, member.X, member.Z);
            }

            connection.Send(_scratch.AsSpan(0, new PartyMembersPacket(wire).Write(_scratch)));
        }

        if (snapshot.PartyRequest is { } request && !ReferenceEquals(request, _partyRequest))
        {
            _partyRequest = request;
            connection.Send(_scratch.AsSpan(0, new PartyRequestPacket(request.FromId, request.FromName, request.Recommendation ? request.JoinerName : "").Write(_scratch)));
        }
    }

    // The trade window whenever it changes (an empty one once it ends): the partner items' values first, then the window;
    // and each new request to trade.
    private void SendTrade(in WorldSnapshot snapshot, IPacketSink connection)
    {
        if (!ReferenceEquals(snapshot.Trade, _trade))
        {
            _trade = snapshot.Trade;
            if (_trade is { } view)
            {
                var details = new ItemDetailEntry[view.PartnerItems.Count];
                var items = new StoreItemEntry[details.Length];
                for (var index = 0; index < details.Length; index++)
                {
                    var item = view.PartnerItems[index];
                    details[index] = ItemWireMapping.Detail(item);
                    items[index] = ItemWireMapping.StoreItem(item);
                }

                var own = new TradeEntryWire[view.Own.Entries.Count];
                for (var index = 0; index < own.Length; index++)
                {
                    own[index] = new TradeEntryWire(view.Own.Entries[index].ItemId, view.Own.Entries[index].Cell);
                }

                var flags = (byte)((view.OwnLocked ? TradeStatePacket.OwnLocked : 0) | (view.PartnerLocked ? TradeStatePacket.PartnerLocked : 0));
                connection.Send(_scratch.AsSpan(0, new TradeDetailsPacket(details).Write(_scratch)));
                connection.Send(_scratch.AsSpan(0, new TradeStatePacket(view.PartnerId, view.PartnerName, flags, view.Own.Money, view.PartnerMoney, own, items)
                    .Write(_scratch)));
            }
            else
            {
                connection.Send(_scratch.AsSpan(0, new TradeStatePacket(0, "", 0, 0, 0, [], []).Write(_scratch)));
            }
        }

        if (snapshot.TradeRequest is { } request && !ReferenceEquals(request, _tradeRequest))
        {
            _tradeRequest = request;
            connection.Send(_scratch.AsSpan(0, new TradeAskedPacket(request.FromId, request.FromName).Write(_scratch)));
        }
    }

    // A dead receiver learns what standing up would cost whenever that changes.
    private void SendRestartOffer(Simulation.Progression.RestartOffer? offer, IPacketSink connection)
    {
        if (offer == _restartOffer)
        {
            return;
        }

        _restartOffer = offer;
        if (offer is { } current)
        {
            var packet = new RestartOfferPacket(current.FieldOpen, current.Scroll, current.ExperienceShare, current.GoldShare);
            connection.Send(_scratch.AsSpan(0, packet.Write(_scratch)));
        }
    }

    // Its own method: a lambda capturing the connection in EncodeLocked would allocate a closure on every encode.
    private void SendQuest(QuestUpdate quest, IPacketSink connection) =>
        QuestWireMapping.Send(quest, LocalizedData.Items, (write, capacity) => SendSized(connection, write, capacity));

    private void SendEventsOnly(in WorldSnapshot snapshot, IPacketSink connection)
    {
        _clockTick = snapshot.ServerTick;
        _clockPending = true;
        SendEvents(snapshot.Events, connection);
        SendSkillEvents(snapshot.SkillEvents, connection);
        SendStatusEvents(snapshot.StatusEvents, snapshot, connection, withSpawned: false);
        SendItemEffects(snapshot.ItemEffects, connection);
        SendOwner(snapshot.Owner, connection);
        SendPremium(snapshot.Owner, connection);
        SendCooldowns(snapshot, connection);
        SendWeather(snapshot.Weather, connection);
    }

    private void SendWeather(WeatherState weather, IPacketSink connection)
    {
        if (weather != _weather)
        {
            _weather = weather;
            connection.Send(_scratch.AsSpan(0, new WeatherPacket(weather).Write(_scratch)));
        }
    }

    // In a crowd denser than the visible cap, only the neighbourhood that holds the nearest MaxVisible entities (and the
    // keep band around it) is collected: a square of half size h holding at least that many puts all of them within
    // h times the square root of two (SquareCornerReach, rounded up).
    private const float SquareCornerReach = 1.42f;

    private static float CrowdRadius(EntityGrid grid, Vector3 center)
    {
        if (grid.Count <= MaxVisible * 2)
        {
            return ForgetRadius;
        }

        for (var half = EntityGrid.CellSize * 1.5f; half * SquareCornerReach * VisibleSet.KeepBand < ForgetRadius; half *= 1.6f)
        {
            if (grid.CountInSquare(center, half) >= MaxVisible * 2)
            {
                return half * SquareCornerReach * VisibleSet.KeepBand;
            }
        }

        return ForgetRadius;
    }

    private void WriteVisible(ref PresenceEntriesWriter group, IPacketSink connection, VisibleSet visible, in WorldSnapshot snapshot, Vector3 center,
        uint tick, int field)
    {
        foreach (var (index, _) in visible.Entries)
        {
            ref readonly var view = ref snapshot.Entities[index];
            var distanceSq = Planar.DistanceSquared(view.Position, center);
            var near = distanceSq <= NearRadius * NearRadius;
            if (WriteEntity(ref group, connection, index, tick, field, near) is { } age)
            {
                // Overdue ratio: the wanted interval grows with distance, and every waiting entity keeps gaining
                // priority, so a crowd is served round-robin.
                var wanted = NearIntervalTicks + MathF.Sqrt(distanceSq) / 50f;
                _pending.Add(index, age / wanted);
            }
        }
    }

    private void ConsiderVisible(in WorldSnapshot snapshot, int field, int index, Vector3 center, bool essential)
    {
        ref readonly var view = ref snapshot.Entities[index];
        if (view.EntityId == _self || view.Hidden || HiddenInArena(field, view))
        {
            return;
        }

        var distanceSq = Planar.DistanceSquared(view.Position, center);
        if (distanceSq > ForgetRadius * ForgetRadius || distanceSq > ViewRadius * ViewRadius && !_known.ContainsKey(view.EntityId))
        {
            return;
        }

        (view.Kind == EntityKind.Player ? _visiblePlayers : _visible).Add(index, distanceSq, essential);
    }

    // The duel arena is solo: a player sees only the opponent and helper of its own duel.
    private bool HiddenInArena(int field, in EntityView view) =>
        field == QuestArena.FieldCode && view.Kind != EntityKind.Npc && (view.Kind == EntityKind.Player || view.DuelPlayerId != _self);

    private void SendSized(IPacketSink connection, Func<Span<byte>, int> write, int capacity)
    {
        if (capacity > _scratch.Length)
        {
            throw new InvalidOperationException("Quest message exceeds the frame size.");
        }

        connection.Send(_scratch.AsSpan(0, write(_scratch)));
    }

    // Writes spawns and discrete changes immediately. Returns the age in ticks of a due position-only update, which the
    // caller schedules under the movement budget, or null when nothing else is due.
    private uint? WriteEntity(ref PresenceEntriesWriter group, IPacketSink connection, int index, uint tick, int field, bool near)
    {
        ref readonly var view = ref _entities[index];
        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(_known, view.EntityId, out var exists);
        if (!exists)
        {
            var descriptor = view.Revision == 0 ? DescriptorHash(view) : 0;
            known = new Known(view, view.Revision == 0 ? (byte)1 : view.Revision, descriptor, _pass, TakeHandle());
            _spawned.Add(view.EntityId);
            SendDescription(index, known.Revision, connection);
            AppendEntry(ref group, connection, ref known, view, field, tick, PresenceFields.All, spawn: true);
            return null;
        }

        known.Pass = _pass;
        var fields = known.Changed(view, field);
        if (view.Revision != 0)
        {
            if (view.Revision != known.Revision)
            {
                known.Revision = view.Revision;
                fields |= PresenceFields.Revision;
            }
        }
        else
        {
            // A view built outside a field carries no revision; the encoder numbers its descriptions itself.
            var descriptor = DescriptorHash(view);
            if (descriptor != known.Descriptor)
            {
                known.Descriptor = descriptor;
                known.Revision = (byte)(known.Revision == byte.MaxValue ? 1 : known.Revision + 1);
                fields |= PresenceFields.Revision;
            }
        }

        if ((fields & PresenceFields.Revision) != 0)
        {
            SendDescription(index, known.Revision, connection);
        }

        if (fields != PresenceFields.None || known.MoveState != view.MoveState)
        {
            AppendEntry(ref group, connection, ref known, view, field, tick, fields, spawn: false);
            return null;
        }

        var moved = known.Position != view.Position || known.Facing != view.Facing;
        var age = tick - known.LastSentTick;
        if (!moved || age < (near ? NearIntervalTicks : FarIntervalTicks))
        {
            return null;
        }

        if (view.EntityId == _self)
        {
            WriteMotion(ref group, connection, index, tick);
            return null;
        }

        return age;
    }

    private void WriteMotion(ref PresenceEntriesWriter group, IPacketSink connection, int index, uint tick)
    {
        ref readonly var view = ref _entities[index];
        ref var known = ref CollectionsMarshal.GetValueRefOrNullRef(_known, view.EntityId);
        AppendEntry(ref group, connection, ref known, view, known.FieldCode, tick, PresenceFields.None, spawn: false);
    }

    // The size of the position-only entry an entity would get now.
    private int MotionSize(int index)
    {
        ref readonly var view = ref _entities[index];
        ref var known = ref CollectionsMarshal.GetValueRefOrNullRef(_known, view.EntityId);
        var (sent, _, absolute) = Placement(view, known, spawn: false);
        return PresenceWire.MotionSize(known.Handle, sent, absolute);
    }

    // The position an entry sends (whole, or as the change from the last one sent when that is smaller), and the point
    // the client then holds.
    private static (PresencePoint Sent, PresencePoint Point, bool Absolute) Placement(in EntityView view, in Known known, bool spawn)
    {
        var point = PresencePoint.From(view.Position);
        var change = point - known.Point;
        var absolute = spawn || PresenceWire.PrefersAbsolute(change);
        return (absolute ? point : change, point, absolute);
    }

    private void AppendEntry(ref PresenceEntriesWriter group, IPacketSink connection, ref Known known, in EntityView view, int field, uint tick,
        PresenceFields fields, bool spawn)
    {
        var (sent, point, absolute) = Placement(view, known, spawn);
        var entry = new PresenceEntry(known.Handle, view.MoveState, sent, PresenceEntry.ToFacing(view.Facing), absolute,
            spawn ? view.EntityId : 0, fields, view.Life, view.MaxLife, view.AttackTargetId, view.AttackSequence, known.Revision,
            WireMapping.SharedTemplate(view), field);
        EnsureSpace(ref group, connection, PresenceWire.MaxEntrySize);
        group.Commit(PresenceWire.Write(group.Reserve(PresenceWire.MaxEntrySize), entry));
        known.Sent(view, tick, field, point);
    }

    private uint TakeHandle() => _freeHandles.TryPop(out var handle) ? handle : _nextHandle++;

    // Pushes the entity's description ahead of the entry that introduces it or its revision, unless it is a shared
    // template's description the client already holds.
    private void SendDescription(int index, byte revision, IPacketSink connection)
    {
        ref readonly var view = ref _entities[index];
        var template = WireMapping.SharedTemplate(view);
        if (template != 0 && _sentTemplates.TryGetValue(template, out var held) && held == revision)
        {
            return;
        }

        NoteTemplate(view, revision);
        if (_wire is { } wire && view.Revision != 0)
        {
            connection.Send(wire.Description(index, view, LocalizedData));
            return;
        }

        connection.Send(_scratch.AsSpan(0, DescriptionPacket(view, revision, LocalizedData).Write(_scratch)));
    }

    // The client keeps the last description of each shared template it was sent.
    private void NoteTemplate(in EntityView view, byte revision)
    {
        if (WireMapping.SharedTemplate(view) is var template and not 0)
        {
            _sentTemplates[template] = revision;
        }
    }

    private void EnsureSpace(ref PresenceEntriesWriter group, IPacketSink connection, int size)
    {
        if (group.Free < size)
        {
            FlushGroup(ref group, connection);
        }
    }

    private void FlushGroup(ref PresenceEntriesWriter group, IPacketSink connection)
    {
        if (group.Count == 0)
        {
            return;
        }

        // The message carries this tick's clock.
        _clockPending = false;
        connection.Send(group.Finish());
        group.Reset();
    }

    private void ForgetAll(IPacketSink connection)
    {
        _removed.Clear();
        _removed.AddRange(_known.Keys.Where(entityId => entityId != _self));
        _known.Clear();
        SendRemovals(connection);
        _freeHandles.Clear();
        _nextHandle = 0;
        _goneDrops.Clear();
        _goneDrops.AddRange(_drops.Keys);
        _drops.Clear();
        SendDropRemovals(connection);
    }

    private void SendRemovals(IPacketSink connection)
    {
        for (var offset = 0; offset < _removed.Count; offset += RemoveObjectList.MaxEntityIds)
        {
            var count = Math.Min(RemoveObjectList.MaxEntityIds, _removed.Count - offset);
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_removed).Slice(offset, count);
            connection.Send(_scratch.AsSpan(0, RemoveObjectList.Write(_scratch, span)));
        }

        _removed.Clear();
    }

    // The events of the entities this receiver knows, collected from the frame's routes instead of scanning every event of
    // the field. False when scanning is cheaper (few events) or the snapshot's events are not the frame's own (merged
    // copies for a receiver that fell behind).
    private bool RouteEvents(in WorldSnapshot snapshot)
    {
        if (snapshot.Frame?.Routes is not { } routes || snapshot.EventSegments is { Length: > 0 }
            || routes.EventCount <= (_known.Count + 1) * RouteThreshold
            || !routes.Indexes(snapshot.Events, snapshot.SkillEvents, snapshot.StatusEvents, snapshot.ItemEffects))
        {
            return false;
        }

        _routes.Clear();
        routes.Collect(_self, _routes);
        foreach (var entityId in _known.Keys)
        {
            if (entityId != _self)
            {
                routes.Collect(entityId, _routes);
            }
        }

        // Sorted by kind, then by index: each kind keeps the field's event order, and an event naming two known entities
        // appears twice in a row.
        _routes.Sort();
        _routedCombat.Clear();
        _routedSkills.Clear();
        _routedStatuses.Clear();
        _routedItems.Clear();
        var previous = -1L;
        foreach (var route in _routes)
        {
            if (route == previous)
            {
                continue;
            }

            previous = route;
            var index = (int)(uint)route;
            switch ((EventRouteKind)(route >> 32))
            {
                case EventRouteKind.Combat:
                    _routedCombat.Add(index);
                    break;
                case EventRouteKind.Skill:
                    _routedSkills.Add(index);
                    break;
                case EventRouteKind.Status:
                    _routedStatuses.Add(index);
                    break;
                case EventRouteKind.Item:
                    _routedItems.Add(index);
                    break;
            }
        }

        return true;
    }

    private void SendEvents(ReadOnlySpan<CombatEventView> combat, IPacketSink connection, List<int>? routed = null)
    {
        _events.Clear();
        if (routed is not null)
        {
            foreach (var index in routed)
            {
                _events.Add(WireMapping.Event(combat[index]));
            }
        }
        else
        {
            foreach (var combatEvent in combat)
            {
                if (Relevant(combatEvent.AttackerId, combatEvent.TargetId))
                {
                    _events.Add(WireMapping.Event(combatEvent));
                }
            }
        }

        if (_events.Count > 0)
        {
            SendClock(connection);
        }

        var events = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_events);
        while (!events.IsEmpty)
        {
            connection.Send(_scratch.AsSpan(0, CombatEventList.Write(_scratch, events, this, out var written)));
            events = events[written..];
        }
    }

    // Events name the entities this receiver knows by their presence handles (called under the gate, while encoding).
    bool IEventHandleWriter.TryGetHandle(ulong entityId, out uint handle)
    {
        var known = _known.TryGetValue(entityId, out var entry);
        handle = known ? entry.Handle : 0;
        return known;
    }

    private bool Relevant(ulong first, ulong second) =>
        first == _self || second == _self || _known.ContainsKey(first) || second != 0 && _known.ContainsKey(second);

    private void SendSkillEvents(ReadOnlySpan<SkillEventView> skills, IPacketSink connection, List<int>? routed = null)
    {
        _skillEvents.Clear();
        if (routed is not null)
        {
            foreach (var index in routed)
            {
                _skillEvents.Add(WireMapping.SkillEvent(skills[index]));
            }
        }
        else
        {
            foreach (var skillEvent in skills)
            {
                if (Relevant(skillEvent.CasterId, skillEvent.TargetId))
                {
                    _skillEvents.Add(WireMapping.SkillEvent(skillEvent));
                }
            }
        }

        if (_skillEvents.Count == 0)
        {
            return;
        }

        SendClock(connection);
        var events = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_skillEvents);
        while (!events.IsEmpty)
        {
            connection.Send(_scratch.AsSpan(0, SkillEventList.Write(_scratch, events, this, out var written)));
            events = events[written..];
        }
    }

    private void SendStatusEvents(ReadOnlySpan<StatusEventView> statuses, in WorldSnapshot snapshot, IPacketSink connection, bool withSpawned,
        List<int>? routed = null)
    {
        _statusEvents.Clear();
        if (routed is not null)
        {
            foreach (var index in routed)
            {
                _statusEvents.Add(WireMapping.Status(statuses[index]));
            }
        }
        else
        {
            foreach (var status in statuses)
            {
                if (Relevant(status.EntityId, 0))
                {
                    _statusEvents.Add(WireMapping.Status(status));
                }
            }
        }

        if (withSpawned)
        {
            foreach (var entity in _spawned)
            {
                foreach (var running in snapshot.ActiveStatuses.For(entity))
                {
                    if (running.RemainingAt(snapshot.FieldNowMs) > 0 && !PublishedWith(snapshot.StatusEvents, running.Status))
                    {
                        _statusEvents.Add(WireMapping.Status(running.At(snapshot.FieldNowMs)));
                    }
                }
            }
        }

        if (_statusEvents.Count == 0)
        {
            return;
        }

        SendClock(connection);
        var entries = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_statusEvents);
        while (!entries.IsEmpty)
        {
            connection.Send(_scratch.AsSpan(0, StatusEffectList.Write(_scratch, entries, this, out var written)));
            entries = entries[written..];
        }
    }

    // A status that changed in the snapshot itself already reaches the receiver through the snapshot's own events.
    private static bool PublishedWith(StatusEventView[] events, in StatusEventView status)
    {
        foreach (var published in events)
        {
            if (published.EntityId == status.EntityId && published.StatusCode == status.StatusCode && published.SourceId == status.SourceId)
            {
                return true;
            }
        }

        return false;
    }

    private void SendCooldowns(in WorldSnapshot snapshot, IPacketSink connection)
    {
        if (snapshot.Cooldowns is not { } cooldowns)
        {
            return;
        }

        var count = Math.Min(cooldowns.Length, SkillCooldownsPacket.MaxEntries);
        var entries = new SkillCooldownEntry[count];
        for (var index = 0; index < count; index++)
        {
            var cooldown = cooldowns[index];
            entries[index] = new SkillCooldownEntry(cooldown.SkillCode, (uint)Math.Max(0, cooldown.RemainingMs), (uint)Math.Max(0, cooldown.DurationMs));
        }

        connection.Send(_scratch.AsSpan(0, new SkillCooldownsPacket(_clockTick, entries).Write(_scratch)));
    }

    private void SendDrops(in WorldSnapshot snapshot, Vector3 selfPosition, IPacketSink connection)
    {
        _visibleDrops.Clear();
        _newDrops.Clear();
        foreach (var drop in snapshot.GroundDrops)
        {
            if (!drop.Public && drop.OwnerId != _self || Planar.DistanceSquared(drop.View.Position, selfPosition) > ItemRadius * ItemRadius)
            {
                continue;
            }

            _visibleDrops.Add(drop.View.Id);
            if (_drops.TryAdd(drop.View.Id, drop.View))
            {
                _newDrops.Add(WireMapping.Drop(drop.View));
            }
        }

        var added = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_newDrops);
        for (var offset = 0; offset < added.Length; offset += GroundItemList.MaxItems)
        {
            var batch = added.Slice(offset, Math.Min(GroundItemList.MaxItems, added.Length - offset));
            connection.Send(_scratch.AsSpan(0, GroundItemList.Write(_scratch, batch)));
        }

        _goneDrops.Clear();
        foreach (var id in _drops.Keys)
        {
            if (!_visibleDrops.Contains(id))
            {
                _goneDrops.Add(id);
            }
        }

        foreach (var id in _goneDrops)
        {
            _drops.Remove(id);
        }

        SendDropRemovals(connection);
    }

    private void SendDropRemovals(IPacketSink connection)
    {
        var gone = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_goneDrops);
        for (var offset = 0; offset < gone.Length; offset += GroundItemRemovals.MaxItems)
        {
            var batch = gone.Slice(offset, Math.Min(GroundItemRemovals.MaxItems, gone.Length - offset));
            connection.Send(_scratch.AsSpan(0, GroundItemRemovals.Write(_scratch, batch)));
        }

        _goneDrops.Clear();
    }

    private void SendOwner(OwnerState? owner, IPacketSink connection)
    {
        if (owner is null || ReferenceEquals(owner, _owner))
        {
            return;
        }

        _owner = owner;
        connection.Send(_scratch.AsSpan(0, ItemWireMapping.Status(owner).Write(_scratch)));
        connection.Send(_scratch.AsSpan(0, WireMapping.Status(owner).Write(_scratch)));
        if (owner.InventoryRevision != _inventoryRevision)
        {
            _inventoryRevision = owner.InventoryRevision;
            ItemWireMapping.SendDetails(owner, connection, _scratch);
            var parts = ItemWireMapping.Parts(owner.Items.Select(WireMapping.Item).ToArray(), InventoryPartPacket.MaxItemsPerPart);
            for (var part = 0; part < parts.Length; part++)
            {
                var packet = new InventoryPartPacket(owner.InventoryRevision, part, parts.Length, parts[part]);
                connection.Send(_scratch.AsSpan(0, packet.Write(_scratch)));
            }
        }

        if (owner.AgingRevision != _agingRevision)
        {
            _agingRevision = owner.AgingRevision;
            var entries = owner.Aging.Take(AgingProgressPacket.MaxEntries)
                .Select(static view => new AgingProgressEntry(view.InstanceId, view.Count, view.Goal)).ToArray();
            connection.Send(_scratch.AsSpan(0, new AgingProgressPacket(entries).Write(_scratch)));
        }

        if (owner.SkillsRevision != _skillsRevision)
        {
            _skillsRevision = owner.SkillsRevision;
            var skills = owner.Skills.Take(SkillsPacket.MaxSkills).Select(skill => (skill.Code, skill.Rank, skill.Uses)).ToArray();
            connection.Send(_scratch.AsSpan(0, new SkillsPacket(owner.SkillsRevision, skills).Write(_scratch)));
        }
    }

    // Places the events of a tick on the server timeline when no entries message of that tick carried its clock (the
    // tick had no entries for this receiver, or it is an earlier tick of a reader that fell behind).
    private void SendClock(IPacketSink connection)
    {
        if (!_clockPending)
        {
            return;
        }

        _clockPending = false;
        var length = new PresenceClockPacket(_clockTick, TickIntervalMs).Write(_scratch);
        connection.Send(_scratch.AsSpan(0, length));
    }

    private static int DescriptorHash(in EntityView view)
    {
        var appearance = view.Appearance;
        return HashCode.Combine(view.MaxLife, view.Level, view.TemplateId, view.MoveSpeed, view.AttackRate,
            HashCode.Combine(appearance.WeaponKey, appearance.ArmorKey, appearance.OffHandKey, appearance.InVillage, appearance.Tier, appearance.HairIndex,
                HashCode.Combine(appearance.HelperPet, appearance.Phoenix, appearance.BigHead, appearance.WeaponGlow, appearance.OffHandGlow, appearance.ShopSign,
                    appearance.ClanId, HashCode.Combine(appearance.ClanName, appearance.ClanMark))),
            view.OwnerId,
            view.ModelScale);
    }

    // What this receiver last got about an entity, only the fields its diff compares (a crowd keeps one per pair); Pass
    // marks the entities seen in the snapshot being encoded.
    private struct Known
    {
        public Vector3 Position;
        public PresencePoint Point;
        public float Facing;
        public int Life;
        public int MaxLife;
        public ulong AttackTargetId;
        public ulong AttackSequence;
        public uint Template;
        public int FieldCode;
        public uint LastSentTick;
        public uint Pass;
        public uint Handle;
        public int Descriptor;
        public MotionState MoveState;
        public EntityKind Kind;
        public byte Revision;

        public Known(in EntityView view, byte revision, int descriptor, uint pass, uint handle)
        {
            Kind = view.Kind;
            Revision = revision;
            Descriptor = descriptor;
            Pass = pass;
            Handle = handle;
        }

        // The values of the entry (other than the description revision) that differ from what was last sent.
        public readonly PresenceFields Changed(in EntityView view, int field) =>
            (Life != view.Life ? PresenceFields.Life : 0) | (MaxLife != view.MaxLife ? PresenceFields.MaxLife : 0)
            | (AttackTargetId != view.AttackTargetId ? PresenceFields.Target : 0)
            | (AttackSequence != view.AttackSequence ? PresenceFields.AttackSequence : 0)
            | (Template != WireMapping.SharedTemplate(view) ? PresenceFields.Template : 0) | (FieldCode != field ? PresenceFields.FieldCode : 0);

        public void Sent(in EntityView view, uint tick, int field, PresencePoint point)
        {
            Position = view.Position;
            Point = point;
            Facing = view.Facing;
            Life = view.Life;
            MaxLife = view.MaxLife;
            AttackTargetId = view.AttackTargetId;
            AttackSequence = view.AttackSequence;
            Template = WireMapping.SharedTemplate(view);
            FieldCode = field;
            MoveState = view.MoveState;
            LastSentTick = tick;
        }
    }
}
