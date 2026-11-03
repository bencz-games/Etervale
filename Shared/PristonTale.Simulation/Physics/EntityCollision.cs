using System.Collections.Generic;
using System.Numerics;

namespace PristonTale.Simulation.Physics;

public static class EntityCollision
{
    public static Vector3 Resolve(Vector3 oldPos, Vector3 desiredPos, float moverRadius, ReadOnlySpan<EntityNeighbour> neighbours)
    {
        foreach (var neighbour in neighbours)
        {
            if (Blocks(oldPos, desiredPos, EntityCollisionRadii.PairDistance(moverRadius, neighbour.Radius), neighbour.Position))
            {
                return oldPos;
            }
        }

        return desiredPos;
    }

    public static Vector3 Resolve(Vector3 oldPos, Vector3 desiredPos, float moverRadius, IReadOnlyList<EntityNeighbour> neighbours)
    {
        if (neighbours is null)
        {
            return desiredPos;
        }

        for (var index = 0; index < neighbours.Count; index++)
        {
            var neighbour = neighbours[index];
            if (Blocks(oldPos, desiredPos, EntityCollisionRadii.PairDistance(moverRadius, neighbour.Radius), neighbour.Position))
            {
                return oldPos;
            }
        }

        return desiredPos;
    }

    public static (Vector3 Center, float Radius) QueryBounds(Vector3 oldPos, Vector3 desiredPos, float radius)
    {
        var deltaX = desiredPos.X - oldPos.X;
        var deltaZ = desiredPos.Z - oldPos.Z;
        return ((oldPos + desiredPos) * 0.5f, MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ) * 0.5f + radius);
    }

    private static bool Blocks(Vector3 oldPos, Vector3 desiredPos, float radius, Vector3 center)
    {
        // Preserve escape from an existing overlap. Test the whole segment for new overlaps so a long
        // presentation frame cannot jump completely through an NPC/monster and land outside its circle.
        if (Planar.Overlaps(oldPos, center, radius))
        {
            return false;
        }

        if (Planar.Overlaps(desiredPos, center, radius))
        {
            return true;
        }

        var delta = desiredPos - oldPos;
        var start = 0f;
        var end = 1f;
        if (delta.Y == 0)
        {
            if (MathF.Abs(oldPos.Y - center.Y) >= radius)
            {
                return false;
            }
        }
        else
        {
            var lower = (center.Y - radius - oldPos.Y) / delta.Y;
            var upper = (center.Y + radius - oldPos.Y) / delta.Y;
            start = MathF.Max(0, MathF.Min(lower, upper));
            end = MathF.Min(1, MathF.Max(lower, upper));
            if (start >= end)
            {
                return false;
            }
        }

        var lengthSquared = delta.X * delta.X + delta.Z * delta.Z;
        var closest = (start + end) * 0.5f;
        if (lengthSquared > 0)
        {
            var projection = (center.X - oldPos.X) * delta.X + (center.Z - oldPos.Z) * delta.Z;
            closest = Math.Clamp(projection / lengthSquared, start, end);
        }

        var dx = oldPos.X + delta.X * closest - center.X;
        var dz = oldPos.Z + delta.Z * closest - center.Z;
        return dx * dx + dz * dz < radius * radius;
    }

}
