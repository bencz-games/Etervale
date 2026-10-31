using System.Threading.Channels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Engine3D;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.World;

// Fields in the world: loading and streaming them around the player, the active field and its lighting and camera.
public sealed partial class GameScene
{
    public void LoadEntered(string assetRoot, EnteredCharacter entered)
    {
        if (!_worldCatalog.TryGetFieldByCode(entered.FieldCode, out var field))
            field = _worldCatalog.Ricarten;

        LoadField(assetRoot, field, entered);
    }

    public void LoadField(string assetRoot, FieldDef field, EnteredCharacter entered = null)
    {
        _assetRoot = assetRoot;
        EnsureRenderer();

        _fieldStreamer?.Dispose();
        _fieldStreamer = new FieldResourceStreamer(assetRoot, _reader, _renderer, _log, ConfigureFieldLighting);
        UnloadAllFields();

        var loaded = AddFieldToWorld(assetRoot, field);
        SetActiveField(loaded);

        _player = entered != null
            ? _classRegistry.CreatePlayer(entered.Class, entered.Name, entered.Level)
            : _classRegistry.CreatePlayer(PlayerClass.Mechanician, "Player");
        var d = _player.Derived;
        _log.Info(
            $"[Player] {_player.Name} ({_player.Class.Name}) Lv{_player.Level}  " +
            $"HP {_player.Life}/{d.MaxLife}  MP {_player.Mana}/{d.MaxMana}  SP {_player.Stamina}/{d.MaxStamina}  " +
            $"Dmg {d.AttackDamageMin}-{d.AttackDamageMax}  AR {d.AttackRating}  Def {d.Defence}  " +
            $"Abs {d.Absorption}  Wt {d.MaxWeight}  Spd {d.MoveSpeed}");
        _playerController = new ActorMotionController();
        _runMode = false;
        _selfLife = _player.Life;
        _selfMaxLife = d.MaxLife;
        var spawn = entered != null && (entered.SpawnX != 0f || entered.SpawnZ != 0f)
            ? new Vector2(entered.SpawnX, -entered.SpawnZ)
            : SpawnPlanarPosition(field);
        _playerBody = new PhysicsBody(spawn) { Radius = PlayerRadius };
        UpdateBodyHeight(_playerBody);
        _playerForward = Vector3.Forward;

        _renderer.ClearActors();
        AddPlayerActors(assetRoot, entered?.FaceIndex ?? 0, entered?.Tier ?? 0, entered?.HairIndex ?? 0);

        _camera = new PristonCamera(_playerBody.Position, _playerForward, ToCameraSettings(field.Camera));
        Status = $"Loaded {field.Name}: {loaded.Stage.Vertices.Count} vertices, {loaded.Stage.Faces.Count} faces, " +
                 $"{field.StageObjectSmds.Count} animation SMDs + {_player.Name}";
        ScanStreaming();
    }

    private void EnsureRenderer()
    {
        if (_renderer != null)
            return;
        _renderer = new PristonRenderer(_graphicsDevice, _content, _log, _presentation) { Sky = _sky };
        _actorScene = new RendererActorScene(_renderer);
        CreateSkillPresentation();
        CreateItemEffects();
        CreateRain();
        var actorScene = _actorScene;
        _playerComposer = new PlayerActorComposer(actorScene, _actorLoader);
        _monsterComposer = new ModelActorComposer(actorScene, _actorLoader);
        _groundLoot = new GroundLootPresentation(actorScene, _actorLoader, _assetRoot, _log);
        _lootInteraction = new GroundLootInteraction(ApplyInventoryAsync, _log, drop => _fieldAudio?.PlayItem(drop.CatalogKey, drop.Gold),
            drop => PickupToCursor?.Invoke(drop.ItemCode) == true, (ok, revision) => PickupToCursorAnswered?.Invoke(ok, revision));
        _lootFloor = LootFloorAt;
    }

    private void RequestField(FieldDef field)
    {
        if (!IsLoaded(field.Id))
        {
            _fieldStreamer.Request(field);
        }
    }

