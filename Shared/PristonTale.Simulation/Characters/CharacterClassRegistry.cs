using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Characters.Classes;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

public sealed class CharacterClassRegistry
{
    private readonly Dictionary<PlayerClass, CharacterClass> _byClass;

    public CharacterClassRegistry()
    {
        CharacterClass[] all =
        [
            new FighterClass(),
            new MechanicianClass(),
            new ArcherClass(),
            new PikemanClass(),
            new AtalantaClass(),
            new KnightClass(),
            new MagicianClass(),
            new PriestessClass(),
            new AssassinClass(),
            new ShamanClass(),
            new MartialArtistClass()
        ];
        _byClass = all.ToDictionary(static c => c.Class);
        All = all;
    }

    public IReadOnlyList<CharacterClass> All { get; }

    public CharacterClass Get(PlayerClass playerClass) => _byClass[playerClass];

    public PlayerCharacter CreatePlayer(PlayerClass playerClass, string name, int level = 1) =>
        new(Get(playerClass), name, level);
}
