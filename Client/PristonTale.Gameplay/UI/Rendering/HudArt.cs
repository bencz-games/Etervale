using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.UI.Rendering;

// Asset-relative paths of the original interface images (Image\SinImage\... and Image\...).
public static class HudArt
{
    private const string Sin = "image/Sinimage/";
    private const string Inter = Sin + "Inter/";

    public const string Menu1 = Inter + "Menu-1.tga";
    public const string BlockText = Inter + "block.tga";
    public const string Menu2 = Inter + "Menu-2.tga";
    public const string GrowLeft = Inter + "inter_01.bmp";
    public const string GrowMiddle = Inter + "inter_02.bmp";
    public const string GrowRight = Inter + "inter_03.bmp";
    public const string Life = Inter + "Bar_Life.bmp";
    public const string Mana = Inter + "Bar_Mana.bmp";
    public const string Stamina = Inter + "Bar_Stamina.bmp";
    public const string Experience = Inter + "sinGage/Bar_Exp.bmp";
    public const string Time = Inter + "sinGage/Bar_Time.bmp";
    public const string Sun = Inter + "flash/Sun.bmp";
    public const string Moon = Inter + "flash/Moon.bmp";
    public const string Walk = Inter + "Button/Walk.bmp";
    public const string CameraAuto = Inter + "Button/AutoCameraImage.bmp";
    public const string CameraFixed = Inter + "Button/PixCameraImage.bmp";
    public const string MapOn = Inter + "Button/MapOnImage.bmp";
    public const string RunInfo = Inter + "ButtonInfo/Run.bmp";
    public const string WalkInfo = Inter + "ButtonInfo/Walk.bmp";
    public const string CameraHandInfo = Inter + "ButtonInfo/Camera_Hand.bmp";
    public const string MapOnInfo = Inter + "ButtonInfo/MapOn.bmp";
    public const string MapOffInfo = Inter + "ButtonInfo/MapOff.bmp";
    public const string SystemMenu = Inter + "ExitMenu.tga";
    public const string SystemSettings = Inter + "Settings.bmp";
    public const string SystemHelp = Inter + "Option.bmp";
    public const string SystemExit = Inter + "Exit.bmp";
    // The restart window of a dead player; the skill master's box reuses its frame and exit button.
    public const string RestartMain = Inter + "ReStart/ReStartMain.bmp";
    public const string RestartField = Inter + "ReStart/ReStart_Field.bmp";
    public const string RestartTown = Inter + "ReStart/ReStart_Town.bmp";
    public const string RestartExit = Inter + "ReStart/ReStart_Exit.bmp";
    // The class change button of the skill window, gray until a rank-up quest is open to the pupil.
    public const string SelectJobGray = Inter + "SkillIcon/G_Select_ChangeJob.bmp";
    public const string SelectJob = Inter + "SkillIcon/Select_ChangeJob.bmp";

    public static readonly string[] MenuButtons = Names("bStatus", "bInvenTory", "bSkill", "bParty", "bQuest", "bSystem", ".bmp");
    public static readonly string[] MenuButtonsDown = Names("bStatus", "bInvenTory", "bSkill", "bParty", "bQuest", "bSystem", "-1.bmp");
    public static readonly string[] MenuInfo =
    [
        Inter + "ButtonInfo/StatusInfo.bmp", Inter + "ButtonInfo/InvenInfo.bmp", Inter + "ButtonInfo/SkillInfo.bmp",
        Inter + "ButtonInfo/PartyInfo.bmp", Inter + "ButtonInfo/QuestInfo.bmp", Inter + "ButtonInfo/SystemInfo.bmp"
    ];

    public const string SkillNormal = Sin + "skill/Skill_Normal.bmp";
    public const string SkillWindow = Sin + "skill/Skil24.bmp";
    // The skill window's second page (a fifth tier this version has no skills for) and the arrows between the pages.
    public const string SkillWindowSecond = Sin + "skill/Skil25.bmp";
    public const string SkillPageForward = Sin + "skill/RightArrow.bmp";
    public const string SkillPageBack = Sin + "skill/LeftArrow.bmp";
    public const string SkillSelect = Sin + "skill/SelectSkill.bmp";
    public const string SkillGauge = Sin + "skill/Gage.bmp";
    public const string SkillGaugeBack = Sin + "skill/Gage-3.bmp";
    public const string SkillGaugePassive = Sin + "skill/Gage-4.bmp";
    public static readonly string[] ShortKeys = Enumerable.Range(1, 8).Select(index => $"{Sin}skill/ShortKey/f{index}.bmp").ToArray();

