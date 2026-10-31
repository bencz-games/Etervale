using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering;

internal enum ShaderVertexFactory
{
    Static,
    Skinned
}

// The surface effects of the SM 6 library (Content/Shaders/Passes/Surface.fx, built as StaticSurface and SkinnedSurface)
// and the constants they share: the frame block, the fog, the lighting and the material model's inputs.
internal sealed class PristonShaderLibrary : IDisposable
{
    public const string StaticSurfaceAsset = "Effects/StaticSurface";
    public const string SkinnedSurfaceAsset = "Effects/SkinnedSurface";

    // Tangent of the light's angular radius for the soft shadows: about twice the sun's, a slightly hazy sun.
    private const float LightRadiusTangent = 0.01f;
    private const float PcfRadiusTexels = 1.5f;
    // Without cascades (tools, checks) the single map covers this light-space depth range and width.
    private const float SingleMapDepthRange = 5600f;
    private const float SingleMapWidth = 2800f;
    // Sky and bounce light fill the shadows, so the sun is fully occluded in them.
    private const float MaximumShadowStrength = 1f;
    private const float AlphaCutoff = PristonTextureLoader.AlphaCutoffByte / 255f;
    private const int MaxLocalLights = 32;
    internal const int MaxLocalLightCount = MaxLocalLights;

    // The skinning palette holds 64 affine bones (Shaders/Vertex/SkinningDecl.hlsli MAX_BONES).
    internal const int MaxBones = 64;

    // Water ripple and wind use angular frequencies of 1.7, 1.9, 0.45 and 0.5 rad/s (VertexAnimation.hlsli).
    // 40π seconds is a whole number of cycles for each (34, 38, 9 and 10), so wrapping there keeps the motion
    // continuous while the float clock stays small enough for precise sin() arguments.
    internal const double VertexAnimationPeriodSeconds = 40.0 * Math.PI;

    private readonly SurfaceEffect _static;
    private readonly SurfaceEffect _skinned;
    private readonly Vector4[] _lightPositions = new Vector4[MaxLocalLights];
    private readonly Vector4[] _lightColors = new Vector4[MaxLocalLights];
    private readonly CascadeConstants _cascades = new();
    private readonly StructuredBuffer _allLights;
    private readonly StructuredBuffer _noEnvironment;

    // neutral: a 1x1 white texture bound to every slot before its first use; a Vulkan draw never samples an unbound slot.
    public PristonShaderLibrary(ContentManager content, IClientLog log, Texture2D neutral)
    {
        // Bound where no tile masks exist; TileParams.z = 0 then makes every light count.
        _allLights = new StructuredBuffer(neutral.GraphicsDevice, sizeof(uint), 1, BufferShaderAccess.ReadWrite);
        _noEnvironment = new StructuredBuffer(neutral.GraphicsDevice, 16, PristonEnvironmentLighting.ShEntries, BufferShaderAccess.ReadWrite);
        _static = new SurfaceEffect(LoadRequired(content, StaticSurfaceAsset, log), ShaderVertexFactory.Static, neutral, _allLights, _noEnvironment);
        _skinned = new SurfaceEffect(LoadRequired(content, SkinnedSurfaceAsset, log), ShaderVertexFactory.Skinned, neutral, _allLights, _noEnvironment);
    }

    // Material texture parameter changes since BeginFrame (identical rebinds are skipped and not counted).
    public int TextureChanges => _static.TextureChanges + _skinned.TextureChanges;

    public void BeginFrame()
    {
        _static.TextureChanges = 0;
        _skinned.TextureChanges = 0;
    }

    internal static float VertexAnimationSeconds(uint renderTimeMs) =>
        (float)(renderTimeMs / 1000.0 % VertexAnimationPeriodSeconds);

    public void ApplyFrame(PristonFrameContext frame)
    {
        var lights = frame.Lighting.PointLights;
        var count = Math.Min(lights.Count, MaxLocalLights);
        Array.Clear(_lightPositions);
        Array.Clear(_lightColors);
        for (var index = 0; index < count; index++)
        {
            var light = lights[index];
            _lightPositions[index] = new Vector4(light.Position, light.Radius);
            var row = frame.LampShadows.Rows is { } rows && frame.LampShadows.Atlas is not null ? rows[index] : -1;
            _lightColors[index] = new Vector4(light.Color * light.Intensity, row + 1f);
        }

        var constants = new FrameConstants(frame, count, Math.Min(frame.Lighting.TransientLightCount, count));
        FillCascades(frame);
        _static.ApplyFrame(constants, _lightPositions, _lightColors, _cascades);
        _skinned.ApplyFrame(constants, _lightPositions, _lightColors, _cascades);
    }