    private LoadedField AddFieldToWorld(string assetRoot, FieldDef field)
    {
        var existing = _fields.FirstOrDefault(f => f.Definition.Id == field.Id);
        if (existing != null)
            return existing;

        var loaded = LoadFieldResources(assetRoot, field);
        _fields.Add(loaded);
        InvalidateGround();
        return loaded;
    }

    private LoadedField LoadFieldResources(string assetRoot, FieldDef field)
    {
        var stage = _reader.ReadStage(AssetFiles.Combine(assetRoot, field.StageSmd));
        var animationModels = field.StageObjectSmds
            .Select(path => AssetFiles.Combine(assetRoot, path))
            .Select(_reader.ReadModel)
            .ToArray();
        var collision = new PristonCollisionWorld(stage);
        ConfigureFieldLighting(field.Lighting);
        var handle = _renderer.AddStage(stage, animationModels);
        return new LoadedField(field, stage, collision, handle);
    }

    private void UnloadAllFields()
    {
        foreach (var field in _fields)
            _renderer.RemoveStage(field.RenderHandle);
        _fields.Clear();
        InvalidateGround();
        _activeField = null;
    }

    private void UpdateStreaming()
    {
        if (_activeField == null || _assetRoot == null || _playerBody == null)
        {
            return;
        }

        UpdateActiveField();

        if (++_streamScanCounter < FieldStreamingPolicy.ScanIntervalFrames)
        {
            return;
        }

        _streamScanCounter = 0;
        ScanStreaming();
    }

    private void ScanStreaming()
    {
        var active = _activeField.Definition;
        var player = _playerBody.PlanarPosition;
        Vector2? movementTarget = _hasMoveTarget ? _moveTarget : null;
        var movementSpeed = _hasMoveTarget ? MovementSpeed(_runMode) : 0f;
        var disconnect = FieldStreamingPolicy.RetentionDistance(movementSpeed);
        foreach (var neighbour in _worldCatalog.Neighbours(active))
        {
            if (IsLoaded(neighbour.Id))
            {
                continue;
            }

            if (TryGateSeam(active, neighbour, out var seam)
                && FieldStreamingPolicy.ShouldPrefetch(player, movementTarget, seam, movementSpeed))
            {
                RequestField(neighbour);
            }
        }

        foreach (var warp in active.WarpGates)
        {
            var point = WorldSpace.Planar(warp.X, warp.Z);
            if (!WingGates.IsStone(warp)
                && FieldStreamingPolicy.ShouldPrefetch(player, movementTarget, point, movementSpeed)
                && _worldCatalog.TryGetField(warp.DestinationFieldId, out var destination))
            {
                RequestField(destination);
            }
        }

        for (var i = _fields.Count - 1; i >= 0; i--)
        {
            var field = _fields[i];
            if (field == _activeField)
            {
                continue;
            }

            var keep = TryGateSeam(active, field.Definition, out var seam) && Vector2.DistanceSquared(player, seam) < disconnect * disconnect;
            foreach (var warp in active.WarpGates)
            {
                if (warp.DestinationFieldId == field.Definition.Id
                    && Vector2.DistanceSquared(player, WorldSpace.Planar(warp.X, warp.Z)) < disconnect * disconnect)
                {
                    keep = true;
                }
            }

            if (!keep)
            {
                _log.Info($"[Stream] unload {field.Definition.Id} (walked away from {active.Id}).");
                UnloadField(field);
            }
        }
    }

    private void UpdateActiveField()
    {
        var position = _playerBody.Position;
        LoadedField best = null;
        var bestHeight = float.MinValue;
        foreach (var field in _fields)
        {
            if (field.Collision.TryGetFloorHeight(position, position.Y + GroundTraversal.VisualOffset, GroundTraversal.StepUp, out var height)
                && height > bestHeight)
            {
                bestHeight = height;
                best = field;
            }
        }

        if (best != null)
        {
            SetActiveField(best);
        }
    }

