using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Casts skills on a lit stage through the client's skill presentation, with the timeline the server schedules, and
// saves the frames (--capture-skills <assets> <content> <out> [hex skill codes, comma separated]). Each frame is checked for the cast's
// motion clip and for effect light against the frame before the cast.
internal sealed class SkillCaptureProbe : Game, ISkillSceneHost
{
    private const ulong CasterId = 1;
    private const ulong TargetId = 2;
    private const float Step = 1f / 120f;

    private static readonly (PlayerClass Class, string Weapon, string OffHand, uint Skill, float[] Times)[] DefaultCasts =
    [
        (PlayerClass.Magician, "WM101", "", 0x08010004, [0.35f, 0.75f, 1.05f, 1.35f]),
        (PlayerClass.Priestess, "WM101", "", 0x07010001, [0.3f, 0.9f, 1.4f]),
        (PlayerClass.Pikeman, "WP101", "", 0x03010001, [0.6f, 0.8f, 1.1f]),
        (PlayerClass.Mechanician, "WC101", "DS101", 0x01010001, [0.3f, 0.6f, 3f]),
        (PlayerClass.Fighter, "WA101", "", 0x02010004, [0.4f, 0.85f, 1.3f])
    ];

    private static readonly (PlayerClass Class, string Weapon, string OffHand)[] Equipment =
    [
        (PlayerClass.Mechanician, "WC101", "DS101"), (PlayerClass.Fighter, "WA101", ""), (PlayerClass.Pikeman, "WP101", ""),
        (PlayerClass.Archer, "WS101", ""), (PlayerClass.Knight, "WS201", "DS101"), (PlayerClass.Atalanta, "WT101", "DS101"),
        (PlayerClass.Priestess, "WM101", ""), (PlayerClass.Magician, "WM101", "")
    ];

    // The classes outside the first eight groups, by their skill code's high byte.
    private static readonly Dictionary<uint, (PlayerClass Class, string Weapon, string OffHand)> LaterClasses = new()
    {
        [0x10] = (PlayerClass.Assassin, "WD101", ""), [0x50] = (PlayerClass.Shaman, "WN101", "")
    };

    private readonly (PlayerClass Class, string Weapon, string OffHand, uint Skill, float[] Times)[] Casts;
    private readonly string _assets;
    private readonly string _output;
    private readonly List<string> _failures = [];
    private PristonRenderer _renderer = null!;
    private PlayerActorComposer _composer = null!;
    private ActorLookPresenter _looks = null!;
    private EffectLibrary _library = null!;
    private SkillPresentation _presentation = null!;
    private PristonCamera _camera = null!;
    private RenderTarget2D _target = null!;
    private Task _preparation = Task.CompletedTask;
    private readonly List<CharacterVisualSet> _visuals = [];
    private PlayerActorHandles _caster = new(ActorHandle.None, ActorHandle.None);
    private PlayerActorHandles _dummy = new(ActorHandle.None, ActorHandle.None);
    private int _cast = -1;
    private int _frame;
    private float _time;
    private double _clockMs;
    private Color[] _baseline = [];
    private int _motionsPlayed;
    private int _warmup;
    private int _brightest;

