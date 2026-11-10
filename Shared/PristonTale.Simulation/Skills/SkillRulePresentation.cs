using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Skills;

// How an entity looks while a skill's status is on it. Frost, Poison and Distortion are afflictions and show only
// while the status is harmful (an ice enchant tints the monster it slowed, never its caster).
public enum SkillLook
{
    None,
    // Blue tint growing over 160 ticks and translucency, fading back after the status ends.
    Frost,
    // Green tint growing over 100 ticks.
    Poison,
    // Red-yellow tint and a wobbling mesh.
    Distortion,
    // Translucent body, no ground shadow.
    Vanish,
    // Translucent body with a scrolling overlay texture.
    Vague
}

public enum SkillSoundOrder
{
    // The n-th impact of a cast plays entry n, wrapping.
    Sequence,
    Random,
    // Only the cast's last impact plays a sound (the first entry).
    Final
}

// Presentation fields of a rule that change how actors look and sound; the server ignores them.
public sealed partial record SkillRule
{
    // Impact sounds by impact of the cast; empty plays ImpactSound on every impact.
    public string[] ImpactSounds { get; init; } = [];
    public SkillSoundOrder ImpactSoundOrder { get; init; }
    // Impact effects by impact of the cast (the n-th impact plays entry n, the last entry after that); an empty entry or
    // list plays ImpactEffect. Silent ("-") in either list plays nothing at that impact.
    public const string Silent = "-";
    public string[] ImpactEffects { get; init; } = [];
    public SkillLook StatusLook { get; init; }
    // Red glow (0..255 of light) on the caster at each impact, fading by 70 a second.
    public int RageGlow { get; init; }
    // Off-hand model (asset path) drawn instead of a held shield while the status lasts.
    public string ShieldModel { get; init; } = "";
    // Percent growth by rank of a red, translucent copy of the weapon drawn over it during the cast's attack clip.
    public int[] WeaponGrowth { get; init; } = [];
    // Afterimages trailing the caster through the cast's skill clip.
    public int Afterimages { get; init; }
    // During the cast the held shield moves to this bone of the body (a thrown or swung shield) and glows by this light
    // (0..255 per channel).
    public string CastShieldBone { get; init; } = "";
    public int[] CastShieldGlow { get; init; } = [];
    // Two effects (a light and its trail) circling the held weapon on either side while the status lasts.
    public string[] WeaponOrbit { get; init; } = [];
    // Each impact shakes the camera of the players within CameraShakeReach of the caster, by (reach - distance) / 15.
    public bool CameraShake { get; init; }
    public const int CameraShakeReach = 400;
    // An effect on each enemy an area struck; the server reports each with a Struck impact.
    public string TargetEffect { get; init; } = "";
    // An effect on the holder each time its buff defends it: an absorbing shield takes its share, a reflection strikes
    // back, a guard returns life, or, for a buff of stats alone, a hit reaches the holder from a monster of DefenseBroods
    // (from any attacker, players too, when empty). The server reports each with a Defended impact.
    public string DefenseEffect { get; init; } = "";
    public MonsterBrood[] DefenseBroods { get; init; } = [];
    // While the status lasts the holder's basic hits show this effect on the struck target instead of the weapon's spark
    // ("<effect>.<element>" for the element the status drew, when defined).
    public string HitEffect { get; init; } = "";
    // While the status lasts the holder's thrown basic attacks fly as this effect, from the weapon to the target, instead
    // of the weapon itself.
    public string ShotEffect { get; init; } = "";
}

public static partial class SkillRuleCatalog
{
    private static bool PresentationValid(SkillRule rule) =>
        rule.ImpactSounds.All(static sound => sound.Length > 0) && rule.RageGlow is >= 0 and <= 255 && Ranked(rule.WeaponGrowth)
        && rule.WeaponGrowth.All(static percent => percent >= 0) && rule.Afterimages is >= 0 and <= 8 && Enum.IsDefined(rule.StatusLook)
        && Enum.IsDefined(rule.ImpactSoundOrder) && rule.CastShieldGlow.Length is 0 or 3
        && (rule.WeaponOrbit.Length == 0 || rule.WeaponOrbit.Length == 2 && rule.WeaponOrbit.All(static name => name.Length > 0))
        && (rule.DefenseEffect.Length == 0 && rule.HitEffect.Length == 0 && rule.ShotEffect.Length == 0 || rule.Buff is not null)
        && (rule.DefenseBroods.Length == 0 || rule.DefenseEffect.Length > 0 && rule.Buff is { StatsOnly: true }) && rule.DefenseBroods.All(Enum.IsDefined);
}
