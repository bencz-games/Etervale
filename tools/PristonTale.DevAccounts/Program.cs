using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World;
using PristonTale.Assets;

// Dev CLI to seed accounts + characters into the same database as the online server.
// Reuses the real persistence layer (PasswordHasher, repositories, CharacterRecordFactory) so seeded data
// is identical to what the server would create. The schema is ensured idempotently via DatabaseMigrator.

const string DefaultSqlitePath = "data/pristontale.db";

var argv = new List<string>(args);
var requestedConnection = TakeOption(argv, "--conn");
var requestedSqlitePath = TakeOption(argv, "--sqlite");
var connection = requestedConnection ?? (requestedSqlitePath is null ? Environment.GetEnvironmentVariable("PT_PG_CONN") : null);
var sqlitePath = requestedSqlitePath ?? DefaultSqlitePath;
if (requestedConnection is not null && requestedSqlitePath is not null)
{
    Console.Error.WriteLine("Use either --conn or --sqlite.");
    return 1;
}

if (argv.Count == 0)
{
    PrintUsage();
    return 1;
}
var command = argv[0].ToLowerInvariant();

var services = new ServiceCollection();
services.AddLogging(); // no provider — the migrator's ILogger is a no-op; results/errors print to console
if (connection is not null)
{
    services.AddPristonPostgres(connection);
}
else
{
    services.AddPristonSqlite(DataPath.Resolve(sqlitePath));
}
services.AddSingleton<CharacterClassRegistry>();
await using var provider = services.BuildServiceProvider();

var migrator = provider.GetRequiredService<IDatabaseMigrator>();
var accounts = provider.GetRequiredService<IAccountRepository>();
var characters = provider.GetRequiredService<ICharacterRepository>();
var classes = provider.GetRequiredService<CharacterClassRegistry>();
var gameMasters = provider.GetRequiredService<IGameMasterRepository>();

