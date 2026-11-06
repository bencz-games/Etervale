using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Protocol.Transport;

// Headless load generator over Etervale's protocol. Each bot logs in, creates a character when the account
// has none, enters the world and then walks, hunts monsters with basic attacks, casts a skill at them or switches its
// bag page (a stored inventory command, as a potion or a pickup is), reporting its movement at 5 Hz and draining
// presence. Accounts (and placed hunting characters) come from:
// dotnet run --project tools/PristonTale.DevAccounts -- create-bots <prefix> <count> <password> [--class ... --field ... --at x,z]
var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine($"usage: --count <bots> [--address {TlsTransport.LocalAddress}] [--prefix bot] [--password bot-pw] [--seconds 60] [--ramp 50]");
    Console.Error.WriteLine("       [--mode walk|fight|cast|items|mixed] [--skill <hex>] [--item-ms 1000] [--server-pid <pid>] [--report <seconds>]");
    return 1;
}

Console.WriteLine($"{options.Count} bots ({options.Mode}) against {options.Address}, ramp {options.RampPerSecond}/s, {options.Seconds} s in world");
var stats = new Stats();
using var stop = new CancellationTokenSource();
var bots = new List<Task>(options.Count);
var ramp = Stopwatch.StartNew();
for (var index = 1; index <= options.Count; index++)
{
    bots.Add(Bot.RunAsync(index, options, stats, stop.Token));
    var wait = TimeSpan.FromSeconds(index / (double)options.RampPerSecond) - ramp.Elapsed;
    if (wait > TimeSpan.Zero)
    {
        await Task.Delay(wait);
    }
}

var server = options.ServerPid > 0 ? Process.GetProcessById(options.ServerPid) : null;
var measured = Stopwatch.StartNew();
var last = stats.Sample();
stats.TakeItemLatencies();
var lastCpu = server?.TotalProcessorTime ?? TimeSpan.Zero;
var totals = new List<double>();
while (measured.Elapsed < TimeSpan.FromSeconds(options.Seconds))
{
    await Task.Delay(TimeSpan.FromSeconds(options.ReportSeconds));
    var now = stats.Sample();
    var seconds = options.ReportSeconds;
    var playing = Math.Max(1, now.Playing);
    var perBot = (now.Bytes - last.Bytes) / 1024.0 / seconds / playing;
    totals.Add(perBot);
    var line = string.Create(CultureInfo.InvariantCulture,
        $"t={measured.Elapsed.TotalSeconds,5:F0}s playing={now.Playing,5} failed={now.Failures,4} in={(now.Bytes - last.Bytes) / 1024.0 / seconds,9:F1} KiB/s " +
        $"({perBot,6:F2} per bot) updates/s={(now.Updates - last.Updates) / (double)seconds,8:F0} hits/s={(now.Hits - last.Hits) / (double)seconds,7:F0} " +
        $"casts/s={(now.Casts - last.Casts) / (double)seconds,6:F0} ok={(now.CastsAccepted - last.CastsAccepted) / (double)seconds,6:F0} corrections={now.Corrections}");
    if (stats.TakeItemLatencies() is { Count: > 0 } latencies)
    {
        latencies.Sort();
        line += string.Create(CultureInfo.InvariantCulture,
            $" items/s={latencies.Count / (double)seconds,6:F0} refused={now.ItemsRefused - last.ItemsRefused,5} " +
            $"ms p50={latencies[latencies.Count / 2],6:F1} p99={latencies[latencies.Count * 99 / 100],7:F1} max={latencies[^1],7:F1}");
    }
    if (server is not null)
    {
        server.Refresh();
        var cpu = server.TotalProcessorTime;
        line += string.Create(CultureInfo.InvariantCulture,
            $" server cpu={(cpu - lastCpu).TotalSeconds / seconds,5:F2} cores ws={server.WorkingSet64 / 1048576.0,6:F0} MiB");
        lastCpu = cpu;
    }

    Console.WriteLine(line);
    last = now;
}

