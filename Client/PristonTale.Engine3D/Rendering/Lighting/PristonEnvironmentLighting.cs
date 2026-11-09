using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Image-based lighting from the sky dome (Shaders/Passes/Environment.fx): the dome's six cube faces (drawn by the
// renderer through PristonCubeFaces.View), an HDR octahedral map of them, its GGX-prefiltered levels side by side in one atlas, the
// order-2 spherical harmonics of the irradiance and the split-sum BRDF table, refreshed a few times a second (the dome
// moves slowly; the scale follows the light model's sky light).
internal sealed class PristonEnvironmentLighting : IDisposable
{
    public const string Asset = "Effects/Environment";
    public const int FaceSize = 64;
    public const int BaseSize = 128;
    public const int Levels = 5;
    public const int ShEntries = 10;
    public const int BrdfSize = 32;
    public const uint RefreshMilliseconds = 250;
    private const int PrefilterSamples = 64;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _octahedral;
    private readonly EffectTechnique _prefilter;
    private readonly EffectTechnique _irradiance;
    private readonly EffectParameter _params;
    private readonly EffectParameter _skyTarget;
    private readonly EffectParameter _groundTarget;
    private readonly EffectParameter _faceAtlas;
    private readonly EffectParameter _octahedralMap;
    private readonly EffectParameter _lightSh;
    private readonly EffectParameter _sh;
    private readonly RenderTarget2D _faces;
    private readonly RenderTarget2D _base;
    private readonly RenderTarget2D _atlas;
    private readonly StructuredBuffer[] _shBuffers = new StructuredBuffer[2];
    private int _current;
    private uint _refreshTime;
    private bool _valid;