    public const string Inventory = Sin + "Inven/Inven.bmp";
    public const string PuzzleBoard = Sin + "Inven/NYEAR_00.bmp";
    public const string CoverLarge = Sin + "Inven/CoverLarge.bmp";
    public const string CoverMiddle = Sin + "Inven/CoverMiddle.bmp";
    public const string CoverSmall = Sin + "Inven/CoverSmall.bmp";
    public const string PotionBack = Sin + "Inven/PotionBack.bmp";
    public const string SideLeft = Sin + "Inven/LeftArrow.bmp";
    public const string SideRight = Sin + "Inven/RightArrow.bmp";

    // The labels the original shows above an inventory button under the pointer: the bag page (A, B), the weapon pair
    // (A, B) and the close button.
    public static readonly string[] PageHints = [Sin + "Inven/InfoBox/ButtonItemA.bmp", Sin + "Inven/InfoBox/ButtonItemB.bmp"];
    public static readonly string[] PairHints = [Sin + "Inven/InfoBox/ButtonA.bmp", Sin + "Inven/InfoBox/ButtonB.bmp"];
    public const string CloseHint = Sin + "Inven/InfoBox/ButtonExit.bmp";

    // The special item box: its button closed and open (or under the pointer), the 44-pixel box drawn for each of its
    // boxes over the bag, and its labels (open it, close it).
    public const string QuestBoxButton = Sin + "Inven/QuestItem_.bmp";
    public const string QuestBoxButtonOpen = Sin + "Inven/QuestItem.bmp";
    public const string QuestBoxTile = Sin + "Inven/QuestItembox.bmp";
    public const string QuestBoxHint = Sin + "Inven/InfoBox/Questitem.bmp";
    public const string QuestBoxCloseHint = Sin + "Inven/InfoBox/close.bmp";
    public const string QuestBoxGlow = Sin + "Effect/InterEffect/UC-hall.tga";

    public const string Status = Sin + "Status/Status.bmp";
    public const string StatusPoint = Sin + "Status/StatusPoint.bmp";
    public const string StatusArrow = Sin + "Status/ChangeArrow.bmp";
    public const string StatusArrowDown = Sin + "Status/SelectArrow.bmp";

    public static readonly string[] InfoBox =
    [
        Sin + "Items/ItemInfoBox/BoxTopLeft.tga", Sin + "Items/ItemInfoBox/BoxTopCenter.tga", Sin + "Items/ItemInfoBox/BoxTopRight.tga",
        Sin + "Items/ItemInfoBox/BoxLeft.tga", Sin + "Items/ItemInfoBox/BoxCenter.tga", Sin + "Items/ItemInfoBox/BoxRight.tga",
        Sin + "Items/ItemInfoBox/BoxBottomLeft.tga", Sin + "Items/ItemInfoBox/BoxBottomCenter.tga", Sin + "Items/ItemInfoBox/BoxBottomRight.tga"
    ];

    public const string HelpTopLeft = Sin + "help/HelpBoxCorner.tga";
    public const string HelpTop = Sin + "help/HelpBoxLine.tga";
    public const string HelpTopRight = Sin + "help/box3.tga";
    public const string HelpLeft = Sin + "help/box4.tga";
    public const string HelpCenter = Sin + "help/box5.tga";
    public const string HelpRight = Sin + "help/box6.tga";
    public const string HelpBottomLeft = Sin + "help/box7.tga";
    public const string HelpBottom = Sin + "help/box8.tga";
    public const string HelpBottomRight = Sin + "help/box9.tga";
    public const string HelpLine = Sin + "help/Box_Line.tga";
    public const string HelpTitle = Sin + "help/box-test-title.bmp";
    public const string HelpButton = Sin + "help/box-test-title2.bmp";
    public const string ScrollBar = Sin + "help/scl_bar.tga";
    public const string ScrollButton = Sin + "help/scl_icon.tga";
    // The guide box (HudPainter.GuideBox): corners, edges and fill, left to right and top to bottom.
    public static readonly string[] GuideBox = Enumerable.Range(1, 9).Select(index => $"{Sin}Help/GH-Box0{index}.tga").ToArray();

    public const string MessageBox = Sin + "MessageBox/BoxMain.tga";
    public const string NoticeLeft = Sin + "MessageBox/CutBox/BoxLeft.tga";
    public const string NoticeCenter = Sin + "MessageBox/CutBox/BoxCenter.tga";
    public const string NoticeRight = Sin + "MessageBox/CutBox/BoxRight.tga";
    public const string OkGray = Sin + "MessageBox/Button_Ok_Glay.bmp";
    public const string OkYellow = Sin + "MessageBox/Button_Ok_Yellow.bmp";
    public const string YesGray = Sin + "MessageBox/Button_Yes_Glay.bmp";
    public const string YesYellow = Sin + "MessageBox/Button_Yes_Yellow.bmp";
    public const string NoGray = Sin + "MessageBox/Button_No_Glay.bmp";
    public const string NoYellow = Sin + "MessageBox/Button_No_Yellow.bmp";
    public const string CancelGray = Sin + "MessageBox/Button_Cancel_Glay.bmp";
    public const string CancelYellow = Sin + "MessageBox/Button_Cancel_Yellow.bmp";
    public const string CountField = Sin + "MessageBox/COUNT.bmp";
    public const string CountUp = Sin + "MessageBox/Arrow_Up.bmp";
    public const string CountDown = Sin + "MessageBox/Arrow_Down.bmp";