    // The cascade constants of a frame, packed once for both effects.
    private void FillCascades(PristonFrameContext frame)
    {
        var cascades = frame.Shadows.Cascades;
        var mapSize = frame.ShadowMap is { Width: > 1 } map ? map.Width : 2048;
        if (cascades is null)
        {
            Array.Fill(_cascades.ViewProjections, frame.LightViewProjection);
            Array.Fill(_cascades.Atlas, new Vector4(0f, 0f, 1f, 1f));
            _cascades.Count = 1;
            _cascades.Splits = new Vector4(1e9f);
            _cascades.TexelWorld = new Vector4(SingleMapWidth / mapSize);
            _cascades.DepthRange = new Vector4(SingleMapDepthRange);
        }
        else
        {
            _cascades.Count = cascades.Count;
            Span<float> splits = stackalloc float[PristonShadowCascades.MaxCascades];
            Span<float> texels = stackalloc float[PristonShadowCascades.MaxCascades];
            Span<float> ranges = stackalloc float[PristonShadowCascades.MaxCascades];
            for (var c = 0; c < PristonShadowCascades.MaxCascades; c++)
            {
                var source = Math.Min(c, cascades.Count - 1);
                _cascades.ViewProjections[c] = cascades.ViewProjections[source];
                _cascades.Atlas[c] = cascades.AtlasRects[source];
                splits[c] = cascades.Splits[source];
                texels[c] = cascades.TexelWorldSizes[source];
                ranges[c] = cascades.DepthRanges[source];
            }

            _cascades.Splits = new Vector4(splits[0], splits[1], splits[2], splits[3]);
            _cascades.TexelWorld = new Vector4(texels[0], texels[1], texels[2], texels[3]);
            _cascades.DepthRange = new Vector4(ranges[0], ranges[1], ranges[2], ranges[3]);
        }

        _cascades.Texel = new Vector2(1f / mapSize);
        _cascades.Filter = frame.Shadows.Filter;
        _cascades.Occlusion = frame.Shadows.Occlusion;
        _cascades.OcclusionStrength = frame.Shadows.OcclusionStrength;
        _cascades.NoiseOffset = frame.Shadows.NoiseOffset;
    }

    // casterLight: a lamp's position and 1 / radius for its cube faces; zero for the sun's cascades.
    public void ApplyShadowFrame(Matrix viewProjection, uint renderTimeMs, Vector4 casterLight = default)
    {
        var animationTime = VertexAnimationSeconds(renderTimeMs);
        _static.ApplyCasterFrame(viewProjection, animationTime, casterLight);
        _skinned.ApplyCasterFrame(viewProjection, animationTime, casterLight);
    }

    public Effect SelectStaticSurface(
        PristonMaterialRuntime material,
        Texture2D texture,
        Vector2 uvScrollOffset,
        Vector4 windParams,
        Vector2 layerOffset,
        float opacity = 1f,
        bool prepass = false)
    {
        _static.SetVertexAnimation(windParams, material.Water);
        _static.ApplyMaterial(material, texture, uvScrollOffset, layerOffset, opacity, Vector3.Zero);
        return _static.Select(_static.SurfaceTechnique(material.AlphaTest, prepass));
    }

    // The static effect's technique that clears one cascade tile of the shadow atlas.
    public Effect SelectShadowClear() => _static.Select(_static.ShadowClear);

    // Sky dome layers: unlit radiance, the layer's opacity cross-fading two sky profiles.
    public Effect SelectSky(PristonMaterialRuntime material, Texture2D texture, Vector2 uvScrollOffset, float opacity)
    {
        _static.SetVertexAnimation(Vector4.Zero, false);
        _static.ApplyMaterial(material, texture, uvScrollOffset, Vector2.Zero, opacity, Vector3.Zero);
        return _static.Select(_static.Sky);
    }

    public Effect SelectSkinnedSurface(
        PristonMaterialRuntime material,
        Texture2D texture,
        Vector2 uvScrollOffset,
        PristonSkinPalette palette,
        Vector3 rimColor,
        Vector2 layerOffset,
        bool prepass = false,
        float floorLight = 1f,
        ActorShade shade = null,
        float overlayOffset = 0f,
        float wobblePhase = 0f,
        bool actor = false)
    {
        _skinned.SetBonePalette(palette);
        _skinned.SetDrawLight(floorLight);
        var overlay = shade?.Overlay;
        _skinned.ApplyMaterial(material, texture, uvScrollOffset, overlay is null ? layerOffset : new Vector2(overlayOffset, 0f), 1f, rimColor, overlay);
        _skinned.ApplyActor(shade is null ? new Vector4(0f, 0f, 0f, 1f) : new Vector4(shade.Tint, shade.Opacity),
            new Vector4(shade is { Wobble: true } ? ActorShade.WobbleAmplitude : 0f, wobblePhase, overlay is null ? 0f : 1f, 0f), actor);
        return _skinned.Select(_skinned.SurfaceTechnique(material.AlphaTest, prepass));
    }

