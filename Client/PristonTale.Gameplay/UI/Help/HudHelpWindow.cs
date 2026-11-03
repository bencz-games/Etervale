using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Help;

public enum HudHelpPage
{
    Help,
    GuideList,
    GuidePage
}

// The help window the system menu's Help opens, in the help box at 208,11 (11 by 11 tiles), with three pages. Help shows
// the client's Help.sin (keys, mouse, commands, trade, party, friends); the icons at its top switch to the game guide.
// The guide's list holds the subject of every level up to the character's, the latest first, ten at a time; a subject
// opens its page, the guide's text and picture for the character's faction, and the list label goes back. The guide
// pages carry the level guide's On/Off switch. The wheel and the scroll bar move a long text or list; OK and Escape
// close the window. requestArt asks for a page's picture, which the HUD's sprites load only once asked.
public sealed class HudHelpWindow(HudTextFiles texts, Action<string> requestArt)
{
    public const string HelpFile = "image/Sinimage/Help/Help.sin";
    private const int BoxX = 208;
    private const int BoxY = 11;
    private const int SizeW = 11;
    private const int SizeH = 11;
    private static readonly Rectangle Box = HudPainter.HelpBoxBounds(BoxX, BoxY, SizeW, SizeH);
    // The header: the help and guide icons with their labels, the title plate between them.
    private static readonly Rectangle HelpLink = new(233, 24, 94, 32);
    public static readonly Rectangle GuideLink = new(508, 18, 100, 32);
    public static readonly Rectangle Switch = new(557, 361, 36, 24);
    private static readonly Point HelpIconAt = new(233, 24);
    private static readonly Point GuideIconAt = new(513, 25);
    private static readonly Rectangle Title = HudPainter.HelpBoxTitle(BoxX, BoxY, SizeW);
    private static readonly Color TitleColor = HudTextColors.HelpText;
    private static readonly Color Highlight = HudTextColors.Marked;
    // The footer: OK in its plate, and on the guide pages the list link, the notice label and the On/Off switch.
    private static readonly Point OkPlateAt = new(392, 361);
    public static readonly Rectangle OkButton = HudPainter.HelpBoxOk(BoxX, BoxY, SizeW, SizeH);
    private static readonly Rectangle ListLink = new(234, 359, 85, 32);
    private static readonly Point ListLabelAt = new(255, 359);
    private static readonly Point NoticeLabelAt = new(493, 359);
    // The help text: 18 lines of 15 pixels from 238,77.
    private const int HelpTextX = 238;
    private const int HelpTextY = 77;
    private const int HelpLineStep = 15;
    private const int HelpVisible = 18;
    // The guide's list: ten subjects 24 pixels apart from 263,91, each behind a 24-pixel icon.
    private const int ListX = 263;
    private const int ListY = 91;
    private const int ListStep = 24;
    private const int ListVisible = 10;
    private const int ListIcon = 24;
    // A guide page: 13 lines of 20 pixels from 86, at 224 beside a scroll bar and 244 without; its picture at the bottom
    // right, which the lines beside it wrap around.
    private const int PageTextX = 224;
    private const int PageTextXWithoutBar = 244;
    private const int PageTextY = 86;
    private const int PageLineStep = 20;
    private const int PageVisible = 13;
    private static readonly Rectangle Picture = new(430, 220, 128, 128);
    private const int TextRight = 592;
    private const int ListWidth = TextRight - ListX;
    private const int PictureGap = 8;
    private const int ScrollX = 602;
    private const int ScrollY = 75;
    private const int ScrollLength = 256;
    private const int ScrollWidth = 16;
    private const int WheelLines = 3;
    // The wire text's arrows, for a font without them.
    private static readonly (char Arrow, char Plain)[] Arrows = [('←', '<'), ('→', '>'), ('↓', 'v'), ('↑', '^')];

    private LevelGuideBook _book;
    // The subjects reached, the latest first, for the level and book they were taken for.
    private readonly List<(int Level, string Subject)> _subjects = new();
    private (int Level, LevelGuideBook Book) _subjectsFor;
    private string[] _helpLines;
    private LevelGuidePage _page;
    private string[] _pageLines;
    private int _first;

    public bool IsOpen { get; private set; }

    public HudHelpPage Page { get; private set; }

    // The guide page shown, or the last one.
    public LevelGuidePage GuidePage => _page;

    // The character's level and faction, which choose the guide's subjects and pages.
    public int Level { get; set; }
    public bool Tempskron { get; set; }

    // The guide's data, empty until both its files are read.
    public LevelGuideBook Book
    {
        get
        {
            if (_book is null && texts.Lines(LevelGuideBook.SubjectsFile) is { } subjects && texts.Lines(LevelGuideBook.ContentFile) is { } content)
            {
                _book = LevelGuideBook.Parse(string.Join('\n', subjects), string.Join('\n', content));
            }

            return _book ?? LevelGuideBook.Empty;
        }
    }

