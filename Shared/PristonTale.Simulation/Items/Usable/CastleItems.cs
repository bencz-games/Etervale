using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Npcs;
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

    public EffectSpec Spec() => Kind == CastleItemKind.Scroll
        ? ScrollEffects.Spec((ScrollEffectKind)Target, StatusCode, CastleItems.ScrollBonus)
        : new EffectSpec(StatusCode, EffectSourceKind.Item, false, ReadOnlySpan<StatModifier>.Empty);
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
    // Restoration raises its dead owner this long after the death, guarded for RestorationGuardMs.
    public const int RestorationDelayMs = 10_000;
    public const int RestorationGuardMs = 5000;

    public static IReadOnlyList<CastleItemDefinition> All { get; } =
    [
        new("BC101", 1, "Athanasia", CastleItemKind.Scroll, (int)ScrollEffectKind.Invulnerability, KeepIcons.Path("S_Ath.tga"),
            "Invincible for a while; attack power halved"),
        new("BC102", 2, "Deadly Edge", CastleItemKind.Scroll, (int)ScrollEffectKind.Critical, KeepIcons.Path("S_Dea.tga"), "Critical chance +5%"),
        new("BC103", 3, "Avail of Evade", CastleItemKind.Scroll, (int)ScrollEffectKind.Evasion, KeepIcons.Path("S_Eva.tga"), "Evade chance +5%"),
        new("BC106", 6, "Defiance Stone", CastleItemKind.Defiance, CharacterSoundCodes.CastleCrystalFire, KeepIcons.Path("F_C_M.tga"), "Offsets 50% of the Fire Crystal Tower's attack"),
        new("BC107", 7, "Defiance Stone", CastleItemKind.Defiance, CharacterSoundCodes.CastleCrystalIce, KeepIcons.Path("I_C_M.tga"), "Offsets 50% of the Ice Crystal Tower's attack"),
        new("BC108", 8, "Defiance Stone", CastleItemKind.Defiance, CharacterSoundCodes.CastleCrystalLightning, KeepIcons.Path("L_C_M.tga"), "Offsets 50% of the Lightning Crystal Tower's attack"),
        new("BC109", 9, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Fighter, KeepIcons.Path("Ft_R.tga"), "+20% damage when striking a Fighter"),
        new("BC110", 10, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Mechanician, KeepIcons.Path("Mc_R.tga"), "+20% damage when striking a Mechanician"),
        new("BC111", 11, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Pikeman, KeepIcons.Path("Pk_R.tga"), "+20% damage when striking a Pikeman"),
        new("BC112", 12, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Archer, KeepIcons.Path("Ac_R.tga"), "+20% damage when striking an Archer"),
        new("BC113", 13, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Knight, KeepIcons.Path("Kt_R.tga"), "+20% damage when striking a Knight"),
        new("BC114", 14, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Atalanta, KeepIcons.Path("At_R.tga"), "+20% damage when striking an Atalanta"),
        new("BC115", 15, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Magician, KeepIcons.Path("Mg_R.tga"), "+20% damage when striking a Magician"),
        new("BC116", 16, "Might Stone", CastleItemKind.Might, (int)PlayerClass.Priestess, KeepIcons.Path("Pt_R.tga"), "+20% damage when striking a Priestess")
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
