namespace PristonTale.Simulation.Quests;

// Quest identities. The values are persisted in the quest journal, carried on the wire and kept in the finished-quest
// history, so they never change. Rank-up quests are named by the mechanic that completes them.
public enum QuestCode : ushort
{
    None = 0,

    // First rank-up of the Tempskron classes: three trophies of the wolverines.
    RankUpTrophies = 0x01,

    // First rank-up of the Morion classes: a royal jelly or a hair tonic brewed for a villager.
    RankUpHoney = 0x02,
    RankUpOil = 0x03,

    // Second rank-up: three vamp sheltoms buy a quest weapon that grows by hunting.
    RankUpQuestWeapon = 0x04,

    Level30 = 0x05,
    Level55 = 0x06,
    Level55Morion = 0x07,
    Level70 = 0x08,
    Level80 = 0x09,
    Level85 = 0x0A,
    Level90 = 0x0B,

    // Bonus chains: seven stat points per level from 80, ten from 90.
    Level80Bonus = 0x0C,
    Level90Bonus = 0x0D,

    // Third rank-up: two timed hunts, an amulet from a named monster and a duel with the class master.
    RankUpMasterTrial = 0x10,

    ElementaryA = 0x20,
    ElementaryB = 0x21,
    ElementaryC = 0x22,
    ElementaryD = 0x23,
    ElementaryE = 0x24,
    ElementaryF = 0x25,
    ElementaryG = 0x26,

    FuryOfPhantom = 0x28,

    // The fourth rank-up, into the fifth tier.
    RankUpHestian = 0x30,

    // The elite chains of levels 106, 110 and 114 (EliteQuests), in order.
    Elite106A = 0x40,
    Elite106B = 0x41,
    Elite106C = 0x42,
    Elite106D = 0x43,
    Elite106E = 0x44,
    Elite106F = 0x45,
    Elite110A = 0x46,
    Elite110B = 0x47,
    Elite110C = 0x48,
    Elite110D = 0x49,
    Elite110E = 0x4A,
    Elite110F = 0x4B,
    Elite110G = 0x4C,
    Elite110H = 0x4D,
    Elite114A = 0x4E,
    Elite114B = 0x4F,
    Elite114C = 0x50,
    Elite114D = 0x51,
    Elite114E = 0x52,
    Elite114F = 0x53,
    Elite114G = 0x54
}

// Completed level-milestone quests (bit per quest); both level 55 branches share one bit.
[Flags]
public enum LevelQuestLog : uint
{
    None = 0,
    Level30 = 1,
    Level55 = 2,
    Level70 = 4,
    Level80 = 8,
    Level85 = 16,
    Level90 = 32,
    Level80Bonus = 64,
    Level90Bonus = 128
}

// Completed beginner quests and Fury of Phantom (bit per quest).
[Flags]
public enum ElementaryQuestLog : uint
{
    None = 0,
    A = 1,
    B = 2,
    C = 4,
    D = 8,
    E = 16,
    F = 32,
    G = 64,
    FuryOfPhantom = 128
}

// Completed elite quests (EliteQuests.Bit): bit n is the chains' n-th quest, Elite106A first.
[Flags]
public enum EliteQuestLog : uint
{
    None = 0
}

public static class QuestCodes
{
    public static bool IsElementary(QuestCode code) => code is >= QuestCode.ElementaryA and <= QuestCode.ElementaryG;

    public static bool IsRankUp(QuestCode code) => code is QuestCode.RankUpTrophies or QuestCode.RankUpHoney or QuestCode.RankUpOil
        or QuestCode.RankUpQuestWeapon or QuestCode.RankUpMasterTrial or QuestCode.RankUpHestian;

    public static bool IsDefined(QuestCode code) => code != QuestCode.None && Enum.IsDefined(code);

    // The elementary quests run in order; the index is also the number of earlier ones completed.
    public static int ElementaryIndex(QuestCode code) => IsElementary(code) ? code - QuestCode.ElementaryA : -1;

    public static QuestCode Elementary(int index) => index is >= 0 and < 7 ? (QuestCode)((int)QuestCode.ElementaryA + index) : QuestCode.None;

    public static ElementaryQuestLog ElementaryBit(int index) => index is >= 0 and < 7 ? (ElementaryQuestLog)(1u << index) : ElementaryQuestLog.None;
}
