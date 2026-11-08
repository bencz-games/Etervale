using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Gameplay.World;

// The bursts of players' basic hits and the blood of players hit (HitEffects), players told apart by SwingOf.
public sealed partial class GameScene
{
    private HitEffects _hits;
    private Func<ulong, string> _hitWeapon;

    private void ObserveHits(PresenceUpdate update)
    {
        if (_hits is null || _remote is null || _playerBody is null || _player is null)
        {
            return;
        }

        _hits.Observe(update, _hitWeapon ??= HitWeapon, _itemAnchor ??= ItemAnchor);
    }

    private string HitWeapon(ulong entityId) => SwingOf(entityId)?.WeaponKey;
}
