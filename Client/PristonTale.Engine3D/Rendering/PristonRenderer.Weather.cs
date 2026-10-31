using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Weather;

namespace PristonTale.Engine3D.Rendering;

// The weather's share of the frame (Weather): an overcast sky shows the rain dome at every hour, as the original
// switched to it; the gathering clouds hide the sun (an overcast sky and its rain light the scene only from the sky,
// diffuse and without shadows, save the brief breaks a light rain opens), dim and grey the sky light and darken the
// metered scene; rain thickens the distance fog and lights the town lamps, fully in a downpour; a lightning flash lights the sky dome and adds a burst of cold sky light the exposure does not follow.
public sealed partial class PristonRenderer
{
    // At full cloud: the sky light kept and its share turned grey, the scene's brightness against a clear sky (rain
    // darkens it further), the lamps' least factor in full rain. The direct sun keeps the sky's clear share to the
    // power SunVeil (none under a full cover), or SunBreakLight of it while a break in the clouds passes the sun.
    private const float SunVeil = 2f;
    private const float SunBreakLight = 0.7f;
    private const float OvercastSky = 0.7f;
    private const float OvercastGrey = 0.6f;
    private const float OvercastBrightness = 0.62f;
    private const float RainBrightness = 0.85f;
    private const float RainLamps = 1f;
    // A full lightning flash adds this share of the metered illuminance as sky light (about twice a storm's light at
    // its peak, at any hour), in this colour.
    private const float FlashShare = 0.6f;
    private static readonly Vector3 FlashColour = new(0.82f, 0.88f, 1f);
    // The sky dome at a full flash is this many times as bright.
    private const float FlashSky = 3f;
    // In full rain the fog starts this share of its distance and grows this many times as dense; a mist of strength 1
    // at its thickest brings it to 1 / (1 + MistFogNear) of its distance, makes it 1 + MistFogDensity times as dense and
    // the volumetric light's medium 1 + MistShafts times, so the low morning sun draws shafts through it.
    private const float MistFogNear = 3f;
    private const float MistFogDensity = 3f;
    private const float MistShafts = 6f;
    private const float MistStart = 4.5f;
    private const float MistPeak = 6f;
    private const float MistEnd = 9f;
    private const float RainFogStart = 0.35f;
    private const float RainFogDensity = 4f;
    // A drop's light: the sky light plus this share of the sun on a white surface, and at least the faint light of a
    // white surface at this share of the metered level (a night's rain still catches the town's light).
    private const float RainSunShare = 0.25f;
    private const float RainLeastLight = 0.07f;
    private const float OvercastSkyCover = 0.5f;

    private static readonly int RainSkyProfile = PristonSkyProfiles.Resolve("FIELD_BACKIMAGE_RAIN");

    public PristonWeather Weather { get; set; }

    // The strength of the field's mist from 4:30 to 9:00, thickest at 6:00 (1 a forest's, 0 none).
    public float MorningMist { get; set; }

    // The radiance of a lit raindrop in the scene's pre-exposed units, set with each frame's lighting.
    public Vector3 RainRadiance { get; private set; }

    // The sky profile of the hour, or the rain dome under the clouds.
    private int SkyProfileNow => WeatherSkyProfile(SkyProfiles.Select(TimeOfDay));

    private int WeatherSkyProfile(int profile) =>
        !PhysicalSkyShown && Weather.Cloud >= OvercastSkyCover && (Sky is null || RainSkyProfile < Sky.ProfileCount) ? RainSkyProfile : profile;

    // The clouds' and the flash's light: the sun and sky illuminance and the horizontal illuminance lit, the one the
    // exposure meters (without the flash and above the lit one by the weather's darkening) and the lamps' factor.
    private (Vector3 Sun, Vector3 Sky, float Horizontal, float Metered, float Lamps) WeatherLight(Vector3 sun, Vector3 sky, float horizontal,
        float lamps)
    {
        var cloud = MathHelper.Clamp(Weather.Cloud, 0f, 1f);
        var rain = MathHelper.Clamp(Weather.Rain, 0f, 1f);
        if (cloud <= 0f && Weather.Flash <= 0f)
        {
            return (sun, sky, horizontal, horizontal, lamps);
        }

        var sunScale = SunThrough(cloud, Weather.SunBreak);
        var skyScale = MathHelper.Lerp(1f, OvercastSky, cloud);
        var grey = new Vector3(PristonPhotometry.Luminance(sky));
        var litSky = Vector3.Lerp(sky, grey, OvercastGrey * cloud) * skyScale;
        var lit = horizontal * (sunScale * Share(sun, sky) + skyScale * (1f - Share(sun, sky)));
        var brightness = MathHelper.Lerp(1f, OvercastBrightness, cloud) * MathHelper.Lerp(1f, RainBrightness, rain);
        var metered = lit / brightness;
        var flash = MathHelper.Clamp(Weather.Flash, 0f, 1f) * FlashShare * metered;
        return (sun * sunScale, litSky + FlashColour * flash, lit + flash, metered, MathF.Max(lamps, RainLamps * rain));
    }

    // The sun's share of the horizontal illuminance.
    private static float Share(Vector3 sun, Vector3 sky)
    {
        var total = PristonPhotometry.Luminance(sun) + PristonPhotometry.Luminance(sky);
        return total > 0f ? PristonPhotometry.Luminance(sun) / total : 0f;
    }

    // The volumetric light's medium: thicker in the morning mist.
    private float VolumetricDensityScale => 1f + MistShafts * Mist;

    // The share of the direct sun that reaches the ground through the clouds.
    internal static float SunThrough(float cloud, float sunBreak) =>
        MathF.Max(MathF.Pow(1f - MathHelper.Clamp(cloud, 0f, 1f), SunVeil), SunBreakLight * MathHelper.Clamp(sunBreak, 0f, 1f));

    // How much brighter the sky dome draws this frame (1 without a flash).
    private float SkyFlash => 1f + (FlashSky - 1f) * MathHelper.Clamp(Weather.Flash, 0f, 1f);

    private PristonDistanceFog WeatherFog(PristonDistanceFog fog)
    {
        var rain = MathHelper.Clamp(Weather.Rain, 0f, 1f);
        var mist = Mist;
        return rain <= 0f && mist <= 0f || Underground
            ? fog
            : fog with
            {
                StartDistance = fog.StartDistance * MathHelper.Lerp(1f, RainFogStart, rain) / (1f + MistFogNear * mist),
                Density = fog.Density * MathHelper.Lerp(1f, RainFogDensity, rain) * (1f + MistFogDensity * mist)
            };
    }

    // The morning mist now: its strength times the hour's share (0 outside 4:30 to 9:00, 1 at 6:00).
    private float Mist
    {
        get
        {
            if (MorningMist <= 0f)
            {
                return 0f;
            }

            var hour = TimeOfDay;
            var rising = MathHelper.Clamp((hour - MistStart) / (MistPeak - MistStart), 0f, 1f);
            var lifting = MathHelper.Clamp((MistEnd - hour) / (MistEnd - MistPeak), 0f, 1f);
            return MorningMist * MathHelper.SmoothStep(0f, 1f, MathF.Min(rising, lifting));
        }
    }
}
