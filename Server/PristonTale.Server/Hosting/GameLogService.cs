using PristonTale.Application.GameLogs;
using PristonTale.Application.GameMasters;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Server.Net;

namespace PristonTale.Server.Hosting;

// Logs:CurrencyMinutes (1 to a day) and Logs:RetentionDays (0 keeps the logs).
public sealed record GameLogOptions(int CurrencyMinutes, int RetentionDays)
{
    private const int DefaultCurrencyMinutes = 10;
    private const int DefaultRetentionDays = 180;
    private const int MaxCurrencyMinutes = 24 * 60;

    public static GameLogOptions From(IConfiguration configuration)
    {
        var options = new GameLogOptions(configuration.GetValue("Logs:CurrencyMinutes", DefaultCurrencyMinutes),
            configuration.GetValue("Logs:RetentionDays", DefaultRetentionDays));
        if (options.CurrencyMinutes is < 1 or > MaxCurrencyMinutes)
        {
            throw new InvalidOperationException($"Logs:CurrencyMinutes must be between 1 and {MaxCurrencyMinutes}.");
        }

        return options.RetentionDays >= 0 ? options : throw new InvalidOperationException("Logs:RetentionDays must not be negative.");
    }
}

// Writes the game logs: the item transfers the world reports (GameLogQueue), the game masters' audit (GmAuditQueue), the
// gold each currency sink took every Logs:CurrencyMinutes, the server's start and stop, and once a day prunes rows older
// than Logs:RetentionDays. The start is logged, and the first prune runs, once the host has started: the server's name
// comes from the settings table, and pruning must not hold the start. Registered before the world and the game server, it
// stops after them, so their last transfers and audit rows are written. A log write that fails is reported and never
// holds the game: currency is kept for the next period.
public sealed class GameLogService(
    GameLogQueue queue,
    GmAuditQueue audit,
    IGameLogRepository logs,
    WorldServer world,
    GameServerControl control,
    GameLogOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<GameLogService> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var writing = queue.RunAsync(stoppingToken);
        var auditing = audit.RunAsync(stoppingToken);
        if (await StartedAsync(stoppingToken))
        {
            await ServerEventAsync(ServerEventKind.Started);
            var nextPrune = DateTime.UtcNow;
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.CurrencyMinutes));
            do
            {
                if (options.RetentionDays > 0 && DateTime.UtcNow >= nextPrune)
                {
                    nextPrune = DateTime.UtcNow + PruneInterval;
                    await PruneAsync();
                }
            }
            while (await TickAsync(timer, stoppingToken) && await CurrencyAsync());
        }

        await CurrencyAsync();
        await writing;
        await auditing;
        await ServerEventAsync(ServerEventKind.Stopped, $"{queue.Stats}");
    }

    private async Task<bool> StartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStopping = stoppingToken.Register(() => started.TrySetCanceled());
        try
        {
            await started.Task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> TickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // Stores the gold each sink took since the last period; a failed write keeps it for the next one. Always true, so a
    // period's write continues the loop.
    private async Task<bool> CurrencyAsync()
    {
        var taken = world.Currency.TakeUnrecorded();
        var now = DateTime.UtcNow;
        var sinks = taken.Select((gold, sink) => new CurrencySinkRecord(now, (short)sink, gold)).Where(static sink => sink.Gold > 0).ToList();
        if (sinks.Count == 0)
        {
            return true;
        }

        try
        {
            await logs.AppendCurrencyAsync(sinks, CancellationToken.None);
        }
        catch (Exception exception)
        {
            world.Currency.Restore(taken);
            logger.LogError(exception, "Could not store the currency sinks; they are kept for the next period");
        }

        return true;
    }

    private async Task PruneAsync()
    {
        try
        {
            var pruned = await logs.PruneAsync(DateTime.UtcNow.AddDays(-options.RetentionDays), CancellationToken.None);
            logger.LogInformation("Game logs: {Rows} rows older than {Days} days pruned", pruned, options.RetentionDays);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not prune the game logs");
        }
    }

    private async Task ServerEventAsync(ServerEventKind kind, string detail = "")
    {
        try
        {
            await logs.AppendServerEventAsync(new ServerEventRecord(DateTime.UtcNow, kind, control.ServerName, detail), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not store the server event {Kind}", kind);
        }
    }
}