    // The texts are asked for on opening, so they are read by the time the page draws.
    public void OpenHelp()
    {
        texts.Lines(HelpFile);
        Show(HudHelpPage.Help);
    }

    public void OpenGuide() => Show(HudHelpPage.GuideList);

    public void OpenPage(LevelGuidePage page)
    {
        if (page.ImagePath is { } image)
        {
            requestArt(image);
        }

        texts.Lines(page.TextPath);
        _page = page;
        _pageLines = null;
        Show(HudHelpPage.GuidePage);
    }

    public void Close() => IsOpen = false;

    public bool Contains(Point pointer) => IsOpen && Box.Contains(pointer);

    public void Scroll(int wheelDelta) => _first = Math.Clamp(_first - Math.Sign(wheelDelta) * WheelLines, 0, Math.Max(0, LineCount - Visible));

    // A click in the window; toggleGuide flips the level guide's switch. False when the window is closed or the click is
    // outside it.
    public bool Click(Point pointer, Action toggleGuide)
    {
        if (!Contains(pointer))
        {
            return false;
        }

        if (OkButton.Contains(pointer))
        {
            Close();
        }
        else if (Page == HudHelpPage.Help && GuideLink.Contains(pointer))
        {
            OpenGuide();
        }
        else if (Page != HudHelpPage.Help && HelpLink.Contains(pointer))
        {
            OpenHelp();
        }
        else if (Page != HudHelpPage.Help && Switch.Contains(pointer))
        {
            toggleGuide();
        }
        else if (Page == HudHelpPage.GuidePage && ListLink.Contains(pointer))
        {
            OpenGuide();
        }
        else if (Page == HudHelpPage.GuideList && SubjectAt(pointer) is { } level && Book.Page(level, Tempskron) is { } page)
        {
            OpenPage(page);
        }
        else if (LineCount > Visible && new Rectangle(ScrollX - ScrollWidth / 2, ScrollY, ScrollWidth, ScrollLength).Contains(pointer))
        {
            _first = HudPainter.ScrollPosition(ScrollY, ScrollLength, pointer.Y, LineCount - Visible);
        }

        return true;
    }

    public void Draw(HudPainter painter, Point pointer, bool levelGuideOn)
    {
        if (!IsOpen)
        {
            return;
        }

        painter.HelpBox(BoxX, BoxY, SizeW, SizeH);
        var help = Page == HudHelpPage.Help;
        painter.Sprite(help || HelpLink.Contains(pointer) ? HudHelpArt.HelpIcon : HudHelpArt.HelpIconGrey, HelpIconAt.X, HelpIconAt.Y);
        painter.Sprite(HudHelpArt.HelpLabel, HelpIconAt.X + 30, HelpIconAt.Y);
        painter.Sprite(!help || GuideLink.Contains(pointer) ? HudHelpArt.GuideIcon : HudHelpArt.GuideIconGrey, GuideIconAt.X, GuideIconAt.Y);
        painter.Sprite(HudHelpArt.GuideLabel, GuideIconAt.X + 35, GuideIconAt.Y);
        painter.Sprite(HudArt.HelpTitle, Title.X, Title.Y);
        painter.TextCentered(help ? "H E L P" : "G  U  I  D  E", Title.Left, Title.Right, Title.Y + 7, TitleColor);
        painter.Sprite(HudArt.HelpButton, OkPlateAt.X, OkPlateAt.Y);
        painter.Sprite(OkButton.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, OkButton.X, OkButton.Y);
        if (!help)
        {
            painter.Sprite(HudHelpArt.NoticeLabel, NoticeLabelAt.X, NoticeLabelAt.Y);
            painter.Sprite(levelGuideOn ? HudHelpArt.SwitchOn : HudHelpArt.SwitchOff, Switch.X, Switch.Y);
        }

        switch (Page)
        {
            case HudHelpPage.Help:
                DrawHelp(painter);
                break;
            case HudHelpPage.GuideList:
                DrawList(painter, pointer);
                break;
            default:
                DrawPage(painter, pointer);
                break;
        }

        DrawScrollBar(painter);
    }

    private int Visible => Page switch
    {
        HudHelpPage.Help => HelpVisible,
        HudHelpPage.GuideList => ListVisible,
        _ => PageVisible
    };

    private int LineCount => Page switch
    {
        HudHelpPage.Help => _helpLines?.Length ?? 0,
        HudHelpPage.GuideList => Subjects().Count,
        _ => _pageLines?.Length ?? 0
    };

    private void Show(HudHelpPage page)
    {
        IsOpen = true;
        Page = page;
        _first = 0;
    }

