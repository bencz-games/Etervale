using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// Low-resolution copy of the composed sky dome for the atmospheric fog colour, display-referred like the dome itself
// (8-bit sRGB, the layers blended as authored; the fog decodes it). Four 90-degree perspective
// tiles facing -Z, +X, +Z and -X sit side by side, so each tile's right edge continues into the next one and the strip
// wraps horizontally; CapturedSky in Shaders/Atmosphere/Fog.hlsli inverts exactly this projection.
internal sealed class PristonSkyCapture : IDisposable
{
    public const int TileSize = 32;
    public const int TileCount = 4;
    // Vertical extent of every tile as the tangent of the elevation over the tile axis: about -8.5 to +24 degrees.
    public const float LowerTangent = -0.15f;
    public const float UpperTangent = 0.45f;
    // The sky domes paint their horizon glow and distant mountains just below the geometric horizon, so ground fog samples
    // there and elevations are halved first: a tall distant structure takes the sky close behind its base.
    public const float SampleLowerTangent = -0.03f;
    public const float SampleUpperTangent = 0.3f;
    public const float ElevationScale = 0.5f;
    private const int MinimumFramesBetweenRefreshes = 4;
    private const uint AnimatedRefreshMilliseconds = 500;
    private const int BlendSteps = 16;
    private const float HeightStep = 16f;

    private readonly GraphicsDevice _device;
    private RenderTarget2D _target;
    private readonly PristonBoundTargets _restore = new();
    private CaptureKey _key;
    private bool _valid;
    private int _framesSinceRefresh;
    private uint _refreshTime;

    public PristonSkyCapture(GraphicsDevice device)
    {
        _device = device;
    }

    private readonly record struct CaptureKey(int Profile, int PreviousProfile, int BlendStep, int HeightStep);

    public Texture2D Texture => _valid ? _target : null;

    public int Refreshes { get; private set; }

    public static Vector4 Band { get; } = new(SampleLowerTangent, SampleUpperTangent, ElevationScale, LowerTangent);

    public static Vector4 Band2 { get; } = new(1f / (UpperTangent - LowerTangent), 0f, 0f, 0f);

    // Refreshes on a profile, transition step or sky-camera height change, and every half second for skies with
    // scrolling or animated layers, but never twice within a few frames except for the first capture.
    public bool ShouldRefresh(int profile, int previousProfile, float blend, float skyHeight, bool animated, uint renderTime)
    {
        _framesSinceRefresh++;
        var transition = previousProfile >= 0 && blend < 1f;
        var key = new CaptureKey(
            profile,
            transition ? previousProfile : -1,
            transition ? (int)(blend * BlendSteps) : BlendSteps,
            (int)MathF.Floor(skyHeight / HeightStep));
        if (!_valid)
        {
            _key = key;
            return true;
        }

        if (_framesSinceRefresh < MinimumFramesBetweenRefreshes)
        {
            return false;
        }

        var stale = animated && renderTime - _refreshTime >= AnimatedRefreshMilliseconds;
        if (key == _key && !stale)
        {
            return false;
        }

        _key = key;
        return true;
    }

    public void Begin(Color clearColour, uint renderTime)
    {
        _target ??= new RenderTarget2D(_device, TileSize * TileCount, TileSize, false, SurfaceFormat.Color, DepthFormat.None);
        _restore.Save(_device);
        _device.SetRenderTarget(_target);
        _device.Clear(clearColour);
        _framesSinceRefresh = 0;
        _refreshTime = renderTime;
    }

    public void BeginTile(int tile)
    {
        _device.Viewport = new Viewport(tile * TileSize, 0, TileSize, TileSize);
    }

    public void End()
    {
        _restore.Restore(_device);
        _restore.Forget();
        _valid = true;
        Refreshes++;
    }

    public static Matrix TileView(int tile, Vector3 position)
    {
        var forward = tile switch
        {
            0 => Vector3.Forward,
            1 => Vector3.Right,
            2 => Vector3.Backward,
            _ => Vector3.Left
        };
        return Matrix.CreateLookAt(position, position + forward, Vector3.Up);
    }

    // Same infinite far plane as the sky pass, so no sky dome layer is clipped by distance.
    public static Matrix TileProjection()
    {
        var projection = Matrix.CreatePerspectiveOffCenter(-1f, 1f, LowerTangent, UpperTangent, 1f, 2f);
        projection.M33 = -1f;
        projection.M43 = -1f;
        return projection;
    }

    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
        _valid = false;
    }
}
