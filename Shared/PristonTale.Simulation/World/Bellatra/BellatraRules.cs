namespace PristonTale.Simulation.World.Bellatra;

public enum BellatraMode : byte
{
    Off = 0,

    // Open while the server's local hour is in OpenHours.
    Hours = 1,
    Always = 2
}

// Why a team booth refuses a player, or Ok when it sells the ticket.
public enum BellatraEntry : byte
{
    Ok = 0,
    Closed = 1,
    LevelTooLow = 2,
    NotTicketTime = 3,
    TeamFull = 4
}

// Bellatra's game, following the game clock (WorldClock): tickets sell at game hours TicketHour and TicketHour + 1 while
// Bellatra is open (BellatraMode), the rounds start at their hours (BellatraRounds) and the game ends at
// BellatraRounds.EndHour; a day whose tickets nobody bought plays nothing, and a game running when Bellatra closes
// finishes. A ticket costs FeePerLevel gold a level from MinLevel; a team's ticket office closes once its entrants'
// levels pass TeamLevels.
public static class BellatraRules
{
    public const int TicketHour = 4;
    public const int MinLevel = 40;
    public const int TeamLevels = 549;
    public const long FeePerLevel = 300;
    public const int DefaultTax = 5;
    public const int DefaultScoreDivisor = 35;
    public const int OutSeconds = 30;

    // One monster enters each room of the round every PulseMs; the teams going on or out move BannerMs after the round's
    // start, once their banner has shown.
    public const int PulseMs = 2000;
    public const int BannerMs = 8000;

    public static bool IsOpen(BellatraMode mode, int localHour) => mode switch
    {
        BellatraMode.Always => true,
        BellatraMode.Hours => localHour is >= 0 and < 6 or >= 12 and < 18,
        _ => false
    };

    public static bool SellsTickets(int gameHour) => gameHour is TicketHour or TicketHour + 1;

    public static long Fee(int level) => level * FeePerLevel;

    // Bellatra's secretary, who shows the operating clan and pays a clan's income, is the booth of this value.
    public const int SecretaryBooth = 5;

    // A team booth carries its team (1 to 4) as its value.
    public static int TeamOfBooth(int boothValue) => boothValue is >= 1 and <= BellatraArena.Teams ? boothValue - 1 : -1;

    // Whether a booth sells a ticket to a player of the level now (the team's entrants' levels so far).
    public static BellatraEntry Sells(bool open, int level, int gameHour, int teamLevels) =>
        !open ? BellatraEntry.Closed
        : level < MinLevel ? BellatraEntry.LevelTooLow
        : !SellsTickets(gameHour) ? BellatraEntry.NotTicketTime
        : teamLevels > TeamLevels ? BellatraEntry.TeamFull
        : BellatraEntry.Ok;

    // A kill's score: the monster's experience by the divisor, a boss's times twice the round.
    public static long Score(long experience, int divisor, bool boss, int round) =>
        experience / Math.Max(1, divisor) * (boss ? round * 2 : 1);

    // What a score pays, the tax (0 to 100 percent) kept for the operating clan.
    public static long Prize(long score, int tax) => score - TaxOf(score, tax);

    public static long TaxOf(long score, int tax) => Math.Max(0, score) * Math.Clamp(tax, 0, 100) / 100;

    // The teams from best to worst by their scores, the lower team first on a tie, the teams with nobody left in the arena
    // (present false) last.
    public static int[] Rank(ReadOnlySpan<long> teamScores, ReadOnlySpan<bool> present)
    {
        var order = new int[teamScores.Length];
        for (var team = 0; team < order.Length; team++)
        {
            order[team] = team;
        }

        var scores = teamScores.ToArray();
        var playing = present.ToArray();
        Array.Sort(order, (a, b) => playing[a] != playing[b] ? playing[b].CompareTo(playing[a])
            : scores[b] != scores[a] ? scores[b].CompareTo(scores[a])
            : a.CompareTo(b));
        return order;
    }

    // Whether the game clock, moving from hour from to hour to (0 to 23, a day wrapping), reached target.
    public static bool HourReached(int from, int to, int target)
    {
        var moved = (to - from + 24) % 24;
        var ahead = (target - from + 24) % 24;
        return moved > 0 && ahead > 0 && ahead <= moved;
    }
}
