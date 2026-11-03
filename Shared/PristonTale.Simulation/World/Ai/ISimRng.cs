namespace PristonTale.Simulation.World.Ai;

public interface ISimRng
{
    int NextExclusive(int minInclusive, int maxExclusive);

    int NextInclusive(int minInclusive, int maxInclusive);

    float NextUnit();

    // A roll from 0 to 99, for percent chances.
    int RollPercent() => NextExclusive(0, 100);
}

public sealed class SeededSimRng : ISimRng
{
    private readonly System.Random _random;

    public SeededSimRng(int seed) => _random = new System.Random(seed);

    public int NextExclusive(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        return _random.Next(minInclusive, maxExclusive);
    }

    public int NextInclusive(int minInclusive, int maxInclusive)
        => NextExclusive(minInclusive, maxInclusive == int.MaxValue ? int.MaxValue : maxInclusive + 1);

    public float NextUnit() => (float)_random.NextDouble();
}
