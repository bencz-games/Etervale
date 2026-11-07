using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Castle;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Gameplay.UI;

// Bless Castle on the HUD: the server's castle state, the siege board while a siege runs in the castle field, at a
// siege's end a notice to the players there that their clan holds the castle or that the siege is over, and the window of
// the castle's butler for the owner clan (a refused request shows its reason in the notice strip).
public sealed partial class GameplayHud
{
    private readonly HudSiegeBoard _siegeBoard = new();
    private readonly HudCastleWindow _castleWindow = new();

    // Sends the butler window's requests; set by the screen.
    public Action<CastleOwnerAction, ulong, CastleSetup, long> SendCastleOwner
    {
        set => _castleWindow.Send = value;
    }

    public CastleInfo Castle => _siegeBoard.Castle;

    private bool HoldsCastle => CastleSiegeRules.HoldsCastle(Combat.OwnClan, _siegeBoard.Castle.OwnerClanId);

    public void ObserveCastle(PresenceUpdate update)
    {
        if (update.CastleOwner is { } owner)
        {
            _castleWindow.Observe(owner);
        }

        if (update.CastleOwnerResult is { } result)
        {
            _castleWindow.Result(result);
            if (result.Status != CastleOwnerStatus.Ok)
            {
                _notice.Show(ClientText.Get("Castle.Status." + result.Status));
            }
        }

        if (update.Castle is not { } castle)
        {
            return;
        }

        var ended = _siegeBoard.Castle.Siege == SiegeState.Running && castle.Siege != SiegeState.Running;
        _siegeBoard.Apply(castle, _clock.Elapsed.TotalMilliseconds);
        _wingGate.CastleOwner = HoldsCastle;
        _wingGate.Tax = castle.Tax;
        _skills.Tax = castle.Tax;
        _aging.Tax = castle.Tax;
        _mix.Tax = castle.Tax;
        if (ended && InCastleField)
        {
            _notice.Show(ClientText.Get(HoldsCastle ? "Siege.Won" : "Siege.Ended"));
        }
    }

    private void OpenCastle(ulong npcId)
    {
        _npcOpen = false;
        _castleWindow.Open(npcId);
    }

    private void AskWithdrawal(long maximum, Action<int> accepted) =>
        _countBox.Open(ClientText.Get("Castle.HowMuch"), ClientText.Get("Castle.ToWithdraw"), maximum, accepted);

    private bool ClickCastle(Point pointer) => _castleWindow.Click(pointer);

    private bool CapturesCastle(Point pointer) => _castleWindow.Contains(pointer);
}
