using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.UI.Travel;

// The wing warp map a warp stone opens: WG_map.tga (256 x 256) at (472, 120) with the 16 x 16 icon of every stone the
// carried wing reaches (WingGates), three rings that grow and fade from the hovered icon, the help box at (50, 45) with
// the hovered stone's name and document (the warp gate's fee table while none is hovered) and the cancel button at the
// map's lower right. Choosing a stone checks the level and the fee, then asks for confirmation before the trip is sent;
// the server checks the stone, wing, level and fee again.
public sealed class HudWingGate(IClientLog log)
{
    public const int MapX = 472;
    public const int MapY = 120;
    public const int MapSize = 256;
    public const int IconSize = 16;
    public const int TextX = HudNpcPanel.BoxX + 30;
    public const int TextY = HudNpcPanel.BoxY + 66;
    public const int TextWidth = HudNpcPanel.SizeW * 32;
    public const string CancelBox = "image/clanImage/chipbutton/msBOX.bmp";
    public const string CancelButton = "image/clanImage/MessageBox/c.bmp";
    public const string CancelButtonHover = "image/clanImage/MessageBox/c1.bmp";
    // The rings start every forty frames of the original's seventy a second while an icon is hovered.
    private const int RingPeriodFrames = 40;
    private static readonly Color TitleColor = HudTextColors.HelpText;

    public static readonly string[] Art = [WingGates.MapArt, WingGates.IconArt, WingGates.SelectArt, CancelBox, CancelButton, CancelButtonHover];

    private readonly HudRequest<WingGateOutcome> _request = new(log, "Wing gate trip failed");
    private WingGateDestination _confirm;
    private WingGateDestination _helpFor;
    private string[] _help;
    private long _wingRevision = -1;
    private WingGateWing _wing;
    private WingGateDestination _ringAt;
    private double _ringStartMs = double.NegativeInfinity;
    private int _currentField;

    // Sends a trip to the server; set by the screen.
    public Func<int, Task<WingGateOutcome>> Travel { get; set; }

    // The level a field asks of its visitors; set by the screen from the world catalog.
    public Func<int, int> RequiredLevel { get; set; } = static _ => 0;

    public bool IsOpen { get; private set; }
    public bool Confirming => _confirm is not null;
    public bool Pending => _request.Pending;
    public string Message { get; private set; } = "";
    public int Completions { get; private set; }
    public WingGateOutcome LastOutcome { get; private set; }

    public static Rectangle Map => new(MapX, MapY, MapSize, MapSize);

    // The hover area of the cancel button; its 48 x 23 box is drawn at the same corner.
    public static Rectangle Cancel => new(MapX + 184, MapY + 218, 32, 21);

    public static Rectangle Icon(WingGateDestination destination) => new(MapX + destination.MapX, MapY + destination.MapY, IconSize, IconSize);

    // The icon's hover area reaches five pixels beyond it on each side.
    public static Rectangle IconHover(WingGateDestination destination)
    {
        var icon = Icon(destination);
        icon.Inflate(5, 5);
        return icon;
    }

    public void Open(int currentField)
    {
        if (Pending)
        {
            return;
        }

        IsOpen = true;
        _currentField = currentField;
        _confirm = null;
    }

    public void Close()
    {
        IsOpen = false;
        _confirm = null;
    }

    public bool Captures(Point pointer) =>
        IsOpen && (Map.Contains(pointer) || HudNpcPanel.Box.Contains(pointer)) || Confirming && HudMessageBox.Bounds.Contains(pointer);

    public WingGateDestination At(Point pointer, OwnerState state)
    {
        if (!IsOpen || Confirming)
        {
            return null;
        }

        var reach = WingGates.Reach(Wing(state));
        foreach (var destination in WingGates.Destinations)
        {
            if (destination.Index < reach && IconHover(destination).Contains(pointer))
            {
                return destination;
            }
        }

        return null;
    }

    // A click while the map or its confirmation is open; true when the map took it.
    public bool Click(Point pointer, OwnerState state)
    {
        if (Confirming)
        {
            if (HudMessageBox.Accept.Contains(pointer))
            {
                Send(_confirm);
                Close();
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                _confirm = null;
            }

            return true;
        }

        if (!IsOpen || state is null || !Captures(pointer))
        {
            return false;
        }

        if (Cancel.Contains(pointer))
        {
            Close();
            return true;
        }

        if (At(pointer, state) is not { } destination)
        {
            return true;
        }

        if (state.Level < WingGates.RequiredLevel(destination, RequiredLevel(destination.FieldCode)))
        {
            Report(WingGateStatus.LevelTooLow);
        }
        else if (WingGates.Fee(Wing(state), destination, _currentField) > state.Money)
        {
            Report(WingGateStatus.NotEnoughGold);
        }
        else
        {
            _confirm = destination;
        }

        return true;
    }

    public void Update()
    {
        if (!_request.TryTake(out var outcome))
        {
            return;
        }

        LastOutcome = outcome;
        Message = WingGates.Message(outcome.Status);
        Completions++;
    }

