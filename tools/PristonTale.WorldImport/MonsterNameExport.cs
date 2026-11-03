using System.Text.Json;
using PristonTale.WorldImport.Parsers;
using PristonTale.Assets;

namespace PristonTale.WorldImport;

// Reproducible migration data, decoded by the same parser as normal imports: every monster's id and English key
// (IdentityRegistry), its label and the file that defines it.
internal static class MonsterNameExport
{
    public static void Write(string sourceRoot, string outputPath)
    {
        var parser = new MonsterInfParser();
        var registry = IdentityRegistry.Monsters();
        var names = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var paths = Directory.EnumerateFiles(AssetFiles.Resolve(Path.Combine(sourceRoot, "Monster")), "*.inf", options)
            .OrderBy(static path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var monster = parser.Parse(path);
            if (!seen.Add(monster.Name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(monster.Name) || string.IsNullOrWhiteSpace(monster.DisplayName))
            {
                throw new InvalidDataException($"Missing monster identity or *Name in '{path}'.");
            }

            var identity = registry.Resolve(monster.Name, monster.DisplayName);
            names.Add(new Entry(identity.Id, identity.Key, monster.DisplayName, Path.GetFileName(path)));
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(names, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Exported {names.Count} monster names to {outputPath}.");
    }

    private sealed record Entry(int Id, string Key, string DisplayName, string SourceFile);
}
