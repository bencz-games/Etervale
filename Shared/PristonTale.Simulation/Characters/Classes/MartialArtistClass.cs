using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MartialArtistClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Martial Artist", "Striker", "Trick Artist", "Master", "Grand Master"],
        new MotionFallback(3200, 24, 2400),
        static weapon => weapon.Definition.Kind == ItemKind.Vambrace,
        CastingWeapon: null,
        Shift: new RequirementShift(new(10, 15), new(-20, -15), default, new(-20, -15)),
        QuestWeapon: "WV109",
        BeginnerWeapon: "WV102",
        QuestWeaponHunt.Melee,
        QuestItems.EmperorRing,
        CharacterBrood.Tempskron,
        Female: true);

    public MartialArtistClass()
        : base(BuildDefinition(
            PlayerClass.MartialArtist,
            "Martial Artist",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\f001.inx",
            headInx: "char\\tmABCD\\tfh-f01.inx",
            armorTableIndex: 10,
            new CharacterAttributes(26, 9, 20, 20, 24),
            new CharacterFunctionProfile(80, 3, 3, 2, 1, 2, 0)))
    {
    }
}
