using System.Numerics;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

public readonly record struct FieldArrival(Vector3 Position, bool IsWarp);

public static class FieldTransition
{
    // Matches the client's gate residency radius. This authorizes membership, never a teleport.
    public const float GateRadius = 1810f;

    public static bool TryResolve(FieldDef source, FieldDef destination, Vector3 origin, int level, out FieldArrival arrival)
    {
        arrival = new FieldArrival(origin, false);
        if (!WorldPosition.IsValid(origin) || source.Id == destination.Id || level < destination.LimitLevel)
        {
            return false;
        }

        foreach (var warp in source.WarpGates)
        {
            var radius = warp.ServerReach;
            if (warp.DestinationFieldId == destination.Id
                && !WingGates.IsStone(warp)
                && level >= warp.LimitLevel
                && Near(origin, warp.X, warp.Z, radius))
            {
                arrival = new FieldArrival(new Vector3(warp.DestinationX, warp.DestinationY, warp.DestinationZ), true);
                return WorldPosition.IsValid(arrival.Position);
            }
        }

        foreach (var gate in source.Gates)
        {
            if (gate.DestinationFieldId == destination.Id && Near(origin, gate.X, gate.Z, GateRadius))
            {
                return true;
            }
        }

        foreach (var gate in destination.Gates)
        {
            if (gate.DestinationFieldId == source.Id && Near(origin, gate.X, gate.Z, GateRadius))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Near(Vector3 origin, int x, int z, float radius)
    {
        var delta = new Vector2(origin.X - x, origin.Z - z);
        return delta.LengthSquared() <= radius * radius;
    }
}
