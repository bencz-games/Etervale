using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Stabilized cascaded shadow maps of the sun (or the moon): the view frustum split by view depth (the practical
// scheme: a blend of logarithmic and uniform splits), each slice enclosed in its bounding sphere, so a cascade's size
// does not change as the camera turns, and its light-space position moved by whole texels only, so shadow edges do not
// crawl as the camera moves (FollowFocus). The cascades share one atlas (a 2x2 grid of tiles from two cascades up). Far cascades
// refresh less often than near ones (UpdateMask).
internal sealed class PristonShadowCascades
{
    public const int MaxCascades = 4;
    // View depth covered by the shadows and the share of logarithmic splits.
    public const float ShadowDistance = 9000f;
    public const float NearDepth = 60f;
    private const float SplitLambda = 0.8f;
    // Casters up to this far beyond a cascade's sphere toward the light still cast into it (towers, cliffs).
    private const float CasterReach = 4000f;

    private readonly Matrix[] _viewProjections = new Matrix[MaxCascades];
    private readonly Vector4[] _atlasRects = new Vector4[MaxCascades];
    private readonly float[] _texelWorldSizes = new float[MaxCascades];
    private readonly float[] _depthRanges = new float[MaxCascades];
    private readonly float[] _splits = new float[MaxCascades];
    private readonly FocusSnap[] _snaps = new FocusSnap[MaxCascades];
    private uint _refreshCount;

    internal struct FocusSnap
    {
        public bool Valid;
        public Vector3 Previous;
        public float WorldPerTexel;
        public double ResidualX;
        public double ResidualY;
    }

    public int Count { get; private set; } = MaxCascades;

    // The shaders always read MaxCascades: the slots past Count repeat the last cascade.
    public int Padded(int slot) => Math.Min(slot, Count - 1);
    public int AtlasSize { get; private set; }
    public int TileSize => Count == 1 ? AtlasSize : AtlasSize / 2;
    public IReadOnlyList<Matrix> ViewProjections => _viewProjections;
    public IReadOnlyList<Vector4> AtlasRects => _atlasRects;
    public IReadOnlyList<float> TexelWorldSizes => _texelWorldSizes;
    public IReadOnlyList<float> DepthRanges => _depthRanges;

    // Far view depth of each cascade.
    public IReadOnlyList<float> Splits => _splits;

    public void Configure(int count, int atlasSize)
    {
        Count = Math.Clamp(count, 1, MaxCascades);
        AtlasSize = atlasSize;
        for (var cascade = 0; cascade < MaxCascades; cascade++)
        {
            _atlasRects[cascade] = Count == 1
                ? new Vector4(0f, 0f, 1f, 1f)
                : new Vector4(cascade % 2 * 0.5f, cascade / 2 * 0.5f, 0.5f, 0.5f);
        }

        for (var cascade = 0; cascade < Count; cascade++)
        {
            _splits[cascade] = SplitDepth(cascade + 1, Count);
        }

        for (var cascade = Count; cascade < MaxCascades; cascade++)
        {
            _splits[cascade] = _splits[Count - 1];
        }

        _refreshCount = 0;
        Array.Clear(_snaps);
    }

    // Practical split scheme (Zhang et al. 2006): lambda * logarithmic + (1 - lambda) * uniform.
    public static float SplitDepth(int index, int count)
    {
        var t = (float)index / count;
        var logarithmic = NearDepth * MathF.Pow(ShadowDistance / NearDepth, t);
        var uniform = NearDepth + (ShadowDistance - NearDepth) * t;
        return MathHelper.Lerp(uniform, logarithmic, SplitLambda);
    }

    // The near cascades: redrawn every frame between refreshes, so the actors in them cast from their current pose.
    public const int NearMask = 0b0011;

    // The cascades this refresh updates: the first two every time, the third every second and the fourth every
    // fourth refresh. A full refresh (teleports, a new light direction) updates all.
    public int UpdateMask(bool full)
    {
        var refresh = _refreshCount++;
        if (full)
        {
            return (1 << Count) - 1;
        }

        var mask = NearMask;
        if (refresh % 2 == 0)
        {
            mask |= 0b0100;
        }

        if (refresh % 4 == 0)
        {
            mask |= 0b1000;
        }

        return mask & ((1 << Count) - 1);
    }

    // Fits a cascade to the camera slice [near split, far split] for a light travelling along lightDirection.
    public void Fit(int cascade, Matrix cameraView, Matrix cameraProjection, Vector3 lightDirection)
    {
        var near = cascade == 0 ? NearDepth * 0.5f : _splits[cascade - 1];
        var far = _splits[cascade];
        var inverseView = Matrix.Invert(cameraView);
        var eye = inverseView.Translation;
        var forward = Vector3.Normalize(-new Vector3(inverseView.M31, inverseView.M32, inverseView.M33));
        var tanX = 1f / cameraProjection.M11;
        var tanY = 1f / cameraProjection.M22;
        var k2 = tanX * tanX + tanY * tanY;
        // The smallest sphere around the slice, centred on the view axis.
        var t = MathF.Min((near + far) * 0.5f * (1f + k2), far);
        var radius = MathF.Sqrt(MathF.Max((far - t) * (far - t) + far * far * k2, (t - near) * (t - near) + near * near * k2));
        // Quantized so the cascade's texel size never changes between frames.
        radius = MathF.Ceiling(radius / 16f) * 16f;
        var direction = Vector3.Normalize(lightDirection);
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
        var depthRange = 2f * radius + CasterReach;
        var centre = FollowFocus(ref _snaps[cascade], eye + forward * t, direction, up, 2f * radius / TileSize);
        var view = Matrix.CreateLookAt(centre - direction * (radius + CasterReach), centre, up);
        var projection = Matrix.CreateOrthographic(2f * radius, 2f * radius, 0f, depthRange);
        _viewProjections[cascade] = view * projection;
        _texelWorldSizes[cascade] = 2f * radius / TileSize;
        _depthRanges[cascade] = depthRange;
    }

    // The cascade's centre: the focus moved back by the sub-texel part of how far the focus has travelled across the
    // light, so the centre advances by whole texels and a still receiver keeps its place in the texel grid. The grid is
    // anchored at the focus, never at a far world anchor, so a turn of the sun pivots it about the characters and leaves
    // still shadow edges in place.
    internal static Vector3 FollowFocus(ref FocusSnap snap, Vector3 focus, Vector3 direction, Vector3 up, float worldPerTexel)
    {
        var right = Vector3.Normalize(Vector3.Cross(up, -direction));
        var lightUp = Vector3.Cross(-direction, right);
        if (!snap.Valid || snap.WorldPerTexel != worldPerTexel)
        {
            snap = new FocusSnap { Valid = true, Previous = focus, WorldPerTexel = worldPerTexel };
        }

        var moved = focus - snap.Previous;
        snap.ResidualX += Vector3.Dot(moved, right) / worldPerTexel;
        snap.ResidualY += Vector3.Dot(moved, lightUp) / worldPerTexel;
        snap.ResidualX -= Math.Floor(snap.ResidualX + 0.5);
        snap.ResidualY -= Math.Floor(snap.ResidualY + 0.5);
        snap.Previous = focus;
        return focus - right * (float)(snap.ResidualX * worldPerTexel) - lightUp * (float)(snap.ResidualY * worldPerTexel);
    }

    // The pixel rectangle of a cascade's tile in the atlas.
    public Rectangle TileRectangle(int cascade)
    {
        var rect = _atlasRects[cascade];
        return new Rectangle((int)(rect.X * AtlasSize), (int)(rect.Y * AtlasSize), TileSize, TileSize);
    }
}
