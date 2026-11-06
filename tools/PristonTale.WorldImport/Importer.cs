using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.WorldImport.Parsers;
using PristonTale.Assets;

namespace PristonTale.WorldImport;

// Orchestrates the idempotent world-data import. Reads the original CP949 config files, resolves cross-references (drop
// and shop item codes to items; monster identities and NPC files to their registry ids, IdentityRegistry), and writes
// the catalog in dependency order: items, monsters, NPCs, fields (the Importer.* partials), then motion timing and the
// source preservation tables. The catalog is replaced whole in one transaction, so a second run gives the same rows.
//
// The production IWorldDataRepository is read-only by design, so the WRITE SQL lives here
// (mirroring DapperCharacterRepository's explicit-column, provider-agnostic style).
//
// Item DWORDs use the original category and ordinal bits of the binary item code, shared with runtime catalog mapping.
public sealed partial class Importer
{
    private readonly IDbConnectionFactory _connections;
    private readonly string _root;
    private readonly string? _assetRoot;
    private readonly List<EntityVariant> _entityVariants = [];
    private IdentityRegistry _monsterIds = null!;
    private IdentityRegistry _npcIds = null!;

    // assetRoot: game assets root (the directory containing assets/…-relative INX paths) for the motion-timing
    // pass. Null = skip the pass (the motion tables stay empty; the world host keeps its fallback timing constants).
    public Importer(IDbConnectionFactory connections, string gameServerRoot, string? assetRoot = null)
    {
        _connections = connections;
        _root = gameServerRoot;
        _assetRoot = assetRoot;
    }

    public async Task<ImportReport> RunAsync(CancellationToken ct = default)
    {
        var report = new ImportReport();
        _entityVariants.Clear();
        _unknownKeys.Clear();
        _monsterIds = IdentityRegistry.Monsters();
        _npcIds = IdentityRegistry.Npcs();

        var items = ParseItems(report);
        var invalidKeys = items
            .Where(static item => !ItemCodes.TryParse(item.Code, out _))
            .Select(static item => item.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (invalidKeys.Length > 0)
        {
            report.Warnings.Add($"Item keys that are not two letters, a letter or digit and two digits are kept and left out of the runtime catalog: {string.Join(", ", invalidKeys)}.");
        }

        var monsters = ParseMonsters(report);
        var npcs = ParseNpcs(report);
        ReportUnknownKeys(report);
        var fields = ParseFields(report);
        TakePlacements(npcs, fields, report);

        // Drops and shops name items in any case ("pm104"); references resolve to the code as its file spells it.
        var itemCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            itemCodes[item.Code] = item.Code;
        }

        var monsterIds = monsters.ToDictionary(static m => m.Name, m => _monsterIds.Resolve(m.Name, m.DisplayName), StringComparer.Ordinal);
        var npcIds = npcs.ToDictionary(static n => n.File, n => _npcIds.Resolve(n.File, n.DisplayName), StringComparer.OrdinalIgnoreCase);
        ReportUnregistered(_monsterIds, report);
        ReportUnregistered(_npcIds, report);

        await using var conn = await _connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await ClearCatalogAsync(conn, tx, ct);
        var footprints = ItemFootprints.Measure(_assetRoot, items.Select(static item => item.Code), report);
        await WriteItemsAsync(conn, tx, items, footprints, report, ct);
        await WriteMonstersAsync(conn, tx, monsters, monsterIds, itemCodes, report, ct);
        await WriteNpcsAsync(conn, tx, npcs, npcIds, itemCodes, report, ct);
        await WriteFieldsAsync(conn, tx, fields, monsterIds, npcIds, report, ct);
        await WriteMotionTimingAsync(conn, tx, monsters, npcs, monsterIds, npcIds, report, ct);
        await WriteSourceFilesAsync(conn, tx, report, ct);
        await WriteEntityVariantsAsync(conn, tx, report, ct);
        await WriteTranslationsAsync(conn, tx, report, ct);

        await tx.CommitAsync(ct);
        return report;
    }

