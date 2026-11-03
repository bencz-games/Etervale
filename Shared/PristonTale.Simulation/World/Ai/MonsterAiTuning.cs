using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.World.Ai;

// One attack or skill clip a monster may play: its slot in the model's motion table (sent to clients), its key, its
// duration and the time its blow lands.
public readonly record struct MonsterActionClip(int Slot, char Key, float DurationMs, float HitAtMs);

public sealed class MonsterAiTuning
{
    public const int DefaultLeashRadius = 1280;

    public const int RunDistance = 512;

    public const int RunIqThreshold = 6;

    public const int VerticalAggroGate = 140;

    public const int StuckDistance = 32;

    public const float DecisionIntervalMs = 60f;

    public const float RetargetIntervalMs = 900f;

    public const float BossTargetLockMs = 4000f;

    // Original clip-end rules: more than 4 clips probe progress; leash and stuck set the manoeuvre count to 6 and 10.
    public const int ProgressProbeClips = 5;

    public const int LeashManoeuvreClips = 6;

    public const int StuckManoeuvreClips = 10;

    // Etervale rules near the target: approach to this much inside the reach, and keep walking instead of running
    // while the target stays within this band beyond the reach after a swing.
    public const int StopMargin = 8;

    public const int ChaseHysteresis = 24;

    // The four great bosses that skills never knock back or taunt and that see through stealth.
    public static bool IsUnyielding(MonsterDefinition def) => def.SoundCode is CharacterSoundCodes.Babel or CharacterSoundCodes.DeathKnight or CharacterSoundCodes.Kelvezu
        or CharacterSoundCodes.Mokova;

    public bool SeesStealth { get; }

    public MonsterAiTuning(MonsterDefinition def, MotionTimingSet timing = null)
    {
        SeesStealth = IsUnyielding(def);
        _clever = def.Ai.Iq >= RunIqThreshold;
        int sight = Math.Max(0, def.Ai.SightRadius);
        SightSq = (long)sight * sight;

        LeashSq = (long)DefaultLeashRadius * DefaultLeashRadius;

        RunDistanceSq = (long)RunDistance * RunDistance;

        // Player targets use the definition's attack range without the summon branch's padding.
        AttackRange = Math.Max(0, def.Combat.MeleeRange);
        AttackRangeSq = (long)AttackRange * AttackRange;
        StopDistance = Math.Max(0, AttackRange - StopMargin);

        // An attack range of 0 never passes the reach test (dist < 0), and the five such definitions (doors and maple
        // event objects) have no walk or run clip, so a walk motion cannot start and they never move either.
        IsImmobile = AttackRange == 0;

        StuckDistanceSq = (long)StuckDistance * StuckDistance;
        VerticalAggroGateF = VerticalAggroGate;


        // Imported clips replace the fallback duration and authored hit offset.
        (WindupMs, CooldownMs) = DeriveAttackTiming(def.Combat.AttackSpeed);
        FlinchDurationMs = FlinchFallbackMs;
        DieDurationMs = 0f;

        // A model without a run clip walks wherever it would run, at walking speed; without imported clips the
        // model is taken to have one.
        HasRunClip = timing == null;
        WalkCycleMs = LocomotionCycleFallbackMs;
        RunCycleMs = LocomotionCycleFallbackMs;
        if (timing != null)
        {
            AttackClips = ActionClips(timing.ClipsForState(MotionCode.Attack), lateHit: false);
            SkillClips = MonsterSkillRules.HasSkill(def)
                ? ActionClips(timing.ClipsForState(MotionCode.Skill), MonsterSkillRules.HasLateSkillHit(def))
                : [];
            if (SkillClips.Count > 0)
            {
                SkillDurationMs = SkillClips[0].DurationMs;
                SkillWindupMs = SkillClips[0].HitAtMs;
                CanCastOffensiveSkill = SkillDurationMs > 0f;
            }

            if (timing.TryGetClip(MotionCode.Eat, out MotionClip eat))
            {
                EatDurationMs = MotionTiming.DurationMs(eat.Frames, 1f);
            }

            if (timing.TryGetClip(MotionCode.Warp, out MotionClip warp))
            {
                WarpDurationMs = MotionTiming.DurationMs(warp.Frames, 1f);
            }

            if (timing.TryGetClip(MotionCode.Walk, out MotionClip walk))
            {
                WalkCycleMs = MotionTiming.DurationMs(walk.Frames, 1f);
            }

            if (timing.TryGetClip(MotionCode.Run, out MotionClip run))
            {
                HasRunClip = true;
                RunCycleMs = MotionTiming.DurationMs(run.Frames, 1f);
            }

            if (timing.TryGetClip(MotionCode.Attack, out MotionClip attack))
            {
                CooldownMs = MotionTiming.DurationMs(attack.Frames, 1f);
                WindupMs = attack.TryGetEventTicks(0, out uint ev)
                    ? MotionTiming.EventOffsetMs(ev, 1f)
                    : CooldownMs;
            }

            if (timing.TryGetClip(MotionCode.Damage, out MotionClip damage))
            {
                FlinchDurationMs = MotionTiming.DurationMs(damage.Frames, MotionCodes.DamageAndEatRate);
            }

            if (timing.TryGetClip(MotionCode.Dead, out MotionClip dead))
            {
                DieDurationMs = MotionTiming.DurationMs(dead.Frames, 1f);
            }
        }
    }

