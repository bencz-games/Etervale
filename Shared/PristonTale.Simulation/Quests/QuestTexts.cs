using System.Globalization;
using PristonTale.Contracts.Texts;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Quests;

// The quest texts of the classic client: confirmation boxes, messages and the quest lines of the chat window, as game
// texts the client words in its language.
public static class QuestTexts
{
    public static readonly GameText QuestInProgress = GameText.Of(GameTextId.QuestInProgress);
    public static readonly GameText NotEnoughSpace = GameText.Of(GameTextId.NoSpace);
    public static readonly GameText NotEnoughGold = GameText.Of(GameTextId.NotEnoughGold);

    public static readonly GameText StatPointsFive = GameText.Of(GameTextId.StatPointsFive);
    public static readonly GameText SkillPointOne = GameText.Of(GameTextId.SkillPointOne);
    public static readonly GameText StatFiveSkillOne = GameText.Of(GameTextId.StatFiveSkillOne);
    public static readonly GameText StatFiveSkillTwo = GameText.Of(GameTextId.StatFiveSkillTwo);
    public static readonly GameText DeathPenaltyReduced = GameText.Of(GameTextId.DeathPenaltyReduced);
    public static readonly GameText VitalityForty = GameText.Of(GameTextId.VitalityForty);
    public static readonly GameText SevenStatPoints = GameText.Of(GameTextId.SevenStatPoints);
    public static readonly GameText TenStatPoints = GameText.Of(GameTextId.TenStatPoints);
    public static readonly GameText TrainingReset = GameText.Of(GameTextId.TrainingReset);
    public static readonly GameText MissionTimedOut = GameText.Of(GameTextId.MissionTimedOut);
    public static readonly GameText LifeFifteen = GameText.Of(GameTextId.LifeFifteen);
    public static readonly GameText QuestItemLevelUp = GameText.Of(GameTextId.QuestItemLevelUp);
    public static readonly GameText QuestItemSlowed = GameText.Of(GameTextId.QuestItemSlowed);
    public static readonly GameText FuryWon = GameText.Of(GameTextId.FuryWon);
    public static readonly GameText FuryLost = GameText.Of(GameTextId.FuryLost);
    public static readonly GameText HestianWon = GameText.Of(GameTextId.HestianWon);
    public static readonly GameText HestianLost = GameText.Of(GameTextId.HestianLost);
    public static readonly GameText RewardReceived = GameText.Of(GameTextId.RewardReceived);
    public static readonly GameText TwentyBargons = GameText.Of(GameTextId.TwentyBargons);
    public static readonly GameText SkillsRedistributed = GameText.Of(GameTextId.SkillsRedistributed);

    public static readonly GameText QuestTitle = GameText.Of(GameTextId.QuestTitle);
    public static readonly GameText FuryTitle = GameText.Of(GameTextId.FuryTitle);

    public static readonly (GameText, GameText) CarryOut = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmCarryOut);
    public static readonly (GameText, GameText) RankUp = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmRankUp);
    public static readonly (GameText, GameText) RankUpFifth = Lines(GameTextId.ConfirmRankUpFifth, GameTextId.ConfirmWantToDo);
    public static readonly (GameText, GameText) ProcessQuest = Lines(GameTextId.ConfirmProcessQuest, GameTextId.ConfirmWantToDo);
    public static readonly (GameText, GameText) TheQuest = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmTheQuest);
    public static readonly (GameText, GameText) SearchQuest = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmSearchQuest);
    public static readonly (GameText, GameText) GetQuest = Lines(GameTextId.ConfirmDoYouWantTo, GameTextId.ConfirmGetQuest);
    public static readonly (GameText, GameText) ReceiveItem = Lines(GameTextId.ConfirmDoYouWantTo, GameTextId.ConfirmReceiveItem);
    public static readonly (GameText, GameText) BattleZone = Lines(GameTextId.ConfirmDoYouWantTo, GameTextId.ConfirmBattleZone);
    public static readonly (GameText, GameText) Redistribute = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmRedistribute);
    public static readonly (GameText, GameText) ReceiveWing = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmReceiveItem);
    public static readonly (GameText, GameText) Exchange = Lines(GameTextId.ConfirmDoYouWanna, GameTextId.ConfirmExchangeItem);

    public static readonly GameText SkillPointName = GameText.Of(GameTextId.SkillPointName);

    // "Quest> name count number of monster needed": the running count of a hunt.
    public static GameText Count(string monster, int kills) => GameText.Of(GameTextId.QuestKillCount, monster, Number(kills));

    public static GameText Hunt(int count, string monster) => GameText.Of(GameTextId.QuestHunt, Number(count), monster);

    public static GameText GetItemFrom(string monster) => GameText.Of(GameTextId.QuestItemFrom, monster);

    public static GameText HuntFinished(int count, string monster) => GameText.Of(GameTextId.QuestHuntFinished, Number(count), monster);

    public static GameText QuestItemGone(string item) => GameText.Of(GameTextId.QuestItemGone, item);

    // A data name shown as it is: an item, a destination.
    public static GameText Name(string name) => GameText.Of(GameTextId.Name, name);

    public static GameText ClassTitle(PlayerClass playerClass, int tier) => GameText.Of(GameTextId.ClassTitle, Number((int)playerClass), Number(tier));

    public static readonly string[] FuryRewardNames =
    [
        "Minotaur Axe", "Extreme Talon", "Dragon Bone Hammer", "Hellfire Scythe", "Revenge Bow", "Immortal Sword", "Salamander Javelin",
        "Gothic Staff"
    ];

    private static (GameText, GameText) Lines(GameTextId first, GameTextId second) => (GameText.Of(first), GameText.Of(second));

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
