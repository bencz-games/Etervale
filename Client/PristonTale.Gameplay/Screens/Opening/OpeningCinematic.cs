using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using PristonTale.Assets;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class OpeningCinematic : IDisposable
{
    private const string StagePath = "StartImage/Opening/Map/stage.smd";
    private const string CameraPathFile = "StartImage/Opening/opening_camera.cam";

    private static readonly (string Path, Matrix World)[] SceneryModels =
    [
        ("StartImage/Opening/Map/st-ani02.smd", Matrix.Identity),
        ("StartImage/Opening/Map/st-ani03.smd", Matrix.Identity),
    ];

    private static readonly (string Mesh, string Skeleton)[] OpeningNpcs =
    [
        ("StartImage/Opening/npc/opening_NPC_A.smd", "StartImage/Opening/npc/opening_NPC_A.smb"),
        ("StartImage/Opening/npc/opening_NPC_B.smd", "StartImage/Opening/npc/opening_NPC_B.smb"),
        ("StartImage/Opening/npc/opening_NPC_C.smd", "StartImage/Opening/npc/opening_NPC_C.smb"),
        ("StartImage/Opening/npc/opening_NPC_D.smd", "StartImage/Opening/npc/opening_NPC_D.smb"),
        ("StartImage/Opening/npc/opening_NPC_E.smd", "StartImage/Opening/npc/opening_NPC_E.smb"),
        ("StartImage/Opening/npc/opening_NPC_F.smd", "StartImage/Opening/npc/opening_NPC_F.smb"),
        ("StartImage/Opening/babel/st-ani04.smd", "StartImage/Opening/babel/st-ani04.smb"),
    ];

    private const float AutoCameraTicksPerStep = 50f;
    private const float CinemaEndTicks = 166000f;
    private const int WarpFrameStart = 472;
    private const int WarpFrameEnd = 473;

    private static readonly PristonCameraSettings OpeningCameraSettings = new()
    {
        FieldOfViewDegrees = 180f / 4.4f,
        NearPlane = 20f,
        FarPlane = 4000f,
    };

    private readonly GraphicsDevice _graphicsDevice;
    private readonly IClientLog _log;
    private readonly Func<string, string> _assetPath;

    private readonly PristonAssetCache _assetCache = new();
    private readonly PristonSmdReader _reader;
    private readonly PristonActorLoader _actorLoader;

    private readonly PristonRenderer _renderer;
    private readonly EffectLibrary _effectLibrary;
    private readonly VfxRenderer _effects;
    private VfxSystem _effectSystem;
    private readonly Random _particleRandom = new(0x5054);
    private MeteorEffects _terrainFire;
    private PristonCamera _camera;
    private PristonCameraPath _cameraPath;
    private float _pathTicks;
    private float _stepAccumulator;
    private float _orbitAngle;
    private Vector3 _fallbackTarget;
    private int _particleSpawnTicks;

    private int _vertexCount;
    private int _faceCount;
    private readonly string _assetRoot;

    public OpeningCinematic(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log,
        Func<string, string> assetPath, string assetRoot, PristonSkyLibrary sky = null, PresentationSettings presentation = null, EffectLibrary effects = null)
    {
        _graphicsDevice = graphicsDevice;
        _log = log;
        _assetPath = assetPath;
        _assetRoot = assetRoot;
        _reader = new PristonSmdReader(_assetCache);
        _actorLoader = new PristonActorLoader(_assetCache);
        _effectLibrary = effects;
        if (effects is not null)
        {
            _effects = new VfxRenderer(graphicsDevice, content);
        }
        _renderer = new PristonRenderer(graphicsDevice, content, log, presentation)
        {
            Sky = sky,
            SkyProfiles = PristonSkyProfiles.Opening
        };
    }

    public bool IsOver => _pathTicks >= CinemaEndTicks;

    public string StatusSuffix =>
        $"{_vertexCount} vertices, {_faceCount} faces" +
        (_cameraPath is { HasPath: true } ? " (flythrough)" : " (static)");

    public void Load()
    {
        var stage = _reader.ReadStage(_assetPath(StagePath));
        // The original opening stage references Ricarten's texture names, while the
        // SMD itself is in StartImage/Opening/Map. Prefer an opening-local texture;
        // only this scene's map materials may fall back to Field/Ricarten.
        // NPC meshes have independent sources; do not give them the stage fallback.
        _renderer.SetTextureFallbackDirectory(Path.GetDirectoryName(stage.Path) ?? ".", _assetPath("Field/Ricarten"));
        // opening_NPC_E.smd references TN-00-h.BMP and TN-006-b.BMP alongside mm.bmp;
        // the original TN-006.smd also references mm.bmp. Only that name may fall back
        // to the existing TN-006 texture, leaving every other opening NPC material alone.
        _renderer.SetTextureFallbackForName(_assetPath("StartImage/Opening/npc"), "mm.bmp", _assetPath("char/npc/TN-006"));
        if (_effects is not null)
        {
            var collision = new PristonCollisionWorld(stage);
            // The converted scripts are registered here, while loading, so the flight never waits for them.
            _effectLibrary.Vfx.CompileAll();
            _effectSystem = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(_renderer.TransientLights), _effectLibrary.Vfx.Library);
            _terrainFire = new MeteorEffects(new SystemScriptEffects(_effectSystem, _effectLibrary.Vfx.Library), _renderer.TransientLights, collision);
        }
        _renderer.ConfigureFieldLighting(intensity: 4f, rangeScale: 1f, overrideLights: null, glowIntensity: 1f, glowScale: 1f);
        _renderer.AddStage(stage, LoadScenery());
        LoadOpeningNpcs();

        _renderer.SetTimeOfDay(11f);
        _renderer.DayLengthSeconds = 0f;

        _fallbackTarget = (stage.Bounds.Min + stage.Bounds.Max) * 0.5f;
        _camera = new PristonCamera(_fallbackTarget, Vector3.Forward, OpeningCameraSettings);

        var camFile = _assetPath(CameraPathFile);
        if (AssetFiles.Exists(camFile))
        {
            _cameraPath = PristonCameraPath.ReadCam(camFile);
            _cameraPath.AddWarpFrame(WarpFrameStart, WarpFrameEnd);
            _renderer.UseExternalAnimationClock = true;
        }

        _vertexCount = stage.Vertices.Count;
        _faceCount = stage.Faces.Count;
    }

    public void Update(GameTime gameTime)
    {
        var seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateCamera(seconds);
        _terrainFire?.Update(seconds);
        _effectSystem?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        _renderer.Update(gameTime);
    }

    private void UpdateCamera(float dt)
    {
        if (_cameraPath is { HasPath: true })
        {
            _stepAccumulator += dt;
            while (_stepAccumulator >= GameUnits.TickSeconds)
            {
                _stepAccumulator -= GameUnits.TickSeconds;
                _pathTicks += AutoCameraTicksPerStep;
                if (_pathTicks < CinemaEndTicks)
                {
                    UpdateParticleSpawn();
                }
            }
            _cameraPath.Sample(_pathTicks, out var position, out var forward, out var up);
            _camera.SetScriptedView(_graphicsDevice, position, position + forward, up);
            _renderer.SetSceneAnimationFrame(_pathTicks);
        }
        else
        {
            _orbitAngle += dt * 0.2f;
            var eye = _fallbackTarget + new Vector3(MathF.Sin(_orbitAngle) * 600f, 250f, MathF.Cos(_orbitAngle) * 600f);
            _camera.SetScriptedView(_graphicsDevice, eye, _fallbackTarget);
        }
    }

    private void UpdateParticleSpawn()
    {
        _particleSpawnTicks++;
        if (_particleSpawnTicks < 70 || _terrainFire is null)
        {
            return;
        }

        _particleSpawnTicks = 0;
        _cameraPath.Sample(_pathTicks, out var cameraPosition, out var forward, out var up);
        SpawnTerrainFire(cameraPosition, forward, up);
    }

    private void SpawnTerrainFire(Vector3 cameraPosition, Vector3 forward, Vector3 up)
    {
        forward = Vector3.Normalize(forward);
        var right = Vector3.Cross(forward, up);
        if (right.LengthSquared() <= 0.000001f)
        {
            return;
        }

        right.Normalize();
        up = Vector3.Normalize(Vector3.Cross(right, forward));

        var type = _particleRandom.Next(100);
        var random1 = _particleRandom.Next(1000);
        var random2 = _particleRandom.Next(1000);
        var random3 = _particleRandom.Next(1000);
        var random4 = _particleRandom.Next(1000);
        var startX = type < 10 ? -random1 : random1;
        var destinationX = type < 10 ? random2 : -random2;
        var startZ = type < 30 ? random1 : random3;
        var destinationZ = type < 30 ? random2 : random4;
        var anchor = cameraPosition + forward * 440f;
        var start = anchor + right * startX + up * (200000f / 256f) + forward * startZ;
        var destination = anchor + right * destinationX - up * (200000f / 256f) + forward * destinationZ;
        _terrainFire.SpawnTrajectory(start, destination);
    }

    public void Draw()
    {
        _graphicsDevice.Clear(_renderer.SkyColor);
        _renderer.Draw(_camera);
        if (_effectSystem is not null)
        {
            _effects.Draw(_effectSystem, _camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        }
        _renderer.ComposeScene();
    }

    private List<PristonSceneryInstance> LoadScenery()
    {
        var instances = new List<PristonSceneryInstance>(SceneryModels.Length);
        foreach (var (relative, world) in SceneryModels)
        {
            var path = _assetPath(relative);
            if (!AssetFiles.Exists(path))
            {
                _log.Warn($"[Opening] scenery missing, skipped: {relative}");
                continue;
            }
            try
            {
                instances.Add(new PristonSceneryInstance(_reader.ReadModel(path), world));
            }
            catch (Exception ex)
            {
                _log.Warn($"[Opening] scenery failed to load, skipped: {relative} ({ex.Message})");
            }
        }
        return instances;
    }

    private void LoadOpeningNpcs()
    {
        foreach (var (mesh, skeleton) in OpeningNpcs)
        {
            var meshPath = _assetPath(mesh);
            var skeletonPath = _assetPath(skeleton);
            if (!AssetFiles.Exists(meshPath) || !AssetFiles.Exists(skeletonPath))
            {
                _log.Warn($"[Opening] NPC missing, skipped: {mesh}");
                continue;
            }
            try
            {
                var actor = _actorLoader.LoadSkinned(meshPath, skeletonPath);
                _renderer.AddActor(new PristonActorInstance(actor, Matrix.Identity, MotionCode.Any));
            }
            catch (Exception ex)
            {
                _log.Warn($"[Opening] NPC failed to load, skipped: {mesh} ({ex.Message})");
            }
        }
    }

    public void Dispose()
    {
        _terrainFire?.Clear();
        _effects?.Dispose();
        _renderer?.Dispose();
    }

    // The flight has no entities to anchor effects to.
}
