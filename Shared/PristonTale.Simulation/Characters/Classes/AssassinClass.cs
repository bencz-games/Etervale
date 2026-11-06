using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class AssassinClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Assassin", "Rogue", "Shadow", "Shadow Walker", "Shadow Master"],
        new MotionFallback(3200, 24, 2400),
        static weapon => weapon.Definition.Kind == ItemKind.Dagger,
        CastingWeapon: null,
        Shift: new RequirementShift(new(5, 15), new(-20, -10), default, new(-20, -15)),
        QuestWeapon: "WD109",
        BeginnerWeapon: "WD102",
        QuestWeaponHunt.Melee,
        QuestItems.ArcaneRing,
        CharacterBrood.Tempskron,
        Female: true);

    public AssassinClass()
        : base(BuildDefinition(
            PlayerClass.Assassin,
            "Assassin",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\e001.inx",
            headInx: "char\\tmABCD\\tfh-e01.inx",
            armorTableIndex: 8,
            new CharacterAttributes(25, 10, 22, 20, 22),
            new CharacterFunctionProfile(80, 3, 3, 2, 1, 2, 0)))
    {
    }
}