    private void SetActiveField(LoadedField field)
    {
        if (_activeField == field)
            return;
        _activeField = field;
        _currentField = field.Definition;
        SnapWeatherToField();
        _renderer.Underground = field.Definition.Underground;
        // The ice fields show the aurora at night; the forests a mist in the early morning.
        _renderer.Aurora = field.Definition.State == FieldState.Ice;
        _renderer.MorningMist = field.Definition.State == FieldState.Forest;
        // A field may clear part of the haze; sight cells (64 units each) push its start out.
        _renderer.FieldHaze = 1f - Math.Clamp(field.Definition.HazeReduction, 0f, 1f);
        _renderer.FieldSightDistance = field.Definition.SightCells * 64f;
        if (!field.Definition.Underground)
        {
            var backdrop = field.Definition.Backdrop;
            _renderer.SkyProfiles = new PristonSkyProfiles(
                PristonSkyProfiles.Resolve(backdrop.Day),
                PristonSkyProfiles.Resolve(backdrop.Glow),
                PristonSkyProfiles.Resolve(backdrop.Night));
        }

        var ambient = field.Definition.Lighting.Ambient;
        _renderer.UndergroundAmbient = new Vector3(ambient.R, ambient.G, ambient.B);
        _renderer.ConfigureDayNight(ToDayNightSettings(field.Definition.DayNight));
        _fieldAudio?.SelectField(field.Definition);
        field.Definition.OnLoaded?.Invoke(this);
    }

    private bool IsLoaded(string fieldId) => _fields.Any(f => f.Definition.Id == fieldId);

    private void UnloadField(LoadedField field)
    {
        _renderer.RemoveStage(field.RenderHandle);
        _fields.Remove(field);
        InvalidateGround();
    }

    private void ConfigureFieldLighting(FieldLighting lighting)
    {
        if (_renderer == null)
            return;

        PristonPointLight[] overrides = null;
        if (lighting.Lights.Count > 0)
        {
            overrides = new PristonPointLight[lighting.Lights.Count];
            for (var i = 0; i < lighting.Lights.Count; i++)
            {
                var l = lighting.Lights[i];
                overrides[i] = new PristonPointLight(
                    new Vector3(l.X, l.Y, l.Z),
                    l.Range,
                    new Vector3(l.R / 255f, l.G / 255f, l.B / 255f),
                    l.Intensity);
            }
        }

        _renderer.ConfigureFieldLighting(lighting.LampIntensity, lighting.LampRangeScale, overrides, lighting.GlowIntensity, lighting.GlowScale);
    }

    private static PristonCameraSettings ToCameraSettings(FieldCamera c) => new()
    {
        DefaultDistance = c.Distance,
        MinDistance = c.MinDistance,
        MaxDistance = c.MaxDistance,
        DefaultPitch = c.Pitch,
        FieldOfViewDegrees = c.FieldOfViewDegrees,
        TargetHeight = c.TargetHeight,
    };

    private static DayNightSettings ToDayNightSettings(FieldDayNight d)
    {
        if (d.Gradient.Count == 0)
            return new DayNightSettings { DayLengthSeconds = d.DayLengthSeconds, StartTimeOfDay = d.StartTimeOfDay };

        var keys = new DayNightKey[d.Gradient.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var k = d.Gradient[i];
            keys[i] = new DayNightKey(k.Hour,
                new Vector3(k.Sun.R, k.Sun.G, k.Sun.B),
                new Vector3(k.Ambient.R, k.Ambient.G, k.Ambient.B),
                new Vector3(k.Sky.R, k.Sky.G, k.Sky.B));
        }
        return new DayNightSettings { Gradient = keys, DayLengthSeconds = d.DayLengthSeconds, StartTimeOfDay = d.StartTimeOfDay };
    }

    private static Vector2 SpawnPlanarPosition(FieldDef field)
    {
        var point = field.StartPoints.Count > 0 ? field.StartPoints[0] : field.Center;
        return WorldSpace.Planar(point.X, point.Z);
    }
}
