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

// The local player's input: moving, picking loot and targets, approaching NPCs and the attack it shows.
public sealed partial class GameScene
{
    private void UpdatePlayerInput(GameTime gameTime)
    {
        if (_renderer == null || _activeField == null || _camera == null || _playerBody == null)
        {
            return;
        }

        var mouse = Mouse.GetState();
        if (_pendingWarp is not null)
        {
            _lastMouseState = mouse;
            return;
        }

        if (_playerController.IsDead)
        {
            _lastMouseState = mouse;
            return;
        }

        if (MovementLocked && (_hasMoveTarget || _followCursor))
        {
            _followCursor = false;
            StopPredictedMovement();
            _reports.Request();
        }

        if (!WorldClicksBlocked && RightAttackEnabled && mouse.RightButton == ButtonState.Pressed
            && _lastMouseState.RightButton == ButtonState.Released && TryPickAttackTarget(mouse, rightButton: true))
        {
            _hasMoveTarget = false;
        }
        else if (!WorldClicksBlocked && !RightAttackEnabled && mouse.RightButton == ButtonState.Pressed
            && _lastMouseState.RightButton == ButtonState.Released && !SelfCasting)
        {
            TryCastSkillClick(mouse, rightButton: true);
        }

        UpdateRunMode(mouse);
        if (!WorldClicksBlocked && mouse.LeftButton == ButtonState.Pressed && _lastMouseState.LeftButton == ButtonState.Released)
        {
            var nowMs = gameTime.TotalGameTime.TotalMilliseconds;
            var doubleClick = (nowMs - _lastLeftClickMs) <= DoubleClickMs;
            _lastLeftClickMs = nowMs;
            _followCursor = false;
            if (TryPickLoot(mouse))
            {
            }
            else if (!SelfCasting && TryCastSkillClick(mouse, rightButton: false))
            {
            }
            else if (TryPickAttackTarget(mouse))
            {
                _hasMoveTarget = false;
            }
            else if (SetMoveTarget(mouse))
            {
                // A ground click during a skill motion is kept: the walk starts once the skill ends.
                CancelPendingCast();
                ClearAttackTarget();
                _lootInteraction.Cancel();
                _followCursor = doubleClick;
                _reports.Request();
            }
        }

        if (_attackInput.Release(mouse.LeftButton == ButtonState.Pressed, mouse.RightButton == ButtonState.Pressed) is { } release)
        {
            SendAttackIntent(release);
        }

        UpdateCharge(mouse);
        EndSkillMotion();
        UpdatePendingCast();
        UpdateNpcApproach();
        UpdateReachedPlayer();
        _lootInteraction?.Update(_groundLoot, _playerBody.Position, _serverPlanarPosition, Owner);
        UpdateAttackPresentation(gameTime);
        if (_followCursor && !WorldClicksBlocked && !SelfCasting)
        {
            SetMoveTarget(mouse);
        }

        if (_hasMoveTarget && !_playerController.LocomotionFrozen && !_selfAttacking && !SelfCasting && !SelfStunned)
        {
            var current = _playerBody.PlanarPosition;
            var running = _runMode;

            var step = DestinationMovement.Advance(
                new System.Numerics.Vector2(current.X, current.Y),
                new System.Numerics.Vector2(_moveTarget.X, _moveTarget.Y),
                MovementSpeed(running),
                (float)gameTime.ElapsedGameTime.TotalSeconds);
            var proposed = new Vector2(step.Position.X, step.Position.Y);
            var slid = MoveWithCollision(current, proposed - current, _playerBody.Height + BodyProbeHeight);
            CommitGroundMovement(ResolveEntityCollision(current, slid));
            // Following the cursor ends where the player is blocked, as in the original.
            if (_followCursor && proposed != current && Vector2.DistanceSquared(_playerBody.PlanarPosition, current) < 1e-4f)
            {
                _followCursor = false;
            }

            if (step.Direction != System.Numerics.Vector2.Zero)
            {
                _playerForward = new Vector3(step.Direction.X, 0f, step.Direction.Y);
            }

            ApplyPlayerWorld();
            var arrived = DestinationMovement.HasArrived(
                new System.Numerics.Vector2(_playerBody.PlanarPosition.X, _playerBody.PlanarPosition.Y),
                new System.Numerics.Vector2(_moveTarget.X, _moveTarget.Y));
            if (arrived)
            {
                _hasMoveTarget = false;
                _positionCorrection = Vector2.Zero;
                ApplyPlayerCommand(_playerController.ApplyLocomotion(MotionCode.Stand));
            }
            else
            {
                var motion = running ? MotionCode.Run : MotionCode.Walk;
                ApplyPlayerCommand(_playerController.ApplyLocomotion(motion));
            }
        }

        _lastMouseState = mouse;
    }

