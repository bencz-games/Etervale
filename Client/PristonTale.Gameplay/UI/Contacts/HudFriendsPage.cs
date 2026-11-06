using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Contacts;

public enum HudFriendsView
{
    Recent,
    Friends,
    Ignored
}

// The friends page of the original party window (its "chatting" tab): the recent players (the last ContactLimits.RecentCount
// whispered, traded with or befriended, kept by the client), the friends (online or not) and the ignored players, ContactLimits.PageRows
// names a page from (100, 55) at 20 px steps under the list's title. Seven buttons at (85, 220) 20 px apart (the last four
// 5 px further): the three lists, then on the recent list "make friend" and "ignore", elsewhere the page arrows and
// "delete", then close. A click picks a name; a click on the picked name (a double click too) whispers to it.
public sealed class HudFriendsPage
{
    public const int ButtonCount = 7;
    private static readonly Color TitleColor = new(255, 220, 0);
    private static readonly Color PickedColor = new(255, 200, 100);
    private static readonly Color WhisperColor = new(255, 0, 0);
    private readonly List<string> _recent = [];
    private readonly Dictionary<string, string> _shownNames = new();
    private IReadOnlyList<(string Name, bool Online)> _friends = [];
    private IReadOnlyList<(string Name, bool Online)> _ignored = [];
    private (HudFriendsView View, int Page, int Pages) _titleKey = (HudFriendsView.Recent, -1, -1);
    private string _title = "";
    private int _first;
    private int _picked = -1;
    private string _whispering;

    public HudFriendsView View { get; private set; } = HudFriendsView.Friends;
    public IReadOnlyList<string> Recent => _recent;

    // A name to put on a list or take off it, and a name to whisper to, for the HUD to send.
    public Action<ContactList, bool, string> Changed { get; set; }
    public Action<string> Whispered { get; set; }
    public Action Closed { get; set; }

    public static Rectangle Row(Point origin, int row) => new(origin.X + 100, origin.Y + 55 + row * 20, 120, 18);

    public static Rectangle Button(Point origin, int index) => new(origin.X + 85 + index * 20 + (index >= 3 ? 5 : 0), origin.Y + 220, 20, 20);

    public void Show(HudFriendsView view)
    {
        View = view;
        _first = 0;
        _picked = -1;
    }

    // A player whispered, traded with or befriended goes to the top of the recent list.
    public void Met(string name)
    {
        _recent.RemoveAll(listed => string.Equals(listed, name, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, name);
        if (_recent.Count > ContactLimits.RecentCount)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }
    }

    public void Apply(ContactListInfo info)
    {
        if (info.List == ContactList.Friends)
        {
            _friends = info.Entries;
        }
        else
        {
            _ignored = info.Entries;
        }

        var count = Count;
        _first = Math.Min(_first, Math.Max(0, (count - 1) / ContactLimits.PageRows * ContactLimits.PageRows));
        _picked = _picked >= count ? -1 : _picked;
    }

    public void Click(Point origin, Point pointer)
    {
        for (var index = 0; index < ButtonCount; index++)
        {
            if (Shown(index) && Button(origin, index).Contains(pointer))
            {
                Press(index);
                return;
            }
        }

        for (var row = 0; row < ContactLimits.PageRows; row++)
        {
            var index = _first + row;
            if (index >= Count || !Row(origin, row).Contains(pointer) && !new Rectangle(origin.X + 80, Row(origin, row).Y, 20, 18).Contains(pointer))
            {
                continue;
            }

            if (index == _picked && View != HudFriendsView.Ignored)
            {
                _whispering = NameAt(index);
                Whispered?.Invoke(_whispering);
            }

            _picked = index;
            return;
        }
    }

