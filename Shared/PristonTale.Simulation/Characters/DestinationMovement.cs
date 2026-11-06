using System.Numerics;

namespace PristonTale.Simulation.Characters;

public readonly record struct DestinationStep(Vector2 Position, Vector2 Direction, bool Arrived);

public static class DestinationMovement
{
    private const float ArrivalDistanceSquared = 0.0001f;

    public static bool HasArrived(Vector2 position, Vector2 destination)
    {
        return Vector2.DistanceSquared(position, destination) <= ArrivalDistanceSquared;
    }

    public static bool WithinDistance(Vector2 position, Vector2 destination, float distance)
    {
        return float.IsFinite(distance) && distance >= 0f && Vector2.DistanceSquared(position, destination) <= distance * distance;
    }

    public static DestinationStep Advance(Vector2 position, Vector2 destination, float speed, float dt)
    {
        var offset = destination - position;
        var distanceSquared = offset.LengthSquared();
        if (distanceSquared <= ArrivalDistanceSquared)
        {
            return new DestinationStep(destination, Vector2.Zero, true);
        }

        if (!float.IsFinite(distanceSquared) || !float.IsFinite(speed) || !float.IsFinite(dt) || speed <= 0f || dt <= 0f)
        {
            return new DestinationStep(position, Vector2.Zero, false);
        }

        var distance = MathF.Sqrt(distanceSquared);
        var direction = offset / distance;
        var step = speed * dt;
        if (step >= distance)
        {
            return new DestinationStep(destination, direction, true);
        }

        return new DestinationStep(position + direction * step, direction, false);
    }
}
