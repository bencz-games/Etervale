using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

public sealed partial class PristonRenderer : IDisposable
{
    private static readonly ConditionalWeakTable<PristonActor, PreparedActorGeometry> PreparedActorGeometries = new();

    // The original game advances stage-object animation by two ticks per millisecond.
    private const float StageObjectFrameRate = 2000f;

    private static readonly bool ShadowDiagnostics =
        Environment.GetEnvironmentVariable("PRISTON_SHADOW_DIAG") == "1";
    // PlaybackRate supplies the rules-layer frame step / 80 ratio for speed-scaled motions.
    private const int MinimumShadowMapSize = 1024;
    private const float ShadowTeleportRefreshDistance = 350f;
    private const uint TexFormScroll = 4;
    private const uint TexFormScroll2 = 6;
    private const uint TexFormScroll10 = 14;
    private const uint TexFormScrollSlow1 = 15;
    private const uint TexFormScrollSlow4 = 18;

    private static readonly PristonMaterial FallbackMaterial = new(
        true,
        0,
        0,
        0,
        false,
        Vector3.One,
        0f,
        0,
        PristonMaterialSurfaceFlags.None,
        string.Empty,
        string.Empty,
        [],
        [],
        0,
        0,
        0);

    private readonly GraphicsDevice _graphicsDevice;
    private readonly IClientLog _log;
    private readonly PristonRenderStateCache _stateCache;
    private readonly PristonShaderLibrary _shaders;
    private readonly PristonTextureLoader _textureLoader;
    private readonly PristonMaterialRuntimeFactory _materialFactory;
    private readonly PristonGpuMeshBuilder _meshBuilder;
    private readonly BatchUploader _batchUploader;
    private RenderTarget2D _shadowMap;
    private readonly PresentationSettings _presentation;
    private readonly PristonSceneTargets _sceneTargets;
    private int _multiSampleCount;
    private readonly List<RenderStageHandle> _stages = [];
    // Rebuilt on residency changes so Draw remains allocation-free.
    private readonly List<RenderBatch> _renderBatches = [];
    private readonly List<RenderBatch> _shadowBatches = [];
    private bool _batchListsDirty;
    // Generations prevent recycled actor slots from validating stale handles.
    private readonly List<ActorRuntime> _actors = [];
    private readonly Stack<int> _freeActorSlots = new();
    private readonly Dictionary<PristonActor, ActorGpuResource> _actorGpuResources = new(ReferenceEqualityComparer.Instance);
    private readonly RenderQueueBuilder _renderQueueBuilder = new();
    private readonly RenderQueueBuilder _portraitQueueBuilder = new();
    private readonly List<RenderBatch> _portraitBatches = [];
    private readonly PristonRenderClock _renderClock = new();
    private uint _renderTime => _renderClock.Milliseconds;
    private bool _disposed;
    private readonly PristonDayNightCycle _dayNight = new();
    private const int LightNight = 0x00001;
    private const int LightLens = 0x00002;
    private const int LightDynamic = 0x80000;

    // Stage lamps (Shaders/Lighting/Shading.hlsli): the illuminance at the reference distance (150 units), in units of the
    // authored level over the night reference (PristonPhotometry), falling off with the inverse square; the authored range
    // is where the light is windowed to zero.
    private const float LampLevelScale = 2f;
    private const float LampRangeStretch = 1f;
    // Self-illumination at night: the luminance (cd/m^2) of an emissive albedo of 1; by day it shows at its authored
    // display level instead (EmissionScale is the larger of the two).
    private const float EmissiveLuminance = 40f;
    // Combat and spell lights by day: a modest share of the daylight, several overlap (meteors, fire fields).
    private const float TransientDaylightShare = 0.12f;

    private PristonPointLight[] _overrideLights;
    private float _lampIntensity = 1f;
    private float _lampRangeScale = 1f;
    private float _glowIntensity = 1f;
    private float _glowScale = 1f;
    private readonly List<PristonPointLight> _activeLights = new(MaxActiveLights);
    public Lighting.PristonTransientLights TransientLights { get; } = new();
    private readonly List<PristonPointLight> _tunedCandidates = [];
    private readonly List<PristonPointLight> _frameGlowLights = [];
    private const float EmitterGlowRadius = 175f;
    private readonly FixedRateRefreshScheduler _sunShadowRefresh = new();
    private readonly PristonShadowCascades _cascades = new();
    private ShadowFilterMode _shadowFilter = ShadowFilterMode.Pcss;
    private bool _ambientOcclusion = true;
    private bool _contactShadows = true;
    private bool _fullShadowRefresh = true;
    private int _cascadeMask;
    private Vector3 _shadowLightDirection;
    private PristonScreenOcclusion _screenOcclusion;
    private readonly PristonLampShadows _lampShadows;
    private readonly PristonLightCulling _lightCulling;
    private readonly PristonEnvironmentLighting _environment;
    private readonly PristonScreenReflections _reflections;
    private bool _screenReflections = true;
    private PristonFrameContext _reflectionFrame;
    private PristonCalibrationSpheres _calibration;
    private bool _calibrationSpheres;
    private readonly PristonVolumetricLight _volumetric;
    private bool _volumetricLight = true;
    private bool _imageBasedLighting = true;
    private int _lampShadowRedraw;
    // Clearing a cascade tile writes far depth whatever the tile held.
    private static readonly DepthStencilState ShadowClearDepth = new()
    {
        Name = "PristonShadowClear",
        DepthBufferEnable = true,
        DepthBufferWriteEnable = true,
        DepthBufferFunction = CompareFunction.Always
    };
    // The depth pre-pass writes depth, linear depth, normals and motion, never the scene colour.
    private static readonly BlendState PrepassBlend = CreatePrepassBlend();
    private Vector3 _sunShadowFocus;
    private bool _sunShadowFocusValid;
    private int _shadowUpdateRate;
    private int _shadowDrawCalls;
    private int _lampShadowMapsUpdated;

