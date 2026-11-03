using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI.Quests;

// Asset-relative paths of the quest window (the party window's quest page) and the quest timer icon. A button image
// ending in '_' is its idle look; the plain image is drawn under the pointer and for the selected page.
public static class HudQuestArt
{
    private const string Party = "image/party/";

    public const string Window = Party + "party_win.tga";
    public const string Title = Party + "title-quest.tga";
    public const string Description = Party + "quest_win.tga";
    public const string Running = Party + "quest_icon-ing.bmp";
    public const string Hovered = Party + "quest_icon-end.bmp";
    public const string Timer = KeepIcons.Folder + "QuestTimer.tga";

    // The list page's buttons in order: previous page, next page, running quests, finished quests, close.
    public static readonly string[] ListButtons =
    [
        Party + "icon-q1.bmp", Party + "icon-q2.bmp", Party + "icon-q3.bmp", Party + "icon-q4.bmp", Party + "ButtonExit.bmp"
    ];

    public static readonly string[] ListButtonsIdle =
    [
        Party + "icon-q1_.bmp", Party + "icon-q2_.bmp", Party + "icon-q3_.bmp", Party + "icon-q4_.bmp", Party + "close_.bmp"
    ];

    public static readonly string[] ListTips =
    [
        Party + "ButtonInfo-qc-pre.bmp", Party + "ButtonInfo-qc-nxt.bmp", Party + "ButtonInfo-q3.bmp", Party + "ButtonInfo-q4.bmp",
        Party + "ButtonInfo-5.bmp"
    ];

    // The description page's buttons: previous lines, next lines, close.
    public static readonly string[] PageButtons = [Party + "LeftArrow.bmp", Party + "RightArrow.bmp", Party + "ButtonExit.bmp"];
    public static readonly string[] PageButtonsIdle = [Party + "LeftArrow_.bmp", Party + "RightArrow_.bmp", Party + "close_.bmp"];
    public static readonly string[] PageTips = [Party + "ButtonInfo-qc-pre.bmp", Party + "ButtonInfo-qc-nxt.bmp", Party + "ButtonInfo-5.bmp"];

    public static readonly string[] All =
    [
        Window, Title, Description, Running, Hovered, Timer, .. ListButtons, .. ListButtonsIdle, .. ListTips, .. PageButtons, .. PageButtonsIdle
    ];
}
