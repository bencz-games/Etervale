using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// A window drawn in the NPC panel's box (the castle's butler, Bellatra's secretary): a title, lines of text from the
// panel's text area down to its buttons, the buttons and OK.
public static class HudPanelPage
{
    public const int LineStep = HudPainter.LineHeight;
    public static readonly Color Muted = new(170, 170, 170);

    // A chosen or leading line.
    public static readonly Color Chosen = new(255, 200, 40);

    public static void Frame(HudPainter painter, string title)
    {
        painter.HelpBox(HudNpcPanel.BoxX, HudNpcPanel.BoxY, HudNpcPanel.SizeW, HudNpcPanel.SizeH);
        var plate = HudNpcPanel.Title;
        HudNpcPanel.DrawPlate(painter, plate, Color.White);
        painter.TextCentered(title, plate.Left, plate.Right, plate.Y + 7, HudTextColors.BoxTitle);
    }

    public static void Buttons(HudPainter painter, IReadOnlyList<string> fitted, Point pointer, bool pending)
    {
        for (var index = 0; index < fitted.Count; index++)
        {
            var bounds = HudNpcPanel.ServiceButton(index, fitted.Count);
            painter.Sprite(HudArt.HelpTitle, bounds.X, bounds.Y);
            var color = pending ? Muted : bounds.Contains(pointer) ? HudTextColors.Marked : Color.White;
            painter.TextCentered(fitted[index], bounds.Left, bounds.Right, bounds.Y + 7, color);
        }

        var ok = HudNpcPanel.OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    // The button under the pointer (-1 for none).
    public static int ButtonAt(Point pointer, int buttons)
    {
        for (var index = 0; index < buttons; index++)
        {
            if (HudNpcPanel.ServiceButton(index, buttons).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }

    // How many lines fit above the buttons (or OK without any).
    public static int Capacity(int buttons)
    {
        var top = buttons > 0 ? HudNpcPanel.ServiceButton(0, buttons).Y : HudNpcPanel.OkButton.Y;
        return Math.Clamp((top - 8 - HudNpcPanel.TextTop) / LineStep, 1, HudNpcPanel.VisibleLines);
    }

    // The line under the pointer (-1 for none).
    public static int LineAt(Point pointer, int rows, int buttons)
    {
        if (pointer.X < HudNpcPanel.TextLeft || pointer.X > HudNpcPanel.TextLeft + HudNpcPanel.TextWidth || pointer.Y < HudNpcPanel.TextTop)
        {
            return -1;
        }

        var line = (pointer.Y - HudNpcPanel.TextTop) / LineStep;
        return line < Capacity(buttons) && line < rows ? line : -1;
    }
}