    // The hover outline of a skinned batch SelectSkinnedSurface just set up (same pose, wobble and texture): parameters as
    // Surface.fx OutlineParams, color display-referred with its strength in w.
    public Effect SelectSkinnedOutline(bool masked, Vector4 parameters, Vector4 color)
    {
        _skinned.ApplyOutline(parameters, color);
        return _skinned.Select(masked ? _skinned.OutlineMasked : _skinned.Outline);
    }

    public Effect SelectStaticShadow(PristonMaterialRuntime material, Texture2D texture, Vector4 windParams, Vector2 uvScrollOffset)
    {
        _static.SetVertexAnimation(windParams, material.Water);
        _static.ApplyCaster(material.AlphaTest ? texture : null, uvScrollOffset);
        return _static.Select(material.AlphaTest ? _static.CasterMasked : _static.Caster);
    }

    public Effect SelectSkinnedShadow(PristonMaterialRuntime material, Texture2D texture, PristonSkinPalette palette, Vector2 uvScrollOffset)
    {
        _skinned.SetBonePalette(palette);
        _skinned.ApplyCaster(material.AlphaTest ? texture : null, uvScrollOffset);
        return _skinned.Select(material.AlphaTest ? _skinned.CasterMasked : _skinned.Caster);
    }

    public void Dispose()
    {
        _static.Dispose();
        _skinned.Dispose();
    }

    internal static Effect LoadRequired(ContentManager content, string assetName, IClientLog log)
    {
        Effect effect;
        try
        {
            effect = content.Load<Effect>(assetName);
        }
        catch (ContentLoadException exception)
        {
            var message = $"Required shader '{assetName}' could not be loaded from '{content.RootDirectory}'. Rebuild and deploy the client Content directory.";
            log.Error(message, exception);
            throw new ContentLoadException(message, exception);
        }

        foreach (var technique in effect.Techniques)
        {
            if (technique.Passes.Count != 1)
            {
                var message = $"Required shader '{assetName}' technique '{technique.Name}' must expose exactly one pass; found {technique.Passes.Count}.";
                log.Error(message);
                throw new ContentLoadException(message);
            }
        }

        // The content manager hands every caller the same instance. Each owner keeps its own copy so the parameter caches
        // (which skip unchanged values) never meet values another screen's renderer left in the shared effect.
        return effect.Clone();
    }

    // Values shared by both surface effects for one frame, computed once.
    private readonly struct FrameConstants
    {
        public FrameConstants(PristonFrameContext frame, int lightCount, int transientCount)
        {
            Frame = frame;
            LightCount = lightCount;
            TransientCount = transientCount;
            const double blinkPeriodMs = 1200.0;
            BlinkPulse = 0.35f + 0.65f * (0.5f + 0.5f * (float)Math.Sin(frame.RenderTime * (2.0 * Math.PI / blinkPeriodMs)));
            AnimationTime = VertexAnimationSeconds(frame.RenderTime);
            ShadowStrength = frame.ShadowsEnabled ? MaximumShadowStrength : 0f;
            PreviousAnimationTime = VertexAnimationSeconds(frame.Motion.Enabled ? frame.Motion.PreviousRenderTime : frame.RenderTime);
        }

        public PristonFrameContext Frame { get; }
        public int LightCount { get; }
        public int TransientCount { get; }
        public float BlinkPulse { get; }
        public float AnimationTime { get; }
        public float PreviousAnimationTime { get; }
        public float ShadowStrength { get; }
    }

    // The cascades as the shaders take them (Shaders/Lighting/LightingDecl.hlsli).
    private sealed class CascadeConstants
    {
        public Matrix[] ViewProjections { get; } = new Matrix[PristonShadowCascades.MaxCascades];
        public Vector4[] Atlas { get; } = new Vector4[PristonShadowCascades.MaxCascades];
        public int Count { get; set; }
        public Vector4 Splits { get; set; }
        public Vector4 TexelWorld { get; set; }
        public Vector4 DepthRange { get; set; }
        public Vector2 Texel { get; set; }
        public ShadowFilterMode Filter { get; set; }
        public Texture2D Occlusion { get; set; }
        public Vector2 OcclusionStrength { get; set; }
        public float NoiseOffset { get; set; }
    }

