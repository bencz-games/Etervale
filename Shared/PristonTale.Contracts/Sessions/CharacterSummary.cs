using PristonTale.Contracts.Characters;

namespace PristonTale.Contracts.Sessions;

public sealed record CharacterSummary(
    string Name,
    PlayerClass Class,
    int Level,
    int FaceIndex,
    int Tier = 0,
    int HairIndex = 0,
    string ArmorKey = "",
    string WeaponKey = "",
    string OffHandKey = "",
    bool InVillage = true,
    string ClanName = "");
