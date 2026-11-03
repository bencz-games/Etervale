using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Loot;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Sessions;

namespace PristonTale.Tests;

// One real client over TCP (GameServerConnection and its play channel) and the world it has observed: every presence
// update is folded into a model the way the game scene applies it, so cases assert what a player would see.
internal sealed class MultiplayerClient : IAsyncDisposable
{
    private readonly GameServerConnection _connection;
    private readonly CancellationTokenSource _lifetime = new();
    private IPlayChannel? _play;
    private Task? _reader;
    private uint _sequence;

    private MultiplayerClient(GameServerConnection connection, string name)
    {
        _connection = connection;
        Name = name;
    }

    public string Name { get; }
    public ulong EntityId { get; private set; }
    public int FieldId { get; private set; }
    public Vector3 Spawn { get; private set; }
    public ObservedWorld World { get; } = new();
    public long BytesReceived => _connection.BytesReceived;

    public static async Task<MultiplayerClient> EnterAsync(MultiplayerServer server, string account, string character)
    {
        var client = await LoginAsync(server, account, character);
        var enter = await client._connection.EnterWorldAsync(character, client._lifetime.Token);
        Assert.True(enter.Ok, $"{character} could not enter the world: {enter.Message}");
        client.EntityId = enter.EntityId;
        client.FieldId = enter.FieldId;
        client.Spawn = new Vector3(enter.SpawnX, 0f, enter.SpawnZ);
        client.Open();
        return client;
    }

    public static async Task<MultiplayerClient> LoginAsync(MultiplayerServer server, string account, string character)
    {
        var connection = new GameServerConnection(server.Address);
        var client = new MultiplayerClient(connection, character);
        var login = await connection.LoginAsync(account, MultiplayerServer.Password, client._lifetime.Token);
        Assert.True(login.Ok, $"{account} could not log in: {login.Message}");
        return client;
    }

    public Task<EnterWorldOutcome> TryEnterAsync() => _connection.EnterWorldAsync(Name, _lifetime.Token);

    public void Adopt(EnterWorldOutcome enter)
    {
        EntityId = enter.EntityId;
        FieldId = enter.FieldId;
        Spawn = new Vector3(enter.SpawnX, 0f, enter.SpawnZ);
        Open();
    }

    private void Open()
    {
        _play = _connection.OpenPlayChannel(_lifetime.Token);
        _reader = ReadAsync(_play);
    }

    private IPlayChannel Play => _play ?? throw new InvalidOperationException("Enter the world first.");

