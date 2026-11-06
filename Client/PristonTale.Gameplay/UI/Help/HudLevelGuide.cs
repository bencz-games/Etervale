using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Help;

// The level guide's notice. Reaching a level the guide has a page for (or entering as a fresh character) shows a picture
// blinking every 0.7 s at the top left; a click on it opens its summary, a guide box with the first six lines of the page
// beside the page's picture, Close, and ">>more..." when the page is longer, which opens the page in the help window.
// A new character's first weapon and armour open their summaries at once. The notice waits while the helper pet's box
// holds the same corner, and a later offer replaces an earlier one.
public sealed class HudLevelGuide(HudTextFiles texts, Action<string> requestArt)
{
    public const double BlinkMs = 700;
    public const int SummaryLines = 6;
    public static string More => ClientText.Get("Guide.More");
    private static readonly Rectangle Icon = new(9, 12, 64, 64);
    private static readonly Point LabelAt = new(49, 19);
    private static readonly Rectangle IconArea = new(9, 12, 104, 64);
    // The summary's guide box at 5,5, 28 columns wide and as many rows as its text and footer need; its picture beside
    // the text, Close and >>more... under it.
    private const int BoxX = 5;
    private const int BoxY = 5;
    private const int Columns = 28;
    private const int MinRows = 4;
    private static readonly Rectangle BoxIcon = new(23, 23, 64, 64);
    private const int TextX = 75;
    private const int TextY = 33;
    private const int LineStep = HudPainter.LineHeight;
    private const int PictureSize = 64;
    private const int FooterGap = 4;
    private const int PictureGap = 8;
    private const int MoreWidth = 80;
    private static readonly Point CloseSize = new(32, 16);
    private static readonly Color MoreColor = HudTextColors.Marked;

    private LevelGuidePage _page;
    private bool _summary;
    private string[] _lines;
    private string _language = ClientText.Language;

    public bool Showing => _page is not null;

    // Whether the level the character is at offers its page: a level above the last seen (seen 0 before the first owner
    // state), or level 1 without experience when first seen (a fresh character entering).
    public static bool Reached(int seenLevel, int level, ulong experience) => seenLevel == 0 ? Fresh(level, experience) : level > seenLevel;

    // A character that has not fought yet: level 1 without experience.
    public static bool Fresh(int level, ulong experience) => level == 1 && experience == 0;

    public bool Summary => _summary;

    // A page to offer: as the blinking notice, or as its summary at once.
    public void Offer(LevelGuidePage page, bool atOnce)
    {
        if (page.ImagePath is { } image)
        {
            requestArt(image);
        }

        texts.Lines(page.TextPath);
        _page = page;
        _summary = atOnce;
        _lines = null;
    }

    public void Dismiss() => _page = null;

    public bool Captures(Point pointer, bool blocked) => Showing && !blocked && (_summary ? SummaryBounds.Contains(pointer) : IconArea.Contains(pointer));

    // A click on the notice: true when it took the click; more is the page the summary's ">>more..." asks for.
    public bool Click(Point pointer, bool blocked, out LevelGuidePage more)
    {
        more = null;
        if (!Captures(pointer, blocked))
        {
            return false;
        }

        if (!_summary)
        {
            _summary = true;
        }
        else if (CloseButton.Contains(pointer))
        {
            Dismiss();
        }
        else if (HasMore && MoreLink.Contains(pointer))
        {
            more = _page;
            Dismiss();
        }

        return true;
    }

    public void Draw(HudPainter painter, Point pointer, double nowMs, bool blocked)
    {
        if (!Showing || blocked)
        {
            return;
        }

        if (!_summary)
        {
            painter.Sprite(HudHelpArt.Notice[(int)(nowMs / BlinkMs) % 2], Icon.X, Icon.Y);
            painter.Sprite(HudHelpArt.GuideLabel, LabelAt.X, LabelAt.Y);
            return;
        }

        if (_language != ClientText.Language)
        {
            _language = ClientText.Language;
            _lines = null;
        }

        _lines ??= Wrap(painter);
        painter.GuideBox(BoxX, BoxY, Columns, Rows);
        painter.Sprite(HudHelpArt.Notice[0], BoxIcon.X, BoxIcon.Y);
        if (_page.ImagePath is { } image)
        {
            painter.Stretch(image, Picture);
        }

        painter.Flush();
        for (var index = 0; index < ShownLines; index++)
        {
            painter.Text(_lines[index], TextX, TextY + index * LineStep, Color.White);
        }

        var close = CloseButton;
        painter.Sprite(close.Contains(pointer) ? HudHelpArt.CloseLit : HudHelpArt.Close, close.X, close.Y);
        if (HasMore)
        {
            painter.Text(More, MoreLink.X, MoreLink.Y, MoreLink.Contains(pointer) ? MoreColor : Color.White);
        }
    }

    private bool HasMore => _lines?.Length > SummaryLines;

    private int ShownLines => Math.Min(SummaryLines, _lines?.Length ?? 0);

    private int FooterY => Math.Max(TextY + ShownLines * LineStep, BoxIcon.Bottom) + FooterGap;

    // Enough rows for the bottom edge to start under the footer.
    private int Rows => Math.Max(MinRows,
        (int)Math.Ceiling((FooterY + CloseSize.Y + FooterGap - HudPainter.GuideBoxBottom(BoxY, 0)) / (double)HudPainter.GuideRowStep));

    private int Right => HudPainter.GuideBoxRight(BoxX, Columns);

    private Rectangle SummaryBounds => new(BoxX, BoxY, Right + HudPainter.GuideCorner - BoxX, HudPainter.GuideBoxBottom(BoxY, Rows) + HudPainter.GuideCorner - BoxY);

    private Rectangle CloseButton => new(Right - CloseSize.X, FooterY, CloseSize.X, CloseSize.Y);

    private Rectangle MoreLink => new(TextX, FooterY, MoreWidth, LineStep);

    // Above the footer, but under the top edge for a short page.
    private Rectangle Picture => new(Right - PictureSize + HudPainter.GuideTile,
        Math.Max(BoxY + HudPainter.GuideCorner, FooterY - PictureSize - PictureGap / 2), PictureSize, PictureSize);

    private string[] Wrap(HudPainter painter) => texts.Lines(_page.TextPath) is { } lines
        ? [.. lines.SelectMany(line => HudTextLayout.Wrap(line, Picture.Left - PictureGap - TextX, text => painter.Measure(text)))]
        : null;
}
