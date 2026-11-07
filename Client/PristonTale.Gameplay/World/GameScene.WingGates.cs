using Microsoft.Xna.Framework;
using PristonTale.Simulation.World;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World;

// The wing warp stones (WingGates): stepping on one holds the player on it and opens the wing warp map; walking off it
// closes the map, and the map opens again only after the player left the stone. As in the original, a chosen trip shows
// the return sparkle and plays the warp sound on the stone, and the trip itself leaves two seconds later.
public sealed partial class GameScene
{
    private const int WingTripDelayMs = 2000;
    private FieldWarpGate _wingStone;

    // The player stepped on a stone of this field; the screen opens the wing warp map.
    public event Action<int> WingGateEntered;

    // The player left the stone; the screen closes the map.
    public event Action WingGateLeft;

    public int FieldLimitLevel(int fieldCode) => _worldCatalog is not null && _worldCatalog.TryGetFieldByCode(fieldCode, out var field) ? field.LimitLevel : 0;

    // The channel and token are taken before the delay: its continuation runs on the pool, where a teardown in between
    // could have cleared or disposed them.
    public async Task<WingGateOutcome> TravelWingGateAsync(int destination)
    {
        var channel = _playChannel;
        if (channel is null)
        {
            return new WingGateOutcome(WingGateStatus.Rejected, destination);
        }

        var token = _presenceCts.Token;

        if (_playerBody is not null)
        {
            PlayReturn(_playerBody.Position);
            _fieldAudio?.PlayPortalWarp();
        }

        await Task.Delay(WingTripDelayMs, token);
        return await channel.TravelWingGateAsync(destination, token);
    }

    private void UpdateWingStone()
    {
        if (_activeField is null || _playerBody is null)
        {
            return;
        }

        FieldWarpGate standing = null;
        foreach (var warp in _activeField.Definition.WarpGates)
        {
            if (WingGates.IsStone(warp) && OnGate(warp))
            {
                standing = warp;
                break;
            }
        }

        if (standing is null)
        {
            if (_wingStone is not null)
            {
                _wingStone = null;
                WingGateLeft?.Invoke();
            }

            return;
        }

        if (_wingStone is not null || PlayerDead || PlayerLevel < standing.LimitLevel)
        {
            return;
        }

        _wingStone = standing;
        ClearAttackTarget();
        _lootInteraction?.Cancel();
        StopPredictedMovement();
        HoldAtSpecialWarp(standing);
        _reports.Request();
        WingGateEntered?.Invoke(_activeField.Definition.FieldCode);
    }
}
