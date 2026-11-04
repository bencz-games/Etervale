using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Weather;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Assets;

namespace PristonTale.Tests;

// Ricarten under each weather at its start point: clear, overcast and light, heavy and storm rain by day, a lightning
// flash, rain at dusk and at night, and the camera close under the eaves: the sky, the light, the fog, the streaks
// and their splashes, the roofs keeping the rain off. Then the realistic sky looking toward the horizon: noon, a broken
// afternoon, sunset, the stars at dusk, a full and a new moon, an overcast and a stormy sky, a rainbow, the aurora and
// the classic dome; the clouds' shadows on the town and a morning mist, also over the harbour toward the sunrise. At
// 1920x1080, after two seconds.
internal sealed class WeatherCaptureProbe : Game
{
    private sealed record Shot(string Name, float Hour, PristonWeather Weather, bool Close = false, bool Sky = false, float Moon = 0.5f,
        bool Aurora = false, bool Classic = false, float Heading = 0f, bool Mist = false, int Frames = 1, Vector2? At = null);

    private static readonly Vector2 Breeze = new(60f, 20f);

    // The harbour square (world x, z) by the sea, where the morning sun rises over the water and the far quays.
    private static readonly Vector2 Harbour = new(214f, 17618f);

    private static readonly Shot[] Shots =
    [
        new("clear", 13f, PristonWeather.Clear),
        new("overcast", 13f, new PristonWeather(1f, 0f, 0f, Breeze)),
        new("light-rain", 13f, new PristonWeather(1f, 96f / 255f, 0f, Breeze)),
        new("heavy-rain", 13f, new PristonWeather(1f, 224f / 255f, 0f, Breeze * 2f)),
        new("storm", 13f, new PristonWeather(1f, 1f, 0f, new Vector2(220f, 90f))),
        new("lightning", 13f, new PristonWeather(1f, 1f, 1f, new Vector2(220f, 90f))),
        new("dusk-rain", 22.0f, new PristonWeather(1f, 0.7f, 0f, Breeze)),
        new("night-rain", 23.5f, new PristonWeather(1f, 0.85f, 0f, Breeze)),
        new("close-rain", 13f, new PristonWeather(1f, 0.85f, 0f, Breeze), Close: true),
        new("cloud-shadows", 12.5f, new PristonWeather(0.35f, 0f, 0f, Breeze * 4f)),
        new("morning-mist", 4.5f, PristonWeather.Clear, Mist: true),
        new("sky-noon", 12f, PristonWeather.Clear, Sky: true),
        new("sky-sunrise", 4.8f, PristonWeather.Clear, Sky: true, Heading: 1.6f),
        new("sky-sunrise-mist", 4.6f, PristonWeather.Clear, Sky: true, Heading: 1.6f, Mist: true),
        new("sky-afternoon", 17.8f, new PristonWeather(0.45f, 0f, 0f, Breeze), Sky: true, Heading: 1.2f),
        new("sky-sunset", 21.8f, new PristonWeather(0.2f, 0f, 0f, Breeze), Sky: true, Heading: -1.6f),
        new("sky-dusk", 22.7f, PristonWeather.Clear, Sky: true, Heading: -1.6f),
        new("sky-full-moon", 23f, new PristonWeather(0.2f, 0f, 0f, Breeze), Sky: true, Heading: 3.9f),
        new("sky-new-moon", 1f, PristonWeather.Clear, Sky: true, Moon: 0.08f, Heading: 3.9f),
        new("sky-overcast", 11f, new PristonWeather(1f, 0f, 0f, Breeze), Sky: true),
        new("sky-storm", 14f, new PristonWeather(1f, 1f, 0f, Breeze * 3f), Sky: true),
        new("sky-night-storm", 23f, new PristonWeather(1f, 0.88f, 0f, Breeze * 3f), Sky: true, Heading: 3.9f),
        new("sky-dawn-storm", 3.95f, new PristonWeather(1f, 0.88f, 0f, Breeze * 3f), Sky: true, Heading: 1.6f),
        new("sky-lightning", 14f, new PristonWeather(1f, 1f, 1f, Breeze * 3f, 0.4f), Sky: true),
        new("sky-rainbow", 19.6f, new PristonWeather(0.25f, 0.2f, 0f, Breeze), Sky: true, Heading: 1.6f),
        new("sky-aurora", 0.5f, PristonWeather.Clear, Sky: true, Aurora: true, Heading: 0.6f),
        new("sky-classic", 12f, PristonWeather.Clear, Sky: true, Classic: true),
        // The classic rain dome is an open cylinder: above its rim the clouds must go on, not the hour's colour.
        new("sky-classic-rain-noon", 12f, new PristonWeather(1f, 0.7f, 0f, Breeze), Sky: true, Classic: true),
        new("sky-classic-rain", 17f, new PristonWeather(1f, 0.7f, 0f, Breeze), Sky: true, Classic: true),
        new("sky-classic-rain-dusk", 21.5f, new PristonWeather(1f, 0.7f, 0f, Breeze), Sky: true, Classic: true),
        new("sky-classic-rain-night", 0.5f, new PristonWeather(1f, 0.7f, 0f, Breeze), Sky: true, Classic: true),
        new("mist-harbour", 4.6f, PristonWeather.Clear, Mist: true, Heading: 1.57f, At: Harbour),
        // A storm as the day breaks in the morning mist: the deck and the mist stay grey, not lit orange by the low sun.
        new("storm-dawn-mist", 5.3f, new PristonWeather(1f, 0.88f, 0f, Breeze * 3f), Mist: true),
        // Broken clouds in a strong wind over consecutive frames: their edges must glide, not waver.
        new("sky-drift", 14f, new PristonWeather(0.45f, 0f, 0f, new Vector2(360f, 0f)), Sky: true, Heading: 1.2f, Frames: 8)
    ];

