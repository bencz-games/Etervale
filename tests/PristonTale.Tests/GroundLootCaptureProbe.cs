using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

internal sealed class GroundLootCaptureProbe : Game
{
    private readonly string _root;
    private readonly string _output;
    private PristonRenderer _renderer = null!;
    private GroundLootPresentation _ground = null!;
    private PristonCamera _camera = null!;
    private SpriteBatch _batch = null!;
    private SpriteFont _font = null!;
    private GroundDropView[] _drops = [];
    private int _frames;
    private HudSprites _sprites = null!;
    private HudPainter _painter = null!;
    private readonly List<GroundLabel> _labels = [];
    private readonly HudGroundLabels _labelBoxes = new();

    public GroundLootCaptureProbe(string root, string content, string output)
    {
        _root = root;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
    }

    protected override void LoadContent()
    {
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = false, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(12);
        _ground = new GroundLootPresentation(new RendererActorScene(_renderer), new PristonActorLoader(), _root, ConsoleClientLog.Instance);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings());
        _batch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/UI");
        _sprites = new HudSprites(GraphicsDevice, _root, ConsoleClientLog.Instance);
        _painter = new HudPainter(_batch, _font, _sprites);
        _drops =
        [
            Drop("GG101", "Gold", 13, 0),
            Drop("GG101", "Gold", 15, 0),
            Drop("WA101", "Stone Axe", 1, -60),
            Drop("WS101", "Short Sword", 1, -120),
            Drop("WP101", "Wooden Spear", 1, 120),
            Drop("PL101", "Mini Life Potion", 1, 60)
        ];
        _ground.Apply(new PresenceUpdate(1, [], [], [], []) { GroundAdded = _drops });
        _ground.Update(MissingFloor);
        Assert.True(!_ground.TryGet(_drops[0].Id, out _), "drop published without a resident floor");
        _ground.InvalidateFloor();
    }

    private static GroundDropView Drop(string key, string name, int amount, float x) =>
        new(Guid.NewGuid(), 0, key, name, amount, key == "GG101", new System.Numerics.Vector3(x, 0, -20));

    private static bool MissingFloor(float x, float reference, float z, out float height)
    {
        height = 0;
        return false;
    }

    private static bool Floor(float x, float reference, float z, out float height)
    {
        height = 50;
        return true;
    }

    private static bool TraversalFloor(Vector3 point, float reference, out float height) => Floor(point.X, reference, point.Z, out height);

    protected override void Update(GameTime gameTime)
    {
        _ground.Update(Floor);
        _renderer.Update(gameTime);
        _frames++;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frames < 180)
        {
            return;
        }

        using var target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(42, 48, 54));
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 195, 230), new Vector3(0, 50, 20));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        DrawLabels(_drops[3].Id, showAll: true);
        DrawBoxSamples();
        foreach (var drop in _drops)
        {
            Assert.True(_ground.TryGet(drop.Id, out var position), "resident drop has no floor position");
            Assert.Near(50, position.Y);
            Assert.Near(20, position.Z);
            Assert.True(GroundTraversal.CanReachSurface(new Vector3(0, 50, 20), position, TraversalFloor),
                "visual lift contaminated the pickup destination");
        }

        var clicked = new HashSet<Guid>();
        var away = new Ray(new Vector3(10000, 10000, 10000), Vector3.Up);
        for (int y = 0; y < 540; y += 4)
        {
            for (int x = 0; x < 960; x += 4)
            {
                if (_ground.Pick(new Point(x, y), away, out var id, out _, showAllLabels: true))
                {
                    clicked.Add(id);
                }
            }
        }

        // Labels never overlap, so the two gold drops on one spot get a label each, one stacked above the other.
        Assert.True(clicked.Count == _drops.Length, "a drop cannot be selected by its label");
        DrawLabels(Guid.Empty, showAll: false);
        for (int y = 0; y < 540; y += 4)
        {
            for (int x = 0; x < 960; x += 4)
            {
                Assert.True(!_ground.Pick(new Point(x, y), away, out _, out _), "hidden label remained clickable after releasing A");
            }
        }

        GraphicsDevice.SetRenderTarget(null);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_output))!);
        using (var stream = File.Create(_output))
        {
            target.SaveAsPng(stream, 960, 540);
        }

        // A close look at how each drop lies: weapons flat, every drop turned by its position's yaw.
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(42, 48, 54));
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 110, 90), new Vector3(0, 50, 20));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        using (var close = File.Create(Path.ChangeExtension(_output, null) + "-close.png"))
        {
            target.SaveAsPng(close, 960, 540);
        }

        Console.WriteLine($"Verified ground elevation, coordinate remapping and a label click target on each of {_drops.Length} drops: {_output}");
        Exit();
    }

    private void DrawLabels(Guid hovered, bool showAll)
    {
        _ground.CollectLabels(GraphicsDevice.Viewport, _camera, hovered, showAll, _labels);
        var layout = HudLayout.Create(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _painter.Begin(layout);
        _labelBoxes.Draw(_painter, layout, _labels, _ground.SetLabelBounds);
        _painter.End();
    }

    // The name box as the monster and NPC names use it, plain and highlighted under the cursor.
    private void DrawBoxSamples()
    {
        var layout = HudLayout.Create(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _painter.Begin(layout);
        var x = 16;
        foreach (var (name, highlighted) in new[] { ("Hopy", false), ("Lena", true) })
        {
            var width = _painter.Measure(name) + 18;
            _painter.NameBox(new Rectangle(x, 16, width, 28), highlighted);
            _painter.Flush();
            _painter.Text(name, x + 9, 16 + 7, highlighted ? new Color(180, 180, 255) : new Color(255, 210, 210), false);
            _painter.Flush();
            x += width + 12;
        }

        _painter.End();
    }

    protected override void UnloadContent()
    {
        _painter.Dispose();
        _sprites.Dispose();
        _ground.Dispose();
        _batch.Dispose();
        _renderer.Dispose();
        base.UnloadContent();
    }
}
