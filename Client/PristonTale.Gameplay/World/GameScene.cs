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

public sealed partial class GameScene : IDisposable, IFieldContext, IHudPortraitScene
{
    private ActorHandle _playerBodyActor = ActorHandle.None;
    private ActorHandle _playerHeadActor = ActorHandle.None;

    // The local player's body and head, which move and play as one.
    private PlayerActorHandles PlayerActors => new(_playerBodyActor, _playerHeadActor);
    private FieldDef _currentField;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly ContentManager _content;
    private readonly IClientLog _log;
    private readonly PristonAssetCache _assetCache = new();
    private readonly PristonSmdReader _reader;
    private readonly PristonActorLoader _actorLoader;
    private readonly WorldCatalog _worldCatalog = new();
    private readonly CharacterClassRegistry _classRegistry = new();
    private PlayerCharacter _player;
    private PristonRenderer _renderer;
    private RendererActorScene _actorScene;
    private PlayerActorComposer _playerComposer;
    private GroundLootPresentation _groundLoot;
    private GroundLootInteraction _lootInteraction;
    private FloorHeightQuery _lootFloor;
    private PlayerEquipmentPresentation _equipmentPresentation;
    private ModelActorComposer _monsterComposer;
    private PristonCamera _camera;
    private readonly PristonTale.Gameplay.Effects.EffectLibrary _effectLibrary;
    private readonly FieldAudioController _fieldAudio;
    private readonly NpcAudioController _npcAudio;
    private readonly PristonAudioLibrary _audio;
    // The sparkle of a special warp, ended when the warp moves the player away.
    private PristonTale.Effects.Vfx.VfxHandle _warpSparkle;
    private readonly List<LoadedField> _fields = [];
    private readonly FieldCollisionQuery _fieldCollision;
    private readonly List<EntityNeighbour> _entityNeighbours = [];
    private LoadedField _activeField;
    private string _assetRoot;
    private PhysicsBody _playerBody;
    private ActorMotionController _playerController = new();
    private bool _runMode;
    private int _selfLife;
    private int _selfMaxLife;
    private Vector3 _playerForward = Vector3.Forward;
    private MouseState _lastMouseState;
    private Vector2 _moveTarget;
    private bool _hasMoveTarget;

    private int? _serverMoveSpeed;
    private float _serverAttackRate = 1f;
    private Vector2 _positionCorrection;
    private readonly GroundFloorQuery _groundFloorQuery;
    private int? _snapshotField;

    private float MovementSpeed(bool running)
    {
        var moveSpeed = _serverMoveSpeed ?? _player?.Derived.MoveSpeed ?? 1;
        var inVillage = _activeField?.Definition.State == FieldState.Village;
        if (running && GameMasterDash)
        {
            return Locomotion.GameMasterDashUnitsPerSecond;
        }

        return running ? Locomotion.RunUnitsPerSecond(moveSpeed) : Locomotion.WalkUnitsPerSecond(moveSpeed, inVillage);
    }

