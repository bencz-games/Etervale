using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.World.Remote;

public sealed class PlayerEquipmentPresentation
{
    private readonly PlayerActorComposer _playerComposer;
    private readonly IActorScene _scene;
    private readonly IClientLog _log;
    private readonly string _assetRoot;
    private CharacterVisualSet _basePlayerVisuals;
    private readonly uint _jobMask;
    private PlayerActorHandles _playerHandles;
    private CharacterVisualSet _requestedPlayerVisuals;
    private Task _appearancePreparation;
    private long _appearanceInventoryRevision = -1;
    private bool _appearanceVillage;
    public PlayerActorHandles Handles => _playerHandles;

    // The body item worn now ("" for none).
    public string ArmorKey { get; private set; } = "";

    public string WeaponKey => (_requestedPlayerVisuals ?? _basePlayerVisuals)?.WeaponKey ?? "";

    public bool HoldsShield => ItemEquipmentMetadata.IsShield((_requestedPlayerVisuals ?? _basePlayerVisuals)?.OffHandKey);

    public PlayerEquipmentPresentation(PlayerActorComposer composer, IActorScene scene, IClientLog log, string root,
        CharacterVisualSet visuals, uint jobMask, PlayerActorHandles handles)
    {
        _playerComposer = composer;
        _scene = scene;
        _log = log;
        _assetRoot = root;
        _basePlayerVisuals = visuals;
        _jobMask = jobMask;
        _playerHandles = handles;
    }

    // A new head (hair tint, big head) re-composes the player on the next update.
    public void SetBaseVisuals(CharacterVisualSet visuals)
    {
        if (visuals is null || visuals == _basePlayerVisuals)
        {
            return;
        }

        _basePlayerVisuals = visuals;
        _appearanceInventoryRevision = -1;
    }

    public void Stop()
    {
        if (_appearancePreparation is not null)
        {
            _ = ObserveAppearancePreparationAsync(_appearancePreparation);
            _appearancePreparation = null;
        }
    }

    private async Task ObserveAppearancePreparationAsync(Task preparation)
    {
        try
        {
            await preparation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log.Error($"[Equipment] Superseded preparation failed: {exception.Message}");
        }
    }

    public bool Update(OwnerState owner, bool village, Matrix world, MotionCode motion)
    {
        if (owner is null || _basePlayerVisuals is null || _playerComposer is null)
        {
            return false;
        }

        if (_appearanceInventoryRevision != owner.InventoryRevision || village != _appearanceVillage)
        {
            _appearanceInventoryRevision = owner.InventoryRevision;
            _appearanceVillage = village;
            string weapon = "";
            string armor = "";
            string offHand = "";
            foreach (var item in owner.Items)
            {
                switch ((EquipmentSlot)item.Slot)
                {
                    case EquipmentSlot.Weapon:
                        weapon = item.CatalogKey;
                        break;
                    case EquipmentSlot.Armor:
                        armor = item.CatalogKey;
                        break;
                    case EquipmentSlot.OffHand:
                        offHand = item.CatalogKey;
                        break;
                }
            }

            ArmorKey = armor;
            var desired = EquipmentVisuals.Apply(_basePlayerVisuals, armor, weapon, offHand, village, _jobMask);
            if (desired != _requestedPlayerVisuals)
            {
                _requestedPlayerVisuals = desired;
                if (_appearancePreparation is not null)
                {
                    _ = ObserveAppearancePreparationAsync(_appearancePreparation);
                }

                _appearancePreparation = _playerComposer.PrepareAsync(_assetRoot, desired, desired.FaceIndex);
            }
        }

        if (_appearancePreparation is null || !_appearancePreparation.IsCompleted)
        {
            return false;
        }

        var completed = _appearancePreparation;
        _appearancePreparation = null;
        if (!completed.IsCompletedSuccessfully)
        {
            _log.Warn($"[Equipment] Appearance preparation failed: {completed.Exception?.GetBaseException().Message}");
            return false;
        }

        PlayerActorHandles handles;
        try
        {
            handles = _playerComposer.Compose(_assetRoot, _requestedPlayerVisuals, _requestedPlayerVisuals.FaceIndex, world, motion);
        }
        catch (Exception exception)
        {
            _log.Error($"[Equipment] Appearance publication failed: {exception.Message}");
            return false;
        }

        try
        {
            _scene.CopyActorPlayback(_playerHandles.Body, handles.Body);
            _scene.CopyActorPlayback(_playerHandles.Head, handles.Head);
        }
        catch (Exception exception)
        {
            handles.Remove(_scene);
            _log.Error($"[Equipment] Appearance playback transfer failed: {exception.Message}");
            return false;
        }

        _playerHandles.Remove(_scene);
        _playerHandles = handles;
        return true;
    }
}
