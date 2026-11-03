using System.Numerics;
using PristonTale.Contracts.World;

namespace PristonTale.Simulation.Physics;

public readonly struct EntityNeighbour
{
    public readonly Vector3 Position;
    public readonly float Radius;

    public EntityNeighbour(Vector3 position, float radius)
    {
        Position = position;
        Radius = radius;
    }

    public EntityNeighbour(Vector3 position, EntityKind kind)
    {
        Position = position;
        Radius = EntityCollisionRadii.ForKind(kind);
    }

    // Whether a body at position stands inside this one: within its radius on the ground plane and in height.
    public bool Overlaps(Vector3 position) => Planar.Overlaps(Position, position, Radius);
}
