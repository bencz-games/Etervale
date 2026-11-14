using System.Globalization;
using System.Text.Json;

namespace PristonTale.Simulation.Items;

// Authority for cosmetic ownership and gender. Body variants are OPTIONAL presentation metadata.
public sealed class CostumeCatalog
{
    private readonly Dictionary<string, bool?> _genders;
    private readonly Dictionary<string, HashSet<int>> _bodyClasses;

    private CostumeCatalog(Dictionary<string, bool?> genders, Dictionary<string, HashSet<int>> bodyClasses)
    {
        _genders = genders;
        _bodyClasses = bodyClasses;
    }

    public static CostumeCatalog Empty { get; } = new(
        new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase));

    public bool Contains(string key) => key is not null && _genders.ContainsKey(key);

    public static bool IsEligibleKey(string key) => CostumeItemPolicy.IsCosmetic(key);
    // Compatibility alias for the previous Hopy-only policy. It now covers all known cosmetic codes.
    public static bool IsExclusiveCosmeticKey(string key) => CostumeItemPolicy.IsCosmetic(key);

    // No body means no visual override, NOT a permission failure.
    public bool Supports(string key, int armorTableIndex) =>
        key is not null && _bodyClasses.TryGetValue(key, out var supported) && supported.Contains(armorTableIndex);

    // A null value in the dictionary means the item is unisex.
    public bool Allows(string key, bool female) =>
        key is not null && _genders.TryGetValue(key, out var requiredFemale)
        && (!requiredFemale.HasValue || requiredFemale.Value == female);

    public static CostumeCatalog FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported costume manifest version.");

        var genders = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        var bodyClasses = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.GetProperty("costumes").EnumerateArray())
        {
            var key = entry.GetProperty("itemKey").GetString();
            if (!IsEligibleKey(key) || genders.ContainsKey(key))
                throw new InvalidDataException($"Invalid, unrecognized or duplicate costume: {key}");

            bool? requiredFemale = null;
            if (entry.TryGetProperty("gender", out var gender))
            {
                if (gender.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"Invalid costume gender for {key}.");
                requiredFemale = gender.GetString()?.ToLowerInvariant() switch
                {
                    "any" => null,
                    "male" => false,
                    "female" => true,
                    _ => throw new InvalidDataException($"Unsupported costume gender for {key}.")
                };
            }

            var classes = new HashSet<int>();
            if (entry.TryGetProperty("bodies", out var bodies))
            {
                if (bodies.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Invalid costume body table for {key}.");
                foreach (var body in bodies.EnumerateObject())
                {
                    if (!int.TryParse(body.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        || index is < 0 or > 10 || !classes.Add(index))
                        throw new InvalidDataException($"Invalid costume class index for {key}: {body.Name}.");
                    var model = body.Value.ValueKind == JsonValueKind.String ? body.Value.GetString()?.Replace('\\', '/') : null;
                    if (string.IsNullOrWhiteSpace(model) || !model.StartsWith("char/", StringComparison.OrdinalIgnoreCase)
                        || !model.EndsWith(".inx", StringComparison.OrdinalIgnoreCase)
                        || model.Contains(':') || model.Split('/').Any(static segment => segment is "" or "." or ".."))
                        throw new InvalidDataException($"Invalid costume model for {key}, class {index}.");
                }
            }

            genders.Add(key!, requiredFemale);
            bodyClasses.Add(key!, classes);
        }

        return new CostumeCatalog(genders, bodyClasses);
    }

    public static CostumeCatalog Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Empty;
        if (!File.Exists(path)) throw new FileNotFoundException("Configured costume manifest is missing.", path);
        return FromJson(File.ReadAllText(path));
    }
}
