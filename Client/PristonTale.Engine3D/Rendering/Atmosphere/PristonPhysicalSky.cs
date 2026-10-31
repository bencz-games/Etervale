using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Engine3D.Rendering.Atmosphere;

// What the physical sky draws this frame (PristonRenderer.Atmosphere.cs fills it): the lights in the scene's
// pre-exposed radiance units, the night's sights as display-referred levels (already divided by the display scale),
// the clouds' coverage and density, and the display scale the sky targets decode with.
public readonly record struct PristonSkyInputs(
    Vector3 TowardSun,
    float SunLight,
    Vector3 TowardMoon,
    float MoonLight,
    Vector3 MoonFace,
    float MoonDisc,
    float Stars,
    float SkyTurn,
    float Airglow,
    float Aurora,
    float Rainbow,
    float Cirrus,
    float Seconds,
    float DisplayScale,
    float Coverage,
    float Density,
    float Overcast,
    Vector2 CloudOffset,
    Vector3 TowardFlash,
    float FlashLight,
    CloudQuality Quality);

// The physical sky (Shaders/Passes/Atmosphere.fx): the transmittance and multiple-scattering tables built once, then
// each frame (Update, before the scene's targets are bound) the sky view, the clouds' hemisphere and their shadow on
// the ground; Draw paints the sky into the bound 8-bit sky target, for any view (the scene, the fog's capture, the
// environment's faces). The clouds' hemisphere renders every texel every frame, blended with the last frame's
// (CloudHistory), so moving and changing clouds never leave blocks of older texels.
public sealed class PristonPhysicalSky : IDisposable
{
    public const string AssetName = "Effects/Atmosphere";
    public const int TransmittanceWidth = 256;
    public const int TransmittanceHeight = 64;
    public const int MultipleSize = 32;
    public const int SkyViewWidth = 256;
    public const int SkyViewHeight = 128;
    public const int CloudShadowSize = 256;
    // Kilometres of ground around the camera the clouds' shadow covers, and world units per km (a character, about 50
    // units, stands about 2 m tall).
    public const float CloudShadowExtent = 6f;
    public const float WorldUnitsPerKilometre = 25000f;
    // The share of the last frame's clouds each frame keeps.
    private const float CloudHistory = 0.8f;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly RenderTarget2D _transmittance;
    private readonly RenderTarget2D _multiple;
    private readonly RenderTarget2D _skyView;
    private readonly RenderTarget2D _cloudShadow;
    private readonly Texture3D _noise;
    private RenderTarget2D _clouds;
    private RenderTarget2D _cloudsHistory;
    private bool _tablesBuilt;
    private float _overcast;
    private bool _cloudsValid;
    private int _frame;

