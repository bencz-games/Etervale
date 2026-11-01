namespace PristonTale.Contracts.World;

// The periods of the game's day, which the server's rules and every client's sky share: day from DayStart, the evening
// glow from GlowStart, night from NightStart to the next DayStart.
public static class GameHours
{
    public const int DayStart = 4;
    public const int GlowStart = 22;
    public const int NightStart = 23;

    public static bool IsNight(float hours) => hours < DayStart || hours >= NightStart;
}
