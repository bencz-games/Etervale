using PristonTale.Application.Services;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Pays the Bellatra prizes the world tick owes (WorldServer.BellatraPrizes), one after another, off the tick.
public sealed class BellatraService(WorldServer world, BellatraAppService bellatra, ILogger<BellatraService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