    // One surface effect (one vertex factory) with its techniques and the parameters the renderer sets.
    private sealed class SurfaceEffect : IDisposable
    {
        private readonly Effect _effect;
        private readonly EffectParameter _viewProjection;
        private readonly EffectParameter _view;
        private readonly EffectParameter _casterViewProjection;
        private readonly EffectParameter _uvOffsets;
        private readonly EffectParameter _reflexLayers;
        private readonly EffectParameter _drawOpacity;
        private readonly EffectParameter _drawLight;
        private float _drawLightValue = float.NaN;
        private readonly EffectParameter _bonePalette;
        private readonly EffectParameter _previousBonePalette;
        private readonly EffectParameter _previousViewProjection;
        private readonly EffectParameter _motionParams;
        private readonly EffectParameter _emissionScale;
        private readonly EffectParameter _windParams;
        private readonly EffectParameter _waterRipple;
        private readonly EffectParameter _vertexAnimTime;
        private readonly EffectParameter _cameraPosition;
        private readonly EffectParameter _exposure;
        private readonly EffectParameter _sunDirection;
        private readonly EffectParameter _frameTime;
        private readonly EffectParameter _sunColor;
        private readonly EffectParameter _displayOutput;
        private readonly EffectParameter _skyAmbient;
        private readonly EffectParameter _debugView;
        private readonly EffectParameter _groundAmbient;
        private readonly EffectParameter _blinkPulse;
        private readonly EffectParameter _fogParams;
        private readonly EffectParameter _fogSkyBand;
        private readonly EffectParameter _fogSkyBand2;
        private readonly EffectParameter _fogColor;
        private readonly EffectParameter _fogPhaseK;
        private readonly EffectParameter _fogInScatter;
        private readonly EffectParameter _fogSky;
        private readonly EffectParameter _cascadeViewProjection;
        private readonly EffectParameter _cascadeAtlas;
        private readonly EffectParameter _cascadeSplits;
        private readonly EffectParameter _cascadeTexelWorld;
        private readonly EffectParameter _cascadeDepthRange;
        private readonly EffectParameter _shadowParams;
        private readonly EffectParameter _shadowTexel;
        private readonly EffectParameter _occlusionParams;
        private readonly EffectParameter _noiseParams;
        private readonly EffectParameter _occlusionMap;
        private readonly EffectParameter _lampShadowParams;
        private readonly EffectParameter _lampShadowMap;
        private readonly EffectParameter _casterLight;
        private readonly EffectParameter _tileParams;
        private readonly EffectParameter _tileLightMasks;
        private readonly Texture2D _neutralTexture;
        private readonly StructuredBuffer _allLights;
        private readonly StructuredBuffer _noEnvironment;
        private readonly EffectParameter _environmentParams;
        private readonly EffectParameter _environmentMap;
        private readonly EffectParameter _environmentSh;
        private readonly EffectParameter _brdfLut;
        private readonly EffectParameter _cloudShadowMap;
        private readonly EffectParameter _cloudShadowParams;
        private readonly EffectParameter _shadowMap;
        private readonly EffectParameter _localLightCount;
        private readonly EffectParameter _transientLightCount;
        private readonly EffectParameter _localLightPosition;
        private readonly EffectParameter _localLightColor;
        private readonly EffectParameter _baseColorFactor;
        private readonly EffectParameter _roughness;
        private readonly EffectParameter _metallic;
        private readonly EffectParameter _reflectance;
        private readonly EffectParameter _alphaCutoff;
        private readonly EffectParameter _shadingModel;
        private readonly EffectParameter _emissiveStrength;
        private readonly EffectParameter _layerMode;
        private readonly EffectParameter _layerStrength;
        private readonly EffectParameter _rimColor;
        private readonly EffectParameter _materialBlink;
        private readonly EffectParameter _translucency;
        private readonly EffectParameter _actorTint;
        private readonly EffectParameter _actorParams;
        private Vector4 _tint = new(0f, 0f, 0f, 1f);
        private Vector4 _actorParamsValue;
        private readonly EffectParameter _actorReceiver;
        private bool _receiver;
        private readonly EffectParameter _outlineParams;
        private readonly EffectParameter _outlineColor;
        private Texture2D _overlay;
        private readonly EffectParameter _baseColorMap;
        private readonly EffectParameter _layerMap;
        private Texture2D _boundBase;
        private Texture2D _boundLayer;
        private PristonMaterialDescription _description;
        private Vector3 _rim;
        private Vector4 _uv;
        private Vector2 _reflex;
        private float _opacity = float.NaN;
        private Vector4 _wind;
        private bool _water;
        private bool _hasVertexAnimation;
        private PristonSkinPalette _palette;
        private uint _paletteRevision;
        private uint _motionFrame;
        private uint _paletteMotionFrame;