    internal PristonPhysicalSky(GraphicsDevice device, ContentManager content, PristonFullscreenTriangle triangle, IClientLog log)
    {
        _device = device;
        _triangle = triangle;
        _effect = PristonShaderLibrary.LoadRequired(content, AssetName, log ?? ConsoleClientLog.Instance);
        _transmittance = Target(TransmittanceWidth, TransmittanceHeight);
        _multiple = Target(MultipleSize, MultipleSize);
        _skyView = Target(SkyViewWidth, SkyViewHeight);
        _cloudShadow = new RenderTarget2D(device, CloudShadowSize, CloudShadowSize, false, SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        _noise = new Texture3D(device, PristonCloudNoise.Size, PristonCloudNoise.Size, PristonCloudNoise.Size, false, SurfaceFormat.Color);
        _noise.SetData(PristonCloudNoise.Generate());
    }

    // The clouds' transmittance toward the sun over CloudShadowExtent km around the camera, and how surfaces read it
    // (CloudShadowParams: x one over the extent in world units, yz the centre, w the strength; 0 without clouds).
    public Texture2D CloudShadow => _cloudShadow;
    public Vector4 CloudShadowParams { get; private set; }

    public bool CloudsDrawn { get; private set; }

    // Builds the frame's tables and clouds; binds its own targets, so the caller binds its own again after it.
    public void Update(in PristonSkyInputs inputs, Vector3 camera, float shadowStrength)
    {
        _frame++;
        Constants(inputs);
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        if (!_tablesBuilt)
        {
            Pass(_transmittance, "Transmittance");
            Set("TransmittanceLut", _transmittance);
            Pass(_multiple, "Multiple");
            _tablesBuilt = true;
        }

        Set("TransmittanceLut", _transmittance);
        Set("MultipleLut", _multiple);
        Pass(_skyView, "SkyView");
        Set("SkyViewLut", _skyView);
        Set("CloudNoise", _noise);

        var size = inputs.Quality switch
        {
            CloudQuality.Low => 256,
            CloudQuality.Medium => 384,
            CloudQuality.High => 512,
            _ => 0
        };
        CloudsDrawn = size > 0 && inputs.Coverage > 0f;
        CloudShadowParams = Vector4.Zero;
        if (!CloudsDrawn)
        {
            _cloudsValid = false;
            return;
        }

        if (_clouds?.Width != size)
        {
            _clouds?.Dispose();
            _cloudsHistory?.Dispose();
            _clouds = Target(size, size);
            _cloudsHistory = Target(size, size);
            _cloudsValid = false;
        }

        (_clouds, _cloudsHistory) = (_cloudsHistory, _clouds);
        Set("Clouds", new Vector4(inputs.Coverage, inputs.Density, _cloudsValid ? 1f - CloudHistory : 1f, _frame % 4096));
        Set("CloudMap", _cloudsHistory);
        Pass(_clouds, "Clouds");
        _cloudsValid = true;

        Pass(_cloudShadow, "CloudShadow");
        var extent = CloudShadowExtent * WorldUnitsPerKilometre;
        CloudShadowParams = new Vector4(1f / extent, camera.X, camera.Z, MathHelper.Clamp(shadowStrength, 0f, 1f));
    }

    // Paints the sky of a view (its translation ignored) into the bound target's viewport.
    public void Draw(Matrix view, Matrix projection)
    {
        view.Translation = Vector3.Zero;
        Set("InverseViewProjection", Matrix.Invert(view * projection));
        Set("CloudMap", CloudsDrawn ? _clouds : null);
        Set("CloudShadowArea", new Vector4(CloudShadowExtent, _overcast, 0f, CloudsDrawn ? 1f : 0f));
        _effect.CurrentTechnique = _effect.Techniques["Sky"];
        _device.BlendState = BlendState.Opaque;
        _triangle.Draw(_effect, DepthStencilState.None);
    }

    private void Constants(in PristonSkyInputs inputs)
    {
        Set("ToSun", new Vector4(inputs.TowardSun, inputs.SunLight));
        Set("ToMoon", new Vector4(inputs.TowardMoon, inputs.MoonLight));
        Set("MoonFace", new Vector4(inputs.MoonFace, inputs.MoonDisc));
        Set("Night", new Vector4(inputs.Stars, inputs.SkyTurn, inputs.Airglow, inputs.Aurora));
        Set("Effects", new Vector4(inputs.Rainbow, inputs.Seconds, inputs.Cirrus, inputs.DisplayScale));
        Set("CloudOffset", new Vector4(inputs.CloudOffset, 0f, 0f));
        Set("Flash", new Vector4(inputs.TowardFlash, inputs.FlashLight));
        Set("CloudShadowArea", new Vector4(CloudShadowExtent, 0f, 0f, 0f));
        // Without drawn clouds the overcast fallback greys the sky.
        _overcast = inputs.Quality == CloudQuality.Off ? inputs.Overcast : 0f;
    }

    // A parameter the compiled techniques may have left out.
    private void Set(string name, Vector4 value) => _effect.Parameters[name]?.SetValue(value);

    private void Set(string name, Matrix value) => _effect.Parameters[name]?.SetValue(value);

    private void Set(string name, Texture value) => _effect.Parameters[name]?.SetValue(value);

    private void Pass(RenderTarget2D target, string technique)
    {
        _device.SetRenderTarget(target);
        Set("Target", new Vector4(target.Width, target.Height, 1f / target.Width, 1f / target.Height));
        _effect.CurrentTechnique = _effect.Techniques[technique];
        _triangle.Draw(_effect, DepthStencilState.None);
    }

    private RenderTarget2D Target(int width, int height) =>
        new(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

    public void Dispose()
    {
        _effect.Dispose();
        _transmittance.Dispose();
        _multiple.Dispose();
        _skyView.Dispose();
        _cloudShadow.Dispose();
        _noise.Dispose();
        _clouds?.Dispose();
        _cloudsHistory?.Dispose();
    }
}