    private async Task ReadAsync(IPlayChannel play)
    {
        try
        {
            await foreach (var update in play.ReadAllAsync(_lifetime.Token))
            {
                World.Apply(update);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException)
        {
            World.Close(ex);
        }
    }

    public Task MoveAsync(Vector3 position, float facing, bool moving = true, bool running = false, ulong target = 0, int? field = null) =>
        Play.SendMovementAsync(position, facing, moving, running, target, field ?? FieldId, _lifetime.Token);

    public Task StopAsync(Vector3 position, float facing) => MoveAsync(position, facing, moving: false);

    public Task AttackAsync(ulong target, bool sustained = true) => Play.SendAttackAsync(target, sustained, _lifetime.Token, ++_sequence);

    public Task<SkillCastResultInfo> CastAsync(uint skill, ulong target = 0, Vector3? point = null)
    {
        var sequence = ++_sequence;
        return CastCoreAsync(sequence, skill, target, point);
    }

    private async Task<SkillCastResultInfo> CastCoreAsync(uint sequence, uint skill, ulong target, Vector3? point)
    {
        await Play.SendSkillCastAsync(sequence, skill, target, point, _lifetime.Token);
        return await WaitAsync(world => world.CastResults.LastOrDefault(result => result.Sequence == sequence), $"{Name}: cast result {sequence}");
    }

    public Task SayAsync(string text) => Play.SendChatAsync(text, party: false, _lifetime.Token);

    public Task<RestartOutcome> RestartAsync(RestartChoice choice) => Play.RestartAsync(choice, _lifetime.Token);

    public Task<Simulation.Items.Usable.ItemUseOutcome> UseItemAsync(string key)
    {
        var (revision, item) = World.Read(world => (world.Owner!.InventoryRevision, world.Owner.Items.First(value => value.CatalogKey == key).InstanceId));
        return Play.UseItemAsync(new Simulation.Items.Usable.ItemUseCommand(Guid.NewGuid(), revision, item), _lifetime.Token);
    }

    public Task<Simulation.Items.InventoryOutcome> InventoryAsync(Simulation.Items.InventoryCommand command) => Play.ApplyInventoryAsync(command, _lifetime.Token);

    // Walks in report-sized steps at 5 Hz, below the run speed of the slowest character (about 123 units per second), so the
    // server's movement budget accepts every report.
    public async Task WalkAsync(Vector3 from, Vector3 to, bool running = false, float unitsPerSecond = 115f)
    {
        var distance = Vector3.Distance(from, to);
        var facing = MathF.Atan2(to.X - from.X, to.Z - from.Z);
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / (unitsPerSecond * 0.2f)));
        for (var step = 1; step <= steps; step++)
        {
            await MoveAsync(Vector3.Lerp(from, to, step / (float)steps), facing, running: running);
            await Task.Delay(200, _lifetime.Token);
        }
    }

    // Locks a basic attack on a target and keeps within reach of it, as the game client walks into range, until done.
    public async Task HuntAsync(ulong target, Func<ObservedWorld, bool> done, float reach = 24f, int timeoutMs = 10000)
    {
        await AttackAsync(target);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!World.Read(done))
        {
            if (Environment.TickCount64 > deadline)
            {
                var dump = World.Read(world => string.Join(" | ", world.Combat.Select(entry => $"{entry.Event.Kind}:{entry.Event.AttackerId}>{entry.Event.TargetId}={entry.Event.Damage}"))
                    + $" self {world.Entities.GetValueOrDefault(EntityId)?.Position} {world.Entities.GetValueOrDefault(EntityId)?.Motion}"
                    + $" prey {world.Entities.GetValueOrDefault(target)?.Position} {world.Entities.GetValueOrDefault(target)?.Motion} {world.Entities.GetValueOrDefault(target)?.Life}"
                    + $" warps {world.Warps.Count}");
                throw new TimeoutException($"{Name} never finished hunting {target}{World.Failure}: {dump}");
            }

            var (self, prey) = World.Read(world => (world.Entities.GetValueOrDefault(EntityId)?.Position, world.Entities.GetValueOrDefault(target)?.Position));
            if (self is { } from && prey is { } to && Vector3.Distance(from, to) > reach)
            {
                var step = Vector3.Distance(from, to) - reach;
                var next = from + Vector3.Normalize(to - from) * MathF.Min(step, 30f);
                await MoveAsync(next, MathF.Atan2(to.X - from.X, to.Z - from.Z), target: target);
            }

            await Task.Delay(200, _lifetime.Token);
        }
    }

    public async Task<T> WaitAsync<T>(Func<ObservedWorld, T?> probe, string what, int timeoutMs = 6000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var value = World.Read(probe);
            if (value is not null && (value is not bool flag || flag))
            {
                return value;
            }

            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"{Name} never saw {what}{World.Failure}");
            }

            await Task.Delay(20, _lifetime.Token);
        }
    }

    public Task WaitUntilAsync(Func<ObservedWorld, bool> condition, string what, int timeoutMs = 6000) =>
        WaitAsync(world => condition(world) ? (object)true : null, what, timeoutMs);

    // The client closes its socket without the leave request, like a crash or a lost connection.
    public async Task DropAsync()
    {
        await _lifetime.CancelAsync();
        await _connection.DisposeAsync();
        await AwaitReaderAsync();
    }

    public async Task LeaveAsync()
    {
        if (_play is not null)
        {
            await _play.DisposeAsync();
        }

        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            await _lifetime.CancelAsync();
        }

        await _connection.DisposeAsync();
        await AwaitReaderAsync();
        _lifetime.Dispose();
    }

    private async Task AwaitReaderAsync()
    {
        if (_reader is not null)
        {
            await _reader;
        }
    }
}

// Everything one client observed, as the scene would hold it. Reads go through Read so the reader task never races a
// probe.
internal sealed class ObservedWorld
{
    private readonly object _gate = new();

