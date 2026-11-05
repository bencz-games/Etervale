using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Application.World;

public sealed class WorldDataLanguagesHolder
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, WorldData> _current = new Dictionary<string, WorldData>(StringComparer.OrdinalIgnoreCase);

    public WorldData Get(string language, WorldData fallback)
    {
        var current = Volatile.Read(ref _current);
        return current.TryGetValue(CatalogLanguages.Normalize(language), out var data) ? data : fallback;
    }

    public void Set(string language, WorldData data)
    {
        lock (_gate)
        {
            var next = new Dictionary<string, WorldData>(_current, StringComparer.OrdinalIgnoreCase)
            {
                [CatalogLanguages.Normalize(language)] = data
            };
            Volatile.Write(ref _current, next);
        }
    }
}
