using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Items.Usable;

public sealed record ItemScrollDefinition(ScrollEffectKind Kind, string Name, uint StatusCode, int DurationSeconds, string KeepIcon)
{
    public EffectSpec Spec() => ScrollEffects.Spec(Kind, StatusCode, ItemScrolls.Bonus);
}

// The effect of a scroll, premium or castle: invulnerable at half the damage dealt, or a critical or evasion bonus.
public static class ScrollEffects
{
    public const int InvulnerableDamagePercent = -50;

    public static EffectSpec Spec(ScrollEffectKind kind, uint statusCode, int bonus) => kind switch
    {
        ScrollEffectKind.Invulnerability => new EffectSpec(statusCode, EffectSourceKind.Item, false,
            [StatModifier.Percent(StatKind.DamageDealt, InvulnerableDamagePercent)]),
        ScrollEffectKind.Critical => new EffectSpec(statusCode, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.CriticalChance, bonus)]),
        _ => new EffectSpec(statusCode, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.Evasion, bonus)])
    };
}

// Eternal Life, Fatal Edge and Avert Scroll: timed item effects shown in the running-effects bar.
public static class ItemScrolls
{
    // The critical and evasion bonus of the premium scrolls.
    public const int Bonus = 15;

    public static IReadOnlyList<ItemScrollDefinition> All { get; } =
    [
        new(ScrollEffectKind.Invulnerability, "Eternal Life", 0xF0000001, 30, KeepIcons.Path("S_Ath.tga")),
        new(ScrollEffectKind.Critical, "Fatal Edge", 0xF0000002, 3600, KeepIcons.Path("S_Dea.tga")),
        new(ScrollEffectKind.Evasion, "Avert Scroll", 0xF0000003, 3600, KeepIcons.Path("S_Eva.tga"))
    ];

    public static ItemScrollDefinition For(ScrollEffectKind kind) => All.First(scroll => scroll.Kind == kind);

    private static readonly Dictionary<uint, ItemScrollDefinition> ByStatus = ForceOrbs.ByStatusCode(All, static scroll => scroll.StatusCode);

    public static bool TryGetByStatus(uint code, out ItemScrollDefinition scroll) => ByStatus.TryGetValue(code, out scroll);

    // Fatal Edge and Avert Scroll exclude each other; every scroll refuses while its own effect runs.
    public static ScrollEffectKind? Excluded(ScrollEffectKind kind) => kind switch
    {
        ScrollEffectKind.Critical => ScrollEffectKind.Evasion,
        ScrollEffectKind.Evasion => ScrollEffectKind.Critical,
        _ => null
    };
}