    public Dictionary<ulong, ObservedEntity> Entities { get; } = new();
    public HashSet<ulong> EverSeen { get; } = new();
    public List<ulong> Removed { get; } = new();
    public List<(double? TimeMs, CombatEventInfo Event)> Combat { get; } = new();
    public List<SkillEventInfo> Skills { get; } = new();
    public List<StatusEffectInfo> Statuses { get; } = new();
    public List<SkillCastResultInfo> CastResults { get; } = new();
    public List<ItemEffectInfo> ItemEffects { get; } = new();
    public List<ChatLineInfo> Chat { get; } = new();
    public Dictionary<Guid, GroundDropView> Ground { get; } = new();
    public List<Guid> GroundRemoved { get; } = new();
    public List<WarpInfo> Warps { get; } = new();
    public OwnerState? Owner { get; private set; }
    public int? FieldId { get; private set; }
    public string Failure { get; private set; } = "";

    public T? Read<T>(Func<ObservedWorld, T?> probe)
    {
        lock (_gate)
        {
            return probe(this);
        }
    }

    public void Close(Exception reason)
    {
        lock (_gate)
        {
            Failure = $" (presence ended: {reason.GetBaseException().Message})";
        }
    }

    public void Apply(PresenceUpdate update)
    {
        lock (_gate)
        {
            if (update.FieldId is { } field)
            {
                FieldId = field;
            }

            foreach (var spawn in update.Spawns)
            {
                EverSeen.Add(spawn.EntityId);
                if (!Entities.TryGetValue(spawn.EntityId, out var entity))
                {
                    entity = new ObservedEntity(spawn.EntityId);
                    Entities.Add(spawn.EntityId, entity);
                }

                entity.Spawn(spawn);
            }

            foreach (var state in update.States)
            {
                EverSeen.Add(state.EntityId);
                if (Entities.TryGetValue(state.EntityId, out var entity))
                {
                    entity.State(state);
                }
            }

            foreach (var removed in update.Removed)
            {
                Removed.Add(removed);
                Entities.Remove(removed);
            }

            foreach (var combat in update.Events)
            {
                Combat.Add((update.ServerTimeMs, combat));
            }

            Skills.AddRange(update.SkillEvents);
            Statuses.AddRange(update.StatusEffects);
            ItemEffects.AddRange(update.ItemEffects);
            Chat.AddRange(update.Chat);
            if (update.CastResult is { } result)
            {
                CastResults.Add(result);
            }

            foreach (var drop in update.GroundAdded)
            {
                Ground[drop.Id] = drop;
            }

            foreach (var id in update.GroundRemoved)
            {
                Ground.Remove(id);
                GroundRemoved.Add(id);
            }

            if (update.Warp is { } warp)
            {
                Warps.Add(warp);
            }

            if (update.Owner is { } owner)
            {
                Owner = owner;
            }
        }
    }

    public bool Mentions(ulong entityId) =>
        EverSeen.Contains(entityId) || Removed.Contains(entityId) || Combat.Any(entry => entry.Event.AttackerId == entityId || entry.Event.TargetId == entityId)
        || Skills.Any(entry => entry.CasterId == entityId || entry.TargetId == entityId) || Statuses.Any(entry => entry.EntityId == entityId);
}

internal sealed class ObservedEntity(ulong id)
{
    public ulong Id { get; } = id;
    public EntityKind Kind { get; private set; }
    public Vector3 Position { get; private set; }
    public float Facing { get; private set; }
    public MotionState Motion { get; private set; }
    public int Life { get; private set; }
    public int MaxLife { get; private set; }
    public int Level { get; private set; }
    public uint TemplateId { get; private set; }
    public ulong OwnerId { get; private set; }
    public PlayerAppearanceInfo? Player { get; private set; }
    public int Spawns { get; private set; }
    // Every position sample with its server time, for comparing what different clients saw at the same tick.
    public List<(double? TimeMs, Vector3 Position, MotionState Motion)> Samples { get; } = new();

    public void Spawn(EntitySpawnInfo spawn)
    {
        Spawns++;
        Kind = spawn.Kind;
        TemplateId = spawn.TemplateId;
        OwnerId = spawn.OwnerId;
        Player = spawn.Player;
        MaxLife = spawn.MaxLife;
        Level = spawn.Level;
        Record(spawn.Position, spawn.Facing, spawn.MoveState, spawn.Life, spawn.ServerTimeMs);
    }

    public void State(EntityStateInfo state) => Record(state.Position, state.Facing, state.MoveState, state.Life, state.ServerTimeMs);

    private void Record(Vector3 position, float facing, MotionState motion, int life, double? timeMs)
    {
        Position = position;
        Facing = facing;
        Motion = motion;
        Life = life;
        Samples.Add((timeMs, position, motion));
    }
}
