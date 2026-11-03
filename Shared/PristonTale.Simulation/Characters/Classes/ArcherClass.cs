using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ArcherClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Archer", "Huntress Master", "Dion's Disciple", "Sagittarion", "Iskar"],
        new MotionFallback(3200, 24, 2400),
        static weapon => weapon.Definition.Kind == ItemKind.Bow,
        CastingWeapon: null,
        Shift: new RequirementShift(new(-25, -15), new(-20, -10), default, new(15, 25)),
        QuestWeapon: "WS110",
        BeginnerWeapon: "WS102",
        QuestWeaponHunt.Ranged,
        QuestItems.FocusRing,
        CharacterBrood.Tempskron,
        Female: true);

    public ArcherClass()
        : base(BuildDefinition(
            PlayerClass.Archer,
            "Archer",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\D001.inx",
            headInx: "char\\tmABCD\\tfh-D01.inx",
            armorTableIndex: 3,
            new CharacterAttributes(17, 11, 21, 27, 23),
            new CharacterFunctionProfile(80, 3, 3, 2, 3, 1, 0)))
    {
    }
}