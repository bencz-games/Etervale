using PristonTale.Simulation.World.Ai;

namespace PristonTale.Tests;

// Always the lowest value of the range asked (Low), or always the highest (High).
internal sealed class BoundRng(bool high) : ISimRng
{
    public static BoundRng Low => new(false);
    public static BoundRng High => new(true);

    public int NextInclusive(int minInclusive, int maxInclusive) => high ? maxInclusive : minInclusive;
    public int NextExclusive(int minInclusive, int maxExclusive) => high ? maxExclusive - 1 : minInclusive;
    public float NextUnit() => high ? 0.999f : 0f;
}
