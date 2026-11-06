using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MartialArtistClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Martial Artist", "Combatant", "Master", "Grand Master", "Iron Fist"],
        new MotionFallback(3520, 22, 2880),
        static weapon => weapon.Definition.Kind == ItemKind.Vambrace,
        CastingWeapon: null,
        Shift: new RequirementShift(new(5, 15), new(-20, -10), default, new(-25, -15)),
        QuestWeapon: "WV108",
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
            new CharacterAttributes(24, 8, 25, 18, 24),
            new CharacterFunctionProfile(65, 2, 2, 2, 2, 2, 0)))
    {
    }
}
