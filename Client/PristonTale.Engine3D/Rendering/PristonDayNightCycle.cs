using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering;

// The light of one hour in photometric units (Lighting/PristonPhotometry): the direct light (the sun by day, the moon at
// night) at normal incidence and the diffuse sky light on a horizontal surface, in lux per colour channel.
internal readonly record struct PristonSkyState(
    Vector3 SunDirection,
    Vector3 SunIlluminance,
    Vector3 SkyIlluminance,
    Vector3 SkyColor,
    float LampFactor,
    Vector3 TowardSun,
    float SunElevation)
{
    // Illuminance on a horizontal surface: what an incident meter at the camera reads.
    public float HorizontalIlluminance =>
        PristonPhotometry.Luminance(SunIlluminance) * MathF.Max(-SunDirection.Y, 0f) + PristonPhotometry.Luminance(SkyIlluminance);
}

internal sealed class PristonDayNightCycle
{
    // The sun rises at 05:30 and sets at 18:45, so the gradient's warm 17:30 key and the dusk skies meet a low sun
    // rather than one already on the horizon.
    public const float Sunrise = 5.5f;
    public const float Sunset = 18.75f;
    // Tilt of the sun's path toward -Z (the path's horizontal offset, as a share of the east-west radius).
    private const float PathTilt = 0.35f;
    // The moon lights the night from a fixed, high position with the sunlight its grey surface reflects, slightly
    // reddened. The gradient's blue night levels give its brightness only: the blue of a moonlit night is the eye's (the
    // final pass's mesopic vision), and a blue light would give every surface a blue specular sheen its diffuse colour
    // does not have.
    internal static readonly Vector3 TowardMoon = Vector3.Normalize(new Vector3(-0.45f, 0.78f, -0.42f));
    private static readonly Vector3 MoonlightHue = PristonPhotometry.Hue(new Vector3(1f, 0.96f, 0.9f));
    // Colour of clear-sky diffuse light (about 10000 K) and its share in the daytime sky light.
    private static readonly Vector3 ClearSkyHue = PristonPhotometry.Hue(new Vector3(0.78f, 0.9f, 1.2f));
    private const float SkylightBlueShare = 0.6f;
    private const float LowSunBlueShare = 0f;
    // The night's moon and sky against the stage lamps: dimmer than the authored levels, so the lamp pools stand out
    // once the exposure has adapted to the night.
    private const float NightLevelScale = 0.5f;

    private DayNightSettings _settings;

    public PristonDayNightCycle(DayNightSettings settings = null)
    {
        _settings = settings ?? DayNightSettings.Default;
        DayLengthSeconds = _settings.DayLengthSeconds;
        TimeOfDay = _settings.StartTimeOfDay;
    }

    public void Configure(DayNightSettings settings)
    {
        _settings = settings ?? DayNightSettings.Default;
        DayLengthSeconds = _settings.DayLengthSeconds;
        _cachedTime = float.NaN;
    }

    public float DayLengthSeconds { get; set; }

    public float TimeOfDay { get; private set; }

    // The hue of the gradient's midnight light: the colour the eye gives the night (the final pass's mesopic vision).
    public Vector3 NightHue => PristonPhotometry.Hue(PristonPhotometry.LevelToIlluminance(SampleGradient(0f).Sun));

    private PristonSkyState _cachedState;
    private float _cachedTime = float.NaN;

    public void Update(float deltaSeconds)
    {
        if (DayLengthSeconds <= 0f)
            return;
        var hoursPerSecond = 24f / DayLengthSeconds;
        TimeOfDay = Wrap24(TimeOfDay + deltaSeconds * hoursPerSecond);
    }

    public void SetTime(float hours) => TimeOfDay = Wrap24(hours);

    public PristonSkyState Evaluate()
    {
        if (TimeOfDay == _cachedTime)
            return _cachedState;
        _cachedTime = TimeOfDay;
        _cachedState = EvaluateUncached();
        return _cachedState;
    }

