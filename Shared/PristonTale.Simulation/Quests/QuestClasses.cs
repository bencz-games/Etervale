using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

// Class-dependent quest facts: tribe, home town, the quest weapon of the second rank-up, the ring that rank-up gives,
// the beginner weapon and the duel master of the third rank-up.
public static class QuestClasses
{

    // The quests split the classes by class number, not by tribe: the first four take the Tempskron side (Ricarten, its
    // NPCs and monsters), every later class the Morion side, the assassin included.
    public static bool IsTempskron(PlayerCharacter character) => ClassOf(character) < PlayerClass.Atalanta;

    // The first rank-up gathers trophies on the Tempskron side and for the assassin; the other classes brew a potion.
    public static bool RanksUpByTrophies(PlayerCharacter character) => IsTempskron(character) || ClassOf(character) == PlayerClass.Assassin;

    // The level 30 quest of the Ricarten store serves the eight classic classes.
    public static bool IsClassicClass(PlayerCharacter character) => ClassOf(character) <= PlayerClass.Priestess;

    public static PlayerClass ClassOf(PlayerCharacter character) => character.Definition.Class;

    public static int HomeField(PlayerCharacter character) => IsTempskron(character) ? FieldCodes.Ricarten : FieldCodes.Pillai;

    // Rank-up quests run only at the skill master of the class's home town.
    public static bool IsHomeTown(PlayerCharacter character, int fieldCode) => fieldCode == HomeField(character);

    public static uint QuestWeapon(PlayerClass playerClass) => CodeOrZero(ClassProfiles.Of(playerClass).QuestWeapon);

    // A class whose quest weapon the item catalog lacks forges none.
    private static uint CodeOrZero(string code) => ItemCodes.TryParse(code, out var value) ? value : 0;

    // How the quest weapon counts kills while it grows.
    public static QuestWeaponHunt WeaponHunt(PlayerClass playerClass) => ClassProfiles.Of(playerClass).Hunt;

    public static uint RankUpRing(PlayerClass playerClass) => ClassProfiles.Of(playerClass).RankUpRing;

    // The first beginner quest's weapon, specialised for the class.
    public static uint BeginnerWeapon(PlayerClass playerClass) => CodeOrZero(ClassProfiles.Of(playerClass).BeginnerWeapon);

    public static uint JobMask(PlayerCharacter character) => (uint)character.Definition.JobBitCode;

    // The master the third rank-up duels: the Tempskron classes face the skill master, the Morion ones the magic master.
    public static int DuelMasterSound(PlayerCharacter character) =>
        IsTempskron(character) ? World.Npcs.CharacterSoundCodes.SkillMaster : World.Npcs.CharacterSoundCodes.MagicMaster;
}

public enum QuestWeaponHunt : byte
{
    // Kills of one monster per stage: Bargon, Skeleton Warrior, Head Cutter.
    Melee = 0,

    // Kills of one monster per stage: Armored Beetle, Skeleton Ranger, Titan.
    Ranged = 1,

    // Kills of any monster in one field per stage.
    Field = 2
}
