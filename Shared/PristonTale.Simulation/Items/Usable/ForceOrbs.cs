using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Items.Usable;

// One force orb: its level window, duration, added power, and the extra percent of the average attack power the
// strongest orbs add. Weapon orbs add their power to normal attacks and weapon skills; magic orbs add it to spells; a
// skill takes its tier's share (ForceOrbs.TierPercent) unless it sets its own or refuses the orb.
public sealed record ForceOrbDefinition(int Index, bool Magic, string Name, int MinimumLevel, int MaximumLevel, int DurationSeconds, int Damage, int Percent)
{
    // The status of a running orb (one orb of either family runs at a time): the base plus the icon number.
    public const uint StatusBase = 0xF0000200;

    public uint StatusCode => StatusBase + (uint)IconNumber;

    public int IconNumber => Magic ? Index + 20 : Index;

    public static bool IsOrbStatus(uint code) => code > StatusBase && code <= StatusBase + 0x40;

    public string Icon => $"image/Sinimage/skill/ForceOrb/FOS_0{IconNumber}.tga";

    public int PowerFor(int averageAttack) => Damage + (Percent > 0 && averageAttack is > 0 and < 1000 ? averageAttack * Percent / 100 : 0);

    public EffectSpec Spec(int averageAttack)
    {
        var stat = Magic ? StatKind.SpellDamage : StatKind.ForceOrbDamage;
        return new EffectSpec(StatusCode, EffectSourceKind.Item, false, [StatModifier.Flat(stat, PowerFor(averageAttack))]);
    }
}

public static class ForceOrbs
{
    // The share of an orb's power a skill of the tier takes, in percent; the fifth tier takes the fourth's.
    public static int TierPercent(JobTier tier) => tier switch
    {
        JobTier.Job1 => 80,
        JobTier.Job2 => 90,
        JobTier.Job3 => 100,
        _ => 150
    };

    private static readonly string[] Names = ["Lucidy", "Sereneo", "Fadeo", "Sparky", "Raident", "Transparo", "Murky", "Devine", "Celesto", "Mirage",
        "Inferna", "Enigma"];
    private static readonly int[] Seconds = [500, 500, 500, 500, 700, 900, 1800, 2400, 3600, 3600, 3600, 3600];
    private static readonly int[] Damage = [2, 4, 7, 10, 15, 25, 40, 60, 80, 100, 120, 140];
    private static readonly int[] Percent = [0, 0, 0, 0, 0, 0, 10, 10, 10, 10, 15, 20];
    private static readonly (int Min, int Max)[] WeaponLevels = [(1, 19), (1, 29), (1, 39), (1, 49), (50, 200), (50, 200), (50, 200), (50, 200),
        (50, 200), (50, 200), (50, 200), (50, 200)];
    private static readonly (int Min, int Max)[] MagicLevels = [(1, 19), (1, 29), (1, 39), (1, 49), (50, 200), (50, 200), (60, 200), (70, 200),
        (80, 200), (80, 200), (90, 200), (90, 200)];

    private static readonly Dictionary<string, ForceOrbDefinition> ByKey = Build();

    public static bool TryGet(string catalogKey, out ForceOrbDefinition orb)
    {
        orb = null;
        return !string.IsNullOrEmpty(catalogKey) && ByKey.TryGetValue(catalogKey, out orb);
    }

    public static IEnumerable<KeyValuePair<string, ForceOrbDefinition>> All => ByKey;

    private static readonly Dictionary<uint, ForceOrbDefinition> ByStatus = ByStatusCode(ByKey.Values, static orb => orb.StatusCode);

    public static bool TryGetByStatus(uint code, out ForceOrbDefinition orb) => ByStatus.TryGetValue(code, out orb);

    // The first definition of each status code, for the lookups presence and the HUD make per update and frame.
    internal static Dictionary<uint, T> ByStatusCode<T>(IEnumerable<T> definitions, Func<T, uint> statusOf)
    {
        var byStatus = new Dictionary<uint, T>();
        foreach (var definition in definitions)
        {
            byStatus.TryAdd(statusOf(definition), definition);
        }

        return byStatus;
    }

    private static Dictionary<string, ForceOrbDefinition> Build()
    {
        var orbs = new Dictionary<string, ForceOrbDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index <= Names.Length; index++)
        {
            var i = index - 1;
            orbs[$"FO1{index:D2}"] = new ForceOrbDefinition(index, false, $"{Names[i]} Force", WeaponLevels[i].Min, WeaponLevels[i].Max, Seconds[i], Damage[i],
                Percent[i]);
            orbs[$"FO1{index + 20:D2}"] = new ForceOrbDefinition(index, true, $"Magic {Names[i]} Force", MagicLevels[i].Min, MagicLevels[i].Max, Seconds[i],
                Damage[i], Percent[i]);
        }

        return orbs;
    }
}
