using PristonTale.Application.Clans;

namespace PristonTale.Server.World;

// Reads the clans before the game server opens (a storage failure leaves the clan service unavailable, refusing every
// request), then dissolves the clans whose deadline passed once a minute.
public sealed class ClanService(ClanAppService clans, ILogger<ClanService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await clans.LoadAsync(cancellationToken);
        }
        catch (System.Data.Common.DbException exception)
        {
            logger.LogError(exception, "The clans could not be read; the clan service stays unavailable");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (clans.Available)
            {
                await clans.ExpireAsync();
            }
        }
    }
}
