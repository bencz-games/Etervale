using System.Data.Common;
using System.Text.Json;
using Dapper;

namespace PristonTale.WorldImport;

// The catalog's texts in another language than English, from Translations/<language>.json (embedded): NPC names and
// dialog lines by NPC key, monster names by monster key, item names by item code. A text replaces the English one of
// the same entity (a dialog line the one of the same ordinal); what a translation lacks reads as English. A key the
// catalog lacks is a warning.
public sealed partial class Importer
{
    private const string TranslationPrefix = "PristonTale.WorldImport.Translations.";

    private sealed record NpcTranslation(string? Name, List<string>? Dialog);

    private sealed record CatalogTranslation(Dictionary<string, NpcTranslation>? Npcs, Dictionary<string, string>? Monsters,
        Dictionary<string, string>? Items);

    private static async Task WriteTranslationsAsync(DbConnection conn, DbTransaction tx, ImportReport report, CancellationToken ct)
    {
        var assembly = typeof(Importer).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(static name => name.StartsWith(TranslationPrefix, StringComparison.Ordinal)))
        {
            var language = resource[TranslationPrefix.Length..^".json".Length];
            await using var stream = assembly.GetManifestResourceStream(resource)!;
            var translation = await JsonSerializer.DeserializeAsync<CatalogTranslation>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct)
                ?? throw new InvalidDataException($"{resource} is empty.");
            foreach (var (key, npc) in translation.Npcs ?? [])
            {
                var known = false;
                if (npc.Name is { Length: > 0 } name)
                {
                    known = await conn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO npc_texts (npc_id, language, name) SELECT id, @Language, @Name FROM npcs WHERE key = @Key",
                        new { Language = language, Name = name, Key = key }, tx, cancellationToken: ct)) > 0;
                    report.TranslatedNameRows += known ? 1 : 0;
                }

                var dialog = npc.Dialog ?? [];
                for (var ordinal = 0; ordinal < dialog.Count; ordinal++)
                {
                    var rows = await conn.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO npc_dialog_lines (npc_id, language, ordinal, text)
                        SELECT n.id, @Language, @Ordinal, @Text FROM npcs n
                        WHERE n.key = @Key AND EXISTS (SELECT 1 FROM npc_dialog_lines e WHERE e.npc_id = n.id AND e.ordinal = @Ordinal)
                        """,
                        new { Language = language, Ordinal = ordinal, Text = dialog[ordinal], Key = key }, tx, cancellationToken: ct));
                    known |= rows > 0;
                    report.TranslatedDialogRows += rows;
                }

                Unknown(report, known, language, "NPC", key);
            }

            foreach (var (key, name) in translation.Monsters ?? [])
            {
                var rows = await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_texts (monster_id, language, name) SELECT id, @Language, @Name FROM monsters WHERE key = @Key",
                    new { Language = language, Name = name, Key = key }, tx, cancellationToken: ct));
                report.TranslatedNameRows += rows;
                Unknown(report, rows > 0, language, "monster", key);
            }

            foreach (var (code, name) in translation.Items ?? [])
            {
                var rows = await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO item_texts (definition_id, language, name) SELECT id, @Language, @Name FROM item_definitions WHERE item_code = @Code",
                    new { Language = language, Name = name, Code = code }, tx, cancellationToken: ct));
                report.TranslatedNameRows += rows;
                Unknown(report, rows > 0, language, "item", code);
            }
        }
    }

    private static void Unknown(ImportReport report, bool known, string language, string kind, string key)
    {
        if (!known)
        {
            report.Warnings.Add($"translation {language}: no {kind} '{key}' (or no line of that ordinal) in the catalog");
        }
    }
}
