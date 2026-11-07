using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Contacts;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Gameplay.UI.Clans;

// The party window's clan page (its third tab, shown to a clan's members): "[Clan]" at (115, 30) over the members, eight
// rows from (81, 49) at 20 px steps (chief, vice, then by name, as the server sends them; a scroll bar at (231, 38) past
// eight). Each row has its mark (the clan's for the chief, the vice's own, a figure for a member) and the name 35 px on,
// grey while offline. A click picks a member (green); a double click whispers to an online one (red, with the whisper
// mark). The chat button at (86, 220) opens the chat on its clan tab; close at (201, 220).
public sealed class HudClanPage
{
    private const int VisibleRows = 8;
    private const int RowStep = 20;
    private const int NameOffset = 35;
    private static readonly Color HeadingColor = new(0, 255, 0);
    private static readonly Color NameColor = new(230, 255, 240);
    private static readonly Color PickedColor = new(0, 255, 0);
    private static readonly Color WhisperColor = new(255, 0, 0);
    private static readonly Color OfflineColor = new(150, 150, 150);

    private ClanInfo _clan = ClanInfo.None;
    private string _heading = "";
    private string[] _shownNames = [];
    private int _first;
    private string _picked;
    private string _whispering;
    private string _lastClicked;
    private double _lastClickSeconds;

    public bool HasClan => _clan.HasClan;

    // A member double-clicked (its name), the chat button and the close button, for the HUD.
    public Action<string> Whispered { get; set; }
    public Action Talk { get; set; }
    public Action Closed { get; set; }

    public static Rectangle Row(Point origin, int row) => new(origin.X + 81, origin.Y + 49 + row * RowStep, 139, 18);

    public static Rectangle ChatButton(Point origin) => new(origin.X + 86, origin.Y + 220, 20, 20);

    public static Rectangle CloseButton(Point origin) => new(origin.X + 201, origin.Y + 220, 20, 20);

    // The clan as the server last sent it; the shown names are fitted again.
    public void Observe(ClanInfo clan)
    {
        _clan = clan;
        _heading = clan.HasClan ? $"[{clan.Name}]" : "";
        _shownNames = new string[clan.Members.Count];
        _first = Math.Clamp(_first, 0, MaxFirst);
    }

    public void Scroll(int wheelDelta) => _first = Math.Clamp(_first - Math.Sign(wheelDelta), 0, MaxFirst);

    public void Click(Point origin, Point pointer, double nowSeconds)
    {
        if (ChatButton(origin).Contains(pointer))
        {
            Talk?.Invoke();
            return;
        }

        if (CloseButton(origin).Contains(pointer))
        {
            Closed?.Invoke();
            return;
        }

        for (var row = 0; row < VisibleRows && _first + row < _clan.Members.Count; row++)
        {
            if (!Row(origin, row).Contains(pointer))
            {
                continue;
            }

            var member = _clan.Members[_first + row];
            if (member.Name == _lastClicked && nowSeconds - _lastClickSeconds < Parties.HudPartyWindow.DoubleClickSeconds && member.Online)
            {
                _whispering = member.Name;
                Whispered?.Invoke(member.Name);
                _lastClicked = null;
            }
            else
            {
                _lastClicked = member.Name;
                _lastClickSeconds = nowSeconds;
            }

            _picked = member.Name;
            return;
        }
    }

    public void Draw(HudPainter painter, Point origin, Point pointer)
    {
        painter.Text(_heading, origin.X + 115, origin.Y + 30, HeadingColor);
        var members = _clan.Members;
        for (var row = 0; row < VisibleRows && _first + row < members.Count; row++)
        {
            var index = _first + row;
            var member = members[index];
            var bounds = Row(origin, row);
            var whispering = member.Name == _whispering;
            if (whispering)
            {
                painter.Sprite(HudContactArt.Whispering, bounds.X, bounds.Y);
            }
            else if (member.Rank == ClanRank.Chief)
            {
                painter.ClanMark(bounds.X, bounds.Y);
            }
            else
            {
                painter.Sprite(member.Rank == ClanRank.Vice ? HudClanArt.Vice : HudClanArt.Member, bounds.X, bounds.Y);
            }

            var color = whispering ? WhisperColor : member.Name == _picked ? PickedColor : member.Online ? NameColor : OfflineColor;
            painter.Text(_shownNames[index] ??= painter.Fit(member.Name, bounds.Width - NameOffset), bounds.X + NameOffset, bounds.Y + 3, color);
        }

        if (members.Count > VisibleRows)
        {
            painter.ScrollBar(origin.X + 231, origin.Y + 38, 160, _first, MaxFirst);
        }

        var chat = ChatButton(origin);
        var close = CloseButton(origin);
        var chatHovered = chat.Contains(pointer);
        var closeHovered = close.Contains(pointer);
        painter.Sprite(chatHovered ? HudClanArt.Chat : HudClanArt.ChatIdle, chat.X, chat.Y);
        painter.Sprite(closeHovered ? HudContactArt.Close : HudContactArt.CloseIdle, close.X, close.Y);
        if (chatHovered || closeHovered)
        {
            var tip = chatHovered ? chat : close;
            painter.Sprite(chatHovered ? HudClanArt.ChatInfo : HudContactArt.CloseInfo, tip.X - 28, tip.Y - 23);
        }
    }

    private int MaxFirst => Math.Max(0, _clan.Members.Count - VisibleRows);
}
