using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering.Weather;

// A drop of the fixed buffer (Rain.fx Drops): its seed and the corner of its streak.
internal struct RainDropVertex : IVertexType
{
    public Vector4 Seed;
    public Vector4 Corner;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0));

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

// A corner of a splash quad (Rain.fx Splashes): its world position and age, its uv, kind and opacity.
internal struct RainSplashVertex : IVertexType
{
    public Vector4 Position;
    public Vector4 Shape;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0));

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

// Rain drawn into the HDR scene after the opaque passes: GPU streaks in a near and a far box around the camera, their
// count following the rain, and splashes on the surfaces of the occlusion map near the player (roofs and ground). The
// streaks take the frame's rain light (PristonRenderer.RainRadiance), so they follow the hour and the lightning.
public sealed class PristonRain : IDisposable
{
    public const string AssetName = "Effects/Rain";
    // Drops at full rain in the near box and the far one.
    public const int NearDrops = 18000;
    public const int FarDrops = 10000;
    // The boxes' widths and height (world units; a character is about 50 tall) and how far ahead of the eye they sit.
    public const float NearBox = 900f;
    public const float FarBox = 2600f;
    public const float BoxHeight = 900f;
    public const float VolumeAhead = 240f;
    // Falling speed (units per second), the time a streak spans, a drop's width and the least width on screen at 1080
    // lines.
    public const float FallSpeed = 850f;
    public const float StreakSeconds = 1f / 26f;
    public const float DropWidth = 0.5f;
    public const float StreakPixels = 1.3f;
    public const float Opacity = 0.38f;
    // Splashes alive at full rain, how far around the player they land and how long one lasts.
    public const int MaxSplashes = 520;
    public const float SplashRadius = 460f;
    public const float SplashSeconds = 0.3f;
    private const float RingSize = 4.5f;
    private const float SpraySize = 2.6f;
    // The drop clock starts over after this many seconds, so float precision holds in the wrap.
    private const float ClockPeriod = 1200f;
    private const float CameraVelocitySmoothing = 10f;
    private const int Layers = 2;
    // Seconds between two uploads of the occlusion map while its cells change.
    private const float OcclusionUploadSeconds = 0.1f;

    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly EffectTechnique _drops;
    private readonly EffectTechnique _splashes;
    private readonly VertexBuffer _dropVertices;
    private readonly IndexBuffer _indices;
    private readonly DynamicVertexBuffer _splashVertices;
    private readonly Texture2D _occlusionMap;
    private float _sinceUpload = OcclusionUploadSeconds;
    private readonly RainSplashVertex[] _splashScratch = new RainSplashVertex[MaxSplashes * 2 * 4];
    private readonly Splash[] _live = new Splash[MaxSplashes];
    private readonly Random _random = new(0x5241494E);
    private int _liveCount;
    private float _spawnDebt;
    private float _clock;
    private Vector3 _lastCamera;
    private Vector3 _cameraVelocity;
    private bool _hasCamera;

    public PristonRain(GraphicsDevice device, ContentManager content, IClientLog log = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(content);
        _device = device;
        _effect = PristonShaderLibrary.LoadRequired(content, AssetName, log ?? ConsoleClientLog.Instance);
        _drops = _effect.Techniques["Drops"];
        _splashes = _effect.Techniques["Splashes"];
        Occlusion = new PristonRainOcclusion();
        _occlusionMap = new Texture2D(device, PristonRainOcclusion.Cells, PristonRainOcclusion.Cells, false, SurfaceFormat.Single);

        var drops = NearDrops + FarDrops;
        var random = new Random(0x44524F50);
        var vertices = new RainDropVertex[drops * 4];
        for (var drop = 0; drop < drops; drop++)
        {
            var seed = new Vector4(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());
            var layer = drop < NearDrops ? 0f : 1f;
            vertices[drop * 4] = new RainDropVertex { Seed = seed, Corner = new Vector4(-1f, 0f, layer, 0f) };
            vertices[drop * 4 + 1] = new RainDropVertex { Seed = seed, Corner = new Vector4(1f, 0f, layer, 0f) };
            vertices[drop * 4 + 2] = new RainDropVertex { Seed = seed, Corner = new Vector4(1f, 1f, layer, 0f) };
            vertices[drop * 4 + 3] = new RainDropVertex { Seed = seed, Corner = new Vector4(-1f, 1f, layer, 0f) };
        }

        _dropVertices = new VertexBuffer(device, RainDropVertex.Declaration, vertices.Length, BufferUsage.WriteOnly);
        _dropVertices.SetData(vertices);
        var indices = new int[drops * 6];
        for (var quad = 0; quad < drops; quad++)
        {
            var vertex = quad * 4;
            indices[quad * 6] = vertex;
            indices[quad * 6 + 1] = vertex + 1;
            indices[quad * 6 + 2] = vertex + 2;
            indices[quad * 6 + 3] = vertex;
            indices[quad * 6 + 4] = vertex + 2;
            indices[quad * 6 + 5] = vertex + 3;
        }

        _indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
        _indices.SetData(indices);
        _splashVertices = new DynamicVertexBuffer(device, RainSplashVertex.Declaration, _splashScratch.Length, BufferUsage.WriteOnly);
    }

