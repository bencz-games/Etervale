using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public sealed class WorldDataAppService
{
    private readonly IWorldDataRepository _repository;

    public WorldDataAppService(IWorldDataRepository repository) => _repository = repository;

    public Task<bool> HasTextsAsync(string language, CancellationToken ct = default) => _repository.HasCatalogTextsAsync(language, ct);

    public async Task<WorldData> BuildAsync(string language = CatalogLanguages.English, CancellationToken ct = default)
    {
        language = CatalogLanguages.Normalize(language);
        var itemRecords = await _repository.GetAllItemTemplatesAsync(language, ct);
        var itemSets = await _repository.GetAllItemSetsAsync(ct);
        var monsterRecords = await _repository.GetAllMonstersAsync(language, ct);
        var npcRecords = await _repository.GetAllNpcsAsync(language, ct);
        var fieldRecords = await _repository.GetAllFieldSpawnsAsync(ct);
        var motionModels = await _repository.GetAllMotionModelsAsync(ct);
        var motionSlots = await _repository.GetAllMotionSlotsAsync(ct);

        var itemDefs = WorldDataFactory.ToItemDefinitions(itemRecords);
        var itemCodes = itemDefs.ToDictionary(item => item.CatalogKey, item => item.Code, StringComparer.OrdinalIgnoreCase);
        var monsterDefs = WorldDataFactory.ToMonsterDefinitions(monsterRecords, itemCodes);
        var npcDefs = WorldDataFactory.ToNpcDefinitions(npcRecords);
        var fieldSpawns = WorldDataFactory.ToFieldSpawnData(fieldRecords);

        var motions = WorldDataFactory.ToMotionTimings(motionModels, motionSlots);

        return new WorldData(
            new ItemCatalog(itemDefs, WorldDataFactory.ToQuestVariantDefinitions(itemRecords), WorldDataFactory.ToNamedUniqueDefinitions(itemRecords),
                WorldDataFactory.ToItemSets(itemSets)),
            new MonsterCatalog(monsterDefs),
            new NpcCatalog(npcDefs),
            fieldSpawns,
            motions);
    }
}