    // The catalog tables, children first; every import writes them whole.
    private static readonly string[] CatalogTables =
    [
        "field_npcs", "field_boss_hours", "field_bosses", "field_monster_spawns", "field_spawn_points", "fields",
        "npc_shop_items", "npc_services", "npc_dialog_lines", "npc_texts", "npcs",
        "monster_bonus_drops", "monster_drop_line_items", "monster_drop_lines", "monster_texts", "monsters",
        "item_texts", "item_spec_bonuses", "item_spec_random_classes", "item_definitions", "items", "item_sets",
        "world_import_unresolved_refs"
    ];

    private static async Task ClearCatalogAsync(DbConnection conn, DbTransaction tx, CancellationToken ct)
    {
        foreach (var table in CatalogTables)
        {
            await conn.ExecuteAsync(new CommandDefinition($"DELETE FROM {table}", transaction: tx, cancellationToken: ct));
        }
    }

    private async Task WriteEntityVariantsAsync(
        DbConnection conn,
        DbTransaction tx,
        ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM world_entity_variants", transaction: tx, cancellationToken: ct));

        foreach (var variant in _entityVariants)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO world_entity_variants (source_path, kind, identity_key, is_catalog_entry, data_json)
                VALUES (@SourcePath, @Kind, @IdentityKey, @IsCatalogEntry, @DataJson)
                """,
                new { variant.SourcePath, variant.Kind, IdentityKey = IdentityKeyOf(variant), variant.IsCatalogEntry, variant.DataJson },
                tx,
                cancellationToken: ct));
            report.EntityVariants++;
        }
    }

    // A monster or NPC definition is listed under its English key; an item under its code; an unkeyed file under none.
    private string IdentityKeyOf(EntityVariant variant) => variant.Kind switch
    {
        "monster" => _monsterIds.TryGet(variant.Identity, out var monster) ? monster.Key : "",
        "npc" => _npcIds.TryGet(variant.Identity, out var npc) ? npc.Key : "",
        _ => variant.Identity
    };

    private static void ReportUnregistered(IdentityRegistry registry, ImportReport report)
    {
        foreach (var entry in registry.Added)
        {
            report.Warnings.Add($"Identities/{registry.FileName} lacks '{entry.Key}': imported as id {entry.Id}; add {IdentityRegistry.Line(entry)}");
        }
    }

    // Definitions hold value-tuple lists (item specials, shop stock); tuples keep their values only as fields.
    private static readonly JsonSerializerOptions VariantJson = new() { IncludeFields = true };

    private EntityVariant CreateVariant(string path, string kind, string identity, bool selected, object value)
    {
        var relativePath = Path.GetRelativePath(_root, path).Replace('\\', '/');
        return new EntityVariant(relativePath, kind, identity, selected, JsonSerializer.Serialize(value, VariantJson));
    }

    // Identity: the item code, the monster's authored identity or the NPC's file (IdentityKeyOf gives its key).
    private sealed record EntityVariant(
        string SourcePath,
        string Kind,
        string Identity,
        bool IsCatalogEntry,
        string DataJson);

    private static async Task RecordUnresolvedAsync(
        DbConnection conn,
        DbTransaction tx,
        string kind,
        string owner,
        string reference,
        object details,
        ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO world_import_unresolved_refs (kind, owner_key, raw_reference, details_json)
            VALUES (@Kind, @Owner, @Reference, @Details)
            """,
            new
            {
                Kind = kind,
                Owner = owner,
                Reference = reference,
                Details = JsonSerializer.Serialize(details),
            },
            tx,
            cancellationToken: ct));
        report.UnresolvedReferenceRows++;
    }

    private async Task WriteSourceFilesAsync(
        DbConnection conn,
        DbTransaction tx,
        ImportReport report,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM world_source_files", transaction: tx, cancellationToken: ct));

        var paths = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var relativePath = Path.GetRelativePath(_root, path).Replace('\\', '/');
            var fields = ReadSourceFields(path, bytes);
            var kind = relativePath.Split('/')[0].ToLowerInvariant();

            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO world_source_files (source_path, kind, sha256, content_base64, fields_json, field_count)
                VALUES (@Path, @Kind, @Sha256, @Content, @Fields, @FieldCount)
                """,
                new
                {
                    Path = relativePath,
                    Kind = kind,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    Content = Convert.ToBase64String(bytes),
                    Fields = JsonSerializer.Serialize(fields),
                    FieldCount = fields.Count,
                },
                tx,
                cancellationToken: ct));

            report.SourceFiles++;
            report.SourceFields += fields.Count;
        }
    }

    private static IReadOnlyList<SourceField> ReadSourceFields(string path, byte[] bytes)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".txt" or ".inf" or ".inf_" or ".npc" or ".spm" or ".zhoon"))
        {
            return [];
        }

        var lines = Cp949.Lines(bytes);
        var fields = new List<SourceField>();

        for (var index = 0; index < lines.Length; index++)
        {
            var tokenizer = new Tokenizer(lines[index]);
            var key = tokenizer.NextWord();
            if (!key.StartsWith('*'))
            {
                continue;
            }

            fields.Add(new SourceField(index + 1, key, tokenizer.RemainingValues()));
        }

        return fields;
    }

    private sealed record SourceField(int Line, string Key, IReadOnlyList<string> Values);

    // Extract each distinct monster and NPC model's INX motion table + the player class body tables (via the
    // shared CharacterClassRegistry visuals) and write motion_models /
    // motion_slots. Idempotent: models upsert; each table's slots are delete-reinserted. Skipped
    // (with a warning) when no assets root was supplied. Unresolvable models are reported, not fatal —
    // the world host falls back to its default timing constants for them.
    private async Task WriteMotionTimingAsync(
        DbConnection conn,
        DbTransaction tx,
        IReadOnlyList<ParsedMonster> monsters,
        IReadOnlyList<ParsedNpc> npcs,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> npcIds,
        ImportReport report, CancellationToken ct)
    {
        if (_assetRoot is null)
        {
            report.Warnings.Add("motion pass skipped: no assets root (pass --assets <dir> to import INX motion timing)");
            return;
        }

        var extractor = new MotionTableExtractor(_assetRoot);

        foreach (var m in monsters)
        {
            if (!extractor.TryExtract(m.Model, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: {monsterIds[m.Name].Key}: {failure}");
            }
        }

        foreach (var npc in npcs)
        {
            if (!extractor.TryExtract(npc.Model, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: NPC {npcIds[npc.File].Key}: {failure}");
            }
        }

        // Player class bodies — the same shared registry the client/server compose characters from.
        var registry = new CharacterClassRegistry();
        foreach (var cls in registry.All)
        {
            var body = cls.Definition.DefaultVisuals.Body.InxPath;
            if (!extractor.TryExtract(body, out var failure))
            {
                report.MotionUnresolvedModels++;
                report.Warnings.Add($"motion: class {cls.Name}: {failure}");
            }
        }

        foreach (var (modelKey, tableKey) in extractor.Models)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO motion_models (model_key, motion_table_key) VALUES (@ModelKey, @TableKey)
                ON CONFLICT (model_key) DO UPDATE SET motion_table_key = excluded.motion_table_key;
                """,
                new { ModelKey = modelKey, TableKey = tableKey }, tx, cancellationToken: ct));
            report.MotionModelRows++;
        }

        foreach (var (tableKey, slots) in extractor.Tables)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM motion_slots WHERE motion_table_key = @TableKey",
                new { TableKey = tableKey }, tx, cancellationToken: ct));

            foreach (var s in slots)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO motion_slots (
                        motion_table_key, slot, state, start_frame, end_frame,
                        event_frame0, event_frame1, event_frame2, event_frame3, repeat, key_code)
                    VALUES (@TableKey, @Slot, @State, @StartFrame, @EndFrame, @E0, @E1, @E2, @E3, @Repeat, @KeyCode)
                    """,
                    new
                    {
                        TableKey = tableKey, s.Slot, s.State, s.StartFrame, s.EndFrame,
                        E0 = s.Event0, E1 = s.Event1, E2 = s.Event2, E3 = s.Event3, s.Repeat, s.KeyCode,
                    }, tx, cancellationToken: ct));
                report.MotionSlotRows++;
            }

            report.MotionTableRows++;
        }
    }

    // ---- Parsing ------------------------------------------------------------------------------------

    private IReadOnlyList<ParsedItem> ParseItems(ImportReport report)
    {
        var parser = new ItemTxtParser();
        var parsed = new List<(string Path, ParsedItem Item)>();

        foreach (var path in EnumerateFiles("OpenItem", "*.txt"))
        {
            report.ItemFilesParsed++;
            ParsedItem item;
            try
            {
                item = parser.Parse(path);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"item parse failed {Path.GetFileName(path)}: {ex.Message}");
                continue;
            }

            parsed.Add((path, item));
            NoteUnknownKeys("item", path, item.UnknownKeys);
        }

        var winners = parsed
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Item.Code))
            .GroupBy(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static entry => string.Equals(
                    Path.GetFileNameWithoutExtension(entry.Path),
                    entry.Item.Code,
                    StringComparison.OrdinalIgnoreCase))
                .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
                .First())
            .ToDictionary(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase);

        var result = new List<ParsedItem>(winners.Count);
        foreach (var (path, item) in parsed)
        {
            var selected = item.Code.Length > 0
                && winners.TryGetValue(item.Code, out var winner)
                && string.Equals(path, winner.Path, StringComparison.Ordinal);

            _entityVariants.Add(CreateVariant(path, "item", item.Code, selected, item));

            if (string.IsNullOrWhiteSpace(item.Code))
            {
                report.Warnings.Add($"item {Path.GetFileName(path)} has no *코드 — retained as an unkeyed variant");
                continue;
            }

            if (!selected)
            {
                if (item.UniqueItem != NamedUnique)
                {
                    report.Warnings.Add($"item variant '{item.Code}' ({Path.GetFileName(path)}) — retained outside the catalog");
                }

                continue;
            }

            result.Add(item);
        }

        // The quest version of a code (unique marker 2): the first file in path order.
        _questVariants.Clear();
        foreach (var group in parsed.Where(static entry => entry.Item.Code.Length > 0 && entry.Item.UniqueItem == ItemDefinition.QuestUnique)
                     .GroupBy(static entry => entry.Item.Code, StringComparer.OrdinalIgnoreCase))
        {
            _questVariants.Add(group.OrderBy(static entry => entry.Path, StringComparer.Ordinal).First().Item);
        }

        // Named uniques (unique marker 1) of a code that lost the selection: one per distinct name, the first in path order,
        // numbered by the registry.
        _namedUniques.Clear();
        var uniques = NamedUniqueRegistry.Load();
        foreach (var entry in parsed.Where(entry => entry.Item.Code.Length > 0 && entry.Item.UniqueItem == NamedUnique
                         && !string.Equals(winners[entry.Item.Code].Path, entry.Path, StringComparison.Ordinal))
                     .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
                     .DistinctBy(static entry => (entry.Item.Code.ToUpperInvariant(), entry.Item.Name)))
        {
            _namedUniques.Add((uniques.Resolve(entry.Item.Code, entry.Item.Name), entry.Item));
        }

        foreach (var added in uniques.Added)
        {
            report.Warnings.Add($"Identities/{NamedUniqueRegistry.FileName} lacks {added.Code} '{added.Name}': imported as number {added.Number}; add {IdentityRegistry.Line(added)}");
        }

        foreach (var unmet in uniques.Unmet)
        {
            report.Warnings.Add($"Identities/{NamedUniqueRegistry.FileName} lists {unmet.Code} '{unmet.Name}', which no file defines as a named unique; saved copies of it no longer load");
        }

        return result;
    }

    // The authored *유니크 value of a named unique.
    private const long NamedUnique = 1;
    private readonly List<(int Variant, ParsedItem Item)> _namedUniques = [];
    private readonly List<ParsedItem> _questVariants = [];

    // Keys no rule reads, by source kind: how many files carry each and the first one.
    private readonly SortedDictionary<string, (int Files, string First)> _unknownKeys = new(StringComparer.Ordinal);

    private void NoteUnknownKeys(string kind, string path, IEnumerable<string> keys)
    {
        // Comment lines and stray text are not keys.
        foreach (var key in keys.Where(static key => key.StartsWith('*')).Distinct(StringComparer.Ordinal))
        {
            var id = $"{kind} {key}";
            _unknownKeys[id] = _unknownKeys.TryGetValue(id, out var seen) ? (seen.Files + 1, seen.First) : (1, Path.GetFileName(path));
        }
    }

    private void ReportUnknownKeys(ImportReport report)
    {
        foreach (var (id, (files, first)) in _unknownKeys)
        {
            report.Warnings.Add($"unknown key {id}: {files} file(s), first {first} — not imported");
        }
    }

    private IReadOnlyList<ParsedMonster> ParseMonsters(ImportReport report)
    {
        var parser = new MonsterInfParser();
        var result = new List<ParsedMonster>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateFiles("Monster", "*.inf"))
        {
            report.MonsterFilesParsed++;
            ParsedMonster m;
            try { m = parser.Parse(path); }
            catch (Exception ex) { report.Warnings.Add($"monster parse failed {Path.GetFileName(path)}: {ex.Message}"); continue; }
            NoteUnknownKeys("monster", path, m.UnknownKeys);
            var selected = !string.IsNullOrWhiteSpace(m.Name) && seen.Add(m.Name);
            _entityVariants.Add(CreateVariant(path, "monster", m.Name, selected, m));

            if (string.IsNullOrWhiteSpace(m.Name))
            {
                report.Warnings.Add($"monster {Path.GetFileName(path)} has no *이름 — retained as an unkeyed variant");
                continue;
            }
            if (!selected)
            {
                report.Warnings.Add($"monster variant '{m.DisplayName}' ({Path.GetFileName(path)}) — retained outside the catalog");
                continue;
            }
            if (string.IsNullOrWhiteSpace(m.DisplayName))
            {
                report.Warnings.Add($"monster {Path.GetFileName(path)} has no English name; its key is shown");
            }
            result.Add(m);
        }
        return result;
    }

    // Every NPC definition file is an NPC, as the original opens each placement from its own file.
    private IReadOnlyList<ParsedNpc> ParseNpcs(ImportReport report)
    {
        var parser = new NpcParser();
        var npcs = new List<ParsedNpc>();
        foreach (var path in EnumerateFiles("NPC", "*.npc"))
        {
            report.NpcFilesParsed++;
            try
            {
                var npc = parser.Parse(path);
                npcs.Add(npc);
                NoteUnknownKeys("npc", path, npc.UnknownKeys);
                _entityVariants.Add(CreateVariant(path, "npc", npc.File, true, npc));
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"npc parse failed {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        foreach (var npc in NpcParser.ShareNames(npcs))
        {
            report.Warnings.Add($"npc {npc.File} has no English name in any definition; its key is shown");
        }

        foreach (var npc in npcs.Where(static npc => npc.UntranslatedDialog.Count > 0))
        {
            report.Warnings.Add($"npc {npc.File}: {npc.UntranslatedDialog.Distinct(StringComparer.Ordinal).Count()} *대화 line(s) without an English translation (EnglishNames.Dialog) are not shown");
        }

        return npcs;
    }

    // A file sets the class and size it lacks from its placement; placements of one file that disagree on a value it
    // lacks give the first in stage and record order.
    private static void TakePlacements(IReadOnlyList<ParsedNpc> npcs, IReadOnlyList<ParsedField> fields, ImportReport report)
    {
        var placements = fields.SelectMany(static field => field.FixedNpcs).ToLookup(static placement => placement.SourceFile, StringComparer.OrdinalIgnoreCase);
        foreach (var npc in npcs)
        {
            var placed = placements[npc.File].ToList();
            if (placed.Count == 0)
            {
                continue;
            }

            var first = placed[0];
            if ((npc.Class is null && placed.Any(placement => placement.Class != first.Class))
                || (npc.SizeLevel is null && placed.Any(placement => placement.SizeLevel != first.SizeLevel)))
            {
                report.Warnings.Add($"npc {npc.File}: its placements give different classes or sizes it lacks; the first is used");
            }

            NpcParser.TakePlacement(npc, first.Class, first.SizeLevel);
        }
    }

    // Every stage with spawn files: the world catalog's fields (their stage is the stem of the stage model), then the
    // stages no field uses, kept without a field code.
    private IReadOnlyList<ParsedField> ParseFields(ImportReport report)
    {
        var codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in new WorldCatalog().AllFields)
        {
            var stage = AseBaseOf(def.StageSmd);
            if (stage.Length > 0 && !codes.TryAdd(stage, def.FieldCode))
            {
                report.Warnings.Add($"fields {codes[stage]} and {def.FieldCode} share the stage {stage}; the spawn data goes to the first");
            }
        }

        var stages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = ResolveDir("Field");
        if (dir is not null)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", CaseInsensitive))
            {
                if (Path.GetExtension(file).ToLowerInvariant() is ".spm" or ".spp" or ".spc")
                {
                    stages.Add(StageOf(file));
                }
            }
        }

        var spm = new SpmParser();
        var spp = new SppReader();
        var spc = new SpcReader();
        var result = new List<ParsedField>();
        foreach (var stage in stages)
        {
            var field = new ParsedField { AseBase = stage, FieldCode = codes.TryGetValue(stage, out var code) ? code : null };
            if (FindFieldFile(stage, "spm") is { } spmPath)
            {
                spm.Parse(spmPath, field);
            }

            if (FindFieldFile(stage, "spp") is { } sppPath)
            {
                spp.Read(sppPath, field);
            }

            if (FindFieldFile(stage, "spc") is { } spcPath)
            {
                spc.Read(spcPath, field);
            }

            result.Add(field);
        }

        return result;
    }

    // The stage of a spawn file: "<stage>.<ext>" or "<stage>.ase.<ext>".
    private static string StageOf(string file)
    {
        var stage = Path.GetFileNameWithoutExtension(file);
        return stage.EndsWith(".ase", StringComparison.OrdinalIgnoreCase) ? stage[..^4] : stage;
    }

    // ---- File helpers -------------------------------------------------------------------------------

    // Glob case-insensitively: the original data mixes extension casing (e.g. NPC has both ".npc" and
    // ".NPC"; Monster has a couple of ".INF"). On a case-sensitive filesystem a plain "*.npc" pattern
    // silently drops the upper-case files, so we always match case-insensitively.
    private static readonly EnumerationOptions CaseInsensitive =
        new() { MatchCasing = MatchCasing.CaseInsensitive };

    private IEnumerable<string> EnumerateFiles(string subfolder, string pattern)
    {
        var dir = ResolveDir(subfolder);
        return dir is null
            ? []
            : Directory.EnumerateFiles(dir, pattern, CaseInsensitive).OrderBy(static p => p, StringComparer.Ordinal);
    }

    // Resolve a subfolder case-insensitively under the GameServer root.
    private string? ResolveDir(string name)
    {
        var resolved = AssetFiles.Resolve(Path.Combine(_root, name));
        return Directory.Exists(resolved) ? resolved : null;
    }

    // The field .spm/.spp/.spc are named "<aseBase>.<ext>" or "<aseBase>.ase.<ext>" (case-insensitive).
    private string? FindFieldFile(string aseBase, string ext)
    {
        var dir = ResolveDir("Field");
        if (dir is null)
            return null;
        foreach (var f in Directory.EnumerateFiles(dir, "*." + ext, CaseInsensitive))
        {
            if (string.Equals(StageOf(f), aseBase, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    // Derive the ASE base name from a FieldDef.StageSmd path like "Field\\desert\\De-1.smd" → "De-1".
    private static string AseBaseOf(string stageSmd)
    {
        if (string.IsNullOrEmpty(stageSmd))
            return "";
        var norm = stageSmd.Replace('\\', '/');
        var name = norm[(norm.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