        public SurfaceEffect(Effect effect, ShaderVertexFactory factory, Texture2D neutral, StructuredBuffer allLights,
            StructuredBuffer noEnvironment)
        {
            _noEnvironment = noEnvironment;
            _effect = effect;
            _neutralTexture = neutral;
            _allLights = allLights;
            Opaque = Technique("Opaque");
            Masked = Technique("Masked");
            Prepass = Technique("Prepass");
            PrepassMasked = Technique("PrepassMasked");
            ShadowClear = factory == ShaderVertexFactory.Static ? Technique("ShadowClear") : null;
            Caster = Technique("ShadowCaster");
            CasterMasked = Technique("ShadowCasterMasked");
            Sky = factory == ShaderVertexFactory.Static ? Technique("Sky") : null;
            Outline = factory == ShaderVertexFactory.Skinned ? Technique("Outline") : null;
            OutlineMasked = factory == ShaderVertexFactory.Skinned ? Technique("OutlineMasked") : null;
            _outlineParams = Parameter("OutlineParams");
            _outlineColor = Parameter("OutlineColor");
            _viewProjection = Parameter("ViewProjection");
            _view = Parameter("View");
            _casterViewProjection = Parameter("CasterViewProjection");
            _uvOffsets = Parameter("UvOffsets");
            _reflexLayers = Parameter("ReflexLayers");
            _drawOpacity = Parameter("DrawOpacity");
            _drawLight = Parameter("DrawLight");
            _bonePalette = Parameter("BonePalette");
            _previousBonePalette = Parameter("PreviousBonePalette");
            _previousViewProjection = Parameter("PreviousViewProjection");
            _motionParams = Parameter("MotionParams");
            _emissionScale = Parameter("EmissionScale");
            _windParams = Parameter("WindParams");
            _waterRipple = Parameter("WaterRipple");
            _vertexAnimTime = Parameter("VertexAnimTime");
            _cameraPosition = Parameter("CameraPosition");
            _exposure = Parameter("Exposure");
            _sunDirection = Parameter("SunDirection");
            _frameTime = Parameter("FrameTime");
            _sunColor = Parameter("SunColor");
            _displayOutput = Parameter("DisplayOutput");
            _skyAmbient = Parameter("SkyAmbient");
            _debugView = Parameter("DebugView");
            _groundAmbient = Parameter("GroundAmbient");
            _blinkPulse = Parameter("BlinkPulse");
            _fogParams = Parameter("FogParams");
            _fogSkyBand = Parameter("FogSkyBand");
            _fogSkyBand2 = Parameter("FogSkyBand2");
            _fogColor = Parameter("FogColor");
            _fogPhaseK = Parameter("FogPhaseK");
            _fogInScatter = Parameter("FogInScatter");
            _fogSky = Parameter("FogSky");
            _cascadeViewProjection = Parameter("CascadeViewProjection");
            _cascadeAtlas = Parameter("CascadeAtlas");
            _cascadeSplits = Parameter("CascadeSplits");
            _cascadeTexelWorld = Parameter("CascadeTexelWorld");
            _cascadeDepthRange = Parameter("CascadeDepthRange");
            _shadowParams = Parameter("ShadowParams");
            _shadowTexel = Parameter("ShadowTexel");
            _occlusionParams = Parameter("OcclusionParams");
            _noiseParams = Parameter("NoiseParams");
            _occlusionMap = Parameter("OcclusionMap");
            _lampShadowParams = Parameter("LampShadowParams");
            _lampShadowMap = Parameter("LampShadowMap");
            _casterLight = Parameter("CasterLight");
            _tileParams = Parameter("TileParams");
            _tileLightMasks = Parameter("TileLightMasks");
            _tileLightMasks?.SetValue(allLights);
            _environmentParams = Parameter("EnvironmentParams");
            _environmentMap = Parameter("EnvironmentMap");
            _environmentSh = Parameter("EnvironmentSh");
            _brdfLut = Parameter("BrdfLut");
            _environmentMap?.SetValue(neutral);
            _environmentSh?.SetValue(noEnvironment);
            _brdfLut?.SetValue(neutral);
            _cloudShadowMap = Parameter("CloudShadowMap");
            _cloudShadowParams = Parameter("CloudShadowParams");
            _cloudShadowMap?.SetValue(neutral);
            _shadowMap = Parameter("ShadowMap");
            _localLightCount = Parameter("LocalLightCount");
            _transientLightCount = Parameter("TransientLightCount");
            _localLightPosition = Parameter("LocalLightPosition");
            _localLightColor = Parameter("LocalLightColor");
            _baseColorFactor = Parameter("BaseColorFactor");
            _roughness = Parameter("Roughness");
            _metallic = Parameter("Metallic");
            _reflectance = Parameter("Reflectance");
            _alphaCutoff = Parameter("AlphaCutoff");
            _shadingModel = Parameter("ShadingModel");
            _emissiveStrength = Parameter("EmissiveStrength");
            _layerMode = Parameter("LayerMode");
            _layerStrength = Parameter("LayerStrength");
            _rimColor = Parameter("RimColor");
            _materialBlink = Parameter("MaterialBlink");
            _translucency = Parameter("Translucency");
            _actorTint = Parameter("ActorTint");
            _actorParams = Parameter("ActorParams");
            _actorTint?.SetValue(_tint);
            _actorParams?.SetValue(Vector4.Zero);
            _actorReceiver = Parameter("ActorReceiver");
            _actorReceiver?.SetValue(0f);
            _baseColorMap = Parameter("BaseColorMap");
            _layerMap = Parameter("LayerMap");
            _alphaCutoff?.SetValue(AlphaCutoff);
            _drawOpacity?.SetValue(1f);
            // The value caches start from these, so the effect must hold them too.
            _rimColor?.SetValue(Vector3.Zero);
            _uvOffsets?.SetValue(Vector4.Zero);
            _reflexLayers?.SetValue(Vector2.Zero);
            _baseColorMap?.SetValue(neutral);
            _layerMap?.SetValue(neutral);
            _shadowMap?.SetValue(neutral);
            _occlusionMap?.SetValue(neutral);
            _lampShadowMap?.SetValue(neutral);
            _fogSky?.SetValue(neutral);
        }

