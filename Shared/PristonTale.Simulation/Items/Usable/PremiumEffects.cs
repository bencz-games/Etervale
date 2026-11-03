using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Items.Usable;

// The premium timers that change a stat feed the effect container as permanent system effects, so skill costs and the
// damage scaling read them like any other modifier: Mana Reduce and Terry lower skill mana costs, Mute raises the damage
// its owner deals, boosters raise the maximum life, mana and stamina.
public static class PremiumEffects
{
    public const uint ManaReduceCode = 0xF0000101;
    public const uint MuteCode = 0xF0000102;
    public const uint BoosterCode = 0xF0000103;

    public static void Sync(PlayerCharacter character)
    {
        var premium = character.Premium;
        var manaPercent = premium.ValueOf(PremiumEffectKind.ManaReduce)
            + (premium.IsActive(PremiumEffectKind.HelpPetTerry) ? PremiumRules.TerryManaReducePercent : 0);
        Set(character, ManaReduceCode, manaPercent > 0 ? [StatModifier.Percent(StatKind.SkillManaCost, -manaPercent)] : []);
        Set(character, MuteCode, premium.IsActive(PremiumEffectKind.HelpPetMute)
            ? [StatModifier.Percent(StatKind.DamageDealt, PremiumRules.MuteDamagePercent)]
            : []);
        var boosters = new List<StatModifier>(3);
        AddBooster(boosters, premium, PremiumEffectKind.LifeBooster, StatKind.MaxLife);
        AddBooster(boosters, premium, PremiumEffectKind.ManaBooster, StatKind.MaxMana);
        AddBooster(boosters, premium, PremiumEffectKind.StaminaBooster, StatKind.MaxStamina);
        Set(character, BoosterCode, boosters.ToArray());
    }

    // A booster raises only the maximum; the current value stays until it regenerates.
    private static void AddBooster(List<StatModifier> modifiers, PremiumTimers premium, PremiumEffectKind kind, StatKind stat)
    {
        if (premium.ValueOf(kind) is var percent and > 0)
        {
            modifiers.Add(StatModifier.Percent(stat, percent));
        }
    }

    private static void Set(PlayerCharacter character, uint code, StatModifier[] modifiers) =>
        character.Effects.SetPermanent(code, EffectSourceKind.System, false, modifiers.Length == 0 ? null : modifiers);
}
