using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Tests;

// Renders a field's minimap (Field/map/<field id>.tga): the stage and its objects seen from straight above at noon,
// framed by the stage's authored map rectangle as the HUD minimap reads it. The camera stands so high, with so narrow a
// view, that the projection is orthographic to a pixel. The map is drawn in tiles at twice the final size, then again in
// surface colour over a key colour that marks what lies outside the stage (black on the map), averaged down and written
// as a 24-bit TGA with a PNG beside it in the output folder.
internal sealed class MinimapRenderProbe : Game
{
    // The longer side of a map in pixels: at the HUD's 3,072-unit view the largest fields stay near one map pixel per
    // screen pixel.
    private const int MapSize = 2048;
    private const int Supersample = 2;
    private const int Tile = 512;
    // Height of the camera above the stage's middle height: a stage 2,000 units tall shifts a point at a tile's edge
    // by under a map pixel.
    private const float CameraHeight = 400000f;
    private const int WarmupFrames = 30;
    private const int TileFrames = 3;
    private const double SettleSeconds = 2.0;
    // The background of the mask pass: no stage texture's albedo comes near this colour.
    private static readonly Color MaskKey = new(255, 0, 255);

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly Queue<string> _fields;
    private readonly PresentationSettings _presentation = new();
    private PristonRenderer? _renderer;
    private PristonCamera? _camera;
    // One target for the whole run: a target freed after a draw the GPU has not finished loses the device.
    private RenderTarget2D? _target;
    private PristonStage? _stage;
    private string _field = "";
    private int _width;
    private int _height;
    // Map pixels per world unit at the supersampled size.
    private float _scale;
    private Color[] _map = [];
    private readonly Color[] _tilePixels = new Color[Tile * Tile];
    private bool _masking;
    private int _tile;
    private int _frame;
    private double _seconds;
    private double _loadedAt;

    public MinimapRenderProbe(string assetRoot, string contentRoot, string output, IEnumerable<string> fields)
    {
        _assetRoot = assetRoot;
        _output = output;
        _fields = new Queue<string>(fields);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Tile,
            PreferredBackBufferHeight = Tile,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = false;
    }

    // What the map shows: the lit surfaces at a fixed exposure, with nothing that depends on the view distance.
    private static ClientSettings MapSettings => new ClientSettings() with
    {
        Quality = GraphicsQuality.Custom,
        AntiAliasing = AntiAliasingMode.Smaa,
        DistanceFog = DistanceFogMode.Off,
        AmbientOcclusion = false,
        ContactShadows = false,
        ScreenSpaceReflections = false,
        VolumetricLight = false,
        ShadowsEnabled = false,
        Bloom = BloomQuality.Off,
        AutoExposure = false,
        WeatherEnabled = false,
        RealisticSky = false,
        Sharpening = 0f
    };

