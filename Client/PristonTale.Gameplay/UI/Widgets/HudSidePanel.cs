using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The place the party window and its quest page share: 256x256 at (539, 155) of the 800x600 frame (right and bottom
// anchored), sliding in from the right edge, with a row of 20 px buttons at the bottom.
public static class HudSidePanel
{
    public const int Width = 256;
    public const int Height = 256;
    private const int DesignX = 539;
    private const int DesignY = 155;

    public static Point Origin(HudLayout layout, int slideMove = HudSlide.Full) =>
        new(layout.Width - (HudLayout.DesignWidth - DesignX) * Math.Clamp(slideMove, 0, HudSlide.Full) / HudSlide.Full, DesignY + layout.BottomOffset);

    // The panel with its title plate above it.
    public static Rectangle Bounds(Point origin) => new(origin.X, origin.Y - 8, Width, Height + 8);

    public static Rectangle Button(Point origin, int index) => new(origin.X + 100 + index * 23, origin.Y + 220, 20, 20);
}
