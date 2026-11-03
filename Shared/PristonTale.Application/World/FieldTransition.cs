using System.Numerics;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

public readonly record struct FieldArrival(Vector3 Position, bool IsWarp);

public static class FieldTransition
{
    // Matches the client's gate residency radius. This authorizes membership, never a teleport.
    public const float GateRadius = 1810f;

    // A warp asks its gate's level; walking through a gate asks the destination's. A warp comes out at the gate's exit
    // nearest where the client says it arrived: the client picks among the gate's exits, the server never takes its
    // placement.
    public static bool TryResolve(FieldDef source, FieldDef destination, Vector3 origin, Vector3 requested, int level, out FieldArrival arrival)
    {
        arrival = new FieldArrival(origin, false);
        if (!WorldPosition.IsValid(origin) || source.Id == destination.Id)
        {
            return false;
        }

        foreach (var warp in source.WarpGates)
        {
            if (warp.DestinationFieldId == destination.Id
                && !WingGates.IsStone(warp)
                && level >= warp.LimitLevel
                && Near(origin, warp.X, warp.Z, warp.ServerReach))
            {
                arrival = new FieldArrival(NearestExit(warp, requested), true);
                return WorldPosition.IsValid(arrival.Position);
            }
        }

        if (level < destination.LimitLevel)
        {
            return false;
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

    private static Vector3 NearestExit(FieldWarpGate warp, Vector3 requested)
    {
        var best = Vector3.Zero;
        var bestDistance = float.PositiveInfinity;
        for (var index = 0; index < warp.ExitCount; index++)
        {
            var exit = warp.Exit(index);
            var position = new Vector3(exit.X, exit.Y, exit.Z);
            var distance = Planar.DistanceSquared(position, requested);
            if (distance < bestDistance)
            {
                best = position;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool Near(Vector3 origin, int x, int z, float radius)
    {
        var delta = new Vector2(origin.X - x, origin.Z - z);
        return delta.LengthSquared() <= radius * radius;
    }
}
