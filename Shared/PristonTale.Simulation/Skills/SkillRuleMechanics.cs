using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

public enum SkillRangeMode
{
    // The Range table, or the weapon's attack range when the table is empty.
    Table,
    // The larger of the table and the weapon's attack range.
    AtLeastWeapon,
    // The weapon's attack range plus the table.
    AddWeapon
}

public enum SkillDamageMode
{
    // Damage rolled from the tables (and the attack power for AttackPower skills).
    Rolled,
    // Share percent of each target's maximum life.
    MaxLifeShare,
    // Share percent of each target's current life.
    CurrentLifeShare
}

// A rank range that plays another clip (more hits at higher ranks): a skill motion, or the weapon's attack clip.
public sealed record SkillMotionVariant
{
    public int FromRank { get; init; }
    public SkillMotion Motion { get; init; }
    public int MotionSpeed { get; init; }
    public int MotionIndex { get; init; }
    public int MotionFrames { get; init; }
    public int[] MotionEvents { get; init; } = [];
    public int[] ExtraEvents { get; init; } = [];
}

// Which players a support skill reaches besides its target: the other living players of the field within Radius of the
// caster (and within Height vertically), only the caster's party members when Party is set (an ally target too).
// RequiresParty refuses the cast to a player without a party.
public sealed record SkillAllyRule
{
    public int[] Radius { get; init; } = [];
    public bool IncludeCaster { get; init; } = true;
    public bool Party { get; init; }
    // With Party: the clicked player may be anyone, only the players around being of the party.
    public bool AnyClicked { get; init; }
    public bool RequiresParty { get; init; }
    // Strength of the effect on players other than the caster, in percent.
    public int OthersPercent { get; init; } = 100;
    public int Height { get; init; } = 80;
    // Players already holding one of these effects are skipped (the original refuses to stack them).
    public uint[] ExcludeWith { get; init; } = [];
}

// One element a weapon enchant can carry: damage added to hits and, for ice, a slow of the struck monster.
public sealed record SkillEnchantChoice
{
    public Element Element { get; init; }
    public int[] Min { get; init; } = [];
    public int[] Max { get; init; } = [];
    // Duration of the slow per rank in tenths of a second (shortened by ice resistance).
    public int[] SlowTenths { get; init; } = [];
    // Move and action speed change of the struck monster in percent (negative).
    public int[] SlowPercent { get; init; } = [];
}

// Element damage carried by the holder's hits while the effect lasts; one choice is drawn per cast.
public sealed record SkillEnchantRule
{
    public SkillEnchantChoice[] Choices { get; init; } = [];
    // Only basic attacks carry it; otherwise basic attacks and single-target weapon skills without an element.
    public bool BasicOnly { get; init; }
}

// Percent of each monster hit taken by the holder that a resource pays instead of life.
public sealed record SkillAbsorbRule
{
    public int[] Percent { get; init; } = [];
    public ResourceKind Resource { get; init; } = ResourceKind.Mana;
}

// Percent of a matching monster's rolled attack damage returned to it when it attacks the holder.
public sealed record SkillReflectRule
{
    public int[] Percent { get; init; } = [];
    public MonsterBrood[] Broods { get; init; } = [];
}

// Life the holder recovers from the attacks of matching monsters: a percent of the hit, on a block or on every hit.
public sealed record SkillGuardRule
{
    public int[] HealPercent { get; init; } = [];
    public MonsterBrood[] Broods { get; init; } = [];
    public bool OnBlock { get; init; }
}

// Repeated short buffs given to the allies around the holder while the holder's effect lasts.
public sealed record SkillPulseRule
{
    public int IntervalMs { get; init; }
    public int DurationTenths { get; init; }
}

// Turns a monster of the listed broods to the caster's side: success percent per rank (lowered by bionic resistance)
// and the monster's life after the charm in percent of its current life. The duration is the buff's.
public sealed record SkillCharmRule
{
    public int[] Chance { get; init; } = [];
    public int[] LifePercent { get; init; } = [];
    public MonsterBrood[] Broods { get; init; } = [];
}

