using System.Text.Json;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

// How a skill resolves on the server. The values come from the class skill files in Skills/Data/Classes (Etervale's
// own data, shared by the server and the client tooltips); every per-rank table has ten entries (rank 1..10).
public enum SkillKind
{
    Passive,
    // Weapon swings or shots at one enemy with the skill's damage and accuracy bonuses; every hit rolls accuracy.
    WeaponAttack,
    // A magic bolt at one enemy rolled from the skill's table, weapon damage and spirit; it never misses.
    MagicBolt,
    // Damage to every enemy in a circle or a box; area hits never miss and never critically hit.
    Area,
    // A timed modifier effect on the caster.
    SelfBuff,
    // A timed modifier effect on the target enemy.
    Debuff,
    // Restores the life of the caster or another player.
    Heal,
    // Converts a share of the caster's maximum life into mana.
    LifeToMana,
    // Revives the nearest dead player in range with a chance per rank.
    Resurrect,
    // Turns a monster of the listed broods to the caster's side for a time (success rolled per rank).
    Charm,
    // Calls an owned monster that follows the caster and fights for it until it expires or dies (see SkillSummonRule).
    Summon
}

public enum SkillTarget
{
    None,
    Self,
    Enemy,
    // The caster or another player; no target heals the caster.
    Ally
}

public enum SkillAreaShape
{
    // A circle around the area centre.
    Circle,
    // A box in front of the caster toward the target: Width wide, Length long.
    Box,
    // A chain from the target to the nearest enemy not yet hit, each link at most Radius long; MaxTargetsByRank links.
    Chain
}

public enum SkillAreaCenter
{
    Caster,
    Target
}

public enum SkillMotion
{
    // A clip of the class's skill motions (MotionIndex of the body's motion table).
    Skill,
    // The equipped weapon's attack clip, repeated Loops times.
    Attack
}

public sealed partial record SkillModifierRule
{
    public StatKind Stat { get; init; }
    public ModifierOp Op { get; init; }
    public int[] Values { get; init; } = [];
    // When present the value is rolled at cast time between Values and ValuesMax (inclusive).
    public int[] ValuesMax { get; init; } = [];
}

public sealed partial record SkillBuffRule
{
    // Duration per rank in tenths of a second.
    public int[] DurationTenths { get; init; } = [];
    public SkillModifierRule[] Modifiers { get; init; } = [];
    public ControlFlags Control { get; init; }
    public bool Harmful { get; init; }
    // The target's bionic resistance shortens the duration: duration x (100 - resistance) / 100.
    public bool ResistedByBionic { get; init; }
    // Resource changes every IntervalMs while the effect lasts (poison, regeneration).
    public SkillPeriodicRule[] Periodic { get; init; } = [];
    // A familiar that strikes while the buff lasts, and a counter strike when the buff blocks.
    public SkillCompanionRule Companion { get; init; }
    public SkillCounterRule Counter { get; init; }
    // Stealth-like buffs: the next single-target hit consumes the effect; any area attack of the holder ends it.
    public bool EndsOnHit { get; init; }
    public bool EndsOnAreaAttack { get; init; }
    // A control effect any damage to its holder ends (a stun that hits break).
    public bool EndsOnDamage { get; init; }
    // The modifiers count only while the holder's main hand holds a weapon the skill lists.
    public bool WeaponBound { get; init; }
}

public sealed record SkillPeriodicRule
{
    public ResourceKind Resource { get; init; }
    // Amount per tick rolled once per application between Min and Max (negative drains); per rank.
    public int[] Min { get; init; } = [];
    public int[] Max { get; init; } = [];
    public float IntervalMs { get; init; }
    public bool PercentOfMax { get; init; }
    public bool NonLethal { get; init; }
}

public sealed partial record SkillDamageRule
{
    // Weapon attacks: the rolled attack damage gains Percent; Min..Max rolled per hit is added.
    // Bolts and areas: Min..Max is the base range.
    public int[] Percent { get; init; } = [];
    public int[] Min { get; init; } = [];
    public int[] Max { get; init; } = [];
    // Level / LevelDivisor added to both ends of the range (only to the maximum with LevelOnMax).
    public int LevelDivisor { get; init; }
    public int[] LevelDivisorByRank { get; init; } = [];
    public bool LevelOnMax { get; init; }
    // Magic scaling: weapon damage / WeaponDivisor and spirit / SpiritMinDivisor (min) and / SpiritMaxDivisor (max).
    public int WeaponDivisor { get; init; }
    public int SpiritMinDivisor { get; init; }
    public int SpiritMaxDivisor { get; init; }

