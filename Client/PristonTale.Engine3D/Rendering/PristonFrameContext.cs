using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// One frame's inputs of the scene effects (PristonShaderLibrary.ApplyFrame). Exposure is the frame's nominal display
// scale: display-referred inputs (the sky dome, self-illumination, unlit effects) are divided by it so they display at
// their authored level when the automatic exposure sits at the light model's reading. EmissionScale is the radiance of
// an emissive albedo of 1 (at least 1 / Exposure; brighter at night, Lighting/PristonPhotometry).
internal readonly record struct PristonFrameContext(
    Matrix View,
    Matrix Projection,
    Vector3 CameraPosition,
    uint RenderTime,
    PristonLighting Lighting,
    Matrix LightViewProjection,
    Texture2D ShadowMap,
    bool ShadowsEnabled,
    float LampFactor,
    PristonDistanceFog Fog = default,
    PristonDebugView DebugView = PristonDebugView.Shaded,
    PristonFogSky FogSky = default,
    float Exposure = 1f,
    bool DisplayOutput = false,
    float EmissionScale = 1f,
    PristonMotionFrame Motion = default,
    PristonShadowFrame Shadows = default,
    PristonLampShadowFrame LampShadows = default,
    Lighting.PristonTileLights Tiles = default,
    PristonEnvironmentFrame Environment = default,
    PristonCloudShadowFrame CloudShadow = default);

// The clouds' shadow of the realistic sky (Atmosphere/PristonPhysicalSky): their transmittance map and how surfaces read
// it (x one over its extent, yz its centre, w strength); Map null: none.
internal readonly record struct PristonCloudShadowFrame(Texture2D Map, Vector4 Params);

// The image-based lighting of the frame (Lighting/PristonEnvironmentLighting): the prefiltered atlas, the irradiance's
// spherical harmonics and the split-sum table; Atlas null: the hemispherical ambient instead.
internal readonly record struct PristonEnvironmentFrame(Texture2D Atlas, StructuredBuffer Sh, Texture2D BrdfLut);

// The lamps' shadow atlas (null: none) and the row of each local light (-1: none), parallel to the light list.
internal readonly record struct PristonLampShadowFrame(Texture2D Atlas, int[] Rows, int FaceSize, int Lamps);

// The sun's cascades (null: one map over the whole ShadowMap, through LightViewProjection), their filter, and the
// half-resolution screen-space occlusion (null: none) with its strengths: x ambient occlusion, y contact shadows.
// NoiseOffset moves the per-pixel noise every frame when the temporal anti-aliasing integrates it.
internal readonly record struct PristonShadowFrame(
    Lighting.PristonShadowCascades Cascades,
    ShadowFilterMode Filter,
    Texture2D Occlusion,
    Vector2 OcclusionStrength,
    float NoiseOffset);


// Inputs of the motion vectors the surfaces write for the temporal anti-aliasing: the previous frame's unjittered
// view-projection, this frame's jitter in clip units (subtracted to get the unjittered position) and the previous
// frame's render time (the vertex animation). Enabled is false when no motion target is bound (the surfaces then write zero motion).
internal readonly record struct PristonMotionFrame(
    Matrix PreviousViewProjection,
    Vector2 JitterClip,
    uint PreviousRenderTime,
    bool Enabled,
    uint Frame);

// Per-frame inputs of the atmospheric fog colour: the HDR sky capture strip (the flat colour applies without one), the
// direction toward the unclamped sun and the in-scatter radiance, already scaled by the sun's elevation gate. Capture is
// always a bindable texture when set, so the fog sampler never keeps a render target the sky capture is writing to.
internal readonly record struct PristonFogSky(Texture2D Capture, bool HasCapture, Vector3 SunDirection, Vector3 InScatter);

// Distance fog: beyond the original clip distance the surface fades toward the visible sky (Shaders/Atmosphere/Fog.hlsli).
public readonly record struct PristonDistanceFog(
    DistanceFogMode Mode,
    float StartDistance,
    float Density,
    float MaxOpacity,
    float InScatter,
    float InScatterAnisotropy,
    Vector3 Color)
{
    // The original game's extended camera sight began its distance clip 2208 units from the eye (sight of 60 cells of
    // 64 units minus a margin of 26 cells and a quarter); the short default sight (1152) would fade terrain just
    // behind Etervale's farther camera.
    public const float OriginalStartDistance = 2208f;
    // The original barely drew past its clip; Etervale draws to 20000 units, so the curve reaches about half its cap
    // 2300 units past the start and never hides distant geometry completely.
    public const float DefaultDensity = 0.0003f;
    public const float DefaultMaxOpacity = 0.85f;
    public const float DefaultInScatter = 0.35f;
    public const float DefaultInScatterAnisotropy = 0.7f;

    public static PristonDistanceFog Atmospheric { get; } = new(
        DistanceFogMode.Atmospheric,
        OriginalStartDistance,
        DefaultDensity,
        DefaultMaxOpacity,
        DefaultInScatter,
        DefaultInScatterAnisotropy,
        Vector3.Zero);

    public static PristonDistanceFog Disabled { get; } = Atmospheric with { Mode = DistanceFogMode.Off };

    // The original game's stepped colour clip (Classic) is no longer drawn: it falls back to the atmospheric fog.
    public static PristonDistanceFog For(DistanceFogMode mode) => mode switch
    {
        DistanceFogMode.Classic or DistanceFogMode.Atmospheric => Atmospheric,
        _ => Disabled
    };

    public bool Enabled => Mode != DistanceFogMode.Off;

    // Mirrors FogOpacity in Shaders/Atmosphere/Fog.hlsli.
    public float Level(float viewDepth)
    {
        if (!Enabled)
        {
            return 0f;
        }

        var distance = MathF.Max(viewDepth - StartDistance, 0f);
        return MaxOpacity * (1f - MathF.Exp(-distance * Density));
    }

    // Schlick's approximation of the Henyey-Greenstein phase function: k = 1.55g - 0.55g^3.
    internal float PhaseK => 1.55f * InScatterAnisotropy - 0.55f * InScatterAnisotropy * InScatterAnisotropy * InScatterAnisotropy;
}

// Development views of the surface shader (Shaders/Passes/Surface.fx DebugOutput).
[JsonConverter(typeof(JsonStringEnumConverter<PristonDebugView>))]
public enum PristonDebugView
{
    Shaded,
    Albedo,
    VertexColour,
    Normals,
    Lighting,
    Overdraw,
    // Final pass: the scene luminance as EV100 false colour, one hue per stop (Shaders/Passes/PostFinal.fx).
    ExposureFalseColour,
    Roughness,
    Metallic,
    // Ambient occlusion (red) and contact-shadow visibility (green).
    AmbientOcclusion,
    // The specular radiance alone (sun, lamps, environment), before exposure.
    Specular
}
