using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Parties;

// Asset-relative paths of the party window's party page (its frame is the quest page's), the request box, the minimap
// mark and the buttons under another player's portrait. A button image ending in '_' is its idle look; the plain one is
// drawn under the pointer.
public static class HudPartyArt
{
    private const string Party = "image/party/";
    private const string Effect = "Effect/Party/";

    public const string Window = HudQuestArt.Window;
    public const string Title = Party + "title-NoClan_Party.tga";

    // In PartyCommand order, then close.
    public static readonly string[] Buttons = [Party + "icon-1.bmp", Party + "icon-2.bmp", Party + "icon-3.bmp", Party + "icon-4.bmp", Party + "ButtonExit.bmp"];
    public static readonly string[] ButtonsIdle = [Party + "icon-1_.bmp", Party + "icon-2_.bmp", Party + "icon-3_.bmp", Party + "icon-4_.bmp", Party + "close_.bmp"];
    public static readonly string[] Tips =
    [
        Party + "ButtonInfo-1.bmp", Party + "ButtonInfo-2.bmp", Party + "ButtonInfo-3.bmp", Party + "ButtonInfo-4.bmp", Party + "ButtonInfo-5.bmp"
    ];

    public const string LifeRed = Effect + "LifeRed.bmp";
    public const string LifeBlue = Effect + "LifeBlue.bmp";

    // A member's mark: idle, under the pointer or picked, and the leader's.
    public const string Member = Effect + "Man1.bmp";
    public const string MemberHovered = Effect + "Man2.bmp";
    public const string Leader = Effect + "Man3.bmp";

    // The request box: who asks at (36, 16), the request line at (28, 38), OK at (140, 11), cancel at (144, 37).
    public static readonly HudRequestBoxArt Request = new(Effect + "MessageBackGround.tga", Effect + "Ok1.bmp", Effect + "Ok2.bmp", new Rectangle(140, 11, 23, 23),
        Effect + "Cancel1.bmp", Effect + "Cancel2.bmp", new Rectangle(144, 37, 15, 15), new Point(36, 16), new Point(28, 38), BlinkOver: true);

    public const string MapMark = "image/party.tga";

    // The trade, party and friend buttons under another player's portrait.
    public const string PlayerMenu = "image/cw-1.tga";
    public static readonly string[] PlayerButtons = [Party + "icon-c5.bmp", Party + "icon-c1.bmp", Party + "icon-c2.bmp", Party + "icon_clan.bmp"];
    public static readonly string[] PlayerButtonsIdle = [Party + "icon-c5_.bmp", Party + "icon-c1_.bmp", Party + "icon-c2_.bmp", Party + "icon_clan_.bmp"];
    public static readonly string[] PlayerTips =
        [Party + "ButtonInfo-c5.bmp", Party + "ButtonInfo-c1.bmp", Party + "ButtonInfo-c0.bmp", Party + "ButtonInfo-c10.bmp"];

    public static readonly string[] All =
    [
        Window, Title, .. Buttons, .. ButtonsIdle, .. Tips, LifeRed, LifeBlue, Member, MemberHovered, Leader, .. Request.All, MapMark, PlayerMenu,
        .. PlayerButtons, .. PlayerButtonsIdle, .. PlayerTips
    ];
}
