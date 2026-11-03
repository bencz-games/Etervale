using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Rendering;

// The interface text colours several windows share: a window's or an item's title, and the pale yellow of prices and
// the points left.
public static class HudTextColors
{
    public static readonly Color Title = new(222, 231, 255);
    public static readonly Color Highlight = new(247, 243, 193);
}
