using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI.Inventory;

public enum HudTeleportAction
{
    None,
    Close,
    Choose
}

// The Teleport Core's field list: the help box at (50, 45), 9 x 10 tiles, titled TELEPORT, ten fields per page with the
// level each needs, the hovered field in orange, page arrows at (100, 365) and (330, 365) and the OK button that closes
// it. Choosing a field asks for confirmation before the core is used.
public sealed class HudTeleportCore
{
    public const int BoxX = 50;
    public const int BoxY = 45;
    public const int SizeW = 9;
    public const int SizeH = 10;
    public const int TextX = BoxX + 30;
    public const int TextY = BoxY + 66;
    public const int LineStep = 15;
    public const int HeaderLines = 3;
    public const string LeftArrow = "image/Sinimage/shopall/LeftArrow.bmp";
    public const string RightArrow = "image/Sinimage/shopall/RightArrow.bmp";
    public const string LeftArrowHover = "image/Sinimage/Inven/LeftArrow.bmp";
    public const string RightArrowHover = "image/Sinimage/Inven/RightArrow.bmp";
    private static readonly Color Hovered = new(255, 100, 45);
    private static readonly Color Line = new(240, 240, 240);

    public static readonly string[] Art = [LeftArrow, RightArrow, LeftArrowHover, RightArrowHover];

    public bool IsOpen { get; private set; }
    public int Page { get; private set; }
    public Guid ItemId { get; private set; }
    public TeleportCoreDestination Selected { get; private set; }

    public static Rectangle Box => HudPainter.HelpBoxBounds(BoxX, BoxY, SizeW, SizeH);
    public static Rectangle Title => HudPainter.HelpBoxTitle(BoxX, BoxY, SizeW);
    public static Rectangle OkButton => HudPainter.HelpBoxOk(BoxX, BoxY, SizeW, SizeH);
    public static Rectangle PreviousPage => new(100, 365, 20, 20);
    public static Rectangle NextPage => new(330, 365, 20, 20);

    // The row of a field of the current page (0..9), aligned with its text line.
    public static Rectangle Row(int entry) => new(BoxX + 20, TextY + (HeaderLines + entry) * LineStep - 1, SizeW * 20 - 20, LineStep);

    public void Open(Guid itemId)
    {
        IsOpen = true;
        ItemId = itemId;
        Selected = null;
    }

    public void Close()
    {
        IsOpen = false;
        Selected = null;
    }

    public bool Contains(Point pointer) => IsOpen && (Box.Contains(pointer) || PreviousPage.Contains(pointer) || NextPage.Contains(pointer));

    public TeleportCoreDestination At(Point pointer)
    {
        if (!IsOpen)
        {
            return null;
        }

        var entries = Entries();
        for (var index = 0; index < entries.Count; index++)
        {
            if (Row(index).Contains(pointer))
            {
                return entries[index];
            }
        }

        return null;
    }

    public HudTeleportAction Click(Point pointer)
    {
        if (OkButton.Contains(pointer))
        {
            Close();
            return HudTeleportAction.Close;
        }

        if (PreviousPage.Contains(pointer))
        {
            Page = Math.Max(0, Page - 1);
            return HudTeleportAction.None;
        }

        if (NextPage.Contains(pointer))
        {
            Page = Math.Min(TeleportCoreDestinations.PageCount - 1, Page + 1);
            return HudTeleportAction.None;
        }

        if (At(pointer) is { } destination)
        {
            Selected = destination;
            IsOpen = false;
            return HudTeleportAction.Choose;
        }

        return HudTeleportAction.None;
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!IsOpen)
        {
            return;
        }

        painter.HelpBox(BoxX, BoxY, SizeW, SizeH);
        var title = Title;
        painter.Sprite(HudArt.HelpTitle, title.X, title.Y);
        painter.TextCentered("TELEPORT", title.Left, title.Right, title.Y + 7, HudTextColors.HelpText);
        painter.TextCentered(Headers[Page], BoxX, Box.Right, TextY);
        _rule ??= Rule(painter);
        painter.TextCentered(_rule, BoxX, Box.Right, TextY + LineStep);
        var hovered = At(pointer);
        var entries = Entries();
        for (var index = 0; index < entries.Count; index++)
        {
            painter.Text(RowTexts[Page][index], TextX, TextY + (HeaderLines + index) * LineStep, ReferenceEquals(entries[index], hovered) ? Hovered : Line);
        }

        var ok = OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
        painter.Sprite(PreviousPage.Contains(pointer) ? LeftArrowHover : LeftArrow, PreviousPage.X, PreviousPage.Y);
        painter.Sprite(NextPage.Contains(pointer) ? RightArrowHover : RightArrow, NextPage.X, NextPage.Y);
    }

    public static string ConfirmText(TeleportCoreDestination destination) => destination?.Name ?? "";

    // The pages, their headers and row texts never change, so they are built once; the rule of '=' once per font.
    private static readonly TeleportCoreDestination[][] Pages = Enumerable.Range(0, TeleportCoreDestinations.PageCount)
        .Select(static page => TeleportCoreDestinations.All.Skip(page * TeleportCoreDestinations.PageSize).Take(TeleportCoreDestinations.PageSize).ToArray())
        .ToArray();

    private static readonly string[] Headers = Enumerable.Range(1, Pages.Length).Select(static page => $"[Teleport Core - Page {page}]").ToArray();

    private static readonly string[][] RowTexts = Pages
        .Select(static page => page.Select(static entry => $"{entry.Index:00}. {entry.Name} (Lvl {Math.Max(1, entry.RequiredLevel)})").ToArray())
        .ToArray();

    private string _rule;

    private IReadOnlyList<TeleportCoreDestination> Entries() => Pages[Page];

    private static string Rule(HudPainter painter)
    {
        var rule = "=";
        while (painter.Measure(rule + "=") <= Box.Width - 60)
        {
            rule += "=";
        }

        return rule;
    }
}
