using PristonTale.Engine3D.Equipment;
using System.Text.Json;
using PristonTale.Assets;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.World.Remote;

// Optional, client-side body overrides. An absent or invalid manifest changes no visuals.
// This catalog does NOT grant ownership or a cosmetic equipment slot.
public sealed class CostumeVisualCatalog
{
    private readonly Dictionary<string, Dictionary<int, string>> _bodies;

    private CostumeVisualCatalog(Dictionary<string, Dictionary<int, string>> bodies) => _bodies = bodies;

    public static CostumeVisualCatalog Empty { get; } = new(new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase));

    public bool HasCostume(string itemKey) => itemKey is not null && _bodies.ContainsKey(itemKey);

    public bool TryBody(string itemKey, int armorTableIndex, out string path)
    {
        path = "";
        if (itemKey is not null && _bodies.TryGetValue(itemKey, out var variants)
            && variants.TryGetValue(armorTableIndex, out var resolved))
        {
            path = resolved;
            return true;
        }

        return false;
    }

    public static CostumeVisualCatalog Load(string assetRoot, IClientLog log)
    {
        var manifest = AssetFiles.ResolveFile(assetRoot, "summit/costumes.json");
        if (manifest is null)
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
            {
                throw new InvalidDataException("Unsupported costume catalog version.");
            }

            var bodies = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in root.GetProperty("costumes").EnumerateArray())
            {
                var key = entry.GetProperty("itemKey").GetString();
                if (!CostumeCatalog.IsEligibleKey(key) || !bodies.TryAdd(key, new Dictionary<int, string>()))
                {
                    // Only CA547/CA647 may be opted in from the CA family.
                    throw new InvalidDataException("Invalid, disabled or duplicate costume item key.");
                }

                var variants = bodies[key];
                foreach (var model in entry.GetProperty("bodies").EnumerateObject())
                {
                    if (!int.TryParse(model.Name, out var classIndex) || classIndex < 0
                        || classIndex >= EquipmentVisualTables.Armors.Length || variants.ContainsKey(classIndex))
                    {
                        throw new InvalidDataException($"Invalid costume class index: {model.Name}.");
                    }

                    var relative = model.Value.GetString()?.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(relative) || !relative.StartsWith("char/", StringComparison.OrdinalIgnoreCase)
                        || relative.Split('/').Any(static segment => segment is ".." or "." or "")
                        || relative.Contains(':') || !relative.EndsWith(".inx", StringComparison.OrdinalIgnoreCase)
                        || AssetFiles.ResolveFile(assetRoot, relative) is null)
                    {
                        throw new InvalidDataException($"Invalid or missing costume model for {key}, class {classIndex}.");
                    }

                    variants.Add(classIndex, relative);
                }

                // Entries with no model remain available in the cosmetic slot; the renderer
                // retains the real armour appearance until a verified body override exists.
            }

            log.Info($"[Costume] Validated {bodies.Count} optional body overrides.");
            return new CostumeVisualCatalog(bodies);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            log.Warn($"[Costume] Manifest rejected; original bodies retained: {e.Message}");
            return Empty;
        }
    }
}
