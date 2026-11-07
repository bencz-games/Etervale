using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Castle;
using PristonTale.Application.Chat;
using PristonTale.Application.Clans;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Castle;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Tests;

internal static class CastleTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the coming siege is this week's day and hour until that minute has passed, then next week's", NextSiegeAsync);
        yield return ("castle storage: a new castle is empty, and the castle row loads back as saved", StorageAsync);
        yield return ("a weekly siege is announced an hour and half an hour before, starts on time, runs its two hours and closes", ScheduleAsync);
        yield return ("a late tick catches up the minutes it missed: the siege still starts and the four o'clock tax still applies", LateTickAsync);
        yield return ("the league restarts sieges three minutes after each end until its two hours are over", LeagueAsync);
        yield return ("a game master's end or the mode leaving League stops the league's restarts", LeagueStopsAsync);
        yield return ("the tax and clan skill chosen take effect at four; the fountain fills a potion a minute; an owner clan that is gone is forgotten",
            OwnerAsync);
        yield return ("an owner is kept while the clans are not loaded, and a refused storage changes nothing and is retried", FailuresAsync);
        yield return ("castle game-master commands start, time and end a siege, set the owner in peace and read the castle and fountain", CommandsAsync);
    }

    private static readonly CastleOptions Weekly = new(CastleMode.Weekly, 0, 20);
    private static readonly CastleOptions League = new(CastleMode.League, 0, 20);
    private static readonly CastleOptions Off = new(CastleMode.Off, 0, 20);

    private static Task NextSiegeAsync()
    {
        var saturday = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero);
        Assert.True(CastleAppService.NextSiege(saturday, Weekly) == sunday
            && CastleAppService.NextSiege(sunday.AddSeconds(30), Weekly) == sunday
            && CastleAppService.NextSiege(sunday.AddMinutes(1), Weekly) == sunday.AddDays(7)
            && CastleAppService.NextSiege(sunday.AddHours(3), new CastleOptions(CastleMode.Weekly, 1, 0)) == sunday.AddHours(4),
            "the coming siege differs");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-castle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var castles = provider.GetRequiredService<ICastleRepository>();
            Assert.True(await castles.LoadAsync() == CastleRecord.Empty, "a new castle is not empty");
            var saved = CastleRecord.Empty with
            {
                OwnerClanId = 7, Tax = 3, NextTax = 5, Towers = "123000", MercenaryA = 20, MercenaryC = 4, ClanSkill = 2, SkillInForce = 1,
                FountainPotions = 9, Treasury = 1_000_000
            };
            await castles.SaveAsync(saved);
            await castles.SaveAsync(saved with { Tax = 4 });
            Assert.True(await castles.LoadAsync() == saved with { Tax = 4 }, "the castle did not load back as last saved");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ScheduleAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 8, 18, 59, 30, TimeSpan.Zero));
        await fixture.TickAsync(Weekly);
        await fixture.TickAsync(Weekly, TimeSpan.FromSeconds(30));
        Assert.True(fixture.Notices() is [GameTextId.SiegeInAnHour], "the hour's notice did not go out at 19:00");
        await fixture.TickAsync(Weekly, TimeSpan.FromMinutes(30));
        await fixture.TickAsync(Weekly, TimeSpan.FromMinutes(30));
        Assert.True(fixture.Notices() is [_, GameTextId.SiegeInHalfAnHour, GameTextId.SiegeStarted] && fixture.Castle.Siege == SiegeState.Running
            && fixture.Castle.View() is { League: false } view && view.SiegeEndsAt - view.SiegeStartedAt == CastleRules.SiegeMinutes * 60,
            "the siege did not start at 20:00 after its half-hour notice");
        Assert.True(!await fixture.Castle.StartSiegeAsync(), "a second siege started");
        await fixture.TickAsync(Weekly, TimeSpan.FromMinutes(CastleRules.SiegeMinutes) - TimeSpan.FromSeconds(1));
        Assert.True(fixture.Castle.Siege == SiegeState.Running, "the siege ended early");
        await fixture.TickAsync(Weekly, TimeSpan.FromSeconds(1));
        Assert.True(fixture.Castle.Siege == SiegeState.Over, "the siege outlived its two hours");
        await fixture.TickAsync(Weekly, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        Assert.True(fixture.Castle.Siege == SiegeState.Peace && fixture.Notices() is [.., GameTextId.SiegeOver], "the siege did not close");
        await fixture.TickAsync(Weekly, TimeSpan.FromMinutes(CastleRules.LeagueRestartMinutes));
        Assert.True(fixture.Castle.Siege == SiegeState.Peace, "a weekly siege restarted");
    }

    private static async Task LateTickAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 8, 19, 59, 50, TimeSpan.Zero));
        await fixture.TickAsync(Weekly);
        await fixture.TickAsync(Weekly, TimeSpan.FromSeconds(75));
        Assert.True(fixture.Castle.Siege == SiegeState.Running, "a tick that missed the siege's minute lost the siege");

        var tax = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 3, 59, 50, TimeSpan.Zero),
            CastleRecord.Empty with { OwnerClanId = 7, NextTax = 6 });
        await tax.TickAsync(Off);
        await tax.TickAsync(Off, TimeSpan.FromMinutes(5));
        Assert.True(tax.Castle.View().Tax == 6, "a tick that missed four o'clock lost the day's tax");
    }

    private static async Task LeagueAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 8, 19, 59, 50, TimeSpan.Zero));
        await fixture.TickAsync(League);
        await fixture.TickAsync(League, TimeSpan.FromSeconds(10));
        var leagueEnds = fixture.Clock.GetUtcNow().AddMinutes(CastleRules.LeagueMinutes).ToUnixTimeSeconds();
        Assert.True(fixture.Castle.View() is { Siege: SiegeState.Running, League: true } && fixture.Notices() is [.., GameTextId.SiegeLeague, GameTextId.SiegeStarted],
            "the league did not start");
        await fixture.TickAsync(League, TimeSpan.FromMinutes(10));
        await fixture.Castle.SetSiegeMinutesAsync(1);
        await fixture.TickAsync(League, TimeSpan.FromMinutes(1));
        await fixture.TickAsync(League, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        Assert.True(fixture.Castle.Siege == SiegeState.Peace && fixture.Notices() is [.., GameTextId.SiegeOver, GameTextId.SiegeRestartsSoon],
            "the league did not announce its restart");
        await fixture.TickAsync(League, TimeSpan.FromMinutes(CastleRules.LeagueRestartMinutes));
        Assert.True(fixture.Castle.View() is { Siege: SiegeState.Running } restarted && restarted.SiegeEndsAt == leagueEnds,
            "the restarted siege does not run to the league's end");
        await fixture.TickAsync(League, TimeSpan.FromSeconds(leagueEnds - fixture.Clock.GetUtcNow().ToUnixTimeSeconds()));
        await fixture.TickAsync(League, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        await fixture.TickAsync(League, TimeSpan.FromMinutes(CastleRules.LeagueRestartMinutes));
        Assert.True(fixture.Castle.View() is { Siege: SiegeState.Peace, League: false }, "a siege restarted after the league's end");
    }

    private static async Task LeagueStopsAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 8, 19, 59, 50, TimeSpan.Zero));
        await fixture.TickAsync(League);
        await fixture.TickAsync(League, TimeSpan.FromSeconds(10));
        Assert.True(await fixture.Castle.EndSiegeAsync(), "the league's siege did not end");
        await fixture.TickAsync(League, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        await fixture.TickAsync(League, TimeSpan.FromMinutes(CastleRules.LeagueRestartMinutes));
        Assert.True(fixture.Castle.View() is { Siege: SiegeState.Peace, League: false }, "a game master's end did not stop the league");

        var modes = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 8, 19, 59, 50, TimeSpan.Zero));
        await modes.TickAsync(League);
        await modes.TickAsync(League, TimeSpan.FromSeconds(10));
        await modes.TickAsync(Off);
        await modes.Castle.SetSiegeMinutesAsync(1);
        await modes.TickAsync(Off, TimeSpan.FromMinutes(1));
        await modes.TickAsync(Off, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        await modes.TickAsync(Off, TimeSpan.FromMinutes(CastleRules.LeagueRestartMinutes));
        Assert.True(modes.Castle.View() is { Siege: SiegeState.Peace, League: false } && modes.Notices()[^1] == GameTextId.SiegeOver,
            "the league went on after its mode was turned off");
    }

    private static async Task OwnerAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 3, 59, 30, TimeSpan.Zero),
            CastleRecord.Empty with { OwnerClanId = 7, Tax = 1, NextTax = 6, ClanSkill = 3 });
        await fixture.TickAsync(Off);
        Assert.True(fixture.Castle.View().Tax == 1, "the tax changed before four");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(30));
        Assert.True(fixture.Castle.View() is { Tax: 6, SkillInForce: CastleClanSkill.Evasion, OwnerName: "Lions" } && fixture.Repository.Saved.Tax == 6,
            "the next tax and clan skill did not take effect at four");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(CastleRules.FountainPotionSeconds));
        Assert.True(fixture.Castle.View().FountainPotions == 1, "the fountain did not fill");
        await fixture.Castle.StartSiegeAsync();
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(CastleRules.FountainPotionSeconds));
        Assert.True(fixture.Castle.View().FountainPotions == 1, "the fountain filled during a siege");

        fixture.Clans.Names.Remove(7);
        await fixture.TickAsync(Off, TimeSpan.FromMinutes(1));
        Assert.True(fixture.Castle.OwnerClanId == 0 && fixture.Repository.Saved.OwnerClanId == 0, "the dissolved owner kept the castle");

        var reloaded = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), CastleRecord.Empty with { OwnerClanId = 5 });
        Assert.True(reloaded.Castle.OwnerClanId == 0 && reloaded.Repository.Saved.OwnerClanId == 0, "a castle loaded with a clan that is gone kept it");
    }

    private static async Task FailuresAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), CastleRecord.Empty with { OwnerClanId = 5 },
            clansLoaded: false);
        await fixture.TickAsync(Off, TimeSpan.FromMinutes(2));
        Assert.True(fixture.Castle.OwnerClanId == 5 && fixture.Repository.Saved.OwnerClanId == 5, "an owner was dropped while the clans were not loaded");

        var refused = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 3, 59, 30, TimeSpan.Zero), CastleRecord.Empty with { OwnerClanId = 7, NextTax = 6 });
        refused.Repository.Refuse = true;
        Assert.True(!await refused.TickAsync(Off, TimeSpan.FromSeconds(30)) && refused.Castle.View().Tax == 0, "a refused tax still applied");
        Assert.True(await refused.Castle.SetOwnerAsync(0) == CastleOwnerResult.StorageFailed && refused.Castle.OwnerClanId == 7,
            "a refused owner still changed");
        refused.Repository.Refuse = false;
        Assert.True(await refused.TickAsync(Off, TimeSpan.FromMinutes(1)) && refused.Castle.View().Tax == 6, "the tax was not tried again");
    }

    private static async Task CommandsAsync()
    {
        var chat = new ChatFixture();
        var gm = await chat.JoinAsync("Keeper", 0, 0, GameMasterLevel.Guide);
        await chat.SayAsync(gm, "/get_castle_info");
        Assert.True(gm.Link.Last == "> Bless Castle is not available", gm.Link.Last);
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), CastleRecord.Empty with { NextTax = 4 });
        chat.GameMasters.Castle = fixture.Castle;
        await chat.SayAsync(gm, "/set_castle_master Lions");
        Assert.True(gm.Link.Last == "> Castle Master Clan [ Lions ]" && fixture.Castle.OwnerClanId == 7, gm.Link.Last);
        await chat.SayAsync(gm, "/set_castle_master Nobody");
        Assert.True(gm.Link.Last == "> No such clan", gm.Link.Last);
        await chat.SayAsync(gm, "/start_castle 30");
        Assert.True(gm.Link.Last == "> Bless Castle siege started" && fixture.Castle.View() is { Siege: SiegeState.Running } view
            && view.SiegeEndsAt - view.SiegeStartedAt == 30 * 60, gm.Link.Last);
        await chat.SayAsync(gm, "/set_castle_master Lions");
        Assert.True(gm.Link.Last == "> Not during a siege", gm.Link.Last);
        await chat.SayAsync(gm, "/set_castle_time 0");
        Assert.True(gm.Link.Last == "> /set_castle_time minutes", gm.Link.Last);
        await chat.SayAsync(gm, "/set_castle_time 5");
        Assert.True(gm.Link.Last == "> The siege ends in 5 minutes" && fixture.Castle.View() is var timed && timed.SiegeEndsAt - timed.SiegeStartedAt == 5 * 60,
            gm.Link.Last);
        await chat.SayAsync(gm, "/get_castle_info");
        Assert.True(gm.Link.Last == "> BlessCastle ( MasterClan[Lions] Tax[0] NextTax[4] ) MyClan() Mode(1)", gm.Link.Last);
        await chat.SayAsync(gm, "/end_castle");
        Assert.True(gm.Link.Last == "> Bless Castle siege ended" && fixture.Castle.Siege == SiegeState.Over, gm.Link.Last);
        await chat.SayAsync(gm, "/end_castle");
        Assert.True(gm.Link.Last == "> No siege is running", gm.Link.Last);
        await chat.SayAsync(gm, "/get_fountain_info");
        Assert.True(gm.Link.Last == "> SoulFountain ( Potion[0] )", gm.Link.Last);
        await chat.SayAsync(gm, "/reset_fountain_time");
        Assert.True(gm.Link.Last == "> Reset SoulFountain", gm.Link.Last);
        await chat.SayAsync(gm, "/set_castle_tax");
        Assert.True(gm.Link.Last == "> BlessCastle ( MasterClan[Lions] Tax[4] NextTax[4] )" && fixture.Castle.View().Tax == 4, gm.Link.Last);
        fixture.Repository.Refuse = true;
        await chat.SayAsync(gm, "/set_castle_tax");
        Assert.True(gm.Link.Last == "> Storage failed, try again", gm.Link.Last);
    }

    private sealed class Fixture
    {
        public required CastleAppService Castle { get; init; }
        public required MemoryCastle Repository { get; init; }
        public required FakeClans Clans { get; init; }
        public required ManualTime Clock { get; init; }
        public required FakeLink Link { get; init; }

        // Lions (7) is the one clan.
        public static async Task<Fixture> CreateAsync(DateTimeOffset now, CastleRecord? stored = null, bool clansLoaded = true)
        {
            var clock = new ManualTime(now);
            var repository = new MemoryCastle();
            await repository.SaveAsync(stored ?? CastleRecord.Empty);
            var clans = new FakeClans { Available = clansLoaded };
            if (clansLoaded)
            {
                clans.Names[7] = "Lions";
            }

            var chat = new ChatDirectory();
            var link = new FakeLink();
            chat.Join(new ChatMember(new SessionRegistry().Create(1, "watcher"), "Watcher", link, 0, 0));
            var castle = new CastleAppService(repository, clans, chat, clock);
            await castle.LoadAsync();
            return new Fixture { Castle = castle, Repository = repository, Clans = clans, Clock = clock, Link = link };
        }

        public Task<bool> TickAsync(CastleOptions options, TimeSpan advance = default)
        {
            Clock.Advance(advance);
            return Castle.TickAsync(options);
        }

        public GameTextId[] Notices() => [.. Link.Lines.Where(static line => line.Kind == Contracts.Social.ChatKind.Notice).Select(static line => line.Line.Id)];
    }

    private sealed class MemoryCastle : ICastleRepository
    {
        public CastleRecord Saved { get; private set; } = CastleRecord.Empty;

        public bool Refuse { get; set; }

        public Task<CastleRecord> LoadAsync(CancellationToken ct = default) => Task.FromResult(Saved);

        public Task SaveAsync(CastleRecord castle, CancellationToken ct = default)
        {
            if (Refuse)
            {
                throw new SqliteException("refused", 5);
            }

            Saved = castle;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClans : IClanDirectory
    {
        public Dictionary<long, string> Names { get; } = new();

        public bool Available { get; init; }

        public string? NameOf(long clanId) => Names.GetValueOrDefault(clanId);

        public long IdOf(string name) => Names.FirstOrDefault(pair => pair.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;

        public long ClanOf(ulong entityId) => 0;
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan by) => _now += by;
    }
}
