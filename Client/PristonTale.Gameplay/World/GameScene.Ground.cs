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

// The local player's body on the ground: floor height, falls, collision with walls and entities, and its world matrix.
public sealed partial class GameScene
{
    private void UpdateBodyHeight(PhysicsBody body)
    {
        if (TryGetFloorHeight(body.Position, out var height))
        {
            body.Height = height - GroundTraversal.VisualOffset;
        }
    }

    // The floor nearest the server's height for a teleport, which can land far above or below the old height; the highest
    // floor when the server's height is not near one.
    private void PlaceAtHeight(PhysicsBody body, float height)
    {
        if (_fieldCollision.TryGetFloorHeight(body.Position, height, WarpFloorTolerance, out var floor)
            || TryGetFloorHeight(body.Position, out floor))
        {
            body.Height = floor - GroundTraversal.VisualOffset;
        }
    }

    // A body with no floor under it (placed before its field's ground was loaded, such as a town restart into a field
    // still streaming) lands on the floor nearest the server's height once that ground is there. A body above its floor
    // falls to it (GroundFall). The special-warp hold keeps the gate's height on purpose.
    private void KeepOnFloor(float dt)
    {
        if (_playerBody is null || _pendingWarp is not null)
        {
            return;
        }

        FinishLanding();
        var feet = _playerBody.Height + GroundTraversal.VisualOffset;
        if (TryGetFloorHeightNear(_playerBody.Position, feet, out var floor))
        {
            // As in the original, the floor is followed only below the motions from 0x100 up (attacks, potions, damage,
            // skills, landings, death): the body holds its height until they end, so a fall never cuts one short.
            if (_playerController.Current < MotionCode.Attack)
            {
                Fall(feet, floor, dt);
            }

            return;
        }

        var before = _playerBody.Height;
        PlaceAtHeight(_playerBody, float.IsFinite(_serverCollisionHeight) ? _serverCollisionHeight : float.MaxValue);
        if (_playerBody.Height != before)
        {
            EndFall(landed: false);
            ApplyPlayerWorld();
            _camera?.SnapFollow(_playerBody.Position, _fieldCollision);
        }
        else if (float.IsFinite(_fallTop))
        {
            // No floor anywhere under the body: the fall cannot land, so it stops rather than hold the fall motion.
            EndFall(landed: false);
        }
    }

