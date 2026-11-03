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
        Shift: null,
        QuestWeapon: "WD109",
        BeginnerWeapon: "",
        QuestWeaponHunt.Melee,
        QuestItems.EmperorRing,
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
            new CharacterFunctionProfile(80, 3, 3, 2, 3, 1, 0)))
    {
    }
}