    // The subjects up to the character's level, the latest first.
    private List<(int Level, string Subject)> Subjects()
    {
        var book = Book;
        if (_subjectsFor != (Level, book))
        {
            _subjectsFor = (Level, book);
            _subjects.Clear();
            foreach (var subject in book.Subjects)
            {
                if (subject.Level <= Level)
                {
                    _subjects.Insert(0, subject);
                }
            }
        }

        return _subjects;
    }

    public static Rectangle ListRow(int row) => new(ListX, ListY + row * ListStep, ListWidth, ListStep);

    private int? SubjectAt(Point pointer)
    {
        var subjects = Subjects();
        for (var row = 0; row < ListVisible && _first + row < subjects.Count; row++)
        {
            if (ListRow(row).Contains(pointer))
            {
                return subjects[_first + row].Level;
            }
        }

        return null;
    }

    private void DrawHelp(HudPainter painter)
    {
        if (_helpLines is null && texts.Lines(HelpFile) is { } lines)
        {
            _helpLines = [.. lines.SelectMany(line => line.Length == 0 ? [""] : HudTextLayout.Wrap(Drawable(painter, line), TextRight - HelpTextX, text => painter.Measure(text)))];
        }

        for (var row = 0; row < HelpVisible && _helpLines is not null && _first + row < _helpLines.Length; row++)
        {
            painter.Text(_helpLines[_first + row], HelpTextX, HelpTextY + row * HelpLineStep, Color.White);
        }
    }

    private void DrawList(HudPainter painter, Point pointer)
    {
        var subjects = Subjects();
        for (var row = 0; row < ListVisible && _first + row < subjects.Count; row++)
        {
            var y = ListY + row * ListStep;
            var hovered = ListRow(row).Contains(pointer);
            painter.Stretch(hovered ? HudHelpArt.GuideIcon : HudHelpArt.GuideIconGrey, new Rectangle(ListX, y, ListIcon, ListIcon));
            painter.Text(painter.Fit(subjects[_first + row].Subject, ListWidth - ListIcon - 6), ListX + ListIcon + 6, y + 5, hovered ? Highlight : Color.White);
        }
    }

    private void DrawPage(HudPainter painter, Point pointer)
    {
        painter.Stretch(ListLink.Contains(pointer) ? HudHelpArt.GuideIcon : HudHelpArt.GuideIconGrey, new Rectangle(ListLink.X, ListLink.Y + 2, ListIcon, ListIcon));
        painter.Sprite(HudHelpArt.ListLabel, ListLabelAt.X, ListLabelAt.Y);
        if (_page is null)
        {
            return;
        }

        if (_page.ImagePath is { } image)
        {
            painter.Stretch(image, Picture);
        }

        // A page that needs the scroll bar starts further left, so it wraps again from there.
        if (_pageLines is null && texts.Lines(_page.TextPath) is { } lines)
        {
            _pageLines = WrapAround(painter, lines, PageTextXWithoutBar, _page.ImagePath is not null);
            if (_pageLines.Length > PageVisible)
            {
                _pageLines = WrapAround(painter, lines, PageTextX, _page.ImagePath is not null);
            }
        }

        var x = _pageLines?.Length > PageVisible ? PageTextX : PageTextXWithoutBar;
        for (var row = 0; row < PageVisible && _pageLines is not null && _first + row < _pageLines.Length; row++)
        {
            painter.Text(_pageLines[_first + row], x, PageTextY + row * PageLineStep, Color.White);
        }
    }

    // A page's lines wrapped to the box, narrower beside the picture: the lines that would reach its top stop short of it.
    private static string[] WrapAround(HudPainter painter, string[] lines, int x, bool picture)
    {
        var wrapped = new List<string>();
        var full = TextRight - x;
        var beside = Picture.Left - PictureGap - x;
        var fullRows = (Picture.Top - PageTextY) / PageLineStep;
        foreach (var line in lines)
        {
            var words = Drawable(painter, line).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = "";
            foreach (var word in words)
            {
                var width = picture && wrapped.Count >= fullRows ? beside : full;
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (current.Length > 0 && painter.Measure(candidate) > width)
                {
                    wrapped.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            wrapped.Add(current);
        }

        return [.. wrapped];
    }

    private void DrawScrollBar(HudPainter painter)
    {
        if (LineCount <= Visible)
        {
            return;
        }

        painter.ScrollBar(ScrollX, ScrollY, ScrollLength, _first, LineCount - Visible);
    }

    private static string Drawable(HudPainter painter, string line)
    {
        foreach (var (arrow, plain) in Arrows)
        {
            if (line.Contains(arrow) && !painter.CanDraw(arrow))
            {
                line = line.Replace(arrow, plain);
            }
        }

        return line;
    }
}
