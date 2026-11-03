using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MechanicianClass : CharacterClass
{
    public static readonly ClassProfile Profile = new(
        ["Mechanician", "Mechanic Master", "Metal Leader", "Metallion", "Metal Rage"],
        new MotionFallback(3520, 22, 2880),
        static weapon => (weapon.JobSpecMask & (uint)JobBitCode.Mechanician) != 0 || weapon.Definition.UniqueItem == 2,
        CastingWeapon: null,
        Shift: new RequirementShift(new(5, 15), new(-20, -10), default, new(-25, -15)),
        QuestWeapon: "WC108",
        BeginnerWeapon: "WC102",
        QuestWeaponHunt.Melee,
        QuestItems.EmperorRing,
        CharacterBrood.Tempskron,
        Female: false);

    public MechanicianClass()
        : base(BuildDefinition(
            PlayerClass.Mechanician,
            "Mechanician",
            Profile.Brood,
            bodyInx: "char\\tmABCD\\A001.inx",
            headInx: "char\\tmABCD\\tmh-a01.inx",
            armorTableIndex: 0,
            new CharacterAttributes(24, 8, 25, 18, 24),
            new CharacterFunctionProfile(65, 2, 2, 2, 2, 2, 0)))
    {
    }
}