using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Gameplay.UI.Clans;

// The Clan Master's window, in the help box the NPC dialog uses: without a clan, the founding text and the name typed
// for it; with one, the clan (state, deadline, members) and the actions of the player's rank, each asked again before it
// is sent; the chief picks a member from the list to expel, appoint or entrust, or a picture from the ClanMarks folder for
// the clan's mark (ClanMarkPicture, read off the game thread; shown fitted before it is sent). A request waits for its
// own result (OK still closes the window meanwhile); the clan that comes back replaces the view.
public sealed class HudClanWindow
{
    private enum Page
    {
        Closed,
        Intro,
        Name,
        View,
        Select,
        Confirm,
        Marks,
        MarkConfirm
    }

    private const int TextWidth = HudNpcPanel.TextWidth;
    private const int TextTop = HudNpcPanel.TextTop;
    private const int LineStep = HudPainter.LineHeight;
    private static readonly Color Highlight = HudTextColors.Marked;
    private static readonly Color Offline = new(170, 170, 170);

    private readonly StringBuilder _name = new();
    private Page _page;
    private ulong _npcId;
    private ClanAction _action;
    private string _target = "";
    private int _firstLine;
    private string[] _lines = [];
    private (string Text, Color Color, string Member)[] _rows = [];
    private bool _rowsStale = true;
    private ClanAction _requested;
    private readonly HudRequest<IReadOnlyList<string>> _listing;
    private readonly HudRequest<ClanMarkPicture> _loading;
    private IReadOnlyList<string> _files;
    private ClanMarkPicture _picture;
    private Texture2D _markPreview;
    private Texture2D _markPreviewSmall;

    public HudClanWindow(IClientLog log = null)
    {
        _listing = new HudRequest<IReadOnlyList<string>>(log ?? NullClientLog.Instance, "listing clan mark pictures");
        _loading = new HudRequest<ClanMarkPicture>(log ?? NullClientLog.Instance, "reading a clan mark picture");
    }

    // Where the chief's mark pictures are listed from (ClanMarkPicture.Folder beside the game).
    public string MarkFolder { get; set; } = ClanMarkPicture.Folder;
    private string[] _buttons = [];

    // Sends a clan request and the chief's new mark (GameScene).
    public Action<ClanAction, ulong, string> Send { get; set; }
    public Action<byte[]> SendMark { get; set; }

    public ClanInfo Clan { get; private set; } = ClanInfo.None;

    public bool IsOpen => _page != Page.Closed;

    public bool Typing => _page == Page.Name;

    public bool Pending { get; private set; }

    // A listing or a picture that finished reading shows (once per frame, from the HUD's update).
    public void Update()
    {
        if (_listing.TryTake(out var files))
        {
            _files = files;
            _lines = [];
            _rowsStale = true;
        }

        if (_loading.TryTake(out var picture))
        {
            _picture = picture;
            if (_page == Page.MarkConfirm)
            {
                Go(_page);
            }
        }
    }

    public void Open(ulong npcId)
    {
        _npcId = npcId;
        Pending = false;
        Go(Clan.HasClan ? Page.View : Page.Intro);
        Send?.Invoke(ClanAction.Open, npcId, "");
    }

    public void Close()
    {
        _page = Page.Closed;
        DropPreview();
    }

    // The clan as the server last sent it (a window open on the old view shows the new one).
    public void Observe(ClanInfo clan)
    {
        Clan = clan;
        _rowsStale = true;
        if (_page is Page.View or Page.Intro)
        {
            Go(clan.HasClan ? Page.View : Page.Intro);
        }
    }

    // A request's end: a done request goes back to the clan (or its absence), a refused one stays (its line is in the chat).
    public void Result(ClanResultInfo result)
    {
        if (!Pending || result.Action != (byte)_requested)
        {
            return;
        }

        Pending = false;
        if (!IsOpen)
        {
            return;
        }

        if (result.Status == ClanStatus.Ok)
        {
            Go(Clan.HasClan ? Page.View : Page.Intro);
        }
    }

    public void Scroll(int wheelDelta) => _firstLine = Math.Max(0, _firstLine - Math.Sign(wheelDelta) * 3);

    public void Type(ReadOnlySpan<char> typed, Func<Keys, bool> pressed, Func<char, bool> canDraw)
    {
        if (_page != Page.Name)
        {
            return;
        }

        _rowsStale |= !typed.IsEmpty;
        foreach (var character in typed)
        {
            if (character == '\b')
            {
                if (_name.Length > 0)
                {
                    _name.Remove(_name.Length - 1, 1);
                }
            }
            else if (!char.IsControl(character) && canDraw(character) && _name.Length < ClanRules.MaxNameLength)
            {
                _name.Append(character);
            }
        }

        if (pressed(Keys.Enter))
        {
            Activate(0);
        }
    }

