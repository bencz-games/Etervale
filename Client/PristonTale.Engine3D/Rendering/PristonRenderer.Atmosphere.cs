using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering.Atmosphere;

namespace PristonTale.Engine3D.Rendering;

// The realistic sky (RealisticSky, PristonPhysicalSky) in place of the painted domes: the sky of the hour's sun, the
// moon in its phase (MoonPhase) and the stars turning with the hour, the weather's clouds drifting with its wind, a
// rainbow when a light rain falls with the sun low behind the viewer, and the aurora over a field that shows it
// (Aurora) at night. The domes stay the classic sky.
public sealed partial class PristonRenderer
{
    // The sun's illuminance outside the atmosphere and a full moon's (lux); the sky's brightness against the painted
    // domes it replaces.
    private const float SolarIlluminance = 120000f;
    private const float MoonIlluminance = 0.3f;
    private const float SkyBrightness = 1f;
    // The night's sights at full dark, display-referred: the brightest stars, the airglow, the aurora, the moon's disc
    // (and its faint day disc).
    private const float StarLevel = 0.9f;
    private const float AirglowLevel = 0.006f;
    private const float AuroraLevel = 0.9f;
    private const float MoonDayLevel = 0.12f;
    private const float MoonNightLevel = 1.1f;
    private const float FlashCloudLevel = 4f;
    // The fair sky's share under cumulus, the share as the weather clouds over, and the clouds' drift (km per second)
    // with the strongest weather wind's share of it per world unit per second.
    private const float FairCloudCover = 0.3f;
    private const float OvercastCloudCover = 0.95f;
    private static readonly Vector2 CloudWind = new(0.012f, 0.004f);
    private const float CloudWindPerUnit = 0.00012f;
    private const float CloudFieldTile = 60f;
    // Rain closes this share of the clear sky left; the stratus deck fills the rest, so the clouds keep their shape.
    private const float RainClosing = 0.6f;

    // The covered share of the visible sky for the coverage parameter in steps of 0.05 (low views cross many km of the
    // layer, so the visible sky fills faster than the layer itself).
    private static readonly float[] SkyShareOfCoverage = [0f, 0.004f, 0.017f, 0.045f, 0.102f, 0.187f, 0.358f, 0.539f, 0.679f, 0.78f, 0.87f,
        0.939f, 0.976f, 0.99f, 0.997f, 1f];

    private readonly PristonPhysicalSky _physicalSky;
    private Vector2 _cloudDrift;

    public bool RealisticSky { get; set; } = true;

    // A game field's sky, which the realistic sky may replace; the opening and other screens keep their painted domes.
    public bool FieldSky { get; set; }
    public CloudQuality CloudQuality { get; set; } = CloudQuality.Medium;

    // 0 new moon, 0.5 full, back to 1.
    public float MoonPhase { get; set; } = 0.5f;

    // The field shows the aurora at night (the ice fields).
    public bool Aurora { get; set; }

    private bool PhysicalSkyShown => RealisticSky && FieldSky && !Underground;

    private void DriftClouds(float seconds)
    {
        var drift = CloudWind + Weather.Wind * CloudWindPerUnit;
        _cloudDrift = new Vector2((_cloudDrift.X + drift.X * seconds) % CloudFieldTile, (_cloudDrift.Y + drift.Y * seconds) % CloudFieldTile);
    }