    public void Draw(HudPainter painter, Point origin, Point pointer)
    {
        var row0 = Row(origin, 0);
        painter.Text(Title(), row0.X + (View == HudFriendsView.Recent ? 20 : View == HudFriendsView.Friends ? 15 : 10), row0.Y - 24, TitleColor);
        for (var row = 0; row < ContactLimits.PageRows && _first + row < Count; row++)
        {
            var index = _first + row;
            var bounds = Row(origin, row);
            var name = NameAt(index);
            var whispering = View != HudFriendsView.Ignored && string.Equals(name, _whispering, StringComparison.OrdinalIgnoreCase);
            if (whispering)
            {
                painter.Sprite(HudContactArt.Whispering, bounds.X - 23, bounds.Y - 3);
            }
            else if (View == HudFriendsView.Friends)
            {
                painter.Sprite(_friends[index].Online ? HudContactArt.Online : HudContactArt.Offline, bounds.X - 20, bounds.Y);
            }

            if (!_shownNames.TryGetValue(name, out var shown))
            {
                _shownNames[name] = shown = painter.Fit(name, bounds.Width);
            }

            painter.Text(shown, bounds.X, bounds.Y, whispering ? WhisperColor : index == _picked ? PickedColor : Color.White);
        }

        Rectangle? tip = null;
        string tipArt = null;
        for (var index = 0; index < ButtonCount; index++)
        {
            if (!Shown(index))
            {
                continue;
            }

            var bounds = Button(origin, index);
            var hovered = bounds.Contains(pointer);
            var (image, idle, info) = Art(index);
            painter.Sprite(hovered || index == (int)View ? image : idle, bounds.X, bounds.Y);
            if (hovered)
            {
                tip = bounds;
                tipArt = info;
            }
        }

        if (tip is { } at)
        {
            painter.Sprite(tipArt, at.X - 30, at.Y - 26);
        }
    }

    private int Count => View switch
    {
        HudFriendsView.Recent => _recent.Count,
        HudFriendsView.Friends => _friends.Count,
        _ => _ignored.Count
    };

    private string NameAt(int index) => View switch
    {
        HudFriendsView.Recent => _recent[index],
        HudFriendsView.Friends => _friends[index].Name,
        _ => _ignored[index].Name
    };

    // The recent list has no page arrows.
    private bool Shown(int index) => index != 3 || View != HudFriendsView.Recent;

    private void Press(int index)
    {
        switch (index)
        {
            case 0 or 1 or 2:
                Show((HudFriendsView)index);
                break;
            case 3:
                _first = Math.Max(0, _first - ContactLimits.PageRows);
                break;
            case 4 when View == HudFriendsView.Recent:
                MoveRecent(ContactList.Friends);
                break;
            case 4:
                _first = _first + ContactLimits.PageRows < Count ? _first + ContactLimits.PageRows : _first;
                break;
            case 5 when View == HudFriendsView.Recent:
                MoveRecent(ContactList.Ignored);
                break;
            case 5 when _picked >= 0 && _picked < Count:
                Changed?.Invoke(View == HudFriendsView.Friends ? ContactList.Friends : ContactList.Ignored, false, NameAt(_picked));
                _picked = -1;
                break;
            case 6:
                Closed?.Invoke();
                break;
        }
    }

    // The original moves the picked recent player onto the list.
    private void MoveRecent(ContactList list)
    {
        if (_picked < 0 || _picked >= _recent.Count)
        {
            return;
        }

        Changed?.Invoke(list, true, _recent[_picked]);
        _recent.RemoveAt(_picked);
        _picked = -1;
    }

    // Formatted only when the list or its page changes.
    private string Title()
    {
        var count = Count;
        var key = (View, count == 0 ? 0 : _first / ContactLimits.PageRows + 1, (count + ContactLimits.PageRows - 1) / ContactLimits.PageRows);
        if (key != _titleKey)
        {
            _titleKey = key;
            _title = View switch
            {
                HudFriendsView.Recent => ClientText.Get("Friends.Recent"),
                HudFriendsView.Friends => ClientText.Format("Friends.Friends", key.Item2, key.Item3),
                _ => ClientText.Format("Friends.Ignored", key.Item2, key.Item3)
            };
        }

        return _title;
    }

    private (string Image, string Idle, string Info) Art(int index) => index switch
    {
        0 => (HudContactArt.RecentButton, HudContactArt.RecentButtonIdle, HudContactArt.RecentInfo),
        1 => (HudContactArt.FriendsButton, HudContactArt.FriendsButtonIdle, HudContactArt.FriendsInfo),
        2 => (HudContactArt.IgnoredButton, HudContactArt.IgnoredButtonIdle, HudContactArt.IgnoredInfo),
        3 => (HudContactArt.Previous, HudContactArt.PreviousIdle, HudContactArt.PreviousInfo),
        4 when View == HudFriendsView.Recent => (HudContactArt.MakeFriend, HudContactArt.MakeFriendIdle, HudContactArt.MakeFriendInfo),
        4 => (HudContactArt.Next, HudContactArt.NextIdle, HudContactArt.NextInfo),
        5 when View == HudFriendsView.Recent => (HudContactArt.Ignore, HudContactArt.IgnoreIdle, HudContactArt.IgnoreInfo),
        5 => (HudContactArt.Delete, HudContactArt.DeleteIdle, HudContactArt.DeleteInfo),
        _ => (HudContactArt.Close, HudContactArt.CloseIdle, HudContactArt.CloseInfo)
    };
}
