using PristonTale.Contracts.Units;
using PristonTale.Contracts.World;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.Effects;

// Fireflies around the local player in a forest at night (GameHours), as the original: every 512 ticks
// of 70 Hz, one time in two, a swarm of twenty (Effect) within 430 units across and 117 up of where it stands.
public sealed class Fireflies(Random random)
{
    public const string Effect = "vfx.ambient.fireflies";
    private const float PeriodTicks = 512f;

    private float _ticks;

    public static bool Shown(FieldDef field, float hour) =>
        field is { State: FieldState.Forest } && GameHours.IsNight(hour);

    // Whether a swarm starts this frame.
    public bool Update(float seconds, FieldDef field, float hour)
    {
        _ticks += seconds * GameUnits.TicksPerSecond;
        if (_ticks < PeriodTicks)
        {
            return false;
        }

        _ticks %= PeriodTicks;
        return Shown(field, hour) && random.Next(2) == 0;
    }
}
