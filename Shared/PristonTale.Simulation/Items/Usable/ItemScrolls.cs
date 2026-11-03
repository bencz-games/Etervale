using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Items.Usable;

public sealed record ItemScrollDefinition(ScrollEffectKind Kind, string Name, uint StatusCode, int DurationSeconds, string KeepIcon)
{
    public EffectSpec Spec() => Kind switch
    {
        // Invulnerable for the duration, at half the damage dealt.
        ScrollEffectKind.Invulnerability => new EffectSpec(StatusCode, EffectSourceKind.Item, false, [StatModifier.Percent(StatKind.DamageDealt, -50)]),
        ScrollEffectKind.Critical => new EffectSpec(StatusCode, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.CriticalChance, 15)]),
        _ => new EffectSpec(StatusCode, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.Evasion, 15)])
    };
}

// Eternal Life, Fatal Edge and Avert Scroll: timed item effects shown in the running-effects bar.
public static class ItemScrolls
{
    public static IReadOnlyList<ItemScrollDefinition> All { get; } =
    [
        new(ScrollEffectKind.Invulnerability, "Eternal Life", 0xF0000001, 30, "image/Sinimage/skill/keep/S_Ath.tga"),
        new(ScrollEffectKind.Critical, "Fatal Edge", 0xF0000002, 3600, "image/Sinimage/skill/keep/S_Dea.tga"),
        new(ScrollEffectKind.Evasion, "Avert Scroll", 0xF0000003, 3600, "image/Sinimage/skill/keep/S_Eva.tga")
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
