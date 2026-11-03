using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.World.Ai;

public static class MonsterLocomotion
{
    // One step of a monster's ground speed; a walk takes one step, a run one more (some families more).
    public const float BaseUnitsPerSecond = 70f;

    public static float UnitsPerSecond(MonsterDefinition definition, bool running)
    {
        int steps = definition.IsBoss && definition.SoundCode == CharacterSoundCodes.DarkKnight ? 2 : 1;
        if (running)
        {
            steps += definition.SoundCode switch
            {
                CharacterSoundCodes.Greven => 4,
                CharacterSoundCodes.Wolverine => 2,
                _ => 1
            };
        }

        return BaseUnitsPerSecond * steps;
    }
}