    // Direction toward the sun: the day arc runs from east (+X) at sunrise over the zenith side to west at sunset and
    // continues below the horizon through the night.
    internal static Vector3 TowardSunAt(float hour)
    {
        var dayLength = Sunset - Sunrise;
        var since = Wrap24(hour - Sunrise);
        var angle = since <= dayLength
            ? since / dayLength * MathHelper.Pi
            : MathHelper.Pi + (since - dayLength) / (24f - dayLength) * MathHelper.Pi;
        return Vector3.Normalize(new Vector3(MathF.Cos(angle), MathF.Sin(angle), -PathTilt));
    }

    private PristonSkyState EvaluateUncached()
    {
        var towardSun = TowardSunAt(TimeOfDay);
        var elevation = MathF.Asin(MathHelper.Clamp(towardSun.Y, -1f, 1f));
        var (sunTint, ambientTint, sky) = SampleGradient(TimeOfDay);

        var sunLux = PristonPhotometry.SunIlluminance(elevation);
        var skyLux = PristonPhotometry.SkyIlluminance(elevation);
        // Night: the gradient's levels (moon and night sky), never below them, so twilight hands over continuously.
        var moonLevel = MoonlightHue * (PristonPhotometry.Luminance(PristonPhotometry.LevelToIlluminance(sunTint)) * NightLevelScale);
        var nightSky = PristonPhotometry.LevelToIlluminance(ambientTint) * NightLevelScale;
        var moonVisibility = MathHelper.Clamp(-(MathHelper.ToDegrees(elevation) + 2f) / 6f, 0f, 1f);
        // The gradient's hue reddened by the air the low sun shines through (PristonPhotometry.SunTransmittance).
        var sunHue = PristonPhotometry.Hue(sunTint * PristonPhotometry.SunTransmittance(elevation));
        // Daylight from the blue sky is cooler than the gradient's grey ambient: shade reads blue against the sun.
        // Toward the horizon the sky's own glow warms that light again.
        var daylight = MathHelper.Clamp((MathHelper.ToDegrees(elevation) + 4f) / 10f, 0f, 1f);
        var highSun = MathHelper.Clamp(MathHelper.ToDegrees(elevation) / 30f, 0f, 1f);
        var blueShare = MathHelper.Lerp(LowSunBlueShare, SkylightBlueShare, highSun) * daylight;
        var skyHue = PristonPhotometry.Hue(Vector3.Lerp(PristonPhotometry.Hue(ambientTint), ClearSkyHue, blueShare));
        var skyIlluminance = Vector3.Max(skyHue * skyLux, nightSky);

        Vector3 sunDirection;
        Vector3 sunIlluminance;
        var moonLux = PristonPhotometry.Luminance(moonLevel) * moonVisibility;
        if (sunLux >= moonLux)
        {
            sunDirection = -towardSun;
            sunIlluminance = sunHue * sunLux;
        }
        else
        {
            sunDirection = -TowardMoon;
            sunIlluminance = moonLevel * moonVisibility;
        }

        // Lamps light up as the sun goes down: off above 4 degrees, full 2 degrees below the horizon.
        var lampFactor = MathHelper.Clamp((4f - MathHelper.ToDegrees(elevation)) / 6f, 0f, 1f);
        return new PristonSkyState(sunDirection, sunIlluminance, skyIlluminance, sky, lampFactor, towardSun, towardSun.Y);
    }

    private (Vector3 Sun, Vector3 Ambient, Vector3 Sky) SampleGradient(float hour)
    {
        var gradient = _settings.Gradient;
        for (var i = 0; i < gradient.Count - 1; i++)
        {
            var a = gradient[i];
            var b = gradient[i + 1];
            if (hour >= a.Hour && hour <= b.Hour)
            {
                var span = b.Hour - a.Hour;
                var t = span > 0f ? (hour - a.Hour) / span : 0f;
                return (Vector3.Lerp(a.Sun, b.Sun, t), Vector3.Lerp(a.Ambient, b.Ambient, t), Vector3.Lerp(a.Sky, b.Sky, t));
            }
        }

        var last = gradient[^1];
        return (last.Sun, last.Ambient, last.Sky);
    }

    private static float Wrap24(float hours) => ((hours % 24f) + 24f) % 24f;
}
