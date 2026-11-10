using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Bellatra;
using PristonTale.Application.Clans;
using PristonTale.Application.World;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Bellatra;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World.Bellatra;

using PristonTale.Contracts.Social;

namespace PristonTale.Tests;

internal static class BellatraClanTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("Bellatra's days run by the local date, the day before crossing months and years", DaysAsync);
        yield return ("Bellatra's clan storage adds a day's points, the operating clan's income with its movement, and redeems with the character",
            StorageAsync);
        yield return ("yesterday's best clan operates Bellatra and gains the tax of today's prizes; nobody operates after a day without games, and no day turns before the clans are loaded", OperatingAsync);
        yield return ("the operating clan's chief redeems its income at the secretary alone into its gold; others, a team booth and more than the balance are refused",
            RedeemAsync);
        yield return ("the secretary's request, state and result, an entrant's board and a result window and its request cross the wire whole", WireAsync);
    }

    private static Task DaysAsync()
    {
        Assert.True(BellatraDays.Of(new DateTimeOffset(2026, 11, 9, 23, 0, 0, TimeSpan.Zero)) == 20261109 && BellatraDays.Before(20261101) == 20261031
            && BellatraDays.Before(20260101) == 20251231 && BellatraDays.Before(0) == 0, "Bellatra's days differ");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-bellatra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var bellatra = provider.GetRequiredService<IBellatraRepository>();
            Assert.True(await bellatra.LoadStateAsync() == BellatraState.None, "a new Bellatra has a state");
            await bellatra.SaveStateAsync(new BellatraState(20261109, 9));
            Assert.True(await bellatra.LoadStateAsync() == new BellatraState(20261109, 9), "Bellatra's state did not load back");
            await bellatra.RecordAsync(20261109, 7, 100, 9, 5, 10);
            await bellatra.RecordAsync(20261109, 7, 100, 9, 5, 11);
            await bellatra.RecordAsync(20261109, 8, 50, 0, 3, 12);
            Assert.True(await bellatra.TopAsync(20261109, 10) is [{ ClanId: 7, Points: 200 }, { ClanId: 8, Points: 50 }] && await bellatra.BalanceAsync(9) == 10
                && await bellatra.BalanceAsync(8) == 0, "the day's points or the operating clan's income differ");
            var account = await provider.GetRequiredService<Persistence.Abstractions.Accounts.IAccountRepository>().CreateAsync("chief", "hash");
            var chief = await provider.GetRequiredService<ICharacterRepository>().CreateAsync(
                CharacterRecordFactory.NewStarter(new Simulation.Characters.CharacterClassRegistry(), account.Id, "Chief", 0, Contracts.Characters.PlayerClass.Knight, 3));
            var paid = chief with { Money = 4, SaveSeq = chief.SaveSeq + 1 };
            Assert.True(!await bellatra.RedeemAsync(9, 11, paid, 13) && await bellatra.RedeemAsync(9, 4, paid, 14) && await bellatra.BalanceAsync(9) == 6
                && !await bellatra.RedeemAsync(9, 1, chief, 15) && await bellatra.BalanceAsync(9) == 6, "a redemption differs, or a stale one was stored");
            await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "world.db")}");
            await connection.OpenAsync();
            var rows = connection.CreateCommand();
            rows.CommandText = "SELECT COUNT(*) FROM castle_treasury WHERE clan_id = 9 AND (kind = 0 AND amount = 5 OR kind = 3 AND amount = -4 AND character_id IS NOT NULL)";
            Assert.True((long)(await rows.ExecuteScalarAsync())! == 3, "the income and redemption movements were not stored");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task OperatingAsync()
    {
        var clock = new BellatraGameTests.GameClock { Now = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero) };
        var repository = new MemoryBellatra();
        var clans = new Clans();
        clans.Names[7] = "Lions";
        clans.Names[8] = "Tigers";
        var service = new BellatraClanAppService(repository, clans, new WorldServer(new WorldDataHolder()), clock);
        Assert.True(await service.RecordAsync(Prize(7, 300, 15, clock.Now)) && await service.RecordAsync(Prize(8, 100, 5, clock.Now)), "a prize was not stored");
        Assert.True(repository.State.OperatingClanId == 0 && repository.Balances.Count == 0, "a clan operated Bellatra on its first day");
        clock.Now = clock.Now.AddDays(1);
        clans.Members[1] = 7;
        clans.Ranks[1] = ClanRank.Chief;
        await service.RecordAsync(Prize(8, 50, 10, clock.Now));
        var view = await service.ViewAsync(1);
        Assert.True(view is { OperatingClanId: 7, OperatingName: "Lions", OperatingChief: "Chief of 7", Balance: 10, CanRedeem: true, Tax: BellatraRules.DefaultTax }
            && view.Top is [{ Name: "Lions", Points: 300 }, { Name: "Tigers", Points: 100 }], "yesterday's best clan does not operate Bellatra with its income");
        clock.Now = clock.Now.AddDays(2);
        Assert.True(await service.ViewAsync(2) is { OperatingClanId: 0, Balance: 0, CanRedeem: false, Top: [] }, "a clan operated after a day without games");

        var unloaded = new MemoryBellatra();
        await unloaded.SaveStateAsync(new BellatraState(BellatraDays.Before(BellatraDays.Of(clock.Now)), 0));
        await unloaded.RecordAsync(unloaded.State.Day, 7, 500, 0, 0, 0);
        var waiting = new Clans { Available = false };
        waiting.Names[7] = "Lions";
        var early = new BellatraClanAppService(unloaded, waiting, new WorldServer(new WorldDataHolder()), clock);
        Assert.True(await early.ViewAsync(1) is { OperatingClanId: 0 } && unloaded.State.Day == BellatraDays.Before(BellatraDays.Of(clock.Now)),
            "the day turned while the clans were not loaded");
        waiting.Available = true;
        Assert.True(await early.ViewAsync(1) is { OperatingClanId: 7 } && unloaded.State == new BellatraState(BellatraDays.Of(clock.Now), 7),
            "the day did not turn once the clans were loaded");
    }

    private static async Task RedeemAsync()
    {
        var team = await BellatraGameTests.Arena.CreateAsync(BellatraMode.Always, 50);
        var teamClans = new Clans();
        teamClans.Members[team.Session.EntityId] = 7;
        teamClans.Ranks[team.Session.EntityId] = ClanRank.Chief;
        var teamRepository = new MemoryBellatra();
        teamRepository.Balances[7] = 1_000;
        var atTeamBooth = new BellatraClanAppService(teamRepository, teamClans, team.World, team.Clock, team.Sessions, team.Saves);
        Assert.True(await WorldPump.RunAsync(team.World, atTeamBooth.RedeemAsync(team.Session.SessionToken, team.BoothId, 400)) == BellatraClanStatus.OutOfReach
            && teamRepository.Balances[7] == 1_000, "a team booth paid a clan's income");
        var opened = await WorldPump.RunAsync(team.World, team.Npc.RequestAsync(team.Session.SessionToken, team.Booth(Simulation.World.Npcs.NpcServiceAction.Open)));
        Assert.True(opened.Outcome.Message != GameText.None, "a team booth did not answer");

        var arena = await BellatraGameTests.Arena.CreateAsync(BellatraMode.Always, 50, BellatraRules.SecretaryBooth);
        var secretary = await WorldPump.RunAsync(arena.World, arena.Npc.RequestAsync(arena.Session.SessionToken, arena.Booth(Simulation.World.Npcs.NpcServiceAction.Open)));
        var ticket = await WorldPump.RunAsync(arena.World, arena.Npc.RequestAsync(arena.Session.SessionToken,
            arena.Booth(Simulation.World.Npcs.NpcServiceAction.EnterBellatra)));
        Assert.True(secretary.Outcome.Ok && secretary.Outcome.Message == GameText.None && secretary.Destinations is null or []
            && ticket.Outcome.Status == Simulation.World.Npcs.NpcServiceStatus.Rejected
            && arena.Session.FieldId == BellatraArena.Village, "the secretary answered as a team booth or sold a ticket");
        var repository = new MemoryBellatra();
        repository.Balances[7] = 1_000;
        var clans = new Clans();
        clans.Names[7] = "Lions";
        clans.Members[arena.Session.EntityId] = 7;
        clans.Ranks[arena.Session.EntityId] = ClanRank.Member;
        var service = new BellatraClanAppService(repository, clans, arena.World, arena.Clock, arena.Sessions, arena.Saves);
        var token = arena.Session.SessionToken;
        Assert.True(await WorldPump.RunAsync(arena.World, service.RedeemAsync(token, arena.BoothId, 400)) == BellatraClanStatus.NotChief, "a member redeemed");
        clans.Ranks[arena.Session.EntityId] = ClanRank.Chief;
        var money = arena.Session.Character!.Money;
        Assert.True(await WorldPump.RunAsync(arena.World, service.RedeemAsync(token, arena.BoothId, 400)) == BellatraClanStatus.Ok
            && arena.Session.Character.Money == money + 400 && repository.Balances[7] == 600, "the chief's redemption was not paid and stored");
        Assert.True(await WorldPump.RunAsync(arena.World, service.RedeemAsync(token, arena.BoothId, 700)) == BellatraClanStatus.NotEnoughBalance
            && await WorldPump.RunAsync(arena.World, service.RedeemAsync(token, arena.BoothId, 0)) == BellatraClanStatus.NotEnoughBalance
            && arena.Session.Character.Money == money + 400, "a redemption past the balance was paid");
    }

    private static Task WireAsync()
    {
        var buffer = new byte[BellatraClanStatePacket.MaxSize];
        var resultBuffer = new byte[BellatraResultPacket.MaxSize];
        var window = new BellatraResultPacket(true, [.. Enumerable.Range(0, BellatraLimits.ResultLines).Select(static index =>
            new BellatraResultLineWire(new string('n', 32), 11, 9_000_000_000, 70 + (uint)index))]);
        var readResult = BellatraResultPacket.Read(resultBuffer.AsSpan(0, window.Write(resultBuffer)));
        var team = new BellatraResultRequestPacket(4);
        Assert.True(readResult.Final && readResult.Lines.SequenceEqual(window.Lines)
            && BellatraResultRequestPacket.Read(resultBuffer.AsSpan(0, team.Write(resultBuffer))) == team, "a Bellatra result or its request did not cross the wire");
        var unknown = new byte[BellatraResultRequestPacket.Size];
        new BellatraResultRequestPacket(4).Write(unknown);
        unknown[^1] = BellatraLimits.Teams + 1;
        Assert.Throws<InvalidDataException>(() => BellatraResultRequestPacket.Read(unknown), "a result request for a fifth team was read");
        var accented = new string('é', 40);
        var trimmed = BellatraResultPacket.Read(resultBuffer.AsSpan(0, new BellatraResultPacket(false, [new(accented, 1, 1, 1)]).Write(resultBuffer)));
        Assert.True(trimmed.Lines is [{ Name: var name }] && accented.StartsWith(name, StringComparison.Ordinal)
            && System.Text.Encoding.UTF8.GetByteCount(name) <= Protocol.Packets.AccountWire.MaxNameBytes, "a long accented name did not cross trimmed to its byte limit");
        var request = new BellatraClanRequestPacket(BellatraClanAction.Redeem, 42, 1_000);
        Assert.True(BellatraClanRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request, "the request did not cross the wire");
        var state = new BellatraClanStatePacket(true, 7, "Lions", "Chief", 5, 9_000_000_000, true, [new("Lions", 300), new("Tigers", 100)]);
        var read = BellatraClanStatePacket.Read(buffer.AsSpan(0, state.Write(buffer)));
        Assert.True(read with { Top = [] } == state with { Top = [] } && read.Top.SequenceEqual(state.Top), "the state did not cross the wire");
        var full = state with { Top = [.. Enumerable.Range(0, Contracts.Social.BellatraLimits.TopClans).Select(static index => new BellatraClanRankWire(new string('W', 24), ulong.MaxValue))] };
        Assert.True(full.Write(buffer) <= BellatraClanStatePacket.MaxSize && BellatraClanStatePacket.Read(buffer.AsSpan(0, BellatraClanStatePacket.None.Write(buffer))) == BellatraClanStatePacket.None,
            "the largest state or the empty one differs");
        var board = new BellatraBoardPacket(true, 7, 2, 1, 0b1011, 0b0010, 123_456, [1, 2, 3, ulong.MaxValue]);
        var readBoard = BellatraBoardPacket.Read(buffer.AsSpan(0, board.Write(buffer)));
        Assert.True(readBoard with { TeamScores = [] } == board with { TeamScores = [] } && readBoard.TeamScores.SequenceEqual(board.TeamScores)
            && board.Write(buffer) <= BellatraBoardPacket.MaxSize && BellatraBoardPacket.Read(buffer.AsSpan(0, BellatraBoardPacket.Hidden.Write(buffer))) == BellatraBoardPacket.Hidden,
            "the board did not cross the wire");
        var result = new BellatraClanResultPacket(BellatraClanAction.Open, 3);
        Assert.True(BellatraClanResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "the result did not cross the wire");
        return Task.CompletedTask;
    }

    private static BellatraPrize Prize(long clanId, long points, long tax, DateTimeOffset when) =>
        new(new Application.Sessions.SessionRegistry().Create(1, "prize"), points - tax, GameText.None, clanId, points, tax, BellatraDays.Of(when));

    private sealed class MemoryBellatra : IBellatraRepository
    {
        public BellatraState State { get; private set; } = BellatraState.None;
        public Dictionary<(int Day, long ClanId), long> Points { get; } = [];
        public Dictionary<long, long> Balances { get; } = [];

        public Task<BellatraState> LoadStateAsync(CancellationToken ct = default) => Task.FromResult(State);

        public Task SaveStateAsync(BellatraState state, CancellationToken ct = default)
        {
            State = state;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BellatraClanPoints>> TopAsync(int day, int count, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BellatraClanPoints>>([.. Points.Where(pair => pair.Key.Day == day).OrderByDescending(static pair => pair.Value)
                .Take(count).Select(static pair => new BellatraClanPoints(pair.Key.ClanId, pair.Value))]);

        public Task<long> BalanceAsync(long clanId, CancellationToken ct = default) => Task.FromResult(Balances.GetValueOrDefault(clanId));

        public Task RecordAsync(int day, long clanId, long points, long operatingClanId, long income, long atSeconds, CancellationToken ct = default)
        {
            if (clanId != 0 && points > 0)
            {
                Points[(day, clanId)] = Points.GetValueOrDefault((day, clanId)) + points;
            }

            if (operatingClanId != 0 && income > 0)
            {
                Balances[operatingClanId] = Balances.GetValueOrDefault(operatingClanId) + income;
            }

            return Task.CompletedTask;
        }

        public Task<bool> RedeemAsync(long clanId, long amount, CharacterRecord character, long atSeconds, CancellationToken ct = default)
        {
            if (Balances.GetValueOrDefault(clanId) < amount)
            {
                return Task.FromResult(false);
            }

            Balances[clanId] -= amount;
            return Task.FromResult(true);
        }
    }

    private sealed class Clans : IClanDirectory
    {
        public Dictionary<long, string> Names { get; } = [];
        public Dictionary<ulong, long> Members { get; } = [];
        public Dictionary<ulong, ClanRank> Ranks { get; } = [];

        public bool Available { get; set; } = true;

        public string? NameOf(long clanId) => Names.GetValueOrDefault(clanId);

        public string? ChiefNameOf(long clanId) => Names.ContainsKey(clanId) ? "Chief of " + clanId : null;

        public long IdOf(string name) => Names.FirstOrDefault(pair => pair.Value == name).Key;

        public long ClanOf(ulong entityId) => Members.GetValueOrDefault(entityId);

        public ClanRank? RankOf(ulong entityId) => Ranks.TryGetValue(entityId, out var rank) ? rank : null;
    }
}
