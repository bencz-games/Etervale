using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Units;

namespace PristonTale.Simulation.Items.Usable;

// The pets of the premium timers and how they fight beside their owner. A helper pet (Terry, Nepsys, Io, Mute) may join
// each attack of its owner on a monster; the Phoenix strikes the owner's target while it is within 300 units, at most
// once a second. A strike lands part-way through the pet's attack and the pet rests until its attack ends.
public static class PremiumPets
{
    public const float PhoenixReach = 300f;
    public const long PhoenixCooldownMs = 1000;
    public const long HelperHitMs = 30 * 1000 / GameUnits.TicksPerSecond;
    public const long HelperEndMs = 60 * 1000 / GameUnits.TicksPerSecond;
    public const long PhoenixHitMs = 60 * 1000 / GameUnits.TicksPerSecond;
    public const long PhoenixEndMs = 100 * 1000 / GameUnits.TicksPerSecond;

    // Nepsys slows the monster it hits: ten seconds less its ice resistance, at 180/256 of its speed.
    public const uint SlowStatusCode = 0xF0000300;
    public const float SlowMs = 10_000f;
    public const int SlowSpeedPercent = 180 * 100 / 256 - 100;

    private static readonly PremiumEffectKind[] HelperPets =
        [PremiumEffectKind.HelpPetTerry, PremiumEffectKind.HelpPetNepsys, PremiumEffectKind.HelpPetIo, PremiumEffectKind.HelpPetMute];

    // The helper pet the timers run, if any.
    public static PremiumEffectKind? HelperPet(PremiumTimers timers)
    {
        foreach (var kind in HelperPets)
        {
            if (timers.IsActive(kind))
            {
                return kind;
            }
        }

        return null;
    }

    // Percent chance (0..100) that a helper pet joins an attack of its owner: low levels are helped rarely, and above
    // level 120 not at all.
    public static int HelperChance(int ownerLevel) => ownerLevel switch
    {
        <= 5 => 21,
        <= 9 => 24,
        <= 120 => 100,
        _ => 0
    };

    public static (int Min, int Max) DamageRange(PremiumEffectKind pet, int level) => pet switch
    {
        PremiumEffectKind.HelpPetTerry or PremiumEffectKind.HelpPetNepsys => (level / 2, level * 95 / 100),
        PremiumEffectKind.HelpPetIo => (level * 80 / 100 + 1, level + level * 80 / 100 + 2),
        PremiumEffectKind.HelpPetMute => (level / 2, level + level * 95 / 100),
        _ => (level / 2 + 1, level + level / 2 + 1)
    };

    public static Element? ElementOf(PremiumEffectKind pet) => pet switch
    {
        PremiumEffectKind.HelpPetTerry => Element.Fire,
        PremiumEffectKind.HelpPetNepsys => Element.Ice,
        _ => null
    };

    // One strike against a monster: the rolled power, resisted by its element, then the monster's absorption; at least 1.
    public static int Damage(PremiumEffectKind pet, int ownerLevel, DefenderProfile monster, CombatRng rng)
    {
        var (min, max) = DamageRange(pet, ownerLevel);
        var damage = rng.Range(min, max);
        if (ElementOf(pet) is { } element)
        {
            damage = Combat.CombatResolver.ApplyResistance(damage, monster.Resistances[element]);
        }

        damage -= damage * Math.Clamp(monster.Absorption, 0, 100) / 100;
        return Math.Max(1, damage);
    }

    public static EffectSpec Slow() =>
        new(SlowStatusCode, EffectSourceKind.Item, true, [StatModifier.Percent(StatKind.MoveSpeed, SlowSpeedPercent)], control: ControlFlags.Slow);

    public static float SlowDurationMs(int iceResist) => CombatResolver.Shortened(SlowMs, iceResist);
}
