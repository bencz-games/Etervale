using PristonTale.Contracts.Units;
#nullable enable
using System.Numerics;
using PristonTale.Simulation.Physics;

namespace PristonTale.Simulation.World;

public enum MonsterStepResult
{
    Blocked,
    Moved,
    Slid,
    // Every probe the stage allowed ended on another monster.
    Crowded
}

// Other live monsters around the mover; the original server tests only monster against monster
// (32 units planar, within 32 units of height).
public interface IStepOccupancy
{
    bool IsOccupied(Vector3 position);
}

// Server-side monster locomotion after the original stage next-move check: the requested heading at the full step, then
// heading -768 and +768 of the 4096-step circle (67.5 degrees) at half the step. The facing is not changed by a
// sideways probe, so a monster slides along a wall while still looking where it wants to go.
public static class MonsterStepping
{
    // The original server's stage step height is 20 units; players keep the client's 10-unit step.
    public const float StepUp = 20f;

    // The original sweeps the model's collision width; models' collision sizes are not imported, so a fixed body is used.
    public const float BodyHalfWidth = 12f;

    public const float SideProbeRadians = 768 * GameUnits.RadiansPerAngle;

    public static MonsterStepResult Step(FieldTerrain terrain, Vector3 from, float heading, float distance, IStepOccupancy? occupancy,
        out Vector3 accepted)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        accepted = from;
        if (!float.IsFinite(distance) || distance <= 0f || !float.IsFinite(heading))
        {
            return MonsterStepResult.Blocked;
        }

        // A monster already overlapping another may walk out of it, as the original character update allows.
        var others = occupancy is not null && !occupancy.IsOccupied(from) ? occupancy : null;
        var crowded = false;
        var half = distance * 0.5f;
        if (TryProbe(terrain, from, heading, distance, others, ref crowded, out accepted))
        {
            return MonsterStepResult.Moved;
        }

        if (TryProbe(terrain, from, heading - SideProbeRadians, half, others, ref crowded, out accepted)
            || TryProbe(terrain, from, heading + SideProbeRadians, half, others, ref crowded, out accepted))
        {
            return MonsterStepResult.Slid;
        }

        accepted = from;
        return crowded ? MonsterStepResult.Crowded : MonsterStepResult.Blocked;
    }

    private static bool TryProbe(FieldTerrain terrain, Vector3 from, float heading, float distance, IStepOccupancy? occupancy, ref bool crowded,
        out Vector3 accepted)
    {
        var end = from + Planar.Forward(heading) * distance;
        if (!terrain.TryBodyStep(from, end, BodyHalfWidth, StepUp, out accepted))
        {
            return false;
        }

        if (occupancy is not null && occupancy.IsOccupied(accepted))
        {
            crowded = true;
            accepted = from;
            return false;
        }

        return true;
    }
}
