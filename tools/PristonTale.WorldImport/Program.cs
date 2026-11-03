using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;
using PristonTale.WorldImport;
using PristonTale.Assets;

Cp949.Encoding.GetType(); // force CP949 provider registration up front
// The report names Korean keys and texts.
Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = new List<string>(args);
if (argv.Count == 0)
{
    PrintUsage();
    return 1;
}
var command = argv[0].ToLowerInvariant();
argv.RemoveAt(0);

var sqlitePath = TakeOption(argv, "--sqlite");
var pgConn = TakeOption(argv, "--postgres");
var sourceRoot = TakeOption(argv, "--source") ?? DefaultSourceRoot();
var assetsRoot = TakeOption(argv, "--assets") ?? AssetFiles.FindAssetsRoot();

if (command == "export-item-names")
{
    ItemNameExport.Write(sourceRoot, TakeOption(argv, "--output") ?? throw new ArgumentException("--output required."));
    return 0;
}

if (command == "export-npc-placements")
{
    NpcPlacementExport.Write(sourceRoot, TakeOption(argv, "--output") ?? throw new ArgumentException("--output required."));
    return 0;
}

if (command == "export-npc-motions")
{
    var outputPath = TakeOption(argv, "--output")
        ?? throw new ArgumentException("export-npc-motions requires --output.");
    var requiredAssetsRoot = assetsRoot
        ?? throw new DirectoryNotFoundException("export-npc-motions requires an assets root.");
    NpcMotionExport.Write(sourceRoot, requiredAssetsRoot, outputPath);
    return 0;
}

if (command == "export-monster-names")
{
    var outputPath = TakeOption(argv, "--output")
        ?? throw new ArgumentException("export-monster-names requires --output.");
    MonsterNameExport.Write(sourceRoot, outputPath);
    return 0;
}

if (sqlitePath is null && pgConn is null)
    sqlitePath = DefaultSqlitePath(); // default to a dev SQLite file

var services = new ServiceCollection();
services.AddLogging();
if (pgConn is not null)
    services.AddPristonPostgres(pgConn);
else
    services.AddPristonSqlite(sqlitePath!);

await using var provider = services.BuildServiceProvider();
var migrator = provider.GetRequiredService<IDatabaseMigrator>();
var connections = provider.GetRequiredService<IDbConnectionFactory>();

var target = pgConn is not null ? $"postgres" : $"sqlite {sqlitePath}";
Console.WriteLine($"ptworld {command} → {target}");
Console.WriteLine($"source: {sourceRoot}");

try
{
    await migrator.MigrateAsync();

    switch (command)
    {
        case "import":
            if (!Directory.Exists(sourceRoot))
            {
                Console.Error.WriteLine($"source root not found: {sourceRoot}");
                return 1;
            }
            Console.WriteLine($"assets: {assetsRoot ?? "(none — motion pass skipped)"}");
            var importer = new Importer(connections, sourceRoot, assetsRoot);
            var report = await importer.RunAsync();
            PrintReport(report);
            await PrintRowCounts(connections);
            return 0;

        case "verify":
            await PrintRowCounts(connections);
            await PrintCrossRefCheck(connections);
            return 0;

        default:
            Console.Error.WriteLine($"unknown command '{command}'");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

// ---- output -----------------------------------------------------------------------------------------

static void PrintReport(ImportReport r)
{
    Console.WriteLine();
    Console.WriteLine("=== import reconciliation ===");
    Console.WriteLine($"source:   {r.SourceFiles,5} files, {r.SourceFields} authored fields preserved");
    Console.WriteLine($"variants: {r.EntityVariants,5} parsed entity definitions preserved");
    Console.WriteLine($"unresolved references retained: {r.UnresolvedReferenceRows}");
    Console.WriteLine($"items:    {r.ItemRows,5} rows from {r.ItemFilesParsed} files  (special {r.ItemSpecialRows}, job-random {r.ItemJobRandomRows})");
    Console.WriteLine($"monsters: {r.MonsterRows,5} rows from {r.MonsterFilesParsed} files  (drop lines {r.MonsterDropLineRows}, line items {r.MonsterDropItemRows}, bonus {r.MonsterDropPlusRows})");
    Console.WriteLine($"npcs:     {r.NpcRows,5} rows from {r.NpcFilesParsed} files  (shop {r.NpcShopRows}, services {r.NpcServiceRows}, with dialog {r.NpcDialogRows}, dialog lines {r.DialogLineRows})");
    Console.WriteLine($"fields:   {r.FieldRows,5} rows  (points {r.SpawnPointRows}, mon-spawns {r.MonsterSpawnRows}, bosses {r.BossSpawnRows}, fixed-npc {r.FixedNpcRows})");
    Console.WriteLine($"motion:   {r.MotionModelRows,5} models → {r.MotionTableRows} tables ({r.MotionSlotRows} clip rows; unresolved {r.MotionUnresolvedModels})");
    Console.WriteLine();
    Console.WriteLine("=== cross-ref resolution (resolved / dangling) ===");
    Console.WriteLine($"drop item codes : {r.DropItemCodesResolved} / {r.DropItemCodesDangling}");
    Console.WriteLine($"shop item codes : {r.ShopItemCodesResolved} / {r.ShopItemCodesDangling}");
    Console.WriteLine($"spawn monsters  : {r.SpawnMonsterRefsResolved} / {r.SpawnMonsterRefsDangling}");
    Console.WriteLine($"boss monsters   : {r.BossMonsterRefsResolved} / {r.BossMonsterRefsDangling}");
    Console.WriteLine($"fixed npcs      : {r.FixedNpcRefsResolved} / {r.FixedNpcRefsDangling}");
    if (r.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {r.Warnings.Count} warning(s) ===");
        foreach (var w in r.Warnings)
            Console.WriteLine($"  - {w}");
    }
}

static async Task PrintRowCounts(IDbConnectionFactory connections)
{
    string[] tables =
    [
        "languages", "items", "item_definitions", "item_texts", "item_spec_bonuses", "item_spec_random_classes", "item_sets",
        "monsters", "monster_texts", "monster_drop_lines", "monster_drop_line_items", "monster_bonus_drops",
        "npcs", "npc_texts", "npc_dialog_lines", "npc_shop_items", "npc_services",
        "fields", "field_spawn_points", "field_monster_spawns", "field_bosses", "field_boss_hours", "field_npcs",
        "world_source_files",
        "world_entity_variants",
        "world_import_unresolved_refs",
    ];
    await using var conn = await connections.OpenAsync();
    Console.WriteLine();
    Console.WriteLine("=== table row counts ===");
    foreach (var t in tables)
    {
        var n = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {t}");
        Console.WriteLine($"  {t,-28} {n,7}");
    }
}

static async Task PrintCrossRefCheck(IDbConnectionFactory connections)
{
    await using var conn = await connections.OpenAsync();
    Console.WriteLine();
    Console.WriteLine("=== persisted cross-ref integrity ===");
    var orphanDrops = await conn.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM monster_drop_line_items d WHERE d.item_code IS NOT NULL " +
        "AND NOT EXISTS (SELECT 1 FROM items i WHERE i.code = d.item_code)");
    var orphanShop = await conn.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM npc_shop_items s " +
        "WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.code = s.item_code)");
    var orphanSpawn = await conn.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM field_monster_spawns f " +
        "WHERE NOT EXISTS (SELECT 1 FROM monsters m WHERE m.id = f.monster_id)");
    Console.WriteLine($"  orphan drop item_code rows : {orphanDrops}");
    Console.WriteLine($"  orphan shop item_code rows : {orphanShop}");
    Console.WriteLine($"  orphan spawn monster rows  : {orphanSpawn}");

    Console.WriteLine();
    Console.WriteLine("=== npc services by kind (rows / npcs) ===");
    var kinds = await conn.QueryAsync<(long Kind, long Rows, long Npcs)>(
        "SELECT kind, COUNT(*), COUNT(DISTINCT npc_id) FROM npc_services GROUP BY kind ORDER BY kind");
    foreach (var (kind, rows, npcs) in kinds)
    {
        Console.WriteLine($"  {(PristonTale.Simulation.World.Npcs.NpcServiceKind)kind,-14} {rows,5} / {npcs,5}");
    }

    var talking = await conn.ExecuteScalarAsync<long>("SELECT COUNT(DISTINCT npc_id) FROM npc_dialog_lines");
    var named = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM npc_texts");
    Console.WriteLine($"  npcs with dialog           : {talking}");
    Console.WriteLine($"  npcs with a display name   : {named}");
}

