using PristonTale.Application.Bellatra;
using PristonTale.Application.Services;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Pays the Bellatra prizes the world tick owes (WorldServer.BellatraPrizes), one after another, off the tick, and beside
// them stores each paid prize's points and income for Bellatra's clans (a refused storage is logged once until one is
// stored again).
public sealed class BellatraService(WorldServer world, BellatraAppService bellatra, BellatraClanAppService clans, ILogger<BellatraService> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(PayAsync(stoppingToken), RecordAsync(stoppingToken));

    private async Task RecordAsync(CancellationToken stoppingToken)
    {
        var failing = false;
        await foreach (var prize in clans.Records.ReadAllAsync(stoppingToken))
        {
            var stored = await clans.RecordAsync(prize);
            if (!stored && !failing)
            {
                logger.LogWarning("Bellatra's clans could not store a prize's points and income; they are lost");
            }

            failing = !stored;
        }
    }

    private async Task PayAsync(CancellationToken stoppingToken)
    {
        await foreach (var prize in world.BellatraPrizes.ReadAllAsync(stoppingToken))
        {
            try
            {
                await bellatra.PayAsync(prize, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "A Bellatra prize of {Gold} gold failed for {Character}", prize.Gold, prize.Session.CharacterName);
            }
        }
    }
}