    public bool Contains(Point pointer) => IsOpen && HudNpcPanel.Box.Contains(pointer);

    // A click on the window: a button, a member of the list, or OK (close). Answers whether the window took it.
    public bool Click(Point pointer)
    {
        if (!Contains(pointer))
        {
            return false;
        }

        if (HudNpcPanel.OkButton.Contains(pointer))
        {
            Close();
            return true;
        }

        if (Pending)
        {
            return true;
        }

        for (var index = 0; index < _buttons.Length; index++)
        {
            if (HudNpcPanel.ServiceButton(index, _buttons.Length).Contains(pointer))
            {
                Activate(index);
                return true;
            }
        }

        if (_page is Page.Select or Page.Marks && LineAt(pointer) is var line && line >= 0 && _rows[line].Member is { Length: > 0 } member)
        {
            _target = _page == Page.Marks ? _rows[line].Text : member;
            if (_page == Page.Marks)
            {
                var folder = MarkFolder;
                _picture = null;
                _loading.Start(() => Task.Run(() => ClanMarkPicture.Load(folder, member)), static _ => null);
            }

            Go(_page == Page.Marks ? Page.MarkConfirm : Page.Confirm);
        }

        return true;
    }

    public void Draw(HudPainter painter, SpriteFont font, Point pointer)
    {
        if (!IsOpen)
        {
            return;
        }

        painter.HelpBox(HudNpcPanel.BoxX, HudNpcPanel.BoxY, HudNpcPanel.SizeW, HudNpcPanel.SizeH);
        var heading = Clan.HasClan ? Clan.Name : ClientText.Get("Clan.Title");
        var title = HudNpcPanel.Title;
        var titleWidth = Math.Min(HudNpcPanel.SizeW * 32, Math.Max(title.Width, painter.Measure(heading) + 40));
        title = new Rectangle(title.Center.X - titleWidth / 2, title.Y, titleWidth, title.Height);
        HudNpcPanel.DrawPlate(painter, title, Color.White);
        painter.TextCentered(painter.Fit(heading, title.Width - 20), title.Left, title.Right, title.Y + 7, HudTextColors.BoxTitle);

        var rows = Rows(font);
        var capacity = Capacity();
        _firstLine = Math.Clamp(_firstLine, 0, Math.Max(0, rows.Length - capacity));
        var hovered = _page is Page.Select or Page.Marks ? LineAt(pointer) : -1;
        for (var line = 0; line < capacity && _firstLine + line < rows.Length; line++)
        {
            var row = rows[_firstLine + line];
            var color = _firstLine + line == hovered && row.Member.Length > 0 ? Highlight : row.Color;
            painter.Text(row.Text, HudNpcPanel.BoxX + 30, TextTop + LineStep * line, color);
        }

        if (rows.Length > capacity)
        {
            var barX = HudNpcPanel.BoxX + (HudNpcPanel.SizeW - 1) * 32 + 80 - 6;
            painter.ScrollBar(barX, HudNpcPanel.BoxY + 53, capacity * LineStep + 16, _firstLine, rows.Length - capacity);
        }

        // The mark as uploaded and, beside it, as name boxes and windows draw it.
        if (_page == Page.MarkConfirm && _picture is { } picture)
        {
            _markPreview ??= painter.CreateTexture(ClanWire.MarkSize, ClanWire.MarkSize, picture.Pixels);
            _markPreviewSmall ??= painter.CreateTexture(ClanMarks.Size, ClanMarks.Size, picture.Small);
            var below = TextTop + LineStep * Math.Min(capacity, rows.Length - _firstLine);
            painter.Image(_markPreview, new Rectangle(HudNpcPanel.BoxX + 30, below, ClanWire.MarkSize, ClanWire.MarkSize));
            painter.Image(_markPreviewSmall, new Rectangle(HudNpcPanel.BoxX + 30 + ClanWire.MarkSize + 12, below + (ClanWire.MarkSize - ClanMarks.Size) / 2,
                ClanMarks.Size, ClanMarks.Size));
        }

        for (var index = 0; index < _buttons.Length; index++)
        {
            var bounds = HudNpcPanel.ServiceButton(index, _buttons.Length);
            painter.Sprite(HudArt.HelpTitle, bounds.X, bounds.Y);
            var color = bounds.Contains(pointer) && !Pending ? Highlight : Pending ? Offline : Color.White;
            painter.TextCentered(painter.Fit(_buttons[index], bounds.Width - 10), bounds.Left, bounds.Right, bounds.Y + 7, color);
        }

        var ok = HudNpcPanel.OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    private void Go(Page page)
    {
        _page = page;
        _firstLine = 0;
        _lines = [];
        _rowsStale = true;
        if (page != Page.MarkConfirm)
        {
            DropPreview();
        }

        if (page == Page.Name)
        {
            _name.Clear();
        }

        _buttons = page switch
        {
            Page.Intro => [ClientText.Get("Clan.Found")],
            Page.Name => [ClientText.Get("Clan.Confirm"), ClientText.Get("Clan.Cancel")],
            Page.View when Clan.Rank == ClanRank.Chief =>
            [
                ClientText.Get("Clan.Expel"), ClientText.Get(Clan.Members.Any(static member => member.Rank == ClanRank.Vice) ? "Clan.Discharge" : "Clan.Appoint"),
                ClientText.Get("Clan.Entrust"), ClientText.Get("Clan.Disband"), ClientText.Get("Clan.Mark")
            ],
            Page.View => [ClientText.Get("Clan.Leave")],
            Page.Select or Page.Marks => [ClientText.Get("Clan.Back")],
            Page.Confirm => [ClientText.Get("Clan.Yes"), ClientText.Get("Clan.No")],
            Page.MarkConfirm when _picture is not null => [ClientText.Get("Clan.Yes"), ClientText.Get("Clan.No")],
            Page.MarkConfirm => [ClientText.Get("Clan.Back")],
            _ => []
        };
    }

    private void Activate(int button)
    {
        switch (_page)
        {
            case Page.Intro:
                Go(Page.Name);
                break;
            case Page.Name when button == 0:
                var name = _name.ToString().Trim();
                if (name.Length > 0)
                {
                    Request(ClanAction.Found, _npcId, name);
                }

                break;
            case Page.Name:
                Go(Page.Intro);
                break;
            case Page.View when Clan.Rank == ClanRank.Chief && button == 4:
                var folder = MarkFolder;
                _files = null;
                _listing.Start(() => Task.Run(() => ClanMarkPicture.List(folder)), static _ => []);
                Go(Page.Marks);
                break;
            case Page.View when Clan.Rank == ClanRank.Chief:
                _action = button switch { 0 => ClanAction.Expel, 1 => ViceAction(), 2 => ClanAction.Entrust, _ => ClanAction.Disband };
                if (_action == ClanAction.Discharge)
                {
                    _target = Clan.Members.First(static member => member.Rank == ClanRank.Vice).Name;
                }

                Go(_action is ClanAction.Disband or ClanAction.Discharge ? Page.Confirm : Page.Select);
                break;
            case Page.View:
                _action = ClanAction.Leave;
                Go(Page.Confirm);
                break;
            case Page.Select or Page.Marks:
                Go(Page.View);
                break;
            case Page.MarkConfirm when button == 0 && _picture is { } picture:
                Wait(ClanAction.Mark);
                SendMark?.Invoke(picture.Pixels);
                break;
            case Page.MarkConfirm:
                Go(Page.Marks);
                break;
            case Page.Confirm when button == 0:
                Request(_action, _action == ClanAction.Entrust ? _npcId : 0, _target);
                break;
            case Page.Confirm:
                Go(Page.View);
                break;
        }
    }

    private ClanAction ViceAction() => Clan.Members.Any(static member => member.Rank == ClanRank.Vice) ? ClanAction.Discharge : ClanAction.Appoint;

    private void Request(ClanAction action, ulong id, string name)
    {
        Wait(action);
        Send?.Invoke(action, id, name);
    }

    private void Wait(ClanAction action)
    {
        Pending = true;
        _requested = action;
    }

    private int Capacity()
    {
        var top = _buttons.Length > 0 ? HudNpcPanel.ServiceButton(0, _buttons.Length).Y : HudNpcPanel.OkButton.Y;
        return Math.Clamp((top - 8 - TextTop) / LineStep, 1, HudNpcPanel.VisibleLines);
    }

    // The row under the pointer (an index into the drawn rows), or -1.
    private int LineAt(Point pointer)
    {
        if (pointer.X < HudNpcPanel.BoxX + 30 || pointer.X > HudNpcPanel.BoxX + 30 + TextWidth || pointer.Y < TextTop)
        {
            return -1;
        }

        var line = (pointer.Y - TextTop) / LineStep;
        return line < Capacity() && _firstLine + line < _rows.Length ? _firstLine + line : -1;
    }

    // The page's lines: wrapped texts, then the member list (chief and vice first) where it shows; built again only after
    // the page, the typed name or the clan changed.
    private (string Text, Color Color, string Member)[] Rows(SpriteFont font)
    {
        if (!_rowsStale)
        {
            return _rows;
        }

        _rowsStale = false;
        if (_lines.Length == 0 || _page == Page.Name)
        {
            _lines = Text().SelectMany(text => text.Length == 0 ? [""] : HudTextLayout.Wrap(font, text, TextWidth, HudPainter.TextScale)).ToArray();
        }

        var rows = _lines.Select(static line => (line, Color.White, "")).ToList();
        if (_page == Page.Marks && _files is { } files)
        {
            foreach (var file in files)
            {
                rows.Add((HudTextLayout.Fit(file, TextWidth, Measure(font)), HudNameColors.Clan, file));
            }
        }

        if (_page is Page.View or Page.Select)
        {
            foreach (var member in Clan.Members)
            {
                if (_page == Page.Select && (member.Rank == ClanRank.Chief || _action == ClanAction.Appoint && member.Rank == ClanRank.Vice))
                {
                    continue;
                }

                var rank = member.Rank switch { ClanRank.Chief => ClientText.Get("Clan.RankChief"), ClanRank.Vice => ClientText.Get("Clan.RankVice"), _ => "" };
                var text = $"{member.Name}  {ClientText.Format("Hud.TargetLevel", member.Level)} {rank}".TrimEnd();
                rows.Add((text, member.Online ? HudNameColors.Clan : Offline, _page == Page.Select ? member.Name : ""));
            }
        }

        _rows = [.. rows];
        return _rows;
    }

    private IEnumerable<string> Text() => _page switch
    {
        Page.Intro =>
        [
            ClientText.Get("Clan.Intro"), "",
            ClientText.Format("Clan.Cost", ClanRules.FoundingCost.ToString("N0", CultureInfo.InvariantCulture), ClanRules.FoundingLevel), "",
            ClientText.Format("Clan.Requirements", ClanRules.RegisteredMembers, ClanRules.GraceDays)
        ],
        Page.Name => [ClientText.Get("Clan.TypeName"), "", "> " + _name + "_"],
        Page.View => [State(), ClientText.Format("Clan.Count", Clan.Members.Count, Clan.Capacity), ""],
        Page.Select => [ClientText.Get(_action switch
        {
            ClanAction.Expel => "Clan.ChooseExpel",
            ClanAction.Entrust => "Clan.ChooseEntrust",
            _ => "Clan.ChooseVice"
        }), ""],
        Page.Marks => [ClientText.Format("Clan.MarkFolder", ClanWire.MarkSize), "", _files switch
        {
            null => ClientText.Get("Clan.MarkLooking"),
            { Count: 0 } => ClientText.Get("Clan.NoMarkFiles"),
            _ => ""
        }],
        Page.MarkConfirm => [_loading.Pending ? ClientText.Get("Clan.MarkLoading")
            : _picture is null ? ClientText.Format("Clan.MarkUnreadable", _target)
            : ClientText.Format("Clan.ConfirmMark", _target)],
        Page.Confirm => [_action switch
        {
            ClanAction.Leave => ClientText.Format("Clan.ConfirmLeave", Clan.Name),
            ClanAction.Disband => ClientText.Get("Clan.ConfirmDisband"),
            ClanAction.Expel => ClientText.Format("Clan.ConfirmExpel", _target),
            ClanAction.Entrust => ClientText.Format("Clan.ConfirmEntrust", _target),
            ClanAction.Appoint => ClientText.Format("Clan.ConfirmAppoint", _target),
            _ => ClientText.Format("Clan.ConfirmDischarge", _target)
        }],
        _ => []
    };

    private static Func<string, float> Measure(SpriteFont font) => text => font.MeasureString(text).X * HudPainter.TextScale;

    private void DropPreview()
    {
        _markPreview?.Dispose();
        _markPreview = null;
        _markPreviewSmall?.Dispose();
        _markPreviewSmall = null;
    }

    public void Dispose() => DropPreview();

    // Registered, or the time left to reach six members.
    private string State()
    {
        if (Clan.DeadlineSeconds == 0)
        {
            return ClientText.Get("Clan.Registered");
        }

        var left = TimeSpan.FromSeconds(Math.Max(0, Clan.DeadlineSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var key = Clan.State == ClanState.Registering ? "Clan.Registering" : "Clan.BelowSix";
        return ClientText.Format(key, (int)left.TotalDays, left.Hours);
    }
}
