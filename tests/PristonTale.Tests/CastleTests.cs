using PristonTale.Application.World;
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
        yield return ("a siege opens the owner's defenders; the tower's fall gives the castle to the leading clan, closes the siege and sends outsiders back",
            TakenAsync);
        yield return ("a conquest refused by storage is kept and stored on a later minute; the owner's towers and mercenaries serve one siege",
            ConquestRetryAsync);
        yield return ("the castle's state crosses the wire whole, and a board of too many clans is refused", WireAsync);
        yield return ("a player in the world is told the castle at once and on every change of owner, tax or siege, never twice the same", AnnounceAsync);
        yield return ("the owner's purchases cost a tower 500,000 and each mercenary its kind's price, only grow, and need gold", OwnerRulesAsync);
        yield return ("the owner clan's chief sets the castle at the butler, paying from its gold; others, a vice and a siege are refused", OwnerSetupAsync);
        yield return ("the chief takes gold out of the treasury at the butler, stored with the castle and the movement", OwnerWithdrawAsync);
        yield return ("the owner window's request, state and result cross the wire whole", OwnerWireAsync);
        yield return ("the butler window: the chief's pages pick a tax up to 10 %, towers and mercenaries, save, and keep the changes when refused",
            OwnerWindowAsync);
        yield return ("the castle's tax adds whole gold to fees, and the tax the world gathered goes to the treasury each minute", TaxAsync);
        yield return ("the soul fountain gives a potion a second to the owner clan's members at it, in turn, with a whisper, and only in peace",
            FountainAsync);
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
            var payer = await provider.GetRequiredService<Persistence.Abstractions.Accounts.IAccountRepository>().CreateAsync("keeper", "hash");
            var chief = await provider.GetRequiredService<Persistence.Abstractions.Characters.ICharacterRepository>().CreateAsync(
                Persistence.Abstractions.Characters.CharacterRecordFactory.NewStarter(new Simulation.Characters.CharacterClassRegistry(), payer.Id, "Chief", 0,
                    Contracts.Characters.PlayerClass.Knight, 3));
            var saved = CastleRecord.Empty with
            {
                OwnerClanId = 7, Tax = 3, NextTax = 5, Towers = "123000", MercenaryA = 20, MercenaryC = 4, ClanSkill = 2, SkillInForce = 1,
                FountainPotions = 9, Treasury = 1_000_000
            };
            await castles.SaveAsync(saved);
            await castles.SaveAsync(saved with { Tax = 4 });
            Assert.True(await castles.LoadAsync() == saved with { Tax = 4 }, "the castle did not load back as last saved");
            var paid = chief with { Money = 300, SaveSeq = chief.SaveSeq + 1 };
            Assert.True(await castles.SaveWithAsync(saved with { Treasury = 700 }, paid, new TreasuryMove(7, TreasuryKind.Withdrawal, -300, 10))
                && (await castles.LoadAsync()).Treasury == 700, "the castle and the receiving character were not stored together");
            Assert.True(!await castles.SaveWithAsync(saved with { Treasury = 1 }, chief, new TreasuryMove(7, TreasuryKind.Withdrawal, -699, 11))
                && (await castles.LoadAsync()).Treasury == 700, "a stale character still stored the castle");
            Assert.True(await castles.SaveWithAsync(saved with { Treasury = 750 }, null, new TreasuryMove(7, TreasuryKind.Tax, 50, 12))
                && (await castles.LoadAsync()).Treasury == 750, "the tax credit was not stored with the castle");
            await using (var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "world.db")}"))
            {
                await connection.OpenAsync();
                var rows = connection.CreateCommand();
                rows.CommandText = "SELECT COUNT(*) FROM castle_treasury WHERE kind = 1 AND amount = 50 AND character_id IS NULL AND clan_id = 7";
                Assert.True((long)(await rows.ExecuteScalarAsync())! == 1, "the tax movement was not stored without a character");
            }
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
        Assert.True(fixture.Castle.View() is { Tax: 6, SkillInForce: CastleClanSkill.Evasion, OwnerName: "Lions" } && fixture.Repository.Saved.Tax == 6
            && fixture.World is { Owner: 7, Skill: CastleClanSkill.Evasion }, "the next tax and clan skill did not take effect at four, or the world was not told");
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

    private static async Task TakenAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero),
            CastleRecord.Empty with { OwnerClanId = 7, Towers = "1309", MercenaryA = 2, MercenaryC = 5 });
        fixture.Clans.Names[8] = "Tigers";
        Assert.True(await fixture.Castle.StartSiegeAsync() && fixture.World.Opened is { OwnerClanId: 7 } order
            && order.Towers is [CastleTower.Ice, CastleTower.Fire, CastleTower.None, CastleTower.None, CastleTower.None, CastleTower.None]
            && order.Mercenaries is [2, 0, 5],
            "the siege did not open with the owner's defenders");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(1));
        Assert.True(fixture.Castle.Siege == SiegeState.Running, "the siege closed before the tower fell");
        fixture.World.Siege = fixture.World.Siege with { ValhallaFallen = true, Winner = 8 };
        fixture.World.Arrivals.Add(new CastleArrival(1, Simulation.World.FieldCodes.Castle, new System.Numerics.Vector3(1, 2, 3)));
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(1));
        Assert.True(fixture.Castle.View() is { OwnerClanId: 8, Siege: SiegeState.Over } && fixture.Repository.Saved.OwnerClanId == 8
            && fixture.Link.Lines[^1].Line is { Id: GameTextId.CastleTaken, Args: ["Tigers"] } && fixture.World.Calls is ["open", "close 8"],
            "the tower's fall did not give the castle to the leading clan");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        Assert.True(fixture.Castle.Siege == SiegeState.Peace && fixture.World.Calls is [.., "end 8"] && fixture.Link.Warps is [(Simulation.World.FieldCodes.Castle, _)],
            "the end did not send the outsiders back");
    }

    private static async Task ConquestRetryAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero),
            CastleRecord.Empty with { OwnerClanId = 7, Towers = "123", MercenaryB = 4 });
        fixture.Clans.Names[8] = "Tigers";
        await fixture.Castle.StartSiegeAsync();
        fixture.World.Siege = fixture.World.Siege with { ValhallaFallen = true, Winner = 8 };
        fixture.Repository.Refuse = true;
        Assert.True(await fixture.Castle.EndSiegeAsync() && fixture.Castle.OwnerClanId == 7 && fixture.World.Calls is ["open", "close 7"],
            "a refused conquest changed the owner");
        fixture.Repository.Refuse = false;
        await fixture.TickAsync(Off, TimeSpan.FromMinutes(1));
        Assert.True(fixture.Castle.OwnerClanId == 8 && fixture.Repository.Saved.OwnerClanId == 8 && fixture.Notices() is [.., GameTextId.CastleTaken, GameTextId.SiegeOver],
            "the conquest was not stored on the next minute");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        Assert.True(fixture.Repository.Saved is { Towers: "", MercenaryA: 0, MercenaryB: 0, MercenaryC: 0, OwnerClanId: 8 },
            "the owner's towers and mercenaries outlived the siege");
    }

    private static Task OwnerRulesAsync()
    {
        var empty = CastleSetup.Empty;
        var wanted = empty with { Tax = 5, Skill = CastleClanSkill.Damage, Towers = [CastleTower.Fire, CastleTower.Ice, .. empty.Towers[2..]], Mercenaries = [2, 1, 1] };
        Assert.True(CastleOwnerRules.Cost(empty, wanted, out var cost) == CastleOwnerStatus.Ok && cost == 2 * 500_000 + 2 * 50_000 + 100_000 + 300_000,
            "the purchases' cost differs");
        var rechosen = wanted with { Towers = [CastleTower.Lightning, .. wanted.Towers[1..]], Mercenaries = [3, 1, 1] };
        Assert.True(CastleOwnerRules.Cost(wanted, rechosen, out var more) == CastleOwnerStatus.Ok && more == 50_000, "another element for a bought tower was not free");
        Assert.True(CastleOwnerRules.Cost(wanted, wanted with { Towers = empty.Towers }, out _) == CastleOwnerStatus.InvalidSetup
            && CastleOwnerRules.Cost(wanted, wanted with { Mercenaries = [1, 1, 1] }, out _) == CastleOwnerStatus.InvalidSetup
            && CastleOwnerRules.Cost(empty, empty with { Tax = 11 }, out _) == CastleOwnerStatus.InvalidSetup
            && CastleOwnerRules.Cost(empty, empty with { Mercenaries = [21, 0, 0] }, out _) == CastleOwnerStatus.InvalidSetup,
            "a setup that shrinks or overflows was accepted");
        return Task.CompletedTask;
    }

    private static async Task OwnerSetupAsync()
    {
        await using var owner = await OwnerFixture.CreateAsync(1_000_000, CastleRecord.Empty with { OwnerClanId = 7, Treasury = 50_000 });
        var (castle, token, entity) = (owner.Castle, owner.Session.SessionToken, owner.Session.EntityId);
        Assert.True(castle.OwnerViewOf(entity) is { CanEdit: true, Treasury: 50_000 } && castle.OwnerViewOf(999) is null, "the owner's window differs");
        var wanted = CastleSetup.Empty with { Tax = 5, Skill = CastleClanSkill.Damage, Towers = [CastleTower.Fire, .. CastleSetup.Empty.Towers[1..]], Mercenaries = [2, 0, 0] };
        Assert.True(await castle.SaveSetupAsync(token, owner.NpcId, wanted) == CastleOwnerStatus.Ok && owner.Session.Character!.Money == 1_000_000 - 600_000
            && owner.Repository.Saved is { NextTax: 5, ClanSkill: 2, Towers: "300000", MercenaryA: 2 } && castle.OwnerViewOf(entity)!.Setup.Towers[0] == CastleTower.Fire,
            "the chief's setup was not paid and stored");
        Assert.True(await castle.SaveSetupAsync(token, owner.NpcId, wanted with { Mercenaries = [20, 20, 20] }) == CastleOwnerStatus.NotEnoughGold
            && owner.Session.Character!.Money == 400_000, "a setup beyond the chief's gold was taken");
        Assert.True(await castle.SaveSetupAsync(token, owner.NpcId, wanted with { Mercenaries = [1, 0, 0] }) == CastleOwnerStatus.InvalidSetup,
            "a dismissed mercenary was accepted");
        owner.Clans.Ranks[entity] = Simulation.Social.Clans.ClanRank.Vice;
        Assert.True(await castle.SaveSetupAsync(token, owner.NpcId, wanted) == CastleOwnerStatus.NotChief && castle.OwnerViewOf(entity) is { CanEdit: false },
            "a vice chief changed the castle");
        owner.Clans.Ranks[entity] = Simulation.Social.Clans.ClanRank.Chief;
        Assert.True(await castle.StartSiegeAsync() && await castle.SaveSetupAsync(token, owner.NpcId, wanted) == CastleOwnerStatus.DuringSiege
            && await castle.WithdrawAsync(token, owner.NpcId, 1) == CastleOwnerStatus.DuringSiege, "the castle changed during a siege");
        owner.Clans.Members[entity] = 8;
        Assert.True(await castle.SaveSetupAsync(token, owner.NpcId, wanted) == CastleOwnerStatus.NotOwner, "a player outside the owner clan changed the castle");
    }

    private static async Task OwnerWithdrawAsync()
    {
        await using var owner = await OwnerFixture.CreateAsync(1_000, CastleRecord.Empty with { OwnerClanId = 7, Treasury = 1_000_000 });
        var (castle, token) = (owner.Castle, owner.Session.SessionToken);
        Assert.True(await castle.WithdrawAsync(token, owner.NpcId, 300_000) == CastleOwnerStatus.Ok && owner.Session.Character!.Money == 301_000
            && owner.Repository.Saved.Treasury == 700_000 && owner.Repository.Moves is [{ ClanId: 7, Kind: TreasuryKind.Withdrawal, Amount: -300_000 }],
            "the withdrawal was not paid and stored with its movement");
        Assert.True(await castle.WithdrawAsync(token, owner.NpcId, 700_001) == CastleOwnerStatus.NotEnoughTreasury
            && await castle.WithdrawAsync(token, owner.NpcId, 0) == CastleOwnerStatus.NotEnoughTreasury && owner.Repository.Saved.Treasury == 700_000,
            "a withdrawal beyond the treasury was taken");
        owner.Repository.StalePayer = true;
        Assert.True(await castle.WithdrawAsync(token, owner.NpcId, 1_000) == CastleOwnerStatus.Unavailable && owner.Session.Character!.Money == 301_000
            && castle.OwnerViewOf(owner.Session.EntityId)!.Treasury == 700_000, "a refused storage still paid");
    }

    private static async Task FountainAsync()
    {
        Assert.True(CastleRules.AtFountain(new System.Numerics.Vector3(37100, 0, -30200)) && !CastleRules.AtFountain(new System.Numerics.Vector3(37040, 0, -30200))
            && !CastleRules.AtFountain(new System.Numerics.Vector3(37100, 0, -30102)), "the fountain's area differs");
        var clock = new ManualTime(new DateTimeOffset(2026, 11, 9, 12, 0, 30, TimeSpan.Zero));
        await using var owner = await OwnerFixture.CreateAsync(1_000, CastleRecord.Empty with { OwnerClanId = 7, FountainPotions = 3 }, clock);
        int Potions() => owner.Session.Character!.Inventory.Items().Where(item => item.Definition.Code == CastleRules.FountainPotion).Sum(item => item.Count);
        async Task NextSecond()
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await owner.Castle.TickAsync(Off);
        }

        await owner.Castle.TickAsync(Off);
        await owner.Castle.TickAsync(Off);
        Assert.True(Potions() == 1 && owner.Repository.Saved.FountainPotions == 2
            && owner.Link.Lines is [{ Kind: Contracts.Social.ChatKind.Whisper, Line: { Id: GameTextId.FountainPotion, Args: ["Chief", "2"] } }],
            "the fountain did not give one potion a second to the member at it, with a whisper of the potions left");
        await owner.World.SetClanTagAsync(owner.Session.EntityId, new Simulation.Social.Clans.ClanTag(8, "Tigers", 0, Registered: true));
        await NextSecond();
        Assert.True(Potions() == 1 && owner.Repository.Saved.FountainPotions == 2, "the fountain gave a potion to a player outside the owner clan");
        await owner.World.SetClanTagAsync(owner.Session.EntityId, new Simulation.Social.Clans.ClanTag(7, "Lions", 0, Registered: true));
        owner.Repository.StalePayer = true;
        await NextSecond();
        Assert.True(Potions() == 1 && owner.Repository.Saved.FountainPotions == 2, "a potion reached the bag although storage refused it");
        owner.Repository.StalePayer = false;
        Assert.True(await owner.Castle.StartSiegeAsync(), "the siege did not start");
        await NextSecond();
        Assert.True(Potions() == 1, "the fountain gave a potion during a siege");
        Assert.True(await owner.Castle.EndSiegeAsync(), "the siege did not end");
        clock.Advance(TimeSpan.FromSeconds(CastleRules.OverSeconds));
        for (var second = 0; second < 4; second++)
        {
            await NextSecond();
        }

        Assert.True(Potions() == 3 && owner.Repository.Saved.FountainPotions == 0 && owner.Castle.View().FountainPotions == 0,
            "the fountain did not give its last potions in peace, or gave more than it held");
    }

    // A chief of the owner clan (7) beside the castle's butler in a running world, or at the soul fountain in the castle.
    private sealed class OwnerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task _ticker = Task.CompletedTask;

        public required WorldServer World { get; init; }
        public required CastleAppService Castle { get; init; }
        public required MemoryCastle Repository { get; init; }
        public required FakeClans Clans { get; init; }
        public required PlayerSession Session { get; init; }
        public required FakeLink Link { get; init; }
        public required ulong NpcId { get; init; }

        public static Task<OwnerFixture> CreateAsync(long money, CastleRecord stored) => CreateAsync(money, stored, null);

        // With a clock, the chief stands at the soul fountain instead, and the castle runs on that clock.
        public static async Task<OwnerFixture> CreateAsync(long money, CastleRecord stored, TimeProvider? clock)
        {
            var classes = new Simulation.Characters.CharacterClassRegistry();
            var starter = Persistence.Abstractions.Characters.CharacterRecordFactory.NewStarter(classes, 1, "Chief", 0, Contracts.Characters.PlayerClass.Fighter, 3)
                with { Id = 1 };
            var character = classes.CreatePlayer(Contracts.Characters.PlayerClass.Fighter, starter.Name, 50);
            character.RestoreProgress(0, money);
            var characters = new MemoryCharacters(Persistence.Abstractions.Characters.CharacterRecordFactory.FromLiveSession(starter, character, 3,
                new System.Numerics.Vector3(100, 0, 100)));
            var npc = new Simulation.World.Npcs.NpcDefinition(1, "Butler", "butler.inx", [new Simulation.World.Npcs.NpcService(Simulation.World.Npcs.NpcServiceKind.BlessCastle)])
            {
                DisplayName = "Butler"
            };
            var empty = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(new Simulation.Items.ItemCatalog([new Simulation.Items.ItemDefinition(
                        CastleRules.FountainPotion, "Mystic Mana Potion", Simulation.Items.ItemKind.Potion) { MaxStack = 100, Weight = 1 }]),
                    empty.Monsters, new Simulation.World.Npcs.NpcCatalog([npc]),
                    new Dictionary<int, Simulation.World.Fields.FieldSpawnData>
                    {
                        [3] = Simulation.World.Fields.FieldSpawnData.Empty with { FixedNpcs = [new Simulation.World.Fields.FixedNpc(1, 100 * 256, 100 * 256, 0)] }
                    },
                    empty.Motions)
            };
            var catalog = new Simulation.World.WorldCatalog();
            var world = new WorldServer(holder, catalog);
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "chief");
            var saves = new ImmediateSaves(characters);
            var entry = new Application.Services.SessionAppService(sessions, classes, characters, saves, catalog, world, holder);
            Assert.True((await entry.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
            var outbox = System.Threading.Channels.Channel.CreateUnbounded<WorldSnapshot>();
            var player = WorldPlayerFactory.Create(session);
            await WorldPump.RunAsync(world, world.JoinAsync(clock is null ? 3 : Simulation.World.FieldCodes.Castle, player, outbox.Writer, session));
            Assert.True(outbox.Reader.TryRead(out var snapshot), "roster missing");
            var npcId = snapshot.Entities.Where(entity => entity.Kind == Contracts.World.EntityKind.Npc).Select(entity => entity.EntityId).SingleOrDefault();
            if (clock is not null)
            {
                player.Teleport(new System.Numerics.Vector3(37100, 0, -30200));
                player.Clan = new Simulation.Social.Clans.ClanTag(7, "Lions", 0, Registered: true);
            }
            var repository = new MemoryCastle();
            await repository.SaveAsync(stored);
            var clans = new FakeClans { Available = true };
            clans.Names[7] = "Lions";
            clans.Members[session.EntityId] = 7;
            clans.Ranks[session.EntityId] = Simulation.Social.Clans.ClanRank.Chief;
            var chat = new ChatDirectory();
            var link = new FakeLink();
            chat.Join(new ChatMember(session, starter.Name, link, 0, money));
            var castle = new CastleAppService(repository, clans, chat, world, clock, sessions, saves);
            await castle.LoadAsync();
            var fixture = new OwnerFixture
            {
                World = world, Castle = castle, Repository = repository, Clans = clans, Session = session, Link = link,
                NpcId = npcId
            };
            fixture._ticker = Task.Run(fixture.TickAsync);
            return fixture;
        }

        private async Task TickAsync()
        {
            uint tick = 10_000;
            while (!_stop.IsCancellationRequested)
            {
                World.Tick(0.05f, tick++);
                await Task.Delay(1);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _ticker;
            _stop.Dispose();
        }
    }

    private static async Task TaxAsync()
    {
        Assert.True(CastleRules.WithTax(1_000, 10) == 1_100 && CastleRules.WithTax(999, 5) == 1_048 && CastleRules.WithTax(1_000, 50) == 1_100
            && CastleRules.TaxIn(1_100, 10) == 100 && CastleRules.TaxIn(1_048, 5) == 49 && CastleRules.TaxIn(500, 0) == 0
            && CastleRules.TaxIn(27 * 3, 10, 3) == 6 && CastleRules.TaxIn(82, 10, 3) == 0, "the tax on fees differs");
        Assert.True(CastleRules.TaxInForce(0, 5) == 0 && CastleRules.TaxInForce(7, 300) == 10 && CastleRules.TaxInForce(7, -5) == 0
            && CastleRules.TaxInForce(7, 4) == 4, "the tax in force differs");
        Assert.True(Simulation.World.WingGates.Fee(Simulation.World.WingGates.Wings[0], Simulation.World.WingGates.Destinations[6], 3, 10) == 110
            && Simulation.Progression.SkillLearning.Price(0, 0, 10) == 550, "the wing fee or the training price does not carry the tax");
        var aging = Simulation.Items.Aging.AgingRules.Price(3, 1_000, 150, 5);
        var mixing = Simulation.Items.Mixing.MixingWork.Price(1_000, new Simulation.Items.Mixing.MixingSettings(150, Tax: 5));
        var alchemy = Simulation.Items.Mixing.MixingWork.AlchemyTotal(999, new Simulation.Items.Mixing.MixingSettings(Tax: 5));
        Assert.True(aging == 3_150 && CastleRules.TaxIn(aging, 5) == 150 && mixing == 1_575 && CastleRules.TaxIn(mixing, 5) == 75
            && alchemy == 1_048 && CastleRules.TaxIn(alchemy, 5) == 49, "a price percent other than 100 taxed the percent, or its tax was not gathered exactly");
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), CastleRecord.Empty with { OwnerClanId = 7, Tax = 5, Treasury = 1_000 });
        await fixture.TickAsync(Off);
        Assert.True(fixture.World.Tax == 5, "the world was not told the tax");
        fixture.World.TaxGold = 250;
        fixture.Repository.Refuse = true;
        await fixture.TickAsync(Off, TimeSpan.FromMinutes(1));
        Assert.True(fixture.Repository.Saved.Treasury == 1_000, "a refused credit still reached the treasury");
        fixture.Repository.Refuse = false;
        fixture.World.TaxGold = 50;
        await fixture.TickAsync(Off, TimeSpan.FromMinutes(1));
        Assert.True(fixture.Repository.Saved.Treasury == 1_300 && fixture.Repository.Moves is [{ Kind: TreasuryKind.Tax, Amount: 300, ClanId: 7 }],
            "the tax gathered was not credited to the treasury once with its movement");
        fixture.World.TaxGold = 20;
        Assert.True(await fixture.Castle.FlushTaxAsync() && fixture.Repository.Saved.Treasury == 1_320 && fixture.Repository.Moves.Count == 2,
            "the last tax was not stored on shutdown");
    }

    private static Task OwnerWireAsync()
    {
        var buffer = new byte[Protocol.Packets.CastleOwnerStatePacket.MaxSize + Protocol.Packets.CastleOwnerRequestPacket.MaxSize];
        var request = new Protocol.Packets.CastleOwnerRequestPacket(Protocol.Packets.CastleOwnerAction.Withdraw, 42, 7, 2, [3, 1, 0, 2, 0, 0], [20, 4, 1], 123_456);
        var read = Protocol.Packets.CastleOwnerRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer)));
        Assert.True(read with { Towers = [], Mercenaries = [] } == request with { Towers = [], Mercenaries = [] } && read.Towers.SequenceEqual(request.Towers)
            && read.Mercenaries.SequenceEqual(request.Mercenaries), "the owner request did not cross the wire");
        var state = new Protocol.Packets.CastleOwnerStatePacket(true, 9_000_000_000, 3, 5, 2, 3, [3, 0, 0, 0, 0, 1], [8, 4, 2], CanEdit: true);
        var view = Protocol.Packets.CastleOwnerStatePacket.Read(buffer.AsSpan(0, state.Write(buffer)));
        Assert.True(view with { Towers = [], Mercenaries = [] } == state with { Towers = [], Mercenaries = [] } && view.Towers.SequenceEqual(state.Towers)
            && view.Mercenaries.SequenceEqual(state.Mercenaries)
            && !Protocol.Packets.CastleOwnerStatePacket.Read(buffer.AsSpan(0, Protocol.Packets.CastleOwnerStatePacket.None.Write(buffer))).HasView,
            "the owner state did not cross the wire");
        var result = new Protocol.Packets.CastleOwnerResultPacket(Protocol.Packets.CastleOwnerAction.Save, (byte)CastleOwnerStatus.NotEnoughGold);
        Assert.True(result.Write(buffer) == Protocol.Packets.CastleOwnerResultPacket.Size
            && Protocol.Packets.CastleOwnerResultPacket.Read(buffer.AsSpan(0, Protocol.Packets.CastleOwnerResultPacket.Size)) == result,
            "the owner result did not cross the wire");
        return Task.CompletedTask;
    }

    private static Task OwnerWindowAsync()
    {
        var sent = new List<(Protocol.Packets.CastleOwnerAction Action, CastleSetup Setup, long Amount)>();
        var window = new Gameplay.UI.Castle.HudCastleWindow { Send = (action, _, setup, amount) => sent.Add((action, setup, amount)) };
        window.Open(7);
        var setup = CastleSetup.Empty with { Tax = 2, Mercenaries = [3, 0, 0] };
        window.Observe(new Client.Net.Presence.CastleOwnerInfo(true, 1_000, 2, setup, CastleClanSkill.None, CanEdit: true));
        window.Result(new Client.Net.Presence.CastleOwnerResultInfo(Protocol.Packets.CastleOwnerAction.Open, CastleOwnerStatus.Ok));
        Microsoft.Xna.Framework.Point Button(int index, int count) => Gameplay.UI.Widgets.HudNpcPanel.ServiceButton(index, count).Center;
        Microsoft.Xna.Framework.Point Line(int line) => new(Gameplay.UI.Widgets.HudNpcPanel.TextLeft + 20,
            Gameplay.UI.Widgets.HudNpcPanel.TextTop + line * Gameplay.UI.Rendering.HudPainter.LineHeight + 4);
        Assert.True(window.Click(Button(0, 5)) && window.Click(Line(CastleOwnerRules.MaxTaxRate)) && window.Click(Button(0, 2)),
            "the tax page did not take a click");
        Assert.True(sent[^1] is { Action: Protocol.Packets.CastleOwnerAction.Save, Setup.Tax: CastleOwnerRules.MaxTaxRate }, "the highest tax was not saved");
        window.Result(new Client.Net.Presence.CastleOwnerResultInfo(Protocol.Packets.CastleOwnerAction.Save, CastleOwnerStatus.NotEnoughGold));
        window.Observe(new Client.Net.Presence.CastleOwnerInfo(true, 1_000, 2, setup, CastleClanSkill.None, CanEdit: true));
        window.Click(Button(0, 2));
        Assert.True(sent[^1].Setup.Tax == CastleOwnerRules.MaxTaxRate, "a refused save lost the chief's changes");
        window.Result(new Client.Net.Presence.CastleOwnerResultInfo(Protocol.Packets.CastleOwnerAction.Save, CastleOwnerStatus.Ok));
        window.Click(Button(3, 5));
        window.Click(Line(0));
        window.Click(Line(0));
        window.Click(Button(0, 3));
        Assert.True(sent[^1].Setup.Mercenaries is [5, 0, 0], "two clicks did not hire two more mercenaries");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var state = new Protocol.Packets.CastleStatePacket(7, "Lions", 4, 1, League: true, DoorDown: true, 3599, 5,
            [new Protocol.Packets.CastleClanWire(9, "Tigers", 1500), new Protocol.Packets.CastleClanWire(8, "Eagles", 700)], 2200);
        var buffer = new byte[Protocol.Packets.CastleStatePacket.MaxSize];
        var read = Protocol.Packets.CastleStatePacket.Read(buffer.AsSpan(0, state.Write(buffer)));
        Assert.True(read with { Top = [] } == state with { Top = [] } && read.Top.SequenceEqual(state.Top), "the castle's state did not cross the wire");
        var none = Protocol.Packets.CastleStatePacket.Read(buffer.AsSpan(0, Protocol.Packets.CastleStatePacket.None.Write(buffer)));
        Assert.True(none.OwnerClanId == 0 && none.Top.Count == 0, "no castle read as a castle");
        var crowded = state with { Top = [.. Enumerable.Range(0, Contracts.Social.CastleLimits.TopClans + 1).Select(static id => new Protocol.Packets.CastleClanWire((ulong)id, "C", 1))] };
        Assert.Throws<InvalidOperationException>(() => crowded.Write(buffer), "a board of too many clans was written");
        return Task.CompletedTask;
    }

    private static async Task AnnounceAsync()
    {
        var fixture = await Fixture.CreateAsync(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), CastleRecord.Empty with { OwnerClanId = 7, Tax = 3 });
        var told = new List<CastleBroadcast>();
        fixture.Castle.Attach(5, new Told(told));
        await fixture.TickAsync(Off);
        Assert.True(told is [{ OwnerClanId: 0 }, { OwnerClanId: 7, OwnerName: "Lions", Siege: SiegeState.Peace, League: false }] && fixture.World.Owner == 7,
            "the castle was not told at once, or the world not told its owner");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(1));
        Assert.True(told.Count == 2, "the same castle was told twice");
        fixture.Clans.Names[9] = "Tigers";
        await fixture.Castle.StartSiegeAsync(30);
        fixture.World.Siege = fixture.World.Siege with { Crystals = 4, DoorDown = true, Top = [new CastleClanScore(9, 500)], TotalDamage = 500 };
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(1));
        Assert.True(told[^1] is { Siege: SiegeState.Running, DoorDown: true, Crystals: 4, TotalDamage: 500, SecondsLeft: 30 * 60 - 1 } running
            && running.Top is [{ ClanId: 9, Name: "Tigers" }], "the running siege was not told");
        await fixture.TickAsync(League, TimeSpan.FromSeconds(1));
        Assert.True(told[^1] is { League: true, Siege: SiegeState.Running }, "the league mode was not told");
        Assert.True(await fixture.Castle.EndSiegeAsync() && fixture.World.Owner == 7, "the world lost its owner");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(CastleRules.OverSeconds));
        Assert.True(await fixture.Castle.SetOwnerAsync(9) == CastleOwnerResult.Set, "the owner did not change");
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(1));
        Assert.True(fixture.World.Owner == 9 && told[^1].OwnerClanId == 9, "the world was not told the new owner");
        fixture.Castle.Detach(5);
        await fixture.Castle.StartSiegeAsync();
        await fixture.TickAsync(Off, TimeSpan.FromSeconds(1));
        Assert.True(told[^1].Siege == SiegeState.Peace, "a player that left was still told");
        var late = new List<CastleBroadcast>();
        fixture.Castle.Attach(6, new Told(late));
        Assert.True(late is [{ Siege: SiegeState.Running, OwnerClanId: 9 }], "a player arriving was not told the castle as it is");
    }

    private sealed class Told(List<CastleBroadcast> told) : ICastleLink
    {
        public void Deliver(CastleBroadcast castle) => told.Add(castle);
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
        var admin = await chat.JoinAsync("Admin", 0, 0, GameMasterLevel.Administrator);
        await chat.SayAsync(admin, "/easy_castle");
        Assert.True(admin.Link.Last == "> Every castle defender has one life left" && fixture.World.Calls[^1] == "weaken", admin.Link.Last);
        await chat.SayAsync(gm, "/end_castle");
        Assert.True(gm.Link.Last == "> Bless Castle siege ended" && fixture.Castle.Siege == SiegeState.Over, gm.Link.Last);
        await chat.SayAsync(gm, "/end_castle");
        Assert.True(gm.Link.Last == "> No siege is running", gm.Link.Last);
        await chat.SayAsync(admin, "/easy_castle");
        Assert.True(admin.Link.Last == "> No siege is running", admin.Link.Last);
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
        public required FakeWorld World { get; init; }

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
            var world = new FakeWorld();
            var castle = new CastleAppService(repository, clans, chat, world, clock);
            await castle.LoadAsync();
            return new Fixture { Castle = castle, Repository = repository, Clans = clans, Clock = clock, Link = link, World = world };
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

        public List<TreasuryMove> Moves { get; } = [];

        public bool StalePayer { get; set; }

        public Task<CastleRecord> LoadAsync(CancellationToken ct = default) => Task.FromResult(Saved);

        public Task<bool> SaveWithAsync(CastleRecord castle, Persistence.Abstractions.Characters.CharacterRecord? character, TreasuryMove? move,
            CancellationToken ct = default)
        {
            if (Refuse)
            {
                throw new SqliteException("refused", 5);
            }

            if (StalePayer)
            {
                return Task.FromResult(false);
            }

            Saved = castle;
            if (move is not null)
            {
                Moves.Add(move);
            }

            return Task.FromResult(true);
        }

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

    private sealed class FakeWorld : ICastleWorld
    {
        public CastleSiegeView Siege { get; set; } = CastleSiegeView.None;

        public List<string> Calls { get; } = [];

        public CastleSiegeOrder? Opened { get; private set; }

        public List<CastleArrival> Arrivals { get; } = [];

        public long Owner { get; private set; }

        public CastleClanSkill Skill { get; private set; }

        public int Tax { get; private set; }

        public long TaxGold { get; set; }

        public void SetOwner(long clanId, CastleClanSkill skill, int tax) => (Owner, Skill, Tax) = (clanId, skill, tax);

        public long TakeTaxGold()
        {
            var gold = TaxGold;
            TaxGold = 0;
            return gold;
        }

        public Task OpenSiegeAsync(CastleSiegeOrder order)
        {
            Opened = order;
            Calls.Add("open");
            Siege = CastleSiegeView.None with { Running = true };
            return Task.CompletedTask;
        }

        public Task CloseSiegeAsync(long ownerClanId)
        {
            Calls.Add($"close {ownerClanId}");
            Siege = Siege with { Running = false };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CastleArrival>> EndSiegeAsync(long ownerClanId)
        {
            Calls.Add("end " + ownerClanId);
            Siege = CastleSiegeView.None;
            return Task.FromResult<IReadOnlyList<CastleArrival>>([.. Arrivals]);
        }

        public Task<bool> WeakenSiegeAsync()
        {
            Calls.Add("weaken");
            return Task.FromResult(true);
        }

        public Task<PreparedNpcPayment?> PrepareOwnerPaymentAsync(PlayerSession session, ulong npcEntityId, long fee) => throw new NotSupportedException();

        public Task<bool> CommitOwnerPaymentAsync(PlayerSession session, PreparedNpcPayment payment) => throw new NotSupportedException();

        public Task<PlayerSession?> FountainRecipientAsync(long ownerClanId, ulong after) => Task.FromResult<PlayerSession?>(null);

        public Task<PreparedCharacterOperation<bool>?> PrepareFountainPotionAsync(PlayerSession session) => throw new NotSupportedException();

        public Task<bool> CommitFountainPotionAsync(PlayerSession session, PreparedCharacterOperation<bool> operation) => throw new NotSupportedException();
    }

    private sealed class FakeClans : IClanDirectory
    {
        public Dictionary<long, string> Names { get; } = new();

        public bool Available { get; init; }

        public string? NameOf(long clanId) => Names.GetValueOrDefault(clanId);

        public string? ChiefNameOf(long clanId) => Names.ContainsKey(clanId) ? "Chief" : null;

        public long IdOf(string name) => Names.FirstOrDefault(pair => pair.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;

        public Dictionary<ulong, long> Members { get; } = new();

        public Dictionary<ulong, Simulation.Social.Clans.ClanRank> Ranks { get; } = new();

        public long ClanOf(ulong entityId) => Members.GetValueOrDefault(entityId);

        public Simulation.Social.Clans.ClanRank? RankOf(ulong entityId) => Ranks.TryGetValue(entityId, out var rank) ? rank : null;
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan by) => _now += by;
    }
}
