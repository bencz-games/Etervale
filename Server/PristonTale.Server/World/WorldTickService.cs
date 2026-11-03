using System.Diagnostics;
using PristonTale.Application.Chat;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Runs the world's ticks on a thread of its own, on an absolute timeline (TickSchedule): the connections' work on the
// thread pool never delays a tick's start, and a late tick is caught up instead of slowing the game.
public sealed class WorldTickService : BackgroundService
{
    private const int TickHz = WorldServer.TickHz;
    // A stall longer than this many ticks is skipped rather than replayed at once.
    private const int MaxCatchUpTicks = 5;

    private readonly WorldServer _world;
    private readonly ChatDirectory _chat;
    private readonly ILogger<WorldTickService> _logger;
    private readonly TimeSpan _reportInterval;

    public WorldTickService(WorldServer world, ChatDirectory chat, ILogger<WorldTickService> logger, IConfiguration configuration)
    {
        _world = world;
        _chat = chat;
        _logger = logger;
        _reportInterval = Hosting.ReportInterval.From(configuration);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => Run(stoppingToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private void Run(CancellationToken stoppingToken)
    {
        Thread.CurrentThread.Name = "World tick";
        const float dt = 1f / TickHz;
        uint tick = 0;
        var schedule = new TickSchedule(Stopwatch.Frequency / TickHz, MaxCatchUpTicks, Stopwatch.GetTimestamp());
        var report = new TickReport(_world, _logger, _reportInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = schedule.WaitTicks(Stopwatch.GetTimestamp());
            if (wait > 0)
            {
                // WaitOne counts whole milliseconds: rounding up keeps a sub-millisecond wait from spinning.
                stoppingToken.WaitHandle.WaitOne((int)Math.Ceiling(wait * 1000.0 / Stopwatch.Frequency));
                continue;
            }

            var started = Stopwatch.GetTimestamp();
            schedule.Advance(started);
            try
            {
                _world.Tick(dt, ++tick);
                _chat.Deliver(_world.TakeTickNotices());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "World tick {Tick} failed", tick);
            }

            report.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds, schedule);
        }
    }

    // The tick's percentiles, phases and the process's cost, logged once per report interval.
    private sealed class TickReport
    {
        private readonly WorldServer _world;
        private readonly ILogger _logger;
        private readonly TimeSpan _interval;
        private readonly List<double> _durations;
        private readonly List<double> _commands;
        private readonly List<double> _fields;
        private readonly Process _process = Process.GetCurrentProcess();
        private int _mostCommands;
        private long _last = Stopwatch.GetTimestamp();
        private TimeSpan _lastCpu;
        private long _lastAllocated = GC.GetTotalAllocatedBytes(precise: true);
        private (int, int, int) _lastCollections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        private long _lastLate;
        private long _lastSkipped;

        public TickReport(WorldServer world, ILogger logger, TimeSpan interval)
        {
            _world = world;
            _logger = logger;
            _interval = interval;
            var ticks = TickHz * (int)interval.TotalSeconds;
            _durations = new List<double>(ticks);
            _commands = new List<double>(ticks);
            _fields = new List<double>(ticks);
            _lastCpu = _process.TotalProcessorTime;
        }

        public void Add(double milliseconds, TickSchedule schedule)
        {
            _durations.Add(milliseconds);
            var phases = _world.LastPhases;
            _commands.Add(phases.CommandsMs);
            _fields.Add(phases.FieldsMs);
            _mostCommands = Math.Max(_mostCommands, phases.Commands);
            var elapsed = Stopwatch.GetElapsedTime(_last);
            if (elapsed < _interval)
            {
                return;
            }

            _durations.Sort();
            _commands.Sort();
            _fields.Sort();
            _process.Refresh();
            var cpu = _process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var collections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            _logger.LogInformation(
                "World tick: {Players} players, p50 {P50:F2} ms, p99 {P99:F2} ms, max {Max:F2} ms over {Count} ticks (budget {Budget:F0} ms), " +
                "{Late} caught up late, {Skipped} skipped; commands p50 {CommandsP50:F2} ms, p99 {CommandsP99:F2} ms, at most {MostCommands} a tick; " +
                "fields p50 {FieldsP50:F2} ms, p99 {FieldsP99:F2} ms; " +
                "process {Cores:F2} cores, {Allocated:F1} MiB/s allocated, GC {Gen0}/{Gen1}/{Gen2}, heap {Heap:F0} MiB, working set {WorkingSet:F0} MiB",
                _world.PlayerCount, Percentile(_durations, 0.5), Percentile(_durations, 0.99), _durations[^1], _durations.Count, 1000.0 / TickHz,
                schedule.Late - _lastLate, schedule.Skipped - _lastSkipped, Percentile(_commands, 0.5), Percentile(_commands, 0.99), _mostCommands,
                Percentile(_fields, 0.5), Percentile(_fields, 0.99),
                (cpu - _lastCpu).TotalSeconds / elapsed.TotalSeconds, (allocated - _lastAllocated) / 1048576.0 / elapsed.TotalSeconds,
                collections.Item1 - _lastCollections.Item1, collections.Item2 - _lastCollections.Item2, collections.Item3 - _lastCollections.Item3,
                GC.GetTotalMemory(false) / 1048576.0, _process.WorkingSet64 / 1048576.0);
            _durations.Clear();
            _commands.Clear();
            _fields.Clear();
            _mostCommands = 0;
            _last = Stopwatch.GetTimestamp();
            _lastCpu = cpu;
            _lastAllocated = allocated;
            _lastCollections = collections;
            _lastLate = schedule.Late;
            _lastSkipped = schedule.Skipped;
        }

        private static double Percentile(List<double> sorted, double share) => sorted[(int)(sorted.Count * share)];
    }
}

// Tick n is due at start + n periods. A tick that starts a period or more after it was due is late, and the ticks owed
// run back to back; past maxCatchUp owed ticks the timeline moves to now and the ticks it passes are skipped.
internal sealed class TickSchedule(long period, int maxCatchUp, long start)
{
    private long _due = start + period;

    public long Late { get; private set; }
    public long Skipped { get; private set; }

    // Timestamp units until the next tick is due; zero or less when it is due now.
    public long WaitTicks(long now) => _due - now;

    // The due tick starts at now.
    public void Advance(long now)
    {
        var owed = (now - _due) / period;
        if (owed > maxCatchUp)
        {
            Skipped += owed;
            _due += owed * period;
        }
        else if (owed > 0)
        {
            Late++;
        }

        _due += period;
    }
}
