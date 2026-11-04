namespace PristonTale.Simulation.Modifiers;

// Every quantity a modifier may change. Items, skills, monster skills, consumables and auras all feed the same
// values; the derived-stats pass and the combat profiles read the totals. Values are integers in the stat's own unit;
// the regeneration stats are tenths of a point per second.
public enum StatKind : byte
{
    None = 0,
    MaxLife = 1,
    MaxMana = 2,
    MaxStamina = 3,
    AttackRating = 4,
    DamageMin = 5,
    DamageMax = 6,
    Defence = 7,
    Absorption = 8,
    MoveSpeed = 9,
    AttackSpeed = 10,
    BlockChance = 11,
    CriticalChance = 12,
    ResistAll = 13,
    ResistBionic = 14,
    ResistEarth = 15,
    ResistFire = 16,
    ResistIce = 17,
    ResistLightning = 18,
    ResistPoison = 19,
    ResistWater = 20,
    ResistWind = 21,
    LifeRegen = 22,
    ManaRegen = 23,
    StaminaRegen = 24,
    // Percent change of the damage this entity deals and receives, after its other modifiers.
    DamageDealt = 25,
    DamageTaken = 26,
    // Extra absorption in percent against undead attackers.
    AbsorptionVsUndead = 27,
    // Life paid per attack in tenths of a point.
    LifeCostPerAttack = 28,
    // Percent of the damage dealt that returns as life or mana.
    LifeLeech = 29,
    ManaLeech = 30,
    SkillManaCost = 31,
    // Shooting range added to ranged weapons, in world units.
    AttackRange = 32,
    // Extra percent of the damage this entity deals to undead monsters.
    DamageVsUndead = 33,
    // Percent chance to evade a monster's normal attack entirely.
    Evasion = 34,
    // Percent added to the base tables of spells that take an elemental spirit's boost.
    SpellPower = 35,
    // Percent of the wielded weapon's own damage added to the attack damage.
    WeaponDamagePercent = 36,
    // Percent change of the damage taken from monster skills (DamageTaken covers normal attacks).
    SkillDamageTaken = 37,
    // Percent change of the life restored by heals on this entity.
    HealingTaken = 38,
    // Percent change of the sight at which monsters notice this entity (stealth).
    MonsterSight = 39,
    // Percent added to the next single-target hit; the effect that grants it ends with that hit.
    NextHitDamage = 40,
    // Points added to the critical damage multiplier (170 = x1.7).
    CriticalDamage = 41,
    // Critical chance every attacker of this entity gains (a marked target).
    CriticalChanceTaken = 42,
    // Percent of the attack animation rate (faster swings).
    AttackMotionSpeed = 43,
    // Damage added to every single-target hit after its skill percent.
    HitDamage = 44,
    // Normal attacks only: percent of the weapon's own maximum added to the maximum, and of its own range added to both
    // ends.
    NormalAttackWeaponMax = 45,
    NormalAttackWeaponDamage = 46,
    // Percent of the worn body armor's defence added while it is made for the wearer's class.
    ArmorDefence = 47,
    // Damage added to every strike of the holder's familiar.
    CompanionDamage = 48,
    // Damage added to the power roll of every spell (magic force orbs).
    SpellDamage = 49,
    // Any positive value lifts the step limit of the move speed (Locomotion.StepLevel).
    MoveSpeedUnbound = 50,
    Count = 51
}

public enum ModifierOp : byte
{
    // Added to the base value.
    Flat = 0,
    // Scales the base plus flat sum: value x (100 + total percent) / 100.
    Percent = 1
}

public readonly record struct StatModifier(StatKind Stat, ModifierOp Op, int Value)
{
    public static StatModifier Flat(StatKind stat, int value) => new(stat, ModifierOp.Flat, value);

    public static StatModifier Percent(StatKind stat, int value) => new(stat, ModifierOp.Percent, value);
}

// Who produced an effect. Effects can be removed by source (unequipping an item, a dispel of skill buffs).
public enum EffectSourceKind : byte
{
    System = 0,
    Item = 1,
    PlayerSkill = 2,
    MonsterSkill = 3,
    Consumable = 4,
    Aura = 5
}

[Flags]
public enum ControlFlags : byte
{
    None = 0,
    // No movement, attacks or casts.
    Stun = 1,
    // No movement.
    Root = 2,
    // No skill casts.
    Silence = 4,
    // Movement slowed; the amount is a MoveSpeed modifier of the same effect.
    Slow = 8,
    // Not targeted by monsters.
    Hidden = 16,
    // A monster fighting for the effect's source: it attacks no player and players' attacks skip it.
    Charmed = 32
}

public enum StackRule : byte
{
    // A reapplication restarts the timer and takes the new magnitude (the original's recast behaviour).
    Refresh = 0,
    // Each application adds a stack up to MaxStacks; the timer restarts; values scale with the stack count.
    Stack = 1,
    // A reapplication only replaces the running effect when its magnitude is at least as high.
    KeepStrongest = 2,
    // Each source keeps its own copy (two players' debuffs on one monster).
    PerSource = 3
}

public enum ResourceKind : byte
{
    Life = 0,
    Mana = 1,
    Stamina = 2
}

// A repeating change of one resource: positive restores, negative drains or damages. A non-lethal drain stops at one
// life and ends the effect there.
public readonly record struct PeriodicEffect(ResourceKind Resource, int Amount, float IntervalMs, bool PercentOfMax = false, bool NonLethal = false);
