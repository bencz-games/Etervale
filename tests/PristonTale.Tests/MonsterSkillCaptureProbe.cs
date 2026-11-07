using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Plays real monsters' attack, skill and death clips next to a player through the client's monster presentation and
// saves frames (--capture-monster-skills <assets> <content> <out> [case names, comma separated]); a status case puts a
// monster status on the player. Each case must light up the frame against the one before its clip started.
internal sealed class MonsterSkillCaptureProbe : Game, ISkillSceneHost, IMonsterScene
{
    private const uint MonsterId = 1;
    private const ulong PlayerId = 2;
    private const float Step = 1f / 120f;

    private sealed record Case(string Name, int Sound, int Level, float Scale, string Model, int Slot, float[] Times, uint Status = 0, float Distance = 1f);

    private static readonly Case[] DefaultCases =
    [
        new("figon-attack", 0x1180, 69, 1.3f, "char/monster/figon/monfigon-boss.inx", 14, [0.3f, 0.7f, 1.1f]),
        new("witch-skill", 0x12C0, 71, 1f, "char/monster/monwitch/monwitch2.inx", 14, [0.4f, 0.8f, 1.3f]),
        new("stygianlord-skill", 0x1350, 70, 1f, "char/monster/monstygianlord/monstygianlord.inx", 15, [0.6f, 1.1f, 1.6f]),
        new("dmachine-skill", 0x1370, 89, 1f, "char/monster/mondmachine/mondmachine.inx", 14, [0.6f, 1.1f, 1.6f]),
        new("naz-skill", 0x1290, 65, 1f, "char/monster/monnazz/monnazz.inx", 15, [0.3f, 0.7f, 1.1f]),
        new("hypermachine-skill", 0x1530, 84, 1f, "char/monster/hyper/hyper.inx", 14, [0.5f, 1.0f, 1.6f]),
        new("hypermachine-death", 0x1530, 84, 1f, "char/monster/hyper/hyper.inx", 16, [0.3f, 0.8f, 1.4f]),
        new("guardiansaint-skill", 0x1470, 80, 1f, "char/monster/guardian/guardian.inx", 16, [1.0f, 1.8f, 2.6f]),
        new("babel-skill", 0x1600, 104, 1f, "char/monster/bguardian/bguardian.inx", 15, [0.8f, 1.8f, 2.3f], Distance: 2f),
        new("coldeye-skill", 0x1630, 91, 1f, "char/monster/coldeye/coldeye.inx", 14, [0.5f, 1.2f, 2.0f]),
        new("chaoscara-skill", 0x1670, 100, 1.2f, "char/monster/chaoscara/chaoscara.inx", 14, [0.4f, 0.9f, 1.4f], Distance: 1.5f),
        new("deathknight-skill", 0x1680, 110, 1f, "char/monster/death_knight/death_knight.inx", 19, [0.6f, 1.2f, 1.8f], Distance: 1.6f),
        new("kelvezu-skill", 0x1830, 120, 1f, "char/monster/kelvezu/kelvezu.inx", 14, [1.0f, 2.0f, 3.0f], Distance: 2.6f),
        new("stingray-skill", 0x1740, 76, 1.2f, "char/monster/stingray/stingray.inx", 14, [0.4f, 0.9f, 1.4f]),
        new("darkphalanx-skill", 0x1840, 105, 1.5f, "char/monster/darkphalanx/darkphalanx.inx", 15, [0.9f, 1.3f, 1.8f], Distance: 1.4f),
        new("bloodyknight-skill", 0x1850, 106, 1f, "char/monster/s_bloodyknight/s_bknight.inx", 15, [0.5f, 1.0f, 1.5f]),
        new("darkguard-skill", 0x1890, 103, 1.2f, "char/monster/darkguard/skullac.inx", 12, [0.7f, 1.0f, 1.4f]),
        new("darkmage-skill", 0x18A0, 104, 1.4f, "char/monster/darkmage/skullma.inx", 13, [0.6f, 1.2f, 1.8f]),
        new("revivedknight-skill", 0x18F0, 107, 1f, "char/monster/d_kn/dkn.inx", 18, [0.4f, 0.9f, 1.5f]),
        new("revivedpriestess-skill", 0x1960, 107, 1f, "char/monster/d_pr/dpr.inx", 18, [0.5f, 1.0f, 1.6f]),
        new("lightningcrystal-attack", 0x5021, 100, 1.2f, "char/monster/t_crystal_g/t_crystal_g.inx", 11, [0.3f, 0.6f, 1.0f]),
        new("draxos-attack", 0x60A0, 157, 1f, "char/monster/Draxos/Draxos.inx", 14, [0.2f, 0.4f, 0.7f], Distance: 1.4f),
        new("draxos-skill", 0x60A0, 157, 1f, "char/monster/Draxos/Draxos.inx", 15, [0.5f, 1.0f, 1.4f], Distance: 1.4f),
        new("dey-skill", 0x6080, 131, 1f, "char/monster/Dey/Dey.inx", 15, [0.4f, 0.8f, 1.2f]),
        new("dmystery-skill", 0x6090, 118, 1f, "char/monster/Monshadow-1/DMystery.inx", 15, [1.1f, 1.4f, 1.8f]),
        new("ibomber-skill", 0x6100, 124, 1.5f, "char/monster/TBomber/TBomber.inx", 15, [0.4f, 0.8f, 1.3f]),
        new("inferno-attack", 0x6150, 117, 1.2f, "char/monster/figon/Inferno.inx", 14, [0.3f, 0.7f, 1.1f]),
        new("shogoth-skill", 0x62D0, 138, 1.3f, "char/monster/Shogoth/Shogoth.inx", 15, [0.4f, 0.8f, 1.2f]),
        new("wlord-attack", 0x6340, 123, 1f, "char/monster/WLord/WLord.inx", 14, [0.3f, 0.7f, 1.1f]),
        new("wlord-death", 0x6340, 123, 1f, "char/monster/WLord/WLord.inx", 16, [0.3f, 0.8f, 1.4f]),
        new("aragonian-skill", 0x63A0, 168, 1f, "char/monster/Aragonian/Aragonian.inx", 16, [1.9f, 2.2f, 2.6f], Distance: 4f),
        new("crystalwrath-skill", 0x63B0, 166, 1f, "char/monster/CrystalWrath/CrystalWrath.inx", 15, [0.4f, 0.8f, 1.2f]),
        new("deius-skill", 0x63C0, 180, 1f, "char/monster/Deius/Deius.inx", 16, [0.4f, 0.8f, 1.3f], Distance: 1.6f),
        new("grangium-attack", 0x63D0, 171, 1f, "char/monster/Grangium/Grangium.inx", 14, [0.2f, 0.4f, 0.7f]),
        new("status-poison", 0x1740, 76, 1.2f, "char/monster/stingray/stingray.inx", -1, [0.5f, 1.5f], MonsterStatuses.Poison),
        new("status-stun", 0x1850, 106, 1f, "char/monster/s_bloodyknight/s_bknight.inx", -1, [0.3f, 0.8f], MonsterStatuses.Stun),
        new("status-curse", 0x12C0, 71, 1f, "char/monster/monwitch/monwitch2.inx", -1, [0.5f, 1.5f], MonsterStatuses.CurseDefence)
    ];

