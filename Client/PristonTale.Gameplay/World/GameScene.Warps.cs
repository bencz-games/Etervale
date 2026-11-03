using System.Threading.Channels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Engine3D;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.World;

// Gates, warps and the server's placements of the local player.
public sealed partial class GameScene
{
    private static bool TryGateSeam(FieldDef a, FieldDef b, out Vector2 seam)
    {
        foreach (var gate in a.Gates)
        {
            if (gate.DestinationFieldId == b.Id)
            {
                seam = GatePlanar(gate);
                return true;
            }
        }

        foreach (var gate in b.Gates)
        {
            if (gate.DestinationFieldId == a.Id)
            {
                seam = GatePlanar(gate);
                return true;
            }
        }

        seam = default;
        return false;
    }

    private static Vector2 GatePlanar(FieldGate gate) => WorldSpace.Planar(gate.X, gate.Z);

    private void UpdateWarps(float dtSeconds)
    {
        if (_pendingWarp is not null)
        {
            HoldAtSpecialWarp(_pendingWarp);
            _pendingWarpDelay = MathF.Max(0f, _pendingWarpDelay - dtSeconds);
            if (_pendingWarpDelay <= 0f && Warp(_pendingWarp))
            {
                _pendingWarp = null;
                _warpCooldown = WarpCooldownSeconds;
                _warpSparkle.StopNow();
            }

            return;
        }

        UpdateWingStone();
        if (_warpCooldown > 0f)
        {
            _warpCooldown -= dtSeconds;
            return;
        }

        if (_activeField == null || _playerBody == null)
        {
            return;
        }

        foreach (var warp in _activeField.Definition.WarpGates)
        {
            if (WingGates.IsStone(warp) || warp.DestinationFieldId == _activeField.Definition.Id)
            {
                continue;
            }

            if (PlayerLevel < warp.LimitLevel)
            {
                continue;
            }

            if (!_worldCatalog.TryGetField(warp.DestinationFieldId, out _))
            {
                continue;
            }

            if (OnGate(warp))
            {
                if (warp.SpecialEffect == WingGates.DelayedEffect)
                {
                    BeginSpecialWarp(warp);
                    return;
                }

                if (Warp(warp))
                {
                    _warpCooldown = WarpCooldownSeconds;
                }

                return;
            }
        }
    }

    // The player's feet stand within the gate's width and, for a gate with a height, within its height.
    private bool OnGate(FieldWarpGate warp)
    {
        var center = WorldSpace.Planar(warp.X, warp.Z);
        var radius = warp.Radius;
        var playerFeet = _playerBody.Height + GroundTraversal.VisualOffset;
        var withinHeight = warp.Y == 0 || MathF.Abs(playerFeet - warp.Y) < warp.Height;
        return withinHeight && Vector2.DistanceSquared(_playerBody.PlanarPosition, center) <= radius * radius;
    }

    private void BeginSpecialWarp(FieldWarpGate warp)
    {
        _pendingWarp = warp;
        _pendingWarpDelay = SpecialWarpDelaySeconds;
        EndFall(landed: false);
        ClearAttackTarget();
        _lootInteraction?.Cancel();
        StopPredictedMovement();
        HoldAtSpecialWarp(warp);
        _reports.Request();
        _warpSparkle = PlayReturn(_playerBody.Position);
        _fieldAudio?.PlayPortalWarp();
        RequestField(_worldCatalog.GetField(warp.DestinationFieldId));
    }

    private void HoldAtSpecialWarp(FieldWarpGate warp)
    {
        _playerBody.PlanarPosition = WorldSpace.Planar(warp.X, warp.Z);
        _playerBody.Height = warp.Y - GroundTraversal.VisualOffset;
        ApplyPlayerWorld();
    }