    // The floor query takes the highest surface at or below the reference plus a step, so the reference is the drop's
    // own height; the player's height is only the fallback for a field the server holds no geometry for.
    private bool LootFloorAt(float x, float referenceHeight, float z, out float height) =>
        TryGetFloorHeightNear(new Vector3(x, 0, z), referenceHeight, out height)
        || TryGetFloorHeightNear(new Vector3(x, 0, z), _playerBody.Height + GroundTraversal.VisualOffset, out height);

    private bool TryPickLoot(MouseState mouse)
    {
        if (_groundLoot is null || _lootInteraction.Busy || !TryCursorRay(mouse, out var ray)
            || !_groundLoot.Pick(mouse.Position, ray, out var id, out var position, ShowAllDropLabels))
        {
            return false;
        }

        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        if (!GroundTraversal.CanReachSurface(feet, position, _groundFloorQuery))
        {
            return true;
        }

        ClearAttackTarget();
        _npcTargetId = 0;
        _lootInteraction.Select(id);
        _moveTarget = new Vector2(position.X, position.Z);
        _hasMoveTarget = true;
        _reports.Request();
        return true;
    }

    private bool SetMoveTarget(MouseState mouse)
    {
        if (!TryCursorRay(mouse, out var ray))
        {
            return false;
        }

        if (!TryRaycastFloor(ray, out var point))
        {
            return false;
        }

        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        if (!GroundTraversal.CanReachSurface(feet, point, _groundFloorQuery))
        {
            return false;
        }

        _moveTarget = new Vector2(point.X, point.Z);
        _hasMoveTarget = true;
        return true;
    }

    private bool TryCursorRay(MouseState mouse, out Ray ray)
    {
        return CursorPicking.TryCreateRay(
            _graphicsDevice.Viewport,
            _camera.Projection,
            _camera.View,
            _camera.Position,
            mouse.Position,
            out ray);
    }

    private bool TryPickAttackTarget(MouseState mouse, bool rightButton = false)
    {
        if (_remote == null)
        {
            return false;
        }
        if (!TryCursorRay(mouse, out var ray))
        {
            return false;
        }
        if (!_remote.TryPick(ray, _remote.SelfEntityId, out var entityId, out var kind))
        {
            return false;
        }
        // A seller is approached and its shop opened as an NPC's dialog is; another player is approached for the buttons
        // under its portrait.
        if (kind is EntityKind.Npc or EntityKind.Player)
        {
            _lootInteraction.Cancel();
            ClearAttackTarget();
            _npcTargetId = entityId;
            _approach = kind == EntityKind.Npc ? ApproachKind.Npc : _remote.ShopSigns.ContainsKey(entityId) ? ApproachKind.Seller : ApproachKind.Player;
            _npcMoveSent = false;
            _targetSelection.Trace(entityId);
            StopPredictedMovement();
            return true;
        }
        if (kind != EntityKind.Monster)
        {
            return false;
        }

        _npcTargetId = 0;
        _lootInteraction.Cancel();
        _targetSelection.Trace(entityId);

        var kb = Keyboard.GetState();
        var automatic = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        SendAttackIntent(_attackInput.Press(entityId, automatic, rightButton));
        return true;
    }

    private void SendAttackIntent(AttackRequest request)
    {
        if (_playChannel is null)
        {
            return;
        }

        SendUpstream(_playChannel.SendAttackAsync(request.TargetId, request.Sustained, _presenceCts.Token, request.Sequence), "attack");
    }

    // Like the original client's attack cancel, this drops the trace but keeps the target frame open; the server stops
    // chasing the target too.
    private void ClearAttackTarget()
    {
        if (_attackInput.Stop() is { } stop)
        {
            SendAttackIntent(stop);
        }

        _targetSelection.ClearTrace();
        _npcTargetId = 0;
        _npcMoveSent = false;
    }

    // Hover picking runs in Update so Draw stays free of scene queries.
    private void UpdateTargetSelection(GameTime gameTime)
    {
        ulong hovered = 0;
        var hoveredKind = EntityKind.None;
        if (_remote is not null && _camera is not null && !PointerCaptured && CameraInputEnabled && TryCursorRay(Mouse.GetState(), out var ray)
            && _remote.TryPick(ray, _remote.SelfEntityId, out var entityId, out var kind))
        {
            hovered = entityId;
            hoveredKind = kind;
        }

        _targetSelection.Hover(hovered);
        UpdateOutline(hovered, hoveredKind);
        _targetSelection.Update(gameTime.TotalGameTime.TotalMilliseconds, _targetLiveness);
        UpdateHoveredDrop();
    }

