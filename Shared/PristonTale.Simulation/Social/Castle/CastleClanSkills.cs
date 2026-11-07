using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Social.Castle;

// The castle's clan skill on the members of the clan holding Bless Castle: the skill in force (CastleClanSkill) runs on
// each of them for LastSeconds, renewed every RenewSeconds while the clan holds the castle; a player whose clan does not
// hold it, or that holds another skill than the one in force, loses it at the next renewal. Absorb takes AbsorbBonus off
// every blow received (flat absorption), Damage adds DamagePercent to the damage dealt, Evasion evades EvasionPercent of
// the normal blows.
public static class CastleClanSkills
{
    public const uint StatusBase = 0xF0000440;
    public const int RenewSeconds = 60;
    public const int LastSeconds = 600;
    public const int AbsorbBonus = 20;
    public const int DamagePercent = 15;
    public const int EvasionPercent = 20;

    private static readonly StatModifier[] AbsorbModifiers = [StatModifier.Flat(StatKind.Absorption, AbsorbBonus)];
    private static readonly StatModifier[] DamageModifiers = [StatModifier.Percent(StatKind.DamageDealt, DamagePercent)];
    private static readonly StatModifier[] EvasionModifiers = [StatModifier.Flat(StatKind.Evasion, EvasionPercent)];

    public static readonly CastleClanSkill[] All = [CastleClanSkill.Absorb, CastleClanSkill.Damage, CastleClanSkill.Evasion];

    public static uint StatusCode(CastleClanSkill skill) => StatusBase + (uint)skill;

    public static bool TryGetByStatus(uint code, out CastleClanSkill skill)
    {
        skill = (CastleClanSkill)(code - StatusBase);
        return code > StatusBase && skill != CastleClanSkill.None && Enum.IsDefined(skill);
    }

    public static EffectSpec Spec(CastleClanSkill skill) => new(StatusCode(skill), EffectSourceKind.System, false, skill switch
    {
        CastleClanSkill.Absorb => AbsorbModifiers,
        CastleClanSkill.Damage => DamageModifiers,
        _ => EvasionModifiers
    });

    public static string Name(CastleClanSkill skill) => skill switch
    {
        CastleClanSkill.Absorb => "Absorb Up",
        CastleClanSkill.Damage => "Attack Up",
        _ => "Evasion Up"
    };

    public static string Icon(CastleClanSkill skill) => KeepIcons.Path(skill switch
    {
        CastleClanSkill.Absorb => "wa1-1.tga",
        CastleClanSkill.Damage => "wa1-3.tga",
        _ => "wa1-2.tga"
    });
}
