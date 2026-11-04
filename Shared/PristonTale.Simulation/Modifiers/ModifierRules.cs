using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.Modifiers;

// How modifier totals change the derived stats and combat values. Stats that DerivedStats does not hold (block,
// critical, resistances, damage dealt or taken) are read from the totals by the combat profiles.
public static class ModifierRules
{
    public static DerivedStats Apply(DerivedStats stats, ModifierTotals totals)
    {
        if (totals.IsEmpty)
        {
            return stats;
        }

        return stats with
        {
            MaxLife = Math.Max(1, totals.Apply(StatKind.MaxLife, stats.MaxLife)),
            MaxMana = Math.Max(0, totals.Apply(StatKind.MaxMana, stats.MaxMana)),
            MaxStamina = Math.Max(0, totals.Apply(StatKind.MaxStamina, stats.MaxStamina)),
            AttackRating = Math.Max(0, totals.Apply(StatKind.AttackRating, stats.AttackRating)),
            AttackDamageMin = Math.Max(0, totals.Apply(StatKind.DamageMin, stats.AttackDamageMin)),
            AttackDamageMax = Math.Max(0, totals.Apply(StatKind.DamageMax, stats.AttackDamageMax)),
            Defence = Math.Max(0, totals.Apply(StatKind.Defence, stats.Defence)),
            Absorption = Math.Max(0, totals.Apply(StatKind.Absorption, stats.Absorption)),
            MoveSpeed = Locomotion.StepLevel(Math.Max(0, totals.Apply(StatKind.MoveSpeed, stats.MoveSpeed)), totals.Flat(StatKind.MoveSpeedUnbound) > 0),
            AttackSpeed = totals.Apply(StatKind.AttackSpeed, stats.AttackSpeed)
        };
    }

    public static (int Min, int Max) ApplyDamage(ModifierTotals totals, int min, int max)
    {
        var low = Math.Max(0, totals.Apply(StatKind.DamageMin, min));
        var high = Math.Max(low, totals.Apply(StatKind.DamageMax, max));
        return (low, high);
    }

    public static ResistanceBlock Resistances(ModifierTotals totals, ResistanceBlock resistances)
    {
        if (totals.IsEmpty)
        {
            return resistances;
        }

        var all = totals.Flat(StatKind.ResistAll);
        return new ResistanceBlock(
            Resist(resistances.Bionic, all + totals.Flat(StatKind.ResistBionic)),
            Resist(resistances.Earth, all + totals.Flat(StatKind.ResistEarth)),
            Resist(resistances.Fire, all + totals.Flat(StatKind.ResistFire)),
            Resist(resistances.Ice, all + totals.Flat(StatKind.ResistIce)),
            Resist(resistances.Lighting, all + totals.Flat(StatKind.ResistLightning)),
            Resist(resistances.Poison, all + totals.Flat(StatKind.ResistPoison)),
            Resist(resistances.Water, all + totals.Flat(StatKind.ResistWater)),
            Resist(resistances.Wind, all + totals.Flat(StatKind.ResistWind)));
    }

    // A value changed by the percent and flat sums of one modifier (as percents), never below floor.
    public static int ChangedBy(int value, ModifierTotals totals, StatKind stat, int floor) =>
        Math.Max(floor, value + value * (totals.Percent(stat) + totals.Flat(stat)) / 100);

    // Percent change of damage dealt by the attacker and taken by the defender, applied once after resolution.
    public static int ScaleDamage(int damage, ModifierTotals attacker, ModifierTotals defender)
    {
        if (damage <= 0)
        {
            return damage;
        }

        long scaled = damage;
        var dealt = attacker.Percent(StatKind.DamageDealt) + attacker.Flat(StatKind.DamageDealt);
        var taken = defender.Percent(StatKind.DamageTaken) + defender.Flat(StatKind.DamageTaken);
        scaled = scaled * Math.Max(0, 100 + dealt) / 100;
        scaled = scaled * Math.Max(0, 100 + taken) / 100;
        return (int)Math.Clamp(scaled, 1, int.MaxValue);
    }

    // Life lost to an undead attacker drops by the defender's undead absorption percent (at least 1 remains).
    public static int AgainstUndead(int damage, bool attackerIsUndead, ModifierTotals defender)
    {
        var percent = defender.Flat(StatKind.AbsorptionVsUndead) + defender.Percent(StatKind.AbsorptionVsUndead);
        if (!attackerIsUndead || percent <= 0 || damage <= 0)
        {
            return damage;
        }

        return Math.Max(1, damage - damage * Math.Min(percent, 100) / 100);
    }

    private static short Resist(short value, int add) => (short)Math.Clamp(value + add, short.MinValue, short.MaxValue);
}
