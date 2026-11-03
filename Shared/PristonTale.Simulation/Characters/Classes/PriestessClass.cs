using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class PriestessClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Priestess", "Saintess", "Bishop", "Celestial", "Arc Angel"],
        new MotionFallback(3200, 20, 3040),
        static weapon => weapon.Definition.Kind == ItemKind.Staff,
        CastingWeapon: ItemKind.Staff,
        Shift: new RequirementShift(new(-25, -20), new(15, 20), new(-15, -10), new(-20, -15)),
        QuestWeapon: "WM109",
        BeginnerWeapon: "WM101",
        QuestWeaponHunt.Field,
        QuestItems.ArcaneRing,
        CharacterBrood.Morion,
        Female: true);

    public PriestessClass()
        : base(BuildDefinition(
            PlayerClass.Priestess,
            "Priestess",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\mC001.inx",
            headInx: "char\\tmABCD\\Mfh-C01.inx",
            armorTableIndex: 6,
            new CharacterAttributes(15, 28, 21, 15, 20),
            new CharacterFunctionProfile(200, 4, 1, 3, 3, 2, 2)))
    {
    }
}