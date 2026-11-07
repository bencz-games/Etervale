using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.World.Ai;

public sealed class MonsterAiBrain
{
    // A stuck monster turns by rand() % 1024 - 512 on the 4096-step circle (a quarter turn, centred).
    private const int StuckTurnSteps = 1024;

    private readonly ISimRng _rng;

    public MonsterAiBrain(ISimRng rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    public MonsterAttackEvent? Tick(MonsterInstance monster, MonsterAiTuning tuning, IPreyView prey, float dtMs)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(prey);
        if (!float.IsFinite(dtMs) || dtMs <= 0f)
        {
            return null;
        }

        if (monster.AiState == MonsterAiState.Dead || !monster.IsAlive)
        {
            return null;
        }

        // Drinking and transforming play out even under a stun.
        if (monster.Motion is MotionState.Eat or MotionState.Warp)
        {
            monster.SinceRetargetMs += dtMs;
            AdvanceSpecial(monster, prey, dtMs);
            return null;
        }

        // A stunned or charmed monster holds still: no decisions, steps or swings until the control state ends. A charmed
        // monster is on its caster's side; it attacks no player while the charm lasts.
        if (monster.Effects.Count > 0 && monster.Effects.Has(ControlFlags.Stun | ControlFlags.Charmed))
        {
            if (monster.AiState == MonsterAiState.Attack)
            {
                // The interrupted swing does not land; the chase resumes on the same target afterwards.
                monster.SkillImpactPending = false;
                monster.AttackHitFired = true;
                monster.AiState = MonsterAiState.Chase;
                monster.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
            }

            monster.Motion = MotionState.Stand;
            return null;
        }

        // A slowed monster moves, decides and swings at the slowed rate, as the original halves both its step and its frame
        // advance.
        dtMs *= SlowFactor(monster);

        monster.SinceDecisionMs += dtMs;
        monster.SinceRetargetMs += dtMs;
        if (monster.TargetLockMsLeft > 0f)
        {
            monster.TargetLockMsLeft = MathF.Max(0f, monster.TargetLockMsLeft - dtMs);
        }

        // Damage motion suppresses decisions and movement until its clip drains.
        if (monster.Motion == MotionState.Damage)
        {
            monster.FlinchMsLeft -= dtMs;
            if (monster.FlinchMsLeft <= 0f)
            {
                bool targetLive = monster.TargetId != 0 && prey.TryGet(monster.TargetId, out PreyCandidate tgt) && tgt.IsAlive;
                monster.ManoeuvreClipsLeft = 0;
                if (targetLive)
                {
                    monster.AiState = MonsterAiState.Chase;
                    monster.Motion = MotionState.Stand;
                    monster.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
                }
                else
                {
                    EnterIdle(monster);
                }

                TryBeginSpecial(monster, tuning);
            }

            return null;
        }

        if (monster.AiState == MonsterAiState.Attack)
        {
            return AdvanceAttack(monster, tuning, prey, dtMs);
        }

        // The original target scan runs during leash and stuck manoeuvres too; only a target lock suspends it.
        if (monster.SinceRetargetMs >= MonsterAiTuning.RetargetIntervalMs && monster.TargetLockMsLeft <= 0f)
        {
            monster.SinceRetargetMs = 0f;
            monster.TargetId = AcquireNearestPrey(monster, tuning, prey);
        }

        AdvanceClips(monster, tuning, dtMs);
        if (monster.Motion is MotionState.Eat or MotionState.Warp)
        {
            return null;
        }

        if (monster.SinceDecisionMs >= MonsterAiTuning.DecisionIntervalMs)
        {
            monster.SinceDecisionMs %= MonsterAiTuning.DecisionIntervalMs;
            return Decide(monster, tuning, prey, dtMs);
        }

        if (monster.Motion.IsMoving())
        {
            if (monster.AiState == MonsterAiState.Wander)
            {
                StepWander(monster, tuning, dtMs);
            }
            else if (monster.AiState == MonsterAiState.Leash)
            {
                IntegrateHeading(monster, monster.ManoeuvreHeading, monster.Motion == MotionState.Run, dtMs);
            }
            else
            {
                IntegrateToward(monster, monster.MoveTarget, monster.Motion == MotionState.Run, dtMs);
            }
        }

        return null;
    }

