using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Sunlight scattered by a thin height fog, shadowed by the cascades (Shaders/Passes/Volumetric.fx): light shafts through
// gaps and a warm glow toward a low sun, marched at quarter resolution, accumulated over frames and composited over the
// scene with the fog's transmittance.
internal sealed class PristonVolumetricLight : IDisposable
{
    public const string Asset = "Effects/Volumetric";
    // Extinction per world unit at the reference height (a little above the ground around the camera focus): a clear
    // evening haze, a few kilometres of visibility. Its scale height, the longest march (without the distance fog, which
    // covers the medium beyond its start) and the forward-scattering anisotropy.
    public const float Density = 4e-5f;
    public const float ScaleHeight = 600f;
    public const float ReferenceBelowFocus = 50f;
    public const float MaxDistance = 6000f;
    public const float Anisotropy = 0.6f;
    // Share of each frame's march in the accumulated in-scatter: about eight frames of jitter in the estimate.
    public const float HistoryWeight = 0.125f;

    // The composite returns the in-scatter and the transmittance: scene * transmittance + in-scatter.
    private static readonly BlendState Composite = new()
    {
        Name = "PristonVolumetricComposite",
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _march;
    private readonly EffectTechnique _accumulate;
    private readonly EffectTechnique _composite;
    private readonly EffectParameterCollection _parameters;
    private readonly Matrix[] _cascadeMatrices = new Matrix[PristonShadowCascades.MaxCascades];
    private readonly Vector4[] _cascadeAtlas = new Vector4[PristonShadowCascades.MaxCascades];
    private readonly RenderTarget2D[] _history = new RenderTarget2D[2];
    private RenderTarget2D _inscatter;
    private int _current;
    private Matrix _previousViewProjection;
    private uint _previousFrame;
    private bool _historyValid;

    public PristonVolumetricLight(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _march = effect.Techniques["March"];
        _accumulate = effect.Techniques["Accumulate"];
        _composite = effect.Techniques["Composite"];
        _parameters = effect.Parameters;
    }

    // The accumulated in-scatter of the last run (rgb, view depth in alpha), for tests.
    internal Texture2D Accumulated => _history[_current];

    // The history is kept only from one frame to the next (frame is the renderer's frame counter); marchDistance is how far
    // the rays march (the distance fog's start, or MaxDistance without it).
    public void Run(Texture2D depth, Texture2D shadowAtlas, PristonShadowCascades cascades, Matrix view, Matrix projection,
        Vector3 cameraPosition, Vector3 focus, Vector3 towardSun, Vector3 sunRadiance, float marchDistance, uint frame,
        Action bindScene, float densityScale = 1f)
    {
        var width = Math.Max(depth.Width / 4, 1);
        var height = Math.Max(depth.Height / 4, 1);
        EnsureSize(width, height);
        for (var c = 0; c < PristonShadowCascades.MaxCascades; c++)
        {
            var source = Math.Min(c, cascades.Count - 1);
            _cascadeMatrices[c] = cascades.ViewProjections[source];
            _cascadeAtlas[c] = cascades.AtlasRects[source];
        }

        var splits = new Vector4(
            cascades.Splits[0],
            cascades.Splits[Math.Min(1, cascades.Count - 1)],
            cascades.Splits[Math.Min(2, cascades.Count - 1)],
            cascades.Splits[cascades.Count - 1]);
        var viewProjection = view * projection;
        var historyValid = _historyValid && frame == _previousFrame + 1;
        _parameters["DepthMap"].SetValue(depth);
        _parameters["ShadowMap"].SetValue(shadowAtlas);
        _parameters["VolumeSize"].SetValue(new Vector4(width, height, depth.Width, depth.Height));
        _parameters["VolumeProjection"].SetValue(new Vector4(1f / projection.M11, 1f / projection.M22, marchDistance, frame % 64));
        _parameters["CameraRight"].SetValue(new Vector3(view.M11, view.M21, view.M31));
        _parameters["CameraUp"].SetValue(new Vector3(view.M12, view.M22, view.M32));
        _parameters["CameraForward"].SetValue(-new Vector3(view.M13, view.M23, view.M33));
        _parameters["CameraPosition"].SetValue(cameraPosition);
        _parameters["FogDensity"].SetValue(Density * densityScale);
        _parameters["FogFalloff"].SetValue(1f / ScaleHeight);
        _parameters["FogReferenceHeight"].SetValue(focus.Y - ReferenceBelowFocus);
        _parameters["PhaseG"].SetValue(Anisotropy);
        _parameters["SunDirection"].SetValue(towardSun);
        _parameters["SunRadiance"].SetValue(sunRadiance);
        _parameters["HistoryWeight"].SetValue(historyValid ? HistoryWeight : 1f);
        _parameters["PreviousViewProjection"].SetValue(historyValid ? _previousViewProjection : viewProjection);
        _parameters["CascadeCount"].SetValue((float)cascades.Count);
        _parameters["CascadeViewProjection"].SetValue(_cascadeMatrices);
        _parameters["CascadeAtlas"].SetValue(_cascadeAtlas);
        _parameters["CascadeSplits"].SetValue(splits);

        _device.BlendState = BlendState.Opaque;
        _device.SetRenderTarget(_inscatter);
        _effect.CurrentTechnique = _march;
        _triangle.Draw(_effect);

        var previous = _current;
        _current ^= 1;
        _device.SetRenderTarget(_history[_current]);
        _parameters["Inscatter"].SetValue(_inscatter);
        _parameters["History"].SetValue(_history[previous]);
        _effect.CurrentTechnique = _accumulate;
        _triangle.Draw(_effect);

        bindScene();
        _parameters["Inscatter"].SetValue(_history[_current]);
        _device.BlendState = Composite;
        _effect.CurrentTechnique = _composite;
        _triangle.Draw(_effect);
        _device.BlendState = BlendState.Opaque;
        _previousViewProjection = viewProjection;
        _previousFrame = frame;
        _historyValid = true;
    }

    private void EnsureSize(int width, int height)
    {
        if (_inscatter is not null && _inscatter.Width == width && _inscatter.Height == height)
        {
            return;
        }

        _inscatter?.Dispose();
        _inscatter = Target(width, height);
        for (var i = 0; i < _history.Length; i++)
        {
            _history[i]?.Dispose();
            _history[i] = Target(width, height);
        }

        _historyValid = false;
    }

    private RenderTarget2D Target(int width, int height) =>
        new(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

    public void Dispose()
    {
        _inscatter?.Dispose();
        foreach (var target in _history)
        {
            target?.Dispose();
        }

        _effect.Dispose();
    }
}
