using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items.Usable;

// The gameplay effect of the running premium timers. The tick expires timers before they are read.
public static class PremiumRules
{
    public const int PhoenixExperiencePercent = 20;
    public const int PhoenixDropPercent = 50;
    public const int TerryManaReducePercent = 20;
    public const int MuteDamagePercent = 10;

    // Boosters add this percent to the maximum life, mana or stamina; the skill-delay item recharges every skill in
    // half a second.
    public const int BoosterPercent = 15;
    public const int SkillDelayCooldownMs = 500;

    public static int SkillCooldownMs(int cooldownMs, PremiumTimers timers) =>
        timers.IsActive(PremiumEffectKind.SkillDelay) ? Math.Min(cooldownMs, SkillDelayCooldownMs) : cooldownMs;

    // Percent of extra experience on a kill: the EXP potion (or package) plus the Phoenix.
    public static int ExperiencePercent(PremiumTimers timers) =>
        timers.ValueOf(PremiumEffectKind.ExperienceUp) + (timers.IsActive(PremiumEffectKind.PhoenixPet) ? PhoenixExperiencePercent : 0);

    public static ulong BoostExperience(ulong experience, PremiumTimers timers)
    {
        var percent = ExperiencePercent(timers);
        if (percent <= 0)
        {
            return experience;
        }

        var total = experience + (UInt128)experience * (uint)percent / 100;
        return total > ulong.MaxValue ? ulong.MaxValue : (ulong)total;
    }

    // Percent chance of one extra drop roll from a monster that drops at all: Third Eyes (or a package) plus the Phoenix;
    // 100 or more always rolls.
    public static int ExtraDropChance(PremiumTimers timers) =>
        timers.ValueOf(PremiumEffectKind.ThirdEyes) + (timers.IsActive(PremiumEffectKind.PhoenixPet) ? PhoenixDropPercent : 0);

    // Life and mana returned by one attack that hit `hits` monsters with an average power: no chance, every attack.
    public static int VampiricLife(int strength, int power, int hits) => (int)((strength + power / 10.0) / 20.0 * (1 + hits / 10.0) * hits + 1);

    public static int VampiricExLife(int strength, int power, int hits) => (int)((strength + power / 10.0) / 15.0 * (1 + hits / 7.0) * hits + 1);

    public static int ManaRecharge(int spirit, int power, int hits) => (int)((spirit + power / 10.0) / 13.0 * (1 + hits / 10.0) * hits + 1);

    // Life the vampiric timers return for one attack (both variants add when both run).
    public static int LifeFor(PremiumTimers timers, int strength, int power, int hits)
    {
        if (hits <= 0)
        {
            return 0;
        }

        var life = timers.IsActive(PremiumEffectKind.VampiricCuspid) ? VampiricLife(strength, power, hits) : 0;
        return life + (timers.IsActive(PremiumEffectKind.VampiricCuspidEx) ? VampiricExLife(strength, power, hits) : 0);
    }

    public static int ManaFor(PremiumTimers timers, int spirit, int power, int hits) =>
        hits > 0 && timers.IsActive(PremiumEffectKind.ManaRecharging) ? ManaRecharge(spirit, power, hits) : 0;

    public static int StaminaCost(int cost, PremiumTimers timers) => Math.Max(0, cost - cost * timers.ValueOf(PremiumEffectKind.StaminaReduce) / 100);

    // Carrying capacity added by Might of Awell and the Gravity Scrolls.
    public static int WeightBonus(PremiumTimers timers) =>
        timers.ValueOf(PremiumEffectKind.MightOfAwell) + timers.GravityScrolls * UsableItemCatalog.GravityScrollWeight;

}
