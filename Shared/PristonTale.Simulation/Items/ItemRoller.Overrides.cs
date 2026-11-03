using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Items;

// How a game master's item command shapes the copy: a forced class specialization (when the item can take it) and the
// middle mode, which rolls every whole-number range without its lowest and highest quarter.
public sealed record ItemRollOverrides(uint ForcedSpecJob = 0, bool Middle = false)
{
    public static readonly ItemRollOverrides None = new();
}

public static partial class ItemRoller
{
    public static bool CanTakeSpec(ItemDefinition definition, uint job)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return job == 0 || definition.Template is { } template && (template.SpecJob == job || template.RandomSpecJobs.Contains(job));
    }

    // A dropped copy with a game master's overrides; without them (None) the roll draws exactly as an ordinary drop.
    public static ItemInstance Create(ItemDefinition definition, int count, ISimRng rng, ItemRollOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(overrides);
        var item = new ItemInstance(definition, count);
        var template = definition.Template;
        if (template is null)
        {
            return item;
        }

        var middle = overrides.Middle;
        var forced = CanTakeSpec(definition, overrides.ForcedSpecJob) ? overrides.ForcedSpecJob : 0u;
        if (template.Durability.Min != 0)
        {
            var maximum = template.Durability.Max != 0 ? Range(rng, template.Durability, middle) : template.Durability.Min;
            item.DurabilityMax = maximum;
            item.DurabilityCurrent = Range(rng, new ItemRange(maximum / 2, maximum), middle);
        }

        var resistances = new short[8];
        for (var element = 0; element < resistances.Length; element++)
        {
            var range = element < template.Resistances.Count ? template.Resistances[element] : default;
            resistances[element] = (short)(range.Max != 0 ? Range(rng, range, middle) : range.Min);
        }

        int damageMin = 0;
        int damageMax = 0;
        if (template.DamageMin.Max != 0 || template.DamageMax.Max != 0)
        {
            damageMin = Range(rng, template.DamageMin, middle);
            damageMax = Range(rng, template.DamageMax, middle);
        }

        var attackRating = Pick(rng, template.AttackRating, middle);
        var absorb = Tenths(rng, template.Absorb);
        var defence = Pick(rng, template.Defence, middle);
        var block = Hundredths(rng, template.Block);
        var speed = Tenths(rng, template.MoveSpeed);
        var manaRegen = Tenths(rng, template.ManaRegen);
        var lifeRegen = Tenths(rng, template.LifeRegen);
        var staminaRegen = Tenths(rng, template.StaminaRegen);
        var addLife = Pick(rng, template.AddLife, middle);
        var addMana = Pick(rng, template.AddMana, middle);
        var addStamina = Pick(rng, template.AddStamina, middle);

        var specJob = 0u;
        var price = definition.Price;
        var requirements = definition.Requirements;
        var bonus = ItemSpecBonus.None;
        // A forced specialization always specializes the copy; the draws of the ordinary roll still happen.
        if (template.CanSpecialize && (rng.NextExclusive(0, 10) <= 3 || forced != 0))
        {
            var mask = template.SpecJob;
            var chosen = mask;
            if (rng.NextExclusive(0, 10) < 3 && template.RandomSpecJobs.Count > 0)
            {
                mask = 0;
            }

            if (mask == 0 && template.RandomSpecJobs.Count > 0)
            {
                chosen = template.RandomSpecJobs[rng.NextExclusive(0, template.RandomSpecJobs.Count)];
                mask = chosen;
            }

            if (forced != 0)
            {
                chosen = forced;
                mask = forced;
            }

            if (mask != 0)
            {
                price = SpecializedPrice(price);
            }

            requirements = Shift(requirements, chosen, range => Range(rng, range, middle));
            var spec = template.Spec;
            var specAbsorb = Hundredths(rng, spec.Absorb);
            var specDefence = Pick(rng, spec.Defence, middle);
            var specSpeed = Hundredths(rng, spec.MoveSpeed);
            var mastery = Hundredths(rng, spec.MagicMastery);
            var specLife = spec.LifeRegenRange.Max != 0 ? Hundredths(rng, spec.LifeRegenRange) : spec.LifeRegen;
            var specMana = Hundredths(rng, spec.ManaRegen);
            var levelRating = Pick(rng, spec.LevelAttackRating, middle);
            specJob = mask;
            if (mask != 0)
            {
                bonus = new ItemSpecBonus(specAbsorb, specDefence, specSpeed, spec.Block, spec.AttackSpeed, spec.Critical, spec.ShootingRange,
                    mastery, spec.LevelLife, spec.LevelMana, levelRating, spec.LevelDamage, specLife, specMana, spec.StaminaRegen);
            }
        }

        // Magic mastery rolls whole points. It draws last, so the other values of a seeded roll do not move.
        var masteryBonus = Pick(rng, Points(template.MagicMastery), middle) - template.MagicMastery.Min;
        item.Roll = new ItemRoll(damageMin, damageMax, attackRating, defence, absorb, block, speed, lifeRegen, manaRegen, staminaRegen,
            addLife, addMana, addStamina,
            new ResistanceBlock(resistances[0], resistances[1], resistances[2], resistances[3], resistances[4], resistances[5], resistances[6],
                resistances[7]),
            definition.RequiredLevel, requirements, price, specJob, bonus) { MagicMasteryBonus = masteryBonus };
        return item;
    }

    // The best copy every range allows. specJob 0 keeps only the item's authored specialization; another class must be one
    // the item can take (null otherwise), and costs a fifth more. The requirements move by the first bound of the class's
    // shift, and a level divisor takes its smallest value that grants a bonus.
    public static ItemInstance CreatePerfect(ItemDefinition definition, int count, uint specJob)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!CanTakeSpec(definition, specJob))
        {
            return null;
        }

        var item = new ItemInstance(definition, count);
        var template = definition.Template;
        if (template is null)
        {
            return item;
        }

        if (template.Durability.Min != 0)
        {
            item.DurabilityMax = template.Durability.Max != 0 ? Top(template.Durability) : template.Durability.Min;
            item.DurabilityCurrent = item.DurabilityMax;
        }

        var resistances = new short[8];
        for (var element = 0; element < resistances.Length; element++)
        {
            var range = element < template.Resistances.Count ? template.Resistances[element] : default;
            resistances[element] = (short)(range.Max != 0 ? Top(range) : range.Min);
        }

        var weapon = template.DamageMin.Max != 0 || template.DamageMax.Max != 0;
        var mask = specJob != 0 ? specJob : template.SpecJob;
        var price = specJob != 0 ? SpecializedPrice(definition.Price) : definition.Price;
        var requirements = mask != 0 ? Shift(definition.Requirements, mask, static range => range.Min) : definition.Requirements;
        var bonus = ItemSpecBonus.None;
        if (mask != 0)
        {
            var spec = template.Spec;
            var levelRating = spec.LevelAttackRating.Max != 0 ? Math.Max(1, spec.LevelAttackRating.Min) : spec.LevelAttackRating.Min;
            bonus = new ItemSpecBonus(TopHundredths(spec.Absorb), spec.Defence.Max != 0 ? Top(spec.Defence) : spec.Defence.Min,
                TopHundredths(spec.MoveSpeed), spec.Block, spec.AttackSpeed, spec.Critical, spec.ShootingRange, TopHundredths(spec.MagicMastery),
                spec.LevelLife, spec.LevelMana, levelRating, spec.LevelDamage,
                spec.LifeRegenRange.Max != 0 ? TopHundredths(spec.LifeRegenRange) : spec.LifeRegen, TopHundredths(spec.ManaRegen), spec.StaminaRegen);
        }

        var mastery = Points(template.MagicMastery);
        var masteryBonus = (mastery.Max != 0 ? Top(mastery) : mastery.Min) - template.MagicMastery.Min;
        item.Roll = new ItemRoll(weapon ? Top(template.DamageMin) : 0, weapon ? Top(template.DamageMax) : 0,
            template.AttackRating.Max != 0 ? Top(template.AttackRating) : template.AttackRating.Min,
            template.Defence.Max != 0 ? Top(template.Defence) : template.Defence.Min,
            TopTenths(template.Absorb), TopHundredths(template.Block), TopTenths(template.MoveSpeed), TopTenths(template.LifeRegen),
            TopTenths(template.ManaRegen), TopTenths(template.StaminaRegen),
            template.AddLife.Max != 0 ? Top(template.AddLife) : template.AddLife.Min,
            template.AddMana.Max != 0 ? Top(template.AddMana) : template.AddMana.Min,
            template.AddStamina.Max != 0 ? Top(template.AddStamina) : template.AddStamina.Min,
            new ResistanceBlock(resistances[0], resistances[1], resistances[2], resistances[3], resistances[4], resistances[5], resistances[6],
                resistances[7]),
            definition.RequiredLevel, requirements, price, mask, bonus) { MagicMasteryBonus = masteryBonus };
        return item;
    }

    // The middle mode drops a quarter of the span from each end (at least one when the span is two or more).
    private static int Range(ISimRng rng, ItemRange range, bool middle)
    {
        if (!middle || range.Max <= range.Min)
        {
            return Range(rng, range);
        }

        var span = range.Max - range.Min;
        var trim = span / 4;
        if (trim == 0 && span >= 2)
        {
            trim = 1;
        }

        return Range(rng, new ItemRange(range.Min + trim, range.Max - trim));
    }

    private static int Pick(ISimRng rng, ItemRange range, bool middle) => range.Max != 0 ? Range(rng, range, middle) : range.Min;

    // The highest value a roll of the range can produce; a reversed range keeps its lower bound plus the span below it.
    private static int Top(ItemRange range) => range.Max >= range.Min ? range.Max : range.Min + Math.Abs(range.Max + 1 - range.Min) - 1;

    // The whole points of a range authored in whole numbers (magic mastery).
    private static ItemRange Points(ItemFloatRange range) => new((int)range.Min, (int)range.Max);

    private static float TopTenths(ItemFloatRange range) => TopSteps(range, 10f);

    private static float TopHundredths(ItemFloatRange range) => TopSteps(range, 100f);

    private static float TopSteps(ItemFloatRange range, float steps)
    {
        if (range.Max == 0)
        {
            return range.Min;
        }

        var span = (int)((range.Max - range.Min) * steps);
        return span <= 0 ? range.Max : range.Min + span / steps;
    }

    // A copy specialized for a class costs a fifth more.
    private static int SpecializedPrice(int price) => price + price * 2 / 10;
}
