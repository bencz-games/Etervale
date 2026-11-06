namespace PristonTale.Contracts.Characters;

// A character's class, as stored, sent on the wire and read by the client. The value doubles as the class's position
// in the motion tables (bit Class - 1 of a motion's job mask).
public enum PlayerClass
{
    Fighter = 1,
    Mechanician = 2,
    Archer = 3,
    Pikeman = 4,
    Atalanta = 5,
    Knight = 6,
    Magician = 7,
    Priestess = 8,
    Assassin = 9,
    Shaman = 10,
    MartialArtist = 11
}
