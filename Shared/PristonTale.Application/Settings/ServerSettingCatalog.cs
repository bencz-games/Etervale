using System.Globalization;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Simulation.World;

namespace PristonTale.Application.Settings;

public sealed record ServerSettingDefinition(string Name, string Default, string Description, Func<ServerSettings, string, ServerSettings?> Apply);

// A row the catalog does not know is ignored; a value it refuses keeps the default and is reported.
public static class ServerSettingCatalog
{
    public static IReadOnlyList<ServerSettingDefinition> All { get; } =
    [
        Text("server.name", ServerSettings.DefaultServerName, 32, "Name of the server shown to the players that connect.",
            static (s, v) => s with { ServerName = v }),
        Int("server.code", 0, 0, int.MaxValue, "Number of this server among the servers of one world (game-master reports).",
            static (s, v) => s with { GameMasters = s.GameMasters with { ServerCode = v } }),
        Int("server.max_users", 0, 0, int.MaxValue, "Players in the world beyond which only game masters may log in; 0 for no limit.",
            static (s, v) => s with { MaxUsers = v }),
        Bool("server.test", false, "A test server: every game master may use /expup, /GiveMeExp! and /GiveMeMoney!.",
            static (s, v) => s with { GameMasters = s.GameMasters with { TestServer = v } }),
        Bool("server.debug", false, "A debug server: a game master's slash and item code drops that item.",
            static (s, v) => s with { GameMasters = s.GameMasters with { DebugServer = v } }),
        Int("server.settings_reload_minutes", ServerSettings.DefaultReloadMinutes, 1, 1440, "Minutes between two readings of this table.",
            static (s, v) => s with { ReloadMinutes = v }),
        Bool("players.starter_gifts", WorldServerOptions.Default.GiveStarterGifts,
            "A new character receives in its first minutes, one at a time, its class's starter gifts (a perfect weapon, armour and off-hand among them).",
            static (s, v) => World(s, s.World with { GiveStarterGifts = v })),
        Bool("gm.potion_monitor", true, "The /get_potion report of the potions players carry.",
            static (s, v) => s with { GameMasters = s.GameMasters with { PotionMonitor = v } }),
        Int("experience.multiplier", WorldServerOptions.DefaultExperienceMultiplier, 1, 1000,
            "Times the experience of each monster killed (the monsters' authored experience times this).",
            static (s, v) => World(s, s.World with { ExperienceMultiplier = v })),
        Int("experience.event_percent", WorldServerOptions.Default.ExperienceEventPercent, 0, 100_000,
            "Experience event: the percent of each monster's experience a kill gives (100 = x1, 500 = x5; 0 is x1).",
            static (s, v) => World(s, s.World with { ExperienceEventPercent = v })),
        Int("world.clock_offset_minutes", 0, -1440, 1440, "Game minutes added to the game clock, to start the day at another hour.",
            static (s, v) => World(s, s.World with { ClockOffsetMinutes = v })),
        Bool("world.weather", WorldServerOptions.Default.Weather,
            "Rain over the open fields on the original's schedule: clouds, then rain, light, steady, heavy or a storm, for some minutes of every cycle.",
            static (s, v) => World(s, s.World with { Weather = v })),
        Int("world.weather_cycle_minutes", WeatherSchedule.DefaultCycleMinutes, WeatherSchedule.MinimumCycleMinutes, 1_000_000,
            "Game minutes (800 ms each) from one rain to the next; the original's 8192 rain about every 1 h 49 min.",
            static (s, v) => World(s, s.World with { WeatherCycleMinutes = v })),
        Int("events.crystal", 0, 0, 2, "Crystal event: 0 off, 1 from 12:00 to 23:59 server time, 2 all day; crystals then call their event monsters.",
            static (s, v) => World(s, s.World with { CrystalEvent = v })),
        Bool("events.mandoo", false, "Mandoo event: monsters drop the event dumplings.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { Mandoo = v } })),
        Rate("events.valentine_day", "Valentine's Day event: chocolate drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { ValentineDay = v } })),
        Rate("events.watermelon", "Watermelon event: drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { Watermelon = v } })),
        Rate("events.valentine", "Valentine event: drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { Valentine = v } })),
        Rate("events.candy_days", "Candy Days event: drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { CandyDays = v } })),
        Rate("events.white_day", "White Day event: drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { WhiteDay = v } })),
        Rate("events.puzzle", "Puzzle event: puzzle piece drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { Puzzle = v } })),
        Rate("events.gift_box", "Gift box event: drop rate per 10000 kills.",
            static (s, v) => World(s, s.World with { ItemEvents = s.World.ItemEvents with { GiftBox = v } })),
        Int("aging.price_percent", 100, 0, 10_000, "Price of an aging in percent of (level + 1) x item price / 2; 0 makes aging free.",
            static (s, v) => World(s, s.World with { Aging = s.World.Aging with { PricePercent = v } })),
        Int("aging.failure_percent", 100, 0, 10_000,
            "Chance of an aging failing (falling a level or losing the item) in percent of the original's chance at each level; 0 never fails.",
            static (s, v) => World(s, s.World with { Aging = s.World.Aging with { FailurePercent = v } })),
        Int("aging.double_percent", 100, 0, 10_000, "Chance of an aging below +10 giving the level at once, in percent of the original's chance.",
            static (s, v) => World(s, s.World with { Aging = s.World.Aging with { DoublePercent = v } })),
        Int("mixing.price_percent", 100, 0, 10_000, "Price of a mix or a reconstruction in percent of the item's price; 0 makes it free.",
            static (s, v) => World(s, s.World with { Mixing = s.World.Mixing with { PricePercent = v } })),
        Int("alchemy.price_percent", 100, 0, 10_000, "Price of turning sheltoms into force orbs, in percent of the original's price per sheltom.",
            static (s, v) => World(s, s.World with { Mixing = s.World.Mixing with { AlchemyPricePercent = v } })),
        Int("bellatra.mode", (int)BellatraOptions.Default.Mode, 0, 2,
            "Bellatra: 0 closed, 1 open while the server's hour is 0 to 5 or 12 to 17, 2 always open (a game follows the game clock: tickets at game hours 4 and 5).",
            static (s, v) => World(s, s.World with { Bellatra = s.World.Bellatra with { Mode = (Simulation.World.Bellatra.BellatraMode)v } })),
        Int("bellatra.tax", BellatraOptions.Default.Tax, 0, 100, "Bellatra: percent of each player's prize kept for the operating clan.",
            static (s, v) => World(s, s.World with { Bellatra = s.World.Bellatra with { Tax = v } })),
        Int("bellatra.score_divisor", BellatraOptions.Default.ScoreDivisor, 1, 1_000_000,
            "Bellatra: a kill scores the monster's experience divided by this (a boss's times twice the round); the score is the prize in gold.",
            static (s, v) => World(s, s.World with { Bellatra = s.World.Bellatra with { ScoreDivisor = v } })),
        Int("castle.mode", (int)Application.Castle.CastleOptions.Default.Mode, 0, 2,
            "Bless Castle: 0 no sieges, 1 a weekly siege at castle.siege_day and castle.siege_hour, 2 the open league (sieges one after another for two hours from then; players never fight each other in the castle).",
            static (s, v) => s with
            {
                Castle = s.Castle with { Mode = (Simulation.Social.Castle.CastleMode)v },
                World = s.World with { CastleLeague = v == (int)Simulation.Social.Castle.CastleMode.League }
            }),
        Int("castle.siege_day", Application.Castle.CastleOptions.Default.SiegeDay, 0, 6, "Bless Castle: the siege's day of the week, 0 Sunday to 6 Saturday (server time).",
            static (s, v) => s with { Castle = s.Castle with { SiegeDay = v } }),
        Int("castle.siege_hour", Application.Castle.CastleOptions.Default.SiegeHour, 0, 23, "Bless Castle: the siege's starting hour, 0 to 23 (server time).",
            static (s, v) => s with { Castle = s.Castle with { SiegeHour = v } })
    ];

    // The definition of a setting by name, in any case; null for none.
    public static ServerSettingDefinition? Find(string name) =>
        All.FirstOrDefault(setting => string.Equals(setting.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<ServerSettingSeed> Seeds { get; } =
        All.Select(static setting => new ServerSettingSeed(setting.Name, setting.Default, setting.Description)).ToArray();

    public static ServerSettings Read(IReadOnlyDictionary<string, string> rows, WorldServerOptions? hostWorld, ICollection<string> problems)
    {
        var settings = ServerSettings.Default with { World = hostWorld ?? WorldServerOptions.Default };
        foreach (var setting in All)
        {
            if (!rows.TryGetValue(setting.Name, out var text))
            {
                text = setting.Default;
            }

            if (setting.Apply(settings, text.Trim()) is { } applied)
            {
                settings = applied;
            }
            else
            {
                problems.Add($"{setting.Name}: '{text}' is not a valid value; {setting.Default} is used.");
                settings = setting.Apply(settings, setting.Default) ?? settings;
            }
        }

        return settings;
    }

    private static ServerSettings World(ServerSettings settings, WorldServerOptions world) => settings with { World = world };

    private static ServerSettingDefinition Text(string name, string value, int maxLength, string description,
        Func<ServerSettings, string, ServerSettings> apply) =>
        new(name, value, description, (settings, text) => text.Length is > 0 and var length && length <= maxLength ? apply(settings, text) : null);

    private static ServerSettingDefinition Int(string name, int value, int min, int max, string description,
        Func<ServerSettings, int, ServerSettings?> apply) =>
        new(name, value.ToString(CultureInfo.InvariantCulture), description,
            (settings, text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
                ? apply(settings, number)
                : null);

    private static ServerSettingDefinition Rate(string name, string description, Func<ServerSettings, int, ServerSettings?> apply) =>
        Int(name, 0, 0, 10_000, description, apply);

    private static ServerSettingDefinition Bool(string name, bool value, string description, Func<ServerSettings, bool, ServerSettings> apply) =>
        new(name, value ? "true" : "false", description, (settings, text) => ParseBool(text) is { } flag ? apply(settings, flag) : null);

    private static bool? ParseBool(string text) => text.ToLowerInvariant() switch
    {
        "true" or "1" or "on" or "yes" => true,
        "false" or "0" or "off" or "no" => false,
        _ => null
    };
}
