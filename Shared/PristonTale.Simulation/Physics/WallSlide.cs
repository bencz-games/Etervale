using System.Numerics;

namespace PristonTale.Simulation.Physics;

public delegate bool WallTrace(Vector3 start, Vector3 end, out Vector3 hit, out Vector3 normal);

// The wall slide of a walking body, the same for the client's own movement and the server's check of what it reports:
// each of three attempts traces the remaining ground-plane motion (reaching reach further), stops clearance short of the
// first wall and keeps the motion along it. Faces as flat as a floor do not stop it.
public static class WallSlide
{
    // A face whose normal rises at least this much is a floor (CollisionMesh's floor faces), never a wall.
    public const float FloorNormalY = PristonTale.Geometry.CollisionMesh.MinFloorNormalY;
    private const int Attempts = 3;

    // from is the trace height's point; the result keeps its height.
    public static Vector3 Slide(Vector3 from, Vector2 delta, float clearance, float reach, WallTrace trace)
    {
        var position = from;
        var remaining = new Vector3(delta.X, 0f, delta.Y);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var length = remaining.Length();
            if (length < 0.0001f)
            {
                break;
            }

            var direction = remaining / length;
            if (!trace(position, position + direction * (length + reach), out var hit, out var normal) || MathF.Abs(normal.Y) >= FloorNormalY)
            {
                position += remaining;
                break;
            }

            var allowed = Math.Clamp(Planar.Distance(position, hit) - clearance, 0f, length);
            position += direction * allowed;
            var wall = new Vector3(normal.X, 0f, normal.Z);
            if (wall.LengthSquared() < 1e-6f)
            {
                break;
            }

            wall = Vector3.Normalize(wall);
            var leftover = remaining - direction * allowed;
            remaining = leftover - wall * Vector3.Dot(leftover, wall);
        }

        return position;
    }
}
