using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Skills;

// Formulas of the support, defence and multi-target mechanics shared by the server and the client tooltips.
public static partial class SkillFormulas
{
    // Interval of the original's per-second slow and charm counters.
    private const float TenthMs = 100f;

    // The wielded weapon's own damage range (the item's, not the attribute-scaled attack); zero bare-handed.
    public static (int Min, int Max) WeaponItemDamage(PlayerCharacter caster)
    {
        var weapon = caster.GetEquipped(EquipmentSlot.Weapon);
        if (weapon is null)
        {
            return default;
        }

        var stats = weapon.TotalStats;
        return (stats.DamageMin, stats.DamageMax);
    }

    // True while one of the skill's exclusive effects is on the caster.
    public static bool BlockedByActiveEffect(PlayerCharacter caster, SkillRule rule)
    {
        foreach (var code in rule.Exclusive)
        {
            if (caster.Effects.TryGet(code, out _))
            {
                return true;
            }
        }

        return false;
    }

    // True when the rule needs no effect, or one of its required effects is on the player.
    public static bool HoldsRequiredEffect(PlayerCharacter player, SkillRule rule)
    {
        foreach (var code in rule.RequiresEffect)
        {
            if (player.Effects.TryGet(code, out _))
            {
                return true;
            }
        }

        return rule.RequiresEffect.Length == 0;
    }

    // How long a taunt holds the struck monster: the rule's own table, else the caster buff's or the buff's duration.
    public static float TauntDurationMs(SkillRule rule, int rank, SkillBuffRule buff)
    {
        if (rule.TauntTenths.Length > 0)
        {
            return At(rule.TauntTenths, rank) * TenthMs;
        }

        var held = rule.CasterBuff ?? buff;
        return held is null ? 0f : BuffDurationMs(held, rank, 0);
    }

    public static bool MatchesBrood(MonsterBrood[] broods, MonsterDefinition target) => target is not null && Array.IndexOf(broods, target.Brood) >= 0;


    // What the main target's own spell hit of an area skill deals: brood bonus first, then element resistance (divided by
    // the rule's divisor), absorption in percent and the dealt/taken scaling; unlike PowerAgainst, no magic resistance.
    public static int MainSpellPowerAgainst(int power, SkillDamageRule damage, int rank, MonsterDefinition target, ModifierTotals attacker,
        ModifierTotals defender)
    {
        var value = WithBroodBonus(power, damage, target, rank);
        var resistances = ModifierRules.Resistances(defender, target.Combat.Resistances);
        var resistance = resistances[damage.Element] / Math.Max(1, damage.ResistanceDivisor);
        value = CombatResolver.ApplyResistance(value, resistance);
        value = CombatResolver.ApplyAbsorbMonster(value, target.Combat.Absorption + defender.Flat(StatKind.Absorption));
        return ModifierRules.ScaleDamage(value, attacker, defender);
    }

    // The holder's extra damage against undead monsters (a percent of the hit).
    public static int WithUndeadBonus(int damage, ModifierTotals attacker, MonsterDefinition target)
    {
        var percent = attacker.Flat(StatKind.DamageVsUndead) + attacker.Percent(StatKind.DamageVsUndead);
        return percent <= 0 || damage <= 0 || target?.Brood != MonsterBrood.Undead ? damage : damage + damage * percent / 100;
    }

    // The table value of a rank-indexed array, 0 outside 1..10.
    public static int RankValue(int[] table, int rank) => table is { Length: 10 } && rank is >= 1 and <= SkillLearning.MaxRank ? table[rank - 1] : 0;

    // A share skill's chance (with the level term, lowered by bionic resistance) and its percent of life.
    public static int ShareChance(SkillDamageRule damage, int rank, int casterLevel, int bionicResistance)
    {
        if (damage.Chance.Length != SkillLearning.MaxRank)
        {
            return 100;
        }

        var chance = RankValue(damage.Chance, rank) + (damage.ChanceLevelDivisor > 0 ? casterLevel / damage.ChanceLevelDivisor : 0);
        return CombatResolver.ApplyResistance(chance, bionicResistance);
    }

    public static int SharePercent(SkillDamageRule damage, int rank, int bionicResistance)
    {
        var share = RankValue(damage.Share, rank);
        return damage.ShareResistedByBionic ? CombatResolver.ApplyResistance(share, bionicResistance) : share;
    }

    // The charm's success chance on a monster: the table lowered by its bionic resistance.
    public static int CharmChance(SkillRule rule, int rank, int bionicResistance) =>
        CombatResolver.Shortened(RankValue(rule.Charm.Chance, rank), bionicResistance);

    // A companion strike's damage range at this rank for the drawn choice, with the weapon and spirit terms.
    public static (int Min, int Max) CompanionDamage(SkillCompanionRule companion, SkillEnchantChoice choice, int rank, PlayerCharacter caster)
    {
        var low = RankValue(choice.Min, rank);
        var high = RankValue(choice.Max, rank);
        if (companion.WeaponDivisor > 0)
        {
            var weapon = WeaponItemDamage(caster);
            low += weapon.Min / companion.WeaponDivisor;
            high += weapon.Max / companion.WeaponDivisor;
        }

        var spirit = caster.EffectiveAttributes.Spirit;
        low += companion.SpiritMinDivisor > 0 ? spirit / companion.SpiritMinDivisor : 0;
        high += companion.SpiritMaxDivisor > 0 ? spirit / companion.SpiritMaxDivisor : 0;
        return (low, Math.Max(low, high));
    }

    // Duration of an enchant's or a companion's slow on a monster: ice resistance shortens it.
    public static float SlowDurationMs(SkillEnchantChoice choice, int rank, int iceResistance) =>
        CombatResolver.Shortened(RankValue(choice.SlowTenths, rank) * TenthMs, iceResistance);
}
