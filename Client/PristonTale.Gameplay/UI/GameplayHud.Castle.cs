using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Gameplay.UI;

// Bless Castle on the HUD: the server's castle state, the siege board while a siege runs in the castle field, and at a
// siege's end a notice to the players there that their clan holds the castle or that the siege is over.
public sealed partial class GameplayHud
{
    private readonly HudSiegeBoard _siegeBoard = new();

    public CastleInfo Castle => _siegeBoard.Castle;

    private bool HoldsCastle => CastleSiegeRules.HoldsCastle(Combat.OwnClan, _siegeBoard.Castle.OwnerClanId);

    public void ObserveCastle(PresenceUpdate update)
    {
        if (update.Castle is not { } castle)
        {
            return;
        }

        var ended = _siegeBoard.Castle.Siege == SiegeState.Running && castle.Siege != SiegeState.Running;
        _siegeBoard.Apply(castle, _clock.Elapsed.TotalMilliseconds);
        _wingGate.CastleOwner = HoldsCastle;
        if (ended && InCastleField)
        {
            _notice.Show(ClientText.Get(HoldsCastle ? "Siege.Won" : "Siege.Ended"));
        }
    }
}