    // A spell: its power grows with the caster's spirit or spell power, so the target's magic resistance applies to it.
    public bool Magic => SpellPower || SpiritMinDivisor > 0 || SpiritMaxDivisor > 0 || SpiritMinDivisorByRank.Length > 0 || SpiritMaxDivisorByRank.Length > 0;

    // Areas: percent of the rolled power each target in the area receives, plus MainPercent more for the main target.
    public int AreaPercent { get; init; } = 100;
    public int MainPercent { get; init; }
    // Multi-hit bolts: the power is multiplied by a count rolled between Hits / 2 + 1 and Hits.
    public int[] RandomHits { get; init; } = [];
    public Element Element { get; init; } = Element.Bionic;
    public bool NoCritical { get; init; }
    // Element resistance reduces only the Min..Max part added to a weapon hit, not the whole hit.
    public bool ResistAddedOnly { get; init; }
    // Weapon hits that skip the accuracy roll.
    public bool NeverMiss { get; init; }
    // Weapon rolls whose maximum also gains strength / StrengthMaxDivisor.
    public int StrengthMaxDivisor { get; init; }
    // A second percent applied after Percent (each step truncates).
    public int[] PercentAfter { get; init; } = [];
    // Bolts and areas: the weapon term (WeaponDivisor) uses the caster's attack damage instead of the weapon's own.
    public bool WeaponFromAttack { get; init; }
    // Extra percent against monster broods ("Undead", "Demon", "Mutant", "Normal", "Mechanic").
    public MonsterBrood[] BonusBroods { get; init; } = [];
    public int BonusPercent { get; init; }
}

// What a learned passive skill adds (see PassiveSkills). Values are per rank.
public enum PassiveEffect
{
    // Flat resistance to Element.
    Resistance,
    // Percent of the equipped weapon's own damage range while the main hand holds a weapon the skill lists.
    WeaponDamagePercent,
    // Percent of the computed maximum stamina, truncated once.
    MaxStaminaPercent,
    // Critical chance points while the main hand holds a listed weapon.
    CriticalChance,
    // Block chance points while the main hand holds a listed weapon (WithoutShield: and the off hand no shield).
    BlockChance,
    // Flat maximum life.
    MaxLife,
    // Percent of the equipped weapon's own attack rating, with a listed weapon.
    WeaponAttackRatingPercent,
    // Points of chance to avoid a monster's normal attack.
    Evasion
}

// A learned passive: one PassiveEffect with its per-rank Values, or permanent Modifiers (optionally only while the main
// hand holds one of Weapons, see PassiveSkillEffects).
public sealed record SkillPassiveRule
{
    public PassiveEffect Effect { get; init; }
    public Element Element { get; init; }
    public int[] Values { get; init; } = [];
    public bool WithoutShield { get; init; }
    // The weapon qualifies when it is made for the holder's class instead of by the skill's weapon list.
    public bool ClassWeapon { get; init; }
    public SkillModifierRule[] Modifiers { get; init; } = [];
    public Simulation.Items.ItemKind[] Weapons { get; init; } = [];
}

// An owned, server-driven ally: a monster definition of the world catalog with per-rank combat values. It follows its
// owner, attacks its owner's target (or, when aggressive, the nearest enemy in sight), is attacked back by what it hits
// and expires after LifetimeMs. One summon per owner: a new one replaces the old.
public sealed record SkillSummonRule
{
    // The catalog monster (its key, else its display name) that gives the model, motions, sight, reach and attack speed.
    public string Monster { get; init; } = "";
    public int LifetimeMs { get; init; }
    // Per-rank values; an empty table keeps the definition's value.
    public int[] DamageMin { get; init; } = [];
    public int[] DamageMax { get; init; } = [];
    public int[] AttackRating { get; init; } = [];
    public int[] Defence { get; init; } = [];
    public int[] Life { get; init; } = [];
    // Owner attributes added once at the call: life + strength x LifePerStrength, defence + agility x DefencePerAgility.
    public int LifePerStrength { get; init; }
    public int DefencePerAgility { get; init; }
    // Model size in 1/256 steps: ScaleBase + ScalePerRank x (rank - 1); 0 keeps the catalog size.
    public int ScaleBase { get; init; }
    public int ScalePerRank { get; init; }
    // Aggressive summons pick the nearest enemy in sight; passive ones only join their owner's fight. The owner toggles it.
    public bool Aggressive { get; init; }
    // Movement compared with the catalog monster, in percent.
    public int MoveSpeedPercent { get; init; } = 100;
}

