using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Physics;

namespace PristonTale.Gameplay.World;

// Item use from the HUD, the premium timers the server sends and the item effects played around players.
public sealed partial class GameScene
{
    private ItemUseEffects _itemEffects;
    // The anchors the item effects follow, as delegates made once instead of every frame.
    private Func<ulong, Vector3?> _itemAnchor;
    private Func<ulong, (Vector3 Position, float Facing)?> _petOwnerPose;
    private PremiumPetPresentation _pets;
    private CrownPresentation _crowns;
    private Action<ulong, PlayerActorHandles, PristonTale.Client.Net.Presence.PlayerAppearanceInfo, Vector3> _crownVisit;

    // The clan holding Bless Castle (0 for none): its players wear the crown.
    public long CastleOwnerClanId { get; set; }
    private ForceOrbPresentation _forceOrbs;

    // The local player's running premium timers as last sent by the server.
    public PremiumTimerSnapshot PremiumTimers { get; private set; }

    public Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command)
    {
        return _playChannel is not null ? _playChannel.UseItemAsync(command, _presenceCts.Token)
            : Task.FromResult(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.Rejected));
    }

    // The return sparkle (ItemVfx.Return) over feet at a point: ether cores, crystal summons, warps and wing gates.
    private PristonTale.Effects.Vfx.VfxHandle PlayReturn(Vector3 feet) =>
        _skillFx?.SpawnAt(ItemVfx.Return, feet + Vector3.Up * GroundTraversal.VisualOffset, Matrix.Identity) ?? default;

    // Loads the item effects with the scene's effects, at the field load that creates the renderer.
    private void CreateItemEffects()
    {
        if (_skillFx is not null && _itemEffects is null)
        {
            ItemVfx.Register(_skillFx.Library);
            _itemEffects = new ItemUseEffects(_skillFx, _renderer?.TransientLights, _skillSounds);
        }

        var scripts = _skillFx is null ? null : new SystemScriptEffects(_skillFx.System, _skillFx.Library);
        if (_forceOrbs is null && _skillFx is not null)
        {
            _forceOrbs = new ForceOrbPresentation(_skillFx, scripts, _renderer?.TransientLights);
        }

        if (_hits is null && _skillFx is not null)
        {
            _hits = new HitEffects(_skillFx);
        }

        if (_pets is null && _renderer is not null)
        {
            _pets = new PremiumPetPresentation(_actorScene, _actorLoader, _assetRoot ?? "", scripts, _renderer.TransientLights, _skillSounds, _log);
        }

        if (_crowns is null && _renderer is not null)
        {
            _crowns = new CrownPresentation(_actorScene, _actorLoader, _assetRoot ?? "", _log);
        }
    }

    private void ObserveItems(PresenceUpdate update)
    {
        if (update.PremiumTimers is { } timers)
        {
            PremiumTimers = timers;
        }

        _pets?.Observe(update, _itemAnchor ??= ItemAnchor);
        ObserveOwnHead(update);
        _forceOrbs?.Observe(update, _itemAnchor ??= ItemAnchor, _skillClockMs);

        foreach (var effect in update.ItemEffects)
        {
            if (ItemAnchor(effect.Entity) is not { } position)
            {
                continue;
            }

            if (effect.Effect == ItemEffectKind.Mastery)
            {
                _skillFx?.Spawn(ItemVfx.Mastery, effect.Entity, effect.Entity, position);
            }
            else if (effect.Effect is ItemEffectKind.Learn or ItemEffectKind.ForceOrb or ItemEffectKind.HeadChange)
            {
                _skillFx?.Spawn(ItemVfx.Learn, effect.Entity, effect.Entity, position);
            }
            else if (effect.Effect == ItemEffectKind.AgingUp)
            {
                _skillFx?.Spawn(ItemVfx.AgingUp, effect.Entity, effect.Entity, position);
            }
            else if (effect.Effect == ItemEffectKind.Return)
            {
                // The sparkle stands where the server placed it: the user, the called monster or the revived player.
                PlayReturn(effect.Position);
            }

            _itemEffects?.Play(effect.Effect, effect.Entity, effect.Variant, position, PlayerPosition, _skillClockMs);
        }
    }

    // A hair tint or a big-head potion changes the local player's head when the server describes the player again.
    private void ObserveOwnHead(PresenceUpdate update)
    {
        ObserveOwnGlow(update);
        if (_remote is null || _equipmentPresentation is null || _player is null)
        {
            return;
        }

        foreach (var spawn in update.Spawns)
        {
            if (spawn.EntityId != _remote.SelfEntityId || spawn.Player is not { } appearance)
            {
                continue;
            }

            var playerClass = _player.Class.Class;
            var head = CharacterHead.Apply(_player.Definition.DefaultVisuals, playerClass, appearance.Tier, appearance.FaceIndex, appearance.HairIndex);
            _equipmentPresentation.SetBaseVisuals(CharacterHead.ApplyBigHead(head, playerClass, appearance.BigHead));
        }
    }

    private void UpdateItemEffects(float dt)
    {
        _itemAnchor ??= ItemAnchor;
        _petOwnerPose ??= PetOwnerPose;
        _itemEffects?.Update(_skillClockMs, _itemAnchor, PlayerPosition);
        _pets?.Update(dt, _petOwnerPose, _itemAnchor, PlayerPosition);
        _forceOrbs?.Update(_skillClockMs, _itemAnchor);
        ShowCrowns();
    }

    private void ShowCrowns()
    {
        if (_crowns is null)
        {
            return;
        }

        if (CastleOwnerClanId != 0 && _remote is not null)
        {
            if (!PlayerDead && Simulation.Social.Castle.CastleSiegeRules.HoldsCastle(OwnClanId, CastleOwnerClanId))
            {
                _crowns.Show(_remote.SelfEntityId, CrownHead(_remote.SelfEntityId, PlayerPosition), PlayerPosition, _skillClockMs);
            }

            _remote.ForEachPlayer(_crownVisit ??= (entityId, _, appearance, feet) =>
            {
                if (Simulation.Social.Castle.CastleSiegeRules.HoldsCastle(appearance.ClanId, CastleOwnerClanId))
                {
                    _crowns.Show(entityId, CrownHead(entityId, feet), PlayerPosition, _skillClockMs);
                }
            });
        }

        _crowns.Finish();
    }

    private Vector3 CrownHead(ulong entityId, Vector3 feet) =>
        ((PristonTale.Effects.Vfx.IVfxAnchors)this).TryGetBone(entityId, UI.Widgets.HudTargetPortrait.HeadBone, out var head)
            ? head
            : feet + Vector3.Up * CrownPresentation.HeadHeight;

    private (Vector3 Position, float Facing)? PetOwnerPose(ulong entityId) =>
        TryGetDrawnPosition(entityId, out var position) && TryGetDrawnFacing(entityId, out var facing) ? (position, facing) : null;

    private Vector3? ItemAnchor(ulong entityId) => TryGetDrawnPosition(entityId, out var position) ? position : null;

    // Where an entity is drawn: the local player's body (its collision position) or a remote entity's drawn position.
    private bool TryGetDrawnPosition(ulong entityId, out Vector3 position)
    {
        if (IsSelf(entityId))
        {
            position = _playerBody.Position;
            return true;
        }

        position = default;
        return _remote is not null && _remote.TryGetDrawnPosition(entityId, out position);
    }

    private bool TryGetDrawnFacing(ulong entityId, out float facing)
    {
        if (IsSelf(entityId))
        {
            facing = PlayerYaw;
            return true;
        }

        facing = 0f;
        return _remote is not null && _remote.TryGetDrawnFacing(entityId, out facing);
    }

    private bool IsSelf(ulong entityId) => _remote is not null && entityId == _remote.SelfEntityId && _playerBody is not null;
}