    public SkillCaptureProbe(string assets, string content, string output, IReadOnlyList<uint>? skills = null)
    {
        Casts = skills is null ? DefaultCasts : skills.Select(Cast).ToArray();
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

    public ulong SelfId => CasterId;

    public Vector3 ListenerPosition => Vector3.Zero;

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        _library = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assets, ConsoleClientLog.Instance);
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = true, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(17.5f);
        var material = new PristonMaterial(true, 1, 0, 1, true, new Vector3(0.08f, 0.08f, 0.09f), 0, 1, PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var floor = new Color(26, 25, 23);
        var stage = new PristonStage("skill-fixture.smd", [material],
            [new(new Vector3(-600, 0, -600), floor), new(new Vector3(-600, 0, 600), floor), new(new Vector3(600, 0, 600), floor),
             new(new Vector3(600, 0, -600), floor)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)], [], [], new BoundingBox(new Vector3(-600, 0, -600), new Vector3(600, 0, 600)));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        _composer = new PlayerActorComposer(new RendererActorScene(_renderer), new PristonActorLoader());
        _looks = new ActorLookPresenter(new RendererActorScene(_renderer), new PristonActorLoader(), _assets);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings { FieldOfViewDegrees = 42 });
        _presentation = new SkillPresentation(GraphicsDevice, Content, _library, _renderer.TransientLights, this,
            new SkillSoundBank(_assets, ConsoleClientLog.Instance) { Enabled = false }, ConsoleClientLog.Instance);
        _library.Vfx.CompileAll();
        var registry = new CharacterClassRegistry();
        var pending = new List<Task>();
        foreach (var cast in Casts)
        {
            var definition = registry.Get(cast.Class).Definition;
            var visuals = EquipmentVisuals.Apply(definition.DefaultVisuals, "DA108", cast.Weapon, cast.OffHand, false, definition.MotionJobMask);
            _visuals.Add(visuals);
            pending.Add(_composer.PrepareAsync(_assets, visuals, 0));
        }

        var dummy = registry.Get(PlayerClass.Knight).Definition;
        _visuals.Add(EquipmentVisuals.Apply(dummy.DefaultVisuals, "DA108", "WS201", "DS101", false, dummy.MotionJobMask));
        pending.Add(_composer.PrepareAsync(_assets, _visuals[^1], 0));
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
            throw new InvalidOperationException("Skill capture assets failed to prepare.", _preparation.Exception);
        }

        if (_cast < 0)
        {
            _dummy = _composer.Compose(_assets, _visuals[^1], 0, World(TargetPosition, MathHelper.Pi), MotionCode.Stand);
            BeginCast(0);
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_cast < 0)
        {
            return;
        }

        if (_warmup > 0)
        {
            // Composed actors upload their textures over the first frames; render them before the cast starts.
            _warmup--;
            _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step)));
            _camera.SetScriptedView(GraphicsDevice, new Vector3(-170, 150, 260), new Vector3(10, 40, -40));
            GraphicsDevice.SetRenderTarget(_target);
            _renderer.Draw(_camera);
            _renderer.ComposeScene();
            GraphicsDevice.SetRenderTarget(null);
            if (_warmup == 0)
            {
                StartTimeline();
            }

            return;
        }

        var (playerClass, _, _, skill, times) = Casts[_cast];
        var targetTime = _frame == 0 ? 0f : times[_frame - 1];
        while (_time < targetTime)
        {
            _time += Step;
            _clockMs += Step * 1000d;
            DispatchScheduled();
            _presentation.Update(Step, _clockMs);
            _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step)));
        }

        if (_frame == 0)
        {
            for (var settle = 0; settle < 10; settle++)
            {
                _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step)));
            }
        }

        _camera.SetScriptedView(GraphicsDevice, new Vector3(-170, 150, 260), new Vector3(10, 40, -40));
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        // Captures jump between moments; snap the exposure and drop the temporal history each time.
        _renderer.ResetTemporalState();
        _renderer.Draw(_camera);
        _presentation.Draw(_camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[960 * 540];
        _target.GetData(pixels);
        SkillRuleCatalog.TryGet(skill, out var rule);
        var name = $"{playerClass}-{rule.Name.Replace(' ', '-')}-{(_frame == 0 ? "before" : targetTime.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))}";
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
                if (now.R + now.G + now.B > before.R + before.G + before.B + 45)
                {
                    brighter++;
                }
            }

            Console.WriteLine($"{name}: {brighter} brighter pixels, {_presentation.System.ParticleCount} particles, {Renderer().LastDrawCalls} draws");
            _brightest = Math.Max(_brightest, brighter);
        }

        string path = Path.Combine(_output, name + ".png");
        using (var stream = File.Create(path))
        {
            _target.SaveAsPng(stream, 960, 540);
        }

        if (_frame > 0)
        {
            // The effects alone on a dark backdrop: the exposed stage hides additive light.
            GraphicsDevice.SetRenderTarget(_target);
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(18, 18, 22).ToVector4(), PristonDepth.Clear, 0);
            Renderer().DisplayOutput = true;
            _presentation.Draw(_camera.View, _camera.Projection, _camera.Position, null!);
            Renderer().DisplayOutput = false;
            GraphicsDevice.SetRenderTarget(null);
            using (var dark = File.Create(Path.Combine(_output, name + ".dark.png")))
            {
                _target.SaveAsPng(dark, 960, 540);
            }

            SaveActorView(name);
        }

        Console.WriteLine(path);
        _frame++;
        if (_frame > times.Length)
        {
            if (_brightest < 150)
            {
                _failures.Add($"{name} cast showed no effect ({_brightest} brighter pixels at most)");
            }

            _brightest = 0;
            if (_cast + 1 < Casts.Length)
            {
                BeginCast(_cast + 1);
            }
            else
            {
                Assert.True(_motionsPlayed >= Casts.Length, "a cast did not start its motion");
                Assert.True(_failures.Count == 0, string.Join("; ", _failures));
                Exit();
            }
        }
    }

    private VfxRenderer Renderer() => _presentation.Renderer;

    // The caster up close at noon without the effects (<name>.actor.png): skill looks on the actors themselves (tints,
    // translucency, afterimages, shield swaps, enlarged weapons).
    private void SaveActorView(string name)
    {
        _renderer.SetTimeOfDay(12f);
        _renderer.ExposureCompensation = 2.5f;
        _camera.SetScriptedView(GraphicsDevice, new Vector3(95, 55, 150), new Vector3(10, 28, -20));
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        _renderer.ResetTemporalState();
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        using (var actor = File.Create(Path.Combine(_output, name + ".actor.png")))
        {
            _target.SaveAsPng(actor, 960, 540);
        }

        _renderer.ExposureCompensation = 0f;
        _renderer.SetTimeOfDay(17.5f);
    }

    // The skill code's high byte is its class group (1 Mechanician .. 8 Magician, then LaterClasses).
    private static (PlayerClass, string, string, uint, float[]) Cast(uint skill)
    {
        var (playerClass, weapon, offHand) = LaterClasses.TryGetValue(skill >> 24, out var later) ? later : Equipment[(int)(skill >> 24) - 1];
        return (playerClass, weapon, offHand, skill, [0.4f, 0.8f, 1.2f, 1.7f, 2.4f, 4f]);
    }

    private static readonly Vector3 CasterPosition = new(-40, 0, 40);
    private static readonly Vector3 TargetPosition = new(60, 0, -80);

    private static Matrix World(Vector3 position, float yaw) => Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(position);

    private void BeginCast(int index)
    {
        if (_caster.Body.IsValid)
        {
            _caster.Remove(new RendererActorScene(_renderer));
        }

        _presentation.Clear();
        _looks.Clear();
        _renderer.TransientLights.Clear();
        _cast = index;
        _frame = 0;
        _time = 0f;
        var facing = MathF.Atan2(TargetPosition.X - CasterPosition.X, TargetPosition.Z - CasterPosition.Z);
        _caster = _composer.Compose(_assets, _visuals[index], 0, World(CasterPosition, facing), MotionCode.Stand);
        _warmup = 30;
    }

    private void StartTimeline()
    {
        var (_, _, _, skill, _) = Casts[_cast];
        SkillRuleCatalog.TryGet(skill, out var rule);
        var timing = SkillFormulas.Timing(rule, 5, 1f, (40 * 33f, 22 * 33f));
        var self = rule.Target is SkillTarget.Self or SkillTarget.None || rule.Kind == SkillKind.Heal;
        var target = self ? CasterId : TargetId;
        var point = self ? CasterPosition : TargetPosition;
        var transport = new System.Numerics.Vector3(point.X, point.Y, -point.Z);
        var serverFacing = MathF.Atan2(TargetPosition.X - CasterPosition.X, -(TargetPosition.Z - CasterPosition.Z));
        var start = new SkillEventInfo(CasterId, target, skill, SkillEventPhase.Start, 5, 0, timing.DurationMs, timing.HitsAtMs[0],
            transport, serverFacing, 0, rule.At(rule.Radius, 5));
        var impactAt = timing.HitsAtMs[0] + (rule.Projectile ? SkillProjectile.FlightMs(Vector3.Distance(CasterPosition, TargetPosition)) : 0f);
        var impact = start with { Phase = SkillEventPhase.Impact, TargetCount = 1 };
        _presentation.Observe(new PresenceUpdate(0, [], [], [], []) { SkillEvents = [start] }, _clockMs);
        _scheduled = (impact, _clockMs + impactAt);
        if (rule.Kind is SkillKind.SelfBuff)
        {
            var status = new StatusEffectInfo(CasterId, CasterId, skill, StatusEffectOp.Add, 1, 2, 0, 0, 60000, 60000, 5);
            _scheduledStatus = (status, _clockMs + timing.HitsAtMs[0]);
        }
        else if (rule.Buff is { Harmful: true } || rule.StatusLook is SkillLook.Frost or SkillLook.Poison or SkillLook.Distortion)
        {
            // A harmful status (a slow, a poison, a curse) lands on the dummy with the impact.
            var status = new StatusEffectInfo(TargetId, CasterId, skill, StatusEffectOp.Add, 1, 2, 0, (ushort)StatusEffectFlags.Harmful, 60000, 60000, 5);
            _scheduledStatus = (status, _clockMs + impactAt);
        }
    }

    private (SkillEventInfo Event, double AtMs)? _scheduled;
    private (StatusEffectInfo Status, double AtMs)? _scheduledStatus;

    private void DispatchScheduled()
    {
        if (_scheduled is { } scheduled && _clockMs >= scheduled.AtMs)
        {
            _scheduled = null;
            _presentation.Observe(new PresenceUpdate(0, [], [], [], []) { SkillEvents = [scheduled.Event] }, _clockMs);
        }

        if (_scheduledStatus is { } status && _clockMs >= status.AtMs)
        {
            _scheduledStatus = null;
            _presentation.Observe(new PresenceUpdate(0, [], [], [], []) { StatusEffects = [status.Status] }, _clockMs);
        }
    }

    public bool TryGetPosition(ulong entityId, out Vector3 position)
    {
        position = entityId == CasterId ? CasterPosition : TargetPosition;
        return entityId is CasterId or TargetId;
    }

    public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
    {
        var found = TryGetBoneWorld(entityId, bone, out var world);
        position = world.Translation;
        return found;
    }

    public bool TryGetBoneWorld(ulong entityId, string bone, out Matrix world)
    {
        var actor = entityId == CasterId ? _caster.Body : _dummy.Body;
        return _renderer.TryGetActorBoneWorld(actor, bone, out world);
    }

    public void ApplyLook(ulong entityId, in EntityLook look)
    {
        var handles = entityId == CasterId ? _caster : _dummy;
        _looks.Apply(entityId, handles, Casts[_cast].OffHand.StartsWith("DS", StringComparison.Ordinal) && entityId == CasterId, look);
    }

    public float AttackRateOf(ulong entityId) => 1f;

    public void PlaySkillMotion(ulong entityId, SkillMotionPlan plan, float durationMs, float rate, int loop)
    {
        if (entityId != CasterId)
        {
            return;
        }

        _motionsPlayed++;
        if (plan.Motion == SkillMotion.Skill)
        {
            _renderer.SetActorMotionIndex(_caster.Body, plan.MotionIndex);
            _renderer.SetActorMotionIndex(_caster.Head, plan.MotionIndex);
            _renderer.SetActorPlaybackRate(_caster.Body, rate);
            _renderer.SetActorPlaybackRate(_caster.Head, rate);
            return;
        }

        _renderer.RestartActorMotion(_caster.Body, MotionCode.Attack);
        _renderer.RestartActorMotion(_caster.Head, MotionCode.Attack);
        _renderer.SetActorPlaybackRate(_caster.Body, rate);
        _renderer.SetActorPlaybackRate(_caster.Head, rate);
    }

    protected override void UnloadContent()
    {
        _presentation.Dispose();
        _renderer.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
