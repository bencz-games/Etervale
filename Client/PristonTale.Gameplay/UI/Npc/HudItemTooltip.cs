using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Npc;

// The original item box above the pointer: the name in the item colour and up to two lines below it.
public static class HudItemTooltip
{
    private static readonly Color NameColor = new(255, 255, 180);
    private static readonly Color LineColor = new(200, 200, 200);

    // Drawn every frame an item is hovered, so it allocates nothing: the empty lines are skipped, not filtered out.
    public static void Draw(HudPainter painter, HudLayout layout, Point pointer, string name, string first, string second = "")
    {
        var count = (string.IsNullOrEmpty(first) ? 0 : 1) + (string.IsNullOrEmpty(second) ? 0 : 1);
        var textWidth = Math.Max(painter.Measure(name), Math.Max(painter.Measure(first), painter.Measure(second)));
        var width = Math.Min(layout.Width - 8, textWidth + 24);
        var size = HudPainter.InfoBoxBounds(0, 0, width, 31 + count * 17);
        var bounds = layout.PlaceOverlay(new Point(pointer.X - size.Width / 2, pointer.Y - size.Height - 8), size.Width, size.Height);
        bounds = HudPainter.InfoBoxBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        painter.Flush();
        painter.InfoBox(bounds);
        painter.TextCentered(painter.Fit(name, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + 9, NameColor);
        var row = 0;
        foreach (var line in (ReadOnlySpan<string>)[first, second])
        {
            if (!string.IsNullOrEmpty(line))
            {
                painter.TextCentered(painter.Fit(line, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + 26 + 17 * row++, LineColor);
            }
        }
    }
}
