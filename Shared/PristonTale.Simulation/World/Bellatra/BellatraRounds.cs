using System.Collections.Immutable;

namespace PristonTale.Simulation.World.Bellatra;

// A monster of a round with its cumulative chance (a roll 0..99 picks the first whose Cumulative it does not pass).
public sealed record BellatraMonster(string Name, int Cumulative);

// A round: it starts at its game hour, fills each of its rooms up to MaxMonsters after DelaySeconds, with one Boss (none
// when empty) a room once the room has opened more than MaxMonsters - BossMargin; a round with SkipBelow is played only when an entered
// player in the area has at least that score.
public sealed record BellatraRound(int StartHour, ImmutableArray<BellatraMonster> Monsters, string Boss, int MaxMonsters, int DelaySeconds, int SkipBelow = 0)
{
    public const int BossMargin = 10;

    public int Pick(int roll)
    {
        for (var index = 0; index < Monsters.Length; index++)
        {
            if (roll <= Monsters[index].Cumulative)
            {
                return index;
            }
        }

        return Monsters.Length - 1;
    }
}

// The three tables of eight rounds; a game plays one picked at random. The game ends at EndHour.
public static class BellatraRounds
{
    public const int Count = 8;
    public const int EndHour = 3;
    public const int Delay = 5;
    public const int FinalNeeds = 50_000;

    public static ImmutableArray<ImmutableArray<BellatraRound>> Tables { get; } =
    [
        [
            Round(6, 40, Delay, "", ("Bee Dog", 40), ("Cokris", 40), ("Decoy", 20)),
            Round(8, 40, Delay, "", ("Bargon", 35), ("Skeleton Archer", 20), ("Crypt", 45)),
            Round(10, 40, Delay, "Unholy Knight", ("Cyclops Knight", 35), ("Skeleton Archer", 30), ("Skeleton Warrior", 35)),
            Round(12, 35, Delay * 2, "", ("Head Cutter", 45), ("Skeleton Ranger", 25), ("Evil Maple", 20), ("web", 10)),
            Round(15, 30, Delay, "", ("Illusion Knight", 30), ("Figon", 20), ("Doom Guard", 40), ("Greven", 10)),
            Round(18, 25, Delay, "Vermun Actarun", ("Hulk", 20), ("Figon", 20), ("Slaughter", 40), ("Shadow", 20)),
            Round(21, 20, Delay * 3, "", ("Omicron", 30), ("Stygian", 40), ("Hulk", 20), ("Doom Guard", 10)),
            Round(0, 15, Delay, "Doom Lord", ("Iron Guard", 20), ("Hopy King", 30), ("Incubus", 30), ("Hyper Machine", 20)) with { SkipBelow = FinalNeeds }
        ],
        [
            Round(6, 40, Delay, "", ("Mephit", 40), ("NorthGoblin", 40), ("Devilish Tree", 20)),
            Round(8, 40, Delay, "", ("Bargon", 25), ("Mephit", 40), ("Buma", 35)),
            Round(10, 40, Delay, "Unholy Knight", ("Cyclops Knight", 30), ("Skeleton Archer", 30), ("Skeleton Warrior", 40)),
            Round(12, 35, Delay * 2, "", ("Skeleton Knight", 45), ("Skeleton Ranger", 25), ("Mire Keeper", 20), ("web", 10)),
            Round(15, 30, Delay, "", ("Slaughter", 30), ("Figon", 20), ("Dark Knight", 40), ("Greven", 10)),
            Round(18, 25, Delay, "Vermun Actarun", ("Hulk", 20), ("Sliver", 20), ("Avelisk-L", 40), ("Shadow", 20)),
            Round(21, 20, Delay * 3, "", ("Dawlin", 30), ("Stygian", 40), ("Hulk", 20), ("Doom Guard", 10)),
            Round(0, 15, Delay, "Doom Lord", ("Iron Guard", 20), ("Hopy King", 30), ("Avelisk Lord", 30), ("Hyper Machine", 20)) with { SkipBelow = FinalNeeds }
        ],
        [
            Round(6, 40, Delay, "", ("Mephit", 40), ("Zombi", 40), ("HobGoblin Chielf", 20)),
            Round(8, 40, Delay, "", ("Ghoul", 25), ("Skeleton Archer", 40), ("Cyclops", 35)),
            Round(10, 40, Delay, "Unholy Knight", ("Mighty Goblin", 30), ("Skeleton Archer", 30), ("Armored Bettle", 40)),
            Round(12, 35, Delay * 2, "", ("Titan", 45), ("Skeleton Ranger", 25), ("Mudy Golem", 20), ("web", 10)),
            Round(15, 30, Delay, "", ("Nightmare", 30), ("Figon", 20), ("BeEvil", 40), ("Greven", 10)),
            Round(18, 25, Delay, "Vermun Actarun", ("Hulk", 20), ("Sliver", 20), ("Avelisk-L", 40), ("Shadow", 20)),
            Round(21, 20, Delay * 3, "", ("Omicron", 30), ("Stygian", 40), ("Hulk", 20), ("Doom Guard", 10)),
            Round(0, 15, Delay, "Doom Lord", ("Iron Guard", 20), ("Chain Golem", 30), ("Witch", 30), ("Hyper Machine", 20)) with { SkipBelow = FinalNeeds }
        ]
    ];

    private static BellatraRound Round(int startHour, int maxMonsters, int delaySeconds, string boss, params (string Name, int Percent)[] monsters)
    {
        var cumulative = 0;
        return new BellatraRound(startHour, [.. monsters.Select(monster => new BellatraMonster(monster.Name, cumulative += monster.Percent))], boss, maxMonsters,
            delaySeconds);
    }
}
