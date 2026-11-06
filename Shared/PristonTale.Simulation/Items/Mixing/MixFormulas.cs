namespace PristonTale.Simulation.Items.Mixing;

[Flags]
public enum MixStat : uint
{
    None = 0,
    Fire = 0x1,
    Ice = 0x2,
    Lightning = 0x4,
    Poison = 0x8,
    Organic = 0x10,
    Critical = 0x20,
    AttackRating = 0x40,
    DamageMin = 0x80,
    DamageMax = 0x100,
    AttackSpeed = 0x200,
    Absorb = 0x400,
    Defence = 0x800,
    Block = 0x1000,
    MoveSpeed = 0x2000,
    Life = 0x4000,
    Mana = 0x8000,
    Stamina = 0x10000,
    LifeRegen = 0x20000,
    ManaRegen = 0x40000,
    StaminaRegen = 0x80000
}

public readonly record struct MixBonus(MixStat Stat, float Value, bool Percent);

// Families of the item, how many of each of the nine sheltoms (Lucidy .. Celesto) the mix takes, and its bonuses.
public sealed record MixFormula(string[] Families, int[] Sheltoms, MixBonus[] Bonuses);

public static partial class MixFormulas
{
    public const int SheltomKinds = 9;

    public static IReadOnlyList<MixFormula> All => Table;

    public static MixFormula Find(string family, IReadOnlyList<int> counts) =>
        Table.FirstOrDefault(formula => Array.IndexOf(formula.Families, family) >= 0 && formula.Sheltoms.AsSpan().SequenceEqual(counts.ToArray()));

    // A percent bonus grows the copy's own value; elements always add their value.
    public static void Apply(ItemInstance item, MixFormula formula, IReadOnlyList<int> counts)
    {
        for (var kind = 0; kind < SheltomKinds; kind++)
        {
            if (counts[kind] > 0)
            {
                item.MixSheltom = kind + 1;
            }
        }

        var roll = item.Roll;
        foreach (var bonus in formula.Bonuses)
        {
            var whole = (int)bonus.Value;
            int Grow(int value) => bonus.Percent ? value + value * whole / 100 : value + whole;
            float GrowFloat(float value) => bonus.Percent ? value + value * (bonus.Value / 100f) : value + bonus.Value;
            var resistances = roll.Resistances;
            roll = bonus.Stat switch
            {
                MixStat.Fire => roll with { Resistances = resistances with { Fire = (short)(resistances.Fire + whole) } },
                MixStat.Ice => roll with { Resistances = resistances with { Ice = (short)(resistances.Ice + whole) } },
                MixStat.Lightning => roll with { Resistances = resistances with { Lighting = (short)(resistances.Lighting + whole) } },
                MixStat.Poison => roll with { Resistances = resistances with { Poison = (short)(resistances.Poison + whole) } },
                MixStat.Organic => roll with { Resistances = resistances with { Bionic = (short)(resistances.Bionic + whole) } },
                MixStat.Critical => roll with { CriticalBonus = roll.CriticalBonus + Grow(item.Definition.Critical + roll.CriticalBonus) - item.Definition.Critical - roll.CriticalBonus },
                MixStat.AttackRating => roll with { AttackRating = Grow(roll.AttackRating) },
                MixStat.DamageMin => roll with { DamageMin = Grow(roll.DamageMin) },
                MixStat.DamageMax => roll with { DamageMax = Grow(roll.DamageMax) },
                MixStat.AttackSpeed => roll with
                {
                    AttackSpeedBonus = Grow(item.Definition.BaseStats.AttackSpeed + roll.AttackSpeedBonus) - item.Definition.BaseStats.AttackSpeed
                },
                MixStat.Absorb => roll with { Absorb = GrowFloat(roll.Absorb) },
                MixStat.Defence => roll with { Defence = Grow(roll.Defence) },
                MixStat.Block => roll with { Block = GrowFloat(roll.Block) },
                MixStat.MoveSpeed => roll with { MoveSpeed = GrowFloat(roll.MoveSpeed) },
                MixStat.Life => roll with { AddLife = (int)GrowFloat(roll.AddLife) },
                MixStat.Mana => roll with { AddMana = (int)GrowFloat(roll.AddMana) },
                MixStat.Stamina => roll with { AddStamina = (int)GrowFloat(roll.AddStamina) },
                MixStat.LifeRegen => roll with { LifeRegen = GrowFloat(roll.LifeRegen) },
                MixStat.ManaRegen => roll with { ManaRegen = GrowFloat(roll.ManaRegen) },
                MixStat.StaminaRegen => roll with { StaminaRegen = GrowFloat(roll.StaminaRegen) },
                _ => roll
            };
            item.MixStats |= bonus.Stat;
        }

        item.Roll = roll;
    }
}