await stop.CancelAsync();
await Task.WhenAll(bots);
var final = stats.Sample();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"done: {final.Entered} entered, {final.Failures} failed, {final.Corrections} position corrections, {final.LeaveTimeouts} leave timeouts, " +
    $"mean {(totals.Count == 0 ? 0 : totals.Average()):F2} KiB/s per bot"));
if (!stats.Refusals.IsEmpty)
{
    Console.WriteLine("refused casts: " + string.Join(", ", stats.Refusals.Select(pair => $"{pair.Key} {pair.Value}")));
}

if (!stats.ItemRefusals.IsEmpty)
{
    Console.WriteLine("refused items: " + string.Join(", ", stats.ItemRefusals.Select(pair => $"{pair.Key} {pair.Value}")));
}

return final.Failures == 0 ? 0 : 2;

internal enum BotMode
{
    Walk,
    Fight,
    Cast,
    Items,
    Mixed
}

internal static class Bot
{
    private const float ReportSeconds = 0.2f;
    // Below the slowest run speed with the server's report tolerance, so legal walks are never corrected.
    private const float UnitsPerSecond = 100f;

    public static async Task RunAsync(int index, Options options, Stats stats, CancellationToken stop)
    {
        var account = $"{options.Prefix}{index:D4}";
        try
        {
            await using var connection = new GameServerConnection(options.Address);
            var login = await connection.LoginAsync(account, options.Password, "en", stop);
            if (!login.Ok)
            {
                throw new InvalidOperationException($"login: {login.Message}");
            }

            var characters = await connection.ListCharactersAsync(stop);
            var name = characters.FirstOrDefault()?.Name;
            if (name is null)
            {
                name = $"Lt{options.Prefix}{index:D4}";
                var created = await connection.CreateCharacterAsync(name, PlayerClass.Fighter, 0, stop);
                if (!created.Ok)
                {
                    throw new InvalidOperationException($"create: {created.Message}");
                }
            }

            var enter = await connection.EnterWorldAsync(name, stop);
            if (!enter.Ok)
            {
                throw new InvalidOperationException($"enter: {enter.Message}");
            }

            Interlocked.Increment(ref stats.Entered);
            var play = connection.OpenPlayChannel(stop);
            Interlocked.Increment(ref stats.Playing);
            var world = new BotWorld(enter.EntityId);
            var reader = DrainAsync(play, world, stats, stop);
            var mode = options.Mode == BotMode.Mixed ? (index % 10) switch { < 5 => BotMode.Walk, < 8 => BotMode.Fight, _ => BotMode.Cast } : options.Mode;
            try
            {
                await PlayAsync(play, connection, world, mode, index, new Vector3(enter.SpawnX, 0f, enter.SpawnZ), enter.FieldId, options, stats, stop);
            }
            catch (OperationCanceledException)
            {
            }

            Interlocked.Decrement(ref stats.Playing);
            try
            {
                // Leaving waits for the server-side save, which queues behind every other bot leaving at once.
                await play.DisposeAsync();
            }
            catch (TimeoutException)
            {
                Interlocked.Increment(ref stats.LeaveTimeouts);
            }

            await reader;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (Interlocked.Increment(ref stats.Failures) <= 10)
            {
                Console.Error.WriteLine($"{account}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static async Task PlayAsync(IPlayChannel play, GameServerConnection connection, BotWorld world, BotMode mode, int index, Vector3 spawn, int field,
        Options options, Stats stats, CancellationToken stop)
    {
        const float Radius = 150f;
        var phase = index * 0.37f;
        // The circle passes through the spawn so the first report stays within one step of the server position.
        var center = new Vector2(spawn.X, spawn.Z) - new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * Radius;
        var position = spawn;
        ulong target = 0;
        uint sequence = 0;
        long lastBytes = 0;
        var nextCast = TimeSpan.FromSeconds(index % 30 / 10.0);
        var nextItem = TimeSpan.FromMilliseconds(index * 37 % options.ItemMs);
        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ReportSeconds));
        while (await timer.WaitForNextTickAsync(stop))
        {
            if (world.TakeCorrection() is { } corrected)
            {
                position = corrected;
                center = new Vector2(corrected.X, corrected.Z) - new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * Radius;
            }

            if (mode == BotMode.Items)
            {
                await play.SendMovementAsync(position, 0f, false, false, 0, field, stop);
                if (clock.Elapsed >= nextItem && world.Inventory is var (revision, page))
                {
                    nextItem = clock.Elapsed + TimeSpan.FromMilliseconds(options.ItemMs);
                    var started = Stopwatch.GetTimestamp();
                    var outcome = await play.ApplyInventoryAsync(
                        new InventoryCommand(Guid.NewGuid(), revision, InventoryAction.SwitchBagPage, Guid.Empty, page ^ 1, 0, default), stop);
                    stats.RecordItem(Stopwatch.GetElapsedTime(started), outcome.Ok ? null : outcome.Message.ToString());
                    // A refusal before the world (save gate busy) answers no revision; the expected one still holds.
                    if (outcome.Revision > 0)
                    {
                        world.Answered(outcome.Revision, outcome.Ok ? page ^ 1 : page);
                    }
                }
            }
            else if (mode == BotMode.Walk)
            {
                phase += UnitsPerSecond * ReportSeconds / Radius;
                position = new Vector3(center.X + MathF.Cos(phase) * Radius, 0f, center.Y + MathF.Sin(phase) * Radius);
                await play.SendMovementAsync(position, MathF.Atan2(-MathF.Sin(phase), MathF.Cos(phase)), true, false, 0, field, stop);
            }
            else
            {
                // Hunters move from where the server has them, which the stage's walls may have changed.
                if (world.Self is { } shown && Vector3.Distance(Flat(shown), Flat(position)) > 40f)
                {
                    position = shown;
                }

                if (target == 0 || !world.Alive(target))
                {
                    target = world.NearestMonster(position, 900f);
                    if (target != 0 && mode == BotMode.Fight)
                    {
                        await play.SendAttackAsync(target, true, stop, ++sequence);
                        Interlocked.Increment(ref stats.Attacks);
                    }
                }

                // Hunters close in to striking distance; casters stop at their skill's range.
                var reach = mode == BotMode.Fight ? 28f : 70f;
                if (target != 0 && world.PositionOf(target) is { } prey && Vector3.Distance(Flat(position), Flat(prey)) > reach)
                {
                    var step = MathF.Min(UnitsPerSecond * ReportSeconds, Vector3.Distance(Flat(position), Flat(prey)) - reach);
                    position += Vector3.Normalize(Flat(prey) - Flat(position)) * step;
                    await play.SendMovementAsync(position, MathF.Atan2(prey.X - position.X, prey.Z - position.Z), true, false, mode == BotMode.Fight ? target : 0,
                        field, stop);
                }
                else
                {
                    await play.SendMovementAsync(position, 0f, false, false, mode == BotMode.Fight ? target : 0, field, stop);
                }

                if (world.TakeCastWait() is { } wait)
                {
                    nextCast = clock.Elapsed + wait;
                }

                if (mode == BotMode.Cast && target != 0 && clock.Elapsed >= nextCast)
                {
                    // Until the answer says otherwise; a cooldown answer moves the next cast to its end.
                    nextCast = clock.Elapsed + TimeSpan.FromSeconds(3);
                    await play.SendSkillCastAsync(++sequence, options.Skill, target, null, stop);
                    Interlocked.Increment(ref stats.Casts);
                }
            }

            var bytes = connection.BytesReceived;
            Interlocked.Add(ref stats.BytesReceived, bytes - lastBytes);
            lastBytes = bytes;
        }
    }

    private static Vector3 Flat(Vector3 value) => new(value.X, 0f, value.Z);

    private static async Task DrainAsync(IPlayChannel play, BotWorld world, Stats stats, CancellationToken stop)
    {
        try
        {
            await foreach (var update in play.ReadAllAsync(stop))
            {
                Interlocked.Increment(ref stats.Updates);
                world.Apply(update, stats);
                play.Recycle(update);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
    }
}

// The monsters a bot sees, enough to pick and chase a target, and the corrections it receives.
internal sealed class BotWorld(ulong self)
{
    private readonly object _gate = new();
    private readonly Dictionary<ulong, (Vector3 Position, bool Alive)> _monsters = new();
    private Vector3? _correction;
    private TimeSpan? _castWait;

    // Where the server last showed this bot (written by the drain task, read by the play loop).
    private Vector3? _self;

    public Vector3? Self
    {
        get
        {
            lock (_gate)
            {
                return _self;
            }
        }
    }

    // The inventory revision the next command expects and the bag page shown: the newer of the last command's answer and
    // the last owner state (one sent before the answer carries an older revision). Null before the first owner state.
    public (long Revision, int Page)? Inventory
    {
        get
        {
            lock (_gate)
            {
                return _revision is { } revision ? (revision, _page) : null;
            }
        }
    }

    private long? _revision;
    private int _page;

    public void Answered(long revision, int page)
    {
        lock (_gate)
        {
            Take(revision, page);
        }
    }

    private void Take(long revision, int page)
    {
        if (revision >= (_revision ?? 0))
        {
            _revision = revision;
            _page = page;
        }
    }

    public void Apply(PresenceUpdate update, Stats stats)
    {
        lock (_gate)
        {
            if (update.Owner is { } owner)
            {
                Take(owner.InventoryRevision, owner.BagPage);
            }

            foreach (var spawn in update.Spawns)
            {
                if (spawn.EntityId == self)
                {
                    _self = spawn.Position;
                }

                if (spawn.Kind == EntityKind.Monster && spawn.OwnerId == 0)
                {
                    _monsters[spawn.EntityId] = (spawn.Position, spawn.Life > 0 && spawn.MoveState != MotionState.Dead);
                }
            }

            for (var index = 0; index < update.States.Count; index++)
            {
                var state = update.States[index];
                if (state.EntityId == self)
                {
                    _self = state.Position;
                }

                if (_monsters.ContainsKey(state.EntityId))
                {
                    _monsters[state.EntityId] = (state.Position, state.Life > 0 && state.MoveState != MotionState.Dead);
                }
            }

            foreach (var removed in update.Removed)
            {
                _monsters.Remove(removed);
            }

            foreach (var combat in update.Events)
            {
                if (combat.Kind == CombatEventKind.Hit && combat.AttackerId == self)
                {
                    Interlocked.Increment(ref stats.Hits);
                }
            }

            if (update.CastResult is { } result)
            {
                if (result.Accepted)
                {
                    Interlocked.Increment(ref stats.CastsAccepted);
                    _castWait = TimeSpan.FromMilliseconds(Math.Max(3000, result.CooldownMs));
                }
                else if (result.RemainingMs > 0)
                {
                    _castWait = TimeSpan.FromMilliseconds(result.RemainingMs);
                }
                else
                {
                    stats.Refusals.AddOrUpdate(result.Status.ToString(), 1, static (_, count) => count + 1);
                }
            }

            if (update.Warp is { Forced: true } warp)
            {
                Interlocked.Increment(ref stats.Corrections);
                _correction = warp.Position;
            }
        }
    }

    public TimeSpan? TakeCastWait()
    {
        lock (_gate)
        {
            var wait = _castWait;
            _castWait = null;
            return wait;
        }
    }

    public Vector3? TakeCorrection()
    {
        lock (_gate)
        {
            var correction = _correction;
            _correction = null;
            return correction;
        }
    }

    public bool Alive(ulong id)
    {
        lock (_gate)
        {
            return _monsters.TryGetValue(id, out var monster) && monster.Alive;
        }
    }

    public Vector3? PositionOf(ulong id)
    {
        lock (_gate)
        {
            return _monsters.TryGetValue(id, out var monster) ? monster.Position : null;
        }
    }

    public ulong NearestMonster(Vector3 from, float within)
    {
        lock (_gate)
        {
            var best = 0UL;
            var bestDistance = within * within;
            foreach (var (id, monster) in _monsters)
            {
                var distance = PristonTale.Simulation.Physics.Planar.DistanceSquared(monster.Position, from);
                if (monster.Alive && distance < bestDistance)
                {
                    best = id;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}

internal sealed class Stats
{
    public long BytesReceived;
    public long Updates;
    public long Hits;
    public long Attacks;
    public long Casts;
    public long CastsAccepted;
    public int Playing;
    public int Entered;
    public int Failures;
    public int Corrections;
    public int LeaveTimeouts;
    public int ItemsRefused;
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> ItemRefusals = new();
    private readonly object _itemGate = new();
    private List<double> _itemLatencies = [];
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Refusals = new();

    public void RecordItem(TimeSpan latency, string? refusal)
    {
        if (refusal is not null)
        {
            Interlocked.Increment(ref ItemsRefused);
            ItemRefusals.AddOrUpdate(refusal, 1, static (_, count) => count + 1);
        }

        lock (_itemGate)
        {
            _itemLatencies.Add(latency.TotalMilliseconds);
        }
    }

    // The answer times of the stored commands since the last call, in milliseconds.
    public List<double> TakeItemLatencies()
    {
        lock (_itemGate)
        {
            var taken = _itemLatencies;
            _itemLatencies = [];
            return taken;
        }
    }

    public Snapshot Sample() => new(Interlocked.Read(ref BytesReceived), Interlocked.Read(ref Updates), Interlocked.Read(ref Hits), Interlocked.Read(ref Casts),
        Interlocked.Read(ref CastsAccepted), Volatile.Read(ref Playing), Volatile.Read(ref Entered), Volatile.Read(ref Failures), Volatile.Read(ref Corrections),
        Volatile.Read(ref LeaveTimeouts), Volatile.Read(ref ItemsRefused));

    public readonly record struct Snapshot(long Bytes, long Updates, long Hits, long Casts, long CastsAccepted, int Playing, int Entered, int Failures,
        int Corrections, int LeaveTimeouts, int ItemsRefused);
}

internal sealed record Options(string Address, string Prefix, string Password, int Count, int Seconds, int RampPerSecond, BotMode Mode, uint Skill,
    int ServerPid, int ReportSeconds, int ItemMs)
{
    public static Options? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        // A malformed number is a usage error (exit 1), as a missing count is.
        bool Number(string name, int fallback, out int value) =>
            int.TryParse(values.GetValueOrDefault(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out value);

        var skillText = values.GetValueOrDefault("--skill", "0x01010002");
        skillText = skillText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? skillText[2..] : skillText;
        if (!values.TryGetValue("--count", out var countText) || !int.TryParse(countText, out var count) || count < 1
            || !Enum.TryParse<BotMode>(values.GetValueOrDefault("--mode", "walk"), ignoreCase: true, out var mode)
            || !Number("--seconds", 60, out var seconds) || !Number("--ramp", 50, out var ramp) || !Number("--server-pid", 0, out var serverPid)
            || !Number("--report", 5, out var report) || !Number("--item-ms", 1000, out var itemMs)
            || !uint.TryParse(skillText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var skill))
        {
            return null;
        }

        return new Options(
            values.GetValueOrDefault("--address", TlsTransport.LocalAddress),
            values.GetValueOrDefault("--prefix", "bot"),
            values.GetValueOrDefault("--password", "bot-pw"),
            count,
            seconds,
            Math.Max(1, ramp),
            mode,
            skill,
            serverPid,
            Math.Max(1, report),
            Math.Max(50, itemMs));
    }
}