    // Explicit fallback for databases without imported locomotion clips, not a claim about every model.
    public const float LocomotionCycleFallbackMs = 1000f;
    public float WalkCycleMs { get; }
    public float RunCycleMs { get; }

    public bool CanCastOffensiveSkill { get; }

    // Every attack and skill clip of the model in table order; a swing plays one drawn at random, as the original did.
    public IReadOnlyList<MonsterActionClip> AttackClips { get; } = [];
    public IReadOnlyList<MonsterActionClip> SkillClips { get; } = [];

    // Drinking a potion and the transformation clip; a missing clip lasts one second.
    public float EatDurationMs { get; } = LocomotionCycleFallbackMs;
    public float WarpDurationMs { get; } = LocomotionCycleFallbackMs;

    private static MonsterActionClip[] ActionClips(IReadOnlyList<MotionClip> clips, bool lateHit)
    {
        var result = new MonsterActionClip[clips.Count];
        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index];
            var duration = MotionTiming.DurationMs(clip.Frames, 1f);
            var hit = clip.TryGetEventTicks(0, out var ticks) ? MotionTiming.EventOffsetMs(ticks, 1f) : duration;
            if (lateHit)
            {
                hit = Math.Min(duration, MonsterSkillRules.LateSkillHitMs);
            }

            result[index] = new MonsterActionClip(clip.Slot, clip.Key, duration, hit);
        }

        return result;
    }
    public float SkillDurationMs { get; }
    public float SkillWindupMs { get; }

    public long SightSq { get; }
    public long LeashSq { get; }
    public long RunDistanceSq { get; }
    public long AttackRangeSq { get; }
    public int AttackRange { get; }
    public int StopDistance { get; }
    public bool IsImmobile { get; }
    public long StuckDistanceSq { get; }
    public float VerticalAggroGateF { get; }
    public bool HasRunClip { get; }

    // Clever monsters (IQ 6 and up) run while chasing, given a run clip.
    public bool CanRun => HasRunClip && _clever;

    private readonly bool _clever;

    public float WindupMs { get; }

    public float CooldownMs { get; }

    public float FlinchDurationMs { get; }

    public const float FlinchFallbackMs = 700f;

    public float DieDurationMs { get; }

    // Without an attack clip: a one-second swing at attack speed 6, shortened by the attack speed's play rate, hitting
    // at frame 40 of 72.
    private const float FallbackSwingMs = 1000f;
    private const float FallbackHitFraction = 40f / 72f;

    private static (float windup, float cooldown) DeriveAttackTiming(int attackSpeed)
    {
        var cooldown = FallbackSwingMs * GameUnits.FixedOne / AnimationRate.AttackSpeedMainFrame(attackSpeed);
        return (cooldown * FallbackHitFraction, cooldown);
    }
}
