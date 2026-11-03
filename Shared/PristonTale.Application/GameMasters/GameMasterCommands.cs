using System.Globalization;
using PristonTale.Application.Chat;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Warehouses;

namespace PristonTale.Application.GameMasters;

// One game-master command: the text it starts with, the lowest level that may use it and what it does.
internal sealed record GmCommand(string Text, Func<GameMasterOptions, int> Level, Func<GmCommandContext, Task> Run)
{
    public GmCommand(string text, int level, Func<GmCommandContext, Task> run)
        : this(text, _ => level, run)
    {
    }

    public Func<GameMasterOptions, bool> Enabled { get; init; } = static _ => true;
}

// A command being run: the line, a cursor over its words, and the replies sent back to the game master.
public sealed class GmCommandContext
{
    private readonly List<string> _replies = new();

    internal GmCommandContext(GameMasterCommands commands, ChatMember issuer, string line, int start)
    {
        Commands = commands;
        Issuer = issuer;
        Line = line;
        Start = start;
        Index = start;
    }

    public GameMasterCommands Commands { get; }
    public ChatMember Issuer { get; }
    public string Line { get; }

    // Where the command's arguments start.
    public int Start { get; }

    public int Index { get; set; }

    public IReadOnlyList<string> Replies => _replies;

    public string Word()
    {
        var index = Index;
        var word = ChatCommandText.ReadWord(Line, ref index);
        Index = index;
        return word;
    }

    public string Phrase()
    {
        var index = Index;
        var phrase = ChatCommandText.ReadPhrase(Line, ref index);
        Index = index;
        return phrase;
    }

    // The unread rest of the line, without the spaces before it.
    public string Rest => ChatCommandText.Rest(Line, Index);

    public void Reply(string text)
    {
        _replies.Add(text);
        Issuer.Link.Deliver(ChatLine.Reply(text));
    }

    // What a command that answers nothing did, for the audit trail only.
    public void Note(string text) => _replies.Add(text);
}

// The game-master commands typed in chat. Each command is checked against the issuer's level on the server; the first
// command whose text starts the line decides, in the classic order, so longer commands come before their prefixes. Every
// attempt at a command is written to the audit trail with its outcome.
public sealed class GameMasterCommands
{
    private const string Denied = "denied: level too low";
    private const int MostAuditResult = 1000;
    private readonly IGmAudit? _audit;
    private GameMasterOptions _options = GameMasterOptions.Default;

    public GameMasterCommands(ChatDirectory directory, WorldServer world, WorldDataHolder worldData, GameMasterOptions? options = null,
        IServerOperations? server = null, IGameMasterRepository? repository = null, IGmAudit? audit = null)
    {
        Directory = directory;
        World = world;
        WorldData = worldData;
        Options = options ?? GameMasterOptions.Default;
        Server = server ?? new DetachedServer();
        Repository = repository;
        _audit = audit;
    }

    public ChatDirectory Directory { get; }
    public WorldServer World { get; }
    public WorldDataHolder WorldData { get; }
    public GameMasterOptions Options
    {
        get => Volatile.Read(ref _options);
        set => Volatile.Write(ref _options, value ?? GameMasterOptions.Default);
    }

    public Settings.ServerSettingsAppService? Settings { get; set; }
    public IServerOperations Server { get; }
    public IGameMasterRepository? Repository { get; }
    public IAccountRepository? Accounts { get; init; }
    public Saves.ICharacterSaves? Saves { get; init; }
    public IWarehouseRepository? Warehouses { get; init; }
    public ItemCreationOverrides ItemOverrides { get; } = new();

    // Milliseconds of the process clock used for the item overrides and connection times.
    public Func<long> NowMs { get; init; } = static () => Environment.TickCount64;

