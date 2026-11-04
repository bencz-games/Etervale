using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering.Post;

// The HDR scene to the display, all on the GPU: exposure metering (compute histogram and adaptation), the bloom
// pyramid, the final pass (sharpening, exposure, display transform, sRGB, colour grade, dither; Passes/PostFinal.fx)
// and FXAA or SMAA on the display image. The input is the pre-exposed HDR scene after the temporal resolve.
internal sealed class PristonPostProcessor : IDisposable
{
    public const string FinalAsset = "Effects/PostFinal";

    private readonly GraphicsDevice _device;
    private readonly IClientLog _log;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _final;
    private readonly EffectTechnique _tonemap;
    private readonly EffectTechnique _fxaa;
    private readonly EffectParameter _scene;
    private readonly EffectParameter _bloomTexture;
    private readonly EffectParameter _lut;
    private readonly EffectParameter _ldr;
    private readonly EffectParameter _exposureState;
    private readonly EffectParameter _sourceRect;
    private readonly EffectParameter _screenParams;
    private readonly EffectParameter _toneParams;
    private readonly EffectParameter _debugParams;
    private readonly EffectParameter _mesopicParams;
    private readonly PristonBloom _bloom;
    private readonly PristonSmaa _smaa;
    private RenderTarget2D _ldrTarget;
    private Texture2D _lutTexture;
    private string _lutSource;
    private string _pendingLutSource;
    private Task<Color[]> _pendingLut;
    private uint _frame;

    public PristonPostProcessor(GraphicsDevice device, ContentManager content, IClientLog log, PristonFullscreenTriangle triangle)
    {
        _device = device;
        _log = log;
        _triangle = triangle;
        _final = PristonShaderLibrary.LoadRequired(content, FinalAsset, log);
        _tonemap = _final.Techniques["Tonemap"];
        _fxaa = _final.Techniques["Fxaa"];
        _scene = _final.Parameters["Scene"];
        _bloomTexture = _final.Parameters["Bloom"];
        _lut = _final.Parameters["Lut"];
        _ldr = _final.Parameters["Ldr"];
        _exposureState = _final.Parameters["ExposureState"];
        _sourceRect = _final.Parameters["SourceRect"];
        _screenParams = _final.Parameters["ScreenParams"];
        _toneParams = _final.Parameters["ToneParams"];
        _debugParams = _final.Parameters["DebugParams"];
        _mesopicParams = _final.Parameters["MesopicParams"];
        _bloom = new PristonBloom(device, triangle, PristonShaderLibrary.LoadRequired(content, PristonBloom.Asset, log));
        _smaa = new PristonSmaa(device, triangle, PristonShaderLibrary.LoadRequired(content, PristonSmaa.Asset, log));
        Exposure = new PristonAutoExposure(device, PristonShaderLibrary.LoadRequired(content, PristonAutoExposure.Asset, log));
    }

    public PristonAutoExposure Exposure { get; }

    // The profiler of the frame (sections Exposure, Bloom, DisplayTransform, PostAntiAliasing), when set.
    public PristonFrameProfiler Profiler { get; set; }

    public void Process(Texture2D scene, in PristonPostFrame frame, Action bindOutput)
    {
        var settings = frame.Settings;
        EnsureLut(settings);

        Profiler?.Begin(RenderSection.Exposure);
        var fixedMultiplier = 0f;
        if (settings.AutoExposure)
        {
            Exposure.Run(scene, frame.Exposure, frame.DeltaSeconds, null);
        }
        else
        {
            fixedMultiplier = FixedMultiplier(frame.Exposure);
        }

        Profiler?.End(RenderSection.Exposure);

        var bloomStrength = BloomStrength(settings.Bloom);
        Profiler?.Begin(RenderSection.Bloom);
        if (bloomStrength > 0f)
        {
            _bloom.Run(scene);
        }

        Profiler?.End(RenderSection.Bloom);

        var filter = settings.AntiAliasing is AntiAliasingMode.Fxaa or AntiAliasingMode.Smaa;
        Profiler?.Begin(RenderSection.DisplayTransform);
        var viewport = frame.Viewport;
        if (filter)
        {
            EnsureLdr(viewport.Width, viewport.Height);
            _device.SetRenderTarget(_ldrTarget);
        }
        else
        {
            bindOutput();
        }

        _device.BlendState = BlendState.Opaque;
        _scene.SetValue(scene);
        _bloomTexture.SetValue(bloomStrength > 0f ? _bloom.Top : scene);
        _lut.SetValue(_lutTexture);
        _exposureState.SetValue(Exposure.State);
        _sourceRect.SetValue(frame.SourceRect);
        _screenParams.SetValue(new Vector4(scene.Width, scene.Height, _frame++ % 64, settings.EffectiveSharpening));
        _toneParams.SetValue(new Vector4((float)settings.Tonemapper, settings.ColorGrading ? 1f : 0f, bloomStrength, fixedMultiplier));
        _debugParams.SetValue(new Vector4(frame.DebugExposure ? 1f : 0f, 1f / frame.Exposure.PreExposure, 0f, 0f));
        _mesopicParams.SetValue(new Vector4(frame.Exposure.NightHue == Vector3.Zero ? Vector3.One : frame.Exposure.NightHue, 0f));
        _final.CurrentTechnique = _tonemap;
        _triangle.Draw(_final);
        Profiler?.End(RenderSection.DisplayTransform);

        if (!filter)
        {
            return;
        }

        Profiler?.Begin(RenderSection.PostAntiAliasing);
        if (settings.AntiAliasing == AntiAliasingMode.Smaa)
        {
            _smaa.Apply(_ldrTarget, bindOutput);
        }
        else
        {
            bindOutput();
            _ldr.SetValue(_ldrTarget);
            _screenParams.SetValue(new Vector4(_ldrTarget.Width, _ldrTarget.Height, _frame % 64, 0f));
            _final.CurrentTechnique = _fxaa;
            _triangle.Draw(_final);
        }

        Profiler?.End(RenderSection.PostAntiAliasing);
    }

