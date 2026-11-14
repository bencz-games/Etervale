using PristonTale.Contracts.Characters;

namespace PristonTale.Client.Net.Presence;

public sealed record PlayerAppearanceInfo(
    string Name,
    PlayerClass Class,
    int Tier,
    int FaceIndex,
    int HairIndex,
    string ModelName,
    string ModelName2,
    int Level,
    string WeaponKey = "", string ArmorKey = "", string OffHandKey = "", bool InVillage = false)
{
    // Premium looks: the helper pet (1 Terry, 2 Nepsys, 3 Io, 4 Mute), the Phoenix and the big head (1 or 2).
    public int HelperPet { get; init; }
    public bool Phoenix { get; init; }
    public int BigHead { get; init; }
    public string CostumeKey { get; init; } = "";

    // How the held weapon and off-hand item glow.
    public Simulation.Items.HeldItemGlow WeaponGlow { get; init; } = Simulation.Items.HeldItemGlow.None;
    public Simulation.Items.HeldItemGlow OffHandGlow { get; init; } = Simulation.Items.HeldItemGlow.None;

    // The sign of the personal shop the player sells from; empty when it sells nothing.
    public string ShopSign { get; init; } = "";

    // The player's clan (0 for none), its name and its mark's version (0 the default mark).
    public long ClanId { get; init; }
    public string ClanName { get; init; } = "";
    public int ClanMark { get; init; }
}
