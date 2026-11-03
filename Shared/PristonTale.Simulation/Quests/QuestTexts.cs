using System.Globalization;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// The English quest texts of the classic client: confirmation boxes, messages and the quest lines of the chat window.
public static class QuestTexts
{
    public const string QuestInProgress = "You have undergoing Quest";
    public const string NotEnoughSpace = ItemRuleMessages.NoSpace;
    public const string NotEnoughGold = ItemRuleMessages.NotEnoughGold;

    public const string StatPointsFive = "stat point increased by 5";
    public const string SkillPointOne = "skill point increased by 1";
    public const string StatFiveSkillOne = "stat & skill point increased by 5 and 1";
    public const string StatFiveSkillTwo = "stat & skill point increased by 5 and 2";
    public const string DeathPenaltyReduced = "Death penalty Exp decreased by 1";
    public const string VitalityForty = "Vitality increased by 40";
    public const string SevenStatPoints = "Over 80lev, you will get 7 stat-point at each level.";
    public const string TenStatPoints = "Over 90lev, you will get 10 stat-point at each level.";
    public const string TrainingReset = "Reset your numbers for training.";
    public const string MissionTimedOut = "The mission failed by over time.";
    public const string LifeFifteen = "Increased HP +15";
    public const string QuestItemLevelUp = "Level up The quest item";
    public const string QuestItemSlowed = "Decreased ATK speed of quest item";
    public const string FuryWon = "Win in the confrontation with Fury.";
    public const string FuryLost = "Lose in the confrontation with Fury.";
    public const string HestianWon = "You won over Hestian.";
    public const string HestianLost = "You lost to Hestian.";
    public const string RewardReceived = "Acquierd items in compensation.";
    public const string TwentyBargons = "You have killed 20 Bargons.";
    public const string SkillsRedistributed = "Statistics has been reset";

    public const string QuestTitle = " Q U E S T ";
    public const string FuryTitle = "Fury's phantasma";

    public static readonly (string, string) CarryOut = ("Do you wanna", "carry out the quest?");
    public static readonly (string, string) RankUp = ("Do you wanna", "rank up?");
    public static readonly (string, string) RankUpFifth = ("Rank-Up", "want to do?");
    public static readonly (string, string) ProcessQuest = ("Process quest", "want to do?");
    public static readonly (string, string) TheQuest = ("Do you wanna", "the quest?");
    public static readonly (string, string) SearchQuest = ("Do you wanna", "search for the quest?");
    public static readonly (string, string) GetQuest = ("Do you want to", "get the Quest?");
    public static readonly (string, string) ReceiveItem = ("Do you want to", "receive the item?");
    public static readonly (string, string) BattleZone = ("Do you want to", "move to the Battle Zone?");
    public static readonly (string, string) Redistribute = ("Do you wanna", "redistribute?");
    public static readonly (string, string) ReceiveWing = ("Do you wanna", "receive the item?");

    public const string SkillPointName = "Skill Point";

    // "Quest> name count number of monster needed": the running count of a hunt.
    public static string Count(string monster, int kills) =>
        string.Create(CultureInfo.InvariantCulture, $"Quest> {monster} {kills} number of monster needed");

    public static string Hunt(int count, string monster) => string.Create(CultureInfo.InvariantCulture, $"<Quest> Hunt {count} of {monster}");

    public static string GetItemFrom(string monster) => $"<Quest> Get the item from {monster}";

    public static string HuntFinished(int count, string monster) =>
        string.Create(CultureInfo.InvariantCulture, $"<Quest>  Finished hunting {count} of the {monster}");

    public static string QuestItemGone(string item) => $"The quest item {item} is gone";

    public static readonly string[] FuryRewardNames =
    [
        "Minotaur Axe", "Extreme Talon", "Dragon Bone Hammer", "Hellfire Scythe", "Revenge Bow", "Immortal Sword", "Salamander Javelin",
        "Gothic Staff"
    ];

}
