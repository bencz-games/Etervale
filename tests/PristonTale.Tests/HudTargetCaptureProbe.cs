using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

// Renders the in-game HUD over Ricarten with live actors spawned through the remote entity roster, so the target frame
// shows its 3D portrait (a monster of each race icon kind used here, an NPC and a player) and the minimap its markers.
internal sealed class HudTargetCaptureProbe : Game
{
    private const ulong SelfId = 1;
    private const uint HopyId = 2;
    private const uint SkeletonId = 3;
    private const ulong NpcId = 4;
    private const ulong PlayerId = 5;
    // A static NPC without a head bone: the portrait falls back to the authored model height.
    private const uint BonelessNpcId = 6;
    // A long name over a head-bone NPC, at a window height that is not a multiple of the design height.
    private const uint CraftsmanId = 7;

    private sealed record Scenario(string Name, int Width, int Height, ulong Target, bool Classic = false, bool HoverMap = false);

    private static readonly Scenario[] Scenarios =
    [
        new("target-monster-800", 800, 600, HopyId),
        new("target-undead-800", 800, 600, SkeletonId),
        new("target-npc-800", 800, 600, NpcId),
        new("target-player-800", 800, 600, PlayerId),
        new("target-monster-classic-800", 800, 600, HopyId, Classic: true),
        new("target-npc-1280", 1280, 720, NpcId),
        new("target-npc-boneless-1280", 1280, 720, BonelessNpcId),
        new("target-npc-craftsman-1920x1009", 1920, 1009, CraftsmanId),
        new("target-monster-1920", 1920, 1080, HopyId),
        new("target-player-1600x1200-scale2", 1600, 1200, PlayerId),
        new("minimap-hover-800", 800, 600, NpcId, HoverMap: true)
    ];

    private sealed class PortraitScene(PristonRenderer renderer, RemoteEntityManager remote) : IHudPortraitScene
    {
        public PristonRenderer Renderer { get; } = renderer;

        public int CopyActorHandles(ulong entityId, Span<ActorHandle> destination) => remote.CopyActorHandles(entityId, destination);
    }

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private readonly System.Diagnostics.Stopwatch _timeout = System.Diagnostics.Stopwatch.StartNew();
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private RemoteEntityManager _remote = null!;
    private SpriteBatch _batch = null!;
    private GameplayHud _hud = null!;
    private OwnerState _owner = null!;
    private HudWorldView _world;
    private PresenceUpdate _spawns = null!;
    private Vector3 _focus;
    private Vector3 _eye;
    private int _scenario;
    private int _bindFrame;
    private bool _leftRefused;
    private int _settle;
    private double _seconds;

