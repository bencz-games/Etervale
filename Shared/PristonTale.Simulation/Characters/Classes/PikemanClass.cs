using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class PikemanClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Pikeman", "Combatant", "Lancer", "Lancelot", "Bayiolunt"],
        new MotionFallback(3680, 22, 2560),
        static weapon => weapon.Definition.Kind == ItemKind.Pole,
        CastingWeapon: null,
        Shift: new RequirementShift(new(10, 15), new(-20, -15), default, new(-25, -15)),
        QuestWeapon: "WP109",
        BeginnerWeapon: "WP102",
        QuestWeaponHunt.Melee,
        QuestItems.EmperorRing,
        CharacterBrood.Tempskron,
        Female: false);

    public PikemanClass()
        : base(BuildDefinition(
            PlayerClass.Pikeman,
            "Pikeman",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\C001.inx",
            headInx: "char\\tmABCD\\tmh-c01.inx",
            armorTableIndex: 2,
            new CharacterAttributes(26, 9, 20, 19, 25),
            new CharacterFunctionProfile(75, 1, 3, 1, 1, 2, 0)))
    {
    }
}