    private MonsterAttackEvent? Decide(MonsterInstance m, MonsterAiTuning t, IPreyView prey, float decisionDt)
    {
        PreyCandidate live = default;
        bool hasTarget = m.TargetId != 0 && prey.TryGet(m.TargetId, out live) && live.IsAlive;
        if (!hasTarget)
        {
            live = default;
            m.TargetId = 0;
            m.Engaged = false;
        }

        if (t.StrikesInPlace)
        {
            StrikeInPlace(m, t, hasTarget, live);
            return null;
        }

        if (t.IsImmobile)
        {
            EnterIdle(m);
            return null;
        }

        switch (m.AiState)
        {
            case MonsterAiState.Leash:
                // The reach test runs on every update, so a target in reach is attacked even mid-manoeuvre.
                if (hasTarget && MonsterAttackReach.CanBegin(m.Def, m.Position, live.Position))
                {
                    FaceToward(m, live.Position);
                    BeginAttack(m, t);
                }
                else
                {
                    IntegrateHeading(m, m.ManoeuvreHeading, m.Motion == MotionState.Run, decisionDt);
                }

                return null;

            case MonsterAiState.Idle:
            case MonsterAiState.Wander:
                if (hasTarget)
                {
                    m.AiState = MonsterAiState.Chase;
                    return StepChase(m, t, live, decisionDt);
                }

                if (m.AiState == MonsterAiState.Idle && TryBeginSpecial(m, t))
                {
                    return null;
                }

                StepWander(m, t, decisionDt);
                return null;

            case MonsterAiState.Chase:
                if (!hasTarget)
                {
                    EnterIdle(m);
                    return null;
                }

                return StepChase(m, t, live, decisionDt);

            default:
                return null;
        }
    }

    // A target out of reach is let go with its lock (a boss's lock on a ranged attacker), so that the next scan picks the
    // nearest prey again.
    private void StrikeInPlace(MonsterInstance m, MonsterAiTuning t, bool hasTarget, in PreyCandidate target)
    {
        if (hasTarget && MonsterAttackReach.CanBegin(m.Def, m.Position, target.Position))
        {
            BeginAttack(m, t);
            return;
        }

        m.TargetLockMsLeft = 0f;
        EnterIdle(m);
    }

    private static ulong AcquireNearestPrey(MonsterInstance m, MonsterAiTuning t, IPreyView prey)
    {
        IReadOnlyList<PreyCandidate> candidates = prey.Nearby(m.Position, MathF.Sqrt(t.SightSq) + 1f);
        ulong best = 0;
        long bestSq = long.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            PreyCandidate c = candidates[i];
            if (!c.IsAlive || m.ClanId != 0 && c.ClanId == m.ClanId)
            {
                continue;
            }

            if (MathF.Abs(c.Position.Y - m.Position.Y) >= t.VerticalAggroGateF)
            {
                continue;
            }

            long dx = (long)(c.Position.X - m.Position.X);
            long dz = (long)(c.Position.Z - m.Position.Z);
            long dy = (long)(c.Position.Y - m.Position.Y);
            long distSq = (dx * dx) + (dy * dy) + (dz * dz);
            if (distSq < bestSq && distSq < SightSqFor(t, c))
            {
                bestSq = distSq;
                best = c.EntityId;
            }
        }

