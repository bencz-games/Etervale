using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.Effects;

// The dust the local player's feet raise, as the original: running on a ruin or desert field (Bellatra aside) while no
// rain falls, a puff every eighth 70 Hz tick, 4 units to either side in turn and 12 up, 10 to 25 across (Running);
// landing from a fall, a puff of 20 on either side, 8 up (Landing).
public static class FieldDust
{
    public const string Running = "vfx.ground.dust";
    public const string Landing = "vfx.ground.dust.landing";
    public const float Side = 4f;
    public const float RunningHeight = 12f;
    public const float LandingHeight = 8f;
    private const int PuffMask = 7;
    private const int SideBit = 8;

    public static bool Kicks(FieldDef field, float rain) =>
        field is { State: FieldState.Ruin or FieldState.Desert } && field.FieldCode != FieldCodes.Bellatra && rain <= 0f;

    // Whether this tick of the running counter puffs, and on which side (+1 right, -1 left).
    public static bool Puffs(int tick, out float side)
    {
        side = (tick & SideBit) == 0 ? 1f : -1f;
        return (tick & PuffMask) == 0;
    }
}