    // The drop under the cursor, by its label or its model: it names the label to highlight and turns the pointer into
    // the original's grabbing hand.
    private void UpdateHoveredDrop()
    {
        var mouse = Mouse.GetState();
        HoveredDropId = Guid.Empty;
        if (_groundLoot is not null && _camera is not null && !PointerCaptured && CameraInputEnabled && TryCursorRay(mouse, out var ray)
            && _groundLoot.Pick(mouse.Position, ray, out var id, out _, ShowAllDropLabels))
        {
            HoveredDropId = id;
        }
    }

    // Holding A names every drop in view, as in the original.
    private bool ShowAllDropLabels => !PointerCaptured && !KeyboardCaptured && Keyboard.GetState().IsKeyDown(Keys.A);

    private TargetLiveness QueryTargetLiveness(ulong entityId)
    {
        if (_remote is null || !_remote.TryGetTarget(entityId, out _, out var dead))
        {
            return TargetLiveness.Missing;
        }

        return dead ? TargetLiveness.Dead : TargetLiveness.Alive;
    }

    // During a skill motion the walk is dropped but the skill clip keeps playing.
    private void StopPredictedMovement()
    {
        _hasMoveTarget = false;
        _positionCorrection = Vector2.Zero;
        if (!SelfCasting)
        {
            ApplyPlayerCommand(_playerController.ApplyLocomotion(MotionCode.Stand));
        }
    }

    private void UpdateNpcApproach()
    {
        if (_npcTargetId == 0 || _remote is null || SelfCasting)
        {
            return;
        }

        if (_playerController.IsDead || !_remote.TryGetTarget(_npcTargetId, out var position, out _))
        {
            ClearAttackTarget();
            StopPredictedMovement();
            return;
        }

        var target = new Vector2(position.X, position.Z);
        var decision = NpcApproach.Evaluate(
            _playerBody.PlanarPosition,
            _serverPlanarPosition,
            target,
            _npcMoveSent);
        if (decision.StopLocally)
        {
            StopPredictedMovement();
        }

        if (decision.SendStop)
        {
            _npcMoveSent = true;
            _reports.Request();
        }

        if (decision.OpenDialog)
        {
            var entityId = _npcTargetId;
            ClearAttackTarget();
            switch (_approach)
            {
                case ApproachKind.Npc:
                    NpcSelected?.Invoke(entityId);
                    break;
                case ApproachKind.Seller when _remote.ShopSigns.ContainsKey(entityId):
                    ShopSellerSelected?.Invoke(entityId);
                    break;
                case ApproachKind.Player:
                    ReachedPlayerId = entityId;
                    break;
            }

            return;
        }

        if (decision.BeginApproach)
        {
            _moveTarget = decision.Destination;
            _hasMoveTarget = true;
            _npcMoveSent = true;
            _reports.Request();
        }
    }

    private void UpdateAttackPresentation(GameTime gameTime)
    {
        if (_attackInput.TargetId == 0 || _remote == null)
        {
            return;
        }

        // A skill motion owns the avatar: a target picked during it is kept, and the approach and swing start once it ends.
        if (_playerController.LocomotionFrozen || _selfAttacking || SelfStunned || SelfCasting)
        {
            return;
        }

        if (!_remote.TryGetTarget(_attackInput.TargetId, out var targetPos, out var targetDead) || targetDead)
        {
            ClearAttackTarget();
            if (_playerController.Current == MotionCode.Attack)
            {
                ApplyPlayerCommand(_playerController.ApplyLocomotion(MotionCode.Stand));
            }

            return;
        }

        var current = _playerBody.PlanarPosition;
        var targetPlanar = new Vector2(targetPos.X, targetPos.Z);
        var decision = AttackPresentation.Decide(current, targetPlanar, Owner?.AttackRange ?? AttackPresentation.MeleeRange);
        if (decision.ShouldAttack)
        {
            FaceToward(targetPlanar);
            ApplyPlayerCommand(_playerController.ApplyLocomotion(MotionCode.Stand));

            return;
        }

        var elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var dir = decision.ApproachDirection;
        var speed = MovementSpeed(_runMode);
        var step = speed * elapsed;
        var probeY = _playerBody.Height + BodyProbeHeight;
        var slid = MoveWithCollision(current, dir * step, probeY);
        CommitGroundMovement(ResolveEntityCollision(current, slid));
        _playerForward = new Vector3(dir.X, 0f, dir.Y);
        ApplyPlayerWorld();
        ApplyPlayerCommand(_playerController.ApplyLocomotion(_runMode ? MotionCode.Run : MotionCode.Walk));
    }
}
