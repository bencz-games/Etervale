using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Progression;

// User-facing tiers are numbered 1..5; the character stores tier 0..4.
public static class GmTierProgression
{
    public const int FirstDisplayTier = 1;
    public static int LastDisplayTier => PlayerCharacter.LastTier + FirstDisplayTier;

    public static int ForLevel(int level)
    {
        var tier = 0;
        while (tier < PlayerCharacter.LastTier && level >= RankUpRules.RequiredLevel(tier))
        {
            tier++;
        }

        return tier;
    }

    // The highest level before the next class change; the last tier has the game's level limit.
    public static int HighestLevel(int internalTier)
    {
        if (internalTier < 0 || internalTier > PlayerCharacter.LastTier)
        {
            throw new ArgumentOutOfRangeException(nameof(internalTier));
        }

        return internalTier == PlayerCharacter.LastTier
            ? ExperienceTable.MaximumLevel
            : RankUpRules.RequiredLevel(internalTier) - 1;
    }
}
