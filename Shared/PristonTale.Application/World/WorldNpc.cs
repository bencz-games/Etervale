using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Units;

namespace PristonTale.Application.World;

public sealed class WorldNpc
{
    private const float WalkUnitsPerSecond = 70f;
    private const float DefaultStandDurationMs = 40f * GameUnits.FrameMs;
    private const float MaximumStepMs = 2_000f;

    private readonly IReadOnlyList<MotionClip> _standClips;
    private readonly bool _canWalk;
    private readonly float _moveRange;
    private uint _randomState;
    private float _standMsLeft;
    private Vector3 _moveTarget;

    private WorldNpc(
        ulong entityId,
        NpcDefinition definition,
        Vector3 position,
        float facing,
        MotionTimingSet? motionTimings)
    {
        EntityId = entityId;
        Definition = definition;
        Position = position;
        HomeAnchor = position;
        Facing = facing;
        MoveState = MotionState.Stand;
        _moveRange = MathF.Max(0f, definition.MoveRange);
        _standClips = motionTimings?.ClipsForState(MotionCode.Stand) ?? Array.Empty<MotionClip>();
        _canWalk = _moveRange > 0f && motionTimings?.TryGetClip(MotionCode.Walk, out _) == true;
        _randomState = (uint)(entityId ^ entityId >> 32) ^ unchecked((uint)definition.Code * 0x9E3779B9u) ^ 0xA341316Cu;
        if (_randomState == 0)
        {
            _randomState = 1;
        }

        BeginStand();
    }

    public ulong EntityId { get; }
    public NpcDefinition Definition { get; }
    public Vector3 HomeAnchor { get; }
    public Vector3 Position { get; private set; }
    public float Facing { get; private set; }
    public MotionState MoveState { get; private set; }
    public bool CanWalk => _canWalk;

    public static WorldNpc Place(
        ulong entityId,
        NpcDefinition definition,
        FixedNpc placement,
        MotionTimingSet? motionTimings = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var position = new Vector3(GameUnits.FromFixed(placement.X), GameUnits.FromFixed(placement.Y), GameUnits.FromFixed(placement.Z));
        var facing = GameUnits.FromAngle(placement.Angle);
        return new WorldNpc(entityId, definition, position, facing, motionTimings);
    }

    public Vector3 IntegrateStep(float dtSeconds)
    {
        if (!float.IsFinite(dtSeconds) || dtSeconds <= 0f)
        {
            return Position;
        }

        var dtMs = Math.Clamp(dtSeconds * 1000f, 0f, MaximumStepMs);
        if (!_canWalk)
        {
            return Position;
        }

        if (MoveState == MotionState.Stand)
        {
            _standMsLeft -= dtMs;
            if (_standMsLeft > 0f)
            {
                return Position;
            }

            // After a Stand boundary an NPC whose model walks and whose wander radius is not zero wanders one time in four:
            // bits 2 and 3 of a draw in 0..4095 are both clear.
            if (((NextRandom(4096) >> 2) & 3) != 0)
            {
                BeginStand();
                return Position;
            }

            BeginWalk();
        }

        var current = new Vector2(Position.X, Position.Z);
        var target = new Vector2(_moveTarget.X, _moveTarget.Z);
        var step = DestinationMovement.Advance(current, target, WalkUnitsPerSecond, dtMs / 1000f);
        if (step.Direction != Vector2.Zero)
        {
            Facing = MathF.Atan2(step.Direction.X, step.Direction.Y);
        }

        return new Vector3(step.Position.X, Position.Y, step.Position.Y);
    }

    public void CommitStep(Vector3 accepted, bool blocked)
    {
        Position = accepted;
        if (blocked || DestinationMovement.HasArrived(
            new Vector2(Position.X, Position.Z),
            new Vector2(_moveTarget.X, _moveTarget.Z)))
        {
            BeginStand();
        }
    }

    public EntityView ToView()
    {
        return new EntityView(
            EntityId,
            EntityKind.Npc,
            false,
            Position,
            Facing,
            MoveState,
            1,
            1,
            1,
            default,
            (uint)Definition.Code,
            moveSpeed: _canWalk ? 1 : 0);
    }

    private void BeginStand()
    {
        MoveState = MotionState.Stand;
        if (_standClips.Count == 0)
        {
            _standMsLeft = DefaultStandDurationMs;
            return;
        }

        var clip = _standClips[NextRandom(_standClips.Count)];
        _standMsLeft = MotionTiming.DurationMs(clip.Frames, 1f);
    }

    private void BeginWalk()
    {
        var angle = NextRandom(4096) * (MathF.Tau / 4096f);
        var distance = _moveRange * (0.35f + 0.65f * NextUnit());
        _moveTarget = new Vector3(
            HomeAnchor.X + MathF.Sin(angle) * distance,
            HomeAnchor.Y,
            HomeAnchor.Z + MathF.Cos(angle) * distance);
        Facing = angle;
        MoveState = MotionState.Walk;
    }

    private int NextRandom(int upperBound)
    {
        _randomState = unchecked((_randomState * 214013u) + 2531011u);
        var value = (int)((_randomState >> 16) & 0x7FFFu);
        return upperBound > 0 ? value % upperBound : 0;
    }

    private float NextUnit()
    {
        return NextRandom(32768) / 32768f;
    }
}