    public void Draw(HudPainter painter, SpriteFont font, HudQuestDocuments documents, OwnerState state, Point pointer, double nowMs)
    {
        if (IsOpen)
        {
            var hovered = At(pointer, state);
            DrawHelp(painter, font, documents, hovered);
            DrawMap(painter, state, hovered, pointer, nowMs);
        }

        if (_confirm is not null)
        {
            painter.Flush();
            HudMessageBox.Draw(painter, _confirm.Name, "Do you want to", pointer, "move there?");
        }
    }

    private void DrawHelp(HudPainter painter, SpriteFont font, HudQuestDocuments documents, WingGateDestination hovered)
    {
        painter.HelpBox(HudNpcPanel.BoxX, HudNpcPanel.BoxY, HudNpcPanel.SizeW, HudNpcPanel.SizeH);
        var title = HudNpcPanel.Title;
        painter.Sprite(HudArt.HelpTitle, title.X, title.Y);
        painter.TextCentered(hovered?.Name ?? "Warp gate", title.Left, title.Right, title.Y + 7, TitleColor);
        var lines = HelpLines(font, documents, hovered);
        var capacity = (HudNpcPanel.OkButton.Y - 8 - TextY) / HudPainter.LineHeight;
        for (var line = 0; line < lines.Length && line < capacity; line++)
        {
            painter.Text(lines[line], TextX, TextY + line * HudPainter.LineHeight);
        }
    }

    private void DrawMap(HudPainter painter, OwnerState state, WingGateDestination hovered, Point pointer, double nowMs)
    {
        painter.Sprite(WingGates.MapArt, MapX, MapY);
        var reach = WingGates.Reach(Wing(state));
        foreach (var destination in WingGates.Destinations)
        {
            if (destination.Index < reach)
            {
                var icon = Icon(destination);
                painter.Sprite(WingGates.IconArt, icon.X, icon.Y);
            }
        }

        if (hovered is not null && (!ReferenceEquals(hovered, _ringAt) || nowMs - _ringStartMs >= RingPeriodFrames * GameUnits.TickMs))
        {
            _ringAt = hovered;
            _ringStartMs = nowMs;
        }

        if (_ringAt is not null)
        {
            var frame = (int)((nowMs - _ringStartMs) / GameUnits.TickMs);
            var origin = Icon(_ringAt);
            for (var ring = 0; ring < 3; ring++)
            {
                var alpha = 155 + ring * 20 - 5 * frame;
                if (frame <= 20 + ring * 2 && alpha > 0)
                {
                    var bounds = new Rectangle(origin.X - frame, origin.Y - frame, IconSize + 2 * frame, IconSize + 2 * frame);
                    painter.Stretch(WingGates.SelectArt, bounds, null, Color.White * (alpha / 255f));
                }
            }
        }

        var cancel = Cancel;
        painter.Sprite(CancelBox, cancel.X, cancel.Y);
        painter.Sprite(cancel.Contains(pointer) ? CancelButtonHover : CancelButton, cancel.X + 8, cancel.Y + 4);
    }

    // The help of the hovered destination (the introduction, its %d filled with the wings' fees, when none), made again only
    // when the hovered destination changes or its document has loaded.
    private string[] HelpLines(SpriteFont font, HudQuestDocuments documents, WingGateDestination hovered)
    {
        if (_help is not null && ReferenceEquals(_helpFor, hovered))
        {
            return _help;
        }

        var document = $"{WingGates.DocumentFolder}/{hovered?.Document ?? WingGates.IntroDocument}.sin";
        if (documents.Wrapped(document, font, TextWidth) is not { } wrapped)
        {
            return [];
        }

        var lines = (string[])wrapped.Clone();
        var fee = 0;
        for (var line = 0; hovered is null && line < lines.Length; line++)
        {
            var at = lines[line].IndexOf("%d", StringComparison.Ordinal);
            if (at >= 0 && fee < WingGates.Wings.Count)
            {
                lines[line] = lines[line][..at] + WingGates.Wings[fee++].Fee + lines[line][(at + 2)..];
            }
        }

        _helpFor = hovered;
        _help = lines;
        return lines;
    }

    // The best wing carried, worked out again only when the inventory changes.
    private WingGateWing Wing(OwnerState state)
    {
        if (state is null)
        {
            return null;
        }

        if (state.InventoryRevision != _wingRevision)
        {
            _wingRevision = state.InventoryRevision;
            _wing = WingGates.BestWing(state.Items.Where(static item => item.InBag || item.QuestBox >= 0).Select(static item => item.Code));
        }

        return _wing;
    }

    private void Report(WingGateStatus status)
    {
        LastOutcome = new WingGateOutcome(status, -1);
        Message = WingGates.Message(status);
        Completions++;
    }

    private void Send(WingGateDestination destination)
    {
        if (Travel is null || destination is null)
        {
            return;
        }

        var index = destination.Index;
        _request.Start(() => Travel(index), _ => new WingGateOutcome(WingGateStatus.StorageFailed, index));
    }

}
