using System.Text.Json;
using PristonTale.Assets;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

// Exports the exact NPC INX motion rows used to repair already-imported installations.
internal static class NpcMotionExport
{
    private const int FormatVersion = 1;

    public static void Write(string sourceRoot, string assetRoot, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var parser = new NpcParser();
        var extractor = new MotionTableExtractor(assetRoot);
        var unresolved = new List<string>();
        var options = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive
        };
        // Every NPC file is an NPC (as the import takes them), whatever names it shares with others.
        var npcDirectory = AssetFiles.Resolve(Path.Combine(sourceRoot, "NPC"));
        var paths = Directory.EnumerateFiles(npcDirectory, "*.npc", options)
            .OrderBy(static path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var npc = parser.Parse(path);
            if (!extractor.TryExtract(npc.Model, out var failure))
            {
                unresolved.Add($"{Path.GetFileName(path)} ({npc.Name}): {failure}");
            }
        }

        var models = extractor.Models
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ModelEntry(pair.Key, pair.Value))
            .ToArray();
        var slots = extractor.Tables
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(static pair => pair.Value
                .OrderBy(static slot => slot.Slot)
                .Select(slot => new SlotEntry(
                    pair.Key,
                    slot.Slot,
                    slot.State,
                    slot.StartFrame,
                    slot.EndFrame,
                    slot.Event0,
                    slot.Event1,
                    slot.Event2,
                    slot.Event3,
                    slot.Repeat)))
            .ToArray();
        var manifest = new Manifest(FormatVersion, models, slots);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(outputPath, json + "\n");

        Console.WriteLine($"Exported {models.Length} NPC models and {slots.Length} motion clips to {outputPath}.");
        foreach (var failure in unresolved)
        {
            Console.WriteLine($"Unresolved NPC motion: {failure}");
        }
    }

    private sealed record Manifest(int FormatVersion, IReadOnlyList<ModelEntry> Models, IReadOnlyList<SlotEntry> Slots);
    private sealed record ModelEntry(string ModelKey, string MotionTableKey);
    private sealed record SlotEntry(
        string MotionTableKey,
        int Slot,
        int State,
        int StartFrame,
        int EndFrame,
        long Event0,
        long Event1,
        long Event2,
        long Event3,
        bool Repeat);
}
