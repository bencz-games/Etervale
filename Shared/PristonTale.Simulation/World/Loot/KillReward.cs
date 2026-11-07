namespace PristonTale.Simulation.World.Loot;

public static class KillReward
{
    // The experience event is a plain percent of each kill (100 = x1, 500 = x5); a setting that is not positive is x1.
    public static int EventPercent(int setting) => setting > 0 ? setting : 100;

    public static ulong ExpForKill(ulong baseExp, int monsterLevel, int playerLevel, int multiplier = 1, int eventPercent = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(multiplier);
        long difference = Math.Abs((long)monsterLevel - playerLevel);
        int percent = difference switch
        {
            <= 10 => 100,
            <= 20 => 85,
            <= 30 => 70,
            _ => 55
        };
        // The event percent precedes the level gap's; computed in 128 bits and saturated, so no award wraps or throws.
        var total = (UInt128)baseExp * (uint)multiplier * (uint)EventPercent(eventPercent) / 100 * (uint)percent / 100;
        return total > ulong.MaxValue ? ulong.MaxValue : (ulong)total;
    }

    public static bool FirstAttackerStealsCredit(
        bool firstAttackerEqualsKiller,
        int firstAttackerAccumulatedDamage,
        int monsterMaxLife,
        bool firstAttackerStillEngaged)
    {
        if (firstAttackerEqualsKiller || !firstAttackerStillEngaged)
            return false;
        return firstAttackerAccumulatedDamage >= (monsterMaxLife * 40) / 100;
    }

    public const long FirstAttackerDisengageMs = 15000;
}
