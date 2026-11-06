namespace PristonTale.Simulation.Characters;

public enum CharacterBrood
{
    Tempskron = 1,
    Morion = 2,
    Sophetio = 4
}

[Flags]
public enum JobBitCode : uint
{
    Mechanician = 0x00000001,
    Fighter = 0x00000002,
    Pikeman = 0x00000004,
    Archer = 0x00000008,
    Knight = 0x00010000,
    Atalanta = 0x00020000,
    Priestess = 0x00040000,
    Magician = 0x00080000,
    Assassin = 0x00000010,
    MartialArtist = 0x00000020,
    Shaman = 0x00100000
}

// Persisted equipment slot order of the classic save layout: no helmet slot, and Sheltom kept separate.
public enum EquipmentSlot
{
    Weapon,
    OffHand,
    Armor,
    Gloves,
    Boots,
    Bracelet,
    Amulet,
    RingLeft,
    RingRight,
    Sheltom
}

public static class EquipmentSlots
{
    public const int Count = (int)EquipmentSlot.Sheltom + 1;
}
