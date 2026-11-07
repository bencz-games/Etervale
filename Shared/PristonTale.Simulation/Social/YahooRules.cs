namespace PristonTale.Simulation.Social;

// The yahoo cheer. A chat line that is one of its words plays it instead of being spoken, and for a minute after, Space
// plays it again for 3% of the maximum stamina (refused while stamina is not above that). It starts only from standing,
// walking or running; a firecracker and the tiger capsule queue it after the motion in progress.
public static class YahooRules
{
    public const long KeyWindowMs = 60_000;
    public const int KeyStaminaPercent = 3;

    private static readonly string[] Words = ["yahoo", "/yahoo", "Yes!"];

    public static bool IsWord(string text)
    {
        var trimmed = text.AsSpan().Trim();
        foreach (var word in Words)
        {
            if (trimmed.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static int KeyStaminaCost(int maxStamina) => maxStamina * KeyStaminaPercent / 100;

    // A cheer heals the player's clan's monsters nearby (ClanRules.NearbyMonsters), sharing a quarter of the player's
    // level among them (at least 1 each).
    public static int ClanMonsterHeal(int level, int count) => Math.Max(1, (level >> 2) / Math.Max(1, count));
}
