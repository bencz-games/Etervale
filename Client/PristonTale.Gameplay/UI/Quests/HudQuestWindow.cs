using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;

public enum HudQuestPage
{
    Running,
    Finished
}

// The quest page of the original party window: 256x256 at (539, 155) of the 800x600 frame (right and bottom anchored),
// sliding in from the right edge. Five quest titles per page at 30 px steps with the running-quest mark; below them the
// page arrows, the running/finished tabs and close. A clicked title opens its description on the panel to the left
// (eight lines of 20 px, wrapped to 160 px) with its own arrows and close.
public sealed class HudQuestWindow
{
    public const int Width = 256;
    public const int Height = 256;
    public const int RowsPerPage = 5;
    public const int DescriptionLines = 8;
    private const int DesignX = 539;
    private const int DesignY = 155;
    private const int DescriptionWidth = 160;
    private static readonly Color DescriptionColor = new(255, 190, 107);
    private static readonly Color Selected = new(255, 0, 0);

    private readonly HudSlide _slide = new();
    private int _firstRow;
    private int _selected = -1;
    private int _firstLine;
    private string _description = "";
    private string _descriptionTitle = "";
    private bool _pendingClamp;

    public HudQuestPage Page { get; private set; } = HudQuestPage.Running;
    public bool Open => _slide.Open;
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;
    public bool DescriptionOpen => _selected >= 0;

    public static Point Origin(HudLayout layout, int slideMove = HudSlide.Full) =>
        new(layout.Width - (HudLayout.DesignWidth - DesignX) * Math.Clamp(slideMove, 0, HudSlide.Full) / HudSlide.Full, DesignY + layout.BottomOffset);

    public static Rectangle Bounds(Point origin) => new(origin.X, origin.Y - 8, Width, Height + 8);

    public static Rectangle DescriptionBounds(Point origin) => new(origin.X - 195, origin.Y + 15, Width, Height);

    // List buttons: previous page, next page, running, finished, close.
    public static Rectangle ListButton(Point origin, int index) => new(origin.X + 100 + index * 23, origin.Y + 220, 20, 20);

    // Description buttons: previous lines, next lines, close.
    public static Rectangle PageButton(Point origin, int index) => index switch
    {
        0 => new Rectangle(origin.X - 80 + 2 * 23, origin.Y + 220, 20, 20),
        1 => new Rectangle(origin.X - 80 + 3 * 23, origin.Y + 220, 20, 20),
        _ => new Rectangle(origin.X - 80 + 5 * 23 - 14, origin.Y + 220, 20, 20)
    };

    public static Rectangle Row(Point origin, int row) => new(origin.X + 110, origin.Y + 40 + row * 30, 120, 20);

    public Point CurrentOrigin(HudLayout layout) => Origin(layout, _slide.Move);

    public void Toggle()
    {
        _slide.Open = !_slide.Open;
        if (!_slide.Open)
        {
            _selected = -1;
        }
    }

