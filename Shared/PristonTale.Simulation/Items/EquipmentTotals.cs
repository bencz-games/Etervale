using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.Items;

// What the usable equipped items add to a character, the class specialization bonuses included. Absorb sums each
// copy's rated absorb cut to one decimal; AbsorbItems counts the copies that carry an absorb of their own.
public sealed record EquipmentTotals
{
    private static readonly EquipmentSlot[] Slots = Enum.GetValues<EquipmentSlot>();

    public static readonly EquipmentTotals None = new();

    public int AttackRating { get; init; }
    public int LevelAttackRating { get; init; }
    public int DamageMin { get; init; }
    public int DamageMax { get; init; }
    public int LevelDamage { get; init; }
    public int Critical { get; init; }
    public int Defence { get; init; }
    public float Absorb { get; init; }
    public int AbsorbItems { get; init; }
    public float Block { get; init; }
    public float MoveSpeed { get; init; }
    public int AttackSpeed { get; init; }
    public int ShootingRange { get; init; }
    public float LifeRegen { get; init; }
    public float ManaRegen { get; init; }
    public float StaminaRegen { get; init; }
    public float AddLife { get; init; }
    public float AddMana { get; init; }
    public float AddStamina { get; init; }
    public int LevelLife { get; init; }
    public int LevelMana { get; init; }
    public float MagicMastery { get; init; }

    // Zero when no worn item sets one; the character then keeps the default belt capacity.
    public int PotionSpace { get; init; }
    public ResistanceBlock Resistances { get; init; }

    public static EquipmentTotals Sum(PlayerCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var job = character.Definition.JobBitCode;
        var level = character.Level;
        var totals = new Accumulator();
        Dictionary<int, (ItemSetDefinition Set, int Worn)> sets = null;
        foreach (var slot in Slots)
        {
            if (character.GetEquipped(slot) is not { } item || !ItemUsability.CanUse(character, item))
            {
                continue;
            }

            if (item.Definition.Set is { } set)
            {
                sets ??= new Dictionary<int, (ItemSetDefinition, int)>();
                sets[set.SetCode] = (set, sets.TryGetValue(set.SetCode, out var worn) ? worn.Worn + 1 : 1);
            }

            var roll = item.Roll;
            totals.AttackRating += roll.AttackRating;
            totals.DamageMin += roll.DamageMin;
            totals.DamageMax += roll.DamageMax;
            totals.Critical += item.Critical;
            totals.Defence += roll.Defence;
            totals.Absorb += Tenth(ItemAbsorbCorrection.Rated(item));
            totals.AbsorbItems += roll.Absorb != 0 ? 1 : 0;
            totals.Block += roll.Block;
            totals.MoveSpeed += roll.MoveSpeed;
            totals.AttackSpeed += item.Definition.BaseStats.AttackSpeed + roll.AttackSpeedBonus;
            totals.ShootingRange += item.Definition.ShootingRange;
            totals.LifeRegen += roll.LifeRegen;
            totals.ManaRegen += roll.ManaRegen;
            totals.StaminaRegen += roll.StaminaRegen;
            // The last worn item with a magic mastery sets it; the specialization bonuses add to that.
            if (item.MagicMastery != 0)
            {
                totals.MagicMastery = item.MagicMastery;
            }

            totals.AddLife += roll.AddLife;
            totals.AddMana += roll.AddMana;
            totals.AddStamina += roll.AddStamina;
            if (item.Definition.PotionSpace != 0)
            {
                totals.PotionSpace = item.Definition.PotionSpace;
            }

            if (roll.SpecializedFor(job))
            {
                var spec = roll.Spec;
                totals.SpecAbsorb += Tenth(spec.Absorb);
                totals.AbsorbItems += spec.Absorb != 0 ? 1 : 0;
                totals.Defence += spec.Defence;
                totals.MoveSpeed += spec.MoveSpeed;
                totals.Block += spec.Block;
                totals.AttackSpeed += spec.AttackSpeed;
                totals.Critical += spec.Critical;
                totals.ShootingRange += spec.ShootingRange;
                totals.SpecMagicMastery += spec.MagicMastery;
                totals.LevelMana += spec.LevelMana != 0 ? level / spec.LevelMana : 0;
                totals.LevelLife += spec.LevelLife != 0 ? level / spec.LevelLife : 0;
                totals.LevelAttackRating += spec.LevelAttackRating != 0 ? level / spec.LevelAttackRating : 0;
                totals.LevelDamage += spec.LevelDamage != 0 ? level / spec.LevelDamage : 0;
                totals.SpecLifeRegen += spec.LifeRegen / 2f;
                totals.SpecManaRegen += spec.ManaRegen / 2f;
                totals.SpecStaminaRegen += spec.StaminaRegen / 2f;
            }

            totals.Resistances = totals.Resistances.Add(roll.Resistances);
        }

        // A set gives its bonus once its pieces are worn (each ring counts).
        foreach (var (set, worn) in sets?.Values ?? Enumerable.Empty<(ItemSetDefinition, int)>())
        {
            if (worn >= set.Pieces)
            {
                totals.AddLife += set.Life;
                totals.Absorb += set.Absorb;
                totals.Block += set.Block;
                totals.LifeRegen += set.LifeRegen;
                totals.ManaRegen += set.ManaRegen;
                totals.StaminaRegen += set.StaminaRegen;
            }
        }

        return totals.Build();
    }

    // Each copy's absorb is cut to one decimal before it is summed, with a tiny bias against float error.
    private static float Tenth(float absorb) => (int)(absorb * 10.000001f) / 10f + 0.000001f;

    private sealed class Accumulator
    {
        public int AttackRating;
        public int LevelAttackRating;
        public int DamageMin;
        public int DamageMax;
        public int LevelDamage;
        public int Critical;
        public int Defence;
        public float Absorb;
        public float SpecAbsorb;
        public int AbsorbItems;
        public float Block;
        public float MoveSpeed;
        public int AttackSpeed;
        public int ShootingRange;
        public float LifeRegen;
        public float ManaRegen;
        public float StaminaRegen;
        public float SpecLifeRegen;
        public float SpecManaRegen;
        public float SpecStaminaRegen;
        public float AddLife;
        public float AddMana;
        public float AddStamina;
        public int LevelLife;
        public int LevelMana;
        public float MagicMastery;
        public float SpecMagicMastery;
        public int PotionSpace;
        public ResistanceBlock Resistances;

        public EquipmentTotals Build() => new()
        {
            AttackRating = AttackRating,
            LevelAttackRating = LevelAttackRating,
            DamageMin = DamageMin,
            DamageMax = DamageMax,
            LevelDamage = LevelDamage,
            Critical = Critical,
            Defence = Defence,
            Absorb = Absorb + SpecAbsorb,
            AbsorbItems = AbsorbItems,
            Block = Block,
            MoveSpeed = MoveSpeed,
            AttackSpeed = AttackSpeed,
            ShootingRange = ShootingRange,
            LifeRegen = LifeRegen + SpecLifeRegen,
            ManaRegen = ManaRegen + SpecManaRegen,
            StaminaRegen = StaminaRegen + SpecStaminaRegen,
            AddLife = AddLife,
            AddMana = AddMana,
            AddStamina = AddStamina,
            LevelLife = LevelLife,
            LevelMana = LevelMana,
            MagicMastery = MagicMastery + SpecMagicMastery,
            PotionSpace = PotionSpace,
            Resistances = Resistances
        };
    }
}