    private static readonly Vector3 MonsterPosition = new(-40, 0, 40);
    private static readonly Vector3 PlayerPosition = new(60, 0, -80);

    private readonly Case[] _cases;
    private readonly string _assets;
    private readonly string _output;
    private readonly List<string> _failures = [];
    private readonly List<MonsterActorView> _views = [];
    private PristonRenderer _renderer = null!;
    private ModelActorComposer _monsters = null!;
    private PlayerActorComposer _players = null!;
    private EffectLibrary _library = null!;
    private SkillPresentation _presentation = null!;
    private MonsterPresentation _monsterEffects = null!;
    private PristonCamera _camera = null!;
    private RenderTarget2D _target = null!;
    private Task _preparation = Task.CompletedTask;
    private CharacterVisualSet _playerVisuals = null!;
    private PlayerActorHandles _player;
    private ActorHandle _monster = ActorHandle.None;
    private int _case = -1;
    private int _frame;
    private float _time;
    private int _warmup;
    private int _brightest;
    private Color[] _baseline = [];

    public MonsterSkillCaptureProbe(string assets, string content, string output, IReadOnlyList<string>? names = null)
    {
        _cases = names is null ? DefaultCases : DefaultCases.Where(value => names.Contains(value.Name)).ToArray();
        _assets = assets;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    public ulong SelfId => PlayerId;

    public Vector3 ListenerPosition => Vector3.Zero;

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        _library = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assets, ConsoleClientLog.Instance);
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = true, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(21.25f);
        var material = new PristonMaterial(true, 1, 0, 1, true, new Vector3(0.08f, 0.08f, 0.09f), 0, 1, PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var floor = new Color(26, 25, 23);
        var stage = new PristonStage("monster-fixture.smd", [material],
            [new(new Vector3(-900, 0, -900), floor), new(new Vector3(-900, 0, 900), floor), new(new Vector3(900, 0, 900), floor),
             new(new Vector3(900, 0, -900), floor)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)], [], [], new BoundingBox(new Vector3(-900, 0, -900), new Vector3(900, 0, 900)));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        var scene = new RendererActorScene(_renderer);
        _monsters = new ModelActorComposer(scene, new PristonActorLoader());
        _players = new PlayerActorComposer(scene, new PristonActorLoader());
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings { FieldOfViewDegrees = 42 });
        _presentation = new SkillPresentation(GraphicsDevice, Content, _library, _renderer.TransientLights, this,
            new SkillSoundBank(_assets, ConsoleClientLog.Instance) { Enabled = false }, ConsoleClientLog.Instance);
        _library.Vfx.CompileAll();
        _monsterEffects = new MonsterPresentation(new SkillEffectSink(_presentation), this);
        var knight = new CharacterClassRegistry().Get(PlayerClass.Knight).Definition;
        _playerVisuals = EquipmentVisuals.Apply(knight.DefaultVisuals, "DA108", "", "", false, knight.MotionJobMask);
        var pending = _cases.Select(value => value.Model).Distinct().Select(model => _monsters.PrepareAsync(_assets, model)).ToList();
        pending.Add(_players.PrepareAsync(_assets, _playerVisuals, 0));
        _preparation = Task.WhenAll(pending);
    }

    protected override void Update(GameTime gameTime)
    {
        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Monster capture assets failed to prepare.", _preparation.Exception);
        }

        if (_case < 0)
        {
            _player = _players.Compose(_assets, _playerVisuals, 0, World(PlayerPosition, MathF.PI, 1f), MotionCode.Stand);
            Begin(0);
        }
    }

    private static Matrix World(Vector3 position, float yaw, float scale) =>
        Matrix.CreateScale(scale) * Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(position);

    // The server's facing toward the player (client space mirrors Z), which remote monsters also use for their model.
    private static float Facing => MathF.Atan2(PlayerPosition.X - MonsterPosition.X, -(PlayerPosition.Z - MonsterPosition.Z));

    private void Begin(int index)
    {
        if (_monster.IsValid)
        {
            _renderer.RemoveActor(_monster);
        }

        _presentation.Clear();
        _monsterEffects.Clear();
        _renderer.TransientLights.Clear();
        _case = index;
        _frame = 0;
        _time = 0f;
        var current = _cases[index];
        _monster = _monsters.Compose(_assets, current.Model, World(MonsterPosition, Facing, current.Scale), MotionCode.Stand).Handle;
        _warmup = 30;
    }

    private void StartClip()
    {
        var current = _cases[_case];
        _monsterEffects.ObserveSwing(MonsterId, PlayerId);
        if (current.Status != 0)
        {
            var control = current.Status == MonsterStatuses.Stun ? ControlFlags.Stun : ControlFlags.None;
            var status = new StatusEffectInfo(PlayerId, MonsterId, current.Status, StatusEffectOp.Add, 1, (byte)EffectSourceKind.MonsterSkill, (byte)control,
                StatusEffectFlags.Harmful, 10000, 10000, 5);
            _presentation.Observe(new PresenceUpdate(0, [], [], [], []) { StatusEffects = [status] }, 0d);
            return;
        }

        _renderer.SetActorMotionIndex(_monster, current.Slot);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_case < 0)
        {
            return;
        }

        var current = _cases[_case];
        var eye = new Vector3(-190, 170, 300) * current.Distance;
        if (_warmup > 0)
        {
            _warmup--;
            Advance(Step, current);
            _camera.SetScriptedView(GraphicsDevice, eye, new Vector3(10, 40, -40));
            GraphicsDevice.SetRenderTarget(_target);
            _renderer.Draw(_camera);
            _renderer.ComposeScene();
            GraphicsDevice.SetRenderTarget(null);
            if (_warmup == 0)
            {
                StartClip();
            }

            return;
        }

        var targetTime = _frame == 0 ? 0f : current.Times[_frame - 1];
        while (_time < targetTime)
        {
            _time += Step;
            Advance(Step, current);
        }

        _camera.SetScriptedView(GraphicsDevice, eye, new Vector3(10, 40, -40));
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        _renderer.ResetTemporalState();
        _renderer.Draw(_camera);
        _presentation.Draw(_camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[960 * 540];
        _target.GetData(pixels);
        var name = $"{current.Name}-{(_frame == 0 ? "before" : targetTime.ToString("0.00", CultureInfo.InvariantCulture))}";
        if (_frame == 0)
        {
            _baseline = pixels;
        }
        else
        {
            var brighter = 0;
            for (var index = 0; index < pixels.Length; index++)
            {
                var now = pixels[index];
                var before = _baseline[index];
                if (Math.Abs(now.R + now.G + now.B - before.R - before.G - before.B) > 45)
                {
                    brighter++;
                }
            }

            Console.WriteLine($"{name}: {brighter} changed pixels, {_presentation.System.ParticleCount} particles, {_presentation.Renderer.LastQuadCount} quads");
            _brightest = Math.Max(_brightest, brighter);
        }

        var path = Path.Combine(_output, name + ".png");
        using (var stream = File.Create(path))
        {
            _target.SaveAsPng(stream, 960, 540);
        }

        if (_frame > 0)
        {
            GraphicsDevice.SetRenderTarget(_target);
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(18, 18, 22).ToVector4(), PristonDepth.Clear, 0);
            _presentation.Renderer.DisplayOutput = true;
            _presentation.Draw(_camera.View, _camera.Projection, _camera.Position, null!);
            _presentation.Renderer.DisplayOutput = false;
            GraphicsDevice.SetRenderTarget(null);
            using var dark = File.Create(Path.Combine(_output, name + ".dark.png"));
            _target.SaveAsPng(dark, 960, 540);
        }

        _frame++;
        if (_frame <= current.Times.Length)
        {
            return;
        }

        if (_brightest < 150)
        {
            _failures.Add($"{current.Name} showed nothing ({_brightest} changed pixels at most)");
        }

        _brightest = 0;
        if (_case + 1 < _cases.Length)
        {
            Begin(_case + 1);
            return;
        }

        Assert.True(_failures.Count == 0, string.Join("; ", _failures));
        Exit();
    }

    private void Advance(float seconds, Case current)
    {
        _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));
        _views.Clear();
        _views.Add(new MonsterActorView(MonsterId, _monster, current.Sound, current.Level, current.Scale, MonsterPosition, Facing, false));
        _monsterEffects.Update(_views, seconds);
        _presentation.Update(seconds, _time * 1000d);
    }

    public bool TryGetPosition(ulong entityId, out Vector3 position)
    {
        position = entityId == MonsterId ? MonsterPosition : PlayerPosition;
        return entityId is MonsterId or PlayerId;
    }

    public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
    {
        var found = TryGetBoneWorld(entityId, bone, out var world);
        position = world.Translation;
        return found;
    }

    public bool TryGetBoneWorld(ulong entityId, string bone, out Matrix world) =>
        _renderer.TryGetActorBoneWorld(entityId == MonsterId ? _monster : _player.Body, bone, out world);

    public float AttackRateOf(ulong entityId) => 1f;

    public void PlaySkillMotion(ulong entityId, SkillMotionPlan plan, float durationMs, float rate, int loop)
    {
    }

    public bool TryGetActorMotion(ActorHandle actor, out PristonMotionInfo motion, out float ticks) => _renderer.TryGetActorMotion(actor, out motion, out ticks);

    public void SetEntityCorpse(ulong entityId, float fade, float dim)
    {
    }

    public void SetEntityTint(ulong entityId, Vector3 tint)
    {
        if (entityId == PlayerId)
        {
            _renderer.SetActorColorAdd(_player.Body, tint);
            _renderer.SetActorColorAdd(_player.Head, tint);
        }
    }

    protected override void UnloadContent()
    {
        _presentation.Dispose();
        _renderer.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
