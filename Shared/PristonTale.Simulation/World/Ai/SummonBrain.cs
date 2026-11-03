using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Physics;

namespace PristonTale.Simulation.World.Ai;

// What the host tells a summon each tick: where its owner stands and the enemy it should fight (none to follow only).
public readonly record struct SummonOrders(Vector3 OwnerPosition, PreyCandidate Target, bool HasTarget);

// The behaviour of an owned summon: it keeps near its owner (runs back beyond 300 units with a target, beyond 150
// without one), chases and attacks the enemy its orders name, and swings with its definition's attack clip. Attacks
// start within the monster's reach plus 32 units (at least 80) and land on a live target at the clip's hit time.
public sealed class SummonBrain
{
    public const float RecallDistance = 500f;
    public const float FollowWithTargetDistance = 300f;
    public const float FollowIdleDistance = 150f;
    private const float FollowStopDistance = 90f;
    private const float AttackReachPad = 32f;
    private const float MinimumAttackReach = 80f;

    private readonly ISimRng _rng;

    public SummonBrain(ISimRng rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    public static float AttackReach(MonsterDefinition definition) => MathF.Max(MinimumAttackReach, definition.Combat.MeleeRange + AttackReachPad);

    // Beyond this distance the summon is placed beside its owner instead of walking.
    public static bool NeedsRecall(MonsterInstance summon, Vector3 ownerPosition) => Planar.Distance(summon.Position, ownerPosition) >= RecallDistance;

    public MonsterAttackEvent? Tick(MonsterInstance summon, MonsterAiTuning tuning, in SummonOrders orders, float dtMs)
    {
        ArgumentNullException.ThrowIfNull(summon);
        ArgumentNullException.ThrowIfNull(tuning);
        if (!summon.IsAlive || !float.IsFinite(dtMs) || dtMs <= 0f)
        {
            return null;
        }

        if (summon.Effects.Count > 0 && summon.Effects.Has(ControlFlags.Stun))
        {
            summon.AiState = MonsterAiState.Idle;
            summon.Motion = MotionState.Stand;
            return null;
        }

        if (summon.AiState == MonsterAiState.Attack)
        {
            return AdvanceAttack(summon, tuning, orders, dtMs);
        }

        var ownerDistance = Planar.Distance(summon.Position, orders.OwnerPosition);
        var hasTarget = orders.HasTarget && orders.Target.IsAlive;
        if (ownerDistance >= FollowWithTargetDistance || !hasTarget && ownerDistance >= FollowIdleDistance
            || summon.AiState == MonsterAiState.Leash && ownerDistance > FollowStopDistance)
        {
            // Running back to the owner drops the target until the summon is close again.
            summon.TargetId = 0;
            summon.AiState = MonsterAiState.Leash;
            summon.Motion = Locomotion(tuning);
            MoveToward(summon, orders.OwnerPosition, FollowStopDistance, dtMs);
            return null;
        }

        if (!hasTarget)
        {
            summon.TargetId = 0;
            summon.AiState = MonsterAiState.Idle;
            summon.Motion = MotionState.Stand;
            return null;
        }

        var target = orders.Target;
        summon.TargetId = target.EntityId;
        summon.AiState = MonsterAiState.Chase;
        var reach = AttackReach(summon.Def);
        var distance = Planar.Distance(summon.Position, target.Position);
        if (distance < reach && MathF.Abs(summon.Position.Y - target.Position.Y) <= MonsterAttackReach.VerticalEntryLimit)
        {
            Face(summon, target.Position);
            BeginAttack(summon, tuning);
            return null;
        }

        summon.Motion = Locomotion(tuning);
        MoveToward(summon, target.Position, reach - MonsterAiTuning.StopMargin, dtMs);
        return null;
    }

    private void BeginAttack(MonsterInstance summon, MonsterAiTuning tuning)
    {
        summon.AiState = MonsterAiState.Attack;
        summon.AttackTimerMs = 0f;
        summon.AttackHitFired = false;
        summon.SwingJustBegan = true;
        summon.AttackIsSkill = tuning.CanCastOffensiveSkill && _rng.RollPercent() < summon.Def.Skill.SkillRatingPercent;
        summon.Motion = summon.AttackIsSkill ? MotionState.Skill : MotionState.Attack;
        summon.SkillTargetId = summon.TargetId;
        summon.SkillImpactPending = false;
    }

    private static MonsterAttackEvent? AdvanceAttack(MonsterInstance summon, MonsterAiTuning tuning, in SummonOrders orders, float dtMs)
    {
        summon.AttackTimerMs += dtMs;
        var hitAtMs = summon.AttackIsSkill ? tuning.SkillWindupMs : tuning.WindupMs;
        var durationMs = summon.AttackIsSkill ? tuning.SkillDurationMs : tuning.CooldownMs;
        MonsterAttackEvent? landed = null;
        var targetLive = orders.HasTarget && orders.Target.IsAlive && orders.Target.EntityId == summon.TargetId;
        if (!summon.AttackHitFired && summon.AttackTimerMs >= hitAtMs)
        {
            summon.AttackHitFired = true;
            if (targetLive)
            {
                landed = new MonsterAttackEvent(summon.EntityId, summon.TargetId, summon.AttackIsSkill);
            }
        }

        if (summon.AttackTimerMs >= MathF.Max(durationMs, hitAtMs))
        {
            summon.AiState = targetLive ? MonsterAiState.Chase : MonsterAiState.Idle;
            summon.Motion = MotionState.Stand;
        }

        return landed;
    }

    // Summons run to their owner and their prey; a model without a run clip walks there instead, at walking speed.
    private static MotionState Locomotion(MonsterAiTuning tuning) => tuning.HasRunClip ? MotionState.Run : MotionState.Walk;

    private static void MoveToward(MonsterInstance summon, Vector3 point, float stopDistance, float dtMs)
    {
        var dx = point.X - summon.Position.X;
        var dz = point.Z - summon.Position.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= MathF.Max(0f, stopDistance) || distance < 1e-3f)
        {
            summon.Motion = MotionState.Stand;
            if (summon.AiState == MonsterAiState.Leash)
            {
                summon.AiState = MonsterAiState.Idle;
            }

            return;
        }

        summon.Facing = Planar.Heading(summon.Position, point);
        var step = MathF.Min(distance - MathF.Max(0f, stopDistance), MonsterLocomotion.UnitsPerSecond(summon.Def, running: summon.Motion == MotionState.Run) * dtMs / 1000f);
        summon.MoveTarget = point;
        summon.Position = new Vector3(summon.Position.X + dx / distance * step, summon.Position.Y, summon.Position.Z + dz / distance * step);
    }

    private static void Face(MonsterInstance summon, Vector3 point) => summon.Facing = Planar.Heading(summon.Position, point);
}
