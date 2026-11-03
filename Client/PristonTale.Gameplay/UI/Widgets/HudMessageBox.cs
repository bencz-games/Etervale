using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Original two-button message box: BoxMain.tga with two 32x16 buttons and two text lines. The original pins it at
// (300,200) of its 800x600 screen; here it is centred on the HUD's screen (Place, every frame), at (300,200) until the
// first placement.
public static class HudMessageBox
{
    public const int VisibleWidth = 168;
    public const int Height = 124;

    private static Point _origin = new(300, 200);

    public static Rectangle Bounds => new(_origin.X, _origin.Y, VisibleWidth, Height);
    public static Rectangle Accept => new(_origin.X + 40, _origin.Y + 95, 32, 16);
    public static Rectangle Reject => new(_origin.X + 94, _origin.Y + 95, 32, 16);

    public static void Place(HudLayout layout) => _origin = new Point((layout.Width - VisibleWidth) / 2, (layout.Height - Height) / 2);

    // A question's third line sits under the second.
    public static void Draw(HudPainter painter, string first, string second, Point pointer, string third = null)
    {
        var bounds = Bounds;
        painter.Sprite(HudArt.MessageBox, bounds.X, bounds.Y);
        painter.Sprite(Accept.Contains(pointer) ? HudArt.YesYellow : HudArt.YesGray, Accept.X, Accept.Y);
        painter.Sprite(Reject.Contains(pointer) ? HudArt.NoYellow : HudArt.NoGray, Reject.X, Reject.Y);
        painter.TextCentered(painter.Fit(first, VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 20, HudTextColors.BoxTitle);
        painter.TextCentered(painter.Fit(second, VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 43);
        if (third is not null)
        {
            painter.TextCentered(third, bounds.Left, bounds.Right, bounds.Y + 60);
        }
    }
}
