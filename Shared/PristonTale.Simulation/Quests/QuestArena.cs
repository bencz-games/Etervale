using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

// The duel arena of the third and fourth rank-ups and of Fury of Phantom. Each duel is one player against the opponent the quest
// opened for that player; inside the arena a player sees and fights only that opponent and its helpers.
public static class QuestArena
{
    public const int FieldCode = FieldCodes.QuestArena;
    public const int EntryX = 22497;
    public const int EntryZ = 9605;
    public const int OpponentX = 22091;
    public const int OpponentZ = 9673;

    // The opponent leaves the arena half an hour after it appeared, Hestian after a quarter of an hour.
    public static long OpponentLifetimeMs(QuestCode quest) => quest == QuestCode.RankUpHestian ? 15 * 60 * 1000 : 30 * 60 * 1000;

    // The class masters give up at a sixteenth of their life and stand still; Fury must be beaten with no potion left.
    public static bool MasterYields(int life, int maxLife) => maxLife > 0 && life <= maxLife >> 4;

    public static int YieldLife(int maxLife) => Math.Max(1, maxLife >> 4);

    // The duel grows with the player: from level 60 on, every level adds one and a half percent of life and damage.
    public static int StrengthPercent(int playerLevel)
    {
        var levels = Math.Max(0, playerLevel - 60);
        return levels + levels / 2;
    }

    // Life and damage are 32-bit: the grown value saturates instead of wrapping (no 16-bit ceiling).
    public static int ScaledLife(int life, int playerLevel) => Grown(life, playerLevel);

    public static int ScaledDamage(int damage, int playerLevel) => Grown(damage, playerLevel);

    private static int Grown(int value, int playerLevel) =>
        (int)Math.Clamp(value + (long)value * StrengthPercent(playerLevel) / 100, int.MinValue, int.MaxValue);

    // Three potions on top of the opponent's own; one is drunk whenever its life falls under 30 percent.
    public const int ExtraPotions = 3;
    public const int PotionThresholdPercent = 30;

    // A drunk potion restores half the maximum life; the magic master heals a quarter with a spell instead.
    public static int PotionHeal(int maxLife, bool magicMaster) => magicMaster ? maxLife >> 2 : maxLife >> 1;

    // The magic master may vanish to a random point within 300 units of the arena centre when the player comes close.
    public const int MagicMasterBlinkPercent = 3;
    public const float MagicMasterBlinkReach = 300f;

    // While using a skill an opponent may call one helper at a time: the skill master a metal golem or a wolverine, the
    // magic master a fire elemental.
    public const int HelperPercent = 3;
    public const long HelperLifetimeMs = 5 * 60 * 1000;
}