    public HudTargetCaptureProbe(string assetRoot, string contentRoot, string output)
    {
        _assetRoot = assetRoot;
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        var field = new WorldCatalog().AllFields.Single(candidate => candidate.Id == "village-2");
        _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _assetRoot);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _presentation) { Sky = _sky, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(14f);
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day),
            PristonSkyProfiles.Resolve(field.Backdrop.Glow), PristonSkyProfiles.Resolve(field.Backdrop.Night));
        var reader = new PristonSmdReader();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(stage, models);
        var collision = new PristonCollisionWorld(stage);
        var start = field.StartPoints.Count > 0 ? field.StartPoints[0] : field.Center;
        _focus = new Vector3(start.X, 0f, -start.Z);
        _focus.Y = collision.TryGetFloorHeight(_focus with { Y = 5000f }, out var floor) ? floor : 0f;
        _eye = _focus + new Vector3(40f, 230f, 340f);
        _camera = new PristonCamera(_eye, Vector3.Normalize(_focus - _eye));

        var loader = new PristonActorLoader();
        var actorScene = new RendererActorScene(_renderer);
        var catalog = new WorldRenderCatalog(
            new Dictionary<uint, MonsterRenderInfo>
            {
                [1] = new(1, "char/monster/Hopy/hopy.inx", 1f, "Hopy", 3, MonsterBrood.Normal, 8, 25),
                [2] = new(2, "char/monster/Monskeleton/MonSkeleton-a.inx", 1f, "Skeleton", 12, MonsterBrood.Undead, -3, 30)
            },
            new Dictionary<uint, NpcRenderInfo>
            {
                [1] = new(1, "char/npc/TN-002/TN-002.inx", 1f, "Ricarten Guard", NpcServiceSet.None),
                [2] = new(2, "char/npc/Teacher/Teacher01.inx", 1f, "Helper Pria", NpcServiceSet.None),
                [3] = new(3, "char/npc/TN-012/TN-012.inx", 1f, "Mixing Craftsman, Morald", NpcServiceSet.None)
            });
        _remote = new RemoteEntityManager(actorScene, new PlayerActorComposer(actorScene, loader), new ModelActorComposer(actorScene, loader),
            new CharacterClassRegistry(), _assetRoot, catalog, ConsoleClientLog.Instance);
        _spawns = new PresenceUpdate(1,
        [
            new(SelfId, EntityKind.Player, true, Numeric(_focus), 0, MotionState.Stand, 64, 64,
                new PlayerAppearanceInfo("Panzer", PlayerClass.Mechanician, 0, 0, 0, "", "", 12), 0),
            new(HopyId, EntityKind.Monster, false, Numeric(Floor(collision, _focus + new Vector3(70f, 0f, 40f))), 2.4f,
                MotionState.Stand, 31, 50, null, 1, Name: "Hopy", Level: 3),
            new(SkeletonId, EntityKind.Monster, false, Numeric(Floor(collision, _focus + new Vector3(150f, 0f, -40f))), 2.0f,
                MotionState.Stand, 180, 200, null, 2, Name: "Skeleton", Level: 12),
            new(NpcId, EntityKind.Npc, false, Numeric(Floor(collision, _focus + new Vector3(-60f, 0f, -80f))), 3.0f,
                MotionState.Stand, 0, 0, null, 1, Name: "Ricarten Guard", Level: 100),
            new(BonelessNpcId, EntityKind.Npc, false, Numeric(Floor(collision, _focus + new Vector3(-150f, 0f, 20f))), 0.5f,
                MotionState.Stand, 0, 0, null, 2, Name: "Helper Pria", Level: 1),
            new(CraftsmanId, EntityKind.Npc, false, Numeric(Floor(collision, _focus + new Vector3(-100f, 0f, 120f))), 1.5f,
                MotionState.Stand, 0, 0, null, 3, Name: "Mixing Craftsman, Morald", Level: 1),
            new(PlayerId, EntityKind.Player, false, Numeric(Floor(collision, _focus + new Vector3(10f, 0f, 90f))), 2.6f,
                MotionState.Stand, 900, 1000, new PlayerAppearanceInfo("Lyra", PlayerClass.Priestess, 0, 1, 0, "", "", 37), 0)
        ], [], [], []);
        _remote.Apply(_spawns);

        _batch = new SpriteBatch(GraphicsDevice);
        var font = Content.Load<SpriteFont>("Fonts/UI");
        var context = new ScreenContext(GraphicsDevice, Content, _batch, font, _assetRoot, null!, ConsoleClientLog.Instance, _ => { }, _presentation);
        _hud = new GameplayHud(context, _ => Task.FromResult(new InventoryOutcome(false, 0, GameText.Of(GameTextId.RequestRefused))))
        {
            CharacterName = "Panzer",
            CharacterClass = PlayerClass.Mechanician,
            PortraitScene = new PortraitScene(_renderer, _remote)
        };
        _hud.Combat.SetCatalog(catalog);
        _hud.Combat.Observe(_spawns);
        _owner = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer"));
        var forward = Vector3.Normalize(new Vector3(_focus.X - _eye.X, 0f, _focus.Z - _eye.Z));
        var mapHidden = HudMinimap.HiddenOn(field.FieldCode, field.State);
        _world = new HudWorldView(field.Id, field.Underground, stage.AuthoredMapRectangle, _focus, forward, 14f, false, mapHidden);
    }

    protected override void Update(GameTime gameTime)
    {
        if (_timeout.Elapsed > TimeSpan.FromMinutes(3))
        {
            throw new TimeoutException("HUD target capture did not finish.");
        }

        if (_scenario >= Scenarios.Length)
        {
            if (BindSkillToMouse())
            {
                Exit();
            }

            return;
        }

        const float step = 1f / 60f;
        _seconds += step;
        var scenario = Scenarios[_scenario];
        _presentation.Requested = _presentation.Requested with { ClassicTargetHealthBar = scenario.Classic };
        _remote.Update(step);
        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(step)));
        _hud.World = _world;
        _hud.TargetId = scenario.Target;
        _hud.Update(_owner, default, Pointer(scenario), step);
        if (_hud.IsReady && _hud.IconsIdle && _remote.Count >= 4)
        {
            _settle++;
        }
    }

    // The socket flow on the live HUD: S opens the skill window; a left click on a right-mouse skill is refused with a
    // notice and a right click puts it in the right socket.
    private bool BindSkillToMouse()
    {
        const float step = 1f / 60f;
        const uint extremeShield = 0x01010001;
        var layout = HudLayout.Create(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        if (_bindFrame == 0)
        {
            var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer", 90);
            player.Skills.Restore(extremeShield, 10);
            _owner = OwnerState.Capture(player);
        }

        var node = HudSkillTreeLayout.Node(HudInventoryLayout.Origin(layout), 0).Center;
        var keyboard = _bindFrame == 2 ? new KeyboardState(Keys.S) : default;
        var left = _bindFrame == 80 ? ButtonState.Pressed : ButtonState.Released;
        var right = _bindFrame == 90 ? ButtonState.Pressed : ButtonState.Released;
        var mouse = new MouseState(node.X * layout.Scale, node.Y * layout.Scale, 0, left, ButtonState.Released, right, ButtonState.Released, ButtonState.Released);
        _hud.Update(_owner, keyboard, mouse, step);
        _bindFrame++;
        if (_bindFrame == 85)
        {
            _leftRefused = _hud.LeftSkillId == HudSkillBindings.BasicAttackId;
        }

        if (_bindFrame < 100)
        {
            return false;
        }

        var bound = _hud.RightSkillId == extremeShield;
        Console.WriteLine($"HUD skill socket: left click refused={_leftRefused}; right click bound={bound}");
        if (!_leftRefused || !bound)
        {
            throw new InvalidOperationException("the skill window did not bind a learned skill to its mouse socket");
        }

        return true;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_scenario >= Scenarios.Length || _settle < 90)
        {
            GraphicsDevice.Clear(Color.Black);
            if (_scenario >= Scenarios.Length)
            {
                _hud.Draw();
            }

            return;
        }

        var scenario = Scenarios[_scenario];
        _hud.RenderPortrait();
        using var target = new RenderTarget2D(GraphicsDevice, scenario.Width, scenario.Height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        _camera.SetScriptedView(GraphicsDevice, _eye, _focus);
        _renderer.ResetTemporalState();
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        _hud.Draw();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[scenario.Width * scenario.Height];
        target.GetData(pixels);
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index].A = 255;
        }

        using var opaque = new Texture2D(GraphicsDevice, scenario.Width, scenario.Height);
        opaque.SetData(pixels);
        using (var stream = File.Create(Path.Combine(_output, scenario.Name + ".png")))
        {
            opaque.SaveAsPng(stream, scenario.Width, scenario.Height);
        }

        var frame = HudCombat.TargetFrame(HudLayout.Create(scenario.Width, scenario.Height));
        Span<ActorHandle> handles = stackalloc ActorHandle[4];
        var anchor = _remote.CopyActorHandles(scenario.Target, handles) > 0
            && _renderer.TryGetActorPortraitAnchor(handles[0], HudTargetPortrait.HeadBone, out var found) ? found : default;
        Console.WriteLine($"HUD target {scenario.Name}: captured; portrait={_hud.PortraitReady}; frame={frame.Box}; minimapView={_hud.MinimapViewSize}; " +
            $"head={anchor.Bone} bone={anchor.HasBone} height={anchor.Height} bounds={anchor.Bounds.Min}..{anchor.Bounds.Max}");
        _scenario++;
        _settle = 60;
    }

    protected override void UnloadContent()
    {
        _hud.Dispose();
        _remote.Dispose();
        _batch.Dispose();
    }

    private static MouseState Pointer(Scenario scenario)
    {
        var layout = HudLayout.Create(scenario.Width, scenario.Height);
        var point = scenario.HoverMap ? HudMinimap.Box(layout).Center : new Point(layout.Width / 2, layout.Height / 3);
        return new MouseState(point.X * layout.Scale, point.Y * layout.Scale, 0,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
    }

    private static Vector3 Floor(PristonCollisionWorld collision, Vector3 point) =>
        collision.TryGetFloorHeight(point with { Y = point.Y + 500f }, out var height) ? point with { Y = height } : point;

    private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);

    private string Resolve(string relative) => AssetFiles.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