    public PristonRainOcclusion Occlusion { get; }

    public bool DisplayOutput { get; set; }

    // Before the frame's draw: the occlusion map read and uploaded around the player, the splashes aged and landed and
    // the drop clock moved, for the eye's position, the seconds since the last update and the rain (0..1).
    public void Update(Vector3 eye, Vector3 focus, float seconds, float rain)
    {
        TrackCamera(eye, seconds);
        Occlusion.Update(focus);
        _sinceUpload += seconds;
        if (Occlusion.Changed && _sinceUpload >= OcclusionUploadSeconds)
        {
            _occlusionMap.SetData(Occlusion.Heights);
            Occlusion.MarkUploaded();
            _sinceUpload = 0f;
        }

        rain = MathHelper.Clamp(rain, 0f, 1f);
        StepSplashes(focus, seconds, rain);
        _clock = rain > 0f ? (_clock + seconds) % ClockPeriod : 0f;
    }

    // Draws the rain of weather into the bound scene, testing its depth (nothing without rain): view and projection of
    // the frame, the eye and the drop light.
    public void Draw(Matrix view, Matrix projection, Vector3 eye, PristonWeather weather, Vector3 radiance)
    {
        var rain = MathHelper.Clamp(weather.Rain, 0f, 1f);
        if (rain <= 0f && _liveCount == 0)
        {
            return;
        }

        var viewport = _device.Viewport;
        var forward = Vector3.Normalize(new Vector3(-view.M13, 0f, -view.M33) + new Vector3(0f, 0f, 1e-4f));
        var centre = eye + forward * VolumeAhead;
        var parameters = _effect.Parameters;
        parameters["ViewProjection"].SetValue(view * projection);
        parameters["Volume"].SetValue(new Vector4(centre, _clock));
        parameters["Boxes"].SetValue(new Vector4(NearBox, FarBox, BoxHeight, StreakSeconds));
        parameters["Fall"].SetValue(new Vector4(weather.Wind.X, -FallSpeed, weather.Wind.Y, 0f));
        parameters["CameraVelocity"].SetValue(new Vector4(_cameraVelocity, 0f));
        parameters["CameraPosition"].SetValue(new Vector4(eye, projection.M22 * viewport.Height * 0.5f));
        parameters["Screen"].SetValue(new Vector4(viewport.Width, viewport.Height, MathF.Max(1f, StreakPixels * viewport.Height / 1080f), DropWidth));
        parameters["RainColor"].SetValue(new Vector4(radiance, Opacity));
        parameters["Occlusion"].SetValue(new Vector4(Occlusion.Centre, PristonRainOcclusion.CellSize, Occlusion.Ready ? 1f : 0f));
        parameters["OcclusionCells"].SetValue((float)PristonRainOcclusion.Cells);
        parameters["OcclusionMap"].SetValue(_occlusionMap);
        parameters["Exposure"].SetValue(PristonUnlitEffect.SceneExposure);
        parameters["DisplayOutput"].SetValue(DisplayOutput ? 1f : 0f);

        _device.DepthStencilState = PristonDepth.Read;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.NonPremultiplied;
        _effect.CurrentTechnique = _drops;
        _effect.CurrentTechnique.Passes[0].Apply();
        _device.SetVertexBuffer(_dropVertices);
        _device.Indices = _indices;
        Span<(int First, int Count)> layers = [(0, (int)(NearDrops * rain)), (NearDrops, (int)(FarDrops * rain))];
        for (var layer = 0; layer < Layers; layer++)
        {
            if (layers[layer].Count > 0)
            {
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, layers[layer].First * 6, layers[layer].Count * 2);
            }
        }

