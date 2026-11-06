using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

// The item-specialisation bit of each class. These bits are not in class order (a motion's job mask is; see
// PlayerClass), so the mapping is a table both ways.
public static class JobBits
{
    public static JobBitCode Of(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Fighter => JobBitCode.Fighter,
        PlayerClass.Mechanician => JobBitCode.Mechanician,
        PlayerClass.Archer => JobBitCode.Archer,
        PlayerClass.Pikeman => JobBitCode.Pikeman,
        PlayerClass.Atalanta => JobBitCode.Atalanta,
        PlayerClass.Knight => JobBitCode.Knight,
        PlayerClass.Magician => JobBitCode.Magician,
        PlayerClass.Priestess => JobBitCode.Priestess,
        PlayerClass.Assassin => JobBitCode.Assassin,
        PlayerClass.Shaman => JobBitCode.Shaman,
        PlayerClass.MartialArtist => JobBitCode.MartialArtist,
        _ => 0
    };

    // The class of a single specialisation bit; null for no bit or several.
    public static PlayerClass? ClassOf(JobBitCode bit)
    {
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            if (Of(playerClass) == bit)
            {
                return playerClass;
            }
        }

        return null;
    }
}