        public EffectTechnique Opaque { get; }
        public EffectTechnique Masked { get; }
        public EffectTechnique Prepass { get; }
        public EffectTechnique PrepassMasked { get; }
        public EffectTechnique ShadowClear { get; }
        public EffectTechnique Sky { get; }
        public EffectTechnique Caster { get; }
        public EffectTechnique CasterMasked { get; }
        public EffectTechnique Outline { get; }
        public EffectTechnique OutlineMasked { get; }
        public int TextureChanges { get; set; }

        public EffectTechnique SurfaceTechnique(bool masked, bool prepass) =>
            prepass ? (masked ? PrepassMasked : Prepass) : (masked ? Masked : Opaque);

        public void SetDrawLight(float light)
        {
            if (light != _drawLightValue)
            {
                _drawLight?.SetValue(light);
                _drawLightValue = light;
            }
        }

        public Effect Select(EffectTechnique technique)
        {
            _effect.CurrentTechnique = technique;
            return _effect;
        }

        // The compiler drops what no technique reads, so a missing parameter is tolerated (null-conditional sets).
        private EffectParameter Parameter(string name) => _effect.Parameters[name];

        private EffectTechnique Technique(string name) =>
            _effect.Techniques[name] ?? throw new ContentLoadException($"Surface effect lacks the '{name}' technique.");

