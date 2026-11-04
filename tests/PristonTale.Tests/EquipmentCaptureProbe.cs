using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal sealed class EquipmentCaptureProbe : Game
{
    private readonly string _root;
    private readonly string _output;
    private readonly bool _ranged;
    private PristonRenderer _renderer = null!;
    private PlayerActorComposer _composer = null!;
    private PristonCamera _camera = null!;
    private Task _preparation = Task.CompletedTask;
    private readonly List<(CharacterVisualSet Visuals, Matrix World)> _avatars = [];
    private readonly List<PlayerActorHandles> _handles = [];
    private int _published;
    private int _frames;
    private int _phase;

    public EquipmentCaptureProbe(string root, string content, string output, bool ranged = false)
    {
        _ranged = ranged;
        _root = root;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = false, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(12);
        var scene = new RendererActorScene(_renderer);
        _composer = new PlayerActorComposer(scene, new PristonActorLoader());
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings { FieldOfViewDegrees = 38 });
        var registry = new CharacterClassRegistry();
        string[] weapons = ["WA101", "WC101", "WS101", "WP101", "WT101", "WS201", "WM101", "WM101", "WD101", "WN101"];
        if (_ranged)
        {
            weapons = ["WS110", "WS101", "WS104"];
        }

        var pending = new List<Task>();
        for (int index = 0; index < weapons.Length; index++)
        {
            var job = _ranged ? PlayerClass.Mechanician : (PlayerClass)(index + 1);
            var definition = registry.Get(job).Definition;
            var visuals = EquipmentVisuals.Apply(definition.DefaultVisuals, _ranged ? "DA119" : "DA108", weapons[index],
                !_ranged && index is 0 or 1 or 4 or 5 ? "DS101" : "", false, definition.MotionJobMask);
            var position = _ranged ? new Vector3((index - 1) * 65, 0, 0) : new Vector3((index % 4 - 1.5f) * 90, 0, index / 4 * 110);
            var world = Matrix.CreateTranslation(position);
            _avatars.Add((visuals, world));
            pending.Add(_composer.PrepareAsync(_root, visuals, 0));
        }

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
            throw new InvalidOperationException("Equipment asset preparation failed.", _preparation.Exception);
        }

        if (_published < _avatars.Count)
        {
            var avatar = _avatars[_published++];
            var motion = _phase switch
            {
                2 => MotionCode.Attack,
                3 when _ranged => MotionCode.Walk,
                4 when _ranged => MotionCode.Run,
                _ => MotionCode.Stand
            };
            _handles.Add(_composer.Compose(_root, avatar.Visuals, 0, avatar.World, motion));
            return;
        }

        _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 60)));
        _frames++;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frames < 24)
        {
            return;
        }

        using var target = new RenderTarget2D(GraphicsDevice, 1280, 720, false, SurfaceFormat.Color, DepthFormat.Depth24);
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(42, 48, 54));
        if (_ranged)
        {
            _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 90, 250), new Vector3(0, 32, 0));
        }
        else
        {
            _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 220, 470), new Vector3(0, 32, 55));
        }
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        string path = Path.Combine(_output, $"equipment-{_phase}.png");
        using (var stream = File.Create(path))
        {
            target.SaveAsPng(stream, 1280, 720);
        }

        Console.WriteLine(path);
        if (++_phase == (_ranged ? 6 : 4))
        {
            Exit();
            return;
        }

        _frames = 0;
        var scene = new RendererActorScene(_renderer);
        foreach (var handles in _handles)
        {
            handles.Remove(scene);
        }

        _handles.Clear();
        _published = 0;
        var pending = new List<Task>();
        for (int index = 0; index < _avatars.Count; index++)
        {
            var avatar = _avatars[index];
            var visuals = avatar.Visuals with { InVillage = _phase == 1 };
            if (_phase == (_ranged ? 5 : 3))
            {
                var job = _ranged ? PlayerClass.Mechanician : (PlayerClass)(index + 1);
                var definition = new CharacterClassRegistry().Get(job).Definition;
                visuals = EquipmentVisuals.Apply(definition.DefaultVisuals, "", "", "", false, definition.MotionJobMask);
            }

            _avatars[index] = (visuals, avatar.World);
            pending.Add(_composer.PrepareAsync(_root, visuals, 0));
        }

        _preparation = Task.WhenAll(pending);
    }

    protected override void UnloadContent() => _renderer.Dispose();
}
