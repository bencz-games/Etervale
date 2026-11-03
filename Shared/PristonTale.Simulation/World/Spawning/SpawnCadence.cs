using PristonTale.Contracts.Units;

namespace PristonTale.Simulation.World.Spawning;

public sealed class SpawnCadence
{
    private readonly ulong _mask;
    private double _frames;
    private ulong _nextFrame;

    public SpawnCadence(int mask)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mask);
        _mask = (ulong)mask;
        _nextFrame = NextEligibleFrame(0);
    }

    public bool Advance(double elapsedMs)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs <= 0)
        {
            return false;
        }

        _frames += elapsedMs * (GameUnits.TicksPerSecond / 1000.0);
        ulong currentFrame = (ulong)Math.Floor(_frames + 1e-9);
        if (currentFrame < _nextFrame)
        {
            return false;
        }

        // Skip elapsed opportunities after a hitch instead of generating a backlog of monsters.
        _nextFrame = NextEligibleFrame(currentFrame);
        return true;
    }

    private ulong NextEligibleFrame(ulong frame)
    {
        ulong candidate = frame + 1UL;
        ulong blocked = candidate & _mask;
        while (blocked != 0)
        {
            // Carry past the lowest forbidden bit and clear lower bits. Each iteration advances to a higher bit;
            // work is bounded by mask width, never by the number of skipped frames after a hitch.
            ulong bit = 1UL << System.Numerics.BitOperations.TrailingZeroCount(blocked);
            candidate = (candidate + bit) & ~(bit - 1UL);
            blocked = candidate & _mask;
        }

        return candidate;
    }
}
