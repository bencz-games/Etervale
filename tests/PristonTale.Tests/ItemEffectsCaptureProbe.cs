using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Item effects in Ricarten: firecracker bursts over the player, the skill-learn light of a hair tint, a force orb's
// weapon glow and hit spark, a big head and a soccer-ball head, the premium pets beside their owners, idle and striking a
// monster, and crystal pets with their name plates called in the return sparkle.
internal sealed class ItemEffectsCaptureProbe : Game
{
    private const ulong SelfId = 1;
    private const uint HopyId = 2;
    private const uint FriendId = 3;
    private const uint KickerId = 4;
    private const ulong PetId = 5;
    private const uint FriendPetId = 6;

    private sealed record Shot(string Name, double AtSeconds, Action<ItemEffectsCaptureProbe>? Begin = null);

    private static readonly Shot[] Shots =
    [
        new("pets-idle", 4.0, probe => probe.Pets()),
        new("pets-strike", 5.0, probe => probe.Strike()),
        new("pets-hit", 5.55),
        new("firecracker-1", 6.6, probe => probe.Firecracker()),
        new("firecracker-2", 7.7),
        new("firecracker-3", 8.7),
        new("learn-ring", 10.35, probe => probe.Learn()),
        new("learn-ring-late", 10.75),
        new("force-orb", 12.5, probe => probe.ForceOrb()),
        new("force-orb-hit", 12.75, probe => probe.ForceOrbHit()),
        new("crystal-summon", 14.25, probe => probe.CrystalPets()),
        new("crystal-pets", 15.5),
        new("soccer-head", 16.0)
    ];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private readonly System.Diagnostics.Stopwatch _timeout = System.Diagnostics.Stopwatch.StartNew();
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private RemoteEntityManager _remote = null!;
    private EffectLibrary _library = null!;
    private ItemUseEffects _items = null!;
    private PremiumPetPresentation _pets = null!;
    private SkillPresentation _skills = null!;
    private ForceOrbPresentation _orbs = null!;
    private SkillSoundBank _sounds = null!;
    private Vector3 _focus;
    private Vector3 _eye;
    private int _shot;
    private double _seconds;
    private bool _begun;
    private PristonCollisionWorld _collision = null!;