// Critical chance a weapon skill borrows from another learned skill: Values indexed by that skill's rank, / Divisor.
public sealed record SkillBorrowedCritical
{
    public uint Skill { get; init; }
    public int[] Values { get; init; } = [];
    public int Divisor { get; init; } = 1;
}

// Attack rating percent a weapon skill's hits from FromHit on (1-based) borrow from another learned skill: Values indexed
// by that skill's rank (none while it is not learned).
public sealed record SkillBorrowedRating
{
    public uint Skill { get; init; }
    public int[] Values { get; init; } = [];
    public int FromHit { get; init; } = 1;
}

// A burst around the caster that follows one hit of a weapon skill: the hit's rolled attack damage with the skill's
// percent, scaled by Percent, to every enemy within Radius (element resistance and absorption apply, no critical).
public sealed record SkillBurstRule
{
    public int AfterHit { get; init; }
    public int Radius { get; init; }
    public int Percent { get; init; } = 100;
    public Element Element { get; init; } = Element.Bionic;
}

// Hold-to-charge skills: the clip plays at the rank's charge speed while the caster holds the button through the
// window StartFrame..FullFrame; a release before FullFrame jumps the clip to FullFrame (from StartFrame at the
// earliest) and leaves the charge partial. The hits are the plan's events from FullFrame on, played at ReleaseSpeed;
// a full charge uses FullPercent in place of the damage percent.
public sealed record SkillChargeRule
{
    public int StartFrame { get; init; }
    public int FullFrame { get; init; }
    public int ReleaseSpeed { get; init; }
    public int MaxHits { get; init; }
    // How a full charge resolves instead of the rule itself (a stronger hit, or a line area).
    public SkillRule Full { get; init; }
}

// A familiar that strikes on its own while its buff lasts (a falcon, orbs, a phantom sword, a spirit): every IntervalMs
// it hits the owner's target (only while the owner fights, OwnerTarget) or the nearest enemy within Radius of the holder,
// never missing and never critical. Damage is Min..Max per rank, or another learned skill's table (BorrowSkill: the
// borrowed skill's Min/Max at its rank, scaled by Percent per rank), or one of Choices drawn at the cast (with its
// element and slow) plus the weapon / WeaponDivisor and spirit terms.
public sealed record SkillCompanionRule
{
    public int[] IntervalMs { get; init; } = [];
    // Flight speeds (units per second) to the target and back; the next strike waits for both flights.
    public int[] FlightSpeeds { get; init; } = [];
    public bool OwnerTarget { get; init; }
    public int Radius { get; init; } = 300;
    public SkillEnchantChoice[] Choices { get; init; } = [];
    public int WeaponDivisor { get; init; }
    public int SpiritMinDivisor { get; init; }
    public int SpiritMaxDivisor { get; init; }
    public int[] Min { get; init; } = [];
    public int[] Max { get; init; } = [];
    public uint BorrowSkill { get; init; }
    public int[] Percent { get; init; } = [];
    public Element Element { get; init; } = Element.Bionic;
    public MonsterBrood[] BonusBroods { get; init; } = [];
    public int BonusPercent { get; init; }
}

// A shield buff that strikes back when it blocks a monster's normal attack: Percent per rank of the BorrowSkill's
// Min..Max table at that skill's learned rank (nothing when it is not learned), never missing.
public sealed record SkillCounterRule
{
    public uint BorrowSkill { get; init; }
    public int[] Percent { get; init; } = [];
    public Element Element { get; init; } = Element.Bionic;
    public MonsterBrood[] BonusBroods { get; init; } = [];
    public int BonusPercent { get; init; }
}

