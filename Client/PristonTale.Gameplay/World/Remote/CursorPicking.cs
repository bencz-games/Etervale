using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

internal static class CursorPicking
{
    public static bool TryCreateRay(
        Viewport viewport,
        Matrix projection,
        Matrix view,
        Vector3 cameraPosition,
        Point cursor,
        out Ray ray)
    {
        ray = default;
        var near = viewport.Unproject(
            new Vector3(cursor.X, cursor.Y, PristonDepth.NearDepth),
            projection,
            view,
            Matrix.Identity);
        var direction = near - cameraPosition;
        var lengthSquared = direction.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        ray = new Ray(cameraPosition, direction);
        return true;
    }

    // Pick the visible torso rather than a large sphere around the feet.
    public static float? IntersectActor(Ray ray, Vector3 feet, float radius)
    {
        var lower = IntersectSphere(ray, feet + Vector3.Up * radius, radius);
        var upper = IntersectSphere(ray, feet + Vector3.Up * (radius * 2f), radius);
        if (lower.HasValue && upper.HasValue)
        {
            return MathF.Min(lower.Value, upper.Value);
        }

        return lower ?? upper;
    }

    public static float? IntersectSphere(Ray ray, Vector3 center, float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            return null;
        }

        var sphere = new BoundingSphere(center, radius);
        if (sphere.Contains(ray.Position) != ContainmentType.Disjoint)
        {
            return null;
        }

        return ray.Intersects(sphere);
    }
}
