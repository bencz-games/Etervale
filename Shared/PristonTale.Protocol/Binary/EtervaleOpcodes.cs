namespace PristonTale.Protocol.Binary;

// Message codes. Every code keeps the high word "ET" (0x4554); the low word groups the messages by domain.
public static class EtervaleOpcodes
{
    public const uint OwnerStatus = 0x45540100;
    public const uint Skills = 0x45540101;
    public const uint Inventory = 0x45540102;
    public const uint InventoryCommand = 0x45540110;
    public const uint InventoryResult = 0x45540111;
    public const uint ProgressionCommand = 0x45540120;
    public const uint ProgressionResult = 0x45540121;
    public const uint NpcResult = 0x45540130;
    public const uint NpcServiceRequest = 0x45540131;
    public const uint NpcServiceResult = 0x45540132;
    public const uint ShopCatalog = 0x45540133;
    public const uint ShopQuotes = 0x45540134;
    public const uint Warehouse = 0x45540135;
    public const uint TeleportList = 0x45540136;
    public const uint ItemWork = 0x45540137;
    public const uint CombatEvents = 0x45540140;
    public const uint RemoveObjects = 0x45540150;
    public const uint LeaveWorld = 0x45540160;
    public const uint PresenceClock = 0x45540170;
    public const uint Weather = 0x45540180;

    public const uint SkillCast = 0x45540200;
    public const uint SkillCastResult = 0x45540201;
    public const uint SkillEvents = 0x45540202;
    public const uint StatusEffects = 0x45540203;
    public const uint SkillCooldowns = 0x45540204;

    public const uint ItemDetails = 0x45540300;
    public const uint OwnerItemStatus = 0x45540301;
    public const uint AgingProgress = 0x45540302;
    public const uint ItemUseCommand = 0x45540400;
    public const uint ItemUseResult = 0x45540401;
    public const uint PremiumTimers = 0x45540402;
    public const uint ItemEffects = 0x45540403;
    public const uint DismissCrystalPets = 0x45540404;
    public const uint QuestRequest = 0x45540500;
    public const uint QuestResult = 0x45540501;
    public const uint QuestJournal = 0x45540502;
    public const uint QuestNotices = 0x45540503;
    public const uint PersonalShopOpen = 0x45540600;
    public const uint PersonalShopClose = 0x45540601;
    public const uint PersonalShopView = 0x45540602;
    public const uint PersonalShopBuy = 0x45540603;
    public const uint PersonalShopResult = 0x45540604;
    public const uint PersonalShopListing = 0x45540605;
    public const uint PersonalShopDetails = 0x45540606;
    public const uint PartyInvite = 0x45540700;
    public const uint PartyAnswer = 0x45540701;
    public const uint PartyCommand = 0x45540702;
    public const uint PartyRequest = 0x45540703;
    public const uint PartyMembers = 0x45540704;
    public const uint TradeRequest = 0x45540800;
    public const uint TradeAnswer = 0x45540801;
    public const uint TradeOffer = 0x45540802;
    public const uint TradeLock = 0x45540803;
    public const uint TradeCancel = 0x45540804;
    public const uint TradeAsked = 0x45540805;
    public const uint TradeState = 0x45540806;
    public const uint TradeDetails = 0x45540807;
    public const uint ContactChange = 0x45541000;
    public const uint ContactRefresh = 0x45541001;
    public const uint ContactList = 0x45541002;
    public const uint ClanRequest = 0x45541100;
    public const uint ClanState = 0x45541101;
    public const uint ClanInvite = 0x45541102;
    public const uint ClanResult = 0x45541103;
    public const uint ClanMark = 0x45541104;
    public const uint CastleState = 0x45541200;
    public const uint CastleOwnerRequest = 0x45541201;
    public const uint CastleOwnerState = 0x45541202;
    public const uint CastleOwnerResult = 0x45541203;
    public const uint BellatraClanRequest = 0x45541300;
    public const uint BellatraClanState = 0x45541301;
    public const uint BellatraClanResult = 0x45541302;
    public const uint BellatraBoard = 0x45541303;
    public const uint BellatraResultRequest = 0x45541304;
    public const uint BellatraResult = 0x45541305;

    public const uint PresenceEntries = 0x45540900;
    public const uint DescribeEntity = 0x45540901;
    public const uint EntityDescription = 0x45540902;

    public const uint RestartRequest = 0x45540A00;
    public const uint RestartResult = 0x45540A01;
    public const uint RestartOffer = 0x45540A02;

    public const uint GroundItems = 0x45540B00;
    public const uint GroundItemsRemoved = 0x45540B01;

    public const uint ChatLine = 0x45540C00;
    public const uint ChatSay = 0x45540C01;

    public const uint MovementReport = 0x45540D00;
    public const uint AttackRequest = 0x45540D01;
    public const uint FieldWarp = 0x45540D02;
    public const uint CloseRequest = 0x45540D03;
    public const uint WingGateRequest = 0x45540D04;
    public const uint WingGateResult = 0x45540D05;
    public const uint YahooKey = 0x45540D06;
    public const uint SwingRequest = 0x45540D07;

    public const uint EnterWorld = 0x45540E00;
    public const uint WorldEntry = 0x45540E01;
    public const uint JoinWorld = 0x45540E02;
    public const uint WorldJoined = 0x45540E03;

    public const uint ServerHello = 0x45540F00;
    public const uint Login = 0x45540F01;
    public const uint LoginResult = 0x45540F02;
    public const uint CharacterList = 0x45540F03;
    public const uint CharacterCommand = 0x45540F04;
    public const uint CharacterResult = 0x45540F05;
    public const uint Heartbeat = 0x45540F06;
}
