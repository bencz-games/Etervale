using System.Text.Json;
using PristonTale.WorldImport.Parsers;
using PristonTale.Assets;

namespace PristonTale.WorldImport;

internal static class ItemNameExport
{
    public static void Write(string root, string output)
    {
        var parser = new ItemTxtParser();
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var entries = Directory.EnumerateFiles(AssetFiles.Resolve(Path.Combine(root, "OpenItem")), "*.txt", options)
            .Order(StringComparer.Ordinal).Select(parser.Parse)
            .Where(item => !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(item => new { item.Code, item.Name }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Exported {entries.Length} item names.");
    }
}
