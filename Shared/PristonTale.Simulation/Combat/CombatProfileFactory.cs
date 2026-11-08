using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public static class CombatProfileFactory
{
    // A normal attack also gains the normal-attack weapon buffs (a share of the weapon's own damage); skills do not.
    public static AttackProfile ForPlayerBasicAttack(PlayerCharacter character, bool normalAttack = false)
    {
        ArgumentNullException.ThrowIfNull(character);
        var damage = WeaponDamage.ComputeForHit(character);
        var totals = character.Effects.Totals;
        if (normalAttack && character.GetEquipped(EquipmentSlot.Weapon) is { } weapon)
        {
            var stats = weapon.TotalStats;
            var both = totals.Flat(StatKind.NormalAttackWeaponDamage);
            var max = damage.Max + stats.DamageMax * (totals.Flat(StatKind.NormalAttackWeaponMax) + both) / 100;
            damage = (damage.Min + stats.DamageMin * both / 100, max);
        }

        var critical = WeaponCanCritical(character) ? CriticalChance(character) : 0;
        return new AttackProfile(character.Derived.AttackRating, damage.Min, damage.Max, character.Level, Element.Bionic, critical, 0, 0)
        {
            CritBonusPercent = totals.Flat(StatKind.CriticalDamage)
        };
    }

    // A staff or a phantom in hand never deals a critical hit (its wielder's normal attacks, weapon skills and spells).
    public static bool WeaponCanCritical(PlayerCharacter character) =>
        character.GetEquipped(EquipmentSlot.Weapon) is not { Definition.Kind: ItemKind.Staff or ItemKind.Phantom };

    // Items, specializations and the critical passives share one cap; buffs add beyond it.
    public static int CriticalChance(PlayerCharacter character)
    {
        var worn = Math.Min(DerivedStats.MaxCriticalChance, character.Derived.CriticalChance + PassiveSkills.CriticalChance(character));
        return Math.Max(0, worn + character.Effects.Totals.Flat(StatKind.CriticalChance));
    }

    public static AttackProfile ForMonsterBasicAttack(MonsterCombatStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new AttackProfile(
            AttackRating: stats.AttackRatingAccuracy,
            DamageMin: stats.AttackDamage.Min,
            DamageMax: stats.AttackDamage.Max,
            AttackerLevel: stats.Level,
            Element: Element.Bionic,
            CritChance: 0,
            SkillMultiplierPercent: 0,
            SkillFlatAdd: 0);
    }

    // Worn items and their specializations, the element passives, then resistance buffs.
    public static ResistanceBlock Resistances(PlayerCharacter character) =>
        ModifierRules.Resistances(character.Effects.Totals, PassiveSkills.Resistances(character.Skills).Add(character.Derived.Resistances));

    public static bool HasShield(PlayerCharacter character) =>
        character.GetEquipped(EquipmentSlot.OffHand) is { } offHand && offHand.Definition.Kind == ItemKind.Shield;

    public static DefenderProfile ForPlayerDefender(PlayerCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        DerivedStats stats = character.Derived;
        return new DefenderProfile(
            Defence: stats.Defence,
            Absorption: stats.Absorption,
            // Every worn item's block rating (weapons included) and its specialization, plus skills and buffs.
            BlockChance: Math.Max(0, stats.BlockChance + character.Effects.Totals.Flat(StatKind.BlockChance) + PassiveSkills.BlockChance(character)),
            Level: character.Level,
            Resistances: Resistances(character),
            MaxLife: character.MaxLife,
            CurrentLife: character.Life,
            IsMonster: false);
    }
}
