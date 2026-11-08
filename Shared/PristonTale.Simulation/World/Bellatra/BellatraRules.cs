using System.Collections.Immutable;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters.Skills;

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

    // A scored kill drops a surprise (SurprisePerMille kills in a thousand), chosen by cumulative percent; an entrant
    // picking one up in a room breaks its seal there at once.
    public const int SurprisePerMille = 20;

    public static ImmutableArray<(string Key, BellatraSeal Seal, int Cumulative)> Surprises { get; } =
    [
        ("SD201", BellatraSeal.Quake, 20), ("SD202", BellatraSeal.Stun, 40), ("SD203", BellatraSeal.Freeze, 60), ("SD204", BellatraSeal.Rabbit, 68),
        ("SD205", BellatraSeal.Stygian, 80), ("SD206", BellatraSeal.Guardian, 95), ("SD207", BellatraSeal.Points, 100)
    ];

    public static string SurpriseFor(int percentRoll)
    {
        foreach (var (key, _, cumulative) in Surprises)
        {
            if (percentRoll < cumulative)
            {
                return key;
            }
        }

        return Surprises[^1].Key;
    }

    public static bool TrySeal(string catalogKey, out BellatraSeal seal)
    {
        foreach (var surprise in Surprises)
        {
            if (surprise.Key.Equals(catalogKey, StringComparison.OrdinalIgnoreCase))
            {
                seal = surprise.Seal;
                return true;
            }
        }

        seal = default;
        return false;
    }

    // The seals: the quake takes a third of every non-boss round monster's whole life in the room (never its last), the
    // stun holds the room's round monsters StunMs and the freeze FreezeMs (held as stunned, shown with the ice's tint), the
    // rabbit turns about half the room's non-boss
    // round monsters into Rabies and opens a Mutant Rabie boss at its centre, the Stygian lord (level StygianLevel) and the
    // Guardian Saint fight for the picker SummonMs from the room's centre, and the points add PointsBonus to the picker
    // and its team.
    // The notice telling a seal's breaking to the picker's team.
    public static GameTextId NoticeOf(BellatraSeal seal) => seal switch
    {
        BellatraSeal.Quake => GameTextId.BellatraQuake,
        BellatraSeal.Stun => GameTextId.BellatraStun,
        BellatraSeal.Freeze => GameTextId.BellatraFreeze,
        BellatraSeal.Rabbit => GameTextId.BellatraRabbit,
        BellatraSeal.Stygian => GameTextId.BellatraStygian,
        BellatraSeal.Guardian => GameTextId.BellatraGuardian,
        _ => GameTextId.BellatraPoints
    };

    public static bool TrySealOfNotice(GameTextId text, out BellatraSeal seal)
    {
        foreach (var candidate in Enum.GetValues<BellatraSeal>())
        {
            if (NoticeOf(candidate) == text)
            {
                seal = candidate;
                return true;
            }
        }

        seal = default;
        return false;
    }

    public const int QuakeShare = 3;
    public static readonly EffectSpec StunSeal = MonsterStatuses.StunSpec(MonsterStatuses.LightningStun);
    public static readonly EffectSpec FreezeSeal = MonsterStatuses.StunSpec(MonsterStatuses.IceSlow);
    public const int StunMs = 10_000;
    public const int FreezeMs = 15_000;
    public const int StygianLevel = 85;
    public const int SummonMs = 4 * 60_000;
    public const long PointsBonus = 5_000;

    // At the end after the last round, every round monster still standing leaves a Mystic mana potion.
    public static readonly uint EndPotion = Items.ItemCodes.MysticManaPotion;
}

public enum BellatraSeal : byte
{
    Quake = 0,
    Stun = 1,
    Freeze = 2,
    Rabbit = 3,
    Stygian = 4,
    Guardian = 5,
    Points = 6
}