    private readonly GlowRenderer _glowRenderer;
    private readonly PristonSkyCapture _skyCapture;
    private Vector3 _towardSun = Vector3.Up;
    private Vector3 _fogSunColour;
    private float _sunElevation;
    private float _lampFactor;
    private PristonFrameExposure _exposure = PristonFrameExposure.Neutral;
    private PristonPostSettings _postSettings = PristonPostSettings.From(new ClientSettings());
    private float _lastDeltaSeconds;
    // Motion vectors: the frame counter the skin palettes are stamped with, and the previous frame's unjittered camera.
    private uint _motionFrame = 1;
    private bool _hasPreviousCamera;
    private Matrix _previousView;
    private Matrix _previousProjection;
    private uint _previousRenderTime;
    private float _previousPreExposure = 1f;
    private PristonTemporalFrame _temporalFrame;
    private PristonExposureInputs _exposureInputs;
    private readonly Dictionary<BlendState, BlendState> _temporalBlendStates = new(ReferenceEqualityComparer.Instance);

    public PristonRenderer(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log = null, PresentationSettings presentation = null)
    {
        _graphicsDevice = graphicsDevice;
        _log = log ?? ConsoleClientLog.Instance;
        _presentation = presentation;
        _stateCache = new PristonRenderStateCache(graphicsDevice);
        _textureLoader = new PristonTextureLoader(graphicsDevice, _log);
        _textureLoader.Initialize();
        _shaders = new PristonShaderLibrary(content, _log, _textureLoader.White);
        _sceneTargets = new PristonSceneTargets(graphicsDevice, content, _log);
        _materialFactory = new PristonMaterialRuntimeFactory(_textureLoader);
        _meshBuilder = new PristonGpuMeshBuilder(graphicsDevice);
        _batchUploader = new BatchUploader(graphicsDevice);
        _glowRenderer = new GlowRenderer(graphicsDevice, content, _log);
        _skyCapture = new PristonSkyCapture(graphicsDevice);
        _lampShadows = new PristonLampShadows(graphicsDevice, _log);
        _environment = new PristonEnvironmentLighting(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonEnvironmentLighting.Asset, _log));
        _reflections = new PristonScreenReflections(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonScreenReflections.Asset, _log));
        _volumetric = new PristonVolumetricLight(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonVolumetricLight.Asset, _log));
        _lightCulling = new PristonLightCulling(graphicsDevice, PristonShaderLibrary.LoadRequired(content, PristonLightCulling.Asset, _log));
        _screenOcclusion = new PristonScreenOcclusion(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonScreenOcclusion.Asset, _log));
        ApplyQuality(presentation?.Rendering ?? new ClientSettings());
        if (_presentation is not null)
        {
            _presentation.RenderingChanged += ApplyQuality;
        }
    }

    private void ApplyQuality(ClientSettings settings)
    {
        if (_shadowMap?.Width != settings.ShadowMapSize)
        {
            // Release first so a size change never holds both maps (an 8192² R32F + D24 pair is ~512 MiB).
            _shadowMap?.Dispose();
            _shadowMap = null;
            _shadowMap = CreateShadowTarget(settings.ShadowMapSize);
        }

        _shadowUpdateRate = settings.ShadowUpdateRate;
        _sunShadowRefresh.Invalidate();
        _cascades.Configure(settings.ShadowCascades, _shadowMap.Width);
        _lampShadows.Configure(settings.ShadowsEnabled ? settings.LampShadowCount : 0, settings.LampShadowTileSize);
        _shadowFilter = settings.ShadowFilter;
        _ambientOcclusion = settings.AmbientOcclusion;
        _contactShadows = settings.ContactShadows;
        _imageBasedLighting = settings.ImageBasedLighting;
        _screenReflections = settings.ScreenSpaceReflections;
        _calibrationSpheres = settings.CalibrationSpheres;
        _volumetricLight = settings.VolumetricLight;
        _fullShadowRefresh = true;
        _multiSampleCount = settings.AntiAliasing == AntiAliasingMode.Msaa ? Math.Clamp(settings.MultiSampleCount, 2, 4) : 0;
        ShadowsEnabled = settings.ShadowsEnabled;
        DistanceFog = PristonDistanceFog.For(settings.DistanceFog);
        DebugView = settings.DebugView;
        ExposureCompensation = settings.ExposureCompensation;
        _maxLocalLights = Math.Clamp(settings.LocalLightLimit, 1, MaxActiveLights);
        _postSettings = PristonPostSettings.From(settings);
        _log.Info(
            $"[Rendering] quality={settings.Quality}; sun shadow={_shadowMap.Width} in {_cascades.Count} cascade(s) of {_cascades.TileSize} " +
            $"to {PristonShadowCascades.ShadowDistance:0} units, {settings.ShadowFilter}; ambient occlusion={settings.AmbientOcclusion}; " +
            $"contact shadows={settings.ContactShadows}; lamp shadows={_lampShadows.Lamps} x {settings.LampShadowTileSize}; " +
            $"anti-aliasing={settings.AntiAliasing}" +
            $"{(_multiSampleCount > 0 ? $" {_multiSampleCount}x" : string.Empty)}; shadow update={FormatUpdateRate(_shadowUpdateRate)}; " +
            $"shadows={ShadowsEnabled}; local lights={_maxLocalLights}; distance fog={settings.DistanceFog}; auto exposure={settings.AutoExposure}; " +
            $"compensation={settings.ExposureCompensation:+0.0;-0.0;0} EV; tonemapper={settings.Tonemapper}; bloom={settings.Bloom}; " +
            $"grade={settings.ColorGrading}; sharpening={settings.Sharpening:0.00}; debug view={DebugView}.");
    }

    // A device that cannot allocate the map throws; each failure steps down to the next smaller size, 2048 being the
    // low tier and 1024 the last resort. The map is a depth surface: the casters write only its 32-bit float depth (no
    // colour, so an opaque caster runs no pixel work) and the lighting samples that depth, the orthographic z.
    private RenderTarget2D CreateShadowTarget(int requestedSize)
    {
        for (var size = requestedSize; ; size /= 2)
        {
            try
            {
                // PreserveContents: the cascades refresh at different rates, and binding a DiscardContents target clears it.
                return new RenderTarget2D(_graphicsDevice, size, size, false, SurfaceFormat.Depth32Float, DepthFormat.None, 0,
                    RenderTargetUsage.PreserveContents);
            }
            catch (Exception exception) when (size > MinimumShadowMapSize && exception is ArgumentException or InvalidOperationException or OutOfMemoryException)
            {
                _log.Warn($"[Rendering] Sun shadow {size} allocation failed; trying {size / 2}: {exception.Message}");
            }
        }
    }

    public BoundingBox Bounds { get; private set; }
    public BoundingBox StageBounds { get; private set; }
    public PristonRenderStats LastFrameStats { get; private set; }
    public PristonFrameProfiler Profiler { get; } = new();
    public PristonStreamingStats LastStreamingStats { get; private set; } = PristonStreamingStats.Empty;

    public bool ShadowsEnabled { get; set; } = true;

    public PristonDistanceFog DistanceFog { get; set; } = PristonDistanceFog.Atmospheric;

    // The active field's share of the distance haze (1 - its haze reduction) and the extra sight before the haze starts.
    public float FieldHaze { get; set; } = 1f;
    public float FieldSightDistance { get; set; }

    // Sky captures rendered since the renderer was created; the fog refreshes its sky copy only on sky changes.
    public int SkyCaptureRefreshes => _skyCapture.Refreshes;

    // Development view of the surface shader; Overdraw also switches every scene pass to additive blending.
    public PristonDebugView DebugView { get; set; }

    private int _skyProfile = -1;
    private int _previousSkyProfile = -1;
    private float _skyBlend = 1f;

    public PristonSkyLibrary Sky { get; set; }
    public PristonSkyProfiles SkyProfiles { get; set; } = PristonSkyProfiles.Forest;

    public float TimeOfDay => _dayNight.TimeOfDay;

    public Color SkyColor { get; private set; } = new(0.45f, 0.62f, 0.85f);

    public int StageLightCount => _stages.Sum(static s => s.StageLights.Length);

    public float DayLengthSeconds
    {
        get => _dayNight.DayLengthSeconds;
        set => _dayNight.DayLengthSeconds = value;
    }

    public void SetTimeOfDay(float hours) => _dayNight.SetTime(hours);

    public void ConfigureDayNight(DayNightSettings settings) => _dayNight.Configure(settings);

    public void ConfigureFieldLighting(
        float intensity,
        float rangeScale,
        PristonPointLight[] overrideLights,
        float glowIntensity = 1f,
        float glowScale = 1f)
    {
        _lampIntensity = intensity;
        _lampRangeScale = MathF.Max(0.01f, rangeScale);
        _overrideLights = overrideLights is { Length: > 0 } ? overrideLights : null;
        _glowIntensity = MathF.Max(0f, glowIntensity);
        _glowScale = MathF.Max(0f, glowScale);
    }

    public bool Underground { get; set; }
    public Vector3 UndergroundAmbient { get; set; } = new(0.30f, 0.30f, 0.34f);

    // This frame's linear view depth of the opaque scene while the depth pre-pass ran (no MSAA); null otherwise.
    // Effects drawn between Draw and ComposeScene may sample it for soft particles.
    public Texture2D SceneLinearDepth => _prepassDrawn ? _sceneTargets.LinearDepth : null;

    private readonly record struct StreamingWork(StreamingStep Step, long Bytes)
    {
        public static StreamingWork Wait => new(StreamingStep.PreparationWait, 0);
    }

    private sealed record PreparedStaticBatch(
        PristonMaterialRuntime Material,
        PristonGpuMeshBuilder.PreparedMesh Mesh,
        PristonStaticVertex[] Vertices,
        bool ShadowCoveredByAggregate);

    private sealed class StaticShadowCollector
    {
        // Spatial aggregation keeps depth submissions low without turning the whole field into one
        // giant caster that defeats the light-frustum cull. A 1024-unit cell gives the 2800-unit sun
        // footprint useful granularity.
        private const float CellSize = 1024f;
        private readonly Dictionary<(int X, int Z), List<PristonCornerVertex>> _cells = [];

        public int Count => _cells.Count;
        public PristonMaterialRuntime Material { get; private set; }

        public void Add(PristonMaterialRuntime material, List<PristonCornerVertex> vertices)
        {
            Material ??= material;
            var source = CollectionsMarshal.AsSpan(vertices);
            for (var index = 0; index + 2 < source.Length; index += 3)
            {
                var center = (source[index].Position + source[index + 1].Position + source[index + 2].Position) / 3f;
                var key = ((int)MathF.Floor(center.X / CellSize), (int)MathF.Floor(center.Z / CellSize));
                ref var cell = ref CollectionsMarshal.GetValueRefOrAddDefault(_cells, key, out var exists);
                if (!exists)
                {
                    // Eight triangles cover the common small-cell case while avoiding a large eager
                    // allocation for sparse edge cells. List<T> grows geometrically if the cell is dense.
                    cell = new List<PristonCornerVertex>(24);
                }

                var destinationIndex = cell.Count;
                cell.EnsureCapacity(destinationIndex + 3);
                CollectionsMarshal.SetCount(cell, destinationIndex + 3);
                source.Slice(index, 3).CopyTo(CollectionsMarshal.AsSpan(cell).Slice(destinationIndex, 3));
            }
        }

        public IEnumerable<List<PristonCornerVertex>> OrderedCells()
        {
            foreach (var cell in _cells.OrderBy(static pair => pair.Key.X).ThenBy(static pair => pair.Key.Z))
            {
                yield return cell.Value;
            }
        }
    }

    internal int BatchListRebuilds { get; private set; }

    public void Load(PristonStage stage, IEnumerable<PristonModel> models) => Load(stage, models, []);

    public void Load(PristonStage stage, IEnumerable<PristonModel> models, IEnumerable<PristonActorInstance> actors)
    {
        RemoveAllStages();
        ClearActors();
        AddStage(stage, models);
        foreach (var actor in actors)
            AddActor(actor);
    }

    public void Update(GameTime gameTime)
    {
        var dt = MathF.Max(0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _lastDeltaSeconds = dt;
        _dayNight.Update(dt);
        TransientLights.Update(MathF.Max(0, dt));
        var profile = SkyProfileNow;
        if (_skyProfile != profile)
        {
            _previousSkyProfile = _skyProfile;
            _skyProfile = profile;
            _skyBlend = _previousSkyProfile < 0 ? 1f : 0f;
        }

        _skyBlend = MathF.Min(1f, _skyBlend + MathF.Max(0f, dt) / 2f);
        _renderClock.Advance(gameTime.ElapsedGameTime);
        if (UseExternalAnimationClock)
            return;

        Profiler.Begin(RenderSection.Update);
        AdvanceAnimation(dt);
        Profiler.End(RenderSection.Update);
    }

    private void AdvanceAnimation(float dt)
    {
        foreach (var stage in _stages)
        {
            foreach (var model in stage.AnimatedModels)
            {
                model.Advance(dt * StageObjectFrameRate);
            }

            foreach (var group in stage.AnimatedGroups)
            {
                UpdateAnimatedStageGroup(group);
            }
        }
        _animationFrame++;
        var posed = 0;
        var reused = 0;
        for (var slot = 0; slot < _actors.Count; slot++)
        {
            var actor = _actors[slot];
            if (!actor.Alive || actor.EchoSource.IsValid)
                continue;
            actor.Advance(dt);
            if (!actor.Parent.IsValid)
            {
                var (interval, onScreen) = PoseLevel(actor);
                // An actor coming into view shows its current pose at once.
                var refresh = CrowdAnimationLod.Due(interval, _animationFrame, slot) || onScreen && !actor.WasOnScreen;
                actor.WasOnScreen = onScreen;
                UpdateActor(actor, refresh);
                if (refresh)
                    posed++;
                else
                    reused++;
            }
        }

        LastAnimationCounts = (posed, reused);

        FollowEchoes();
        foreach (var actor in _actors)
        {
            if (actor.Alive && actor.Parent.IsValid && TryResolveActor(actor.Parent, out var parent))
            {
                actor.World = AttachedWorld(actor, parent);
                UpdateActor(actor);
            }
        }
    }

    public bool UseExternalAnimationClock { get; set; }

    // The root actors of the last animation update whose pose was sampled again, and those that kept theirs.
    public (int Posed, int Reused) LastAnimationCounts { get; private set; }

    // The crowd level of detail of an actor, from the view of the last frame drawn (every frame before the first): its
    // distance from the camera's focus, so what the player looks at stays at full rate whatever the zoom.
    private (int Interval, bool OnScreen) PoseLevel(ActorRuntime actor)
    {
        if (!_lodCameraKnown)
            return (1, true);
        var onScreen = !actor.HasWorldBounds || _lodFrustum.Intersects(actor.WorldBounds);
        return (CrowdAnimationLod.Interval(Vector3.Distance(actor.World.Translation, _lodFocus), onScreen), onScreen);
    }

    public void SetSceneAnimationFrame(float ticks)
    {
        foreach (var stage in _stages)
        {
            foreach (var model in stage.AnimatedModels)
            {
                model.SetFrame(ticks);
            }

            foreach (var group in stage.AnimatedGroups)
            {
                UpdateAnimatedStageGroup(group);
            }
        }
        foreach (var actor in _actors)
        {
            if (!actor.Alive)
                continue;
            actor.SetFrame(ticks);
            UpdateActor(actor);
        }
    }

    // Frame passes, in order (each a profiler section): Lighting (CPU light selection), Queue (culling), SunShadow,
    // SkyCapture (the fog's sky copy, when the sky changed), Sky (the dome, then the pass that fills every scene target),
    // Opaque and Masked (with the motion and depth targets of the temporal anti-aliasing), Transparent, Glow. The HDR
    // scene stays bound after Draw so effects can add to it; ComposeScene then runs the post chain (Temporal, Exposure,
    // Bloom, DisplayTransform, PostAntiAliasing) into the caller's target.
    public void Draw(PristonCamera camera)
    {
        _lodFrustum.Matrix = camera.View * camera.Projection;
        _lodFocus = camera.Target;
        _lodCameraKnown = true;
        var staticBatches = 0;
        var dynamicBatches = 0;
        var drawCalls = 0;
        var triangles = 0;
        var staticVertices = 0;
        var staticIndices = 0;
        var normalBatches = 0;
        var twoSidedBatches = 0;
        var visibleStaticMeshDiagnostics = new PristonMeshDiagnostics();
        var temporal = _postSettings.AntiAliasing == AntiAliasingMode.Taa && DebugView != PristonDebugView.Overdraw;
        // The depth pre-pass pays for itself: the lit pass shades each pixel once and the light tiles get depth bounds.
        var prepass = _multiSampleCount == 0 && DebugView != PristonDebugView.Overdraw;
        // Binding the scene target ends the caller's render pass, so the profiler can reset its timestamps outside one.
        _sceneTargets.Begin(_multiSampleCount, temporal, prepass);
        Profiler.BeginFrame();
        Profiler.Begin(RenderSection.Frame);
        EnsureBatchLists();
        _batchUploader.BeginFrame();
        _shaders.BeginFrame();
        _shadowDrawCalls = 0;
        _lampShadowMapsUpdated = 0;
        var skyBatchCount = 0;
        Profiler.Begin(RenderSection.Lighting);
        var lighting = BuildFrameLighting(camera.Target);
        Profiler.End(RenderSection.Lighting);
        var sunShadowsEnabled = ShadowsEnabled && lighting.Sun.DiffuseColor != Vector3.Zero && !Underground;
        InvalidateShadowAfterTeleport(camera.Target);
        // The moon's shadows move with the actors only: half the refresh rate is enough for its soft, dim light.
        var shadowRate = _sunElevation > -0.05f || _shadowUpdateRate == 0 ? _shadowUpdateRate : Math.Max(_shadowUpdateRate / 2, 30);
        var sunShadowUpdated = _sunShadowRefresh.ShouldRefresh(_renderTime, shadowRate, sunShadowsEnabled);
        var sunShadowRedraw = sunShadowUpdated;
        if (sunShadowUpdated)
        {
            FitCascades(camera, lighting.Sun.Direction);
            _sunShadowFocus = camera.Target;
            _sunShadowFocusValid = true;
        }
        else if (sunShadowsEnabled && _sunShadowFocusValid)
        {
            // Between refreshes the near cascades, which hold the characters around the camera, still redraw on their
            // current fit: a caster left a refresh behind would shadow the character that has moved or turned away from it
            // (dark facets on a running character with the light at its back).
            _cascadeMask = PristonShadowCascades.NearMask & ((1 << _cascades.Count) - 1);
            sunShadowRedraw = true;
        }

        var lightViewProjection = Matrix.Identity;
        _lampShadowRedraw = ShadowsEnabled && _lampFactor > 0.01f
            ? _lampShadows.Assign(lighting.PointLights, lighting.TransientLightCount, _lampHasCasters ??= LampHasCasters)
            : 0;
        var lampShadows = _lampShadows.Atlas is not null && ShadowsEnabled && _lampFactor > 0.01f
            ? new PristonLampShadowFrame(_lampShadows.Atlas, _lampShadows.Rows, _lampShadows.FaceSize, _lampShadows.Lamps)
            : default;
        var displayScale = _exposure.DisplayScale;
        var skyRadiance = SrgbToLinear((BackgroundColor ?? SkyColor).ToVector3()) / displayScale;
        var projection = camera.Projection;
        var jitterPixels = Vector2.Zero;
        var jitterClip = Vector2.Zero;
        if (temporal)
        {
            jitterPixels = PristonTemporalAntiAliasing.JitterPixels(_motionFrame);
            jitterClip = PristonTemporalAntiAliasing.PixelsToClip(jitterPixels, _sceneTargets.Width, _sceneTargets.Height);
            projection = PristonTemporalAntiAliasing.Jitter(projection, jitterClip);
        }

        var hasPrevious = temporal && _hasPreviousCamera;
        var motion = new PristonMotionFrame(
            hasPrevious ? _previousView * _previousProjection : camera.View * camera.Projection,
            jitterClip,
            hasPrevious ? _previousRenderTime : _renderTime,
            temporal,
            _motionFrame);
        var frame = new PristonFrameContext(
            camera.View,
            projection,
            camera.Position,
            _renderTime,
            lighting,
            lightViewProjection,
            sunShadowsEnabled ? _shadowMap : _textureLoader.White,
            sunShadowsEnabled,
            _lampFactor,
            WeatherFog(DistanceFog with
            {
                Color = skyRadiance, MaxOpacity = DistanceFog.MaxOpacity * FieldHaze, StartDistance = DistanceFog.StartDistance + FieldSightDistance
            }),
            DebugView,
            Exposure: displayScale,
            EmissionScale: _exposure.EmissionScale,
            Motion: motion,
            Shadows: new PristonShadowFrame(_cascades, ShadowFilterOfHour(), null, Vector2.Zero, temporal ? _motionFrame % 8 : 0f),
            LampShadows: lampShadows);
        PristonUnlitEffect.SceneExposure = displayScale;
        _temporalFrame = new PristonTemporalFrame(
            camera.View,
            camera.Projection,
            hasPrevious ? _previousView : camera.View,
            hasPrevious ? _previousProjection : camera.Projection,
            jitterPixels,
            _previousPreExposure > 0f ? _exposure.PreExposure / _previousPreExposure : 1f);
        Profiler.Begin(RenderSection.Queue);
        var culledBatches = _renderQueueBuilder.Build(
            _renderBatches,
            _shadowBatches,
            frame,
            sunShadowRedraw || _lampShadowRedraw != 0);
        Profiler.End(RenderSection.Queue);
        if (sunShadowRedraw)
        {
            Profiler.Begin(RenderSection.SunShadow);
            RenderShadowMap();
            Profiler.End(RenderSection.SunShadow);
        }

        if (_lampShadowRedraw != 0)
        {
            Profiler.Begin(RenderSection.LampShadows);
            RenderLampShadows();
            Profiler.End(RenderSection.LampShadows);
        }

        _stateCache.BeginFrame();
        _stateCache.InvalidateDeviceState();
        var skyVisible = Sky != null && !Underground && DebugView != PristonDebugView.Overdraw;
        var skyPosition = new Vector3(0f, MathF.Min(camera.Position.Y, 150000f / 256f) / 8f, 0f);
        frame = frame with { FogSky = PrepareFogSky(frame, skyVisible, skyPosition) };
        if (_imageBasedLighting && DebugView != PristonDebugView.Overdraw)
        {
            if (_environment.ShouldRefresh(_renderTime))
            {
                Profiler.Begin(RenderSection.Environment);
                RenderEnvironment(frame, skyVisible, skyPosition);
                Profiler.End(RenderSection.Environment);
            }

            frame = frame with { Environment = new PristonEnvironmentFrame(_environment.Atlas, _environment.Sh, _environment.BrdfLut) };
        }
        Profiler.Begin(RenderSection.Sky);
        if (skyVisible)
        {
            _sceneTargets.BeginSky(SkyColor);
            _stateCache.InvalidateDeviceState();
            var skyView = frame.View;
            skyView.Translation = Vector3.Zero;
            skyView.Translation = Vector3.TransformNormal(-skyPosition, skyView);
            var skyProjection = PristonDepth.InfiniteFar(frame.Projection);
            _shaders.ApplyFrame(CreateSkyFrame(frame, skyView, skyProjection, skyPosition));
            _stateCache.SetDepthStencilState(DepthStencilState.None);
            skyBatchCount = DrawSkyLayers(
                SkyProfileNow,
                ref staticBatches,
                ref dynamicBatches,
                ref drawCalls,
                ref triangles,
                ref staticVertices,
                ref staticIndices,
                ref normalBatches,
                ref twoSidedBatches,
                ref visibleStaticMeshDiagnostics);
        }

        _sceneTargets.Fill(skyVisible, displayScale, skyRadiance);
        Profiler.End(RenderSection.Sky);

        _stateCache.InvalidateDeviceState();
        _stateCache.SetDepthStencilState(PristonDepth.Write);
        _shaders.ApplyFrame(frame);
        _prepassDrawn = false;
        if (_sceneTargets.Prepass)
        {
            Profiler.Begin(RenderSection.Prepass);
            DrawPrepass(_renderQueueBuilder.Queue);
            Profiler.End(RenderSection.Prepass);
            // Contact shadows of the sun only, as for the shafts.
            var contact = _contactShadows && sunShadowsEnabled && _sunElevation > -0.05f;
            if (_ambientOcclusion || contact)
            {
                Profiler.Begin(RenderSection.AmbientOcclusion);
                var occlusion = _screenOcclusion.Run(
                    _sceneTargets.LinearDepth,
                    _sceneTargets.Normals,
                    frame.View,
                    frame.Projection,
                    -Vector3.Normalize(lighting.Sun.Direction),
                    _ambientOcclusion,
                    contact);
                Profiler.End(RenderSection.AmbientOcclusion);
                var strength = new Vector2(_ambientOcclusion ? 1f : 0f, contact ? 1f : 0f);
                frame = frame with { Shadows = frame.Shadows with { Occlusion = occlusion, OcclusionStrength = strength } };
                _shaders.ApplyFrame(frame);
            }

            _sceneTargets.Bind();
            _stateCache.InvalidateDeviceState();
            _prepassDrawn = true;
        }

        if (lighting.PointLights.Count > 0)
        {
            Profiler.Begin(RenderSection.LightCulling);
            var near = PristonDepth.NearPlane(frame.Projection);
            var far = PristonDepth.FarPlane(frame.Projection);
            var tiles = _lightCulling.Run(lighting.PointLights, frame.View, frame.Projection, near, far, _sceneTargets.Width,
                _sceneTargets.Height, _prepassDrawn ? _sceneTargets.LinearDepth : null, _textureLoader.White);
            Profiler.End(RenderSection.LightCulling);
            frame = frame with { Tiles = tiles };
            _shaders.ApplyFrame(frame);
            _sceneTargets.Bind();
            _stateCache.InvalidateDeviceState();
        }

        _reflectionFrame = frame;
        DrawQueue(
            _renderQueueBuilder.Queue,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        if (_calibrationSpheres)
        {
            _calibration ??= new PristonCalibrationSpheres(_textureLoader.White);
            _calibration.Place(camera.Target);
            _stateCache.SetBlendState(BlendState.Opaque);
            _stateCache.SetDepthStencilState(PristonDepth.Write);
            DrawBatch(_calibration.Grey, ref staticBatches, ref dynamicBatches, ref drawCalls, ref triangles, ref staticVertices,
                ref staticIndices, ref normalBatches, ref twoSidedBatches, ref visibleStaticMeshDiagnostics);
            DrawBatch(_calibration.Chrome, ref staticBatches, ref dynamicBatches, ref drawCalls, ref triangles, ref staticVertices,
                ref staticIndices, ref normalBatches, ref twoSidedBatches, ref visibleStaticMeshDiagnostics);
        }

        _frameGlowLights.Clear();
        foreach (var stage in _stages)
        {
            _frameGlowLights.AddRange(stage.GlowLights);
            foreach (var prop in stage.AnimatedModels)
            {
                if (prop.IsGlowEmitter)
                {
                    _frameGlowLights.Add(new PristonPointLight(
                        prop.WorldCenter(),
                        EmitterGlowRadius * stage.GlowScale,
                        prop.GlowColor,
                        stage.GlowIntensity));
                }
            }
        }

        Profiler.Begin(RenderSection.Glow);
        _glowRenderer.Draw(frame, _frameGlowLights, _lampFactor);
        Profiler.End(RenderSection.Glow);
        // Shafts of the sun only: the moon's are too faint to pay for.
        if (_prepassDrawn && _volumetricLight && sunShadowsEnabled && _sunElevation > -0.05f)
        {
            Profiler.Begin(RenderSection.Volumetric);
            // The march stops where the distance fog starts, which covers the medium beyond.
            _volumetric.Run(
                _sceneTargets.LinearDepth,
                _shadowMap,
                _cascades,
                camera.View,
                camera.Projection,
                camera.Position,
                camera.Target,
                -Vector3.Normalize(lighting.Sun.Direction),
                lighting.Sun.DiffuseColor,
                DistanceFog.Enabled ? MathF.Min(DistanceFog.StartDistance, PristonVolumetricLight.MaxDistance) : PristonVolumetricLight.MaxDistance,
                _motionFrame,
                _sceneTargets.Bind);
            Profiler.End(RenderSection.Volumetric);
            _stateCache.InvalidateDeviceState();
        }
        LastFrameStats = new PristonRenderStats(
            _renderBatches.Count + skyBatchCount,
            _renderQueueBuilder.Queue.Count + skyBatchCount,
            culledBatches,
            staticBatches,
            dynamicBatches,
            drawCalls,
            triangles,
            staticVertices,
            staticIndices,
            normalBatches,
            twoSidedBatches,
            _stateCache.TotalStateChanges + _shaders.TextureChanges,
            _shaders.TextureChanges,
            _stateCache.VertexBufferChanges,
            _stateCache.IndexBufferChanges,
            _batchUploader.UploadedBytes,
            visibleStaticMeshDiagnostics,
            sunShadowUpdated,
            _lampShadowMapsUpdated,
            _shadowDrawCalls);
        var dayMetering = MathHelper.Clamp((_exposure.IncidentEv100 - 4f) / 4f, 0f, 1f);
        _exposureInputs = new PristonExposureInputs(
            _exposure.PreExposure,
            _exposure.IncidentEv100,
            Underground ? 0f : PristonAutoExposure.DayIncidentWeight * dayMetering,
            ExposureCompensation,
            Underground,
            _dayNight.NightHue);
        _previousView = camera.View;
        _previousProjection = camera.Projection;
        _previousRenderTime = _renderTime;
        _previousPreExposure = _exposure.PreExposure;
        _hasPreviousCamera = temporal;
        Profiler.End(RenderSection.Frame);
    }

    // Ends the HDR scene: the temporal resolve, the exposure, bloom and the display transform into the target that was
    // bound when Draw began. Call after the effects that draw into the scene, before the UI.
    public void ComposeScene()
    {
        var postFrame = new PristonPostFrame(
            _postSettings,
            _exposureInputs,
            Vector4.Zero,
            default,
            _lastDeltaSeconds,
            DebugView == PristonDebugView.ExposureFalseColour);
        _sceneTargets.Compose(postFrame, _temporalFrame, Profiler);
        _stateCache.InvalidateDeviceState();
        _motionFrame++;
    }

    // Exposure compensation in stops on top of the automatic (or fixed) exposure (ClientSettings.ExposureCompensation).
    public float ExposureCompensation { get; set; }

    // The nominal display scale of the last frame (PristonFrameContext.Exposure).
    public float DisplayScale => _exposure.DisplayScale;

    // Makes the next frame adopt its metered exposure and drop the temporal history (camera cuts, teleports).
    public void ResetTemporalState()
    {
        _sceneTargets.Post.Exposure.Snap();
        _sceneTargets.InvalidateHistory();
        _hasPreviousCamera = false;
    }

    // Whether actors take the authored light of the floor they stand on (PristonStageLightField). A showcase stage (the
    // character selection) lights its characters in full instead.
    public bool ActorFloorLight { get; set; } = true;

    // Background of a scene without a sky (tools and captures); null clears to the sky colour of the hour.
    public Color? BackgroundColor { get; set; }

    // Samples of the HDR scene target as created (0 without MSAA).
    public int EffectiveMultiSampleCount => _sceneTargets.EffectiveMultiSampleCount;

    private static Vector3 SrgbToLinear(Vector3 colour) => new(SrgbToLinear(colour.X), SrgbToLinear(colour.Y), SrgbToLinear(colour.Z));

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private const int MaxActiveLights = 32;
    private int _maxLocalLights = MaxActiveLights;
    // The gameplay colour add (hover and target highlight) becomes a rim of this strength rather than added light.
    private const float SelectionRimScale = 0.6f;

    private readonly BoundingFrustum _shadowCasterFrustum = new(Matrix.Identity);
    // The camera of the last frame drawn, for the crowd level of detail of the next animation update.
    private readonly BoundingFrustum _lodFrustum = new(Matrix.Identity);
    private Vector3 _lodFocus;
    private bool _lodCameraKnown;
    private uint _animationFrame;
    private Func<Vector3, float, bool> _lampHasCasters;

    private bool _temporalTargetsBound;
    private bool _prepassDrawn;
    private const float OutlinePixels = 1.4f;
    private const float OutlineDepthPush = 16f;
    private static readonly (float X, float Y)[] OutlineDirections = [(1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f)];
    private bool _drawingTransparent;
    private bool _prepassMode;
    // Whether the pre-pass drew a surface smooth enough for the screen-space reflections (Passes/Reflections.fx reflects
    // smoothness from 0.55; water and ice are this smooth, the armour shine is not): the pass and its copy are skipped
    // otherwise.
    private bool _smoothSurfaceVisible;
    private const float SmoothRoughness = 0.3f;

    private sealed record PreparedSkinnedMesh(
        PristonSkinnedVertex[] Vertices,
        int[] Indices,
        int TriangleCount,
        BoundingBox Bounds);

    private sealed record PreparedAnimatedBatch(PristonMaterial Material, PreparedSkinnedMesh Mesh);
    private sealed record PreparedAnimatedModel(int BoneCount, PreparedAnimatedBatch[] Batches);
    private sealed record PreparedAnimatedInstance(
        PristonModel Model,
        string Directory,
        Matrix World,
        PreparedAnimatedModel Prepared);
    private sealed record PendingAnimatedBatch(
        PristonMaterialRuntime Material,
        List<PreparedSkinnedMesh> Meshes,
        List<AnimatedModelRuntime> Models);
    private sealed record PreparedActorBatch(PristonMaterial Material, PreparedSkinnedMesh Mesh);
    private sealed record PreparedActorGeometry(int[] BoneMap, PreparedActorBatch[] Batches);
    private sealed record ActorGpuBatch(
        PristonMaterialRuntime Material,
        VertexBuffer VertexBuffer,
        IndexBuffer IndexBuffer,
        int PrimitiveCount,
        int VertexCount,
        int IndexCount,
        BoundingBox Bounds,
        bool ShadowCoveredByAggregate = false);

    private readonly record struct StaticMergeKey(
        Texture2D Texture,
        bool AlphaTest,
        bool TwoSided,
        bool SelfIllum,
        BlendState Blend);

    private readonly record struct AnimatedMergeKey(
        Texture2D Texture,
        bool AlphaTest,
        bool TwoSided,
        bool SelfIllum,
        bool Blinking,
        bool Water,
        bool Wind,
        bool Reflex,
        BlendState Blend);

    public void Dispose()
    {
        if (_disposed)
            return;

        RemoveAllStages();
        ClearActors();
        if (_presentation is not null)
        {
            _presentation.RenderingChanged -= ApplyQuality;
        }

        _shadowMap.Dispose();
        Profiler.Dispose();
        _glowRenderer.Dispose();
        _skyCapture.Dispose();
        _sceneTargets.Dispose();
        _screenOcclusion.Dispose();
        _lampShadows.Dispose();
        _lightCulling.Dispose();
        _environment.Dispose();
        _reflections.Dispose();
        _volumetric.Dispose();
        _shaders.Dispose();
        _textureLoader.Dispose();
        _disposed = true;
    }

    private sealed class ActorRuntime
    {
        public ActorRuntime(PristonActorInstance instance)
        {
            Instance = instance;
            World = instance.World;
            Animator = new PristonModelAnimator(instance.Actor.Skeleton);
            var defaultCount = Math.Min(Animator.ObjectCount, PristonShaderLibrary.MaxBones);
            BoneMap = new int[defaultCount];
            for (var i = 0; i < defaultCount; i++)
                BoneMap[i] = i;
            Palette = new PristonSkinPalette(defaultCount);
            Motion = SelectActorMotion(instance.Actor, instance.Motion, instance.MotionContext);
            Frame = StartTicks(Motion);
        }

        public PristonActorInstance Instance { get; }
        public PristonModelAnimator Animator { get; }
        private float _sampledFrame = float.NaN;
        private Matrix[] _sampledTransforms;
        private float _paletteFrame = float.NaN;
        private Matrix _paletteWorld;
        private bool _paletteAttached;

        public Matrix[] SampleSkeleton()
        {
            if (_sampledFrame != Frame)
            {
                _sampledTransforms = Animator.Evaluate(Frame, Motion.EndFrame * GameUnits.FrameTicks);
                _sampledFrame = Frame;
            }

            return _sampledTransforms;
        }

        // The pose of the palette: sampled again as Pose.NeedsSample says, otherwise the one it shows.
        public ActorPose Pose { get; } = new();

        public Matrix[] SamplePose(bool refresh) =>
            Pose.NeedsSample(refresh, Frame, Motion) ? Pose.Take(SampleSkeleton(), Frame, Motion) : Pose.Transforms;

        // The pose an attached actor hangs from: the palette's, or the current frame's before the first palette.
        public Matrix[] ShownPose() => Pose.Taken ? Pose.Transforms : SampleSkeleton();

        // The world bounds of the last palette and whether the last update found it in view, for the level of detail.
        public BoundingBox WorldBounds { get; set; }
        public bool HasWorldBounds { get; set; }
        public bool WasOnScreen { get; set; }

        // Shares the animator's buffer with SampleSkeleton, so the next SampleSkeleton evaluates again.
        public Matrix[] SampleSkeletonAt(float frame, float rotationEnd)
        {
            _sampledFrame = float.NaN;
            return Animator.Evaluate(frame, rotationEnd);
        }

        public PristonSkinPalette Palette { get; private set; }
        public int[] BoneMap { get; private set; }
        public Matrix World { get; set; }
        public ActorHandle Parent { get; set; } = ActorHandle.None;
        public int ParentBone { get; set; }
        public int CheerBone { get; set; } = -1;
        public ActorGpuResource GpuResource { get; set; }

        public bool Alive { get; set; } = true;
        // Kept with its clip and world but not drawn (a model's alternate between the motions it plays).
        public bool Hidden { get; set; }
        public int Generation { get; set; }
        public ActorShade Shade { get; set; }
        // An echo plays its source's clip EchoLagTicks behind.
        public ActorHandle EchoSource { get; set; } = ActorHandle.None;
        public float EchoLagTicks { get; set; }

        public void Follow(ActorRuntime source)
        {
            World = source.World;
            Motion = source.Motion;
            var start = StartTicks(Motion);
            Frame = source.Frame - EchoLagTicks;
            if (Shade is { } shade)
            {
                shade.EchoHidden = Frame <= start;
            }

            Frame = MathF.Max(Frame, start);
        }

        public void SetBoneMap(int[] boneMap)
        {
            BoneMap = boneMap;
            Palette = new PristonSkinPalette(boneMap.Length);
            _paletteFrame = float.NaN;
        }

        // A pose kept by the level of detail stays current while the world and the motion do.
        public bool PaletteIsCurrent(bool refresh)
        {
            var attached = Parent.IsValid;
            if (_paletteWorld != World || _paletteAttached != attached)
                return false;
            return attached || !Pose.NeedsSample(refresh, Frame, Motion) && _paletteFrame == Pose.Frame;
        }

        public void MarkPaletteCurrent()
        {
            _paletteFrame = Parent.IsValid ? 0f : Pose.Frame;
            _paletteWorld = World;
            _paletteAttached = Parent.IsValid;
        }
        public PristonMotionInfo Motion { get; private set; }
        public float Frame { get; private set; }
        public float PlaybackRate { get; set; } = 1f;
        public List<RenderBatch> Batches { get; } = [];
        public List<RenderBatch> ShadowBatches { get; } = [];
        public BoundingBox BindBounds { get; private set; }
        private bool _hasBindBounds;

        public void AddBatch(RenderBatch batch)
        {
            Batches.Add(batch);
            BindBounds = _hasBindBounds
                ? BoundingBox.CreateMerged(BindBounds, batch.SkinnedBounds)
                : batch.SkinnedBounds;
            _hasBindBounds = true;
        }

        public void AddShadowBatch(RenderBatch batch)
        {
            ShadowBatches.Add(batch);
        }

        public void SetMotion(MotionCode motion)
        {
            var resolved = SelectActorMotion(Instance.Actor, motion, Instance.MotionContext);
            if (resolved == Motion)
                return;
            Motion = resolved;
            Frame = StartTicks(Motion);
        }

        public void ForceMotion(MotionCode motion)
        {
            Motion = SelectActorMotion(Instance.Actor, motion, Instance.MotionContext);
            Frame = StartTicks(Motion);
        }

        public void ForceMotionIndex(int motionIndex)
        {
            var info = Instance.Actor.Info;
            var count = Math.Min(info.Motions.Count, checked((int)info.MotionCount));
            if ((uint)motionIndex >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(motionIndex), motionIndex, $"Motion index must be between 0 and {count - 1}.");
            }

            var motion = info.Motions[motionIndex];
            if (motion.EndFrame <= motion.StartFrame)
            {
                throw new InvalidDataException($"Motion {motionIndex} in '{info.Path}' has an empty frame range.");
            }

            Motion = ApplyMotionFrameOffset(Instance.Actor, motion);
            Frame = StartTicks(Motion);
        }

        // Jumps within the current motion to a frame counted from its start (a released charge skips ahead).
        public void SeekMotion(int framesFromStart)
        {
            var start = StartTicks(Motion);
            var end = EndTicks(Motion, Instance.Actor.Skeleton.LastFrame);
            Frame = Math.Clamp(start + framesFromStart * GameUnits.FrameTicks, start, Math.Max(start, end));
        }

        public void Advance(float deltaSeconds)
        {
            var start = StartTicks(Motion);
            var end = EndTicks(Motion, Instance.Actor.Skeleton.LastFrame);
            if (end <= start)
            {
                Frame = start;
                return;
            }

            Frame += deltaSeconds * GameUnits.FrameTicksPerSecond * RateFactor();
            if (Motion.Repeat != 0)
            {
                var span = end - start;
                while (Frame >= end)
                    Frame = start + (Frame - end) % span;
                if (Frame < start)
                    Frame = start;
            }
            else if (Frame >= end)
            {
                if (ReturnsToStand(Motion))
                {
                    Motion = SelectActorMotion(Instance.Actor, MotionCode.Stand, Instance.MotionContext);
                    Frame = StartTicks(Motion);
                }
                else
                {
                    Frame = end - 1;
                }
            }
        }

        public void SetFrame(float ticks)
        {
            var start = StartTicks(Motion);
            var end = EndTicks(Motion, Instance.Actor.Skeleton.LastFrame);
            Frame = end > start ? MathHelper.Clamp(ticks, start, end - 1) : start;
        }

        private static int StartTicks(PristonMotionInfo motion) => (int)motion.StartFrame * GameUnits.FrameTicks;

        private static int EndTicks(PristonMotionInfo motion, int fallback) =>
            motion.EndFrame > motion.StartFrame ? (int)motion.EndFrame * 160 : fallback;

        // Only one-shots driven by gameplay return to Stand; unlisted motions hold their final pose.
        private static bool ReturnsToStand(PristonMotionInfo motion)
        {
            var state = (MotionCode)motion.State;
            return state is
                MotionCode.Attack or
                MotionCode.Damage or
                MotionCode.Skill or
                MotionCode.Eat or
                MotionCode.FallStand or
                MotionCode.FallDamage or
                MotionCode.Yahoo or
                MotionCode.Restart or
                MotionCode.Hammer;
        }

        private float RateFactor()
        {
            var state = (MotionCode)Motion.State;
            return state switch
            {
                MotionCode.Walk or
                MotionCode.Run or
                MotionCode.Attack or
                MotionCode.Skill => PlaybackRate,
                MotionCode.Damage or
                MotionCode.Eat => MotionCodes.DamageAndEatRate,
                _ => 1f,
            };
        }
    }

    private sealed class ActorGpuResource(PristonActor actor, ActorGpuBatch[] batches, ActorGpuBatch shadowAggregate)
    {
        public PristonActor Actor { get; } = actor;
        public ActorGpuBatch[] Batches { get; } = batches;
        public ActorGpuBatch ShadowAggregate { get; } = shadowAggregate;
        public int ReferenceCount { get; set; }
    }
}