try
{
    await migrator.MigrateAsync(); // idempotent: ensures the schema exists even if the server never ran
    return command switch
    {
        "create-account" => await CreateAccount(argv),
        "create-bots" => await CreateBots(argv),
        "add-char" => await AddCharacter(argv),
        "list" => await ListCharacters(argv),
        "set-gm" => await SetGameMaster(argv),
        "gm-audit" => await ShowAudit(argv),
        _ => Usage($"unknown command '{command}'"),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine($"(database: {(connection is null ? sqlitePath : "Postgres")})");
    return 1;
}

async Task<int> CreateAccount(List<string> a)
{
    if (a.Count < 3)
        return Usage("usage: create-account <user> <password>");
    var (user, password) = (a[1], a[2]);

    if (await accounts.FindByUserIdAsync(user) is not null)
    {
        Console.WriteLine($"account '{user}' already exists — leaving it unchanged");
        return 0;
    }
    var account = await accounts.CreateAsync(user, PasswordHasher.Hash(password));
    Console.WriteLine($"created account #{account.Id} '{account.UserId}'");
    return 0;
}

async Task<int> CreateBots(List<string> a)
{
    // Optional placed characters (one per account, named Lt<prefix><index>), for load tests that hunt in a field.
    var classText = TakeOption(a, "--class");
    var levelText = TakeOption(a, "--level");
    var fieldText = TakeOption(a, "--field");
    var atText = TakeOption(a, "--at");
    var spacingText = TakeOption(a, "--spacing");
    var skillText = TakeOption(a, "--skills");
    if (a.Count < 4 || !int.TryParse(a[2], out var count) || count is < 1 or > 100_000)
        return Usage("usage: create-bots <prefix> <count> <password> [--class <class> --field <code> --at <x,z> [--spacing <units>] [--level <n>] [--skills <hex:rank,...>]]");
    var (prefix, password) = (a[1], a[3]);
    var placed = classText is not null;
    var playerClass = PlayerClass.Fighter;
    if (placed && (!TryParseClass(classText!, out playerClass) || fieldText is null || atText is null))
        return Usage("placed bots need --class, --field and --at");
    var level = int.TryParse(levelText, out var parsedLevel) ? Math.Max(1, parsedLevel) : 1;
    var field = int.TryParse(fieldText, out var parsedField) ? parsedField : FieldCodes.Ricarten;
    var at = (atText ?? "0,0").Split(',').Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    // Spacing 0 packs every bot into one 500-unit square (a crowd); otherwise a grid with that spacing.
    var spacing = float.TryParse(spacingText, System.Globalization.CultureInfo.InvariantCulture, out var parsedSpacing) ? parsedSpacing : 0f;
    var skills = (skillText ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(entry => entry.Split(':'))
        .Select(pair => (Code: Convert.ToUInt32(pair[0], 16), Rank: int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture)))
        .ToArray();
    var hash = PasswordHasher.Hash(password);
    var columns = (int)MathF.Ceiling(MathF.Sqrt(count));
    var random = new Random(17);
    var created = 0;
    var charactersCreated = 0;
    for (var index = 1; index <= count; index++)
    {
        var user = $"{prefix}{index:D4}";
        var account = await accounts.FindByUserIdAsync(user);
        if (account is null)
        {
            account = await accounts.CreateAsync(user, hash);
            created++;
        }

        var name = $"Lt{prefix}{index:D4}";
        if (!CharacterNames.IsValidLength(name))
            return Usage($"bot name '{name}' is not {CharacterNames.MinLength}–{CharacterNames.MaxLength} characters; use a shorter prefix");
        if (!placed || (await characters.ListByAccountAsync(account.Id)).Count > 0 || await characters.NameExistsAsync(name))
            continue;
        var position = spacing <= 0f
            ? new System.Numerics.Vector3(at[0] + (float)(random.NextDouble() * 500 - 250), 0f, at[1] + (float)(random.NextDouble() * 500 - 250))
            : new System.Numerics.Vector3(at[0] + ((index - 1) % columns - columns / 2) * spacing, 0f, at[1] + ((index - 1) / columns - columns / 2) * spacing);
        var character = classes.CreatePlayer(playerClass, name, level);
        foreach (var (code, rank) in skills)
            character.Skills.Restore(code, rank);
        var starter = CharacterRecordFactory.NewStarter(classes, account.Id, name, 0, playerClass, field, level: level);
        await characters.CreateAsync(CharacterRecordFactory.FromLiveSession(starter, character, field, position));
        charactersCreated++;
    }

    Console.WriteLine($"created {created} of {count} load-test accounts '{prefix}0001'..'{prefix}{count:D4}' and {charactersCreated} placed characters");
    return 0;
}

async Task<int> AddCharacter(List<string> a)
{
    if (a.Count < 4)
        return Usage($"usage: add-char <user> <name> <class> [level]   (class: {ClassList()})");
    var (user, name, classText) = (a[1], a[2], a[3]);
    if (!TryParseClass(classText, out var playerClass))
        return Usage($"unknown class '{classText}' (valid: {ClassList()})");
    var level = a.Count > 4 && int.TryParse(a[4], out var parsed) ? Math.Max(1, parsed) : 1;

    var account = await accounts.FindByUserIdAsync(user);
    if (account is null)
        return Usage($"no account '{user}' — run create-account first");
    if (!CharacterNames.IsValidLength(name))
        return Usage($"character name must be {CharacterNames.MinLength}–{CharacterNames.MaxLength} characters");
    if (await characters.NameExistsAsync(name))
        return Usage($"character name '{name}' is already taken");

    if (CharacterSlots.FirstFree(await characters.UsedSlotsAsync(account.Id)) is not { } slot)
        return Usage($"account '{user}' has no free character slots (max {CharacterSlots.Maximum})");

    var record = CharacterRecordFactory.NewStarter(classes, account.Id, name, slot, playerClass, PristonTale.Simulation.Progression.DeathPenalty.TownField(playerClass), level: level);
    var created = await characters.CreateAsync(record);
    Console.WriteLine($"created character #{created.Id} '{created.Name}' ({playerClass}, level {created.Level}) in slot {slot} of '{user}'");
    return 0;
}

async Task<int> ListCharacters(List<string> a)
{
    if (a.Count < 2)
        return Usage("usage: list <user>");
    var account = await accounts.FindByUserIdAsync(a[1]);
    if (account is null)
        return Usage($"no account '{a[1]}'");

    var rows = await characters.ListByAccountAsync(account.Id);
    Console.WriteLine($"account '{account.UserId}' (#{account.Id}) — {rows.Count} character(s):");
    foreach (var r in rows)
        Console.WriteLine($"  slot {r.Slot}: {r.Name,-16} {(PlayerClass)r.JobCode,-10} Lv {r.Level,-3} " +
                          $"str {r.Strength} spr {r.Spirit} tal {r.Talent} agi {r.Agility} hea {r.Health}");
    return 0;
}

// Game-master levels: 0 player, 1 guide, 2 moderator, 3 operator, 4 administrator (see docs/chat-and-gm.md).
async Task<int> SetGameMaster(List<string> a)
{
    if (a.Count < 3 || !int.TryParse(a[2], out var level) || level is < 0 or > 4)
        return Usage("usage: set-gm <user> <level 0-4>");
    if (!await gameMasters.SetGmLevelAsync(a[1], level))
        return Usage($"no account '{a[1]}'");
    Console.WriteLine($"account '{a[1]}' now has game-master level {level} (applies at its next login)");
    return 0;
}

async Task<int> ShowAudit(List<string> a)
{
    var count = a.Count > 1 && int.TryParse(a[1], out var parsed) ? Math.Clamp(parsed, 1, 1000) : 20;
    foreach (var entry in await gameMasters.RecentAuditAsync(count))
        Console.WriteLine($"{entry.CreatedAt:u} {entry.UserId}/{entry.CharacterName} (level {entry.GmLevel}, needs {entry.RequiredLevel}) " +
                          $"{(entry.Accepted ? "ran" : "denied")}: {entry.Command} -> {entry.Result}");
    return 0;
}

// --- helpers -------------------------------------------------------------------------------------

static bool TryParseClass(string text, out PlayerClass playerClass)
{
    if (Enum.TryParse(text, ignoreCase: true, out playerClass) && Enum.IsDefined(playerClass))
        return true;
    if (int.TryParse(text, out var id) && Enum.IsDefined((PlayerClass)id))
    {
        playerClass = (PlayerClass)id;
        return true;
    }
    playerClass = default;
    return false;
}

static string ClassList() => string.Join(", ", Enum.GetValues<PlayerClass>().Select(c => $"{(int)c}={c}"));

// Extract `--name value` from the arg list (mutating it), returning the value or null.
static string? TakeOption(List<string> a, string name)
{
    var i = a.IndexOf(name);
    if (i < 0 || i + 1 >= a.Count)
        return null;
    var value = a[i + 1];
    a.RemoveRange(i, 2);
    return value;
}

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        ptdevaccounts — seed dev accounts/characters into the online server database.

        Commands:
          create-account <user> <password>            create a login account (hashed)
          create-bots <prefix> <count> <password>     create load-test accounts <prefix>0001..<prefix>NNNN
              [--class C --field F --at x,z]            ... each with a character Lt<prefix>NNNN placed in field F around (x, z)
              [--spacing S] [--level N]                 grid spacing (0: a 500-unit crowd) and level
              [--skills hex:rank,...]                   learned skills
          add-char <user> <name> <class> [level]      seed a character on an account
          list <user>                                 list an account's characters
          set-gm <user> <level>                       set an account's game-master level (0 player .. 4 administrator)
          gm-audit [count]                            show the newest game-master commands (default 20)

        Options:
          --sqlite <path>                           SQLite path (default: data/pristontale.db)
          --conn "<npgsql connection string>"         use Postgres instead of SQLite

        Examples:
          dotnet run --project tools/PristonTale.DevAccounts -- create-account demo change-me
          dotnet run --project tools/PristonTale.DevAccounts -- add-char demo Hero Knight 20
          dotnet run --project tools/PristonTale.DevAccounts -- list demo
          dotnet run --project tools/PristonTale.DevAccounts -- set-gm demo 4
        """);
}