public sealed partial record SkillRule
{
    public uint Code { get; init; }
    public string Name { get; init; } = "";
    public SkillKind Kind { get; init; }
    public SkillTarget Target { get; init; }
    // The skill's mastery requirement: base and per rank (cooldown gauge units).
    public int MasteryBase { get; init; }
    public int MasteryPerRank { get; init; }
    // Cast range in world units from the caster to the target; empty uses the weapon's attack range.
    public int[] Range { get; init; } = [];
    public SkillAreaShape AreaShape { get; init; } = SkillAreaShape.Circle;
    public SkillAreaCenter AreaCenter { get; init; } = SkillAreaCenter.Target;
    // Circle radius, or box length, per rank.
    public int[] Radius { get; init; } = [];
    public int BoxWidth { get; init; }
    public int MaxTargets { get; init; } = 41;
    // Per-rank target limit; empty uses MaxTargets.
    public int[] MaxTargetsByRank { get; init; } = [];
    // Areas around the caster whose radius is the distance to the clicked target plus this pad.
    public int RadiusTargetPad { get; init; }
    // Cast range from the weapon reach r: r + (r - 50) x ReachPercent / 100; a box area is that long plus BoxLengthPad.
    public int[] ReachPercent { get; init; } = [];
    // Flat range added to the weapon reach per rank.
    public int[] ReachBonus { get; init; } = [];
    public int BoxLengthPad { get; init; }
    public int[] AttackRating { get; init; } = [];
    // Also adds the caster's level to the attack rating (Farina).
    public bool AttackRatingPlusLevel { get; init; }
    public int[] AttackRatingPercent { get; init; } = [];
    public int[] CriticalAdd { get; init; } = [];
    public SkillDamageRule Damage { get; init; }
    public SkillBuffRule Buff { get; init; }
    // A second timed effect the caster gains when the skill resolves (a debuff skill that also empowers its caster).
    public SkillBuffRule CasterBuff { get; init; }
    public SkillPassiveRule Passive { get; init; }
    public SkillSummonRule Summon { get; init; }
    public int[] HealMin { get; init; } = [];
    public int[] HealMax { get; init; } = [];
    // Area targets are pushed away from the caster to this distance.
    public int[] PushTo { get; init; } = [];
    // Weapon attacks: tenths of a percent of max life paid per landed hit. LifeToMana: percent converted.
    public int[] LifeCost { get; init; } = [];
    public SkillMotion Motion { get; init; } = SkillMotion.Skill;
    public int MotionIndex { get; init; }
    public int MotionFrames { get; init; }
    // Event frames of the clip; each one is a hit (skill motions) or the impact of the single effect.
    public int[] MotionEvents { get; init; } = [];
    // Per-rank hit frames of the clip when the hit count grows with the rank; replaces MotionEvents.
    public int[][] HitFramesByRank { get; init; } = [];
    // Weapon skills: a hit after a miss of this cast is skipped; listed hits (1-based) cannot be critical; ForceFlinch
    // makes every hit able to flinch the target.
    public bool StopAfterMiss { get; init; }
    public int[] NoCriticalHits { get; init; } = [];
    // Listed hits (1-based) skip the accuracy roll.
    public int[] NoAccuracyHits { get; init; } = [];
    public bool ForceFlinch { get; init; }
    // Relative critical chance increase: critical + critical x percent / 100.
    public int[] CriticalPercent { get; init; } = [];
    public SkillBorrowedCritical BorrowedCritical { get; init; }
    public SkillBorrowedRating BorrowedRating { get; init; }
    public SkillBurstRule Burst { get; init; }
    public SkillChargeRule Charge { get; init; }
    // Areas: a weapon hit with this damage rule on the clicked target besides the area share; accuracy rolled per area
    // target (a miss still takes a target slot); a critical roll per area target.
    public SkillDamageRule MainHit { get; init; }
    // The clicked target always joins the area list, without an accuracy roll and beyond the target limit.
    public bool MainInArea { get; init; }
    // Weapon skills: an area (its own rule; the skill's effect) that strikes with every hit from FollowAreaFromHit (1-based)
    // on, after the weapon hit on the target.
    public SkillRule FollowArea { get; init; }
    public int FollowAreaFromHit { get; init; } = 1;
    public bool AreaAccuracy { get; init; }
    public bool AreaCritical { get; init; }
    // Area targets are knocked this far away from the caster (bosses excepted) and flinch.
    public int PushBy { get; init; }
    // Areas that make every enemy hit target the caster for the buff's duration (bosses excepted).
    public bool Taunt { get; init; }
    // One of these resolutions is picked at random per cast (an element chosen per cast); timing stays the rule's.
    public SkillRule[] Modes { get; init; } = [];
    // A skill the caster must have learned for the cast (its table feeds this one).
    public uint RequiresSkill { get; init; }
    // The cast needs body armor made for the caster's class only.
    public bool RequiresClassArmor { get; init; }
    // Weapon-clip skills: percent of the motion rate (a slower shot).
    public int MotionRatePercent { get; init; } = 100;
    // Per-rank fixed frame step, overriding MotionSpeed (a skill that ignores the attack speed).
    public int[] MotionSpeedByRank { get; init; } = [];
    // Frame step of the original (80 = attack rate 1); 0 follows the caster's attack speed.
    public int MotionSpeed { get; init; }
    // Attack-speed steps added to the weapon clip (one step = 32/256 faster).
    public int[] AttackSpeedSteps { get; init; } = [];
    // Weapon clip repeats per rank (one hit each).
    public int[] Loops { get; init; } = [];
    public bool Projectile { get; init; }
    // Height of a lobbed projectile's arc at mid flight (presentation).
    public float ProjectileArc { get; init; }
    public string CastEffect { get; init; } = "";
    public string ImpactEffect { get; init; } = "";
    public string ProjectileEffect { get; init; } = "";
    public string BuffEffect { get; init; } = "";
    public string CastSound { get; init; } = "";
    public string ImpactSound { get; init; } = "";
    public string KeepIcon { get; init; } = "";
    // The effect and sound of a familiar's strike or a counter strike, from the caster to the struck target.
    public string CompanionEffect { get; init; } = "";
    public string CompanionSound { get; init; } = "";

