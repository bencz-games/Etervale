using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Server.Hosting;

public sealed class WorldDataLoadService(
    WorldDataAppService worldData,
    WorldDataHolder holder,
    WorldDataLanguagesHolder localizedHolder,
    ILogger<WorldDataLoadService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var data = await worldData.BuildAsync(cancellationToken, CatalogLanguages.English);
        holder.Current = data;
        localizedHolder.Set(CatalogLanguages.English, data);
        foreach (var language in CatalogLanguages.Supported)
        {
            if (language != CatalogLanguages.English)
            {
                localizedHolder.Set(language, await worldData.BuildAsync(cancellationToken, language));
            }
        }

        logger.LogInformation(
            "World catalogs loaded: {Items} items / {Monsters} monsters / {Npcs} npcs / {Fields} fields-with-spawns.",
            data.Items.All.Count, data.Monsters.All.Count, data.Npcs.All.Count, data.FieldSpawns.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
