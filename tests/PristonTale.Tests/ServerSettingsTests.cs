using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Settings;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

internal static class ServerSettingsTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("server settings read typed values, keep defaults for missing rows and report refused values", CatalogAsync);
        yield return ("the settings table seeds missing rows, keeps edited values and writes one setting", RepositoryAsync);
        yield return ("a settings reading applies the world, game-master, name and player-limit settings", ApplyAsync);
        yield return ("the experience event is a plain percent of each kill and the aging settings scale price and chances", RulesAsync);
        yield return ("the server hello carries the server name", HelloAsync);
    }

    private static Task CatalogAsync()
    {
        var problems = new List<string>();
        var defaults = ServerSettingCatalog.Read(new Dictionary<string, string>(), null, problems);
        Assert.True(problems.Count == 0 && defaults == ServerSettings.Default, "missing rows did not give the defaults");
        Assert.True(ServerSettingCatalog.All.Select(static setting => setting.Name).Distinct().Count() == ServerSettingCatalog.All.Count,
            "a setting name repeats");

        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["server.name"] = "Babel",
            ["server.max_users"] = "300",
            ["server.test"] = "on",
            ["experience.multiplier"] = "7",
            ["experience.event_percent"] = "200",
            ["events.crystal"] = "2",
            ["events.mandoo"] = "1",
            ["events.puzzle"] = "200",
            ["aging.price_percent"] = "0",
            ["aging.failure_percent"] = "50",
            ["world.clock_offset_minutes"] = "abc",
            ["unknown.setting"] = "1"
        };
        var host = WorldServerOptions.Default with { MaxPendingCommandsPerPlayer = 9 };
        var settings = ServerSettingCatalog.Read(rows, host, problems);
        Assert.True(settings.ServerName == "Babel" && settings.MaxUsers == 300 && settings.GameMasters.TestServer
            && settings.World.ExperienceMultiplier == 7 && settings.World.ExperienceEventPercent == 200 && settings.World.CrystalEvent == 2
            && settings.World.ItemEvents.Mandoo && settings.World.ItemEvents.Puzzle == 200
            && settings.World.Aging == new AgingSettings(0, 50, 100) && settings.World.MaxPendingCommandsPerPlayer == 9,
            "the table's values were not read");
        Assert.True(problems.Count == 1 && problems[0].StartsWith("world.clock_offset_minutes", StringComparison.Ordinal)
            && settings.World.ClockOffsetMinutes == 0, "a refused value was not reported or did not keep the default");

        problems.Clear();
        ServerSettingCatalog.Read(new Dictionary<string, string> { ["events.crystal"] = "3", ["server.name"] = "" }, null, problems);
        Assert.True(problems.Count == 2, "out-of-range values were accepted");
        return Task.CompletedTask;
    }

    private static async Task RepositoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "settings.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var repository = provider.GetRequiredService<IServerSettingsRepository>();
            await repository.SeedAsync(ServerSettingCatalog.Seeds);
            var rows = await repository.LoadAsync();
            Assert.True(ServerSettingCatalog.All.All(setting => rows[setting.Name] == setting.Default), "the seed did not write every default");

            Assert.True(await repository.SetAsync("experience.multiplier", "9") && !await repository.SetAsync("no.such", "1"), "a write misbehaved");
            await repository.SeedAsync([new ServerSettingSeed("experience.multiplier", "5", "Changed description")]);
            rows = await repository.LoadAsync();
            Assert.True(rows["experience.multiplier"] == "9", "seeding again overwrote an edited value");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static async Task ApplyAsync()
    {
        var holder = new WorldDataHolder { Current = WorldData.Empty };
        var world = new WorldServer(holder);
        var server = new DetachedServer();
        var gameMasters = new GameMasterCommands(new ChatDirectory(), world, holder, server: server);
        var repository = new MemorySettings();
        var settings = new ServerSettingsAppService(repository, world, gameMasters, server, WorldServerOptions.Default);
        repository.Rows["server.name"] = "Babel";
        repository.Rows["server.max_users"] = "100";
        repository.Rows["server.debug"] = "true";
        repository.Rows["experience.multiplier"] = "3";
        await WorldPump.RunAsync(world, settings.ReloadAsync());
        Assert.True(repository.Seeded && world.Options.ExperienceMultiplier == 3 && gameMasters.Options.DebugServer && server.ServerName == "Babel"
            && server.UserLimit == 100, "the reading was not applied");

        server.UserLimit = 50;
        await WorldPump.RunAsync(world, settings.ReloadAsync());
        Assert.True(server.UserLimit == 50, "an unchanged player limit overrode the game master's");
        repository.Rows["server.max_users"] = "200";
        Assert.True(await WorldPump.RunAsync(world, settings.SetAsync("aging.price_percent", "0")) && world.Options.Aging.PricePercent == 0
            && server.UserLimit == 200, "a written setting was not applied");
        Assert.True(!await WorldPump.RunAsync(world, settings.SetAsync("aging.price_percent", "-1")) && !await WorldPump.RunAsync(world, settings.SetAsync("nope", "1")),
            "a refused setting was written");
    }

    private static Task RulesAsync()
    {
        Assert.True(KillReward.ExpForKill(100, 10, 10) == 100 && KillReward.ExpForKill(100, 10, 10, 1, 150) == 150
            && KillReward.ExpForKill(100, 10, 10, 1, 500) == 500 && KillReward.ExpForKill(100, 10, 10, 1, 30_000) == 30_000
            && KillReward.ExpForKill(100, 10, 10, 1, 0) == 100 && KillReward.ExpForKill(100, 10, 10, 1, -5) == 100
            && KillReward.ExpForKill(100, 10, 10, 2, 50) == 100, "the experience event is not a plain percent of the kill");

        Assert.True(AgingRules.Price(3, 1000, 0) == 0 && AgingRules.Price(3, 1000, 50) == 1000, "the aging price does not follow its percent");
        var sure = new AgingSettings(FailurePercent: 0);
        Assert.True(AgingRules.Roll(12, AgingStone.None, new ScriptedRng(10, 60, 0), sure) == AgingResult.Success, "a failure happened at 0%");
        var doubled = new AgingSettings(DoublePercent: 200);
        Assert.True(AgingRules.Roll(0, AgingStone.None, new ScriptedRng(50, 50, 59), doubled) == AgingResult.Double, "the double chance was not scaled");
        return Task.CompletedTask;
    }

    private static Task HelloAsync()
    {
        var buffer = new byte[ServerHelloPacket.MaxSize];
        var hello = new ServerHelloPacket(ProtocolVersion.Current, ProtocolVersion.Minimum, 0, "Babel");
        Assert.True(ServerHelloPacket.Read(buffer.AsSpan(0, hello.Write(buffer))) == hello, "the server name was not carried");
        return Task.CompletedTask;
    }

    private sealed class MemorySettings : IServerSettingsRepository
    {
        public Dictionary<string, string> Rows { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Seeded { get; private set; }

        public Task SeedAsync(IReadOnlyList<ServerSettingSeed> seeds, CancellationToken ct = default)
        {
            foreach (var seed in seeds)
            {
                Rows.TryAdd(seed.Name, seed.DefaultValue);
            }

            Seeded = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Rows, StringComparer.OrdinalIgnoreCase));

        public Task<bool> SetAsync(string name, string value, CancellationToken ct = default)
        {
            if (!Rows.ContainsKey(name))
            {
                return Task.FromResult(false);
            }

            Rows[name] = value;
            return Task.FromResult(true);
        }
    }
}
