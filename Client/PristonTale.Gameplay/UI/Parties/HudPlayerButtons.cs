using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Parties;

public enum HudPlayerButton
{
    Trade,
    Party,
    Friend,
    Clan
}

// The buttons under another player's portrait once the local player reached it, as the original's trade, party and friend
// requests: cw-1.tga (100x50) from 50 px left of the portrait's centre and 6 px above its bottom, the 20x20 buttons from
// 36 px left of the centre at 25 px steps along the bottom, the hovered one's tip 60 px to its left and 18 px below it. A clan chief or vice
// chief beside a player without a clan also gets the clan invitation: the box widens to 120 px from 60 px left of the
// centre and the four buttons start 10 px further left. The portrait shows no life bar while they do.
public static class HudPlayerButtons
{
    private static readonly HudPlayerButton[] Three = [HudPlayerButton.Trade, HudPlayerButton.Party, HudPlayerButton.Friend];
    private static readonly HudPlayerButton[] Four = Enum.GetValues<HudPlayerButton>();

    public static Rectangle Menu(HudTargetFrame frame, bool clan = false) =>
        clan ? new(Center(frame) - 60, frame.Box.Bottom - 6, 120, 50) : new(Center(frame) - 50, frame.Box.Bottom - 6, 100, 50);

    public static Rectangle Button(HudTargetFrame frame, HudPlayerButton button, bool clan = false) =>
        new(Center(frame) - 36 - (clan ? 10 : 0) + (int)button * 25, frame.Box.Bottom, 20, 20);

    public static HudPlayerButton? At(HudTargetFrame frame, Point pointer, bool clan = false)
    {
        foreach (var button in clan ? Four : Three)
        {
            if (Button(frame, button, clan).Contains(pointer))
            {
                return button;
            }
        }

        return null;
    }

    public static void Draw(HudPainter painter, HudTargetFrame frame, Point pointer, bool clan = false)
    {
        painter.Stretch(HudPartyArt.PlayerMenu, Menu(frame, clan));
        var hovered = At(frame, pointer, clan);
        foreach (var button in clan ? Four : Three)
        {
            var bounds = Button(frame, button, clan);
            var index = (int)button;
            painter.Sprite(button == hovered ? HudPartyArt.PlayerButtons[index] : HudPartyArt.PlayerButtonsIdle[index], bounds.X, bounds.Y);
        }

        if (hovered is { } tip)
        {
            var bounds = Button(frame, tip, clan);
            painter.Sprite(HudPartyArt.PlayerTips[(int)tip], bounds.X - 60, bounds.Y + 18);
        }
    }

    private static int Center(HudTargetFrame frame) => frame.Box.X + (frame.Box.Width >> 1);
}
