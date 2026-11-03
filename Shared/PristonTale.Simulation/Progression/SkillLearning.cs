using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Progression;

// Skill-master training. The class skill list is in skill-window order: each class tier (0..4) opens the next four
// skills, a skill needs one rank in the skill before it, rank r+1 needs level RequiredLevel + 2r, and the price grows
// with the window position. The first twelve skills spend skill points; the fourth and fifth tiers' eight share the
// elite skill points.
public static class SkillLearning
{
    public const int SkillsPerTier = 4;
    public const int EliteStart = 12;
    // The ranks a skill takes.
    public const int MaxRank = 10;

    private static readonly int[] BasePrices = [500, 1000, 1500, 2000, 3000, 5000, 7000, 9000, 10000, 12000, 15000, 20000, 25000, 30000, 35000, 40000,
        50000, 60000, 70000, 80000];
    private static readonly int[] RankPrices = [500, 600, 700, 800, 1000, 1200, 1400, 1600, 2000, 2500, 3000, 4000, 5000, 6000, 7000, 8000,
        10000, 12000, 14000, 16000];

    public static int Price(int index, int currentRank)
    {
        if (index < 0 || index >= BasePrices.Length || currentRank is < 0 or >= MaxRank)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return BasePrices[index] + RankPrices[index] * currentRank;
    }

    // Skills a class tier has opened in the window (tier 0 = the first four).
    public static int OpenedBy(int tier) => SkillsPerTier * (Math.Clamp(tier, 0, PlayerCharacter.LastTier) + 1);

    public static bool UsesElitePoints(int index) => index >= EliteStart;

    // Whether the window skill at index can take its next rank, money aside. A definition without its ten ranks of costs
    // cannot be trained.
    public static bool CanTrain(IReadOnlyList<Skill> skills, int index, Func<uint, int> rankOf, int level, int tier, int skillPoints,
        int elitePoints)
    {
        if (index < 0 || index >= skills.Count || index >= OpenedBy(tier))
        {
            return false;
        }

        var skill = skills[index];
        var rank = rankOf(skill.Code);
        var points = UsesElitePoints(index) ? elitePoints : skillPoints;
        return skill.Code != 0 && skill.ManaByRank.Count == MaxRank && rank < MaxRank && points > 0 && level >= skill.RequiredLevel + rank * 2
            && (index == 0 || rankOf(skills[index - 1].Code) > 0);
    }

    public static bool TryQuote(PlayerCharacter character, uint code, out int price, out bool elite)
    {
        price = 0;
        elite = false;
        var skills = character.Class.Skills;
        for (var index = 0; index < skills.Count; index++)
        {
            if (skills[index].Code != code)
            {
                continue;
            }

            if (!CanTrain(skills, index, character.Skills.RankOf, character.Level, character.Tier, character.SkillPoints, character.EliteSkillPoints))
            {
                return false;
            }

            price = Price(index, character.Skills.RankOf(code));
            elite = UsesElitePoints(index);
            return character.Money >= price;
        }

        return false;
    }
}
