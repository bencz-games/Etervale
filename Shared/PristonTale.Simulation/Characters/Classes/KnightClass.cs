using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class KnightClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Knight", "Paladin", "Holy Knight", "SaintKnight", "Valiant"],
        new MotionFallback(3520, 22, 2880),
        static weapon => weapon.Definition.Kind == ItemKind.Sword,
        CastingWeapon: null,
        Shift: new RequirementShift(new(5, 15), new(-15, -10), new(5, 10), new(-25, -15)),
        QuestWeapon: "WS210",
        BeginnerWeapon: "WS202",
        QuestWeaponHunt.Melee,
        QuestItems.EmperorRing,
        CharacterBrood.Morion,
        Female: false);

    public KnightClass()
        : base(BuildDefinition(
            PlayerClass.Knight,
            "Knight",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\mA001.inx",
            headInx: "char\\tmABCD\\Mmh-A01.inx",
            armorTableIndex: 4,
            new CharacterAttributes(26, 13, 17, 19, 24),
            new CharacterFunctionProfile(190, 2, 2, 2, 1, 2, 0)))
    {
    }
}