        public void ApplyFrame(FrameConstants constants, Vector4[] lightPositions, Vector4[] lightColors, CascadeConstants cascades)
        {
            var frame = constants.Frame;
            var lighting = frame.Lighting;
            _viewProjection?.SetValue(frame.View * frame.Projection);
            _view?.SetValue(frame.View);
            _vertexAnimTime?.SetValue(constants.AnimationTime);
            _cameraPosition?.SetValue(frame.CameraPosition);
            _exposure?.SetValue(frame.Exposure);
            _sunDirection?.SetValue(-Vector3.Normalize(lighting.Sun.Direction));
            _frameTime?.SetValue(constants.AnimationTime);
            _sunColor?.SetValue(lighting.Sun.DiffuseColor);
            _displayOutput?.SetValue(frame.DisplayOutput ? 1f : 0f);
            _skyAmbient?.SetValue(lighting.AmbientColor);
            _groundAmbient?.SetValue(lighting.GroundAmbientColor);
            _emissionScale?.SetValue(frame.EmissionScale);
            var motion = frame.Motion;
            _previousViewProjection?.SetValue(motion.Enabled ? motion.PreviousViewProjection : frame.View * frame.Projection);
            _motionParams?.SetValue(new Vector4(motion.JitterClip, constants.PreviousAnimationTime, motion.Enabled ? 1f : 0f));
            _motionFrame = motion.Frame;
            _debugView?.SetValue(frame.DebugView == PristonDebugView.ExposureFalseColour ? 0f : (float)frame.DebugView);
            _blinkPulse?.SetValue(constants.BlinkPulse);
            var fog = frame.Fog;
            var fogSky = frame.FogSky;
            var hasCapture = fogSky.HasCapture && fogSky.Capture is not null;
            _fogParams?.SetValue(new Vector4(fog.Enabled ? 1f : 0f, fog.StartDistance, fog.Density, fog.MaxOpacity));
            _fogSkyBand?.SetValue(PristonSkyCapture.Band);
            _fogSkyBand2?.SetValue(new Vector4(PristonSkyCapture.Band2.X, hasCapture ? 1f : 0f, 0f, 0f));
            _fogColor?.SetValue(fog.Color);
            _fogPhaseK?.SetValue(fog.PhaseK);
            _fogInScatter?.SetValue(fogSky.InScatter);
            if (fogSky.Capture is not null)
            {
                _fogSky?.SetValue(fogSky.Capture);
            }

            _cascadeViewProjection?.SetValue(cascades.ViewProjections);
            _cascadeAtlas?.SetValue(cascades.Atlas);
            _cascadeSplits?.SetValue(cascades.Splits);
            _cascadeTexelWorld?.SetValue(cascades.TexelWorld);
            _cascadeDepthRange?.SetValue(cascades.DepthRange);
            _shadowParams?.SetValue(new Vector4(
                cascades.Count,
                cascades.Filter == ShadowFilterMode.Pcss ? 1f : 0f,
                LightRadiusTangent,
                constants.ShadowStrength));
            var occlusion = cascades.Occlusion;
            _shadowTexel?.SetValue(new Vector4(cascades.Texel, PcfRadiusTexels, occlusion is null ? 0f : 1f));
            _occlusionParams?.SetValue(occlusion is null
                ? Vector4.Zero
                : new Vector4(1f / occlusion.Width, 1f / occlusion.Height, cascades.OcclusionStrength.Y, cascades.OcclusionStrength.X));
            _noiseParams?.SetValue(new Vector2(cascades.NoiseOffset, 0f));
            _occlusionMap?.SetValue(occlusion ?? _neutralTexture);
            var tiles = frame.Tiles;
            var tilesOn = tiles.Enabled && tiles.Masks is not null;
            _tileLightMasks?.SetValue(tilesOn ? tiles.Masks : _allLights);
            _tileParams?.SetValue(new Vector4(tiles.TilesAcross, 1f / PristonLightCulling.TileSize, tilesOn ? 1f : 0f, 0f));
            var environment = frame.Environment;
            var environmentOn = environment.Atlas is not null && environment.Sh is not null && environment.BrdfLut is not null;
            _environmentMap?.SetValue(environmentOn ? environment.Atlas : _neutralTexture);
            _environmentSh?.SetValue(environmentOn ? environment.Sh : _noEnvironment);
            _brdfLut?.SetValue(environmentOn ? environment.BrdfLut : _neutralTexture);
            var clouds = frame.CloudShadow;
            _cloudShadowMap?.SetValue(clouds.Map ?? _neutralTexture);
            _cloudShadowParams?.SetValue(clouds.Map is null ? Vector4.Zero : clouds.Params);
            _environmentParams?.SetValue(environmentOn
                ? new Vector4(1f, PristonEnvironmentLighting.BaseSize, PristonEnvironmentLighting.AtlasWidth, PristonEnvironmentLighting.Levels)
                : Vector4.Zero);
            var lamps = frame.LampShadows;
            var lampAtlas = lamps.Atlas;
            _lampShadowMap?.SetValue(lampAtlas ?? _neutralTexture);
            _lampShadowParams?.SetValue(lampAtlas is null
                ? Vector4.Zero
                : new Vector4(1f / lampAtlas.Width, 1f / lampAtlas.Height, lamps.FaceSize, lamps.Lamps));
            _shadowMap?.SetValue(frame.ShadowMap);
            _localLightCount?.SetValue((float)constants.LightCount);
            _transientLightCount?.SetValue((float)constants.TransientCount);
            _localLightPosition?.SetValue(lightPositions);
            _localLightColor?.SetValue(lightColors);
        }

        public void ApplyCasterFrame(Matrix viewProjection, float animationTime, Vector4 casterLight)
        {
            _casterViewProjection?.SetValue(viewProjection);
            _casterLight?.SetValue(casterLight);
            _vertexAnimTime?.SetValue(animationTime);
        }

        // Packing happens once per pose in the owner, not once per material and shadow pass.
        public void SetBonePalette(PristonSkinPalette palette)
        {
            if (ReferenceEquals(_palette, palette) && _paletteRevision == palette.Revision && _paletteMotionFrame == _motionFrame)
            {
                return;
            }

            _bonePalette?.SetValue(palette.ShaderRows);
            _previousBonePalette?.SetValue(palette.PreviousRows(_motionFrame));
            _palette = palette;
            _paletteRevision = palette.Revision;
            _paletteMotionFrame = _motionFrame;
        }