        DrawSplashes(view);
        _device.SetVertexBuffer(null);
    }

    public void Dispose()
    {
        _effect.Dispose();
        _dropVertices.Dispose();
        _indices.Dispose();
        _splashVertices.Dispose();
        _occlusionMap.Dispose();
    }

    private void TrackCamera(Vector3 eye, float seconds)
    {
        if (_hasCamera && seconds > 0f)
        {
            var velocity = (eye - _lastCamera) / seconds;
            _cameraVelocity = Vector3.Lerp(_cameraVelocity, velocity, 1f - MathF.Exp(-CameraVelocitySmoothing * seconds));
        }

        _lastCamera = eye;
        _hasCamera = true;
    }

    // Splashes age and new ones land at random points of the disc around the player, on the surface the map holds there.
    private void StepSplashes(Vector3 focus, float seconds, float rain)
    {
        for (var index = 0; index < _liveCount;)
        {
            _live[index].Age += seconds / SplashSeconds;
            if (_live[index].Age >= 1f)
            {
                _live[index] = _live[--_liveCount];
                continue;
            }

            index++;
        }

        if (!Occlusion.Ready || rain <= 0f)
        {
            _spawnDebt = 0f;
            return;
        }

        _spawnDebt += rain * MaxSplashes / SplashSeconds * seconds;
        for (; _spawnDebt >= 1f && _liveCount < MaxSplashes; _spawnDebt -= 1f)
        {
            var angle = _random.NextSingle() * MathF.Tau;
            var distance = MathF.Sqrt(_random.NextSingle()) * SplashRadius;
            var x = focus.X + MathF.Cos(angle) * distance;
            var z = focus.Z + MathF.Sin(angle) * distance;
            var height = Occlusion.HeightAt(x, z);
            if (height > PristonRainOcclusion.NoSurface)
            {
                _live[_liveCount++] = new Splash(new Vector3(x, height, z), 0f, 0.5f + 0.5f * _random.NextSingle());
            }
        }

        _spawnDebt = MathF.Min(_spawnDebt, 1f);
    }

    // Each splash is a flat ring and a spray facing the camera.
    private void DrawSplashes(Matrix view)
    {
        if (_liveCount == 0)
        {
            return;
        }

        var right = new Vector3(view.M11, view.M21, view.M31);
        var vertex = 0;
        for (var index = 0; index < _liveCount; index++)
        {
            var splash = _live[index];
            var ground = splash.Position + new Vector3(0f, 0.6f, 0f);
            Quad(ref vertex, ground, Vector3.UnitX * RingSize, Vector3.UnitZ * RingSize, splash, 0f);
            Quad(ref vertex, ground + Vector3.UnitY * SpraySize, right * SpraySize * 0.6f, Vector3.UnitY * SpraySize, splash, 1f);
        }

        _splashVertices.SetData(_splashScratch, 0, vertex, SetDataOptions.Discard);
        _effect.CurrentTechnique = _splashes;
        _effect.CurrentTechnique.Passes[0].Apply();
        _device.SetVertexBuffer(_splashVertices);
        _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, vertex / 2);
    }

    private void Quad(ref int vertex, Vector3 centre, Vector3 u, Vector3 v, Splash splash, float kind)
    {
        ReadOnlySpan<Vector2> corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];
        foreach (var corner in corners)
        {
            _splashScratch[vertex++] = new RainSplashVertex
            {
                Position = new Vector4(centre + u * corner.X + v * corner.Y, splash.Age),
                Shape = new Vector4(corner.X, corner.Y, kind, splash.Opacity)
            };
        }
    }

    private record struct Splash(Vector3 Position, float Age, float Opacity);
}