    // Before the frame's sky draws: the tables, clouds and cloud shadow of this frame's lights and weather.
    private void UpdatePhysicalSky(Vector3 camera, float displayScale)
    {
        if (!PhysicalSkyShown)
        {
            return;
        }

        var pre = _exposure.PreExposure;
        var night = MathHelper.Clamp((-MathHelper.ToDegrees(_sunElevation) - 2f) / 10f, 0f, 1f);
        var phase = MoonPhase * MathF.Tau;
        var towardMoon = PristonDayNightCycle.TowardMoon;
        var side = Vector3.Normalize(Vector3.Cross(Vector3.Up, towardMoon));
        var fullness = (1f - MathF.Cos(phase)) / 2f;
        var cloud = MathHelper.Clamp(Weather.Cloud, 0f, 1f);
        var rain = MathHelper.Clamp(Weather.Rain, 0f, 1f);
        var flashAngle = Weather.FlashAzimuth;
        var inputs = new PristonSkyInputs(
            _towardSun,
            SolarIlluminance * pre * SkyBrightness,
            towardMoon,
            MoonIlluminance * fullness * pre * SkyBrightness,
            towardMoon * MathF.Cos(phase) + side * MathF.Sin(phase),
            MathHelper.Lerp(MoonDayLevel, MoonNightLevel, night) / displayScale,
            night * night * StarLevel / displayScale,
            TimeOfDay / 24f * MathF.Tau,
            night * AirglowLevel / displayScale,
            Aurora ? night * AuroraLevel / displayScale : 0f,
            RainbowStrength(cloud, rain, _sunElevation, Weather.SunBreak),
            0.5f * (1f - cloud),
            _renderTime / 1000f,
            displayScale,
            CloudCoverage(cloud, rain),
            1f + 1.5f * rain,
            cloud * 0.85f,
            new Vector2(Wrap(_cloudDrift.X + camera.X / PristonPhysicalSky.WorldUnitsPerKilometre),
                Wrap(_cloudDrift.Y + camera.Z / PristonPhysicalSky.WorldUnitsPerKilometre)),
            Vector3.Normalize(new Vector3(MathF.Sin(flashAngle), 0.35f, MathF.Cos(flashAngle))),
            MathHelper.Clamp(Weather.Flash, 0f, 1f) * FlashCloudLevel / displayScale,
            CloudQuality);
        // The clouds' moving shadows show under a fair or broken sky; an overcast one already dims the light evenly.
        _physicalSky.Update(inputs, camera, _sunElevation > 0f ? SunThrough(cloud, Weather.SunBreak) : 0f);
        _stateCache.InvalidateDeviceState();
    }

    // Paints the physical sky of a view into the bound sky target; false while the domes draw instead.
    private bool DrawPhysicalSky(Matrix view, Matrix projection)
    {
        if (!PhysicalSkyShown)
        {
            return false;
        }

        _physicalSky.Draw(view, projection);
        _stateCache.InvalidateDeviceState();
        return true;
    }

    // The clouds' moving shadow for the surfaces while the realistic sky draws clouds.
    private PristonCloudShadowFrame CloudShadowFrame() =>
        PhysicalSkyShown && _physicalSky.CloudsDrawn && _physicalSky.CloudShadowParams.W > 0f
            ? new PristonCloudShadowFrame(_physicalSky.CloudShadow, _physicalSky.CloudShadowParams)
            : default;

    // The coverage parameter of the clouds: the fair sky's share growing to the overcast share as the clouds gather,
    // then closing further in the rain.
    internal static float CloudCoverage(float cloud, float rain)
    {
        var share = FairCloudCover + (OvercastCloudCover - FairCloudCover) * cloud;
        var coverage = 0.05f * (SkyShareOfCoverage.Length - 1);
        for (var index = 1; index < SkyShareOfCoverage.Length; index++)
        {
            if (share <= SkyShareOfCoverage[index])
            {
                coverage = 0.05f * (index - 1 + (share - SkyShareOfCoverage[index - 1]) / (SkyShareOfCoverage[index] - SkyShareOfCoverage[index - 1]));
                break;
            }
        }

        return coverage + (1f - coverage) * RainClosing * rain;
    }

    // A light rain with the sun low enough for the bow to stand above the horizon (below 42 degrees) and shining
    // through the clouds (a broken sky, or a break passing the sun); a downpour hides it.
    internal static float RainbowStrength(float cloud, float rain, float sunElevation, float sunBreak = 0f)
    {
        var sun = SunThrough(cloud, sunBreak);
        var light = MathHelper.Clamp(sun * 3f, 0f, 1f);
        var falling = MathHelper.SmoothStep(0f, 1f, rain / 0.08f) * (1f - MathHelper.SmoothStep(0f, 1f, (rain - 0.4f) / 0.35f));
        var low = MathHelper.Clamp(sunElevation / 0.05f, 0f, 1f) * MathHelper.Clamp((0.73f - sunElevation) / 0.1f, 0f, 1f);
        return light * falling * low;
    }

    private static float Wrap(float kilometres) => (kilometres % CloudFieldTile + CloudFieldTile) % CloudFieldTile;
}
