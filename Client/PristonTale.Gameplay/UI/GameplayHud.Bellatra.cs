using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Bellatra;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.UI;

// Bellatra on the HUD: the board while the server shows it, a notice when a round starts, when the player's team goes on
// or is out and when the game is over, and the secretary's window (a refused request shows its reason in the notice
// strip). A team booth offers its ticket as the NPC panel's one destination.
public sealed partial class GameplayHud
{
    private readonly HudBellatraBoard _bellatraBoard = new();
    private readonly HudBellatraWindow _bellatraWindow = new();

    // Sends the secretary window's requests; set by the screen.
    public Action<BellatraClanAction, ulong, long> SendBellatraClan
    {
        set => _bellatraWindow.Send = value;
    }

    public void ObserveBellatra(PresenceUpdate update)
    {
        if (update.BellatraClan is { } clan)
        {
            _bellatraWindow.Observe(clan);
        }

        if (update.BellatraClanResult is { } result)
        {
            _bellatraWindow.Result(result);
            if (result.Status != Contracts.Social.BellatraClanStatus.Ok)
            {
                _notice.Show(ClientText.Get("Bellatra.Status." + result.Status));
            }
        }

        if (update.Bellatra is not { } board)
        {
            return;
        }

        var was = _bellatraBoard.Board;
        _bellatraBoard.Apply(board);
        if (board.Shown && board.Outcome != was.Outcome && board.Outcome != Contracts.Social.BellatraOutcome.Playing)
        {
            _notice.Show(ClientText.Get("Bellatra.Outcome." + board.Outcome));
        }
        else if (board.Shown && board.Round > 0 && board.Round != was.Round)
        {
            _notice.Show(ClientText.Format("Bellatra.Round", board.Round.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private void OpenBellatra(ulong npcId)
    {
        _npcOpen = false;
        _bellatraWindow.Open(npcId);
    }

    private void AskRedemption(long maximum, Action<int> accepted) =>
        _countBox.Open(ClientText.Get("Bellatra.HowMuch"), ClientText.Get("Bellatra.ToRedeem"), maximum, accepted);

    private bool ClickBellatra(Point pointer) => _bellatraWindow.Click(pointer);

    private bool CapturesBellatra(Point pointer) => _bellatraWindow.Contains(pointer);
}
