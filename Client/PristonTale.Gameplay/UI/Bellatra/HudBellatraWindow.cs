using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.UI.Bellatra;

// The window of Bellatra's secretary, in the NPC panel: the clan operating Bellatra and its chief, the tax on prizes and
// the player's clan's income, which its chief redeems (all of it or part); its other page lists yesterday's best clans.
// The server answers every request with its result and the window as it is then.
public sealed class HudBellatraWindow
{
    private enum Page
    {
        Closed,
        Main,
        Ranking
    }

    private Page _page;
    private ulong _npcId;
    private BellatraClanAction _requested;
    private (string Text, Color Color)[] _rows = [];
    private string[] _buttons = [];
    private string[] _fitted;
    private string[] _fittedButtons;

    public BellatraClanInfo View { get; private set; } = BellatraClanInfo.None;

    public bool IsOpen => _page != Page.Closed;

    public bool Pending { get; private set; }

    // Sends a request with a redemption's gold; set by the HUD.
    public Action<BellatraClanAction, ulong, long> Send { get; set; }

    // Asks how much gold to redeem (at most the income); set by the HUD.
    public Action<long, Action<int>> AskAmount { get; set; }

    public void Open(ulong npcId)
    {
        _npcId = npcId;
        Go(Page.Main);
        Request(BellatraClanAction.Open, 0);
    }

    public void Close() => _page = Page.Closed;

    public void Observe(BellatraClanInfo view)
    {
        View = view;
        if (!view.HasView)
        {
            Close();
        }

        Refresh();
    }

    public void Result(BellatraClanResultInfo result)
    {
        if (result.Action == _requested)
        {
            Pending = false;
        }
    }

    public bool Contains(Point pointer) => IsOpen && HudNpcPanel.Box.Contains(pointer);

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

        if (!Pending && HudPanelPage.ButtonAt(pointer, _buttons.Length) is var button && button >= 0)
        {
            Activate(_buttons[button]);
        }

        return true;
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!IsOpen)
        {
            return;
        }

        _fitted ??= [.. _rows.Select(row => painter.Fit(row.Text, HudNpcPanel.TextWidth))];
        _fittedButtons ??= [.. _buttons.Select(button => painter.Fit(ClientText.Get(button), HudNpcPanel.ButtonWidth - 10))];
        HudPanelPage.Frame(painter, ClientText.Get("Bellatra.Secretary"));
        for (var line = 0; line < _rows.Length && line < HudPanelPage.Capacity(_buttons.Length); line++)
        {
            painter.Text(_fitted[line], HudNpcPanel.TextLeft, HudNpcPanel.TextTop + HudPanelPage.LineStep * line, _rows[line].Color);
        }

        HudPanelPage.Buttons(painter, _fittedButtons, pointer, Pending);
    }

    private void Activate(string button)
    {
        switch (button)
        {
            case "Bellatra.Redeem":
                AskAmount?.Invoke(View.Balance, amount => Request(BellatraClanAction.Redeem, amount));
                break;
            case "Bellatra.Ranking":
                Go(Page.Ranking);
                break;
            default:
                Go(Page.Main);
                break;
        }
    }

    private void Request(BellatraClanAction action, long amount)
    {
        Pending = true;
        _requested = action;
        Send?.Invoke(action, _npcId, amount);
    }

    private void Go(Page page)
    {
        _page = page;
        Refresh();
    }

    private void Refresh()
    {
        _fitted = null;
        _fittedButtons = null;
        var view = View;
        _buttons = _page switch
        {
            Page.Main when view.CanRedeem => ["Bellatra.Redeem", "Bellatra.Ranking"],
            Page.Main => ["Bellatra.Ranking"],
            Page.Ranking => ["Bellatra.Back"],
            _ => []
        };
        _rows = _page switch
        {
            Page.Main =>
            [
                (view.OperatingClanId == 0 ? ClientText.Get("Bellatra.NoOperator") : ClientText.Format("Bellatra.Operator", view.OperatingName), HudPanelPage.Chosen),
                (view.OperatingClanId == 0 ? "" : ClientText.Format("Bellatra.Chief", view.Chief), Color.White),
                (ClientText.Format("Bellatra.Tax", string.Create(CultureInfo.InvariantCulture, $"{view.Tax}%")), Color.White),
                (ClientText.Format("Bellatra.Income", view.Balance.ToString("N0", CultureInfo.InvariantCulture)), Color.White),
                (ClientText.Get(view.CanRedeem ? "Bellatra.ChiefRedeems" : "Bellatra.ChiefOnly"), HudPanelPage.Muted)
            ],
            Page.Ranking when view.Top.Count == 0 => [(ClientText.Get("Bellatra.NoRanking"), HudPanelPage.Muted)],
            Page.Ranking => [.. view.Top.Select((clan, index) => (ClientText.Format("Bellatra.RankLine", (index + 1).ToString(CultureInfo.InvariantCulture), clan.Name,
                clan.Points.ToString("N0", CultureInfo.InvariantCulture)), index == 0 ? HudPanelPage.Chosen : Color.White))],
            _ => []
        };
    }
}
