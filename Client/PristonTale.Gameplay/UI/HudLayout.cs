using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

// The original HUD is authored for 800x600 and offset by (window width - 800, window height - 600):
// bottom-anchored elements move down by the extra height and right-anchored elements move right by the extra width.
public readonly record struct HudLayout(int Scale, int Width, int Height, int ViewportWidth, int ViewportHeight)
{
    public const int DesignWidth = 800;
    public const int DesignHeight = 600;

    public static HudLayout Of(Microsoft.Xna.Framework.Graphics.Viewport viewport) => Create(viewport.Width, viewport.Height);

    public static HudLayout Create(int viewportWidth, int viewportHeight)
    {
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        var scale = Math.Max(1, Math.Min(viewportWidth / DesignWidth, viewportHeight / DesignHeight));
        return new HudLayout(scale, Math.Max(1, viewportWidth / scale), Math.Max(1, viewportHeight / scale), viewportWidth, viewportHeight);
    }

    public int BottomOffset => Height - DesignHeight;
    public int RightOffset => Width - DesignWidth;

    public Rectangle Bottom(Rectangle design) => new(design.X, design.Y + BottomOffset, design.Width, design.Height);
    public Rectangle BottomRight(Rectangle design) => new(design.X + RightOffset, design.Y + BottomOffset, design.Width, design.Height);
    public Point Bottom(int x, int y) => new(x, y + BottomOffset);
    public Point BottomRight(int x, int y) => new(x + RightOffset, y + BottomOffset);

    public Point ToLogical(Point pointer)
    {
        var scale = Math.Max(1, Scale);
        return new Point(FloorDivide(pointer.X, scale), FloorDivide(pointer.Y, scale));
    }

    public Rectangle PlaceOverlay(Point preferred, int width, int height)
    {
        var viewportWidth = Math.Max(1, Width);
        var viewportHeight = Math.Max(1, Height);
        var margin = Math.Min(4, (Math.Min(viewportWidth, viewportHeight) - 1) / 2);
        width = Math.Clamp(width, 1, Math.Max(1, viewportWidth - margin * 2));
        height = Math.Clamp(height, 1, Math.Max(1, viewportHeight - margin * 2));
        var x = Math.Clamp(preferred.X, margin, Math.Max(margin, viewportWidth - width - margin));
        var y = Math.Clamp(preferred.Y, margin, Math.Max(margin, viewportHeight - height - margin));
        return new Rectangle(x, y, width, height);
    }

    // value / divisor rounded down, for negative values too.
    internal static int FloorDivide(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