        public void SetVertexAnimation(Vector4 windParams, bool water)
        {
            if (_hasVertexAnimation && _wind == windParams && _water == water)
            {
                return;
            }

            _windParams?.SetValue(windParams);
            _waterRipple?.SetValue(water ? 1f : 0f);
            _wind = windParams;
            _water = water;
            _hasVertexAnimation = true;
        }

        // MonoGame uploads a stage's whole cbuffer when any of its parameters changes, so unchanged values are skipped.
        public void ApplyMaterial(
            PristonMaterialRuntime material,
            Texture2D texture,
            Vector2 uvScrollOffset,
            Vector2 layerOffset,
            float opacity,
            Vector3 rimColor,
            Texture2D overlay = null)
        {
            BindTexture(_baseColorMap, ref _boundBase, texture);
            var layer = material.Layer;
            var description = material.Description;
            if (overlay is not null)
            {
                BindTexture(_layerMap, ref _boundLayer, overlay);
            }
            else if (description.LayerMode != PristonLayerMode.None && layer is not null)
            {
                BindTexture(_layerMap, ref _boundLayer, layer.Texture);
            }

            var uv = new Vector4(uvScrollOffset.X, uvScrollOffset.Y, layerOffset.X, layerOffset.Y);
            if (uv != _uv)
            {
                _uvOffsets?.SetValue(uv);
                _uv = uv;
            }

            var reflex = new Vector2(
                material.StageReflex(0) ? 1f : 0f,
                overlay is null && layer is not null && description.LayerMode != PristonLayerMode.None && material.StageReflex(1) ? 1f : 0f);
            if (reflex != _reflex)
            {
                _reflexLayers?.SetValue(reflex);
                _reflex = reflex;
            }

            if (opacity != _opacity)
            {
                _drawOpacity?.SetValue(opacity);
                _opacity = opacity;
            }

            if (rimColor != _rim)
            {
                _rimColor?.SetValue(rimColor);
                _rim = rimColor;
            }

            if (overlay is not null)
            {
                // A gameplay overlay (a scrolling texture added over the body) takes the second layer's place.
                ApplyDescription(description, null);
                _layerMode?.SetValue((float)PristonLayerMode.Add);
                _layerStrength?.SetValue(1f);
                _overlay = overlay;
                return;
            }

            if (description == _description && _overlay is null)
            {
                return;
            }

            _overlay = null;
            ApplyDescription(description, layer);
        }

        private void ApplyDescription(PristonMaterialDescription description, PristonMaterialLayer layer)
        {

            _baseColorFactor?.SetValue(description.BaseColorFactor);
            _roughness?.SetValue(description.Roughness);
            _metallic?.SetValue(description.Metallic);
            _reflectance?.SetValue(description.Reflectance);
            _shadingModel?.SetValue((float)description.ShadingModel);
            _emissiveStrength?.SetValue(description.EmissiveStrength);
            _layerMode?.SetValue(layer is null ? 0f : (float)description.LayerMode);
            _layerStrength?.SetValue(description.LayerStrength);
            _materialBlink?.SetValue(description.Blink ? 1f : 0f);
            _translucency?.SetValue(description.Translucency);
            _description = description;
        }

        public void ApplyActor(Vector4 tint, Vector4 parameters, bool receiver = false)
        {
            if (receiver != _receiver)
            {
                _actorReceiver?.SetValue(receiver ? 1f : 0f);
                _receiver = receiver;
            }

            if (tint != _tint)
            {
                _actorTint?.SetValue(tint);
                _tint = tint;
            }

            if (parameters != _actorParamsValue)
            {
                _actorParams?.SetValue(parameters);
                _actorParamsValue = parameters;
            }
        }

        public void ApplyOutline(Vector4 parameters, Vector4 color)
        {
            _outlineParams?.SetValue(parameters);
            _outlineColor?.SetValue(color);
        }

        public void ApplyCaster(Texture2D texture, Vector2 uvScrollOffset)
        {
            BindTexture(_baseColorMap, ref _boundBase, texture);
            var uv = new Vector4(uvScrollOffset, _uv.Z, _uv.W);
            if (uv != _uv)
            {
                _uvOffsets?.SetValue(uv);
                _uv = uv;
            }
        }

        public void Dispose() => _effect.Dispose();

        private void BindTexture(EffectParameter parameter, ref Texture2D bound, Texture2D texture)
        {
            if (parameter is null || texture is null || ReferenceEquals(bound, texture))
            {
                return;
            }

            parameter.SetValue(texture);
            bound = texture;
            TextureChanges++;
        }
    }
}
