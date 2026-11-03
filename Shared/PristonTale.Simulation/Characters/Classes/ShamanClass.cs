using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ShamanClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Shaman", "Clairvoyant", "Soul Mage", "Necromancer", "Soul Master"],
        new MotionFallback(3360, 22, 3040),
        static weapon => weapon.Definition.Kind == ItemKind.Phantom,
        CastingWeapon: ItemKind.Phantom,
        Shift: null,
        QuestWeapon: "WN109",
        BeginnerWeapon: "",
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
            new CharacterFunctionProfile(190, 5, 1, 3, 3, 2, 1)))
    {
    }
}
