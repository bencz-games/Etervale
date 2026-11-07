using System.Globalization;

namespace PristonTale.Simulation.World.Bellatra;

// Bellatra's clans count by the server's local day, as yyyymmdd (none: 0).
public static class BellatraDays
{
    public static int Of(DateTimeOffset local) => Of(DateOnly.FromDateTime(local.DateTime));

    // The day before, 0 for none or a day that is no date.
    public static int Before(int day) =>
        DateOnly.TryParseExact(day.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Of(date.AddDays(-1))
            : 0;

    private static int Of(DateOnly date) => date.Year * 10_000 + date.Month * 100 + date.Day;
}
