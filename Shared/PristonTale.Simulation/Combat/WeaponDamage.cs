using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Combat;

public static class WeaponDamage
{
    public static (int Min, int Max) Compute(PlayerCharacter character)
    {
        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        if (weapon is null)
        {
            // Bare-hand damage already includes the modifiers through the derived stats.
            return (character.Derived.AttackDamageMin, character.Derived.AttackDamageMax);
        }

        var attributes = character.EffectiveAttributes;
        var function = character.Definition.FunctionProfile;
        // Every usable worn item adds its damage range; a held weapon the character cannot use adds nothing.
        var items = character.EquipmentTotals;
        var stats = (DamageMin: items.DamageMin, DamageMax: items.DamageMax);
        int primary;
        int divisor;
        int secondary;
        if (weapon.Definition.Kind is ItemKind.Staff or ItemKind.Phantom && function.MagicDamageFunction > 0)
        {
            primary = attributes.Spirit;
            divisor = function.MagicDamageFunction == 1 ? 150 : 170;
            secondary = attributes.Talent / 30;
        }
        else if (weapon.Definition.Kind is ItemKind.Bow or ItemKind.ThrowingWeapon)
        {
            primary = attributes.Agility;
            divisor = function.RangedDamageFunction == 1 ? 130 : 190;
            secondary = (attributes.Talent + attributes.Strength) / (function.RangedDamageFunction == 1 ? 40 : 50);
        }
        else
        {
            primary = attributes.Strength;
            divisor = function.MeleeDamageFunction switch
            {
                1 => 130,
                2 => 150,
                _ => 190
            };
            var secondaryDivisor = function.MeleeDamageFunction switch
            {
                1 => 40,
                2 => 45,
                _ => 50
            };
            secondary = (attributes.Talent + attributes.Agility) / secondaryDivisor;
        }

        var favoured = ClassProfiles.Of(character.Definition.Class).Favours(weapon);
        var jobDamage = favoured ? character.Level / 6 : 0;
        var mastery = PassiveSkills.WeaponBonus(character, weapon);
        var passive = PassiveSkillEffects.WeaponBonus(character, weapon);
        mastery = (mastery.Min + passive.Min, mastery.Max + passive.Max);
        var minimum = 2 + stats.DamageMin * (primary + divisor) / divisor + secondary + jobDamage + (stats.DamageMin + stats.DamageMax) / 16;
        var maximum = 4 + stats.DamageMax * (primary + divisor) / divisor + secondary + jobDamage + items.LevelDamage;
        return ModifierRules.ApplyDamage(character.Effects.Totals, minimum + mastery.Min, maximum + mastery.Max);
    }

    // A percent of the weapon's own damage range, each end truncated (the percent applied before the multiplication).
    public static (int Min, int Max) PercentOfWeapon(ItemInstance weapon, int percent)
    {
        var stats = weapon.TotalStats;
        return ((int)(percent / 100f * stats.DamageMin), (int)(percent / 100f * stats.DamageMax));
    }
}
