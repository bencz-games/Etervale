using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// A board of lines on the right under the target frame (the siege's, Bellatra's), each line with an optional right-hand
// value.
public static class HudSideBoard
{
    private const int Width = 200;
    private const int Margin = 8;
    private const int LineHeight = 14;

    // A board's title, the player's own line, the others.
    public static readonly Color Title = new(255, 220, 120);
    public static readonly Color Own = new(255, 200, 40);
    public static readonly Color Line = new(220, 220, 220);

    public static void Draw(HudPainter painter, HudLayout layout, IReadOnlyList<(string Text, string Right, Color Color)> lines)
    {
        var top = HudCombat.TargetFrame(layout).StatsY + LineHeight + Margin;
        var bounds = new Rectangle(layout.Width - Width - Margin, top, Width, 2 * Margin + lines.Count * LineHeight);
        painter.InfoBox(bounds);
        for (var index = 0; index < lines.Count; index++)
        {
            var (text, right, color) = lines[index];
            var y = bounds.Y + Margin + index * LineHeight;
            painter.Text(text, bounds.X + Margin + 4, y, color);
            if (right.Length > 0)
            {
                painter.Text(right, bounds.Right - Margin - 4 - painter.Measure(right), y, color);
            }
        }
    }
}
