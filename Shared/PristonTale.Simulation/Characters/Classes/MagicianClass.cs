using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MagicianClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Magician", "Wizard", "RoyalWizard", "ArchMage", "Elemental Master"],
        new MotionFallback(3360, 22, 3040),
        static weapon => weapon.Definition.Kind == ItemKind.Staff,
        CastingWeapon: ItemKind.Staff,
        Shift: new RequirementShift(new(-25, -20), new(15, 25), new(-15, -10), new(-20, -15)),
        QuestWeapon: "WM109",
        BeginnerWeapon: "WM101",
        QuestWeaponHunt.Field,
        QuestItems.ArcaneRing,
        CharacterBrood.Morion,
        Female: false);

    public MagicianClass()
        : base(BuildDefinition(
            PlayerClass.Magician,
            "Magician",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\mD001.inx",
            headInx: "char\\tmABCD\\Mmh-D01.inx",
            armorTableIndex: 7,
            new CharacterAttributes(16, 29, 19, 14, 21),
            new CharacterFunctionProfile(190, 5, 1, 3, 3, 2, 1)))
    {
    }
}