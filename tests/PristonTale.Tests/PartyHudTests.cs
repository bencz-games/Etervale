using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Parties;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Tests;

// The party in the HUD: the party page of the party window, the request box and the buttons under a player's portrait.
internal static class PartyHudTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the party page lists members with levels in tens, gives the leader its buttons, picks a member and whispers on a double click", WindowAsync);
        yield return ("the party request box answers once from its buttons and closes itself after twenty seconds, the trade's after ten", RequestAsync);
        yield return ("the trade, party and friend buttons sit under the portrait from 36 px left of its centre", PlayerButtonsAsync);
    }

    private static readonly PartyInfo Party = new(
    [
        new PartyMemberInfo(1, "Ann", 45, 100, 200, System.Numerics.Vector2.Zero),
        new PartyMemberInfo(2, "Bob", 9, 50, 50, System.Numerics.Vector2.Zero)
    ]);

    private static Task WindowAsync()
    {
        Assert.True(HudPartyWindow.LevelText(45) == "L4x" && HudPartyWindow.LevelText(10) == "L10" && HudPartyWindow.LevelText(9) == "L9",
            "levels past 10 show in tens");
        Assert.True(Enumerable.Range(0, 5).All(button => HudPartyWindow.Enabled(button, Party, 1)), "the leader has every button");
        Assert.True(!HudPartyWindow.Enabled(0, Party, 2) && !HudPartyWindow.Enabled(2, Party, 2) && HudPartyWindow.Enabled(3, Party, 2)
            && HudPartyWindow.Enabled(HudPartyWindow.CloseButton, Party, 2), "a member may only leave and close");
        Assert.True(!HudPartyWindow.Enabled(3, PartyInfo.None, 1) && HudPartyWindow.Enabled(HudPartyWindow.CloseButton, PartyInfo.None, 1),
            "without a party only close works");

        var layout = HudLayout.Create(800, 600);
        var window = new HudPartyWindow();
        var commands = new List<(PartyCommand, ulong)>();
        var whispers = new List<string>();
        window.Commanded = (command, member) => commands.Add((command, member));
        window.Whispered = whispers.Add;
        window.Show();
        window.Advance(1);
        var origin = window.CurrentOrigin(layout);
        Assert.True(origin == new Point(539, 155), $"the window stands at {origin}");

        var bob = HudPartyWindow.Row(origin, 1).Center;
        Assert.True(window.Click(layout, bob, Party, 1, 10) && window.Picked == 2, "a click picks the member");
        Assert.True(window.Click(layout, HudSidePanel.Button(origin, (int)PartyCommand.Kick).Center, Party, 1, 11)
            && commands is [(PartyCommand.Kick, 2)] && window.Picked == 0, "kick acts on the picked member and clears the pick");
        window.Click(layout, bob, Party, 1, 20);
        window.Click(layout, bob, Party, 1, 20.2);
        Assert.True(whispers is ["Bob"], "a double click whispers to the member");
        window.Click(layout, HudPartyWindow.Row(origin, 0).Center, Party, 1, 30);
        window.Click(layout, HudPartyWindow.Row(origin, 0).Center, Party, 1, 30.1);
        Assert.True(whispers.Count == 1, "the player whispered to itself");

        window.Click(layout, HudSidePanel.Button(origin, (int)PartyCommand.Delegate).Center, Party, 2, 40);
        Assert.True(commands.Count == 1, "a member used a leader's button");
        window.Click(layout, bob, Party, 1, 45);
        window.Refresh(new PartyInfo([Party.Members[0]]));
        Assert.True(window.Picked == 0, "a member who left stayed picked");
        Assert.True(window.Click(layout, HudSidePanel.Button(origin, HudPartyWindow.CloseButton).Center, Party, 1, 50) && !window.Open,
            "close did not close");
        return Task.CompletedTask;
    }

    private static Task RequestAsync()
    {
        var box = new HudRequestBox(HudPartyArt.Request, PartyRules.InviteMs);
        box.Show(7, "Ann", ClientText.Format(PartyMessages.Request), 1000);
        Assert.True(box.IsOpen && box.Click(new Point(0, 300)) is null && box.IsOpen, "a click elsewhere answered");
        Assert.True(box.Click(box.Ok.Center) == true && !box.IsOpen, "OK accepts and closes");
        box.Show(7, "Ann", ClientText.Format(PartyMessages.Request), 1000);
        Assert.True(box.Click(box.Cancel.Center) == false && !box.IsOpen && box.FromId == 7, "cancel refuses and closes");
        box.Show(7, "Ann", ClientText.Format(PartyMessages.Request), 1000);
        box.Update(1000 + PartyRules.InviteMs - 1);
        Assert.True(box.IsOpen, "the box closed early");
        box.Update(1000 + PartyRules.InviteMs);
        Assert.True(!box.IsOpen && box.Ok.Location == new Point(653, 14), "the box outlived its request or moved");

        var trade = new HudRequestBox(PristonTale.Gameplay.UI.Trades.HudTradeArt.Request, PristonTale.Contracts.Social.TradeLimits.RequestMs);
        trade.Show(9, "Bob", null, 0);
        trade.Update(PristonTale.Contracts.Social.TradeLimits.RequestMs);
        Assert.True(!trade.IsOpen && trade.Cancel.Location == new Point(657, 40), "the trade box keeps the original's ten seconds and place");
        return Task.CompletedTask;
    }

    private static Task PlayerButtonsAsync()
    {
        var frame = HudCombat.TargetFrame(HudLayout.Create(800, 600));
        var center = frame.Box.X + frame.Box.Width / 2;
        var party = HudPlayerButtons.Button(frame, HudPlayerButton.Party);
        Assert.True(HudPlayerButtons.Button(frame, HudPlayerButton.Trade) == new Rectangle(center - 36, frame.Box.Bottom, 20, 20)
            && party.X == center - 11 && HudPlayerButtons.Menu(frame) == new Rectangle(center - 50, frame.Box.Bottom - 6, 100, 50), "button places");
        Assert.True(HudPlayerButtons.At(frame, party.Center) == HudPlayerButton.Party && HudPlayerButtons.At(frame, new Point(center, frame.Box.Y)) is null,
            "the pointer finds the party button only over it");
        return Task.CompletedTask;
    }
}
