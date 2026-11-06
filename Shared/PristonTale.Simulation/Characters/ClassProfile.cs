using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters.Classes;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters;

// Percent ranges an item specialised for the class moves each requirement by; a zero lower bound leaves it unchanged.
public readonly record struct RequirementShift(ItemRange Strength, ItemRange Spirit, ItemRange Talent, ItemRange Agility);

// The class's motion timing when its model's motion table is not loaded: the attack's hit and the eat's restore event in
// clip ticks, and the damage motion's length in frames.
public readonly record struct MotionFallback(uint AttackEventTicks, int DamageFrames, uint EatEventTicks);

// What the rules know about one class beyond its definition: the titles of its five tiers, its fallback motion timing,
// the weapon it deals extra damage with (level / 6), the weapon it casts at range with, the requirement shift of items
// specialised for it (none for the classes items are not rolled for yet), its quests' weapons, hunt and ring, its
// realm (whose town it starts and stands up in) and whether its characters are female.
// Each class declares its profile in its own file.
public sealed record ClassProfile(
    IReadOnlyList<string> Titles,
    MotionFallback Motion,
    Func<ItemInstance, bool> Favours,
    ItemKind? CastingWeapon,
    RequirementShift? Shift,
    string QuestWeapon,
    string BeginnerWeapon,
    QuestWeaponHunt Hunt,
    uint RankUpRing,
    CharacterBrood Brood,
    bool Female)
{
    public string TitleAt(int tier) => Titles[Math.Clamp(tier, 0, Titles.Count - 1)];
}

public static class ClassProfiles
{
    // The classes that cast at range with a staff (their basic attack and its sounds are a caster's).
    public static bool CastsWithStaff(PlayerClass playerClass) => Of(playerClass).CastingWeapon == ItemKind.Staff;

    // The classes items can be specialised for (those with a requirement shift).
    public static bool Specializes(PlayerClass playerClass) => Of(playerClass).Shift is not null;

    public static ClassProfile Of(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Fighter => FighterClass.Profile,
        PlayerClass.Mechanician => MechanicianClass.Profile,
        PlayerClass.Archer => ArcherClass.Profile,
        PlayerClass.Pikeman => PikemanClass.Profile,
        PlayerClass.Atalanta => AtalantaClass.Profile,
        PlayerClass.Knight => KnightClass.Profile,
        PlayerClass.Magician => MagicianClass.Profile,
        PlayerClass.Priestess => PriestessClass.Profile,
        PlayerClass.Assassin => AssassinClass.Profile,
        PlayerClass.Shaman => ShamanClass.Profile,
        PlayerClass.MartialArtist => MartialArtistClass.Profile,
        _ => throw new ArgumentOutOfRangeException(nameof(playerClass))
    };
}
