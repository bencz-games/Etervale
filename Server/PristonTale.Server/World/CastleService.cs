using PristonTale.Application.Castle;
using PristonTale.Application.Settings;

namespace PristonTale.Server.World;

// Reads Bless Castle after the clans and before the game server opens (a storage failure leaves the castle without its
// schedule), then runs its clock every TickInterval with the castle settings in force; a throwing tick is logged and
// the clock goes on, and a refused storage is logged once until a tick stores again. It stops after the game server, so
// the tax of the last operations is stored with the treasury.
public sealed class CastleService(CastleAppService castle, ServerSettingsAppService settings, ILogger<CastleService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await castle.LoadAsync(cancellationToken);
        }
        catch (System.Data.Common.DbException exception)
        {
            logger.LogError(exception, "Bless Castle could not be read; it stays without its schedule");
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            if (!await castle.FlushTaxAsync())
            {
                logger.LogWarning("Bless Castle's last tax could not be stored with its treasury");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bless Castle's last tax could not be stored with its treasury");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        var failing = false;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var stored = await castle.TickAsync(settings.Current.Castle);
                if (!stored && !failing)
                {
                    logger.LogWarning("Bless Castle could not store a change; it is tried again each minute");
                }

                failing = !stored;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The Bless Castle tick failed");
            }
        }
    }
}