// Revives the nearest dead player within Radius: success percent per rank, the life it returns with and the time it
// cannot be hurt.
public sealed record SkillResurrectRule
{
    public int[] Chance { get; init; } = [];
    public int Radius { get; init; } = 300;
    public int LifePercent { get; init; } = 50;
    public int GuardTenths { get; init; } = 50;
}

public sealed partial record SkillModifierRule
{
    // The caster's level / LevelDivisor is added to the value.
    public int LevelDivisor { get; init; }
    // The table is indexed by the caster's rank in this skill instead of the cast skill's rank (0 when unlearned).
    public uint RankOf { get; init; }
    // Strength on players other than the caster in percent; -1 follows the ally rule.
    public int OthersPercent { get; init; } = -1;
}

public sealed partial record SkillBuffRule
{
    // Resistance of this element shortens the duration (instead of bionic).
    public Element? ResistedBy { get; init; }
    // The effect ends when the holder no longer wields one of the skill's weapons.
    public bool RequiresWeapon { get; init; }
    // Boss monsters never take the effect.
    public bool SparesBosses { get; init; }
    // Only a full resistance (100) turns the effect away; a lower one does not shorten it.
    public bool FullResistanceOnly { get; init; }
    // A running copy, from any caster, is kept: a new cast neither refreshes nor stacks it.
    public bool KeptWhileRunning { get; init; }
    // Percent of a monster's maximum life the effect takes while it lasts, the monster drawn smaller (FieldSkills.Shrink).
    public int[] LifeCut { get; init; } = [];
    public SkillEnchantRule Enchant { get; init; }
    public SkillAbsorbRule Absorb { get; init; }
    public SkillReflectRule Reflect { get; init; }
    public SkillGuardRule Guard { get; init; }
    public SkillPulseRule Pulse { get; init; }

    // A helpful buff that only changes stats: no shield, reflection, guard, enchant, familiar, counter or pulse.
    public bool StatsOnly => !Harmful && Absorb is null && Reflect is null && Guard is null && Enchant is null && Companion is null && Counter is null
        && Pulse is null;

    // The resistance that shortens the effect on its target: the rule's element, else bionic for a bionic-resisted one,
    // else none.
    public int ResistanceFrom(in ResistanceBlock resistances)
    {
        var resistance = ResistedBy is { } element ? (int)resistances[element] : ResistedByBionic ? (int)resistances.Bionic : 0;
        return FullResistanceOnly && resistance < 100 ? 0 : resistance;
    }
}

public sealed partial record SkillDamageRule
{
    public SkillDamageMode Mode { get; init; } = SkillDamageMode.Rolled;
    // The caster's attack damage (weapon, attributes and modifiers) is rolled and gains Percent.
    public bool AttackPower { get; init; }
    // The attack damage alone gains this percent per rank before it joins the roll.
    public int[] AttackPowerPercent { get; init; } = [];
    // The tables gain the caster's spell power percent (an elemental spirit's boost).
    public bool SpellPower { get; init; }
    // Brood bonus per rank, applied after BonusPercent.
    public int[] BonusPercentByRank { get; init; } = [];
    // The resistance of the element is divided by this before it applies (lightning of holy bolts, meteorites).
    public int ResistanceDivisor { get; init; } = 1;
    // Element of the main target's share when it differs from the area's.
    public Element? MainElement { get; init; }
    // A separate hit on the main target, resolved with its own damage rule.
    public SkillDamageRule Main { get; init; }
    // Element damage added to a weapon hit (Min..Max of the rule), resisted by its own element.
    public Element? FlatElement { get; init; }
    // Spell scaling per rank: the caster's level x LevelPercent / 100 added to both ends, spirit / the rank's divisor
    // (0: none; empty tables use SpiritMinDivisor and SpiritMaxDivisor) and the weapon's own damage x WeaponTimes.
    public int[] LevelPercent { get; init; } = [];
    public int[] SpiritMinDivisorByRank { get; init; } = [];
    public int[] SpiritMaxDivisorByRank { get; init; } = [];
    public int[] WeaponTimesMin { get; init; } = [];
    public int[] WeaponTimesMax { get; init; } = [];
    // The Min..Max range alone gains this percent per rank, before the attack power or the other terms join it.
    public int[] TablePercent { get; init; } = [];
    // The hit carries no element: no element resistance lowers it.
    public bool Unresisted { get; init; }
    // Share modes: percent of life per rank; Chance per rank plus level / ChanceLevelDivisor. A rolled single-target
    // spell drains Share percent of its target's maximum life after the hit (bosses are spared).
    public int[] Share { get; init; } = [];
    public int[] Chance { get; init; } = [];
    public int ChanceLevelDivisor { get; init; }
    public bool ShareResistedByBionic { get; init; }
    public int MinLifePercent { get; init; }
    public MonsterBrood[] OnlyBroods { get; init; } = [];
    public MonsterBrood[] ExcludeBroods { get; init; } = [];
    // The life taken is returned to the caster, at most this percent of the caster's maximum life.
    public int LifeToCasterCapPercent { get; init; }
    // The drain after a hit takes from bosses too (it spares them otherwise).
    public bool DrainsBosses { get; init; }
}

