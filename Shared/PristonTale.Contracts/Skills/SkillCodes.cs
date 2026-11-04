namespace PristonTale.Contracts.Skills;

// Why the server refused a cast, as the cast result carries it. Append only.
public enum SkillCastVerdict : byte
{
    Accepted = 0,
    NotLearned = 1,
    NotCastable = 2,
    Cooldown = 3,
    NotEnoughMana = 4,
    NotEnoughStamina = 5,
    InvalidTarget = 6,
    OutOfRange = 7,
    Busy = 8,
    Dead = 9,
    WeaponNotAllowed = 10,
    Stale = 11,
    InVillage = 12,
    Rejected = 13,

    // A buff that cannot be cast again while it runs.
    AlreadyActive = 14
}

// What a cast request asks: a new cast, the release of a held charge, or an order to the caster's summon.
public enum SkillCastCommand : byte
{
    Cast = 0,
    Release = 1,
    SummonPassive = 2,
    SummonAggressive = 3
}

public enum SkillEventPhase : byte
{
    Start = 1,
    Impact = 2,
    Cancel = 3,

    // A held charge was released: the motion jumps ahead; DurationMs and HitAtMs give the new remaining timeline.
    Release = 4
}

[Flags]
public enum SkillEventFlags : ushort
{
    None = 0,
    Area = 1,
    Projectile = 2,
    SelfCast = 4,

    // A chain link, a strike of the caster's familiar, a buff given to another player, a counter strike of a shield
    // buff and a fully charged resolution.
    Chain = 8,
    Companion = 16,
    Ally = 32,
    Counter = 64,
    Charged = 128,

    // Two bits: the resolution mode picked for the cast (Mode and WithMode read and set it).
    Mode1 = 256,
    Mode2 = 512,
    ModeMask = Mode1 | Mode2,

    // An enemy an area struck, for a rule that shows an effect on each (SkillRule.TargetEffect).
    Struck = 1024
}

public static class SkillEventFlagsExtensions
{
    public static int Mode(this SkillEventFlags flags) => (int)(flags & SkillEventFlags.ModeMask) >> 8;

    public static SkillEventFlags WithMode(this SkillEventFlags flags, int mode) =>
        (flags & ~SkillEventFlags.ModeMask) | (SkillEventFlags)((mode << 8) & (int)SkillEventFlags.ModeMask);
}

public enum StatusEffectOp : byte
{
    Add = 1,
    Refresh = 2,
    Remove = 3,

    // A periodic effect ticked (damage or heal over time).
    Tick = 4
}
