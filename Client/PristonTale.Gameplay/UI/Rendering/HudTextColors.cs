using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Rendering;

// The interface text colours several windows share: a window's or an item's title, the pale yellow of prices and the
// points left, a box's or tooltip's name line, the gold of a marked word, the near-white of help text and the grey of
// hints and requirements.
public static class HudTextColors
{
    public static readonly Color Title = new(222, 231, 255);
    public static readonly Color Highlight = new(247, 243, 193);
    public static readonly Color BoxTitle = new(255, 255, 180);
    public static readonly Color Marked = new(255, 220, 90);
    public static readonly Color HelpText = new(255, 255, 240);
    public static readonly Color Hint = new(200, 200, 200);
}
