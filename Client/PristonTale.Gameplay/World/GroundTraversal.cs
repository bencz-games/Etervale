using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.World;

public delegate bool GroundFloorQuery(Vector3 position, float referenceHeight, out float height);

public static class GroundTraversal
{
    public const float StepUp = Simulation.World.FieldTerrain.StepUp;
    public const float VisualOffset = 2f;
    private const float SampleDistance = 4f;
    private const int MaximumSamples = 1024;

    public static Vector3 ToVisualPosition(Vector3 bodyPosition)
    {
        return bodyPosition + Vector3.Up * VisualOffset;
    }

    public static bool TryFollow(Vector3 feet, Vector2 destination, GroundFloorQuery floor, out Vector3 reached) =>
        TryFollow(feet, destination, floor, out reached, out _);

    // largestDrop: the largest fall between two samples four units apart, which tells a ledge from a slope however far
    // a frame carries the body.
    public static bool TryFollow(Vector3 feet, Vector2 destination, GroundFloorQuery floor, out Vector3 reached, out float largestDrop)
    {
        reached = feet;
        largestDrop = 0f;
        var start = new Vector2(feet.X, feet.Z);
        var distance = Vector2.Distance(start, destination);
        if (!float.IsFinite(distance) || !float.IsFinite(feet.Y) || distance > SampleDistance * MaximumSamples)
        {
            return false;
        }

        var samples = Math.Max(1, (int)MathF.Ceiling(distance / SampleDistance));
        for (var sample = 1; sample <= samples; sample++)
        {
            var planar = Vector2.Lerp(start, destination, sample / (float)samples);
            var proposed = new Vector3(planar.X, reached.Y, planar.Y);
            if (!floor(proposed, reached.Y, out var height) || !float.IsFinite(height) || height - reached.Y > StepUp)
            {
                return false;
            }

            largestDrop = MathF.Max(largestDrop, reached.Y - height);
            reached = new Vector3(planar.X, height, planar.Y);
        }

        return true;
    }

    public static bool CanReachSurface(Vector3 feet, Vector3 surface, GroundFloorQuery floor)
    {
        return TryFollow(feet, new Vector2(surface.X, surface.Z), floor, out var reached)
            && MathF.Abs(reached.Y - surface.Y) <= 0.1f;
    }
}