    // The same view in surface colour without anti-aliasing, over MaskKey: what stays MaskKey is outside the stage.
    private static ClientSettings MaskSettings => MapSettings with { AntiAliasing = AntiAliasingMode.Off, DebugView = PristonDebugView.Albedo };

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        NextField();
    }

    private bool NextField()
    {
        _renderer?.Dispose();
        _renderer = null;
        if (!_fields.TryDequeue(out var id))
        {
            return false;
        }

        var field = new WorldCatalog().AllFields.Single(candidate => candidate.Id == id);
        _field = id;
        var sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _assetRoot);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _presentation)
        {
            Sky = sky,
            DayLengthSeconds = 0,
            BackgroundColor = MaskKey,
            SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day), PristonSkyProfiles.Resolve(field.Backdrop.Glow),
                PristonSkyProfiles.Resolve(field.Backdrop.Night))
        };
        var reader = new PristonSmdReader();
        _stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(_stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(_stage, models);
        _renderer.SetTimeOfDay(12f);

        var rectangle = _stage.AuthoredMapRectangle;
        var worldWidth = (rectangle.Right - rectangle.Left) / GameUnits.FixedScale;
        var worldHeight = (rectangle.Bottom - rectangle.Top) / GameUnits.FixedScale;
        if (worldWidth <= 0 || worldHeight <= 0)
        {
            throw new InvalidDataException($"{id}: the stage has no map rectangle.");
        }

        _scale = MapSize * Supersample / MathF.Max(worldWidth, worldHeight);
        _width = Even(worldWidth * _scale);
        _height = Even(worldHeight * _scale);
        _map = new Color[_width * _height];
        var tileWorld = Tile / _scale;
        var depth = _stage.Bounds.Max.Y - _stage.Bounds.Min.Y + 1000f;
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings
        {
            FieldOfViewDegrees = 2f * MathF.Atan(tileWorld * 0.5f / CameraHeight) * 180f / MathF.PI,
            MaxHorizontalFieldOfViewDegrees = 179f,
            NearPlane = CameraHeight - depth,
            FarPlane = CameraHeight + depth
        });
        _tile = 0;
        _masking = false;
        _presentation.ApplyRendering(MapSettings);
        _frame = -WarmupFrames;
        _loadedAt = _seconds;
        return true;
    }

    // The tonemapper moves MaskKey a little (255, 0, 255 reads back near 232, 32, 238).
    private static bool IsMaskKey(Color color) => color.R > 192 && color.G < 64 && color.B > 192;

    private static int Even(float pixels) => Math.Max(Supersample, (int)MathF.Ceiling(pixels / Supersample) * Supersample);

    protected override void Update(GameTime gameTime)
    {
        const float step = 1f / 60f;
        _seconds += step;
        _renderer?.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(step)));
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_renderer is null || _stage is null || _camera is null)
        {
            Exit();
            return;
        }

        // The stage's textures and the sky finish their uploads before the first frame is drawn.
        if (_seconds - _loadedAt < SettleSeconds)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        var columns = (_width + Tile - 1) / Tile;
        var rows = (_height + Tile - 1) / Tile;
        var column = _tile % columns;
        var row = _tile / columns;
        var rectangle = _stage.AuthoredMapRectangle;
        var left = rectangle.Left / GameUnits.FixedScale;
        var top = -rectangle.Bottom / GameUnits.FixedScale;
        var tileWorld = Tile / _scale;
        var middle = (_stage.Bounds.Min.Y + _stage.Bounds.Max.Y) * 0.5f;
        var center = new Vector3(left + (column + 0.5f) * tileWorld, middle, top + (row + 0.5f) * tileWorld);
        _target ??= new RenderTarget2D(GraphicsDevice, Tile, Tile, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(_target);
        // Image up is -Z (the map's top row is the rectangle's bottom edge, mirrored), image right is +X.
        _camera.SetScriptedView(GraphicsDevice, center + Vector3.Up * CameraHeight, center, Vector3.Forward);
        // The mask pass hides the sky so the background keeps MaskKey.
        _renderer.Underground = _masking;
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        if (++_frame < TileFrames)
        {
            return;
        }

        // The shaded pass fills the map; the mask pass then blacks out what lies outside the stage.
        _target.GetData(_tilePixels);
        for (var y = 0; y < Tile && row * Tile + y < _height; y++)
        {
            for (var x = 0; x < Tile && column * Tile + x < _width; x++)
            {
                var index = (row * Tile + y) * _width + column * Tile + x;
                if (!_masking)
                {
                    _map[index] = _tilePixels[y * Tile + x];
                }
                else if (IsMaskKey(_tilePixels[y * Tile + x]))
                {
                    _map[index] = Color.Black;
                }
            }
        }

        _frame = 0;
        if (++_tile < columns * rows)
        {
            return;
        }

        if (!_masking)
        {
            _presentation.ApplyRendering(MaskSettings);
            _masking = true;
            _tile = 0;
            _frame = -WarmupFrames;
            return;
        }

        Save();
        if (!NextField())
        {
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        _renderer?.Dispose();
        _target?.Dispose();
    }

    // Averages the supersampled map down and writes it.
    private void Save()
    {
        var width = _width / Supersample;
        var height = _height / Supersample;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int r = 0, g = 0, b = 0;
                for (var sy = 0; sy < Supersample; sy++)
                {
                    for (var sx = 0; sx < Supersample; sx++)
                    {
                        var source = _map[(y * Supersample + sy) * _width + x * Supersample + sx];
                        r += source.R;
                        g += source.G;
                        b += source.B;
                    }
                }

                const int count = Supersample * Supersample;
                pixels[y * width + x] = new Color(r / count, g / count, b / count, 255);
            }
        }

        WriteTga(Path.Combine(_output, _field + ".tga"), pixels, width, height);
        using var preview = new Texture2D(GraphicsDevice, width, height);
        preview.SetData(pixels);
        using var stream = File.Create(Path.Combine(_output, _field + ".png"));
        preview.SaveAsPng(stream, width, height);
        Console.WriteLine($"Minimap {_field}: {width}x{height}");
    }

    // An uncompressed true-colour TGA, rows stored bottom-up.
    private static void WriteTga(string path, Color[] pixels, int width, int height)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)24);
        writer.Write((byte)0);
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                writer.Write(pixel.B);
                writer.Write(pixel.G);
                writer.Write(pixel.R);
            }
        }
    }

    private string Resolve(string relative) => AssetFiles.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
