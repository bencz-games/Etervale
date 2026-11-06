using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ShamanClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Shaman", "Summoner", "High Summoner", "Soul Catcher", "Soul Master"],
        new MotionFallback(3360, 22, 3040),
        static weapon => weapon.Definition.Kind == ItemKind.Phantom,
        CastingWeapon: ItemKind.Phantom,
        Shift: new RequirementShift(new(-25, -20), new(15, 25), new(-15, -10), new(-20, -15)),
        QuestWeapon: "WN109",
        BeginnerWeapon: "WN101",
        QuestWeaponHunt.Field,
        QuestItems.EmperorRing,
        CharacterBrood.Morion,
        Female: false);

    public ShamanClass()
        : base(BuildDefinition(
            PlayerClass.Shaman,
            "Shaman",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\me001.inx",
            headInx: "char\\tmABCD\\Mmh-E01.inx",
            armorTableIndex: 9,
            new CharacterAttributes(15, 27, 20, 15, 22),
            new CharacterFunctionProfile(200, 2, 1, 2, 3, 2, 1)))
    {
    }
}
