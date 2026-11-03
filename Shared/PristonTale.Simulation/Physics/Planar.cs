using System.Numerics;

namespace PristonTale.Simulation.Physics;

// Distances on the ground plane (x, z), where the game measures reach, range and proximity whatever the heights.
public static class Planar
{
    public static float DistanceSquared(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt(DistanceSquared(a, b));

    // Strictly inside the circle of this radius around b.
    public static bool Within(Vector3 a, Vector3 b, float radius) => DistanceSquared(a, b) < radius * radius;

    // Two bodies of this radius overlapping: within it on the ground plane and in height.
    public static bool Overlaps(Vector3 a, Vector3 b, float radius) => MathF.Abs(a.Y - b.Y) < radius && Within(a, b, radius);
}
