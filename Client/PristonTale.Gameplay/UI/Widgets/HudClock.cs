using System.Globalization;

namespace PristonTale.Gameplay.UI.Widgets;

// A countdown as the HUD shows it: minutes and two-digit seconds.
public static class HudClock
{
    public static string MinutesSeconds(long seconds) => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:D2}");
}