    // The renderer returns the actor to Stand when a landing's clip ends (or at once when the model has none); the
    // controller follows it so the landing releases the player.
    private void FinishLanding()
    {
        if (_playerController.Current is MotionCode.FallStand or MotionCode.FallDamage
            && _renderer is not null && _playerBodyActor.IsValid
            && _renderer.TryGetActorMotion(_playerBodyActor, out var motion, out _)
            && (MotionCode)motion.State != _playerController.Current)
        {
            ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.Stand));
        }
    }

    private void Fall(float feet, float floor, float dt)
    {
        // Within eight units the feet stand on the floor, as the original sets them on it.
        if (!GroundFall.IsAirborne(feet, floor))
        {
            if (feet != floor)
            {
                _playerBody.Height = floor - GroundTraversal.VisualOffset;
                ApplyPlayerWorld();
            }

            if (float.IsFinite(_fallTop))
            {
                EndFall(landed: true, _fallTop - floor);
                _reports.Request();
            }

            return;
        }

        // The server checks walls on a straight line between reports; reporting where the fall begins and ends keeps that
        // line on the path the player took off the ledge.
        if (!float.IsFinite(_fallTop))
        {
            _fallTop = feet;
            _reports.Request();
        }

        if (feet - floor > GroundFall.MotionDistance && _playerController.Current != MotionCode.FallDown)
        {
            ApplyPlayerCommand(_playerController.ApplyAuthoritative(MotionCode.FallDown));
        }

        _playerBody.Height = GroundFall.Step(feet, floor, dt) - GroundTraversal.VisualOffset;
        ApplyPlayerWorld();
    }

    // Only a fall that showed the fall motion lands with a landing motion; a short drop or a dead player keeps its motion.
    private void EndFall(bool landed, float fallen = 0f)
    {
        _fallTop = float.NaN;
        if (_playerController.Current == MotionCode.FallDown)
        {
            ApplyPlayerCommand(_playerController.ApplyAuthoritative(landed ? GroundFall.Landing(fallen) : MotionCode.Stand));
        }
    }

    private void TrackBodyHeight(PhysicsBody body)
    {
        if (TryGetFloorHeightNear(body.Position, body.Height + GroundTraversal.VisualOffset, out var height))
        {
            body.Height = height - GroundTraversal.VisualOffset;
        }
    }

    private void CommitGroundMovement(Vector2 destination)
    {
        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        GroundTraversal.TryFollow(feet, destination, _groundFloorQuery, out var reached, out var largestDrop);
        _playerBody.PlanarPosition = new Vector2(reached.X, reached.Z);
        // Walking off a ledge keeps the height: the fall takes the body down (KeepOnFloor). A slope is followed whatever
        // the frame time; only a drop of more than eight units between samples is a ledge.
        if (largestDrop <= GroundFall.SnapDistance)
        {
            _playerBody.Height = reached.Y - GroundTraversal.VisualOffset;
        }
    }

    private bool TryGetFloorHeightNear(Vector3 position, float referenceHeight, out float height)
    {
        return _fieldCollision.TryGetFloorHeight(position, referenceHeight, GroundTraversal.StepUp, out height);
    }

    private Vector2 ResolveEntityCollision(Vector2 oldPlanar, Vector2 desiredPlanar)
    {
        if (_remote == null)
        {
            return desiredPlanar;
        }

        _remote.CollectNeighbours(_remote.SelfEntityId, _entityNeighbours);
        if (_entityNeighbours.Count == 0)
        {
            return desiredPlanar;
        }

        var y = _serverCollisionHeight;
        var old3 = new NumVector3(oldPlanar.X, y, oldPlanar.Y);
        var desired3 = new NumVector3(desiredPlanar.X, y, desiredPlanar.Y);
        var accepted = EntityCollision.Resolve(old3, desired3, EntityCollisionRadii.Player, _entityNeighbours);
        return new Vector2(accepted.X, accepted.Z);
    }

    // The shared wall slide at probeY, stopping the body's radius short of a wall.
    private Vector2 MoveWithCollision(Vector2 from, Vector2 delta, float probeY)
    {
        _wallTrace ??= TraceWalls;
        var slid = WallSlide.Slide(new NumVector3(from.X, probeY, from.Y), new System.Numerics.Vector2(delta.X, delta.Y), PlayerRadius, PlayerRadius, _wallTrace);
        return new Vector2(slid.X, slid.Z);
    }

    private bool TraceWalls(NumVector3 start, NumVector3 end, out NumVector3 hit, out NumVector3 normal)
    {
        var found = _fieldCollision.TryTraceSegment(start, end, out var hitPoint, out var hitNormal);
        hit = hitPoint.ToNumerics();
        normal = hitNormal.ToNumerics();
        return found;
    }

    private bool TryGetFloorHeight(Vector3 position, out float height)
    {
        return _fieldCollision.TryGetFloorHeight(position, float.MaxValue, 0f, out height);
    }

    private bool TryRaycastFloor(Ray ray, out Vector3 point)
    {
        point = default;
        var found = false;
        var bestDistance = float.MaxValue;
        foreach (var field in _fields)
        {
            if (field.Collision.TryRaycastFloor(ray, out var p))
            {
                var distance = Vector3.DistanceSquared(ray.Position, p);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    point = p;
                    found = true;
                }
            }
        }
        return found;
    }

    private void ApplyPlayerWorld()
    {
        if (!_playerBodyActor.IsValid)
            return;
        PlayerActors.SetWorld(_actorScene, PlayerWorld());
    }

    private Matrix PlayerWorld()
    {
        return Matrix.CreateRotationY(PlayerYaw) * Matrix.CreateTranslation(GroundTraversal.ToVisualPosition(_playerBody.Position));
    }
}