    public const string MapBox = "image/MapBox.tga";
    public const string MapArrow = "image/ARROW.tga";
    public const string MapNpc = "image/npc.tga";
    public const string TargetBox = "image/cw.tga";
    public const string EnergyBack = "image/Energy_Red.tga";
    public const string EnergyFill = "image/Energy_Blue.tga";
    public const string BroodNormal = "image/M_Nm.tga";
    public const string BroodMutant = "image/M_Mt.tga";
    public const string BroodUndead = "image/M_Un.tga";
    public const string BroodDemon = "image/M_D.tga";
    public const string BroodMechanic = "image/M_mc.tga";

    public static readonly string[] Static =
    [
        Menu1, Menu2, GrowLeft, GrowMiddle, GrowRight, Life, Mana, Stamina, Experience, Time, Sun, Moon,
        Walk, CameraAuto, CameraFixed, MapOn, RunInfo, WalkInfo, CameraHandInfo, MapOnInfo, MapOffInfo,
        SystemMenu, SystemSettings, SystemHelp, SystemExit, RestartMain, RestartField, RestartTown, RestartExit, SelectJobGray, SelectJob,
        .. MenuButtons, .. MenuButtonsDown, .. MenuInfo,
        SkillNormal, SkillWindow, SkillWindowSecond, SkillPageForward, SkillPageBack, SkillSelect, SkillGauge, SkillGaugeBack, SkillGaugePassive, .. ShortKeys,
        Inventory, PuzzleBoard, CoverLarge, CoverMiddle, CoverSmall, PotionBack, SideLeft, SideRight, .. PageHints, .. PairHints, CloseHint,
        QuestBoxButton, QuestBoxButtonOpen, QuestBoxTile, QuestBoxHint, QuestBoxCloseHint, QuestBoxGlow,
        Status, StatusPoint, StatusArrow, StatusArrowDown,
        .. InfoBox,
        HelpTopLeft, HelpTop, HelpTopRight, HelpLeft, HelpCenter, HelpRight, HelpBottomLeft, HelpBottom, HelpBottomRight, HelpLine,
        HelpTitle, HelpButton, ScrollBar, ScrollButton, .. GuideBox,
        MessageBox, NoticeLeft, NoticeCenter, NoticeRight, OkGray, OkYellow, YesGray, YesYellow, NoGray, NoYellow,
        CancelGray, CancelYellow, CountField, CountUp, CountDown,
        MapBox, MapArrow, MapNpc, TargetBox, EnergyBack, EnergyFill,
        BroodNormal, BroodMutant, BroodUndead, BroodDemon, BroodMechanic
    ];

    public static string ClassDirectory(PlayerClass characterClass) => characterClass switch
    {
        PlayerClass.Mechanician => "Mecha",
        PlayerClass.Fighter => "Fighter",
        PlayerClass.Pikeman => "Pikeman",
        PlayerClass.Archer => "Archer",
        PlayerClass.Knight => "Knight",
        PlayerClass.Atalanta => "Atalanta",
        PlayerClass.Priestess => "Priestess",
        PlayerClass.Magician => "Magician",
        PlayerClass.Assassin => "Assassin",
        PlayerClass.Shaman => "Shaman",
        _ => "Mecha"
    };

    public static string JobTitle(PlayerClass characterClass, int tier) => $"{Sin}skill/{ClassDirectory(characterClass)}/JobTitle/{tier + 1}.bmp";

    // Skill buttons: Button\<FileName>.bmp, and <FileName>_.bmp for the gray (unusable) state.
    public static string SkillButton(PlayerClass characterClass, string iconKey, bool gray) =>
        $"{Sin}skill/{ClassDirectory(characterClass)}/Button/{iconKey}{(gray ? "_" : "")}.bmp";

    // Field art: field\map\<name>.tga and field\title\<name>t.tga.
    public static string FieldMap(string fieldId) => $"Field/map/{fieldId}.tga";
    public static string FieldTitle(string fieldId) => $"Field/title/{fieldId}t.tga";

    private static string[] Names(string a, string b, string c, string d, string e, string f, string suffix) =>
        [Inter + a + suffix, Inter + b + suffix, Inter + c + suffix, Inter + d + suffix, Inter + e + suffix, Inter + f + suffix];
}
