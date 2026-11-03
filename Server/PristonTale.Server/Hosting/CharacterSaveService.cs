using PristonTale.Application.Saves;

namespace PristonTale.Server.Hosting;

// Runs the character save queue. Starting stores the journal a previous run left before the game server opens (a
// player must never load a character older than its journaled capture); it retries until storage takes it. Registered
// before the world and the game server, it stops after them, so the leave saves of the players they disconnect are
// stored, or journaled when storage refuses them during the shutdown.
public sealed class CharacterSaveService(
    CharacterSaveQueue queue,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<CharacterSaveService> logger) : BackgroundService
{
    private static readonly TimeSpan ReplayRetry = TimeSpan.FromSeconds(5);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await queue.ReplayAsync(cancellationToken);
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not store the character save journal; retrying in {Seconds} s", ReplayRetry.TotalSeconds);
                await Task.Delay(ReplayRetry, cancellationToken);
            }
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var reports = new CancellationTokenSource();
        var reporting = ReportAsync(ReportInterval.From(configuration), reports.Token);
        await queue.RunAsync(lifetime.ApplicationStopping, stoppingToken);
        await reports.CancelAsync();
        await reporting;
        logger.LogInformation("Character saves stopped: {Stats}", queue.Stats);
    }

    private async Task ReportAsync(TimeSpan interval, CancellationToken ct)
    {
        var last = queue.Stats;
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var stats = queue.Stats;
                if (stats.Written != last.Written || stats.Failures != last.Failures || stats.Pending > 0)
                {
                    logger.LogInformation(
                        "Character saves: {Written} written in {Batches} batches, {Failures} failed, {Pending} pending, last batch {LastBatchMs:F1} ms",
                        stats.Written - last.Written, stats.Batches - last.Batches, stats.Failures - last.Failures, stats.Pending, stats.LastBatchMs);
                }

                last = stats;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
