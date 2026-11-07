using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Parties;

public enum HudPartyPage
{
    Party,
    Friends,
    Clan
}

// The party window's party and friends pages (its "Party" and "chatting" tabs over the frame, at x 60 and 120), and for a
// clan's members the clan page (a third tab at x 180; the titles then show three tabs), where its quest page
// (HudQuestWindow) stands: 256x256 at (539, 155) of the 800x600 frame (right and bottom anchored), sliding in from the
// right edge. The friends page is HudFriendsPage, the clan page HudClanPage. On the party page, members go down from (80, 38) at 30 px
// steps: the life bar (94 px) under the name, the member's mark (the leader's own) before it and the level after it,
// shown in tens past level 10 ("L4x"). Under them the delegate, disband, kick, leave and close buttons: the first three
// are the leader's, and without a party only close works. A click picks a member for delegate and kick (neither acts
// with nobody picked); a double click whispers to it.
public sealed class HudPartyWindow
{
    public const int CloseButton = 4;
    private const int LifeWidth = 94;
    public const double DoubleClickSeconds = 0.4;
    private static readonly Color NameColor = new(209, 190, 154);
    private static readonly Color PickedColor = new(255, 150, 0);
    private static readonly Color LevelColor = new(255, 190, 107);
    private static readonly string[] LevelTexts = new string[256];

    private readonly HudSlide _slide = new();
    private ulong _lastClicked;
    private double _lastClickSeconds;

    public bool Open => _slide.Open;

    public HudPartyPage Page { get; private set; }
    public Contacts.HudFriendsPage Friends { get; } = new();
    public Clans.HudClanPage Clan { get; } = new();
    public bool Visible => _slide.Visible;
    public bool FullyOpen => _slide.FullyOpen;

    // The member the leader's delegate and kick buttons act on; 0 for none.
    public ulong Picked { get; private set; }

    // A party button pressed (with the picked member) and a member double-clicked (its name), for the HUD to send.
    public Action<PartyCommand, ulong> Commanded { get; set; }
    public Action<string> Whispered { get; set; }


    // What the pointer must be over to point at a member (its mark, name and level).
    public static Rectangle Row(Point origin, int index) => new(origin.X + 80, origin.Y + 38 + index * 30 - 10, 110, 28);

    public static bool Enabled(int button, PartyInfo party, ulong self) =>
        button == CloseButton || party.Members.Count > 0 && (button == (int)PartyCommand.Leave || party.Leads(self));

    public static string LevelText(int level)
    {
        return (uint)level >= LevelTexts.Length ? Format(level) : LevelTexts[level] ??= Format(level);

        static string Format(int level) => level > 10 ? $"L{level / 10}x" : $"L{level}";
    }

    public Point CurrentOrigin(HudLayout layout) => HudSidePanel.Origin(layout, _slide.Move);

    public HudPartyWindow()
    {
        Friends.Closed = Close;
        Clan.Closed = Close;
    }

    public static Rectangle PartyTab(Point origin) => new(origin.X + 60, origin.Y + 2, 60, 20);
    public static Rectangle FriendsTab(Point origin) => new(origin.X + 120, origin.Y + 2, 60, 20);
    public static Rectangle ClanTab(Point origin) => new(origin.X + 180, origin.Y + 2, 60, 20);

    public void Show()
    {
        Page = HudPartyPage.Party;
        _slide.Open = true;
    }

    public void ShowFriends(Contacts.HudFriendsView view)
    {
        Page = HudPartyPage.Friends;
        Friends.Show(view);
        _slide.Open = true;
    }

    // The player's clan; leaving it takes the window off the clan page.
    public void ObserveClan(ClanInfo clan)
    {
        Clan.Observe(clan);
        if (!clan.HasClan && Page == HudPartyPage.Clan)
        {
            Page = HudPartyPage.Party;
        }
    }

    public void Toggle()
    {
        if (_slide.Open)
        {
            Close();
        }
        else
        {
            Show();
        }
    }

    public void Close()
    {
        _slide.Open = false;
        Picked = 0;
    }

    public void Advance(double seconds) => _slide.Advance(seconds);

    public bool Contains(HudLayout layout, Point pointer) => _slide.Visible && HudSidePanel.Bounds(CurrentOrigin(layout)).Contains(pointer);

    // A picked member who left is no longer picked.
    public void Refresh(PartyInfo party)
    {
        if (Picked != 0 && !party.Contains(Picked))
        {
            Picked = 0;
        }
    }