        return best;
    }

    // The slow modifiers of the monster's effects scale its clock (a -50 % move speed halves everything).
    public static float SlowFactor(MonsterInstance monster)
    {
        if (monster.Effects.Count == 0 || !monster.Effects.Has(ControlFlags.Slow))
        {
            return 1f;
        }

        return Math.Clamp((100 + monster.Effects.Totals.Percent(StatKind.MoveSpeed)) / 100f, 0.05f, 1f);
    }

    // Stealth shortens the sight at which a monster notices the prey; some bosses see through it.
    private static long SightSqFor(MonsterAiTuning t, in PreyCandidate prey)
    {
        if (prey.SightPercent >= 100 || t.SeesStealth)
        {
            return t.SightSq;
        }

        var sight = MathF.Sqrt(t.SightSq) * prey.SightPercent / 100f;
        return (long)(sight * sight);
    }

    private static void EnterIdle(MonsterInstance m)
    {
        m.AiState = MonsterAiState.Idle;
        m.Motion = MotionState.Stand;
        m.TargetId = 0;
        m.Engaged = false;
        m.ManoeuvreClipsLeft = 0;
    }

    private static void EndManoeuvre(MonsterInstance m)
    {
        m.ManoeuvreClipsLeft = 0;
        m.Motion = MotionState.Stand;
        if (m.TargetId != 0)
        {
            m.AiState = MonsterAiState.Chase;
            m.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
        }
        else
        {
            EnterIdle(m);
        }
    }

    private MonsterAttackEvent? StepChase(MonsterInstance m, MonsterAiTuning t, PreyCandidate target, float dt)
    {
        if (MonsterAttackReach.CanBegin(m.Def, m.Position, target.Position))
        {
            FaceToward(m, target.Position);
            BeginAttack(m, t);
            return null;
        }

        float dx = target.Position.X - m.Position.X;
        float dz = target.Position.Z - m.Position.Z;
        float distSq = (dx * dx) + (dz * dz);
        float dist = MathF.Sqrt(distSq);
        if (m.Engaged && dist > t.AttackRange + MonsterAiTuning.ChaseHysteresis)
        {
            m.Engaged = false;
        }

        // Approach a point inside the reach on the line to the target instead of the target itself; there the next
        // decision swings. A target the monster cannot swing at from there (height) is faced while standing.
        if (dist <= t.StopDistance + 0.01f)
        {
            FaceToward(m, target.Position);
            m.Motion = MotionState.Stand;
            return null;
        }

        bool run = !m.Engaged && t.CanRun && distSq < t.RunDistanceSq;
        float approach = (dist - t.StopDistance) / dist;
        m.MoveTarget = new Vector3(m.Position.X + dx * approach, m.Position.Y, m.Position.Z + dz * approach);
        m.Motion = run ? MotionState.Run : MotionState.Walk;
        IntegrateToward(m, m.MoveTarget, run, dt);
        return null;
    }

    private static void StepWander(MonsterInstance m, MonsterAiTuning t, float dt)
    {
        if (m.AiState == MonsterAiState.Idle)
        {
            return;
        }

        if (t.IsImmobile)
        {
            EnterIdle(m);
            return;
        }

        // A new monster starts with a manoeuvre count of 2. Without prey, walk in the spawn direction for two clips, then stand.
        float remainingMs = 2f * t.WalkCycleMs - m.SpawnWalkElapsedMs;
        if (remainingMs <= 0f)
        {
            EnterIdle(m);
            return;
        }

        float elapsed = MathF.Min(dt, remainingMs);
        var forward = Planar.Forward(m.Facing);
        m.MoveTarget = m.Position + forward * 10000f;
        IntegrateToward(m, m.MoveTarget, run: false, elapsed);
        m.SpawnWalkElapsedMs += elapsed;
        if (m.SpawnWalkElapsedMs >= 2f * t.WalkCycleMs)
        {
            EnterIdle(m);
        }
    }

    // Clip-end bookkeeping of the original character update for a monster with a target or a manoeuvre: the progress probe, the
    // manoeuvre count and the leash, in the original order.
    private void AdvanceClips(MonsterInstance m, MonsterAiTuning t, float dtMs)
    {
        bool counting = !t.IsImmobile && m.AiState is MonsterAiState.Chase or MonsterAiState.Leash;
        if (!counting)
        {
            m.ClipProgress = 0f;
            m.ProbeClips = 0;
            m.CrowdedSinceProbe = false;
            m.ProgressProbePosition = m.Position;
            return;
        }

        m.CrowdedSinceProbe |= m.Crowded;
        float cycleMs = m.Motion == MotionState.Run ? t.RunCycleMs : t.WalkCycleMs;
        m.ClipProgress += dtMs / MathF.Max(1f, cycleMs);
        while (m.ClipProgress >= 1f && m.AiState is MonsterAiState.Chase or MonsterAiState.Leash)
        {
            m.ClipProgress -= 1f;
            EndClip(m, t);
        }
    }

    private void EndClip(MonsterInstance m, MonsterAiTuning t)
    {
        if (m.Motion is MotionState.Eat or MotionState.Warp || TryBeginSpecial(m, t))
        {
            return;
        }

        m.ProbeClips++;
        if (m.ProbeClips >= MonsterAiTuning.ProgressProbeClips)
        {
            bool stuck = Planar.DistanceSquared(m.Position, m.ProgressProbePosition) < t.StuckDistanceSq;
            m.ProgressProbePosition = m.Position;
            m.ProbeClips = 0;
            bool queued = m.CrowdedSinceProbe;
            m.CrowdedSinceProbe = false;
            if (stuck && !queued)
            {
                BeginStuckManoeuvre(m, t);
                return;
            }
        }

        if (m.AiState == MonsterAiState.Leash)
        {
            m.ManoeuvreClipsLeft--;
            if (m.ManoeuvreClipsLeft <= 0)
            {
                EndManoeuvre(m);
            }
        }

        // Soft leash: head home for six clips, keeping the target; the chase may resume afterwards.
        if (m.Motion.IsMoving() && Planar.DistanceSquared(m.Position, m.HomeAnchor) > t.LeashSq)
        {
            BeginManoeuvre(m, HeadingTo(m, m.HomeAnchor), MonsterAiTuning.LeashManoeuvreClips, m.Motion);
        }
    }

    // Stuck: turn up to 45 degrees off the way home and run for ten clips without dropping the target.
    private void BeginStuckManoeuvre(MonsterInstance m, MonsterAiTuning t)
    {
        int turn = _rng.NextExclusive(0, StuckTurnSteps) - (StuckTurnSteps / 2);
        float heading = HeadingTo(m, m.HomeAnchor) + (turn * GameUnits.RadiansPerAngle);
        var motion = t.CanRun ? MotionState.Run : MotionState.Walk;
        BeginManoeuvre(m, heading, MonsterAiTuning.StuckManoeuvreClips, motion);
    }

    private static void BeginManoeuvre(MonsterInstance m, float heading, int clips, MotionState motion)
    {
        m.AiState = MonsterAiState.Leash;
        m.Motion = motion;
        m.Engaged = false;
        m.ManoeuvreClipsLeft = clips;
        m.ManoeuvreHeading = heading;
        m.Facing = heading;
    }

    private void BeginAttack(MonsterInstance m, MonsterAiTuning tuning)
    {
        m.AiState = MonsterAiState.Attack;
        m.Motion = MotionState.Attack;
        m.Engaged = true;
        m.ManoeuvreClipsLeft = 0;
        m.AttackTimerMs = 0f;
        m.ClipProgress = 0f;
        m.ProbeClips = 0;
        m.CrowdedSinceProbe = false;
        m.ProgressProbePosition = m.Position;
        m.AttackHitFired = false;
        m.SwingJustBegan = true;

        // The attack clip is drawn among the model's attack clips, then the skill roll rand()%100 < the definition's skill
        // rating percent picks a skill clip the same way.
        var attack = Draw(tuning.AttackClips, new MonsterActionClip(-1, default(char), tuning.CooldownMs, tuning.WindupMs));
        m.AttackVariant = attack;
        SetAction(m, attack);
        m.SecondHitAtMs = MonsterSkillRules.StrikesTwice(m.Def) ? attack.HitAtMs + MonsterSkillRules.BabelSecondHitMs : 0f;
        int rating = m.Def.Skill.SkillRatingPercent;
        m.AttackIsSkill = tuning.CanCastOffensiveSkill && _rng.RollPercent() < rating;
        m.SkillImpactPending = false;
        if (m.AttackIsSkill)
        {
            SetAction(m, Draw(tuning.SkillClips, new MonsterActionClip(-1, default(char), tuning.SkillDurationMs, tuning.SkillWindupMs)));
            m.SecondHitAtMs = 0f;
            m.Motion = MotionState.Skill;
            m.SkillTargetId = m.TargetId;
            m.SkillOrigin = m.Position;
            m.SkillFacing = m.Facing;
            m.SkillCenter = MonsterOffensiveSkill.AreaCenter(m.Position, m.Facing, m.Def.Skill);
        }
    }

    private MonsterAttackEvent? AdvanceAttack(MonsterInstance m, MonsterAiTuning t, IPreyView prey, float dtMs)
    {
        m.AttackTimerMs += dtMs;

        MonsterAttackEvent? landed = null;

        float hitAtMs = m.ActionHitAtMs;
        float durationMs = m.ActionDurationMs;

        // Area casts own their placement even if the original target dies; the host filters live victims at impact.
        if (!m.AttackHitFired && m.AttackTimerMs >= hitAtMs)
        {
            m.AttackHitFired = true;
            m.AttackAnger++;
            if (m.AttackIsSkill)
            {
                m.SkillImpactPending = true;
                landed = new MonsterAttackEvent(m.EntityId, m.SkillTargetId, true);
            }
            else if (m.TargetId != 0 && prey.TryGet(m.TargetId, out PreyCandidate tgt) && tgt.IsAlive
                && MonsterAttackReach.CanLandBasic(m.Def, m.Position, tgt.Position))
            {
                landed = new MonsterAttackEvent(m.EntityId, m.TargetId, m.AttackIsSkill);
            }
        }

        // Babel's second blow lands like the first when its target is still in reach.
        if (landed is null && m.SecondHitAtMs > 0f && m.AttackTimerMs >= m.SecondHitAtMs)
        {
            m.SecondHitAtMs = 0f;
            if (m.TargetId != 0 && prey.TryGet(m.TargetId, out PreyCandidate second) && second.IsAlive
                && MonsterAttackReach.CanLandBasic(m.Def, m.Position, second.Position))
            {
                landed = new MonsterAttackEvent(m.EntityId, m.TargetId, false);
            }
        }

        if (m.AttackTimerMs >= durationMs)
        {
            if (!m.AttackIsSkill && MonsterSkillRules.DropsTargetAfterBlow(m.Def))
            {
                // A curse monster forgets its player after a normal blow and finds it again on its next scan.
                m.TargetId = 0;
            }

            PreyCandidate tgt2 = default;
            bool stillValid = m.TargetId != 0 && prey.TryGet(m.TargetId, out tgt2) && tgt2.IsAlive;
            m.AiState = stillValid ? MonsterAiState.Chase : MonsterAiState.Idle;
            m.Motion = MotionState.Stand;
            if (TryBeginSpecial(m, t))
            {
                return landed;
            }

            // The clip ends in the stand motion and the reach test swings again in the same update while the target stays in
            // reach, so consecutive attacks never pass through a walk. An impact on this tick is resolved first.
            if (stillValid && landed is null && MonsterAttackReach.CanBegin(m.Def, m.Position, tgt2.Position))
            {
                if (!t.StrikesInPlace)
                {
                    FaceToward(m, tgt2.Position);
                }

                BeginAttack(m, t);
            }
            else if (stillValid)
            {
                m.AiState = MonsterAiState.Chase;
                m.Motion = MotionState.Stand;
                m.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
            }
            else
            {
                EnterIdle(m);
            }
        }

        return landed;
    }

    private MonsterActionClip Draw(IReadOnlyList<MonsterActionClip> clips, MonsterActionClip fallback)
    {
        if (clips.Count == 0)
        {
            return fallback;
        }

        return clips.Count == 1 ? clips[0] : clips[_rng.NextExclusive(0, clips.Count)];
    }

    private static void SetAction(MonsterInstance m, in MonsterActionClip clip)
    {
        m.ActionSlot = clip.Slot;
        m.ActionKey = clip.Key;
        m.ActionDurationMs = clip.DurationMs;
        m.ActionHitAtMs = clip.HitAtMs;
    }

    // At the end of a clip a hurt monster drinks a potion, and the three shape-shifters start turning into their other
    // form; the host completes both when their clip ends.
    private static bool TryBeginSpecial(MonsterInstance m, MonsterAiTuning t)
    {
        if (!m.IsAlive || m.Motion is MotionState.Eat or MotionState.Warp)
        {
            return false;
        }

        MotionState special;
        float durationMs;
        if (MonsterTransformRules.Wants(m))
        {
            special = MotionState.Warp;
            durationMs = t.WarpDurationMs;
        }
        else if (MonsterLifeRules.WantsPotion(m))
        {
            m.Potions--;
            m.DrinkHeals = true;
            special = MotionState.Eat;
            durationMs = t.EatDurationMs;
        }
        else
        {
            return false;
        }

        m.Motion = special;
        m.SpecialMsLeft = durationMs;
        m.ManoeuvreClipsLeft = 0;
        if (m.AiState is MonsterAiState.Leash or MonsterAiState.Attack)
        {
            m.AiState = m.TargetId != 0 ? MonsterAiState.Chase : MonsterAiState.Idle;
        }

        return true;
    }

    private static void AdvanceSpecial(MonsterInstance m, IPreyView prey, float dtMs)
    {
        m.SpecialMsLeft -= dtMs;
        if (m.SpecialMsLeft > 0f)
        {
            return;
        }

        m.CompletedSpecial = m.Motion;
        m.Motion = MotionState.Stand;
        if (m.TargetId != 0 && prey.TryGet(m.TargetId, out PreyCandidate target) && target.IsAlive)
        {
            m.AiState = MonsterAiState.Chase;
            m.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
        }
        else
        {
            EnterIdle(m);
        }
    }

    public static void ApplyFlinchOnHit(MonsterInstance monster, ISimRng rng, int damage, bool crit, float durationMs)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(rng);

        if (!monster.IsAlive || damage <= 0)
        {
            return;
        }

        if (monster.Motion is MotionState.Damage or MotionState.Dead)
        {
            return;
        }

        int maxLife = monster.Def.Combat.MaxLife;
        int multiplier = monster.Def.Combat.Level > 14 ? 5 : 10;
        bool significant = maxLife > 0 && (long)damage * multiplier >= maxLife;
        if (!significant && !crit)
        {
            return;
        }

        if (rng.RollPercent() >= monster.Def.Combat.DamageStunPercent)
        {
            return;
        }

        if (monster.AiState == MonsterAiState.Attack)
        {
            // Interrupt the swing: its windup blow never lands (mark it fired so AdvanceAttack can't).
            monster.AttackHitFired = true;
            monster.AiState = MonsterAiState.Chase;
        }

        monster.SkillImpactPending = false;
        monster.ProbeClips = 0;
        monster.ClipProgress = 0f;
        monster.ProgressProbePosition = monster.Position;
        monster.Motion = MotionState.Damage;
        monster.FlinchMsLeft = durationMs;
    }

    public static void ApplyBossRetargetOnHit(MonsterInstance boss, ulong attackerEntityId)
    {
        ArgumentNullException.ThrowIfNull(boss);
        if (!boss.Def.IsBoss)
        {
            return;
        }

        if (boss.TargetLockMsLeft > 0f)
        {
            return;
        }

        if (ForceTarget(boss, attackerEntityId, MonsterAiTuning.BossTargetLockMs) && boss.Motion != MotionState.Damage)
        {
            boss.Motion = MotionState.Stand;
        }
    }

    // Locks the monster on a target for lockMs (it does not retarget meanwhile); an idle, wandering or leashing monster
    // turns to chase it at once. True when it started chasing.
    public static bool ForceTarget(MonsterInstance monster, ulong targetId, float lockMs)
    {
        monster.TargetId = targetId;
        monster.TargetLockMsLeft = lockMs;
        if (monster.AiState is not (MonsterAiState.Idle or MonsterAiState.Wander or MonsterAiState.Leash))
        {
            return false;
        }

        monster.AiState = MonsterAiState.Chase;
        monster.ManoeuvreClipsLeft = 0;
        monster.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
        return true;
    }

    private static void IntegrateToward(MonsterInstance m, Vector3 target, bool run, float dtMs)
    {
        var step = DestinationMovement.Advance(new Vector2(m.Position.X, m.Position.Z), new Vector2(target.X, target.Z),
            MonsterLocomotion.UnitsPerSecond(m.Def, run), dtMs / 1000f);
        if (step.Direction != Vector2.Zero)
        {
            m.Facing = MathF.Atan2(step.Direction.X, step.Direction.Y);
        }

        m.Position = new Vector3(step.Position.X, m.Position.Y, step.Position.Y);
    }

    // Manoeuvres keep the yaw heading chosen when they began; walls are slid by the host.
    private static void IntegrateHeading(MonsterInstance m, float heading, bool run, float dtMs)
    {
        float step = MonsterLocomotion.UnitsPerSecond(m.Def, run) * (dtMs / 1000f);
        var direction = Planar.Forward(heading);
        m.Facing = heading;
        m.MoveTarget = m.Position + direction * 10000f;
        m.Position += direction * step;
    }

    private static float HeadingTo(MonsterInstance m, Vector3 point) =>
        DestinationMovement.HasArrived(new Vector2(m.Position.X, m.Position.Z), new Vector2(point.X, point.Z)) ? m.Facing : Planar.Heading(m.Position, point);

    private static void FaceToward(MonsterInstance m, Vector3 target)
        => m.Facing = Planar.Heading(m.Position, target);
}