public sealed partial record SkillRule
{
    public SkillRangeMode RangeMode { get; init; }
    // Area targets each roll the caster's accuracy (with the AttackRating table) before they are hit.
    public bool RollsAccuracy { get; init; }
    // Weapon skills that never miss (the original sends them without an accuracy roll).
    public bool NoAccuracy { get; init; }
    // Weapon skills that throw this many shots per rank: each rolls accuracy, the hits add up into one strike.
    public int[] Volley { get; init; } = [];
    // Attack-speed-driven clips: speed steps added to the caster's attack speed, then scaled by this percent.
    public int MotionSpeedSteps { get; init; }
    public int MotionSpeedPercent { get; init; } = 100;
    // Hits beyond the clip's own events (frames from the clip start) and a last hit at the clip's end.
    public int[] ExtraEvents { get; init; } = [];
    public bool EndHit { get; init; }
    // Only the first MaxHits events strike; 0 uses every event.
    public int MaxHits { get; init; }
    public SkillMotionVariant[] MotionVariants { get; init; } = [];
    // The cast is refused while one of these effects is on the caster (its own code: no recast while active).
    public uint[] Exclusive { get; init; } = [];
    // These effects end on the caster when the skill is cast.
    public uint[] Replaces { get; init; } = [];
    // The classes another player must be to take an Ally rule (any when empty).
    public Contracts.Characters.PlayerClass[] AllyClasses { get; init; } = [];
    public SkillAllyRule Allies { get; init; }
    public SkillCharmRule Charm { get; init; }
    public SkillResurrectRule Resurrect { get; init; }
    // Area skills that always strike their target, even outside the area's shape.
    public bool AreaIncludesTarget { get; init; }
    // How long a taunt holds per rank in tenths of a second; empty uses the caster buff's or the buff's duration.
    public int[] TauntTenths { get; init; } = [];
    // The cast needs one of these effects on the caster.
    public uint[] RequiresEffect { get; init; } = [];
    // Area skills: the caster's party members within the ally radius who hold one of RequiresEffect strike the same
    // area around themselves, with their own level and spirit and the caster's weapon.
    public bool PartyEcho { get; init; }

    // The rule as it plays at this rank: the motion variant of the rank replaces the clip.
    public SkillRule ForRank(int rank)
    {
        SkillMotionVariant chosen = null;
        foreach (var variant in MotionVariants)
        {
            if (rank >= variant.FromRank && (chosen is null || variant.FromRank > chosen.FromRank))
            {
                chosen = variant;
            }
        }

        return chosen is null
            ? this
            : this with
            {
                Motion = chosen.Motion, MotionSpeed = chosen.MotionSpeed > 0 ? chosen.MotionSpeed : MotionSpeed, MotionIndex = chosen.MotionIndex,
                MotionFrames = chosen.MotionFrames, MotionEvents = chosen.MotionEvents, ExtraEvents = chosen.ExtraEvents
            };
    }