    // Handles a click inside the window; false when the pointer is outside it.
    public bool Click(HudLayout layout, Point pointer, PartyInfo party, ulong self, double nowSeconds)
    {
        if (!_slide.FullyOpen || !Contains(layout, pointer))
        {
            return false;
        }

        var origin = CurrentOrigin(layout);
        if (TabAt(origin, pointer) is { } page)
        {
            Page = page;
            return true;
        }

        if (Page == HudPartyPage.Friends)
        {
            Friends.Click(origin, pointer);
            return true;
        }

        if (Page == HudPartyPage.Clan)
        {
            Clan.Click(origin, pointer, nowSeconds);
            return true;
        }

        for (var index = 0; index <= CloseButton; index++)
        {
            if (!HudSidePanel.Button(origin, index).Contains(pointer))
            {
                continue;
            }

            if (index == CloseButton)
            {
                Close();
            }
            else if (Enabled(index, party, self) && (Picked != 0 || index is not ((int)PartyCommand.Delegate or (int)PartyCommand.Kick)))
            {
                Commanded?.Invoke((PartyCommand)index, Picked);
                Picked = 0;
            }

            return true;
        }

        for (var index = 0; index < party.Members.Count; index++)
        {
            if (!Row(origin, index).Contains(pointer))
            {
                continue;
            }

            var member = party.Members[index];
            if (member.EntityId == _lastClicked && nowSeconds - _lastClickSeconds < DoubleClickSeconds && member.EntityId != self)
            {
                Whispered?.Invoke(member.Name);
                _lastClicked = 0;
            }
            else
            {
                _lastClicked = member.EntityId;
                _lastClickSeconds = nowSeconds;
            }

            Picked = member.EntityId;
            return true;
        }

        return true;
    }

    public void Draw(HudPainter painter, HudLayout layout, PartyInfo party, ulong self, Point pointer)
    {
        if (!_slide.Visible)
        {
            return;
        }

        var origin = CurrentOrigin(layout);
        painter.Stretch(HudPartyArt.Window, new Rectangle(origin.X, origin.Y, HudSidePanel.Width, HudSidePanel.Height));
        painter.Stretch(Title(), new Rectangle(origin.X, origin.Y - 8, HudSidePanel.Width, 32));
        if (!_slide.FullyOpen)
        {
            return;
        }

        if (Page == HudPartyPage.Friends)
        {
            Friends.Draw(painter, origin, pointer);
            return;
        }

        if (Page == HudPartyPage.Clan)
        {
            Clan.Draw(painter, origin, pointer);
            return;
        }

        for (var index = 0; index < party.Members.Count; index++)
        {
            DrawMember(painter, origin, index, party.Members[index], pointer);
        }

        Rectangle? tip = null;
        string tipArt = null;
        for (var index = 0; index <= CloseButton; index++)
        {
            var bounds = HudSidePanel.Button(origin, index);
            var hovered = bounds.Contains(pointer) && Enabled(index, party, self);
            painter.Sprite(hovered ? HudPartyArt.Buttons[index] : HudPartyArt.ButtonsIdle[index], bounds.X, bounds.Y);
            if (hovered)
            {
                tip = bounds;
                tipArt = HudPartyArt.Tips[index];
            }
        }

        if (tip is { } at)
        {
            painter.Sprite(tipArt, at.X - 30, at.Y - 30);
        }
    }

    private HudPartyPage? TabAt(Point origin, Point pointer) =>
        PartyTab(origin).Contains(pointer) ? HudPartyPage.Party
        : FriendsTab(origin).Contains(pointer) ? HudPartyPage.Friends
        : Clan.HasClan && ClanTab(origin).Contains(pointer) ? HudPartyPage.Clan
        : null;

    // A clan's member sees the titles with the clan tab.
    private string Title() => Page switch
    {
        HudPartyPage.Clan => Clans.HudClanArt.Title,
        HudPartyPage.Friends => Clan.HasClan ? Clans.HudClanArt.FriendsTitle : Contacts.HudContactArt.Title,
        _ => Clan.HasClan ? Clans.HudClanArt.PartyTitle : HudPartyArt.Title
    };

    private void DrawMember(HudPainter painter, Point origin, int index, PartyMemberInfo member, Point pointer)
    {
        var x = origin.X + 80;
        var y = origin.Y + 38 + index * 30;
        var life = member.MaxLife <= 0 ? 0 : (int)Math.Clamp((long)member.Life * LifeWidth / member.MaxLife, 0, LifeWidth);
        painter.Sprite(HudPartyArt.LifeRed, x + 11, y + 13);
        if (life > 0)
        {
            painter.Sprite(HudPartyArt.LifeBlue, x + 11, y + 13, new Rectangle(0, 0, life, 4));
        }

        var picked = Row(origin, index).Contains(pointer) || member.EntityId == Picked;
        var mark = index == 0 ? HudPartyArt.Leader : picked ? HudPartyArt.MemberHovered : HudPartyArt.Member;
        painter.Sprite(mark, x - 4, y - 2);
        painter.Text(painter.Fit(member.Name, 96), x + 10, y, picked ? PickedColor : NameColor);
        painter.Text(LevelText(member.Level), x + 110, y + 5, LevelColor);
    }
}
