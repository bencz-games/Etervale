using System.Numerics;
using PristonTale.Application.Settings;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Bellatra;

namespace PristonTale.Tests;

internal static class BellatraTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("Bellatra opens by the server's hour, sells tickets at game hours 4 and 5 for 300 gold a level from 40, and closes a full team",
            EntryRulesAsync);
        yield return ("Bellatra's rooms hold their rectangles, rounds fill their rooms, the best teams go on and the arena bounds the game", ArenaAsync);
        yield return ("Bellatra's three tables play eight rounds of four monsters by cumulative chance, bosses, caps, delays and the final's need",
            RoundsAsync);
        yield return ("Bellatra's kill score, the boss's bonus, the prize less the tax and the teams' ranking", ScoreAsync);
        yield return ("Bellatra's settings are read from the server settings table and refused when out of range", SettingsAsync);
    }

    private static Task EntryRulesAsync()
    {
        Assert.True(BellatraRules.IsOpen(BellatraMode.Hours, 0) && BellatraRules.IsOpen(BellatraMode.Hours, 17) && !BellatraRules.IsOpen(BellatraMode.Hours, 6)
            && !BellatraRules.IsOpen(BellatraMode.Hours, 18) && BellatraRules.IsOpen(BellatraMode.Always, 9) && !BellatraRules.IsOpen(BellatraMode.Off, 1),
            "Bellatra's open hours differ");
        Assert.True(BellatraRules.SellsTickets(4) && BellatraRules.SellsTickets(5) && !BellatraRules.SellsTickets(6), "the ticket hours differ");
        Assert.True(BellatraRules.Fee(40) == 12_000 && BellatraRules.TeamOfBooth(1) == 0 && BellatraRules.TeamOfBooth(4) == 3 && BellatraRules.TeamOfBooth(5) == -1,
            "the fee or the booths' teams differ");
        Assert.True(BellatraRules.Sells(false, 80, 4, 0) == BellatraEntry.Closed && BellatraRules.Sells(true, 39, 4, 0) == BellatraEntry.LevelTooLow
            && BellatraRules.Sells(true, 40, 6, 0) == BellatraEntry.NotTicketTime && BellatraRules.Sells(true, 40, 5, 550) == BellatraEntry.TeamFull
            && BellatraRules.Sells(true, 40, 5, 549) == BellatraEntry.Ok, "a booth's refusals differ");
        return Task.CompletedTask;
    }

    private static Task ArenaAsync()
    {
        var first = BellatraArena.Rooms[0];
        Assert.True(BellatraArena.Rooms.Length == 7 && Inside(first, first.Centre) && !Inside(first, first.Corner0), "the rooms differ");
        var rng = new CombatRng(5);
        for (var draw = 0; draw < 50; draw++)
        {
            var point = first.RandomPoint(rng);
            Assert.True(Inside(first, point) && point.Y == 1154, "a monster's point left its room");
        }

        Assert.True(BellatraArena.RoomsOf(1) == (0, 4) && BellatraArena.RoomsOf(5) == (4, 6) && BellatraArena.RoomsOf(8) == (6, 7) && BellatraArena.RoomsOf(9) == (0, 0),
            "the rounds' rooms differ");
        Assert.True(BellatraArena.GoOn(4) == 2 && BellatraArena.GoOn(7) == 1 && BellatraArena.GoOn(5) == 4 && BellatraArena.AdvanceRoom(0, 4) == 4
            && BellatraArena.AdvanceRoom(1, 4) == 5 && BellatraArena.AdvanceRoom(0, 7) == 6, "the teams going on differ");
        Assert.True(BellatraArena.InArea(first.Centre) && BellatraArena.InArea(BellatraArena.Rooms[6].Centre) && !BellatraArena.InArea(first.Centre with { Y = 1000 })
            && !BellatraArena.InArea(new Vector3(18516, 1154, 3000)), "the event area differs");
        return Task.CompletedTask;
    }

    private static Task RoundsAsync()
    {
        foreach (var table in BellatraRounds.Tables)
        {
            Assert.True(table.Length == BellatraRounds.Count && table.Select(static round => round.StartHour).SequenceEqual([6, 8, 10, 12, 15, 18, 21, 0])
                && table.All(static round => round.Monsters[^1].Cumulative == 100) && table[^1].SkipBelow == BellatraRounds.FinalNeeds
                && table.Count(static round => round.Boss != "") == 3 && table.Select(static round => round.MaxMonsters).SequenceEqual([40, 40, 40, 35, 30, 25, 20, 15])
                && table.Select(static round => round.DelaySeconds).SequenceEqual([5, 5, 5, 10, 5, 5, 15, 5]), "a round table differs");
        }

        var opening = BellatraRounds.Tables[1][0];
        Assert.True(opening.Pick(0) == 0 && opening.Pick(40) == 0 && opening.Pick(41) == 1 && opening.Pick(81) == 2 && opening.Pick(100) == 2
            && opening.Monsters[1].Name == "NorthGoblin" && BellatraRounds.Tables[0][7].Boss == "Doom Lord", "a round's picks differ");
        return Task.CompletedTask;
    }

    private static Task ScoreAsync()
    {
        Assert.True(BellatraRules.Score(3500, 35, false, 3) == 100 && BellatraRules.Score(3500, 35, true, 3) == 600 && BellatraRules.Score(3500, 0, false, 1) == 3500,
            "the kill score differs");
        Assert.True(BellatraRules.Prize(10_000, 5) == 9_500 && BellatraRules.TaxOf(10_000, 5) == 500 && BellatraRules.Prize(-5, 5) == -5 && BellatraRules.TaxOf(99, 5) == 4,
            "the prize or its tax differs");
        Assert.True(BellatraRules.Rank([10, 40, 40, 5], [true, true, true, true]) is [1, 2, 0, 3] && BellatraRules.Rank([0, 0, 0, 0], [true, true, true, true]) is [0, 1, 2, 3]
            && BellatraRules.Rank([1000, 500, 300, 0], [false, true, true, false]) is [1, 2, 0, 3], "the teams' ranking differs, or a team with nobody left ranks");
        Assert.True(BellatraRules.HourReached(5, 6, 6) && !BellatraRules.HourReached(6, 6, 6) && BellatraRules.HourReached(3, 9, 8) && BellatraRules.HourReached(23, 1, 0)
            && !BellatraRules.HourReached(22, 23, 0) && !BellatraRules.HourReached(7, 7, 8), "the hours reached differ");
        return Task.CompletedTask;
    }

    // The room's rectangle, bounds excluded.
    internal static bool Inside(BellatraRoom room, Vector3 point) =>
        point.X > room.Corner0.X && point.X < room.Corner1.X && point.Z > room.Corner1.Z && point.Z < room.Corner0.Z;

    private static Task SettingsAsync()
    {
        var problems = new List<string>();
        var settings = ServerSettingCatalog.Read(new Dictionary<string, string> { ["bellatra.mode"] = "2", ["bellatra.tax"] = "10", ["bellatra.score_divisor"] = "0" },
            null, problems);
        Assert.True(settings.World.Bellatra is { Mode: BellatraMode.Always, Tax: 10, ScoreDivisor: BellatraRules.DefaultScoreDivisor } && problems.Count == 1
            && problems[0].StartsWith("bellatra.score_divisor", StringComparison.Ordinal), "Bellatra's settings were not read, or a refused one was kept");
        return Task.CompletedTask;
    }
}
