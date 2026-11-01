using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Tests;

// CPU side of the photometric lighting and the post chain: the day's light model, the exposure calibration, the
// quality presets and their validation, the temporal jitter and the colour grade's LUT parsing.
internal static class PhotorealTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("graphics settings validate post options and presets set every rendering option", PresetsAsync);
        yield return ("the sun keeps the game's hours, low and warm before the evening glow, and the moon lights the night", DaylightAsync);
        yield return ("incident exposure shows a white surface under the metered light at the day key", ExposureAsync);
        yield return ("temporal jitter is centred and the projection moves by whole clip offsets", JitterAsync);
        yield return ("scene depth is reversed: the near plane is 1, the far plane 0, and both planes are recoverable", ReversedDepthAsync);
        yield return ("colour grade cube files parse and interpolate trilinearly", CubeLutAsync);
        yield return ("shadow cascades keep their size as the camera turns", CascadesAsync);
        yield return ("lamp shadow rows stay with their lamps and refresh in turn", LampRowsAsync);
        yield return ("the split-sum BRDF table conserves energy and reflects fully at grazing smooth angles", BrdfAsync);
    }

    private static Task PresetsAsync()
    {
        var invalid = new ClientSettings
        {
            Quality = (GraphicsQuality)99,
            AntiAliasing = (AntiAliasingMode)99,
            ExposureCompensation = 9f,
            Tonemapper = (TonemapperMode)99,
            Bloom = (BloomQuality)99,
            Sharpening = float.NaN,
            LocalLightLimit = 5,
            ShadowCascades = 7,
            ShadowFilter = (ShadowFilterMode)9,
            LampShadowCount = 3
        };
        Assert.True(invalid.Validate(NullClientLog.Instance) == new ClientSettings(), "invalid post options did not restore the defaults");
        var defaults = new ClientSettings();
        Assert.True(defaults.WithQuality(GraphicsQuality.High) == defaults, "the defaults are not the High preset");
        var low = defaults.WithQuality(GraphicsQuality.Low);
        Assert.True(low.AntiAliasing == AntiAliasingMode.Off && low.LocalLightLimit == 8 && low.ShadowMapSize == 2048 && low.Bloom == BloomQuality.Off && low.ShadowCascades == 2 && !low.AmbientOcclusion,
            "the Low preset lost its budget options");
        Assert.True(defaults.WithQuality(GraphicsQuality.Medium).AntiAliasing == AntiAliasingMode.Smaa, "Medium is not SMAA");
        Assert.True(defaults.WithQuality(GraphicsQuality.Ultra).ShadowUpdateRate == 0, "Ultra does not refresh shadows every frame");
        Assert.True(defaults.WithQuality(GraphicsQuality.Ultra).ScreenSpaceReflections && !defaults.ScreenSpaceReflections,
            "screen-space reflections are not an Ultra option");
        Assert.True(defaults.VolumetricLight && !low.VolumetricLight && !low.CalibrationSpheres, "volumetric light presets");
        foreach (var quality in Enum.GetValues<GraphicsQuality>())
        {
            var preset = low.WithQuality(quality);
            Assert.True(preset.Validate(NullClientLog.Instance) == preset, $"preset {quality} does not validate");
            Assert.True(preset.MaxFps == low.MaxFps && preset.WindowWidth == low.WindowWidth, $"preset {quality} changed display options");
        }

        Assert.True(!defaults.HasSameRendering(defaults with { Tonemapper = TonemapperMode.AgX }), "a tonemapper change is not a rendering change");
        Assert.True(defaults.HasSameRendering(defaults with { MusicEnabled = false }), "audio counts as a rendering change");
        return Task.CompletedTask;
    }

    private static Task DaylightAsync()
    {
        // The sun keeps the game's hours: low before the evening glow, below the horizon from the night's start to dawn.
        var lowElevation = MathHelper.ToDegrees(MathF.Asin(PristonDayNightCycleProbe.TowardSun(21.5f).Y));
        Assert.True(lowElevation is > 6f and < 14f, $"the 21:30 sun stands at {lowElevation:F1} degrees");
        Assert.True(PristonDayNightCycleProbe.TowardSun(23f).Y < -0.2f && PristonDayNightCycleProbe.TowardSun(3.9f).Y < 0f
            && PristonDayNightCycleProbe.TowardSun(4.25f).Y > 0f, "the sun is up in the game's night or down after its dawn");
        Assert.True(PristonDayNightCycleProbe.TowardSun(12f).Y > 0.85f, "the noon sun is not high");
        Assert.True(PristonDayNightCycleProbe.TowardSun(0f).Y < -0.5f, "the midnight sun is not below the horizon");
        var noon = PristonPhotometry.SunIlluminance(MathHelper.ToRadians(60f));
        var low = PristonPhotometry.SunIlluminance(MathHelper.ToRadians(10f));
        Assert.True(noon is > 80000f and < 110000f && low < noon * 0.5f && PristonPhotometry.SunIlluminance(MathHelper.ToRadians(-2f)) == 0f,
            $"sun illuminance noon {noon}, low {low}");
        Assert.True(PristonPhotometry.SkyIlluminance(MathHelper.ToRadians(-10f)) < 5f && PristonPhotometry.SkyIlluminance(MathHelper.ToRadians(45f)) > 10000f,
            "sky light does not fade through twilight");
        var night = PristonDayNightCycleProbe.State(23.5f);
        var dusk = PristonDayNightCycleProbe.State(21.5f);
        Assert.True(night.LampFactor == 1f && dusk.LampFactor == 0f, "lamps are not off in the 21:30 sun and on at night");
        Assert.True(night.SunDirection.Y < 0f && night.HorizontalIlluminance is > 0.5f and < 20f, $"night light {night.HorizontalIlluminance} lux");
        Assert.True(dusk.SunIlluminance.X > dusk.SunIlluminance.Z * 2.5f, "the low sun is not warm");
        // Moonlight is sunlight off a grey surface: a blue moon gives every surface a blue sheen its diffuse colour lacks.
        // The night's blue is the eye's (mesopic vision in the final pass), in the hue the night light was authored with.
        var moonHue = night.SunIlluminance.Z / night.SunIlluminance.X;
        Assert.True(moonHue is > 0.8f and < 1.05f, $"moonlight blue/red {moonHue:F2}");
        var nightHue = new PristonDayNightCycle().NightHue;
        Assert.True(nightHue.Z > nightHue.X * 1.3f && MathF.Abs(PristonPhotometry.Luminance(nightHue) - 1f) < 1e-3f, $"night hue {nightHue}");
        var dim = PristonDisplayTransform.Mesopic(new Vector3(0.05f), 1f, nightHue);
        var bright = PristonDisplayTransform.Mesopic(new Vector3(500f), 1f, nightHue);
        Assert.True(dim.Z > dim.X * 1.2f && MathF.Abs(bright.Z / bright.X - 1f) < 0.01f, $"mesopic vision: dim grey {dim}, bright grey {bright}");
        var highSun = PristonPhotometry.SunTransmittance(MathHelper.ToRadians(70f));
        var lowSun = PristonPhotometry.SunTransmittance(MathHelper.ToRadians(8f));
        Assert.True(highSun.Z > 0.95f && lowSun.X > lowSun.Y && lowSun.Y > lowSun.Z && lowSun.Z < 0.5f,
            $"air reddens the low sun: {lowSun}, high {highSun}");
        return Task.CompletedTask;
    }

    private static Task ExposureAsync()
    {
        var exposure = PristonFrameExposure.ForIlluminance(100000f, false, 0f, 40f);
        // Pre-exposed white Lambertian under the metered light: E / pi * preExposure = 1.
        Assert.Near(1f, 100000f / MathF.PI * exposure.PreExposure, 1e-3f);
        Assert.Near(MathF.Pow(2f, PristonPhotometry.DayKeyStops), exposure.DisplayScale, 1e-4f);
        Assert.True(exposure.EmissionScale >= 1f / exposure.DisplayScale - 1e-6f, "emission below its authored level by day");
        var night = PristonFrameExposure.ForIlluminance(2f, false, 0f, 40f);
        Assert.True(night.DisplayScale < exposure.DisplayScale * 0.4f, "the night is exposed like the day");
        Assert.True(night.EmissionScale > 1f / night.DisplayScale, "emission does not glow at night");
        var underground = PristonFrameExposure.ForIlluminance(2f, true, 0f, 40f);
        Assert.Near(exposure.DisplayScale, underground.DisplayScale, 1e-4f);
        var inputs = new PristonExposureInputs(exposure.PreExposure, exposure.IncidentEv100, 0f, 0f, false);
        Assert.Near(exposure.DisplayScale, PristonPostProcessor.FixedMultiplier(inputs), 1e-3f);
        return Task.CompletedTask;
    }

    private static Task JitterAsync()
    {
        var sum = Vector2.Zero;
        for (var frame = 0; frame < PristonTemporalAntiAliasing.JitterSamples; frame++)
        {
            var jitter = PristonTemporalAntiAliasing.JitterPixels(frame);
            Assert.True(MathF.Abs(jitter.X) < 0.5f && MathF.Abs(jitter.Y) < 0.5f, $"jitter {jitter} leaves the pixel");
            sum += jitter;
        }

        Assert.True(sum.Length() < 1e-4f, $"the jitter sequence is not centred: {sum}");
        var projection = Matrix.CreatePerspectiveFieldOfView(1f, 16f / 9f, 1f, 1000f);
        var offset = PristonTemporalAntiAliasing.PixelsToClip(new Vector2(0.5f, 0.25f), 1920, 1080);
        var jittered = PristonTemporalAntiAliasing.Jitter(projection, offset);
        var point = new Vector4(3f, -2f, -50f, 1f);
        var before = Vector4.Transform(point, projection);
        var after = Vector4.Transform(point, jittered);
        Assert.Near(before.X / before.W + offset.X, after.X / after.W, 1e-5f);
        Assert.Near(before.Y / before.W + offset.Y, after.Y / after.W, 1e-5f);
        return Task.CompletedTask;
    }

    private static Task ReversedDepthAsync()
    {
        const float near = 1f;
        const float far = 20000f;
        var projection = PristonDepth.Perspective(1f, 16f / 9f, near, far);
        float Depth(float distance, Matrix matrix)
        {
            var clip = Vector4.Transform(new Vector4(0f, 0f, -distance, 1f), matrix);
            return clip.Z / clip.W;
        }

        Assert.Near(1f, Depth(near, projection), 1e-6f);
        Assert.Near(0f, Depth(far, projection), 1e-6f);
        Assert.True(Depth(200f, projection) > Depth(201f, projection), "nearer is not greater");
        Assert.Near(near, PristonDepth.NearPlane(projection), 1e-3f);
        Assert.Near(far, PristonDepth.FarPlane(projection), 1f);

        // x, y and w match the ordinary projection, so screen positions and everything built on them are unchanged.
        var ordinary = Matrix.CreatePerspectiveFieldOfView(1f, 16f / 9f, near, far);
        var point = new Vector4(3f, -2f, -50f, 1f);
        var reversed = Vector4.Transform(point, projection);
        var expected = Vector4.Transform(point, ordinary);
        Assert.Near(expected.X / expected.W, reversed.X / reversed.W, 1e-6f);
        Assert.Near(expected.Y / expected.W, reversed.Y / reversed.W, 1e-6f);

        // The sky's projection never clips at distance and stays inside the depth range.
        var sky = PristonDepth.InfiniteFar(projection);
        var distant = Depth(1.0e7f, sky);
        Assert.True(distant > 0f && distant < 1e-6f, $"the infinite-far sky depth is {distant}");
        Assert.Near(1f, Depth(near, sky), 1e-6f);
        return Task.CompletedTask;
    }

    private static Task CascadesAsync()
    {
        var cascades = new PristonShadowCascades();
        cascades.Configure(4, 4096);
        Assert.True(cascades.TileSize == 2048 && cascades.Splits[3] == PristonShadowCascades.ShadowDistance, "cascade layout");
        for (var i = 1; i < 4; i++)
        {
            Assert.True(cascades.Splits[i] > cascades.Splits[i - 1], "cascade splits do not increase");
        }

        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(50f), 16f / 9f, 10f, 20000f);
        var light = Vector3.Normalize(new Vector3(-0.4f, -0.8f, -0.3f));
        var eye = new Vector3(100f, 500f, 300f);
        cascades.Fit(1, Matrix.CreateLookAt(eye, eye + new Vector3(1f, -0.6f, 0f), Vector3.Up), projection, light);
        var texel = cascades.TexelWorldSizes[1];
        cascades.Fit(1, Matrix.CreateLookAt(eye, eye + new Vector3(-0.3f, -0.9f, 0.8f), Vector3.Up), projection, light);
        Assert.Near(texel, cascades.TexelWorldSizes[1], 1e-4f);
        return Task.CompletedTask;
    }

    private static Task LampRowsAsync()
    {
        using var shadows = new PristonLampShadows(null!, NullClientLog.Instance);
        shadows.Configure(2, 256);
        PristonPointLight[] lights =
        [
            new(new Vector3(0f, 0f, 0f), 300f, Vector3.One, 1f),
            new(new Vector3(500f, 0f, 0f), 300f, Vector3.One, 1f),
            new(new Vector3(900f, 0f, 0f), 300f, Vector3.One, 1f)
        ];
        var first = shadows.Assign(lights, 0);
        Assert.True(first == 0b11 && shadows.Rows[0] >= 0 && shadows.Rows[1] >= 0 && shadows.Rows[2] == -1, $"first assignment {first}");
        var rowOfSecond = shadows.Rows[1];
        PristonPointLight[] moved = [lights[1], lights[2], lights[0]];
        var second = shadows.Assign(moved, 0);
        Assert.True(shadows.Rows[0] == rowOfSecond && shadows.Rows[2] == -1, "a lamp that stayed lost its row");
        Assert.True((second & (1 << shadows.Rows[1])) != 0, "a new lamp was not drawn at once");

        var occupied = true;
        var steady = shadows.Assign(moved, 0, (_, _) => occupied);
        Assert.True(steady == 0b11 && shadows.Assign(moved, 0, (_, _) => occupied) == 0b11, "lamps with actors were not redrawn every frame");
        occupied = false;
        Assert.True(shadows.Assign(moved, 0, (_, _) => occupied) == 0b11 && shadows.Assign(moved, 0, (_, _) => occupied) == 0,
            "an emptied lamp was not cleared once and then left alone");
        return Task.CompletedTask;
    }

    private static Task BrdfAsync()
    {
        const int size = 16;
        var table = PristonEnvironmentLighting.IntegrateBrdf(size, 128);
        foreach (var entry in table)
        {
            Assert.True(entry.X >= 0f && entry.Y >= 0f && entry.X + entry.Y <= 1.02f, $"BRDF entry {entry} gains energy");
        }

        var smoothFacing = table[0 * size + size - 1];
        Assert.True(smoothFacing.X + smoothFacing.Y > 0.9f, $"a smooth surface seen head-on reflects {smoothFacing}");
        var roughFacing = table[(size - 1) * size + size - 1];
        Assert.True(roughFacing.X + roughFacing.Y < smoothFacing.X + smoothFacing.Y, "rough surfaces do not lose single-scattering energy");
        return Task.CompletedTask;
    }

    private static Task CubeLutAsync()
    {
        var lines = new List<string> { "TITLE \"invert red\"", "LUT_3D_SIZE 2" };
        for (var b = 0; b < 2; b++)
        {
            for (var g = 0; g < 2; g++)
            {
                for (var r = 0; r < 2; r++)
                {
                    lines.Add($"{1 - r} {g} {b}");
                }
            }
        }

        var cube = PristonColorGrading.CubeLut.Parse(lines);
        var sample = cube.Sample(new Vector3(0.25f, 0.5f, 0.75f));
        Assert.True(Vector3.Distance(sample, new Vector3(0.75f, 0.5f, 0.75f)) < 1e-5f, $"cube sample {sample}");
        var threw = false;
        try
        {
            PristonColorGrading.CubeLut.Parse(["LUT_3D_SIZE 3", "0 0 0"]);
        }
        catch (FormatException)
        {
            threw = true;
        }

        Assert.True(threw, "a truncated cube file was accepted");
        var grey = PristonColorGrading.BuiltInLook(new Vector3(0.5f));
        Assert.True(MathF.Abs(grey.X - grey.Y) < 0.02f && MathF.Abs(grey.X - 0.5f) < 0.05f, $"the built-in grade moves mid grey to {grey}");
        return Task.CompletedTask;
    }
}

// Access to the day/night model for the tests.
internal static class PristonDayNightCycleProbe
{
    public static Vector3 TowardSun(float hour) => PristonDayNightCycle.TowardSunAt(hour);

    public static PristonSkyState State(float hour)
    {
        var cycle = new PristonDayNightCycle();
        cycle.SetTime(hour);
        return cycle.Evaluate();
    }
}
