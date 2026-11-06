using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Physics;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal sealed class RenderBenchmarkProbe : Game
{
    private readonly record struct FrameSample(
        double IntervalMs,
        double UpdateMs,
        double DrawMs,
        double PresentMs,
        long AllocatedBytes,
        bool SunShadowUpdated,
        int LampShadowMapsUpdated,
        int ShadowDrawCalls);
    private sealed record Distribution(double Mean, double P95, double P99, double Maximum);
    private sealed record RunResult(int Run, int Frames, double Seconds, double Fps, Distribution IntervalMs,
        Distribution UpdateMs, Distribution DrawMs, Distribution PresentMs, double AllocatedBytesPerFrame,
        int SunShadowUpdates, int LampShadowMapUpdates, long ShadowDrawCalls, PristonRenderStats LastFrame,
        Dictionary<string, double> CpuSectionsMs, Dictionary<string, double> GpuSectionsMs, int GpuResultsMissed);
    private sealed record PreparedActor(PristonActor Actor, Matrix World, MotionCode Motion);
    private sealed record PreparedScene(PristonStage Stage, PristonModel[] Models, Vector3 Focus, PreparedActor[] Actors);

    private const int SampleCapacity = 360_000;
    private readonly FrameSample[] _samples = new FrameSample[SampleCapacity];
    private readonly double[] _scratch = new double[SampleCapacity];
    private readonly List<RunResult> _results = new();
    private readonly string _assetRoot;
    private readonly string _output;
    private readonly FieldDef _field;
    private readonly double _duration;
    private readonly int _repeat;
    private readonly int _hour;
    private readonly bool _orbit;
    private readonly float _cameraDistance;
    private readonly float _cameraPitch;
    private readonly float _focusX;
    private readonly float _focusZ;
    private readonly float _cameraYaw;
    private readonly PresentationSettings _settings;
    private readonly string _settingsPath;
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private SpriteBatch _spriteBatch = null!;
    private HudAtlas _uiAtlas = null!;
    private GameplayHud _hud = null!;
    private OwnerState _owner = null!;
    private Task<PreparedScene> _preparation = null!;
    private IEnumerator<RenderStageHandle>? _upload;
    private RenderStageHandle? _stage;
    private readonly Stopwatch _loadingTimeout = Stopwatch.StartNew();
    private long _runStart;
    private long _lastFrame;
    private long _updateStart;
    private long _updateBytes;
    private long _frameBytes;
    private double _updateMs;
    private double _drawMs;
    private int _count;
    private bool _measuring;
    private bool _capturePending;
    private string _gpu = string.Empty;
    private bool _gpuTimers;
    private Vector3 _focus;
    private long _cameraStart;
    private bool _actorsPublished;
    private int _renderActorCount;
    private const int LogicalEntityCount = 25;
    // Extra walking actors spread out to the edge of the view, for the crowd level of detail.
    private readonly int _crowd;
    private const float CrowdInnerRadius = 150f;
    private const float CrowdOuterRadius = 1000f;
    private static readonly float GoldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

    public RenderBenchmarkProbe(string root, string content, string output, string fieldId, string settingsPath,
        double duration = 60,
        int repeat = 5,
        int hour = 12,
        bool orbit = false,
        float cameraDistance = 0f,
        float cameraPitch = float.NaN,
        float focusX = float.NaN,
        float focusZ = float.NaN,
        float cameraYaw = 0f,
        int crowd = 0)
    {
        if (duration is < 1 or > 600 || repeat is < 1 or > 10 || hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration 1..600s, repeat 1..10, hour 0..23 required.");
        }

        _assetRoot = root;
        _crowd = crowd;
        _output = Path.GetFullPath(output);
        _field = new WorldCatalog().AllFields.Single(field => field.Id == fieldId);
        if (fieldId is not "fore-1" and not "village-2")
        {
            throw new ArgumentException("The benchmark scenes are fore-1 and village-2.", nameof(fieldId));
        }
        _duration = duration;
        _repeat = repeat;
        _hour = hour;
        _orbit = orbit;
        _cameraDistance = cameraDistance == 0f ? _field.Camera.Distance : cameraDistance;
        if (!float.IsFinite(_cameraDistance)
            || _cameraDistance < _field.Camera.MinDistance
            || _cameraDistance > _field.Camera.MaxDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cameraDistance),
                $"Camera distance must be 0 (field default) or {_field.Camera.MinDistance}..{_field.Camera.MaxDistance}.");
        }

        _cameraPitch = float.IsNaN(cameraPitch) ? _field.Camera.Pitch : cameraPitch;
        if (!float.IsFinite(_cameraPitch) || _cameraPitch is < -0.05f or > 1.05f)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraPitch), "Camera pitch must be -0.05..1.05 radians.");
        }

        if (float.IsNaN(focusX) != float.IsNaN(focusZ)
            || (!float.IsNaN(focusX) && (!float.IsFinite(focusX) || !float.IsFinite(focusZ))))
        {
            throw new ArgumentException("Camera focus X and Z must both be omitted or finite.");
        }

        _focusX = float.IsNaN(focusX) ? _field.Center.X : focusX;
        _focusZ = float.IsNaN(focusZ) ? _field.Center.Z : focusZ;
        if (!float.IsFinite(cameraYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(cameraYaw), "Camera yaw must be finite radians.");
        }

        _cameraYaw = cameraYaw;
        _settingsPath = settingsPath;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(settingsPath), options)
            ?? throw new InvalidDataException("Benchmark settings are null.");
        if (settings.Version != ClientSettings.CurrentVersion || settings != settings.Validate(PristonTale.Engine3D.Diagnostics.NullClientLog.Instance))
        {
            throw new InvalidDataException("Benchmark settings must be valid; silent defaults would invalidate comparisons.");
        }

        if (settings.MaxFps != 0 || settings.WindowMode != WindowMode.Windowed || settings.Monitor != -1 || settings.RefreshRate != 0)
        {
            throw new InvalidDataException("Render benchmark requires an uncapped window on the current desktop; use --validate-display for display modes and caps.");
        }
        _settings = new PresentationSettings { Requested = settings };
        _settings.ApplyRendering(settings);
        var graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = settings.WindowWidth,
            PreferredBackBufferHeight = settings.WindowHeight,
            PreferMultiSampling = settings.MultiSampleCount > 0,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = settings.VerticalSync
        };
        graphics.PreparingDeviceSettings += (_, args) =>
            args.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = settings.MultiSampleCount;
        Content = new AbsoluteContentManager(Services, content);
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
    }

    protected override void LoadContent()
    {
        _gpu = GraphicsDevice.Adapter.DeviceName;
        _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _assetRoot);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _settings)
        {
            Sky = _sky,
            DayLengthSeconds = 0
        };
        _gpuTimers = _renderer.Profiler.EnableGpuTimers(GraphicsDevice);
        _renderer.SetTimeOfDay(_hour);
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(_field.Backdrop.Day),
            PristonSkyProfiles.Resolve(_field.Backdrop.Glow), PristonSkyProfiles.Resolve(_field.Backdrop.Night));
        var lighting = _field.Lighting;
        _renderer.ConfigureFieldLighting(lighting.LampIntensity, lighting.LampRangeScale, null, lighting.GlowIntensity, lighting.GlowScale);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings { FieldOfViewDegrees = _field.Camera.FieldOfViewDegrees });
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _uiAtlas = HudAtlas.LoadBootstrap(
            GraphicsDevice,
            _assetRoot,
            PristonTale.Engine3D.Diagnostics.ConsoleClientLog.Instance);
        var font = Content.Load<SpriteFont>("Fonts/UI");
        var context = new ScreenContext(
            GraphicsDevice,
            Content,
            _spriteBatch,
            font,
            _assetRoot,
            null!,
            PristonTale.Engine3D.Diagnostics.ConsoleClientLog.Instance,
            _ => { },
            _settings,
            _uiAtlas);
        _hud = new GameplayHud(
            context,
            _ => Task.FromResult(new InventoryOutcome(false, 0, GameText.Of(GameTextId.RequestRefused))));
        _hud.CharacterName = "Benchmark";
        _hud.CharacterClass = PlayerClass.Mechanician;
        var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Benchmark");
        _owner = OwnerState.Capture(player);
        _hud.Combat.SetCatalog(new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new MonsterRenderInfo(1, "char/monster/hopy/hopy.inx", 1f, "Hopy", 1)
        }));
        _hud.Combat.Observe(new PresenceUpdate(
            1,
            [new EntitySpawnInfo(2, EntityKind.Monster, false, default, 0, MotionState.Attack, 35, 50, null, 1)],
            [],
            [],
            []));
        _hud.TargetId = 2;
        _preparation = Task.Run(async () =>
        {
            var reader = new PristonSmdReader();
            var stage = reader.ReadStage(Resolve(_field.StageSmd));
            var models = new PristonModel[_field.StageObjectSmds.Count];
            for (var index = 0; index < models.Length; index++)
            {
                models[index] = reader.ReadModel(Resolve(_field.StageObjectSmds[index]));
            }

            await _renderer.PrepareStageTexturesAsync(stage, models);
            var focus = new Vector3(_focusX, 0, -_focusZ);
            if (!new PristonCollisionWorld(stage).TryGetFloorHeight(focus, out var height))
            {
                throw new InvalidDataException("The benchmark camera target has no floor.");
            }

            focus.Y = height + _field.Camera.TargetHeight;
            var actors = await PrepareActorsAsync(stage, focus);
            return new PreparedScene(stage, models, focus, actors);
        });
    }

    protected override void Update(GameTime gameTime)
    {
        _hud.Update(_owner, default(KeyboardState), default(MouseState));
        if (_stage is null)
        {
            PublishStage();
            return;
        }

        if (!_actorsPublished || !_hud.IsReady || !_hud.IconsIdle)
        {
            return;
        }

        _updateStart = Stopwatch.GetTimestamp();
        _updateBytes = GC.GetAllocatedBytesForCurrentThread();
        var cameraElapsed = Stopwatch.GetElapsedTime(_cameraStart).TotalSeconds;
        var angle = _cameraYaw + (_orbit ? (float)(cameraElapsed * Math.Tau / 30) : 0f);
        var radius = _cameraDistance * MathF.Cos(_cameraPitch);
        var elevation = _cameraDistance * MathF.Sin(_cameraPitch);
        var offset = new Vector3(MathF.Sin(angle) * radius, elevation, -MathF.Cos(angle) * radius);
        _camera.SetScriptedView(GraphicsDevice, _focus + offset, _focus);
        _renderer.Update(gameTime);
        _updateMs = Stopwatch.GetElapsedTime(_updateStart).TotalMilliseconds;
        _frameBytes = GC.GetAllocatedBytesForCurrentThread() - _updateBytes;
    }

    private void PublishStage()
    {
        if (_loadingTimeout.Elapsed > TimeSpan.FromMinutes(3))
        {
            throw new TimeoutException("Benchmark scene did not finish loading.");
        }

        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Benchmark preparation failed.", _preparation.Exception);
        }

        var prepared = _preparation.Result;
        _upload ??= _renderer.AddStageIncrementally(prepared.Stage, prepared.Models);
        var started = Stopwatch.GetTimestamp();
        do
        {
            if (!_upload.MoveNext())
            {
                throw new InvalidOperationException("Stage upload ended without publishing a stage.");
            }

            if (_upload.Current is not null)
            {
                _stage = _upload.Current;
                _focus = prepared.Focus;
                foreach (var actor in prepared.Actors)
                {
                    _renderer.AddActor(new PristonActorInstance(actor.Actor, actor.World, actor.Motion));
                }

                _renderActorCount = prepared.Actors.Length;
                _actorsPublished = true;
                _upload.Dispose();
                _upload = null;
                _runStart = Stopwatch.GetTimestamp();
                _cameraStart = _runStart;
                _capturePending = true;
                return;
            }
        }
        while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 3);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_stage is null || _updateStart == 0)
        {
            GraphicsDevice.Clear(_renderer.SkyColor);
            return;
        }

        // No clear before the scene: the composed scene covers the back buffer, and the GPU profiler can reset its
        // timestamps only while no render pass is open.

        if (_capturePending && Stopwatch.GetElapsedTime(_runStart).TotalSeconds >= 1)
        {
            CaptureScene();
            _capturePending = false;
            _runStart = Stopwatch.GetTimestamp();
            _cameraStart = _runStart;
        }

        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        DrawFrame();
        _drawMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _frameBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
    }

    private void DrawFrame()
    {
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        _renderer.Profiler.Begin(RenderSection.Hud);
        _hud.Draw();
        _renderer.Profiler.End(RenderSection.Hud);
    }

    // The frame, scene and HUD, drawn once more into an 8-bit target and read back (reading the presented back buffer
    // is not reliable on the Vulkan runtime); the timed frames are unaffected.
    private void CaptureScene()
    {
        var parameters = GraphicsDevice.PresentationParameters;
        using var target = new RenderTarget2D(GraphicsDevice, parameters.BackBufferWidth, parameters.BackBufferHeight, false,
            SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        DrawFrame();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[target.Width * target.Height];
        target.GetData(pixels);
        using var opaque = new Texture2D(GraphicsDevice, target.Width, target.Height);
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index].A = 255;
        }

        opaque.SetData(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        using var stream = File.Create(_output + ".png");
        opaque.SaveAsPng(stream, opaque.Width, opaque.Height);
    }

    protected override void EndDraw()
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        base.EndDraw();
        var now = Stopwatch.GetTimestamp();
        if (_stage is null || _updateStart == 0)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_runStart, now).TotalSeconds;
        if (!_measuring)
        {
            if (elapsed >= 5)
            {
                _runStart = now;
                _lastFrame = now;
                _measuring = true;
                _renderer.Profiler.ResetAverages();
            }

            return;
        }

        if (_count == SampleCapacity)
        {
            throw new InvalidOperationException("Benchmark sample capacity exhausted; shorten the requested duration.");
        }

        _frameBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        var stats = _renderer.LastFrameStats;
        _samples[_count++] = new FrameSample(
            Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds,
            _updateMs,
            _drawMs,
            Stopwatch.GetElapsedTime(started, now).TotalMilliseconds,
            _frameBytes,
            stats.SunShadowUpdated,
            stats.LampShadowMapsUpdated,
            stats.ShadowDrawCalls);
        _lastFrame = now;
        if (elapsed >= _duration)
        {
            FinishRun(elapsed);
        }
    }

    private void FinishRun(double elapsed)
    {
        var bytes = 0L;
        var sunShadowUpdates = 0;
        var lampShadowMapUpdates = 0;
        var shadowDrawCalls = 0L;
        for (var index = 0; index < _count; index++)
        {
            bytes += _samples[index].AllocatedBytes;
            sunShadowUpdates += _samples[index].SunShadowUpdated ? 1 : 0;
            lampShadowMapUpdates += _samples[index].LampShadowMapsUpdated;
            shadowDrawCalls += _samples[index].ShadowDrawCalls;
        }

        var result = new RunResult(_results.Count + 1, _count, elapsed, _count / elapsed,
            Summarize(static frame => frame.IntervalMs), Summarize(static frame => frame.UpdateMs),
            Summarize(static frame => frame.DrawMs), Summarize(static frame => frame.PresentMs), (double)bytes / _count,
            sunShadowUpdates, lampShadowMapUpdates, shadowDrawCalls, _renderer.LastFrameStats,
            _renderer.Profiler.CpuMeans(), _renderer.Profiler.GpuMeans(), _renderer.Profiler.GpuResultsMissed);
        _results.Add(result);
        Console.WriteLine($"RENDER {_field.Id} run={result.Run} FPS={result.Fps:F2}; frame p95/p99={result.IntervalMs.P95:F2}/{result.IntervalMs.P99:F2}ms; GPU={_gpu}");
        Console.WriteLine($"RENDER gpu frame={_renderer.Profiler.GpuMean(RenderSection.Frame):F3}ms cpu frame={_renderer.Profiler.CpuMean(RenderSection.Frame):F3}ms");
        _count = 0;
        _measuring = false;
        _runStart = Stopwatch.GetTimestamp();
        _cameraStart = _runStart;
        if (_results.Count == _repeat)
        {
            Exit();
        }
    }

    private Distribution Summarize(Func<FrameSample, double> select)
    {
        var total = 0d;
        for (var index = 0; index < _count; index++)
        {
            var value = select(_samples[index]);
            total += value;
            _scratch[index] = value;
        }

        _scratch.AsSpan(0, _count).Sort();
        return new Distribution(total / _count, _scratch[(int)Math.Ceiling(_count * 0.95) - 1],
            _scratch[(int)Math.Ceiling(_count * 0.99) - 1], _scratch[_count - 1]);
    }

    public void WriteReport()
    {
        Assert.True(_results.Count == _repeat, "benchmark did not complete all runs");
        var report = new
        {
            Field = _field.Id,
            Hour = _hour,
            Orbit = _orbit,
            CameraDistance = _cameraDistance,
            CameraPitch = _cameraPitch,
            FocusX = _focusX,
            FocusZ = _focusZ,
            CameraYaw = _cameraYaw,
            Entities = LogicalEntityCount,
            RenderActors = _renderActorCount,
            Hud = true,
            AnimatedMotions = new[] { "Stand", "Walk", "Run", "Attack" },
            Backend = "MonoGame Native (Vulkan), HDR forward",
            EffectContentRoot = Content.RootDirectory,
            Gpu = _gpu,
            CpuArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            OperatingSystem = RuntimeInformation.OSDescription,
            SettingsFile = _settingsPath,
            Settings = _settings.Requested,
            EffectiveWidth = GraphicsDevice.PresentationParameters.BackBufferWidth,
            EffectiveHeight = GraphicsDevice.PresentationParameters.BackBufferHeight,
            EffectiveMultiSampleCount = _renderer.EffectiveMultiSampleCount,
            WarmupSecondsPerRun = 5,
            GpuTimestamps = _gpuTimers,
            StreamingUploadSteps = _renderer.LastStreamingStats.ToDictionary(),
            SkyCaptureRefreshes = _renderer.SkyCaptureRefreshes,
            Runs = _results
        };
        File.WriteAllText(_output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    protected override void UnloadContent()
    {
        _upload?.Dispose();
        _hud.Dispose();
        _uiAtlas.Dispose();
        _spriteBatch.Dispose();
        _renderer.Dispose();
        _sky.Dispose();
    }

    private async Task<PreparedActor[]> PrepareActorsAsync(PristonStage stage, Vector3 focus)
    {
        var loader = new PristonActorLoader();
        var templates = new List<(PristonActor Actor, bool Monster)>();
        foreach (var path in new[]
        {
            "char/npc/TN-003/TN-003.inx",
            "char/npc/TN-004/TN-004.inx",
            "char/npc/TN-007/TN-007.inx",
            "char/npc/TN-010/TN-010.inx",
            "char/npc/TN-001/TN-001.inx",
            "char/npc/TN-005/TN-005.inx"
        })
        {
            templates.Add((await LoadActorAsync(loader, path), false));
        }

        foreach (var path in new[]
        {
            "char/monster/hopy/hopy.inx",
            "char/monster/hopy/mopy.inx",
            "char/monster/Morgon/Morgon.inx"
        })
        {
            templates.Add((await LoadActorAsync(loader, path), true));
        }

        var playerClass = new CharacterClassRegistry().Get(PlayerClass.Mechanician).Definition;
        var body = await LoadActorAsync(loader, playerClass.DefaultVisuals.Body.InxExtensionPath);
        var head = await LoadActorAsync(loader, CharacterFace.ResolveHeadPath(playerClass.DefaultVisuals, 0));
        var collision = new PristonCollisionWorld(stage);
        var result = new List<PreparedActor>(26);
        var playerWorld = ActorWorld(collision, focus, Vector2.Zero, 0f, 1f);
        result.Add(new PreparedActor(body, playerWorld, MotionCode.Run));
        result.Add(new PreparedActor(head, playerWorld, MotionCode.Run));

        for (var entity = 0; entity < 24; entity++)
        {
            var template = templates[entity % templates.Count];
            var ring = entity / 8;
            var angle = entity % 8 * MathHelper.TwoPi / 8f + ring * 0.2f;
            var radius = 130f + ring * 115f;
            var offset = new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * radius;
            var motion = template.Monster
                ? (entity % 3) switch
                {
                    0 => MotionCode.Walk,
                    1 => MotionCode.Run,
                    _ => MotionCode.Attack
                }
                : MotionCode.Stand;
            var scale = template.Monster && entity % templates.Count == templates.Count - 1 ? 0.75f : 1f;
            result.Add(new PreparedActor(template.Actor, ActorWorld(collision, focus, offset, angle + MathHelper.Pi, scale), motion));
        }

        for (var extra = 0; extra < _crowd; extra++)
        {
            var template = templates[extra % templates.Count];
            var angle = extra * GoldenAngle;
            var radius = CrowdInnerRadius + (CrowdOuterRadius - CrowdInnerRadius) * MathF.Sqrt((extra + 0.5f) / _crowd);
            var offset = new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * radius;
            result.Add(new PreparedActor(template.Actor, ActorWorld(collision, focus, offset, angle, 1f), extra % 2 == 0 ? MotionCode.Walk : MotionCode.Run));
        }

        return result.ToArray();
    }

    private async Task<PristonActor> LoadActorAsync(PristonActorLoader loader, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var actor = loader.Load(_assetRoot, Path.Combine(_assetRoot, normalized));
        await _renderer.PrepareActorResourcesAsync(actor);
        return actor;
    }

    private static Matrix ActorWorld(PristonCollisionWorld collision, Vector3 focus, Vector2 offset, float facing, float scale)
    {
        var position = new Vector3(focus.X + offset.X, 0f, focus.Z + offset.Y);
        if (collision.TryGetFloorHeight(position, out var height))
        {
            position.Y = height;
        }

        return Matrix.CreateScale(scale) * Matrix.CreateRotationY(facing) * Matrix.CreateTranslation(position);
    }

    private string Resolve(string relative) => AssetFiles.Resolve(Path.Combine(_assetRoot, relative));
}
