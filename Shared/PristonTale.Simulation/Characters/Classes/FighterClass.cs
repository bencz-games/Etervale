using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class FighterClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Fighter", "Warrior", "Champion", "Immortal Warrior", "Warlord"],
        new MotionFallback(3520, 22, 2880),
        static weapon => weapon.Definition.Kind == ItemKind.Axe,
        CastingWeapon: null,
        Shift: new RequirementShift(new(10, 15), new(-20, -15), default, new(-20, -15)),
        QuestWeapon: "WA108",
        BeginnerWeapon: "WA102",
        QuestWeaponHunt.Melee,
        QuestItems.EmperorRing,
        CharacterBrood.Tempskron,
        Female: false);

    public FighterClass()
        : base(BuildDefinition(
            PlayerClass.Fighter,
            "Fighter",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\B001.inx",
            headInx: "char\\tmABCD\\tmh-b01.inx",
            armorTableIndex: 1,
            new CharacterAttributes(28, 6, 21, 17, 27),
            new CharacterFunctionProfile(70, 1, 3, 1, 1, 2, 0)))
    {
    }
}