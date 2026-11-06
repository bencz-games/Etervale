namespace PristonTale.Contracts.Texts;

// The phrases of GameText, carried on the wire as a uint16; the client words each as "Game.<name>" of its texts.
// Append only. The placeholders a phrase takes follow its name.
public enum GameTextId : ushort
{
    None = 0,

    // Item rules shared by inventory, use, NPC services, trade, personal shops, quests and gates.
    OverWeight = 1,
    CannotUseYet = 2,
    CannotDrop = 3,
    PotionNotStored = 4,
    NotEnoughGold = 5,
    NoSpace = 6,
    GoldLimit = 7,
    StorageRejected = 8,
    StaleInventory = 9,

    // The answer to a retried operation the server had already applied.
    AlreadyApplied = 10,

    // Refusals of any character operation before its domain rules.
    NoSession = 11,
    CharacterBusy = 12,
    WarehouseChanged = 13,
    NotPlaying = 14,
    TooManyRequests = 15,
    RequestRefused = 16,

    // The client could not reach the server.
    ConnectionError = 17,

    // {0} a name the server words (an item, a class title, a destination).
    Name = 18,

    // Item use.
    ItemUsed = 100,
    ItemNotUsable = 101,
    InventoryChanged = 102,
    ActionPending = 103,
    ItemAlreadyRunning = 104,
    SameAttributeRunning = 105,
    ItemNotHere = 106,
    LevelTooLow = 107,
    NothingToChange = 108,
    DeadCannotUse = 109,
    UseDelay = 110,
    OperationConflict = 111,
    CharacterUnchanged = 112,
    ClanOnly = 113,
    MaleCannotUse = 114,
    FemaleCannotUse = 115,
    CrystalRefused = 116,

    // {0} crystal summons at a time.
    SummonLimit = 117,
    ItemNotUsed = 118,

    // {0} the item's name.
    ItemNamedUsed = 119,
    StatsReset = 120,
    SkillsReset = 121,
    SkillsMastered = 122,
    AgedItemMatured = 123,
    GravityStoneUsed = 124,

    // {0} the item's name.
    ItemReceived = 125,
    LuckyBagHeld = 126,

    // {0} the item's name, {1} the count.
    ItemsReceived = 127,

    // Inventory and ground.
    DropOutOfReach = 200,
    PickupNoRoom = 201,
    NoRoomOnGround = 202,
    PotionNotNow = 203,
    ItemInTrade = 204,
    CloseShopFirst = 205,

    // Progression.
    SkillMasterAway = 250,
    ProgressionRefused = 251,

    // NPCs and their services.
    NpcOutOfReach = 300,
    NpcNoSuchService = 301,
    ServiceNotAvailable = 302,
    OtherTownMaster = 303,
    WarehouseNotOpen = 304,
    ServiceStale = 305,
    NpcRefused = 306,
    WrongPuzzle = 307,
    WrongMaterials = 308,
    NoShopHere = 309,
    ShopBuysAndRepairs = 310,
    ShopRefused = 311,
    WarehouseUnreadable = 312,
    EnterWorldFirst = 313,
    CloseTradeFirst = 314,
    AlreadyTalking = 315,

    // Personal shops.
    ShopNotInTown = 400,
    ShopNotListable = 401,
    SellerShopClosed = 402,
    ShopSoldOut = 403,
    ShopCountChanged = 404,
    ShopPriceChanged = 405,
    ShopTradePending = 406,
    SellerNotFound = 407,
    PersonalShopRefused = 408,

    // Quests: notices, dialog titles, confirmation lines and refusals. QuestKillCount: {0} the monster, {1} the kills
    // left; QuestHunt and QuestHuntFinished: {0} the count, {1} the monster; QuestItemFrom: {0} the monster;
    // QuestItemGone: {0} the item.
    QuestInProgress = 500,
    StatPointsFive = 501,
    SkillPointOne = 502,
    StatFiveSkillOne = 503,
    StatFiveSkillTwo = 504,
    DeathPenaltyReduced = 505,
    VitalityForty = 506,
    SevenStatPoints = 507,
    TenStatPoints = 508,
    TrainingReset = 509,
    MissionTimedOut = 510,
    LifeFifteen = 511,
    QuestItemLevelUp = 512,
    QuestItemSlowed = 513,
    FuryWon = 514,
    FuryLost = 515,
    HestianWon = 516,
    HestianLost = 517,
    RewardReceived = 518,
    TwentyBargons = 519,
    SkillsRedistributed = 520,
    QuestTitle = 521,
    FuryTitle = 522,
    ConfirmDoYouWanna = 523,
    ConfirmDoYouWantTo = 524,
    ConfirmCarryOut = 525,
    ConfirmRankUp = 526,
    ConfirmRankUpFifth = 527,
    ConfirmWantToDo = 528,
    ConfirmProcessQuest = 529,
    ConfirmTheQuest = 530,
    ConfirmSearchQuest = 531,
    ConfirmGetQuest = 532,
    ConfirmReceiveItem = 533,
    ConfirmBattleZone = 534,
    ConfirmRedistribute = 535,
    SkillPointName = 536,
    QuestKillCount = 537,
    QuestHunt = 538,
    QuestItemFrom = 539,
    QuestHuntFinished = 540,
    QuestItemGone = 541,
    InvalidQuestRequest = 542,
    TalkToNpcFirst = 543,
    QuestMovedOn = 544,
    NoQuestWeapon = 545,
    NoClassChange = 546,
    NoSkillRedistribution = 547,
    ConfirmExchangeItem = 548
}