    public PristonEnvironmentLighting(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _octahedral = effect.Techniques["Octahedral"];
        _prefilter = effect.Techniques["Prefilter"];
        _irradiance = effect.Techniques["Irradiance"];
        var parameters = effect.Parameters;
        _params = parameters["EnvParams"];
        _skyTarget = parameters["SkyAmbientTarget"];
        _groundTarget = parameters["GroundAmbientTarget"];
        _faceAtlas = parameters["FaceAtlas"];
        _octahedralMap = parameters["Octahedral"];
        _lightSh = parameters["LightSh"];
        _sh = parameters["Sh"];
        _faces = new RenderTarget2D(device, FaceSize * PristonCubeFaces.Count, FaceSize, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _base = new RenderTarget2D(device, BaseSize, BaseSize, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _atlas = new RenderTarget2D(device, AtlasWidth, BaseSize, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        for (var i = 0; i < 2; i++)
        {
            _shBuffers[i] = new StructuredBuffer(device, 16, ShEntries, BufferShaderAccess.ReadWrite);
            var initial = new Vector4[ShEntries];
            initial[9] = new Vector4(1f, 0f, 0f, 0f);
            _shBuffers[i].SetData(initial);
        }

        BrdfLut = CreateBrdfLut(device);
    }

    // Width of the prefiltered atlas: the levels' sizes summed (BaseSize, BaseSize / 2, ...).
    public static int AtlasWidth => 2 * BaseSize - 2 * (BaseSize >> (Levels - 1)) + (BaseSize >> (Levels - 1));

    public bool Valid => _valid;
    public Texture2D Atlas => _atlas;
    public StructuredBuffer Sh => _shBuffers[_current];
    public Texture2D BrdfLut { get; }

    public bool ShouldRefresh(uint renderTime) => !_valid || renderTime - _refreshTime >= RefreshMilliseconds;

    // A 90-degree square face with the sky pass's infinite far plane.
    public static Matrix FaceProjection()
    {
        var projection = Matrix.CreatePerspectiveOffCenter(-1f, 1f, -1f, 1f, 1f, 2f);
        projection.M33 = -1f;
        projection.M43 = -1f;
        return projection;
    }

    // Binds the face atlas, cleared to the colour behind the dome; the renderer draws each face over FaceViewport.
    public void BeginFaces(Color clear)
    {
        _device.SetRenderTarget(_faces);
        _device.Clear(clear);
    }

    public static Viewport FaceViewport(int face) => new(face * FaceSize, 0, FaceSize, FaceSize);

    // Builds the irradiance, the octahedral map and the prefiltered atlas from the faces just drawn.
    public void Build(uint renderTime, float displayScale, Vector3 skyAmbient, Vector3 groundAmbient)
    {
        _current ^= 1;
        _device.BlendState = BlendState.Opaque;
        _faceAtlas.SetValue(_faces);
        _skyTarget.SetValue(skyAmbient);
        _groundTarget.SetValue(groundAmbient);
        _params.SetValue(new Vector4(displayScale, 0f, BaseSize, PrefilterSamples));
        // The irradiance first (from the faces): the octahedral map takes its scale and fill.
        _sh.SetValue(_shBuffers[_current]);
        _effect.CurrentTechnique = _irradiance;
        _irradiance.Passes[0].Apply();
        _device.Dispatch(1, 1, 1);

        _lightSh.SetValue(_shBuffers[_current]);
        _device.SetRenderTarget(_base);
        _effect.CurrentTechnique = _octahedral;
        _triangle.Draw(_effect);

        _device.SetRenderTarget(_atlas);
        _octahedralMap.SetValue(_base);
        _effect.CurrentTechnique = _prefilter;
        var offset = 0;
        for (var level = 0; level < Levels; level++)
        {
            var size = BaseSize >> level;
            _device.Viewport = new Viewport(offset, 0, size, size);
            _params.SetValue(new Vector4(displayScale, (float)level / (Levels - 1), size, PrefilterSamples));
            _triangle.Draw(_effect);
            offset += size;
        }

        _refreshTime = renderTime;
        _valid = true;
    }

    // The split-sum BRDF (Karis 2013): scale and bias of F0 for the prefiltered radiance, by n.v (x) and perceptual
    // roughness (y), integrated with GGX importance samples and the height-correlated Smith visibility.
    internal static Vector2[] IntegrateBrdf(int size, int samples)
    {
        var table = new Vector2[size * size];
        for (var y = 0; y < size; y++)
        {
            var roughness = (y + 0.5f) / size;
            var a = roughness * roughness;
            for (var x = 0; x < size; x++)
            {
                var nv = (x + 0.5f) / size;
                var v = new Vector3(MathF.Sqrt(1f - nv * nv), 0f, nv);
                var scale = 0f;
                var bias = 0f;
                for (var i = 0; i < samples; i++)
                {
                    var xi = new Vector2((i + 0.5f) / samples, RadicalInverse((uint)i));
                    var phi = 2f * MathF.PI * xi.X;
                    var cosTheta = MathF.Sqrt((1f - xi.Y) / (1f + (a * a - 1f) * xi.Y));
                    var sinTheta = MathF.Sqrt(1f - cosTheta * cosTheta);
                    var h = new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
                    var l = 2f * Vector3.Dot(v, h) * h - v;
                    var nl = MathF.Max(l.Z, 0f);
                    var nh = MathF.Max(h.Z, 0f);
                    var vh = MathF.Max(Vector3.Dot(v, h), 0f);
                    if (nl <= 0f)
                    {
                        continue;
                    }

                    var a2 = a * a;
                    var lambdaV = nl * MathF.Sqrt(nv * nv * (1f - a2) + a2);
                    var lambdaL = nv * MathF.Sqrt(nl * nl * (1f - a2) + a2);
                    var visibility = 0.5f / MathF.Max(lambdaV + lambdaL, 1e-5f);
                    // pdf = D nh / (4 vh); the estimator of D V nl / pdf is V nl 4 vh / nh.
                    var g = visibility * nl * 4f * vh / MathF.Max(nh, 1e-5f);
                    var fresnel = MathF.Pow(1f - vh, 5f);
                    scale += (1f - fresnel) * g;
                    bias += fresnel * g;
                }

                table[y * size + x] = new Vector2(scale / samples, bias / samples);
            }
        }

        return table;
    }

    // The directional albedo of the sheen lobe (glTF KHR_materials_sheen): Charlie's distribution (Estevez and Kulla 2017)
    // with their fitted visibility, by n.v (x) and sheen roughness (y), integrated over the hemisphere; the share of light
    // the sheen reflects and takes from the layer below.
    internal static float[] IntegrateSheen(int size, int samples)
    {
        var table = new float[size * size];
        for (var y = 0; y < size; y++)
        {
            var a = MathF.Max(MathF.Pow((y + 0.5f) / size, 2f), 1e-3f);
            for (var x = 0; x < size; x++)
            {
                var nv = (x + 0.5f) / size;
                var v = new Vector3(MathF.Sqrt(1f - nv * nv), 0f, nv);
                var sum = 0f;
                for (var i = 0; i < samples; i++)
                {
                    var phi = 2f * MathF.PI * (i + 0.5f) / samples;
                    var nl = RadicalInverse((uint)i);
                    var sinTheta = MathF.Sqrt(1f - nl * nl);
                    var l = new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), nl);
                    var h = Vector3.Normalize(v + l);
                    var sinH = MathF.Sqrt(MathF.Max(1f - h.Z * h.Z, 0f));
                    var distribution = (2f + 1f / a) * MathF.Pow(sinH, 1f / a) / (2f * MathF.PI);
                    // Uniform hemisphere samples: pdf 1 / (2 pi).
                    sum += distribution * SheenVisibility(nl, nv, a) * nl * 2f * MathF.PI;
                }

                table[y * size + x] = MathF.Min(sum / samples, 1f);
            }
        }

        return table;
    }

    private static float SheenVisibility(float nl, float nv, float a)
    {
        if (nl <= 0f || nv <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(1f / ((1f + SheenLambda(nv, a) + SheenLambda(nl, a)) * 4f * nv * nl), 0f, 1f);
    }

    private static float SheenLambda(float cosine, float a) =>
        cosine < 0.5f ? MathF.Exp(SheenLambdaFit(cosine, a)) : MathF.Exp(2f * SheenLambdaFit(0.5f, a) - SheenLambdaFit(1f - cosine, a));

    private static float SheenLambdaFit(float x, float a)
    {
        var t = (1f - a) * (1f - a);
        return MathHelper.Lerp(21.5473f, 25.3245f, t) / (1f + MathHelper.Lerp(3.82987f, 3.32435f, t) * MathF.Pow(x, MathHelper.Lerp(0.19823f, 0.16801f, t)))
            + MathHelper.Lerp(-1.97760f, -1.27393f, t) * x + MathHelper.Lerp(-4.32054f, -4.85967f, t);
    }

    // rg the split-sum scale and bias, b the sheen's directional albedo.
    private static Texture2D CreateBrdfLut(GraphicsDevice device)
    {
        var table = IntegrateBrdf(BrdfSize, 256);
        var sheen = IntegrateSheen(BrdfSize, 256);
        var packed = new HalfVector4[table.Length];
        for (var i = 0; i < table.Length; i++)
        {
            packed[i] = new HalfVector4(table[i].X, table[i].Y, sheen[i], 0f);
        }

        var texture = new Texture2D(device, BrdfSize, BrdfSize, false, SurfaceFormat.HalfVector4) { Name = "Split-sum BRDF" };
        texture.SetData(packed);
        return texture;
    }

    private static float RadicalInverse(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f;
    }

    public void Dispose()
    {
        _faces.Dispose();
        _base.Dispose();
        _atlas.Dispose();
        foreach (var buffer in _shBuffers)
        {
            buffer.Dispose();
        }

        BrdfLut.Dispose();
        _effect.Dispose();
    }
}