    public int At(int[] table, int rank) => SkillFormulas.At(table, rank);
}

public static partial class SkillRuleCatalog
{
    public static int Count => Rules.Count;

    public static IEnumerable<SkillRule> All => Rules.Values;

    public static bool TryGet(uint code, out SkillRule rule) => Rules.TryGetValue(code, out rule);

    public static JsonSerializerOptions JsonOptions => SkillJson.Options;

    private static IReadOnlyDictionary<uint, SkillRule> Rules => ClassSkillCatalog.Rules;

    private static bool Ranked(int[] table) => table is null || table.Length is 0 or SkillLearning.MaxRank;

    private static bool ValidSummon(SkillSummonRule summon) => summon is not null && summon.Monster.Length > 0 && summon.LifetimeMs > 0
        && Ranked(summon.DamageMin) && Ranked(summon.DamageMax) && Ranked(summon.AttackRating) && Ranked(summon.Defence) && Ranked(summon.Life)
        && summon.DamageMin.Length == summon.DamageMax.Length && summon.ScaleBase >= 0;

    internal static void Validate(SkillRule rule)
    {
        var ok = rule.Code != 0 && rule.Name.Length > 0 && rule.MasteryBase >= 0 && rule.MasteryPerRank >= 0 && WellFormed(rule)
            && (rule.HitFramesByRank.Length == 0 || rule.HitFramesByRank.Length == SkillLearning.MaxRank && rule.HitFramesByRank.All(row => row.Length > 0
                && row.All(frame => frame > 0 && (rule.MotionFrames == 0 || frame <= rule.MotionFrames))))
            && rule.Modes.All(WellFormed) && (rule.Charge is null || ValidCharge(rule))
            && (rule.FollowArea is null || rule.Kind == SkillKind.WeaponAttack && rule.FollowAreaFromHit >= 1 && rule.FollowArea.Kind == SkillKind.Area
                && WellFormed(rule.FollowArea) && Complete(rule.FollowArea));
        if (!ok || !MechanicsValid(rule) || !PresentationValid(rule))
        {
            throw new InvalidDataException($"Skill rule {rule.Code:X8} ({rule.Name}) is malformed.");
        }

        var castable = rule.Kind != SkillKind.Passive;
        if (!Complete(rule) || rule.Modes.Any(mode => !Complete(mode))
            || rule.Kind == SkillKind.Passive && (rule.Passive is null || rule.Passive.Values.Length != SkillLearning.MaxRank && rule.Passive.Modifiers.Length == 0)
            || rule.Kind == SkillKind.Summon && !ValidSummon(rule.Summon)
            || rule.Kind == SkillKind.Charm && (rule.Charm is null || rule.Buff is null) || rule.Kind == SkillKind.Resurrect && rule.Resurrect is null
            || rule.Kind == SkillKind.Heal && (rule.HealMin.Length != SkillLearning.MaxRank || rule.HealMax.Length != SkillLearning.MaxRank)
            || castable && rule.Motion == SkillMotion.Skill
                && (rule.MotionFrames == 0 || rule.MotionEvents.Length == 0 && rule.ExtraEvents.Length == 0 && rule.HitFramesByRank.Length == 0))
        {
            throw new InvalidDataException($"Skill rule {rule.Code:X8} ({rule.Name}) lacks the data its kind needs.");
        }
    }

