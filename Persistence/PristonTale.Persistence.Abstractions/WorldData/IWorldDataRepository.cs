namespace PristonTale.Persistence.Abstractions.WorldData;

public interface IWorldDataRepository
{
    Task<IReadOnlyList<ItemTemplateRecord>> GetAllItemTemplatesAsync(string language = CatalogLanguages.English, CancellationToken ct = default);

    Task<IReadOnlyList<ItemSetRecord>> GetAllItemSetsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemSetRecord>>([]);

    Task<IReadOnlyList<MonsterRecord>> GetAllMonstersAsync(string language = CatalogLanguages.English, CancellationToken ct = default);

    Task<IReadOnlyList<NpcRecord>> GetAllNpcsAsync(string language = CatalogLanguages.English, CancellationToken ct = default);

    Task<IReadOnlyList<FieldSpawnRecord>> GetAllFieldSpawnsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MotionModelRecord>> GetAllMotionModelsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MotionSlotRecord>> GetAllMotionSlotsAsync(CancellationToken ct = default);
}