    // The frames that strike: the clip's events (MaxHits of them), the extra events and the clip's end.
    public int[] HitFrames()
    {
        var events = MaxHits > 0 ? MotionEvents.Take(MaxHits) : MotionEvents;
        var frames = events.Concat(ExtraEvents).ToList();
        if (EndHit)
        {
            frames.Add(MotionFrames);
        }

        return frames.ToArray();
    }
}

public static partial class SkillRuleCatalog
{
    private static bool MechanicsValid(SkillRule rule)
    {
        var damage = rule.Damage;
        var buff = rule.Buff;
        return Ranked(rule.Volley) && Ranked(rule.TauntTenths) && rule.RequiresEffect.All(code => code != 0)
            && (!rule.PartyEcho || rule.Allies is not null && rule.RequiresEffect.Length > 0) && rule.MotionSpeedPercent is > 0 and <= 400 && rule.MotionSpeedSteps is >= 0 and <= 6
            && rule.MaxHits >= 0 && rule.ExtraEvents.All(frame => frame > 0) && rule.NoAccuracyHits.All(hit => hit > 0) && rule.Exclusive.All(code => code != 0) && rule.Replaces.All(code => code != 0)
            && rule.MotionVariants.All(variant => variant.FromRank is >= 1 and <= SkillLearning.MaxRank
                && (variant.Motion == SkillMotion.Attack || variant.MotionFrames > 0 && variant.MotionEvents.Length > 0)
                && variant.MotionEvents.All(frame => frame > 0 && (variant.MotionFrames == 0 || frame <= variant.MotionFrames))
                && variant.ExtraEvents.All(frame => frame > 0))
            && (rule.Allies is null || Ranked(rule.Allies.Radius) && rule.Allies.OthersPercent is >= 0 and <= 100)
            && (rule.Charm is null || rule.Charm.Chance.Length == SkillLearning.MaxRank && rule.Charm.LifePercent.Length == SkillLearning.MaxRank)
            && (rule.Resurrect is null || rule.Resurrect.Chance.Length == SkillLearning.MaxRank && rule.Resurrect.Radius > 0)
            && (rule.Passive is null || rule.Passive.Modifiers.All(modifier => modifier.Values.Length == SkillLearning.MaxRank))
            && (damage is null || DamageValid(damage))
            && (buff is null || BuffValid(buff));
    }

    private static bool DamageValid(SkillDamageRule damage)
    {
        return Ranked(damage.BonusPercentByRank) && Ranked(damage.Share) && Ranked(damage.Chance) && damage.ResistanceDivisor >= 1
            && Ranked(damage.LevelPercent) && Ranked(damage.SpiritMinDivisorByRank) && Ranked(damage.SpiritMaxDivisorByRank)
            && Ranked(damage.WeaponTimesMin) && Ranked(damage.WeaponTimesMax) && Ranked(damage.TablePercent) && Ranked(damage.AttackPowerPercent)
            && (damage.Mode == SkillDamageMode.Rolled || damage.Share.Length == SkillLearning.MaxRank) && (damage.Main is null || DamageValid(damage.Main));
    }

    private static bool BuffValid(SkillBuffRule buff)
    {
        static bool Choice(SkillEnchantChoice choice) => Ranked(choice.Min) && Ranked(choice.Max) && Ranked(choice.SlowPercent) && Ranked(choice.SlowTenths)
            && choice.Min.Length == choice.Max.Length && choice.SlowPercent.Length == choice.SlowTenths.Length;
        return Ranked(buff.LifeCut) && (buff.Enchant is null || buff.Enchant.Choices.Length > 0 && buff.Enchant.Choices.All(Choice))
            && (buff.Absorb is null || buff.Absorb.Percent.Length == SkillLearning.MaxRank)
            && (buff.Reflect is null || buff.Reflect.Percent.Length == SkillLearning.MaxRank)
            && (buff.Guard is null || buff.Guard.HealPercent.Length == SkillLearning.MaxRank)
            && (buff.Companion is null || buff.Companion.Choices.All(Choice))
            && (buff.Pulse is null || buff.Pulse.IntervalMs > 0 && buff.Pulse.DurationTenths > 0);
    }
}
