namespace PristonTale.Simulation.Quests;

// Quest codes of the monsters quests hunt. The authored monster files carry them as their classification code; a kill
// credits a hunt when the code matches.
public static class QuestMonsters
{
    public const int Bargon = 1;
    public const int SkeletonWarrior = 2;
    public const int HeadCutter = 3;
    public const int ArmoredBeetle = 4;
    public const int SkeletonRanger = 5;
    public const int Titan = 6;
    public const int Muffin = 7;
    public const int DireBee = 8;
    public const int Typhoon = 9;
    public const int Ratoo = 10;
    public const int Grotesque = 11;
    public const int IronFist = 12;
    public const int Omega = 13;
    public const int DMachine = 14;
    public const int Mountain = 15;
    public const int SkeletonKnight = 16;
    public const int SolidSnail = 17;
    public const int ThornCrawler = 18;
    public const int Mummy = 19;
    public const int DoomGuard = 20;
    public const int Figon = 21;
    public const int StoneGiant = 22;
    public const int StoneGolem = 23;
    public const int IronGuard = 24;
    public const int Avelin = 25;
    public const int AveliskL = 26;
    public const int BeEvil = 27;
    public const int IllusionKnight = 28;
    public const int Nightmare = 29;
    public const int Witch = 30;
    public const int HeavyGoblin = 31;
    public const int Dawlin = 32;
    public const int Stygian = 33;
    public const int Incubus = 34;
    public const int ChainGolem = 35;
    public const int DarkSpecter = 36;
    public const int Metron = 37;
    public const int AveliskLord = 38;
    public const int Dusk = 39;
    public const int Sadness = 40;
    public const int Hopy = 41;
    public const int Rabie = 42;
    public const int NorthGoblin = 43;
    public const int Hobgoblin = 44;
    public const int Corrupt = 45;
    public const int Skeleton = 46;
    public const int Cyclops = 47;

    // Fury of Phantom hunts every monster of a field; these codes only name the hunt.
    public const int HeartOfPerum = 48;
    public const int GalluviaValley = 49;

    public const int Spidertrooper = 60;
    public const int DeadZone = 84;
    public const int GoblinShaman = 85;
    public const int Hestian = 87;
    public const int HyperMachine = 88;

    // The event monsters of the original never count for a quest.
    public const int EventMonster = 100;

    private static readonly string[] Names =
    [
        "", "Bargon", "Skeleton Warrior", "Head Cutter", "Armored Beetle", "Skeleton Ranger", "Titan", "Muffin", "Dire Bee", "Typhoon",
        "Ratoo", "Grotesque", "Iron Fist", "Omega", "D-Machine", "Mountain", "Skeleton Knight", "Solid Snail", "Thorn Crawler", "Mummy",
        "Doom Guard", "Figon", "Stone Giant", "Stone Golem", "Iron Guard", "Avelin", "Avelisk-L", "BeEvil", "Illusion Knight", "Nightmare",
        "Witch", "Heavy Goblin", "Dawlin", "Stygian", "Incubus", "Chain Golem", "Dark Specter", "Metron", "Avelisk Lord", "Dusk",
        "Sadnesst", "Hopy", "Rabie", "NorthGoblin", "Hobgoblin", "Corrupt", "Skeleton", "Cyclops",
        "Monsters in the Heart of Perum", "Monsters of the Galluvia  Velly", "Doral", "Cokris", "Plant Lord", "Bee Dog", "Devilish Tree",
        "Skeleton Archer", "Crypt", "Mighty Goblin", "Slaughter", "Sand Lem", "Spidertrooper", "Vampiric Machine", "Runic Guardian",
        "Turtle Cannon", "Incubus Summoner", "Chaoscara", "Hell Hound", "Dark Guard", "Bloody Knight", "Seto", "Revived Mecanician",
        "Revived Fighter", "Undead King Hopy", "Revived Archer", "Revived Pikeman", "Hobogolem", "Revived Magician", "Revived Atalanta",
        "Gorgon", "Revived Knight", "Revived Priestess", "Kakoa", "Undead Maple", "Xetan", "Dead Zone", "Goblin Shaman", "Hest", "Hestian",
        "Hyper Machine"
    ];

    public static string NameOf(int code) => code > 0 && code < Names.Length ? Names[code] : "";
}
