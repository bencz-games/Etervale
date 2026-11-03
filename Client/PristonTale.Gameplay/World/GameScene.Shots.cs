using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.World;

// The basic shots of every drawn player's bow, javelin or staff (PlayerShots): a swing names the target, the attack
// clip's first event throws the shot, and a javelin in flight is not drawn in its thrower's hand.
public sealed partial class GameScene
{
    private readonly PlayerShots _shots = new();
    private Action<ulong, PlayerActorHandles, PlayerAppearanceInfo, Vector3> _shootRemote;
    private Func<ulong, Vector3?> _shotTarget;

    private void ObservePlayerSwings(PresenceUpdate update)
    {
        foreach (var combatEvent in update.Events)
        {
            if (combatEvent.Kind == CombatEventKind.SwingBegin)
            {
                _shots.ObserveSwing(combatEvent.AttackerId, combatEvent.TargetId);
            }
        }
    }

    private void UpdateShots(float seconds)
    {
        _shotTarget ??= ShotTarget;
        _shots.Content = _effectLibrary?.Vfx;
        if (_equipmentPresentation is not null && _playerBody is not null && !_playerController.IsDead)
        {
            Shoot(_remote.SelfEntityId, _equipmentPresentation.Handles, _equipmentPresentation.WeaponKey, _ownWeaponGlow,
                _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset, _player is not null && ClassProfiles.CastsWithStaff(_player.Class.Class));
        }

        _remote.ForEachPlayer(_shootRemote ??= (entityId, handles, appearance, position) =>
            Shoot(entityId, handles, appearance.WeaponKey, appearance.WeaponGlow, position, ClassProfiles.CastsWithStaff(appearance.Class)));
        _shots.Update(seconds, _shotTarget);
    }

    private void Shoot(ulong entityId, PlayerActorHandles handles, string weaponKey, in HeldItemGlow glow, Vector3 position, bool staffCaster)
    {
        if (_shots.Throwing(entityId))
        {
            _looks.PaintHidden(handles.Weapon);
        }

        if (!_renderer.TryGetActorMotion(handles.Body, out var motion, out var ticks) || motion is null)
        {
            return;
        }

        var weapon = handles.Weapon.IsValid && _renderer.TryGetActorWorld(handles.Weapon, out var world) ? world : Matrix.CreateTranslation(position + Vector3.Up * 28f);
        _shots.Hold(_skillFx, entityId, motion, ticks, weaponKey ?? "", weapon, glow, position, staffCaster, _shotTarget);
    }

    // Shots aim at the local player's drawn feet, which sit the visual offset above its body.
    private Vector3? ShotTarget(ulong entityId) =>
        TryGetDrawnPosition(entityId, out var position) ? position + (IsSelf(entityId) ? Vector3.Up * GroundTraversal.VisualOffset : Vector3.Zero) : null;
}
