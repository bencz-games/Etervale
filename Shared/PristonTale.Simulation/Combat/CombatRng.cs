using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Combat;

public sealed class CombatRng : ISimRng
{
    private readonly Random _random;

    public CombatRng(int seed) => _random = new Random(seed);

    public int Range(int min, int max)
    {
        if (max <= min)
            return min;
        // Inclusive of both bounds: span = (max + 1) - min, and Random.Next(span) is [0, span).
        int span = (max + 1) - min;
        return min + _random.Next(span);
    }

    public int RollPercent() => _random.Next(100);

    public int RollPerMyriad() => _random.Next(10000);

    public int NextExclusive(int minInclusive, int maxExclusive)
        => maxExclusive <= minInclusive ? minInclusive : _random.Next(minInclusive, maxExclusive);

    public int NextInclusive(int minInclusive, int maxInclusive) => Range(minInclusive, maxInclusive);

    public float NextUnit() => (float)_random.NextDouble();
}