    public ItemEffectsCaptureProbe(string assetRoot, string contentRoot, string output)
    {
        _assetRoot = assetRoot;
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
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
        _renderer.SetTimeOfDay(23.5f);
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day),
            PristonSkyProfiles.Resolve(field.Backdrop.Glow), PristonSkyProfiles.Resolve(field.Backdrop.Night));
        var reader = new PristonSmdReader();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(stage, models);
        var collision = new PristonCollisionWorld(stage);
        _collision = collision;
        var start = field.StartPoints[0];
        _focus = new Vector3(start.X, 0f, -start.Z);
        _focus.Y = collision.TryGetFloorHeight(_focus with { Y = 5000f }, out var floor) ? floor : 0f;
        _eye = _focus + new Vector3(40f, 70f, 150f);
        _camera = new PristonCamera(_eye, Vector3.Normalize(_focus - _eye));

        var loader = new PristonActorLoader();
        var actorScene = new RendererActorScene(_renderer);
        var catalog = new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new(1, "char/monster/Hopy/hopy.inx", 1f, "Hopy", 3, MonsterBrood.Normal, 8, 25)
        });
        _remote = new RemoteEntityManager(actorScene, new PlayerActorComposer(actorScene, loader), new ModelActorComposer(actorScene, loader),
            new CharacterClassRegistry(), _assetRoot, catalog, ConsoleClientLog.Instance);
        _remote.Apply(new PresenceUpdate(1,
        [
            new(SelfId, EntityKind.Player, false, Numeric(_focus), 0f, MotionState.Stand, 64, 64,
                new PlayerAppearanceInfo("Panzer", PlayerClass.Mechanician, 0, 0, 0, "", "", 40), 0),
            new(HopyId, EntityKind.Monster, false, Numeric(Floor(collision, _focus + new Vector3(-90f, 0f, 30f))), 1.2f, MotionState.Stand, 31, 50,
                null, 1, Name: "Hopy", Level: 3),
            new(FriendId, EntityKind.Player, false, Numeric(Floor(collision, _focus + new Vector3(110f, 0f, -40f))), 2.6f, MotionState.Stand, 900,
                1000, new PlayerAppearanceInfo("Lyra", PlayerClass.Priestess, 0, 1, 0, "", "", 37) { BigHead = 2 }, 0),
            new(KickerId, EntityKind.Player, false, Numeric(Floor(collision, _focus + new Vector3(-40f, 0f, -70f))), 0.6f, MotionState.Stand, 900,
                1000, new PlayerAppearanceInfo("Kicker", PlayerClass.Fighter, 0, 0, 0, "", "", 52) { BigHead = CharacterHead.SoccerLook }, 0)
        ], [], [], []));
        _library = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assetRoot, ConsoleClientLog.Instance);
        _sounds = new SkillSoundBank(_assetRoot, ConsoleClientLog.Instance) { Enabled = false };
        _skills = new SkillPresentation(GraphicsDevice, Content, _library, _renderer.TransientLights, new SceneHost(this), _sounds, ConsoleClientLog.Instance);
        _library.Vfx.CompileAll();
        ItemVfx.Register(_skills.Library);
        _items = new ItemUseEffects(_skills, _renderer.TransientLights, _sounds);
        var scripts = new SystemScriptEffects(_skills.System, _skills.Library);
        _orbs = new ForceOrbPresentation(_skills, scripts, _renderer.TransientLights);
        _pets = new PremiumPetPresentation(actorScene, loader, _assetRoot, scripts, _renderer.TransientLights, _sounds, ConsoleClientLog.Instance);
    }

    // Two crystal pets, the viewer's own and a friend's, each called in the return sparkle (their name boxes are HUD
    // drawing, outside this probe).
    private void CrystalPets()
    {
        var own = Floor(_collision, _focus + new Vector3(-55f, 0f, 60f));
        var theirs = Floor(_collision, _focus + new Vector3(80f, 0f, 10f));
        _remote.Apply(new PresenceUpdate(5,
        [
            new(PetId, EntityKind.Monster, false, Numeric(own), 0.4f, MotionState.Stand, 120, 153, null, 1, Name: "Hopy", Level: 4)
            {
                OwnerId = SelfId
            },
            new(FriendPetId, EntityKind.Monster, false, Numeric(theirs), 2.4f, MotionState.Stand, 153, 153, null, 1, Name: "Hopy", Level: 4)
            {
                OwnerId = FriendId
            }
        ], [], [], []));
        _skills.SpawnAt(ItemVfx.Return, own, Matrix.Identity);
        _items.Play(ItemEffectKind.Return, SelfId, 0, own, _eye, _seconds * 1000d);
    }

    private void Pets()
    {
        _pets.SetPets(SelfId, 1, true);
        _pets.SetPets(FriendId, 3, false);
    }

    private void Strike()
    {
        var update = new PresenceUpdate(2, [], [], [], [])
        {
            ItemEffects =
            [
                new ItemEffectInfo(SelfId, (uint)PremiumEffectKind.HelpPetTerry, ItemEffectKind.PetAttack, default, 0, HopyId),
                new ItemEffectInfo(SelfId, (uint)PremiumEffectKind.PhoenixPet, ItemEffectKind.PetAttack, default, 0, HopyId),
                new ItemEffectInfo(FriendId, (uint)PremiumEffectKind.HelpPetIo, ItemEffectKind.PetAttack, default, 0, HopyId)
            ]
        };
        _pets.Observe(update, Anchor);
    }

    private void Firecracker() => _items.Play(ItemEffectKind.Firecracker, SelfId, 4321 * 2, _focus, _eye, _seconds * 1000d);

    private void Learn()
    {
        _items.Play(ItemEffectKind.Learn, SelfId, 0, _focus, _eye, _seconds * 1000d);
        _skills.Spawn(ItemVfx.Learn, SelfId, SelfId, _focus);
    }

    private void ForceOrb()
    {
        var orb = ForceOrbs.All.Select(entry => entry.Value).First(candidate => candidate.Index == 9 && !candidate.Magic);
        var status = new StatusEffectInfo(SelfId, SelfId, orb.StatusCode, StatusEffectOp.Add, 1, 0, 0, 0, 60000, 60000, 0);
        _orbs.Observe(new PresenceUpdate(3, [], [], [], []) { StatusEffects = [status] }, Anchor, _seconds * 1000d);
    }

    private void ForceOrbHit()
    {
        var hit = new CombatEventInfo(SelfId, HopyId, CombatEventKind.Hit, 500f, 250f, 120, false, false);
        _orbs.Observe(new PresenceUpdate(4, [], [], [], [hit]), Anchor, _seconds * 1000d);
    }

    private Vector3? Anchor(ulong entityId) => _remote.TryGetDrawnPosition(entityId, out var position) ? position : null;

    private (Vector3 Position, float Facing)? Pose(ulong entityId) =>
        _remote.TryGetDrawnPosition(entityId, out var position) && _remote.TryGetDrawnFacing(entityId, out var facing) ? (position, facing) : null;

    protected override void Update(GameTime gameTime)
    {
        if (_timeout.Elapsed > TimeSpan.FromMinutes(3))
        {
            throw new TimeoutException("Item effect capture did not finish.");
        }

        if (_shot >= Shots.Length)
        {
            Exit();
            return;
        }

        const float step = 1f / 60f;
        if (_remote.Count < 4)
        {
            _remote.Update(step);
            return;
        }

        _seconds += step;
        if (!_begun && Shots[_shot].Begin is { } begin && _seconds >= Shots[_shot].AtSeconds - 1.0)
        {
            begin(this);
            _begun = true;
        }

        _remote.Update(step);
        _pets.Update(step, Pose, Anchor, _eye);
        _items.Update(_seconds * 1000d, Anchor, _eye);
        _orbs.Update(_seconds * 1000d, Anchor);
        _skills.Update(step, _seconds * 1000d);
        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(step)));
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_shot >= Shots.Length || _seconds < Shots[_shot].AtSeconds)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        var shot = Shots[_shot];
        using var target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        if (shot.Name == "soccer-head" && Anchor(KickerId) is { } kicker)
        {
            _camera.SetScriptedView(GraphicsDevice, kicker + new Vector3(25f, 45f, 45f), kicker + Vector3.Up * 38f);
        }
        else
        {
            _camera.SetScriptedView(GraphicsDevice, _eye, _focus + Vector3.Up * (shot.Name.StartsWith("firecracker", StringComparison.Ordinal) ? 90f : 30f));
        }

        _renderer.Draw(_camera);
        _skills.Draw(_camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[960 * 540];
        target.GetData(pixels);
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index].A = 255;
        }

        using var opaque = new Texture2D(GraphicsDevice, 960, 540);
        opaque.SetData(pixels);
        using (var stream = File.Create(Path.Combine(_output, shot.Name + ".png")))
        {
            opaque.SaveAsPng(stream, 960, 540);
        }

        var vfx = $"{_skills.System.Instances.Count}/{_skills.System.ParticleCount}/{_skills.Renderer.LastQuadCount}";
        Console.WriteLine($"Item effects {shot.Name}: captured; pets={_pets.OwnerCount}; vfx={vfx}; bursts={_items.PendingBursts}");
        foreach (var (owner, kind, position) in _pets.Pets)
        {
            Console.WriteLine($"  pet {kind} of {owner} at {position - (Anchor(owner) ?? Vector3.Zero)} from its owner");
        }
        _shot++;
        _begun = false;
    }

    protected override void UnloadContent()
    {
        _pets.Dispose();
        _remote.Dispose();
        _library.Dispose();
        _sounds.Dispose();
    }

    private sealed class SceneHost(ItemEffectsCaptureProbe probe) : ISkillSceneHost
    {
        public ulong SelfId => ItemEffectsCaptureProbe.SelfId;

        public Vector3 ListenerPosition => probe._eye;

        public void PlaySkillMotion(ulong entityId, SkillMotionPlan plan, float durationMs, float rate, int loop)
        {
        }

        public float AttackRateOf(ulong entityId) => 1f;

        public bool TryGetPosition(ulong entityId, out Vector3 position) => probe._remote.TryGetDrawnPosition(entityId, out position);

        public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
        {
            position = default;
            if (!probe._remote.TryGetBodyActor(entityId, out var actor) || !probe._renderer.TryGetActorBoneWorld(actor, bone, out var world))
            {
                return false;
            }

            position = world.Translation;
            return true;
        }
    }

    private static Vector3 Floor(PristonCollisionWorld collision, Vector3 point) =>
        collision.TryGetFloorHeight(point with { Y = point.Y + 500f }, out var height) ? point with { Y = height } : point;

    private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);

    private string Resolve(string relative) => AssetFiles.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
