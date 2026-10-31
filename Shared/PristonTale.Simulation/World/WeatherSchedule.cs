using PristonTale.Contracts.World;

namespace PristonTale.Simulation.World;

// The original's rain on the world clock: in every cycle of 8192 game minutes (about 1 h 49 min) the sky clouds over at
// minute 512, rains from 60 minutes later until 60 minutes before minute 932 (4 real minutes), then clears; never in a
// dungeon, a room, an ice field or Bellatra. The original's rain was always the same; here each cycle's number draws its
// rain (light, steady or heavy, or a storm) and its wind, and the rain swells, comes and goes and eases off as it
// falls (Varied), so every server and client restart agrees on it.
public static class WeatherSchedule
{
    public const int DefaultCycleMinutes = 8192;
    public const int CloudsMinute = 512;
    public const int CloudyMinutes = 420;
    public const int RampMinutes = 60;
    // A cycle needs room for the cloudy spell.
    public const int MinimumCycleMinutes = CloudsMinute + CloudyMinutes + 1;

    // The rain swells and eases over this long, and comes and goes in spells this long (game minutes; 24 s and 12 s).
    public const int RainSwellMinutes = 30;
    public const int SpellMinutes = 15;
    public const int DensityStep = 8;

    // The cycle's rain at its fullest: light, steady or heavy; a storm rains at the most.
    public const byte LightRain = 96;
    public const byte SteadyRain = 160;
    public const byte HeavyRain = 224;
    public const byte StormRain = 255;

    public static bool Rains(FieldDef field) =>
        field.State is not (FieldState.Dungeon or FieldState.Room or FieldState.Ice) && field.FieldCode != FieldCodes.Bellatra;

    // The weather at a game clock reading (game milliseconds), for a cycle of cycleMinutes game minutes (at least
    // MinimumCycleMinutes).
    public static WeatherState At(long gameMs, int cycleMinutes = DefaultCycleMinutes)
    {
        var minutes = Math.Max(0L, gameMs / WorldClock.MinuteMs);
        var minute = minutes % cycleMinutes;
        if (minute <= CloudsMinute || minute >= CloudsMinute + CloudyMinutes)
        {
            return WeatherState.Clear;
        }

        var draw = Mix((ulong)(minutes / cycleMinutes));
        var heading = (byte)(draw >> 8);
        var raining = minute > CloudsMinute + RampMinutes && minute < CloudsMinute + CloudyMinutes - RampMinutes;
        var (kind, intensity, wind) = (draw % 100) switch
        {
            < 30 => (WeatherKind.Rain, LightRain, 16 + (int)((draw >> 16) % 64)),
            < 65 => (WeatherKind.Rain, SteadyRain, 32 + (int)((draw >> 16) % 80)),
            < 85 => (WeatherKind.Rain, HeavyRain, 64 + (int)((draw >> 16) % 96)),
            _ => (WeatherKind.Storm, StormRain, 128 + (int)((draw >> 16) % 128))
        };
        return raining
            ? new WeatherState(kind, Varied(intensity, minutes, minute - CloudsMinute - RampMinutes, draw), heading, (byte)wind)
            : new WeatherState(WeatherKind.Overcast, 0, heading, (byte)(wind / 2));
    }

    // The rain's density as it falls: it starts at a third of the cycle's rain and swells to it over RainSwellMinutes,
    // eases off over the last RainSwellMinutes, and between them comes and goes in spells of SpellMinutes (from 60% to
    // all of it, eased from one spell's draw to the next); in steps of DensityStep, so it changes about once a spell.
    private static byte Varied(byte peak, long minutes, long sinceRain, ulong draw)
    {
        var rainMinutes = CloudyMinutes - 2 * RampMinutes;
        var swell = MathF.Min(1f, MathF.Min(sinceRain, rainMinutes - sinceRain) / (float)RainSwellMinutes);
        var spell = minutes / SpellMinutes;
        var along = Smooth((minutes % SpellMinutes) / (float)SpellMinutes);
        var spells = float.Lerp(Spell(draw, spell), Spell(draw, spell + 1), along);
        var density = peak * float.Lerp(1f / 3f, 1f, swell) * float.Lerp(0.6f, 1f, spells);
        return (byte)Math.Clamp(MathF.Round(density / DensityStep) * DensityStep, DensityStep, peak);
    }

    private static float Spell(ulong draw, long spell) => (Mix(draw ^ (ulong)spell) & 0xFFFF) / 65535f;

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    // SplitMix64's finaliser: one well-spread draw per cycle number.
    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