    // Every per-rank table of a rule (or of a mode or a charged resolution) has ten entries.
    private static bool WellFormed(SkillRule rule) =>
        Ranked(rule.Range) && Ranked(rule.Radius) && Ranked(rule.AttackRating) && Ranked(rule.AttackRatingPercent) && Ranked(rule.CriticalAdd)
        && Ranked(rule.CriticalPercent) && Ranked(rule.HealMin) && Ranked(rule.HealMax) && Ranked(rule.PushTo) && Ranked(rule.LifeCost)
        && Ranked(rule.AttackSpeedSteps) && Ranked(rule.Loops) && Ranked(rule.MaxTargetsByRank) && Ranked(rule.ReachPercent) && Ranked(rule.ReachBonus)
        && Ranked(rule.MotionSpeedByRank) && rule.MaxTargets is > 0 and <= 64 && rule.MotionFrames >= 0 && rule.MotionSpeed >= 0
        && rule.MotionRatePercent is > 0 and <= 400
        && rule.MotionEvents.All(frame => frame > 0 && (rule.MotionFrames == 0 || frame <= rule.MotionFrames))
        && WellFormed(rule.Damage) && WellFormed(rule.MainHit) && WellFormed(rule.Buff) && WellFormed(rule.CasterBuff)
        && (rule.BorrowedCritical is null || rule.BorrowedCritical.Values.Length == SkillLearning.MaxRank && rule.BorrowedCritical.Divisor > 0)
        && (rule.BorrowedRating is null || rule.BorrowedRating.Values.Length == SkillLearning.MaxRank && rule.BorrowedRating.FromHit >= 1);

    private static bool WellFormed(SkillDamageRule damage) => damage is null || Ranked(damage.Percent) && Ranked(damage.PercentAfter)
        && Ranked(damage.Min) && Ranked(damage.Max) && Ranked(damage.RandomHits) && Ranked(damage.LevelDivisorByRank) && Ranked(damage.BonusPercentByRank)
        && damage.Min.Length == damage.Max.Length;

    private static bool WellFormed(SkillBuffRule buff) => buff is null || buff.DurationTenths.Length == SkillLearning.MaxRank && buff.DurationTenths.All(value => value > 0)
        && buff.Modifiers.All(modifier => modifier.Values.Length == SkillLearning.MaxRank && Ranked(modifier.ValuesMax))
        && buff.Periodic.All(entry => entry.IntervalMs > 0f && entry.Min.Length == SkillLearning.MaxRank && entry.Max.Length == SkillLearning.MaxRank)
        && (buff.Companion is null || buff.Companion.IntervalMs.Length == SkillLearning.MaxRank && buff.Companion.IntervalMs.All(value => value > 0)
            && (buff.Companion.BorrowSkill != 0
                ? buff.Companion.Percent.Length == SkillLearning.MaxRank
                : buff.Companion.Choices.Length > 0 || buff.Companion.Min.Length == SkillLearning.MaxRank && buff.Companion.Max.Length == SkillLearning.MaxRank)
            && (buff.Companion.OwnerTarget || buff.Companion.Radius > 0))
        && (buff.Counter is null || buff.Counter.BorrowSkill != 0 && buff.Counter.Percent.Length == SkillLearning.MaxRank);

    // The data a rule's kind resolves with: damage for attacks, an area for areas, an effect for buffs.
    private static bool Complete(SkillRule rule)
    {
        var areaSized = rule.Radius.Length == SkillLearning.MaxRank || rule.RadiusTargetPad > 0
            || rule.AreaShape == SkillAreaShape.Box && (rule.ReachPercent.Length == SkillLearning.MaxRank || rule.ReachBonus.Length == SkillLearning.MaxRank);
        return rule.Kind switch
        {
            SkillKind.SelfBuff or SkillKind.Debuff => rule.Buff is not null,
            SkillKind.WeaponAttack or SkillKind.MagicBolt => rule.Damage is not null,
            SkillKind.Area => areaSized && (rule.Damage is not null || rule.Buff is not null || rule.Taunt || rule.CasterBuff is not null),
            _ => true
        };
    }

    private static bool ValidCharge(SkillRule rule)
    {
        var charge = rule.Charge;
        return charge.StartFrame > 0 && charge.FullFrame > charge.StartFrame && charge.ReleaseSpeed > 0 && rule.MotionSpeedByRank.Length == SkillLearning.MaxRank
            && rule.MotionEvents.Any(frame => frame >= charge.FullFrame) && (charge.Full is null || WellFormed(charge.Full) && Complete(charge.Full));
    }
}