    internal static IReadOnlyList<GmCommand> Table { get; } =
    [
        new("/localtime", GameMasterLevel.Guide, GmInformationCommands.LocalTime),
        new("/get_bosstime", GameMasterLevel.Guide, GmInformationCommands.BossTime),
        new("/get_forceorb", GameMasterLevel.Guide, GmInformationCommands.ForceOrb),
        new("/get_primitem", GameMasterLevel.Guide, GmInformationCommands.PremiumItems),
        new("/get_currency", GameMasterLevel.Guide, GmInformationCommands.Currency),
        new("/init_premiumitem", GameMasterLevel.Administrator, GmItemCommands.EndPremium),
        new("/specific", GameMasterLevel.Guide, GmItemCommands.Specialization),
        new("/Performance", GameMasterLevel.Guide, GmItemCommands.Performance),
        new("/@get_perf", GameMasterLevel.Administrator, GmItemCommands.Perfect),
        new("/@get", GameMasterLevel.Administrator, GmItemCommands.Create),
        new("/@put", GameMasterLevel.Administrator, GmItemCommands.Gift),
        new("/AgingSetting", GameMasterLevel.Operator, GmItemCommands.Aged),
        new("/get_potion", GameMasterLevel.Guide, GmInformationCommands.Potions) { Enabled = static options => options.PotionMonitor },
        new("/>", GameMasterLevel.Moderator, GmServerCommands.Notice),
        new("/netlog", GameMasterLevel.Guide, GmInformationCommands.NetLog),
        new("//netlog", GameMasterLevel.Guide, GmInformationCommands.NetLog),
        new("/user_limit", GameMasterLevel.Moderator, GmServerCommands.UserLimit),
        new("/kick", GameMasterLevel.Moderator, GmPlayerCommands.Kick),
        new("/kill", GameMasterLevel.Moderator, GmPlayerCommands.Kill),
        new("/open_warehouse", GameMasterLevel.Moderator, GmAccountCommands.OpenWarehouse),
        new("/shutdown", GameMasterLevel.Moderator, GmServerCommands.Shutdown),
        new("/weather", GameMasterLevel.Moderator, GmServerCommands.Weather),
        new("/reload_settings", GameMasterLevel.Administrator, GmServerCommands.ReloadSettings),
        new("/setting", GameMasterLevel.Administrator, GmServerCommands.Setting),
        new("/insert", GameMasterLevel.Moderator, GmAccountCommands.Insert),
        new("/usercode", GameMasterLevel.Guide, GmInformationCommands.UserByEntityId),
        new("/userid", GameMasterLevel.Guide, GmInformationCommands.UserByAccount),
        new("/username", GameMasterLevel.Guide, GmInformationCommands.UserByName),
        new("/near", GameMasterLevel.Moderator, GmPlayerCommands.Near),
        new("/call", GameMasterLevel.Moderator, GmPlayerCommands.Call),
        new("/where", GameMasterLevel.Moderator, GmPlayerCommands.Where),
        new("/field", GameMasterLevel.Moderator, GmPlayerCommands.Field),
        new("/player_location", GameMasterLevel.Moderator, GmPlayerCommands.Location),
        new("/expup", TestOrAdministrator, GmItemCommands.ExperienceUp),
        new("/GiveMeExp!", TestOrAdministrator, GmItemCommands.Experience),
        new("/GiveMeMoney!", TestOrAdministrator, GmItemCommands.Gold),
        new("/user", GameMasterLevel.Guide, GmInformationCommands.Users),
        new("/sockets", GameMasterLevel.Guide, GmInformationCommands.Sockets),
        new("/talk", GameMasterLevel.Guide, GmMonsterCommands.Talk),
        new("/set_absorb", GameMasterLevel.Administrator, GmMonsterCommands.SetAbsorption),
        new("/area_users", GameMasterLevel.Guide, GmInformationCommands.AreaUsers),
        new("/monster", GameMasterLevel.Guide, GmMonsterCommands.Monster),
        new("/mymonster", GameMasterLevel.Guide, GmMonsterCommands.Pet),
        new("/event_monster", GameMasterLevel.Guide, GmMonsterCommands.EventMonster),
        new("/area_monster", GameMasterLevel.Guide, GmMonsterCommands.FieldMonsters),
        new("/show_blackuser", GameMasterLevel.Guide, GmServerCommands.ShowBlocked),
        new("/reset_blackuser", GameMasterLevel.Guide, GmServerCommands.ResetBlocked),
        new("/free_blackuser", GameMasterLevel.Guide, GmServerCommands.FreeBlocked),
        new("/hide", GameMasterLevel.Guide, GmPlayerCommands.Hide),
        new("/show", GameMasterLevel.Guide, GmPlayerCommands.Show),
        new("/get_money", GameMasterLevel.Operator, GmInformationCommands.Gold),
        new("/dm_info", GameMasterLevel.Guide, GmInformationCommands.DamageLimits),
        new("/get_guid", GameMasterLevel.Guide, GmInformationCommands.Session),
        new("/grant", GameMasterLevel.Guide, GmPlayerCommands.Grant)
    ];

    // The commands a level may use, in table order (for the documentation and tests).
    public static IReadOnlyList<(string Text, int Level)> Describe(GameMasterOptions options) =>
        Table.Select(command => (command.Text, command.Level(options))).ToArray();

    // Runs the game-master command the line starts with; false when no command matched.
    public async Task<bool> HandleAsync(ChatMember issuer, string line)
    {
        foreach (var command in Table)
        {
            var length = ChatCommandText.Match(command.Text, line);
            if (length == 0 || !command.Enabled(Options))
            {
                continue;
            }

            var required = command.Level(Options);
            var context = new GmCommandContext(this, issuer, line, length);
            var accepted = issuer.GmLevel >= required;
            if (accepted)
            {
                try
                {
                    await command.Run(context);
                }
                catch (WorldBusyException)
                {
                    context.Reply("> Server busy, try again");
                }
                catch (Exception exception) when (exception is System.Data.Common.DbException or InvalidDataException or System.Text.Json.JsonException)
                {
                    // Storage refused or held unreadable data: the command fails with a reply and is still audited.
                    context.Reply("> Storage failed, try again");
                }
            }
            else if (issuer.GmLevel >= GameMasterLevel.Guide)
            {
                // A game master learns the level a command needs; a player is never told the command exists.
                context.Reply(string.Format(CultureInfo.InvariantCulture, "> {0} needs game-master level {1} (yours is {2})",
                    command.Text, required, issuer.GmLevel));
            }

            Audit(issuer, line, required, accepted, accepted ? string.Join(" | ", context.Replies) : Denied);
            return true;
        }

        if (Options.DebugServer && issuer.GmLevel >= GameMasterLevel.Guide && line.Length > 1 && line[0] == '/')
        {
            var context = new GmCommandContext(this, issuer, line, 1);
            if (await GmItemCommands.DebugDrop(context))
            {
                Audit(issuer, line, GameMasterLevel.Guide, true, "dropped");
                return true;
            }
        }

        return false;
    }

    private static int TestOrAdministrator(GameMasterOptions options) =>
        options.TestServer || options.DebugServer ? GameMasterLevel.Guide : GameMasterLevel.Administrator;

    private void Audit(ChatMember issuer, string line, int required, bool accepted, string result) =>
        _audit?.Append(new GmAuditEntry(issuer.AccountId, issuer.UserId, issuer.Name, issuer.GmLevel, line, required, accepted,
            result.Length > MostAuditResult ? result[..MostAuditResult] : result));
}
