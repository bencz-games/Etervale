using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Npc;

// The original item box above the pointer: the name in the item colour and up to two lines below it.
public static class HudItemTooltip
{
    private static readonly Color NameColor = HudTextColors.BoxTitle;
    private static readonly Color LineColor = HudTextColors.Hint;

    // Drawn every frame an item is hovered, so it allocates nothing: the empty lines are skipped, not filtered out.
    public static void Draw(HudPainter painter, HudLayout layout, Point pointer, string name, string first, string second = "")
    {
        var count = (string.IsNullOrEmpty(first) ? 0 : 1) + (string.IsNullOrEmpty(second) ? 0 : 1);
        var textWidth = Math.Max(painter.Measure(name), Math.Max(painter.Measure(first), painter.Measure(second)));
        var width = Math.Min(layout.Width - 8, textWidth + 24);
        var bounds = painter.TooltipBox(layout, pointer, width, 31 + count * 17);
        painter.TextCentered(painter.Fit(name, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + HudPainter.TooltipNameY, NameColor);
        var row = 0;
        foreach (var line in (ReadOnlySpan<string>)[first, second])
        {
            if (!string.IsNullOrEmpty(line))
            {
                painter.TextCentered(painter.Fit(line, bounds.Width - 16), bounds.Left, bounds.Right, bounds.Y + HudPainter.TooltipLinesY + 17 * row++, LineColor);
            }
        }
    }
}