    private const int Width = 1920;
    private const int Height = 1080;
    private const float Step = 1f / 60f;
    private const double SettleSeconds = 2.0;

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonRain _rain = null!;
    private PristonCamera _camera = null!;
    private PristonCollisionWorld _collision = null!;
    private RenderTarget2D _target = null!;
    private Color[] _pixels = new Color[Width * Height];
    private Color[]? _previous;
    private int _frame;
    private Vector3 _focus;
    private int _shot;
    private double _seconds;
    private double _shotSeconds;

    public WeatherCaptureProbe(string assetRoot, string contentRoot, string output)
    {
        _assetRoot = assetRoot;
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Width,
            PreferredBackBufferHeight = Height,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        var field = new WorldCatalog().AllFields.Single(candidate => candidate.FieldCode == FieldCodes.Ricarten);
        _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _assetRoot);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _presentation) { Sky = _sky, DayLengthSeconds = 0, FieldSky = true };
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day),
            PristonSkyProfiles.Resolve(field.Backdrop.Glow), PristonSkyProfiles.Resolve(field.Backdrop.Night));
        var reader = new PristonSmdReader();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(stage, models);
        _collision = new PristonCollisionWorld(stage);
        var start = field.StartPoints[0];
        _focus = Ground(new Vector2(start.X, -start.Z));
        _rain = new PristonRain(GraphicsDevice, Content);
        _target = new RenderTarget2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        _rain.Occlusion.Reset(_collision);
        _presentation.ApplyRendering(new ClientSettings());
    }

    protected override void Update(GameTime gameTime)
    {
        if (_shot >= Shots.Length)
        {
            Exit();
            return;
        }

        var shot = Shots[_shot];
        _renderer.SetTimeOfDay(shot.Hour);
        _renderer.Weather = shot.Weather;
        _renderer.MoonPhase = shot.Moon;
        _renderer.Aurora = shot.Aurora;
        _renderer.MorningMist = shot.Mist ? RicartenField.Definition.MorningMistStrength : 0f;
        if (_renderer.RealisticSky == shot.Classic)
        {
            _presentation.ApplyRendering(new ClientSettings { RealisticSky = !shot.Classic });
        }

        _seconds += Step;
        _shotSeconds += Step;
        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(Step)));
        _rain.Update(Eye(shot), _focus, Step, shot.Weather.Rain);
    }

    private Vector3 Ground(Vector2 at) => _collision.TryGetFloorHeight(new Vector3(at.X, 5000f, at.Y), out var height)
        ? new Vector3(at.X, height, at.Y) : new Vector3(at.X, 0f, at.Y);

    private static Vector3 Toward(Shot shot) => new(MathF.Sin(shot.Heading), 0f, MathF.Cos(shot.Heading));

    private Vector3 Eye(Shot shot) => shot.At is { } at ? Ground(at) - Toward(shot) * 260f + Vector3.Up * 170f
        : shot.Sky ? _focus + new Vector3(0f, 60f, 0f)
        : shot.Close ? _focus + new Vector3(40f, 55f, 90f) : _focus + new Vector3(160f, 170f, 360f);

    // Sky shots look toward the horizon along the shot's heading, 14 degrees up.
    private Vector3 Target(Shot shot) => shot.At is { } at ? Ground(at) + Toward(shot) * 500f
        : shot.Sky
        ? Eye(shot) + new Vector3(MathF.Sin(shot.Heading), 0.25f, MathF.Cos(shot.Heading)) * 100f
        : _focus + Vector3.Up * 30f;

    protected override void Draw(GameTime gameTime)
    {
        if (_shot >= Shots.Length)
        {
            return;
        }

        var shot = Shots[_shot];
        GraphicsDevice.SetRenderTarget(_target);
        var eye = Eye(shot);
        _camera ??= new PristonCamera(eye, Vector3.Forward);
        _camera.SetScriptedView(GraphicsDevice, eye, Target(shot));
        _renderer.Draw(_camera);
        _rain.Draw(_camera.View, _camera.Projection, eye, shot.Weather, _renderer.RainRadiance);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        if (_shotSeconds < SettleSeconds)
        {
            return;
        }

        var pixels = _pixels;
        _target.GetData(pixels);
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index].A = 255;
        }

        using var opaque = new Texture2D(GraphicsDevice, Width, Height);
        opaque.SetData(pixels);
        using (var stream = File.Create(Path.Combine(_output, shot.Frames > 1 ? $"{shot.Name}-{_frame}.png" : $"{shot.Name}.png")))
        {
            opaque.SaveAsPng(stream, Width, Height);
        }

        // Consecutive frames of a moving sky: the change from the previous frame, eight times brighter, and the pixels
        // that changed by more than 6 levels; a steady drift moves edges a little each frame, a wavering one flickers
        // over the clouds' bodies too.
        if (_previous is not null)
        {
            var changed = 0;
            var difference = new Color[pixels.Length];
            for (var index = 0; index < pixels.Length; index++)
            {
                var delta = Math.Max(Math.Abs(pixels[index].R - _previous[index].R),
                    Math.Max(Math.Abs(pixels[index].G - _previous[index].G), Math.Abs(pixels[index].B - _previous[index].B)));
                changed += delta > 6 ? 1 : 0;
                var shade = (byte)Math.Min(255, delta * 8);
                difference[index] = new Color(shade, shade, shade, (byte)255);
            }

            opaque.SetData(difference);
            using var change = File.Create(Path.Combine(_output, $"{shot.Name}-{_frame}-change.png"));
            opaque.SaveAsPng(change, Width, Height);
            Console.WriteLine($"Weather {shot.Name}-{_frame}: {changed} pixels changed from the previous frame");
        }

        Console.WriteLine($"Weather {shot.Name}: captured");
        if (++_frame < shot.Frames)
        {
            (_previous, _pixels) = (pixels, _previous ?? new Color[Width * Height]);
            return;
        }

        _previous = null;
        _frame = 0;
        _shotSeconds = 0;
        _shot++;
    }

    protected override void UnloadContent()
    {
        _rain.Dispose();
        _target.Dispose();
        _sky.Dispose();
    }

    private string Resolve(string relative) => AssetFiles.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