// ---- helpers ----------------------------------------------------------------------------------------

static string? TakeOption(List<string> a, string name)
{
    var i = a.IndexOf(name);
    if (i < 0 || i + 1 >= a.Count)
        return null;
    var value = a[i + 1];
    a.RemoveRange(i, 2);
    return value;
}

// Default dev SQLite path: a throwaway file next to the working dir.
static string DefaultSqlitePath() =>
    Path.Combine(Path.GetTempPath(), "pristontale-world.db");

static string DefaultSourceRoot()
{
    var workingDirectoryRoot = Path.GetFullPath("GameServer");
    var workingDirectoryAssetsRoot = Path.GetFullPath(Path.Combine("assets", "GameServer"));
    if (Directory.Exists(workingDirectoryAssetsRoot))
    {
        return workingDirectoryAssetsRoot;
    }

    if (Directory.Exists(workingDirectoryRoot))
    {
        return workingDirectoryRoot;
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var assetsCandidate = Path.Combine(dir.FullName, "assets", "GameServer");
        if (Directory.Exists(assetsCandidate))
        {
            return assetsCandidate;
        }

        var candidate = Path.Combine(dir.FullName, "GameServer");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return workingDirectoryRoot;
}



static void PrintUsage()
{
    Console.WriteLine("""
        ptworld — apply the baseline and import the original CP949 world configuration.

        Commands:
          import [options]    parse the config files and idempotently upsert all world data
          verify [options]    print table row counts + persisted cross-ref integrity
          export-monster-names --source <dir> --output <path>
                              export authored *Name labels for migration; no database access
          export-npc-motions --source <dir> --assets <dir> --output <path>
                              export exact NPC INX clips for existing-database repair
          export-item-names --source <dir> --output <path>
                              export item codes and names; no database access
          export-npc-placements --source <dir> --output <path>
                              export the fixed NPC placements of every field; no database access

        Options:
          --sqlite <path>     use a SQLite DB at <path> (default: %TEMP%/pristontale-world.db)
          --postgres <conn>   use a Postgres DB (Npgsql connection string)
          --source <dir>      GameServer config root (default: assets/GameServer/ or GameServer/
                              or an ancestor of the executable)
          --assets <dir>      game assets root for the INX motion-timing pass (default: nearest
                              ancestor assets/; motion pass skipped when absent)

        Examples:
          dotnet run --project tools/PristonTale.WorldImport -- import --sqlite /tmp/world.db
          dotnet run --project tools/PristonTale.WorldImport -- verify --sqlite /tmp/world.db
        """);
}
