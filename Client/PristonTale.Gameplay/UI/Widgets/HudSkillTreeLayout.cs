using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Widgets;

// Original skill boxes and titles in design coordinates of the fully open Skil24.bmp window.
public static class HudSkillTreeLayout
{
    private static readonly Point[] Nodes =
    [
        new(27 - 10, 502), new(67 - 10, 546), new(77 - 10, 466), new(137 - 10, 476),
        new(212 - 15, 466), new(232 - 15, 516), new(272 - 15, 466), new(332, 466),
        new(402, 476), new(472, 456), new(512, 506), new(532, 456),
        new(602, 481), new(662, 511), new(681, 461), new(722, 511)
    ];

    private static readonly Point[] Titles = [new(85, 438 - 4), new(280, 438 - 4), new(470, 438 - 9), new(675, 438 - 4)];

    public const int NodeCount = 16;

    public static Rectangle Node(Point origin, int index) => new(origin.X + Nodes[index].X, origin.Y + Nodes[index].Y, 49, 46);
    public static Rectangle BasicAttack(Point origin) => new(origin.X + 142, origin.Y + 537, 49, 46);

    // Gauges: the training gauge sits at +47, the ready gauge (the cooldown charging) one pixel right of its 5-pixel width.
    public static Rectangle MasteryGauge(Point origin, int index) => new(origin.X + Nodes[index].X + 47, origin.Y + Nodes[index].Y + 5, 5, 35);
    public static Rectangle ReadyGauge(Point origin, int index) => new(origin.X + Nodes[index].X + 53, origin.Y + Nodes[index].Y + 5, 5, 35);

    public static Point Title(Point origin, int tier) => new(origin.X + Titles[tier].X, origin.Y + Titles[tier].Y);
    public static Rectangle Close(Point origin) => new(origin.X + 269, origin.Y + 571, 20, 20);

    // A skill master's training box (13 by 21 tiles of 16 pixels) at the screen's top left, and its class change button
    // (44x44): screen design coordinates, not the window's.
    public static Rectangle MasterBox => new(90, 100, 13 * 16, 21 * 16);
    public static Rectangle ClassChange => new(240, 380, 44, 44);

    // The arrows of the window's two pages: forward on the first page's right edge, back on the second page's left.
    public static Rectangle PageForward(Point origin) => new(origin.X + 765, origin.Y + 482, 20, 20);
    public static Rectangle PageBack(Point origin) => new(origin.X + 12, origin.Y + 482, 20, 20);
    public static Rectangle Window(Point origin) => new(origin.X, origin.Y + 400, HudLayout.DesignWidth, 200);
}
