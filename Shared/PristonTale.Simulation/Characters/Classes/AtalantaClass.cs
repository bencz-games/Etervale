using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class AtalantaClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Atalanta", "Valkyrie", "Brunhild", "Valhalla", "Queen of Valhalla"],
        new MotionFallback(3200, 24, 2400),
        static weapon => weapon.Definition.Kind == ItemKind.ThrowingWeapon,
        CastingWeapon: null,
        Shift: new RequirementShift(new(-20, -15), new(-20, -10), default, new(15, 25)),
        QuestWeapon: "WT109",
        BeginnerWeapon: "WT102",
        QuestWeaponHunt.Ranged,
        QuestItems.FocusRing,
        CharacterBrood.Morion,
        Female: true);

    public AtalantaClass()
        : base(BuildDefinition(
            PlayerClass.Atalanta,
            "Atalanta",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\mB001.inx",
            headInx: "char\\tmABCD\\Mfh-B01.inx",
            armorTableIndex: 5,
            new CharacterAttributes(23, 15, 19, 19, 23),
            new CharacterFunctionProfile(200, 2, 2, 2, 2, 1, 0)))
    {
    }
}