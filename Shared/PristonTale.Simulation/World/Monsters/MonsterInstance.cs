using System.Numerics;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Motion;

namespace PristonTale.Simulation.World.Monsters;

public sealed class MonsterInstance
{
    public MonsterInstance(ulong entityId, MonsterDefinition definition, Vector3 home, float facing, int spawnPointIndex)
    {
        EntityId = entityId;
        Def = definition ?? throw new ArgumentNullException(nameof(definition));
        Position = home;
        HomeAnchor = home;
        Facing = facing;
        SpawnPointIndex = spawnPointIndex;
        Life = definition.Combat.MaxLife;
        Motion = MotionState.Walk;
        AiState = MonsterAiState.Wander;
        ProgressProbePosition = home;
        MoveTarget = home;
    }

    public ulong EntityId { get; }

    public MonsterDefinition Def { get; }

    public Vector3 Position { get; set; }

    public float Facing { get; set; }

    public int Life { get; set; }

    // Life as a whole percent of the definition's maximum; null for a definition without one.
    public int? LifePercent => Def.Combat.MaxLife is > 0 and var max ? (int)(Life * 100L / max) : null;

    public MotionState Motion { get; set; }

    public MonsterAiState AiState { get; set; }

    public Vector3 HomeAnchor { get; }

    public int SpawnPointIndex { get; }

    public Vector3 MoveTarget { get; set; }

    public ulong TargetId { get; set; }

    public float SinceDecisionMs { get; set; }

    public float SinceRetargetMs { get; set; }

    public float TargetLockMsLeft { get; set; }

    public float AttackTimerMs { get; set; }

    public bool AttackHitFired { get; set; }

    public bool AttackIsSkill { get; set; }

    public ulong SkillTargetId { get; set; }
    public float SkillFacing { get; set; }
    public Vector3 SkillOrigin { get; set; }
    public Vector3 SkillCenter { get; set; }
    public bool SkillImpactPending { get; set; }

    public bool SwingJustBegan { get; set; }

    public Vector3 ProgressProbePosition { get; set; }

    // Locomotion clip ends since the last progress probe and the running fraction of the current clip.
    public int ProbeClips { get; set; }

    public float ClipProgress { get; set; }

    // Clips left on a fixed-heading leash or stuck manoeuvre; the target is kept meanwhile.
    public int ManoeuvreClipsLeft { get; set; }

    public float ManoeuvreHeading { get; set; }

    // Set by the first swing on the current target; it widens the distance at which the chase may run again.
    public bool Engaged { get; set; }

    // Set by the host after collision: walk and run are published only for ticks where the monster really moved.
    public bool MovedThisTick { get; set; }

    // Set by the host when other monsters, not the stage, stopped the step; a queue is not a stuck monster.
    public bool Crowded { get; set; }

    public bool CrowdedSinceProbe { get; set; }

    public MotionState PresentedMotion => Motion.IsMoving() && !MovedThisTick ? MotionState.Stand : Motion;

    public float SpawnWalkElapsedMs { get; set; }

    public float FlinchMsLeft { get; set; }

    public float IdleDespawnMsLeft { get; set; }

    public bool IsAlive => Life > 0 && AiState != MonsterAiState.Dead;

    // Debuffs and buffs from any source (player skills, auras); the field steps their timers.
    public EffectContainer Effects { get; } = new();

    // Summons: the player that called the monster, the calling skill, the field-clock time it expires, whether it picks
    // its own targets and its model size (0 keeps the catalog's). Wild monsters have no owner.
    public ulong OwnerId { get; set; }
    public uint SummonSkill { get; set; }
    public long ExpiresAtMs { get; set; }
    public bool Aggressive { get; set; }
    public float ModelScale { get; set; }
    public bool IsSummon => OwnerId != 0;

    // A monster a game master called up drops nothing when it dies.
    public bool DropsDisabled { get; set; }

    // A quest duel opponent fights only the player it was opened for; MinimumLife keeps it from dying before it yields
    // or drinks its last potion.
    public ulong DuelPlayerId { get; set; }
    public bool DuelYielded { get; set; }
    public int Potions { get; set; }
    public int MinimumLife { get; set; }

    // Monster skills: the clip playing for the current swing (slot and key, -1 without a clip), the blow times of that
    // clip, the lives of the bosses that carry them, the blows dealt so far (anger), the monster a power-lending skill aims
    // at, a finished potion or transformation clip the host completes, and the time alive.
    public int ActionSlot { get; set; } = -1;
    public char ActionKey { get; set; }
    public float ActionDurationMs { get; set; }
    public float ActionHitAtMs { get; set; }
    public float SecondHitAtMs { get; set; }
    public MonsterActionClip AttackVariant { get; set; }
    public int VirtualLives { get; set; }
    public int VirtualLivesMax { get; set; }
    public int LivesSpent { get; set; }
    public int AttackAnger { get; set; }
    public ulong AllyTargetId { get; set; }

    // The hammer event: blows landed on the same player in a row, and whether the monster already gave its big head.
    public ulong HammerTargetId { get; set; }
    public int HammerHits { get; set; }
    public bool HammerSpent { get; set; }

    // An event creature leaving at dawn: its warp clip ends by removing it.
    public bool VanishOnWarp { get; set; }

    // A night creature stays until dawn even with no player near.
    public bool StaysUntilDawn { get; set; }
    public float SpecialMsLeft { get; set; }
    public MotionState CompletedSpecial { get; set; }
    public bool DrinkHeals { get; set; }
    public float AliveMs { get; set; }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Life -= amount;
        if (MinimumLife > 0 && Life < MinimumLife)
        {
            Life = MinimumLife;
        }

        if (MonsterLifeRules.TrySpendVirtualLife(this))
        {
            LivesSpent++;
            return;
        }

        if (Life <= 0)
        {
            Life = 0;
            SkillImpactPending = false;
            AiState = MonsterAiState.Dead;
            Motion = MotionState.Dead;
        }
    }
}