    // The multiplier of a fixed exposure at the light model's incident reading (Shaders/Passes/PostExposure.fx Adapt).
    internal static float FixedMultiplier(in PristonExposureInputs inputs)
    {
        var compensation = inputs.CompensationStops + PristonPhotometry.CompensationStops(inputs.IncidentEv100, inputs.Underground);
        return MathF.Pow(2f, compensation) * PristonPhotometry.ExposureAt(inputs.IncidentEv100) / inputs.PreExposure;
    }

    internal static float BloomStrength(BloomQuality quality) => quality switch
    {
        BloomQuality.Low => 0.03f,
        BloomQuality.High => 0.05f,
        _ => 0f
    };

    // A .cube file is read on the thread pool; the previous grade (or the built-in one) stays bound until it is ready.
    private void EnsureLut(PristonPostSettings settings)
    {
        var source = settings.ColorGradingLut ?? string.Empty;
        if (_lutTexture is null)
        {
            _lutTexture = PristonColorGrading.Upload(_device, PristonColorGrading.BuildPixels(string.Empty, _log));
            _lutSource = string.Empty;
        }

        if (_pendingLut is { IsCompleted: true } finished)
        {
            _pendingLut = null;
            if (finished.IsCompletedSuccessfully)
            {
                _lutTexture.Dispose();
                _lutTexture = PristonColorGrading.Upload(_device, finished.Result);
                _lutSource = _pendingLutSource;
            }
            else
            {
                _log.Warn($"[Rendering] colour grade '{_pendingLutSource}' failed: {finished.Exception?.GetBaseException().Message}");
                _lutSource = _pendingLutSource;
            }
        }

        if (_lutSource == source || _pendingLut is not null && _pendingLutSource == source)
        {
            return;
        }

        if (_pendingLut is not null)
        {
            return;
        }

        var log = _log;
        _pendingLutSource = source;
        _pendingLut = Task.Run(() => PristonColorGrading.BuildPixels(source, log));
    }

    private void EnsureLdr(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (_ldrTarget is not null && _ldrTarget.Width == width && _ldrTarget.Height == height)
        {
            return;
        }

        _ldrTarget?.Dispose();
        _ldrTarget = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
    }

    public void Dispose()
    {
        _bloom.Dispose();
        _smaa.Dispose();
        Exposure.Dispose();
        _ldrTarget?.Dispose();
        _lutTexture?.Dispose();
        _final.Dispose();
    }
}

// The post-processing options of the frame (from ClientSettings).
internal sealed record PristonPostSettings(
    AntiAliasingMode AntiAliasing,
    BloomQuality Bloom,
    TonemapperMode Tonemapper,
    bool ColorGrading,
    string ColorGradingLut,
    float Sharpening,
    bool AutoExposure)
{
    public static PristonPostSettings From(ClientSettings settings) => new(
        settings.AntiAliasing,
        settings.Bloom,
        settings.Tonemapper,
        settings.ColorGrading,
        settings.ColorGradingLut,
        settings.Sharpening,
        settings.AutoExposure);

    public float EffectiveSharpening => AntiAliasing == AntiAliasingMode.Taa
        ? MathF.Min(1f, Sharpening + PristonTemporalAntiAliasing.CompensatingSharpening)
        : Sharpening;
}

// One frame's inputs of the post chain: the options, the light model's exposure inputs, the viewport part of the scene
// (uv) and the caller's viewport, the time since the last frame, and the EV100 false-colour view.
internal readonly record struct PristonPostFrame(
    PristonPostSettings Settings,
    PristonExposureInputs Exposure,
    Vector4 SourceRect,
    Viewport Viewport,
    float DeltaSeconds,
    bool DebugExposure);
