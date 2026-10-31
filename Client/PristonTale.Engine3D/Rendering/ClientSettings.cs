using System.Text.Json.Serialization;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

[JsonConverter(typeof(JsonStringEnumConverter<WindowMode>))]
public enum WindowMode
{
    Windowed,
    Borderless,
    Fullscreen
}

public sealed record ClientSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public int Monitor { get; init; } = -1;
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public WindowMode WindowMode { get; init; }
    // Where the window was left in windowed mode: its client area's top-left in desktop coordinates (Window.Position)
    // while not maximized, and whether it was maximized. Without a position the window opens centred. Not part of the
    // display: moving or maximizing the window applies nothing.
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public bool WindowMaximized { get; init; }
    public int RefreshRate { get; init; }
    public bool VerticalSync { get; init; }
    public bool MusicEnabled { get; init; } = true;
    public bool SoundEnabled { get; init; } = true;
    public int MaxFps { get; init; }
    public int BackgroundMaxFps { get; init; } = 30;
    public float UiScale { get; init; } = 1f;
    public int MultiSampleCount { get; init; } = 4;
    public int ShadowMapSize { get; init; } = 4096;
    // Cube faces of the lamp shadows (the nearest LampShadowCount lamps, 0, 2 or 4), in pixels.
    public int LampShadowTileSize { get; init; } = 512;
    public int LampShadowCount { get; init; } = 4;
    // Image-based lighting from the sky dome (irradiance and prefiltered reflections); off: a hemispherical ambient.
    public bool ImageBasedLighting { get; init; } = true;
    // Screen-space reflections of smooth surfaces (water, ice) over the image-based reflection; needs the depth pre-pass.
    public bool ScreenSpaceReflections { get; init; }
    // Look development: an 18 % grey and a chrome sphere beside the camera focus.
    public bool CalibrationSpheres { get; init; }
    // Sunlight scattered by a height fog, with the shafts the shadows cut (needs the depth pre-pass).
    public bool VolumetricLight { get; init; } = true;
    public int ShadowUpdateRate { get; init; } = 60;
    public bool ShadowsEnabled { get; init; } = true;
    // Classic: the target HP bar appears only after you attack a monster at full HP, without numbers.
    public bool ClassicTargetHealthBar { get; init; }
    // Classic: the name boxes over monsters and NPCs use the speech bubble frame and the HUD text size, and a monster's
    // life bar shows while the cursor is on it; otherwise a compact box, the bar once the monster was clicked or attacked.
    public bool ClassicNamePlates { get; init; }
    // The level guide's notice on reaching a level it has a guide for (its On/Off switch in the guide window).
    public bool LevelGuide { get; init; } = true;
    // The weather the server sends: clouds, rain, lightning and their sounds; off keeps the sky clear.
    public bool WeatherEnabled { get; init; } = true;
    // The physical sky (sun, moon, stars, volumetric clouds) instead of the original's painted domes, and its clouds.
    public bool RealisticSky { get; init; } = true;
    public CloudQuality CloudQuality { get; init; } = CloudQuality.Medium;
    // Atmospheric fades distant geometry toward the captured sky up to a capped opacity; Classic is still accepted but
    // is drawn as Atmospheric; Off disables it.
    public DistanceFogMode DistanceFog { get; init; } = DistanceFogMode.Atmospheric;
    // Development view of the surface shader; also cycled in game with Ctrl+F10.
    public PristonDebugView DebugView { get; init; }
    // The preset the rendering options below came from (WithQuality); Custom once one of them is changed by hand.
    public GraphicsQuality Quality { get; init; } = GraphicsQuality.High;
    public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Taa;
    // Automatic exposure (luminance histogram, adaptation over time); off uses the light model's incident reading.
    public bool AutoExposure { get; init; } = true;
    // Exposure compensation in stops, -3..3, added to the automatic or fixed exposure.
    public float ExposureCompensation { get; init; }
    public TonemapperMode Tonemapper { get; init; } = TonemapperMode.PbrNeutral;
    public BloomQuality Bloom { get; init; } = BloomQuality.High;
    // The built-in colour grade (a 3D LUT after the display transform), or the .cube file ColorGradingLut names.
    public bool ColorGrading { get; init; } = true;
    public string ColorGradingLut { get; init; } = string.Empty;
    // Contrast-adaptive sharpening of the final image, 0..1.
    public float Sharpening { get; init; } = 0.35f;
    // Stage lamps and combat lights shaded per pixel: 8, 16 or 32, the nearest to the camera focus.
    public int LocalLightLimit { get; init; } = 32;
    // Cascades of the sun's shadow map (1-4) in an atlas of ShadowMapSize, and their filter.
    public int ShadowCascades { get; init; } = 4;
    public ShadowFilterMode ShadowFilter { get; init; } = ShadowFilterMode.Pcss;
    // Ground-truth ambient occlusion and screen-space contact shadows from a depth pre-pass (not with MSAA).
    public bool AmbientOcclusion { get; init; } = true;
    public bool ContactShadows { get; init; } = true;

    public ClientSettings Validate(IClientLog log)
    {
        var defaults = new ClientSettings();
        var valid = this with
        {
            Monitor = Monitor >= -1 ? Monitor : defaults.Monitor,
            WindowWidth = WindowWidth is >= 800 and <= 16384 ? WindowWidth : defaults.WindowWidth,
            WindowHeight = WindowHeight is >= 600 and <= 16384 ? WindowHeight : defaults.WindowHeight,
            WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : defaults.WindowMode,
            RefreshRate = RefreshRate is >= 0 and <= 1000 ? RefreshRate : defaults.RefreshRate,
            MaxFps = IsValidFps(MaxFps) ? MaxFps : defaults.MaxFps,
            BackgroundMaxFps = IsValidFps(BackgroundMaxFps) ? BackgroundMaxFps : defaults.BackgroundMaxFps,
            UiScale = float.IsFinite(UiScale) && UiScale is >= 0.75f and <= 1.5f ? UiScale : defaults.UiScale,
            MultiSampleCount = MultiSampleCount is 0 or 2 or 4 or 8 ? MultiSampleCount : defaults.MultiSampleCount,
            ShadowMapSize = ShadowMapSize is 1024 or 2048 or 4096 or 8192 ? ShadowMapSize : defaults.ShadowMapSize,
            LampShadowTileSize = LampShadowTileSize is 256 or 512 or 1024 ? LampShadowTileSize : defaults.LampShadowTileSize,
            ShadowUpdateRate = ShadowUpdateRate is 0 or 30 or 60 or 120 ? ShadowUpdateRate : defaults.ShadowUpdateRate,
            DistanceFog = Enum.IsDefined(DistanceFog) ? DistanceFog : defaults.DistanceFog,
            CloudQuality = Enum.IsDefined(CloudQuality) ? CloudQuality : defaults.CloudQuality,
            DebugView = Enum.IsDefined(DebugView) ? DebugView : defaults.DebugView,
            Quality = Enum.IsDefined(Quality) ? Quality : defaults.Quality,
            AntiAliasing = Enum.IsDefined(AntiAliasing) ? AntiAliasing : defaults.AntiAliasing,
            ExposureCompensation = float.IsFinite(ExposureCompensation) && ExposureCompensation is >= -3f and <= 3f
                ? ExposureCompensation
                : defaults.ExposureCompensation,
            Tonemapper = Enum.IsDefined(Tonemapper) ? Tonemapper : defaults.Tonemapper,
            Bloom = Enum.IsDefined(Bloom) ? Bloom : defaults.Bloom,
            ColorGradingLut = ColorGradingLut ?? defaults.ColorGradingLut,
            Sharpening = float.IsFinite(Sharpening) && Sharpening is >= 0f and <= 1f ? Sharpening : defaults.Sharpening,
            LocalLightLimit = LocalLightLimit is 8 or 16 or 32 ? LocalLightLimit : defaults.LocalLightLimit,
            ShadowCascades = ShadowCascades is >= 1 and <= 4 ? ShadowCascades : defaults.ShadowCascades,
            ShadowFilter = Enum.IsDefined(ShadowFilter) ? ShadowFilter : defaults.ShadowFilter,
            LampShadowCount = LampShadowCount is 0 or 2 or 4 ? LampShadowCount : defaults.LampShadowCount
        };

        if (valid != this)
        {
            log.Warn("[Settings] Invalid presentation values replaced with defaults.");
        }

        return valid;
    }

    // The rendering options of a preset; display, audio and gameplay options are kept. Measured GPU frame times per
    // preset are listed in docs/client-settings.md.
    public ClientSettings WithQuality(GraphicsQuality quality) => quality switch
    {
        GraphicsQuality.Low => this with
        {
            Quality = quality,
            VolumetricLight = false,
            ScreenSpaceReflections = false,
            LampShadowCount = 0,
            LampShadowTileSize = 512,
            ShadowCascades = 2,
            ShadowFilter = ShadowFilterMode.Pcf,
            AmbientOcclusion = false,
            ContactShadows = false,
            LocalLightLimit = 8,
            AntiAliasing = AntiAliasingMode.Off,
            ShadowMapSize = 2048,
            ShadowUpdateRate = 30,
            CloudQuality = CloudQuality.Low,
            ShadowsEnabled = true,
            Bloom = BloomQuality.Off,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0f
        },
        GraphicsQuality.Medium => this with
        {
            Quality = quality,
            VolumetricLight = false,
            ScreenSpaceReflections = false,
            LampShadowCount = 2,
            LampShadowTileSize = 512,
            ShadowCascades = 3,
            ShadowFilter = ShadowFilterMode.Pcf,
            AmbientOcclusion = true,
            ContactShadows = false,
            LocalLightLimit = 16,
            AntiAliasing = AntiAliasingMode.Smaa,
            ShadowMapSize = 2048,
            ShadowUpdateRate = 60,
            CloudQuality = CloudQuality.Low,
            ShadowsEnabled = true,
            Bloom = BloomQuality.High,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0.2f
        },
        GraphicsQuality.High => this with
        {
            Quality = quality,
            VolumetricLight = true,
            ScreenSpaceReflections = false,
            LampShadowCount = 4,
            LampShadowTileSize = 512,
            ShadowCascades = 4,
            ShadowFilter = ShadowFilterMode.Pcss,
            AmbientOcclusion = true,
            ContactShadows = true,
            LocalLightLimit = 32,
            AntiAliasing = AntiAliasingMode.Taa,
            ShadowMapSize = 4096,
            ShadowUpdateRate = 60,
            CloudQuality = CloudQuality.Medium,
            ShadowsEnabled = true,
            Bloom = BloomQuality.High,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0.35f
        },
        GraphicsQuality.Ultra => this with
        {
            Quality = quality,
            VolumetricLight = true,
            ScreenSpaceReflections = true,
            LampShadowCount = 4,
            LampShadowTileSize = 1024,
            ShadowCascades = 4,
            ShadowFilter = ShadowFilterMode.Pcss,
            AmbientOcclusion = true,
            ContactShadows = true,
            LocalLightLimit = 32,
            AntiAliasing = AntiAliasingMode.Taa,
            ShadowMapSize = 4096,
            ShadowUpdateRate = 0,
            CloudQuality = CloudQuality.High,
            ShadowsEnabled = true,
            Bloom = BloomQuality.High,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0.35f
        },
        _ => this with { Quality = GraphicsQuality.Custom }
    };

    // The options PristonRenderer reads (PresentationSettings.ApplyRendering).
    public bool HasSameRendering(ClientSettings other)
    {
        return ShadowMapSize == other.ShadowMapSize
            && LampShadowTileSize == other.LampShadowTileSize
            && ShadowUpdateRate == other.ShadowUpdateRate
            && ShadowsEnabled == other.ShadowsEnabled
            && DistanceFog == other.DistanceFog
            && DebugView == other.DebugView
            && MultiSampleCount == other.MultiSampleCount
            && AntiAliasing == other.AntiAliasing
            && AutoExposure == other.AutoExposure
            && ExposureCompensation == other.ExposureCompensation
            && Tonemapper == other.Tonemapper
            && Bloom == other.Bloom
            && ColorGrading == other.ColorGrading
            && ColorGradingLut == other.ColorGradingLut
            && Sharpening == other.Sharpening
            && LocalLightLimit == other.LocalLightLimit
            && ShadowCascades == other.ShadowCascades
            && ShadowFilter == other.ShadowFilter
            && AmbientOcclusion == other.AmbientOcclusion
            && ContactShadows == other.ContactShadows
            && LampShadowCount == other.LampShadowCount
            && ImageBasedLighting == other.ImageBasedLighting
            && ScreenSpaceReflections == other.ScreenSpaceReflections
            && CalibrationSpheres == other.CalibrationSpheres
            && VolumetricLight == other.VolumetricLight
            && RealisticSky == other.RealisticSky
            && CloudQuality == other.CloudQuality;
    }

    public bool HasSameDisplay(ClientSettings other)
    {
        return Monitor == other.Monitor
            && WindowWidth == other.WindowWidth
            && WindowHeight == other.WindowHeight
            && WindowMode == other.WindowMode
            && RefreshRate == other.RefreshRate
            && MultiSampleCount == other.MultiSampleCount;
    }

    public int FrameLimit(bool active)
    {
        if (active || BackgroundMaxFps == 0)
        {
            return MaxFps;
        }

        return MaxFps == 0 ? BackgroundMaxFps : Math.Min(MaxFps, BackgroundMaxFps);
    }

    private static bool IsValidFps(int value) => value == 0 || value is >= 15 and <= 1000;
}
