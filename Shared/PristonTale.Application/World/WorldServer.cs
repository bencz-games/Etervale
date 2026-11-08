using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.GameLogs;
using PristonTale.Contracts.World;
using PristonTale.Application.Saves;
using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Texts;

using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    public const int TickHz = ServerTick.Hz;

    public const int CommandQueueCapacity = 65536;

    private readonly Channel<WorldCommand> _commands = Channel.CreateBounded<WorldCommand>(new BoundedChannelOptions(CommandQueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    // Movement reports arrive at about 5 Hz per moving player; the budget bounds one tick's command phase.
    private const int CommandsPerTick = 16384;
    private readonly List<FieldInstance> _activeFields = new();
    // The tick's inputs every field steps with, and the step itself made once, so the tick's parallel pass allocates no
    // closure.
    private FieldStepInputs _stepInputs;
    private Action<FieldInstance>? _stepField;
    private int _playerCount;
    private bool _membershipChanged;
    private readonly Dictionary<int, FieldInstance> _fields = new();
    private readonly Dictionary<ulong, Connection> _connections = new();
    private readonly EntityIdAllocator _ids = new();
    private readonly WorldDataHolder _worldData;
    private readonly WorldDataLanguagesHolder _localizedData;
    private readonly WorldCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly long _clockOrigin;
    private readonly BossSpawnSchedule _bossSchedule;
    private readonly FieldTerrainCatalog _terrain;
    private WorldServerOptions _options;
    private readonly ICharacterSaves? _saves;
    private readonly IGameLog? _log;
    // Commands each player has waiting in the shared queue; entries are dropped when the player leaves the world.
    private readonly ConcurrentDictionary<ulong, PendingCommands> _pendingByPlayer = new();

    // A player's commands waiting in the queue, and apart from them their movement reports: a burst of reports a held
    // session sends at once must never fill the quota of the commands behind it.
    private sealed class PendingCommands
    {
        public int Count;
        public int Movements;
    }

    private sealed record Connection(
        int FieldId,
        WorldPlayer Player,
        ChannelWriter<WorldSnapshot> Outbound,
        PlayerSession? Session)
    {
        public OwnerState? LastOwner { get; set; }

        public long CooldownRevision { get; set; } = -1;

        // The tick the next starter gift is due on, and whether the gift service holds a request for it.
        public uint NextGiftTick { get; set; }
        public bool GiftRequested { get; set; }
    }

    public WorldServer(
        WorldDataHolder worldData,
        WorldCatalog? catalog = null,
        TimeProvider? clock = null,
        ISimRng? bossRng = null,
        FieldTerrainCatalog? terrain = null,
        WorldServerOptions? options = null,
        ICharacterSaves? saves = null,
        IGameLog? log = null,
        WorldDataLanguagesHolder? localizedData = null)
    {
        _saves = saves;
        _log = log;
        _localizedData = localizedData ?? new WorldDataLanguagesHolder();
        _options = (options ?? WorldServerOptions.Default).Validated();
        _worldData = worldData;
        _terrain = terrain ?? new FieldTerrainCatalog();
        _catalog = catalog ?? new WorldCatalog();
        _clock = clock ?? TimeProvider.System;
        _clockOrigin = _clock.GetTimestamp();
        _bossSchedule = new BossSpawnSchedule(_clock.GetLocalNow(), bossRng ?? new CombatRng(0xB055));
    }

    private FieldInstance GetField(int fieldId)
    {
        if (_fields.TryGetValue(fieldId, out var field))
        {
            return field;
        }

        var data = _worldData.Current;
        var spawnData = data.FieldSpawns.TryGetValue(fieldId, out var spawns) ? spawns : FieldSpawnData.Empty;
        field = new FieldInstance(
            fieldId,
            spawnData,
            data.Monsters,
            _ids,
            new CombatRng(fieldId),
            data.Motions,
            data.Npcs,
            experienceMultiplier: _options.ExperienceMultiplier,
            items: data.Items,
            terrain: _terrain.Get(fieldId));
        ConfigureQuests(field);
        field.State = _catalog.TryGetFieldByCode(fieldId, out var definition) ? definition.State : FieldState.Forest;
        ApplyOptions(field, _options, CastleTax);
        field.Event = _catalog.TryGetFieldByCode(fieldId, out var described) ? described.FieldEvent : FieldEventKind.None;
        field.Rains = described is not null && WeatherSchedule.Rains(described);
        field.DropLedger = DropLedgerFor(_worldData.Current.Items);
        _fields.Add(fieldId, field);
        OpenSiegeIn(field);
        if (fieldId == Simulation.World.Bellatra.BellatraArena.Field)
        {
            field.Bellatra = _bellatra;
        }

        return field;
    }

    public WorldServerOptions Options => Volatile.Read(ref _options);

    public Task ApplyOptionsAsync(WorldServerOptions options)
    {
        var validated = options.Validated();
        return EnqueueAsync(NoOwner, () =>
        {
            Volatile.Write(ref _options, validated);
            foreach (var field in _fields.Values)
            {
                ApplyOptions(field, validated, CastleTax);
            }

            return true;
        }, waitForCapacity: true);
    }

    private static void ApplyOptions(FieldInstance field, WorldServerOptions options, int tax)
    {
        field.ItemEventRates = options.ItemEvents;
        field.ExperienceMultiplier = options.ExperienceMultiplier;
        field.ExperienceEventPercent = options.ExperienceEventPercent;
        field.CastleTax = tax;
        field.BellatraScoreDivisor = options.Bellatra.ScoreDivisor;
        field.Aging = options.Aging with { Tax = tax };
        field.Mixing = options.Mixing with { Tax = tax };
        field.PlayersFight = field.State == FieldState.Castle && !options.CastleLeague;
    }

    // The game clock in game milliseconds: the Unix time plus the configured offset (World:ClockOffsetMinutes game minutes).
    public long GameClockMs => _clock.GetUtcNow().ToUnixTimeMilliseconds() + _options.ClockOffsetMinutes * (long)WorldClock.MinuteMs;

    // The weather of the fields that take rain: a game master's (/weather) while one is set, else the schedule on the
    // game clock while World:Weather is on, else clear.
    private WeatherState Weather => _weatherOverride
        ?? (_options.Weather ? WeatherSchedule.At(GameClockMs, _options.WeatherCycleMinutes) : WeatherState.Clear);

    private WeatherState? _weatherOverride;

    // A game master's weather for every field, or null to follow the schedule again; the weather then.
    public Task<WeatherState> SetWeatherAsync(ulong issuerId, WeatherState? weather) => EnqueueAsync(issuerId, () =>
    {
        _weatherOverride = weather;
        return Weather;
    });

    public Task<WeatherState> WeatherNowAsync(ulong issuerId) => EnqueueAsync(issuerId, () => Weather);

    private DailyDropLedger? _dropLedger;

    // One ledger per item catalog: a reloaded world data starts its own count.
    private DailyDropLedger DropLedgerFor(ItemCatalog items) =>
        _dropLedger is { } ledger && ReferenceEquals(_dropLedgerItems, items) ? ledger : (_dropLedger = new DailyDropLedger(_dropLedgerItems = items, _clock));

    private ItemCatalog? _dropLedgerItems;

    public Task<bool> JoinAsync(int fieldId, WorldPlayer player, ChannelWriter<WorldSnapshot> outbound, PlayerSession? session = null)
    {
        return EnqueueAsync(player.EntityId, () =>
        {
            if (!_catalog.TryGetFieldByCode(fieldId, out var definition)
                || _connections.ContainsKey(player.EntityId)
                || session is not null && !session.IsPlaying)
            {
                return false;
            }

            var field = GetField(fieldId);
            player.Character.MeetHelpPet(field.Rng);
            // A saved place without a floor (outside the stage) enters at the field's first start point instead.
            if (!field.Terrain.TryPlaceOnFloor(player.Position, out var position))
            {
                var start = definition.Arrival;
                position = field.Terrain.PlaceOnFloor(new Vector3(start.X, 0f, start.Z));
            }

            player.EnterField(position, definition.State == FieldState.Village);
            player.Guard(PlayerGrace.ArrivalMs);
            field.Add(player);
            _connections.Add(player.EntityId, new Connection(fieldId, player, outbound, session) { NextGiftTick = GiftTick(StarterGifts.FirstDelay) });
            _membershipChanged = true;
            UpdatePlacement(session, fieldId, player.Position, definition.State == FieldState.Village);
            return true;
        });
    }

    // Leaving must not be refused: it waits for queue room and is exempt from the player's quota.
    // The character is captured in the same tick it leaves, so a player who leaves dead is stored at the town restart.
    public Task<WorldLeave> LeaveAsync(int fieldId, ulong entityId, ChannelWriter<WorldSnapshot> outbound)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || !ReferenceEquals(connection.Outbound, outbound))
            {
                return new WorldLeave(false, null);
            }

            var save = connection.Session is { } session ? CaptureLeaving(session, connection) : null;
            LeaveParty(connection.Player);
            LeaveTrade(connection.Player);

            _fields[connection.FieldId].Remove(entityId);
            _connections.Remove(entityId);
            _pendingByPlayer.TryRemove(entityId, out _);
            _membershipChanged = true;
            return new WorldLeave(true, save, TakeNotices());
        }, waitForCapacity: true);
    }

    public Task<bool> MoveToFieldAsync(
        int oldFieldId,
        int newFieldId,
        WorldPlayer player,
        Vector3 requestedPosition,
        ChannelWriter<WorldSnapshot> outbound)
    {
        return EnqueueAsync(player.EntityId, () =>
        {
            // Reports keep naming the new field until the client hears the move committed: an already-made move is a success,
            // not a refusal the session would answer with a forced warp.
            if (_connections.TryGetValue(player.EntityId, out var current) && ReferenceEquals(current.Outbound, outbound)
                && current.FieldId == newFieldId && oldFieldId != newFieldId)
            {
                return true;
            }

            if (!WorldPosition.IsValid(requestedPosition)
                || player.IsDead
                || !_connections.TryGetValue(player.EntityId, out var connection)
                || connection.FieldId != oldFieldId
                || !ReferenceEquals(connection.Player, player)
                || !ReferenceEquals(connection.Outbound, outbound)
                || !_catalog.TryGetFieldByCode(oldFieldId, out var source)
                || !_catalog.TryGetFieldByCode(newFieldId, out var destination)
                || !FieldTransition.TryResolve(source, destination, player.Position, requestedPosition, player.Level, out var arrival))
            {
                return false;
            }

            // The request's placement never becomes authoritative. Gates retain position; warps use catalog exits.
            var inVillage = destination.State == FieldState.Village;
            var terrain = GetField(newFieldId).Terrain;
            Relocate(connection, newFieldId, arrival.Position, inVillage);
            if (arrival.IsWarp)
            {
                // Warp exits carry an authored height; settle on the floor at or just below it rather than on a roof.
                player.EnterField(terrain.SettleNear(arrival.Position, FieldTerrain.ArrivalTolerance), inVillage);
                player.Guard(PlayerGrace.ArrivalMs);
            }
            else
            {
                player.CrossGate(inVillage);
            }

            return true;
        });
    }

    public Task<MovementVerdict> ReportMovementAsync(int fieldId, ulong entityId, Vector3 position, float facing, bool moving, bool running, ulong actionTarget)
    {
        return EnqueueAsync(entityId, () =>
        {
            if (_connections.TryGetValue(entityId, out var connection) && connection.FieldId == fieldId
                && _fields.TryGetValue(fieldId, out var field))
            {
                return connection.Player.ReportMovement(position, facing, moving, running, actionTarget, field.NowMs, field.Terrain);
            }

            return MovementVerdict.Ignored;
        });
    }

    public Task<SkillCastOutcome> CastSkillAsync(int fieldId, ulong entityId, SkillCastRequest request)
    {
        return EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || connection.FieldId != fieldId
                || !_fields.TryGetValue(fieldId, out var field))
            {
                return SkillCastOutcome.Refused(request, SkillCastVerdict.Stale);
            }

            return field.CastSkill(connection.Player, request);
        });
    }

    public Task<Vector3?> PositionOfAsync(ulong entityId)
    {
        return EnqueueAsync<Vector3?>(entityId, () => _connections.TryGetValue(entityId, out var connection) ? connection.Player.Position : null);
    }

    public Task<bool> SetAttackTargetAsync(int fieldId, ulong attackerEntityId, ulong targetEntityId, bool sustained, ulong sequence = 0)
    {
        return EnqueueAsync(attackerEntityId, () =>
        {
            return _fields.TryGetValue(fieldId, out var field) && field.SetAttackTarget(attackerEntityId, targetEntityId, sustained, sequence);
        });
    }

    // Space with nothing ahead in an action field (FieldState.Action) swings at nothing; elsewhere it does nothing.
    public Task<bool> SwingAtNothingAsync(int fieldId, ulong entityId) =>
        EnqueueAsync(entityId, () => _catalog.TryGetFieldByCode(fieldId, out var definition) && definition.State == FieldState.Action
            && _fields.TryGetValue(fieldId, out var field) && field.SwingAtNothing(entityId));

    public Task<CharacterSaveSnapshot?> CaptureForSaveAsync(PlayerSession session)
    {
        return EnqueueAsync(NoOwner, () => CaptureForSave(session, _connections.GetValueOrDefault(session.EntityId)), waitForCapacity: true);
    }

    private CharacterSaveSnapshot? CaptureForSave(PlayerSession session, Connection? connection)
    {
        if (session.Character is null || session.Loaded is null)
        {
            return null;
        }

        if (connection is not null)
        {
            if (connection.Player.IsDead && CaptureDead(session, connection.Player.Character, connection.FieldId, connection.Player.Position) is { } dead)
            {
                return dead;
            }

            UpdatePlacement(session, connection.FieldId, connection.Player.Position);
        }

        return CharacterRecordFactory.Capture(session.Loaded, session.Character, session.FieldId, session.SpawnPosition);
    }

    // A durable operation still holding the save gate commits after this tick, possibly on another character too (a
    // personal shop sale): the leave save then waits for it and captures the character it leaves (no snapshot here), a
    // dead one through its town restart.
    private CharacterSaveSnapshot? CaptureLeaving(PlayerSession session, Connection connection)
    {
        if (session.SaveGate.CurrentCount > 0)
        {
            return CaptureForSave(session, connection);
        }

        UpdatePlacement(session, connection.FieldId, connection.Player.Position);
        session.LeftDead = connection.Player.IsDead;
        return null;
    }

    // Each playing character is captured once per CheckpointSeconds, on the tick its entity id falls on, unless an
    // operation holds its save gate (that operation queues its own capture).
    private void Checkpoint(uint serverTick)
    {
        if (_saves is null)
        {
            return;
        }

        var period = (ulong)_options.CheckpointSeconds * TickHz;
        foreach (var connection in _connections.Values)
        {
            if (connection.Session is { IsPlaying: true } session && (serverTick + connection.Player.EntityId) % period == 0
                && session.SaveGate.CurrentCount > 0 && CaptureForSave(session, connection) is { } snapshot)
            {
                _saves.Enqueue(session, snapshot);
            }
        }
    }

    // The save of a character that left dead while an operation held its gate, once that operation is done.
    public Task<CharacterSaveSnapshot?> CaptureLeftAsync(PlayerSession session)
    {
        return EnqueueAsync(NoOwner, () => session.Character is null || session.Loaded is null
            ? null
            : CaptureDead(session, session.Character, session.FieldId, session.SpawnPosition)
                ?? CharacterRecordFactory.Capture(session.Loaded, session.Character, session.FieldId, session.SpawnPosition), waitForCapacity: true);
    }

    public int PlayerCount => Volatile.Read(ref _playerCount);

    // Tick-thread state: callers must not run concurrently with Tick.
    // Tick only. The session's connection while the session owns it, which a commit or an abort still needs after the
    // player started leaving.
    private bool TryGetOwnConnection(PlayerSession session, out Connection connection) =>
        _connections.TryGetValue(session.EntityId, out connection!) && ReferenceEquals(connection.Session, session);

    // Tick only. The session's connection while its character plays in the world.
    private bool TryGetPlaying(PlayerSession session, out Connection connection) =>
        TryGetOwnConnection(session, out connection) && session.IsPlaying && session.Character is not null;

    internal long FieldNowMs(int fieldId) => _fields.TryGetValue(fieldId, out var field) ? field.NowMs : 0;

    internal FieldInstance? FieldFor(int fieldId) => _fields.GetValueOrDefault(fieldId);

    public Task<PreparedProgressionOperation?> PrepareProgressionAsync(
        PlayerSession session, ProgressionCommand command)
    {
        return EnqueueAsync<PreparedProgressionOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            return ProgressionTransactions.Prepare(session, connection.Player, _fields[connection.FieldId], command);
        });
    }

    public Task<bool> CommitProgressionAsync(PlayerSession session,
        PreparedProgressionOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var moneyBefore = operation.Original.Money;
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null
                || !operation.Original.ApplyProgression(operation.Candidate.ProgressionOperations[^1].Command, operation.Tax))
            {
                throw new CommitInvariantException("Character ownership changed during progression commit.");
            }

            var spent = moneyBefore - operation.Original.Money;
            RecordSpending(GameMasters.CurrencySink.SkillTraining, spent, CastleRules.TaxIn(spent, operation.Tax));

            return true;
        }, waitForCapacity: true);
    }

    public Task<PreparedCharacterOperation<InventoryOutcome>?> PrepareInventoryAsync(PlayerSession session, InventoryCommand command)
    {
        return EnqueueAsync<PreparedCharacterOperation<InventoryOutcome>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var original = connection.Player.Character;
            if (original.InventoryOperations.Find(command.OperationId) is { } applied)
            {
                var duplicate = applied.Command == command;
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(duplicate, applied.Revision, GameText.Of(duplicate ? GameTextId.AlreadyApplied : GameTextId.OperationConflict)));
            }

            if (connection.Player.Shop is not null && command.Action != InventoryAction.ConsumePotion)
            {
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(false, original.InventoryRevision, GameText.Of(GameTextId.CloseShopFirst)));
            }

            if (connection.Player.Offers(command.ItemId))
            {
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(false, original.InventoryRevision, GameText.Of(GameTextId.ItemInTrade)));
            }

            if (command.Action == InventoryAction.Pickup)
            {
                return GroundPickupTransactions.Prepare(session, connection.Player, _fields[connection.FieldId], command, GoldSharers(connection));
            }

            if (command.Action is InventoryAction.Drop or InventoryAction.DropGold)
            {
                return GroundThrowTransactions.Prepare(session, connection.Player, _fields[connection.FieldId], command);
            }

            if (command.Action == InventoryAction.ConsumePotion && (connection.Player.IsDead || connection.Player.HasPendingPotion))
            {
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(false, original.InventoryRevision, GameText.Of(GameTextId.PotionNotNow)));
            }

            var candidate = original.CloneForInventoryOperation();
            if (!InventoryCommands.Apply(candidate, command))
            {
                return new PreparedCharacterOperation<InventoryOutcome>(original, null, null,
                    new InventoryOutcome(false, original.InventoryRevision,
                        InventoryCommands.Refusal(original, command) is { IsNone: false } refusal ? refusal : GameText.Of(GameTextId.StaleInventory)));
            }

            candidate.RecordInventoryOperation(command);
            var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, connection.Player.Position);
            var outcome = new InventoryOutcome(true, candidate.InventoryRevision, GameText.None);
            if (command.Action == InventoryAction.ConsumePotion)
            {
                return new PreparedPotionOperation(original, candidate, snapshot, outcome, original.FindOwnedItem(command.ItemId)!.Definition.Potion!);
            }

            return new PreparedCharacterOperation<InventoryOutcome>(original, candidate, snapshot, outcome);
        });
    }

    public Task<bool> CommitInventoryAsync(PlayerSession session, PreparedCharacterOperation<InventoryOutcome> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            // SaveGate remains held through commit, including when the connection leaves during storage I/O.
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null)
            {
                throw new CommitInvariantException("Character ownership changed during inventory commit.");
            }

            // A pickup of what a character threw is a transfer from that character; a monster's drop is not logged.
            var picking = operation as PreparedPickupOperation;
            var thrower = picking?.Ground.ThrownBy(picking.DropId) ?? FieldGroundLoot.NoThrower;
            var pickedUp = thrower != FieldGroundLoot.NoThrower ? Transfer(ItemTransferKind.GroundPickup, picking!.DropId, session.FieldId) : null;
            pickedUp?.Arrived(operation.Original, operation.Candidate, thrower, CharacterIdOf(session));
            operation.Original.CommitInventoryOperation(operation.Candidate);
            if (operation is PreparedPickupOperation pickup)
            {
                pickedUp?.Gold(thrower, CharacterIdOf(session), pickup.Gold);
                if (!operation.Original.TryChangeMoney(pickup.Gold))
                {
                    throw new CommitInvariantException("Money changed beyond capacity during pickup commit.");
                }

                // A member who left or reloaded since the pickup was prepared, or who can no longer take its part, gets nothing.
                foreach (var share in pickup.Shares)
                {
                    if (_connections.TryGetValue(share.MemberId, out var member) && ReferenceEquals(member.Player.Character, share.Character)
                        && TakesGoldShare(member) && MoneyLimits.CanCarry(share.Character, share.Gold))
                    {
                        share.Character.TryChangeMoney(share.Gold);
                        pickedUp?.Gold(thrower, CharacterIdOf(member.Session), share.Gold);
                    }
                }

                pickup.Ground.Resolve(pickup.DropId, pickup.OperationId, committed: true);
                Log(pickedUp);
            }
            if (operation is PreparedThrowOperation thrown)
            {
                if (!operation.Original.TryChangeMoney(-thrown.Gold))
                {
                    throw new CommitInvariantException("Money changed beyond capacity during a gold throw commit.");
                }

                var dropId = thrown.Field.GroundLoot.PlaceThrown(thrown.Item, thrown.Gold, thrown.Position, thrown.ThrowerId, thrown.Field.NowMs,
                    CharacterIdOf(session) ?? FieldGroundLoot.NoThrower);
                if (Transfer(ItemTransferKind.GroundThrow, dropId, thrown.Field.FieldId) is { } throwLog)
                {
                    if (thrown.Item is { } item)
                    {
                        throwLog.Item(CharacterIdOf(session), null, null, item, item.Count);
                    }

                    throwLog.Gold(CharacterIdOf(session), null, thrown.Gold);
                    Log(throwLog);
                }
            }

            if (operation is PreparedPotionOperation potion && TryGetOwnConnection(session, out var connection))
            {
                connection.Player.QueuePotion(potion.Potion);
                // A seller may drink the potions it offers.
                connection.Player.PruneShop();
            }

            return true;
        }, waitForCapacity: true);
    }

    // The party members who split a pickup's gold with the picker: in its field and share range.
    private List<WorldPlayer> GoldSharers(Connection picker)
    {
        var sharers = new List<WorldPlayer>();
        if (picker.Player.Party is not { } party)
        {
            return sharers;
        }

        foreach (var id in party.Members)
        {
            if (id != picker.Player.EntityId && _connections.TryGetValue(id, out var member) && member.FieldId == picker.FieldId
                && Simulation.Parties.PartyRules.InShareRange(picker.Player.Position, member.Player.Position) && TakesGoldShare(member))
            {
                sharers.Add(member.Player);
            }
        }

        return sharers;
    }

    // A member whose save gate is held is left out: its durable operation (a trade's receipt, a sale) checked the gold it
    // may carry before the share, and its commit must not land past that limit.
    private static bool TakesGoldShare(Connection member) => member.Session?.SaveGate.CurrentCount != 0;

    private const ulong NoOwner = 0;

    // The rows of a transfer committed now; null when no game log is attached.
    private TransferLog? Transfer(ItemTransferKind kind, Guid transferId, int fieldId) =>
        _log is null ? null : new TransferLog(kind, transferId.ToString(), fieldId, _clock.GetUtcNow().UtcDateTime);

    private void Log(TransferLog? transfer)
    {
        if (transfer is { Rows.Count: > 0 })
        {
            _log!.ItemTransfers(transfer.Rows);
        }
    }

    private static long? CharacterIdOf(PlayerSession? session) => session?.Loaded?.Id;

    public int PendingCommandsOf(ulong entityId) => _pendingByPlayer.TryGetValue(entityId, out var pending) ? Volatile.Read(ref pending.Count) : 0;

    // Not async: a refused command throws WorldBusyException to the caller synchronously instead of faulting the task.
    private Task<T> EnqueueAsync<T>(ulong owner, Func<T> operation, bool waitForCapacity = false)
    {
        PendingCommands? pending = null;
        if (owner != NoOwner)
        {
            pending = _pendingByPlayer.GetOrAdd(owner, static _ => new PendingCommands());
            if (Interlocked.Increment(ref pending.Count) > _options.MaxPendingCommandsPerPlayer)
            {
                Interlocked.Decrement(ref pending.Count);
                throw new WorldBusyException("The player's command quota is full.");
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action command = () =>
        {
            if (pending is not null)
            {
                Interlocked.Decrement(ref pending.Count);
            }

            try
            {
                completion.TrySetResult(operation());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        };
        if (_commands.Writer.TryWrite(new WorldCommand(command, default)))
        {
            return completion.Task;
        }

        if (!waitForCapacity)
        {
            if (pending is not null)
            {
                Interlocked.Decrement(ref pending.Count);
            }

            throw new WorldBusyException("The authoritative command queue is full.");
        }

        // Durably admitted transactions cannot be abandoned when ordinary command admission is saturated.
        return WriteThenAwaitAsync(command, completion.Task);
    }

    private async Task<T> WriteThenAwaitAsync<T>(Action command, Task<T> result)
    {
        await _commands.Writer.WriteAsync(new WorldCommand(command, default));
        return await result;
    }

    // The last tick's commands and the time its serial command phase and its fields' parallel step and publication took.
    public readonly record struct TickPhases(int Commands, double CommandsMs, double FieldsMs);

    public TickPhases LastPhases { get; private set; }

    public void Tick(float dt, uint serverTick)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        _serverTick = serverTick;
        var started = Stopwatch.GetTimestamp();
        var processed = 0;
        for (; processed < CommandsPerTick && _commands.Reader.TryRead(out var command); processed++)
        {
            if (command.Action is { } action)
            {
                action();
            }
            else
            {
                ApplyMovement(command.Movement);
            }
        }

        var commandsDone = Stopwatch.GetTimestamp();
        RefreshParties(serverTick);
        var localNow = _clock.GetLocalNow();
        bool bossPassDue = _bossSchedule.TryBeginPass(localNow, _clock.GetElapsedTime(_clockOrigin));
        int? bossHour = bossPassDue ? localNow.Hour : null;

        _activeFields.Clear();
        _activeFields.AddRange(_fields.Values);
        var nowSeconds = _clock.GetUtcNow().ToUnixTimeSeconds();
        // Fields share only immutable catalogs and the thread-safe id allocator, so they step in parallel.
        var night = GameHours.IsNight(WorldClock.HoursAt(GameClockMs));
        var weather = Weather;
        _stepInputs = new FieldStepInputs(dt, serverTick, bossHour, nowSeconds, night, weather);
        Parallel.ForEach(_activeFields, _stepField ??= field => StepField(field, _stepInputs.Dt, _stepInputs.ServerTick, _stepInputs.BossHour,
            _stepInputs.NowSeconds, _stepInputs.Night, _stepInputs.Weather));
        LastPhases = new TickPhases(processed, Stopwatch.GetElapsedTime(started, commandsDone).TotalMilliseconds,
            Stopwatch.GetElapsedTime(commandsDone).TotalMilliseconds);
        PublishSiege(nowSeconds);
        CheerCastleOwners(UnixNowMs);
        ApplyCastleTax();
        RenewClanSkill(nowSeconds);
        StepBellatra(UnixNowMs);
        ApplyQuestTravels();
        Checkpoint(serverTick);
        OfferStarterGifts(serverTick);

        if (!_membershipChanged)
        {
            return;
        }

        _membershipChanged = false;
        Volatile.Write(ref _playerCount, _connections.Count);
    }

    private void StepField(FieldInstance field, float dt, uint serverTick, int? bossHour, long nowSeconds, bool night, WeatherState weather)
    {
        field.Premium.NowSeconds = nowSeconds;
        field.Night = night;
        field.SetWeather(weather);
        field.Step(dt, bossHour);
        if (field.PlayerCount == 0)
        {
            return;
        }

        var frame = field.Frames.Rent(field.EntityCount);
        field.FillViews(frame.Views);
        frame.Grid.Rebuild(frame.Views);
        // Open to readers before any outbox can hand it out.
        field.Frames.Published(frame);
        var snapshot = new WorldSnapshot(serverTick, frame.Views, field.SnapshotEvents(), field.FieldId, groundDrops: field.GroundLoot.Snapshot())
        {
            Grid = frame.Grid,
            Frame = frame,
            FrameGeneration = frame.Generation,
            SkillEvents = field.SnapshotSkillEvents(),
            StatusEvents = field.SnapshotStatusEvents(),
            ItemEffects = field.ItemEvents.Snapshot(),
            ActiveStatuses = field.Statuses.Snapshot,
            FieldNowMs = field.NowMs,
            Weather = field.Weather
        };
        frame.Routes.Rebuild(snapshot.Events, snapshot.SkillEvents, snapshot.StatusEvents, snapshot.ItemEffects);
        foreach (var entityId in field.PlayerIds)
        {
            if (_connections.TryGetValue(entityId, out var connection))
            {
                var owner = OwnerState.Capture(connection.Player.Character, connection.LastOwner, nowSeconds);
                connection.LastOwner = owner;
                var cooldowns = connection.Player.Cooldowns.SnapshotIfChanged(connection.CooldownRevision, field.NowMs);
                connection.CooldownRevision = connection.Player.Cooldowns.Revision;
                connection.Outbound.TryWrite(snapshot.WithOwner(owner, cooldowns) with
                {
                    Quest = connection.Player.QuestState.TakeUpdate(connection.Player.Character),
                    Restart = connection.Player.IsDead ? DeathPenalty.Offer(connection.Player.Character, field.State) : null,
                    Party = connection.Player.Party?.View,
                    PartyRequest = connection.Player.PartyRequest,
                    Trade = connection.Player.Trade?.ViewFor(entityId),
                    TradeRequest = connection.Player.TradeRequest,
                    Bellatra = BellatraViewOf(connection)
                });
                // Only an outbox pins and releases frames; any other writer may keep the snapshot forever.
                frame.Retained |= connection.Outbound is not WorldOutbox.OutboxWriter;
            }
        }
    }

    // Moves a player between field instances on the command phase: its cooldowns and running statuses follow it onto the
    // destination's clock.
    // Tick only. Puts the connection's player in fieldCode (moving it from its field when that differs) and records where
    // the session now is; the caller then stands the player at position (EnterField, CrossGate or a restart).
    private void Relocate(Connection connection, int fieldCode, Vector3 position, bool inVillage)
    {
        // A shop sells where its seller opened it, and a trade happens where it opened.
        connection.Player.CloseShop();
        LeaveTrade(connection.Player);

        if (connection.FieldId != fieldCode)
        {
            TransferPlayer(connection.Player, _fields[connection.FieldId], GetField(fieldCode));
            _connections[connection.Player.EntityId] = connection with { FieldId = fieldCode };
        }

        _membershipChanged = true;
        UpdatePlacement(connection.Session, fieldCode, position, inVillage);
    }

    private static void TransferPlayer(WorldPlayer player, FieldInstance from, FieldInstance to)
    {
        var statuses = from.Statuses.Take(player.EntityId);
        from.Remove(player.EntityId);
        player.Cooldowns.Rebase(from.NowMs, to.NowMs);
        to.Add(player);
        to.Statuses.Carry(player.EntityId, statuses, from.NowMs, to.NowMs);
        to.ArriveWithoutCrystalPets(player);
    }

    private static void UpdatePlacement(PlayerSession? session, int fieldId, Vector3 position, bool? inVillage = null)
    {
        if (session is null)
        {
            return;
        }

        session.FieldId = fieldId;
        session.SpawnPosition = position;
        if (inVillage.HasValue)
        {
            session.InVillage = inVillage.Value;
        }
    }
}

internal readonly record struct FieldStepInputs(float Dt, uint ServerTick, int? BossHour, long NowSeconds, bool Night, WeatherState Weather);
