using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Photometric model of the scene light and the camera exposure. Light reaches the shaders as the radiance of a white
// Lambertian surface it falls on (illuminance / pi, in cd/m^2), multiplied by the frame's pre-exposure so the HDR
// targets hold values near 1 whatever the hour.
//
// Exposure follows the incident-meter calibration (ISO 2720, C = 250): EV100 = log2(E * 100 / C) for a horizontal
// illuminance E, and a scene luminance L displays as L * pi * 0.4 / 2^EV100. A white Lambertian surface under the
// metered light then displays as 1 and a surface of albedo a as a, so the art shows its authored albedo by day. The
// reflected reading of the automatic exposure uses K = 12.5: EV100 = log2(Lavg * 100 / K).
internal static class PristonPhotometry
{
    public const float IncidentCalibration = 250f;
    public const float MinEv100 = -4f;
    public const float MaxEv100 = 17f;

    // Clear-sky solar illuminance at normal incidence above the atmosphere's attenuation, lux.
    public const float SolarIlluminance = 120000f;
    // Broadband extinction per air mass of a clear, slightly hazy sky (Linke turbidity about 3).
    public const float SolarExtinction = 0.25f;

    // Diffuse daylight on a horizontal surface under a clear sky: TwilightSkyIlluminance at the geometric horizon plus
    // DaySkyIlluminance * sin(elevation)^0.8; it falls by a factor e every TwilightFalloffDegrees below the horizon.
    public const float DaySkyIlluminance = 20000f;
    public const float TwilightSkyIlluminance = 1200f;
    public const float TwilightFalloffDegrees = 1.6f;

    // The game's night is lit for play, about ten times a full moon: the night levels of the day/night gradient (and the
    // stage lamps) are taken as original light levels of NightReferenceIlluminance lux each, mapped by LevelExponent.
    public const float NightReferenceIlluminance = 10f;
    // Light levels authored for a renderer that multiplied the sRGB texel by the summed light: exponent 1.4 keeps the
    // ratio of lamps, moonlight and night sky that the game was tuned with once they are added in linear light.
    public const float LevelExponent = 1.4f;

    // Bounce light reaching downward-facing surfaces: a mid-grey ground.
    public const float GroundAlbedo = 0.2f;

    // Displayed exposure relative to the incident reading: a day key slightly above the meter (the original textures are
    // dark), and below DimStartEv a darkening that approaches MaxDimStops (1 - exp(-(DimStartEv - EV) / DimScaleEv)):
    // a low sun reads as evening rather than a second noon, and the night stays dark instead of adapting to day level.
    public const float DayKeyStops = 0.5f;
    public const float DimStartEv = 15f;
    public const float DimScaleEv = 6f;
    public const float MaxDimStops = 1.9f;

    public static float Ev100FromIlluminance(float lux) =>
        MathHelper.Clamp(MathF.Log2(MathF.Max(lux, 1e-6f) * 100f / IncidentCalibration), MinEv100, MaxEv100);

    // Display scale of a scene luminance at the given EV100, before compensation: pi * 0.4 / 2^EV100.
    public static float ExposureAt(float ev100) => MathF.PI * 0.4f / MathF.Pow(2f, ev100);

    // Stops added to the metered exposure (mirrors CompensationStops in Shaders/Passes/PostExposure.fx).
    public static float CompensationStops(float ev100, bool underground)
    {
        if (underground || ev100 >= DimStartEv)
        {
            return DayKeyStops;
        }

        return DayKeyStops - MaxDimStops * (1f - MathF.Exp(-(DimStartEv - ev100) / DimScaleEv));
    }

    public static float LevelToIlluminance(float level) =>
        level <= 0f ? 0f : MathF.Pow(level, LevelExponent) * NightReferenceIlluminance;

    public static Vector3 LevelToIlluminance(Vector3 level) =>
        new(LevelToIlluminance(level.X), LevelToIlluminance(level.Y), LevelToIlluminance(level.Z));

    // Kasten and Young (1989) relative optical air mass at an apparent elevation in radians.
    public static float AirMass(float elevation)
    {
        var degrees = MathHelper.ToDegrees(MathF.Max(elevation, 0f));
        return 1f / (MathF.Sin(MathF.Max(elevation, 0f)) + 0.50572f * MathF.Pow(degrees + 6.07995f, -1.6364f));
    }

    // Spectral extinction per air mass at the sRGB primaries (Rayleigh scattering and a light aerosol): blue is scattered
    // out of the direct beam most, so a low sun turns orange.
    private static readonly Vector3 SpectralExtinction = new(0.04f, 0.09f, 0.2f);

    // Relative transmittance of the direct beam per channel at an elevation (1 at the zenith in every channel).
    public static Vector3 SunTransmittance(float elevation)
    {
        var excess = MathF.Max(AirMass(elevation) - 1f, 0f);
        return new Vector3(
            MathF.Exp(-SpectralExtinction.X * excess),
            MathF.Exp(-SpectralExtinction.Y * excess),
            MathF.Exp(-SpectralExtinction.Z * excess));
    }

    // Direct sun at normal incidence, lux; it fades over the half degree of the disc crossing the horizon.
    public static float SunIlluminance(float elevation)
    {
        var horizon = MathHelper.Clamp((MathHelper.ToDegrees(elevation) + 0.5f) / 1.0f, 0f, 1f);
        return SolarIlluminance * MathF.Exp(-SolarExtinction * AirMass(elevation)) * horizon;
    }

    public static float SkyIlluminance(float elevation)
    {
        var degrees = MathHelper.ToDegrees(elevation);
        if (degrees >= 0f)
        {
            return TwilightSkyIlluminance + DaySkyIlluminance * MathF.Pow(MathF.Sin(elevation), 0.8f);
        }

        return TwilightSkyIlluminance * MathF.Exp(degrees / TwilightFalloffDegrees);
    }

    // The colour of a light with unit luminance: the hue of an authored colour, its brightness removed.
    public static Vector3 Hue(Vector3 colour)
    {
        var luminance = Luminance(colour);
        return luminance > 1e-5f ? colour / luminance : Vector3.One;
    }

    public static float Luminance(Vector3 colour) => 0.2126f * colour.X + 0.7152f * colour.Y + 0.0722f * colour.Z;
}
