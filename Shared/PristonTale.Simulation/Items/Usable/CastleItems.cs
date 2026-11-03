using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Items.Usable;

// Scrolls last 30 s; a Defiance Stone halves the blows of one crystal tower for 60 s; a Might Stone adds 20% to player
// damage against one class for 60 s.
public enum CastleItemKind : byte
{
    Scroll,
    Defiance,
    Might
}

// Target: the scroll effect, the sound code of the crystal tower a Defiance Stone resists, or the class a Might Stone
// strikes harder.
public sealed record CastleItemDefinition(string CatalogKey, int Index, string Name, CastleItemKind Kind, int Target, string KeepIcon, string Effect)
{
    public uint StatusCode => CastleItems.StatusBase + (uint)Index;

    public int DurationSeconds => Kind == CastleItemKind.Scroll ? CastleItems.ScrollSeconds : CastleItems.StoneSeconds;

    public EffectSpec Spec() => (Kind, (ScrollEffectKind)Target) switch
    {
        (CastleItemKind.Scroll, ScrollEffectKind.Invulnerability) => new EffectSpec(StatusCode, EffectSourceKind.Item, false,
            [StatModifier.Percent(StatKind.DamageDealt, -50)]),
        (CastleItemKind.Scroll, ScrollEffectKind.Critical) => new EffectSpec(StatusCode, EffectSourceKind.Item, false,
            [StatModifier.Flat(StatKind.CriticalChance, CastleItems.ScrollBonus)]),
        (CastleItemKind.Scroll, _) => new EffectSpec(StatusCode, EffectSourceKind.Item, false, [StatModifier.Flat(StatKind.Evasion, CastleItems.ScrollBonus)]),
        _ => new EffectSpec(StatusCode, EffectSourceKind.Item, false, ReadOnlySpan<StatModifier>.Empty)
    };
}

// The Bless Castle items. They work only in the castle field and end when their holder leaves it. A castle scroll
// excludes the premium scroll of the same effect (Athanasia and Eternal Life, Deadly Edge and Fatal Edge, Avail of Evade
// and Avert Scroll); one castle scroll, one Defiance Stone and one Might Stone run at a time, a new one replacing the
// running one of its kind.
public static class CastleItems
{
    public const uint StatusBase = 0xF0000400;
    public const int ScrollSeconds = 30;
    public const int StoneSeconds = 60;
    public const int ScrollBonus = 5;
    public const int MightPercent = 20;

    private const string Keep = "image/Sinimage/skill/keep/";

    public static IReadOnlyList<CastleItemDefinition> All { get; } =
    [
        new("BC101", 1, "Athanasia", CastleItemKind.Scroll, (int)ScrollEffectKind.Invulnerability, Keep + "S_Ath.tga",
            "Invincible for a while; attack power halved"),
        new("BC102", 2, "Deadly Edge", CastleItemKind.Scroll, (int)ScrollEffectKind.Critical, Keep + "S_Dea.tga", "Critical chance +5%"),
        new("BC103", 3, "Avail of Evade", CastleItemKind.Scroll, (int)ScrollEffectKind.Evasion, Keep + "S_Eva.tga", "Evade chance +5%"),
        new("BC106", 6, "Defiance Stone", CastleItemKind.Defiance, 0x5020, Keep + "F_C_M.tga", "Offsets 50% of the Fire Crystal Tower's attack"),
        new("BC107", 7, "Defiance Stone", CastleItemKind.Defiance, 0x5022, Keep + "I_C_M.tga", "Offsets 50% of the Ice Crystal Tower's attack"),
        new("BC108", 8, "Defiance Stone", CastleItemKind.Defiance, 0x5021, Keep + "L_C_M.tga", "Offsets 50% of the Lightning Crystal Tower's attack"),
        new("BC109", 9, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Fighter, Keep + "Ft_R.tga", "+20% damage when striking a Fighter"),
        new("BC110", 10, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Mechanician, Keep + "Mc_R.tga", "+20% damage when striking a Mechanician"),
        new("BC111", 11, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Pikeman, Keep + "Pk_R.tga", "+20% damage when striking a Pikeman"),
        new("BC112", 12, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Archer, Keep + "Ac_R.tga", "+20% damage when striking an Archer"),
        new("BC113", 13, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Knight, Keep + "Kt_R.tga", "+20% damage when striking a Knight"),
        new("BC114", 14, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Atalanta, Keep + "At_R.tga", "+20% damage when striking an Atalanta"),
        new("BC115", 15, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Magician, Keep + "Mg_R.tga", "+20% damage when striking a Magician"),
        new("BC116", 16, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Priestess, Keep + "Pt_R.tga", "+20% damage when striking a Priestess")
    ];

    public static bool TryGet(string catalogKey, out CastleItemDefinition item)
    {
        item = All.FirstOrDefault(candidate => string.Equals(candidate.CatalogKey, catalogKey, StringComparison.OrdinalIgnoreCase));
        return item is not null;
    }

    public static bool TryGetByIndex(int index, out CastleItemDefinition item)
    {
        item = All.FirstOrDefault(candidate => candidate.Index == index);
        return item is not null;
    }

    private static readonly Dictionary<uint, CastleItemDefinition> ByStatus = ForceOrbs.ByStatusCode(All, static item => item.StatusCode);

    public static bool TryGetByStatus(uint code, out CastleItemDefinition item)
    {
        item = null;
        return code > StatusBase && code <= StatusBase + 16 && ByStatus.TryGetValue(code, out item);
    }

    // The running castle item of a kind, or null.
    public static CastleItemDefinition Running(EffectContainer effects, CastleItemKind kind)
    {
        foreach (var item in All)
        {
            if (item.Kind == kind && effects.TryGet(item.StatusCode, out _))
            {
                return item;
            }
        }

        return null;
    }

    // A crystal tower's blow on a holder of its Defiance Stone deals half.
    public static int Defend(int towerSoundCode, EffectContainer effects, int damage)
    {
        return Running(effects, CastleItemKind.Defiance) is { } stone && stone.Target == towerSoundCode ? damage / 2 : damage;
    }

    // Player damage against a player of the Might Stone's class grows by 20%.
    public static int Strike(EffectContainer attacker, PlayerClass defender, int damage)
    {
        return Running(attacker, CastleItemKind.Might) is { } stone && stone.Target == (int)defender ? damage + damage * MightPercent / 100 : damage;
    }
}