    private bool Warp(FieldWarpGate warp)
    {
        var destDef = _worldCatalog.GetField(warp.DestinationFieldId);
        var dest = FindLoaded(destDef.Id);
        if (dest is null)
        {
            RequestField(destDef);
            return false;
        }

        for (var i = _fields.Count - 1; i >= 0; i--)
        {
            if (_fields[i] != dest)
            {
                UnloadField(_fields[i]);
            }
        }

        SetActiveField(dest);
        _hasMoveTarget = false;
        // The client picks the exit of a gate with several; the server keeps the exit the report arrives at.
        var exit = warp.Exit(Random.Shared.Next(warp.ExitCount));
        _playerBody.PlanarPosition = WorldSpace.Planar(exit.X, exit.Z);
        // The authored destination height is near the floor, not on it: stand on the floor nearest it.
        _playerBody.Height = exit.Y - GroundTraversal.VisualOffset;
        PlaceAtHeight(_playerBody, exit.Y);
        EndFall(landed: false);
        ApplyPlayerWorld();
        _camera = new PristonCamera(_playerBody.Position, _playerForward, ToCameraSettings(destDef.Camera));
        _log.Info($"[Warp] -> {destDef.Id} at ({exit.X},{exit.Z}).");
        return true;
    }

    private void ApplyWarp(WarpInfo warp)
    {
        // The unforced warp that confirms a field change can arrive while a forced one is held; it keeps the force.
        if (_pendingFieldWarp is { Forced: true } held && held.FieldId == warp.FieldId)
        {
            warp = warp with { Forced = true };
        }

        _pendingFieldWarp = null;
        _serverPlanarPosition = new Vector2(warp.Position.X, warp.Position.Z);
        if (_playerBody is null)
        {
            return;
        }

        var changedField = false;
        if (_activeField?.Definition.FieldCode != warp.FieldId && _worldCatalog.TryGetFieldByCode(warp.FieldId, out var field))
        {
            var loaded = FindLoaded(field.Id);
            if (loaded is null)
            {
                // Held until the field is loaded; the player stays put meanwhile.
                _pendingFieldWarp = warp;
                _hasMoveTarget = false;
                _followCursor = false;
                RequestField(field);
                return;
            }

            SetActiveField(loaded);
            _reports.SetField(field.FieldCode);
            _hasMoveTarget = false;
            changedField = true;
        }

        var authoritative = new Vector2(warp.Position.X, warp.Position.Z);
        var difference = authoritative - _playerBody.PlanarPosition;
        if (!warp.Forced && changedField && difference.LengthSquared() <= WarpCorrection * WarpCorrection)
        {
            // The unforced warp confirming a field the player is already at (a restart moved it across, a seamless border):
            // only the height, which may still be the old field's; snapping the place would pull back a player who has
            // walked on.
            PlaceAtHeight(_playerBody, warp.Position.Y);
            EndFall(landed: false);
            ApplyPlayerWorld();
            return;
        }

        if (warp.Forced || difference.LengthSquared() > WarpCorrection * WarpCorrection)
        {
            _log.Info($"[Movement] correction {difference.Length():F1} units; field={warp.FieldId}, forced={warp.Forced}.");
            StopPredictedMovement();
            _playerBody.PlanarPosition = authoritative;
            _positionCorrection = Vector2.Zero;
            PlaceAtHeight(_playerBody, warp.Position.Y);
            EndFall(landed: false);
            ApplyPlayerWorld();
            _camera?.SnapFollow(_playerBody.Position, _fieldCollision);
            _reports.Request();
        }
    }

    private void ApplyPositionCorrection(float dt)
    {
        if (_playerBody is null || _positionCorrection.LengthSquared() <= 0.0001f)
        {
            return;
        }

        var step = _positionCorrection * (1f - MathF.Exp(-12f * dt));
        _playerBody.PlanarPosition += step;
        _positionCorrection -= step;
        TrackBodyHeight(_playerBody);
        ApplyPlayerWorld();
    }

    private void RespawnAt(NumVector3 worldPosition)
    {
        _hasMoveTarget = false;
        _playerBody.PlanarPosition = new Vector2(worldPosition.X, worldPosition.Z);
        UpdateBodyHeight(_playerBody);
        EndFall(landed: false);
        ApplyPlayerWorld();
        _camera?.SnapFollow(_playerBody.Position, _fieldCollision);
        ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.Restart));
    }
}
