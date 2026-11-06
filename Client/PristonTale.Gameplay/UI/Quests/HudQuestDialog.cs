using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Quests;

// The quest dialog in the help box the NPC dialog uses (at 50,45, 9x10 tiles): the quest title, the dialog document and
// the OK button. A reward list shows one button per reward; a reward view has arrows that browse the rewards. The yes/no
// box that may follow is the original two-button message box with the confirmation lines.
public sealed class HudQuestDialog
{
    private const int TextWidth = HudNpcPanel.SizeW * 32;
    private const int TextTop = HudNpcPanel.BoxY + 66;
    private const int LineStep = HudPainter.LineHeight;
    private static readonly Color TitleColor = HudTextColors.BoxTitle;
    private static readonly Color Highlight = HudTextColors.Marked;
    private int _firstLine;
    private string _document = "";
    private int _lineCount;

    public static Rectangle Box => HudNpcPanel.Box;
    public static Rectangle OkButton => HudNpcPanel.OkButton;
    public static Rectangle PreviousArrow => new(OkButton.X - 44, OkButton.Y - 2, 20, 20);
    public static Rectangle NextArrow => new(OkButton.Right + 24, OkButton.Y - 2, 20, 20);

    public static Rectangle ChoiceButton(int index, int count) => HudNpcPanel.ServiceButton(index, count);

    public void Scroll(int wheelDelta)
    {
        _firstLine = Math.Clamp(_firstLine - Math.Sign(wheelDelta) * 3, 0, Math.Max(0, _lineCount - 1));
    }

    public static int ChoiceAt(QuestPrompt dialog, Point pointer)
    {
        if (dialog is not { Followup: QuestFollowup.ChooseFromList })
        {
            return -1;
        }

        for (var index = 0; index < dialog.Choices.Count; index++)
        {
            if (ChoiceButton(index, dialog.Choices.Count).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }

    private static int LineCapacity(QuestPrompt dialog)
    {
        var top = dialog.Followup == QuestFollowup.ChooseFromList && dialog.Choices.Count > 0
            ? ChoiceButton(0, dialog.Choices.Count).Y
            : OkButton.Y;
        return Math.Clamp((top - 8 - TextTop) / LineStep, 1, HudNpcPanel.VisibleLines);
    }

    public void Draw(HudPainter painter, SpriteFont font, HudQuestController quests, HudQuestDocuments documents, Point pointer)
    {
        if (quests.Dialog is not { } dialog)
        {
            return;
        }

        var document = quests.DialogDocument;
        if (document != _document)
        {
            _document = document;
            _firstLine = 0;
        }

        painter.HelpBox(HudNpcPanel.BoxX, HudNpcPanel.BoxY, HudNpcPanel.SizeW, HudNpcPanel.SizeH);
        // A title wider than the plate widens it around its centre.
        var title = HudNpcPanel.Title;
        var heading = ClientText.Format(dialog.Title);
        var titleWidth = Math.Min(HudNpcPanel.SizeW * 32, Math.Max(title.Width, painter.Measure(heading) + 40));
        title = new Rectangle(title.Center.X - titleWidth / 2, title.Y, titleWidth, title.Height);
        HudNpcPanel.DrawPlate(painter, title, Color.White);
        painter.TextCentered(painter.Fit(heading, title.Width - 20), title.Left, title.Right, title.Y + 7, TitleColor);
        var lines = documents.Wrapped(HudQuestDocuments.DialogPath(document), font, TextWidth, ClientText.Format(dialog.Subject)) ?? [];
        _lineCount = lines.Length;
        var capacity = LineCapacity(dialog);
        _firstLine = Math.Clamp(_firstLine, 0, Math.Max(0, lines.Length - capacity));
        for (var line = 0; line < capacity && _firstLine + line < lines.Length; line++)
        {
            painter.Text(lines[_firstLine + line], HudNpcPanel.BoxX + 30, TextTop + LineStep * line);
        }

        if (lines.Length > capacity)
        {
            var barX = HudNpcPanel.BoxX + (HudNpcPanel.SizeW - 1) * 32 + 80 - 6;
            var height = capacity * LineStep + 16;
            painter.ScrollBar(barX, HudNpcPanel.BoxY + 53, height, _firstLine, lines.Length - capacity);
        }

        if (dialog.Followup == QuestFollowup.ChooseFromList)
        {
            for (var index = 0; index < dialog.Choices.Count; index++)
            {
                var bounds = ChoiceButton(index, dialog.Choices.Count);
                painter.Sprite(HudArt.HelpTitle, bounds.X, bounds.Y);
                var color = bounds.Contains(pointer) && !quests.Pending ? Highlight : Color.White;
                painter.TextCentered(painter.Fit(dialog.Choices[index], bounds.Width - 10), bounds.Left, bounds.Right, bounds.Y + 7, color);
            }
        }

        if (dialog.Followup == QuestFollowup.ConfirmChoice && dialog.Choices.Count > 1)
        {
            var previous = PreviousArrow;
            var next = NextArrow;
            painter.Sprite(previous.Contains(pointer) ? HudQuestArt.PageButtons[0] : HudQuestArt.PageButtonsIdle[0], previous.X, previous.Y);
            painter.Sprite(next.Contains(pointer) ? HudQuestArt.PageButtons[1] : HudQuestArt.PageButtonsIdle[1], next.X, next.Y);
        }

        var ok = OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) && !quests.Confirming ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    // The yes/no box after a dialog: the detail (a job, an item, a destination) in yellow over the two question lines.
    public static void DrawConfirm(HudPainter painter, HudQuestController quests, Point pointer)
    {
        if (!quests.Confirming || quests.Dialog is not { } dialog)
        {
            return;
        }

        DrawBox(painter, quests.ConfirmDetail, ClientText.Format(dialog.ConfirmLine1), ClientText.Format(dialog.ConfirmLine2), pointer);
    }

    // The two question lines of the classic texts are one sentence; it wraps to the box under the yellow detail line.
    // The last wrapped confirmation, kept while the box shows the same lines (the HUD draws on one thread).
    private static (string First, string Second, int Width) _boxText;
    private static IReadOnlyList<string> _boxLines = [];

    public static void DrawBox(HudPainter painter, string detail, string first, string second, Point pointer)
    {
        HudMessageBox.Draw(painter, "", "", pointer);
        var bounds = HudMessageBox.Bounds;
        var width = bounds.Width - 16;
        var y = bounds.Y + 14;
        if (!string.IsNullOrEmpty(detail))
        {
            painter.TextCentered(painter.Fit(detail, width), bounds.Left, bounds.Right, y, TitleColor);
            y += HudPainter.LineHeight + 2;
        }

        if (_boxText != (first, second, width))
        {
            _boxText = (first, second, width);
            _boxLines = HudTextLayout.Wrap($"{first} {second}".Trim(), width, candidate => painter.Measure(candidate));
        }

        var lines = _boxLines;
        var room = (HudMessageBox.Accept.Y - 6 - y) / HudPainter.LineHeight;
        for (var line = 0; line < lines.Count && line < room; line++)
        {
            painter.TextCentered(painter.Fit(lines[line], width), bounds.Left, bounds.Right, y + line * HudPainter.LineHeight,
                string.IsNullOrEmpty(detail) && line == 0 ? TitleColor : Color.White);
        }
    }

}