    public void Close()
    {
        _slide.Open = false;
        _selected = -1;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(HudLayout layout, Point pointer)
    {
        if (!_slide.Visible)
        {
            return false;
        }

        var origin = CurrentOrigin(layout);
        return Bounds(origin).Contains(pointer) || DescriptionOpen && DescriptionBounds(origin).Contains(pointer);
    }

    private static IReadOnlyList<QuestBoardEntry> Entries(QuestView view, HudQuestPage page) =>
        view is null ? [] : page == HudQuestPage.Running ? view.Progress : view.Finished;

    // Handles a click inside the window; returns false when the pointer is outside it.
    public bool Click(HudLayout layout, Point pointer, QuestView view)
    {
        if (!_slide.FullyOpen || !Contains(layout, pointer))
        {
            return false;
        }

        var origin = CurrentOrigin(layout);
        var entries = Entries(view, Page);
        for (var index = 0; index < 5; index++)
        {
            if (!ListButton(origin, index).Contains(pointer))
            {
                continue;
            }

            switch (index)
            {
                case 0:
                    _firstRow = Math.Max(0, _firstRow - RowsPerPage);
                    break;
                case 1:
                    _firstRow = _firstRow + RowsPerPage < entries.Count ? _firstRow + RowsPerPage : _firstRow;
                    break;
                case 2 or 3:
                    var page = index == 2 ? HudQuestPage.Running : HudQuestPage.Finished;
                    if (page != Page)
                    {
                        Page = page;
                        _firstRow = 0;
                        _selected = -1;
                    }

                    break;
                default:
                    Close();
                    break;
            }

            return true;
        }

        for (var row = 0; row < RowsPerPage; row++)
        {
            var index = _firstRow + row;
            if (index < entries.Count && Row(origin, row).Contains(pointer))
            {
                _selected = _selected == index ? -1 : index;
                _firstLine = 0;
                _description = _selected < 0 ? "" : HudQuestDocuments.BoardPath(entries[index].Description);
                _descriptionTitle = _selected < 0 ? "" : entries[index].Title;
                return true;
            }
        }

        if (DescriptionOpen)
        {
            if (PageButton(origin, 0).Contains(pointer))
            {
                _firstLine = Math.Max(0, _firstLine - DescriptionLines);
            }
            else if (PageButton(origin, 1).Contains(pointer))
            {
                _firstLine += DescriptionLines;
                _pendingClamp = true;
            }
            else if (PageButton(origin, 2).Contains(pointer))
            {
                _selected = -1;
            }
        }

        return true;
    }

    // The journal changed: a selection past the end of the list closes.
    public void Refresh(QuestView view)
    {
        var entries = Entries(view, Page);
        if (_selected >= entries.Count)
        {
            _selected = -1;
        }

        if (_firstRow >= entries.Count)
        {
            _firstRow = Math.Max(0, (entries.Count - 1) / RowsPerPage * RowsPerPage);
        }
    }

    public void Draw(HudPainter painter, HudLayout layout, SpriteFont font, QuestView view, HudQuestDocuments documents, Point pointer)
    {
        if (!_slide.Visible)
        {
            return;
        }

        var origin = CurrentOrigin(layout);
        var active = _slide.FullyOpen;
        if (DescriptionOpen && active)
        {
            var panel = DescriptionBounds(origin);
            painter.Stretch(HudQuestArt.Description, panel);
        }

        painter.Stretch(HudQuestArt.Window, new Rectangle(origin.X, origin.Y, Width, Height));
        painter.Stretch(HudQuestArt.Title, new Rectangle(origin.X, origin.Y - 8, Width, 32));
        if (!active)
        {
            return;
        }

        DrawList(painter, origin, Entries(view, Page), pointer);
        DrawListButtons(painter, origin, pointer);
        if (DescriptionOpen)
        {
            DrawDescription(painter, origin, font, documents, pointer);
        }
    }

    private void DrawList(HudPainter painter, Point origin, IReadOnlyList<QuestBoardEntry> entries, Point pointer)
    {
        for (var row = 0; row < RowsPerPage; row++)
        {
            var index = _firstRow + row;
            if (index >= entries.Count)
            {
                break;
            }

            var bounds = Row(origin, row);
            var hovered = bounds.Contains(pointer);
            painter.Sprite(hovered ? HudQuestArt.Hovered : HudQuestArt.Running, bounds.X - 15, bounds.Y);
            var color = index == _selected ? Selected : Color.White;
            painter.Text(painter.Fit(entries[index].Title, 130), bounds.X + 6, bounds.Y, color);
        }
    }

    private void DrawListButtons(HudPainter painter, Point origin, Point pointer)
    {
        Rectangle? tip = null;
        string tipArt = null;
        for (var index = 0; index < 5; index++)
        {
            var bounds = ListButton(origin, index);
            var hovered = bounds.Contains(pointer);
            var selected = index == 2 && Page == HudQuestPage.Running || index == 3 && Page == HudQuestPage.Finished;
            painter.Sprite(hovered || selected ? HudQuestArt.ListButtons[index] : HudQuestArt.ListButtonsIdle[index], bounds.X, bounds.Y);
            if (hovered)
            {
                tip = bounds;
                tipArt = HudQuestArt.ListTips[index];
            }
        }

        if (tip is { } at)
        {
            painter.Sprite(tipArt, at.X - 30, at.Y - 30);
        }
    }

    private void DrawDescription(HudPainter painter, Point origin, SpriteFont font, HudQuestDocuments documents, Point pointer)
    {
        var x = origin.X - 105;
        var y = origin.Y + 55;
        painter.Text(painter.Fit(_descriptionTitle, DescriptionWidth + 20), x + 30, y - 20, DescriptionColor);
        var lines = documents.Wrapped(_description, font, DescriptionWidth) ?? [];
        if (_pendingClamp)
        {
            _pendingClamp = false;
            if (_firstLine >= lines.Length)
            {
                _firstLine = Math.Max(0, _firstLine - DescriptionLines);
            }
        }

        for (var line = 0; line < DescriptionLines && _firstLine + line < lines.Length; line++)
        {
            painter.Text(lines[_firstLine + line], x, y + line * 20, DescriptionColor);
        }

        Rectangle? tip = null;
        string tipArt = null;
        for (var index = 0; index < 3; index++)
        {
            var bounds = PageButton(origin, index);
            var hovered = bounds.Contains(pointer);
            painter.Sprite(hovered ? HudQuestArt.PageButtons[index] : HudQuestArt.PageButtonsIdle[index], bounds.X, bounds.Y);
            if (hovered)
            {
                tip = bounds;
                tipArt = HudQuestArt.PageTips[index];
            }
        }

        if (tip is { } at)
        {
            painter.Sprite(tipArt, at.X - 30, at.Y - 30);
        }
    }
}