    // A game master running with Shift held dashes, as the original's debug mode did; the server allows game masters
    // that speed.
    private bool GameMasterDash => (_playChannel?.GameMasterLevel ?? 0) > 0 && !KeyboardCaptured
        && (Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift));

    public bool RunFlag => _runFlag;

    public void ToggleRun() => _runFlag = !_runFlag;

    // The original's walk/run rule: the setting decides, except that a player tracing a target or holding the button
    // keeps its pace while its stamina is at a quarter or less, and walks once it runs out. Overweight only walks.
    private void UpdateRunMode(MouseState mouse)
    {
        var tracing = _hasMoveTarget || _followCursor || _attackInput.TargetId != 0 || _npcTargetId != 0
            || mouse.LeftButton == ButtonState.Pressed;
        var stamina = Owner?.Stamina ?? 1;
        var maxStamina = Math.Max(1, Owner?.MaxStamina ?? 1);
        var run = _runMode;
        if (!tracing || stamina > maxStamina / 4)
        {
            run = _runFlag;
        }

        run = run && stamina > 0 && !Overweight;
        if (run != _runMode)
        {
            _runMode = run;
            _reports.Request();
        }
    }

    private const float PlayerRadius = 24f;
    private const float BodyProbeHeight = 30f;
    private const float WallFloorThreshold = 0.25f;
    private const double DoubleClickMs = 350.0;
    private double _lastLeftClickMs = double.NegativeInfinity;
    // The walk/run setting (R or the bar's button), running at first as in the original.
    private bool _runFlag = true;
    // After a double click on the ground the player keeps walking toward the cursor until it is blocked or clicks again.
    private bool _followCursor;

    private readonly AttackInputController _attackInput = new();
    private readonly TargetSelection _targetSelection = new();
    private readonly TargetLivenessQuery _targetLiveness;
    private bool _selfAttacking;

    private RemoteEntityManager _remote;
    // Catalog handoff and presence draining keep renderer access on the game thread.
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _worldRenderCatalog =
        PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog.Empty;
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _pendingCatalog;
    private IPlayChannel _playChannel;
    private CancellationTokenSource _presenceCts;
    private Task _presenceReader;
    private readonly Channel<PresenceUpdate> _presenceQueue = Channel.CreateBounded<PresenceUpdate>(
        new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    // The share of a frame presence updates may take (DrainPresence), and how many apply however long they take.
    private static readonly TimeSpan PresenceBudget = TimeSpan.FromMilliseconds(4);
    private const int MinPresencePerFrame = 8;
    // A walk toward a skill target reports its destination again once the target has moved this far from it.
    private const float ApproachReportDistance = 16f;
    private readonly MovementReportPacer _reports = new();

    private readonly PristonSkyLibrary _sky;
    private readonly PresentationSettings _presentation;

    public GameScene(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log = null, PristonSkyLibrary sky = null,
        PristonTale.Gameplay.Effects.EffectLibrary effects = null, PresentationSettings presentation = null, PristonAudioLibrary audio = null)
    {
        _presentation = presentation;
        _sky = sky;
        _effectLibrary = effects;
        if (audio is not null)
        {
            _audio = audio;
            _fieldAudio = new FieldAudioController(audio);
            _npcAudio = new NpcAudioController(audio);
        }
        _groundFloorQuery = TryGetFloorHeightNear;
        _targetLiveness = QueryTargetLiveness;
        _fieldCollision = new FieldCollisionQuery(_fields);
        _graphicsDevice = graphicsDevice;
        _content = content;
        _log = log ?? ConsoleClientLog.Instance;
        _reader = new PristonSmdReader(_assetCache);
        _actorLoader = new PristonActorLoader(_assetCache);
    }

    public string Status { get; private set; } = "Not loaded";

    public PlayerCharacter Player => _player;
    public OwnerState Owner { get; private set; }

    // The server's level once the owner state arrived; _player keeps the level the character entered with.
    private int PlayerLevel => Owner?.Level ?? _player?.Level ?? 0;

    // Over the weight limit the character only walks.
    private bool Overweight => Owner?.Overweight == true;
    public bool PointerCaptured { get; set; }

    // The HUD holds the player in place (GameplayHud.HoldsPlayer).
    public bool MovementLocked { get; set; }

    private bool WorldClicksBlocked => PointerCaptured || MovementLocked;
    public bool CameraInputEnabled { get; set; } = true;
    public bool RightAttackEnabled { get; set; }
    public ulong AttackTargetId => _attackInput.TargetId;
    public ulong SelectedTargetId => _targetSelection.FrameId;
    public ulong HoveredTargetId => _targetSelection.HoveredId;
    public Guid HoveredDropId { get; private set; }
    public ulong TracedTargetId => _targetSelection.TracedId;
    public FieldDef ActiveFieldDefinition => _activeField?.Definition;
    public PristonIntRectangle ActiveMapRectangle => _activeField?.Stage.AuthoredMapRectangle ?? default;
    public PristonRenderer Renderer => _renderer;

    public int CopyActorHandles(ulong entityId, Span<ActorHandle> destination) => _remote?.CopyActorHandles(entityId, destination) ?? 0;

    // The loaded field other than the active one whose map lies nearest the player: the minimap draws it underneath.
    public bool TryGetNeighbourMap(out string fieldId, out PristonIntRectangle rectangle)
    {
        fieldId = null;
        rectangle = default;
        if (_activeField is null || _playerBody is null)
        {
            return false;
        }

        var position = _playerBody.Position;
        var best = float.MaxValue;
        foreach (var field in _fields)
        {
            var map = field.Stage.AuthoredMapRectangle;
            if (field == _activeField || map.Right <= map.Left || map.Bottom <= map.Top)
            {
                continue;
            }

            // Stage map rectangles are fixed point with the original's z, the engine's -z.
            var x = Math.Clamp(position.X, map.Left / 256f, map.Right / 256f) - position.X;
            var z = Math.Clamp(-position.Z, map.Top / 256f, map.Bottom / 256f) + position.Z;
            var distance = x * x + z * z;
            if (distance < best)
            {
                best = distance;
                fieldId = field.Definition.Id;
                rectangle = map;
            }
        }

        return fieldId is not null;
    }
    public Vector3 PlayerPosition => _playerBody?.Position ?? Vector3.Zero;
    public Vector3 PlayerForward => _playerForward;
    private float PlayerYaw => WorldSpace.RenderYaw(_playerForward);
    public bool RunMode => _runMode;
    public event Action<PresenceUpdate> PresenceObserved;

    // What a request answers when the scene has no world connection.
    private const string NoConnection = "world connection unavailable";

    public Task<PristonTale.Simulation.Items.InventoryOutcome> ApplyInventoryAsync(PristonTale.Simulation.Items.InventoryCommand command)
    {
        return _playChannel is not null ? _playChannel.ApplyInventoryAsync(command, _presenceCts.Token)
            : Task.FromResult(new PristonTale.Simulation.Items.InventoryOutcome(false, 0, NoConnection));
    }
    public event Action<ulong> NpcSelected;
    // Asked as a ground item's pickup leaves, with its item code: whether it comes to the inventory's cursor; that
    // pickup's reply (succeeded, revision) is then heard by PickupToCursorAnswered.
    public Func<uint, bool> PickupToCursor { get; set; }
    public event Action<bool, long> PickupToCursorAnswered;
    // A player selling from a personal shop was reached: its shop opens.
    public event Action<ulong> ShopSellerSelected;
    // The NPC, seller or other player a click approaches (_approach says which).
    private ulong _npcTargetId;
    private ApproachKind _approach;
    private bool _npcMoveSent;
    private Vector2 _serverPlanarPosition;
    private float _serverCollisionHeight;
    // Feet height where the current fall began; NaN on the floor.
    private float _fallTop = float.NaN;

    public Task<PristonTale.Simulation.Progression.ProgressionOutcome> ApplyProgressionAsync(PristonTale.Simulation.Progression.ProgressionCommand command)
    {
        return _playChannel is not null ? _playChannel.ApplyProgressionAsync(command, _presenceCts.Token)
            : Task.FromResult(new PristonTale.Simulation.Progression.ProgressionOutcome(false, 0, NoConnection));
    }

    public Task<PristonTale.Simulation.World.Npcs.NpcInteraction> InteractNpcAsync(ulong entityId)
    {
        return _playChannel is not null ? _playChannel.InteractNpcAsync(entityId, _presenceCts.Token)
            : Task.FromResult(PristonTale.Simulation.World.Npcs.NpcInteraction.Rejected(NoConnection));
    }

    public Task<PristonTale.Simulation.World.Npcs.NpcServiceReply> RequestNpcServiceAsync(PristonTale.Simulation.World.Npcs.NpcServiceCommand command)
    {
        return _playChannel is not null ? _playChannel.RequestNpcServiceAsync(command, _presenceCts.Token)
            : Task.FromResult(new PristonTale.Simulation.World.Npcs.NpcServiceReply(PristonTale.Simulation.World.Npcs.NpcServiceOutcome.Failed(
                PristonTale.Simulation.World.Npcs.NpcServiceStatus.Rejected, NoConnection)));
    }

    public Task<PristonTale.Simulation.Quests.QuestOutcome> RequestQuestAsync(PristonTale.Simulation.Quests.QuestCommand command)
    {
        return _playChannel is not null ? _playChannel.RequestQuestAsync(command, _presenceCts.Token)
            : Task.FromResult(PristonTale.Simulation.Quests.QuestOutcome.Failed(PristonTale.Simulation.Quests.QuestStatus.Rejected,
                NoConnection));
    }

    public void PlayItemSound(string catalogKey) => _fieldAudio?.PlayItem(catalogKey);

    public void PlayQuestFanfare()
    {
        _fieldAudio?.PlayQuestFanfare();
    }

    public void PlayCraftResult(bool success)
    {
        _fieldAudio?.PlayCraftResult(success);
    }

    public void PlayWhisper()
    {
        _fieldAudio?.PlayWhisper();
    }

    public void PlayNpcInteractionAudio(PristonTale.Simulation.World.Npcs.NpcInteraction interaction)
    {
        if (_npcAudio is null || _remote is null || _playerBody is null
            || !_remote.TryGetNpcAudioSource(interaction.EntityId, out var soundCode, out var sourcePosition))
        {
            return;
        }

        _npcAudio.PlayInteraction(interaction, soundCode, sourcePosition, _playerBody.Position);
    }

    public void StopNpcTalk()
    {
        _npcAudio?.StopTalk();
    }

    public PristonRenderStats LastRenderStats => _renderer?.LastFrameStats ?? default;

    public (int Posed, int Reused) LastAnimationCounts => _renderer?.LastAnimationCounts ?? default;

    public float TimeOfDay => _renderer?.TimeOfDay ?? 0f;
    public Color SkyColor => _renderer?.SkyColor ?? new Color(72, 92, 116);
    public int StageLightCount => _renderer?.StageLightCount ?? 0;

    public float DayLengthSeconds
    {
        get => _renderer?.DayLengthSeconds ?? 0f;
        set { if (_renderer != null) _renderer.DayLengthSeconds = value; }
    }

    // The server keeps the time of day; the sky follows its clock.
    private void FollowServerClock()
    {
        if (_playChannel?.GameClockMs is not { } clock)
        {
            return;
        }

        _renderer.DayLengthSeconds = 0f;
        _renderer.SetTimeOfDay(WorldClock.HoursAt(clock));
        _renderer.MoonPhase = WorldClock.MoonPhaseAt(clock);
    }
    public void SetTimeOfDay(float hours) => _renderer?.SetTimeOfDay(hours);

    public bool ShadowsEnabled => _renderer?.ShadowsEnabled ?? false;

    public void ToggleShadows()
    {
        if (_renderer != null)
            _renderer.ShadowsEnabled = !_renderer.ShadowsEnabled;
    }

    public PristonDebugView RenderDebugView => _renderer?.DebugView ?? PristonDebugView.Shaded;

    public void CycleRenderDebugView()
    {
        if (_renderer != null)
        {
            var next = ((int)_renderer.DebugView + 1) % Enum.GetValues<PristonDebugView>().Length;
            _renderer.DebugView = (PristonDebugView)next;
        }
    }

    private FieldResourceStreamer _fieldStreamer;

    private void AddPlayerActors(string assetRoot, int faceIndex, int tier, int hairIndex)
    {
        var visuals = CharacterHead.Apply(_player.Definition.DefaultVisuals, _player.Class.Class, tier, faceIndex, hairIndex);
        var handles = _playerComposer.Compose(
            assetRoot,
            visuals,
            faceIndex,
            PlayerWorld(),
            _playerController.Current);
        _equipmentPresentation = new PlayerEquipmentPresentation(_playerComposer, _actorScene, _log,
            assetRoot, visuals, _player.Definition.MotionJobMask, handles);
        _playerBodyActor = handles.Body;
        _playerHeadActor = handles.Head;
    }

    // The play channel's leave (the server stores the character and answers) after Dispose; the owner of the connection
    // waits for it before closing the connection.
    public Task Closing { get; private set; } = Task.CompletedTask;

    private int _streamScanCounter;

    private const float WarpCooldownSeconds = 1.5f;
    private const float SpecialWarpDelaySeconds = 2f;
    private float _warpCooldown;
    private FieldWarpGate _pendingWarp;
    private WarpInfo _pendingFieldWarp;
    // The server settles teleports within twice the drop tolerance of their height.
    private const float WarpFloorTolerance = 64f;
    private float _pendingWarpDelay;

    public int PlayerServerLife => _selfLife;
    public int PlayerServerMaxLife => _selfMaxLife;

    void IFieldContext.SetDayLength(float seconds) => DayLengthSeconds = seconds;

    public void Update(GameTime gameTime)
    {
        _npcAudio?.Update();
        if (_equipmentPresentation?.Update(Owner, _activeField?.Definition.State == FieldState.Village, PlayerWorld(), _playerController.Current) == true)
        {
            _playerBodyActor = _equipmentPresentation.Handles.Body;
            _playerHeadActor = _equipmentPresentation.Handles.Head;
        }
        UpdateMonsterEffects((float)gameTime.ElapsedGameTime.TotalSeconds);
        UpdateCombatSounds();
        var frameDt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateWeather(frameDt);
        if (_playerBody is not null)
        {
            _fieldAudio?.Update(_playerBody.Position, TimeOfDay, frameDt);
        }
        var completedField = _fieldStreamer?.Update();
        if (completedField is not null)
        {
            _fields.Add(completedField);
            InvalidateGround();
        }
        _skillClockMs = gameTime.TotalGameTime.TotalMilliseconds;
        DrainPresence(frameDt);
        KeepOnFloor(frameDt);
        UpdateSkills(gameTime);
        UpdateItemEffects(frameDt);
        ApplyPositionCorrection(frameDt);
        UpdatePlayerInput(gameTime);
        UpdateTargetSelection(gameTime);
        UpdateWarps((float)gameTime.ElapsedGameTime.TotalSeconds);
        UpdateStreaming();
        if (_camera != null && _activeField != null)
        {
            _camera.Update(gameTime, _graphicsDevice, _playerBody.Position, _playerForward, _fieldCollision, PointerCaptured, CameraInputEnabled);
        }

        if (_renderer != null)
        {
            FollowServerClock();
            _renderer.Update(gameTime);
        }

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateMovementReport(dt);
        _currentField?.OnUpdate?.Invoke(this, dt);
    }

    private void OnNpcMotionStarted(int soundCode, MotionCode motion, Vector3 sourcePosition)
    {
        if (_npcAudio is not null && _playerBody is not null)
        {
            _npcAudio.PlayMotion(soundCode, motion, sourcePosition, _playerBody.Position);
        }
    }

    private void UpdateMovementReport(float dt)
    {
        _reports.Advance(dt);
        if (_playChannel == null || _activeField == null || _playerBody == null)
        {
            return;
        }

        var current = _playerController.Current;
        var moving = current is MotionCode.Walk or MotionCode.Run;
        var running = current == MotionCode.Run;
        var target = _attackInput.TargetId;
        var fieldCode = _activeField.Definition.FieldCode;
        if (!_reports.TryTake(moving, running, target, fieldCode))
        {
            return;
        }

        var planar = _playerBody.PlanarPosition;
        var position = WorldSpace.Mirror(new NumVector3(planar.X, _playerBody.Height, planar.Y));
        var facing = WorldSpace.TransportFacing(_playerForward);
        SendUpstream(_playChannel.SendMovementAsync(position, facing, moving, running, target, fieldCode, _presenceCts.Token), "movement");
    }

    private void SendUpstream(Task send, string what)
    {
        // Network sends must never block the game loop.
        _ = send.ContinueWith(
            t => _log.Warn($"[Remote] {what} send failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ObserveEffects(PresenceUpdate update)
    {
        if (_effectLibrary is null)
        {
            return;
        }

        ObserveMonsterSwings(update);
        ObservePlayerSwings(update);
        foreach (var effect in update.Events)
        {
            if (effect.Kind != CombatEventKind.PotionApplied || _itemEffects is null)
            {
                continue;
            }

            Vector3 position;
            if (effect.TargetId == _remote.SelfEntityId && _playerBody is not null)
            {
                position = _playerBody.Position;
            }
            else if (!_remote.TryGetDrawnPosition(effect.TargetId, out position))
            {
                continue;
            }

            position.Y += 48f;
            _itemEffects.Potion(effect.Resources, position, PlayerPosition);
        }
    }

    public void Draw()
    {
        if (_renderer != null && _camera != null)
        {
            _renderer.Draw(_camera);
            DrawRain();
            DrawSkillEffects();
            _renderer.ComposeScene();
        }
    }

    // The drop names the HUD draws this frame (GroundLootPresentation.CollectLabels); the HUD reports back where each
    // landed through PlaceGroundLabel.
    public void CollectGroundLabels(List<GroundLabel> labels)
    {
        if (_groundLoot is null || _camera is null)
        {
            labels.Clear();
            return;
        }

        _groundLoot.CollectLabels(_graphicsDevice.Viewport, _camera, HoveredDropId, ShowAllDropLabels, labels);
    }

    public void PlaceGroundLabel(Guid id, Rectangle bounds) => _groundLoot?.SetLabelBounds(id, bounds);

    public void Dispose()
    {
        _fieldStreamer?.Dispose();
        TearDownRemotePresence();
        _equipmentPresentation?.Stop();
        _groundLoot?.Dispose();
        _looks?.Clear();
        _skillFx?.Dispose();
        _rain?.Dispose();
        _renderer?.Dispose();
        _monsterFx?.Clear();
        _combatSounds?.Dispose();
        _fieldAudio?.Dispose();
        _npcAudio?.Dispose();
    }

    private sealed class PhysicsBody
    {
        public PhysicsBody(Vector2 planarPosition)
        {
            PlanarPosition = planarPosition;
        }

        public Vector2 PlanarPosition { get; set; }
        public float Height { get; set; }
        public float Radius { get; init; }
        public Vector3 Position => new(PlanarPosition.X, Height, PlanarPosition.Y);
    }
}
