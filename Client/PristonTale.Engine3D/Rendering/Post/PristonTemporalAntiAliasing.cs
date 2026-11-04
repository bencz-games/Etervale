using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// Temporal anti-aliasing (Shaders/Passes/PostTemporal.fx): the projection is jittered by a Halton (2, 3) sequence of
// eight sub-pixel offsets, surfaces write their screen motion, and the resolve blends this frame into a reprojected
// history (two ping-pong HalfVector4 targets: compressed colour and view depth).
internal sealed class PristonTemporalAntiAliasing : IDisposable
{
    public const string Asset = "Effects/PostTemporal";
    public const int JitterSamples = 8;
    // Weight of this frame at rest and at MovingSpeed pixels of motion per frame.
    public const float RestWeight = 0.08f;
    public const float MovingWeight = 0.25f;
    public const float MovingSpeed = 8f;
    public const float VarianceGamma = 1.25f;
    // Sharpness of the Gaussian reconstruction of the current frame (per pixel squared): a standard deviation of a third
    // of a pixel, so the frame adds little blur to the jitter's own spread over the pixel.
    public const float FilterSharpness = 5f;
    // Added to the final pass's sharpening (capped at 1) while this resolve runs: averaging the jittered frames softens
    // fine texture by about a third, and this sharpening restores most of it.
    public const float CompensatingSharpening = 0.4f;
    public const float DepthTolerance = 0.06f;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectParameter _scene;
    private readonly EffectParameter _motion;
    private readonly EffectParameter _history;
    private readonly EffectParameter _texelSize;
    private readonly EffectParameter _jitterParams;
    private readonly EffectParameter _currentDirectionToWorld;
    private readonly EffectParameter _previousDirectionToClip;
    private readonly EffectParameter _blendParams;
    private readonly EffectParameter _historyParams;
    private readonly RenderTarget2D[] _histories = new RenderTarget2D[2];
    private readonly RenderTargetBinding[][] _bindings = [new RenderTargetBinding[2], new RenderTargetBinding[2]];
    private RenderTarget2D _resolved;
    private int _index;
    private bool _historyValid;
    private int _framesSinceReset;

    public PristonTemporalAntiAliasing(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        var parameters = effect.Parameters;
        _scene = parameters["Scene"];
        _motion = parameters["Motion"];
        _history = parameters["History"];
        _texelSize = parameters["TexelSize"];
        _jitterParams = parameters["JitterParams"];
        _currentDirectionToWorld = parameters["CurrentDirectionToWorld"];
        _previousDirectionToClip = parameters["PreviousDirectionToClip"];
        _blendParams = parameters["BlendParams"];
        _historyParams = parameters["HistoryParams"];
    }

    public void Invalidate() => _historyValid = false;

    // Halton (2, 3) offset of a frame in pixels, centred on the sequence's mean so the image does not drift.
    public static Vector2 JitterPixels(long frame)
    {
        var index = (int)(((frame % JitterSamples) + JitterSamples) % JitterSamples) + 1;
        var meanX = 0f;
        var meanY = 0f;
        for (var i = 1; i <= JitterSamples; i++)
        {
            meanX += Halton(i, 2);
            meanY += Halton(i, 3);
        }

        return new Vector2(Halton(index, 2) - meanX / JitterSamples, Halton(index, 3) - meanY / JitterSamples);
    }

    public static float Halton(int index, int radix)
    {
        var fraction = 1f;
        var result = 0f;
        for (var i = index; i > 0; i /= radix)
        {
            fraction /= radix;
            result += fraction * (i % radix);
        }

        return result;
    }

    // The projection moved by a pixel offset (x right, y down): clip x += 2 dx / width * w, clip y -= 2 dy / height * w.
    public static Matrix Jitter(Matrix projection, Vector2 clipOffset)
    {
        var p = projection;
        p.M11 += clipOffset.X * p.M14;
        p.M21 += clipOffset.X * p.M24;
        p.M31 += clipOffset.X * p.M34;
        p.M41 += clipOffset.X * p.M44;
        p.M12 += clipOffset.Y * p.M14;
        p.M22 += clipOffset.Y * p.M24;
        p.M32 += clipOffset.Y * p.M34;
        p.M42 += clipOffset.Y * p.M44;
        return p;
    }

    public static Vector2 PixelsToClip(Vector2 pixels, int width, int height) =>
        new(2f * pixels.X / width, -2f * pixels.Y / height);

    public RenderTarget2D Resolve(Texture2D scene, Texture2D motion, in PristonTemporalFrame frame)
    {
        EnsureSize(scene.Width, scene.Height);
        var previous = _index;
        _index ^= 1;
        _framesSinceReset = _historyValid ? _framesSinceReset + 1 : 0;
        // After a reset the history holds one frame: a running mean until the exponential weights take over.
        var ramp = 1f / (_framesSinceReset + 1);
        _scene.SetValue(scene);
        _motion.SetValue(motion);
        _history.SetValue(_histories[previous]);
        _texelSize.SetValue(new Vector4(1f / scene.Width, 1f / scene.Height, scene.Width, scene.Height));
        _jitterParams.SetValue(new Vector4(frame.JitterPixels, FilterSharpness, _historyValid ? 1f : 0f));
        _currentDirectionToWorld.SetValue(Matrix.Invert(RotationOnly(frame.View) * frame.Projection));
        _previousDirectionToClip.SetValue(RotationOnly(frame.PreviousView) * frame.PreviousProjection);
        _blendParams.SetValue(new Vector4(MathF.Max(RestWeight, ramp), MathF.Max(MovingWeight, ramp), MovingSpeed, VarianceGamma));
        var ratio = float.IsFinite(frame.PreExposureRatio) && frame.PreExposureRatio > 0f ? frame.PreExposureRatio : 1f;
        _historyParams.SetValue(new Vector4(ratio, DepthTolerance, 0f, 0f));

        _device.SetRenderTargets(_bindings[_index]);
        _device.BlendState = BlendState.Opaque;
        _triangle.Draw(_effect);
        _historyValid = true;
        return _resolved;
    }

    private static Matrix RotationOnly(Matrix view)
    {
        view.Translation = Vector3.Zero;
        return view;
    }

    private void EnsureSize(int width, int height)
    {
        if (_resolved is not null && _resolved.Width == width && _resolved.Height == height)
        {
            return;
        }

        ReleaseTargets();
        for (var i = 0; i < 2; i++)
        {
            var history = new RenderTarget2D(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0,
                RenderTargetUsage.PreserveContents);
            _device.SetRenderTarget(history);
            // No NaN from fresh memory in the first lookups.
            _device.Clear(Color.Transparent);
            _histories[i] = history;
        }

        _resolved = new RenderTarget2D(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        for (var i = 0; i < 2; i++)
        {
            _bindings[i][0] = new RenderTargetBinding(_histories[i]);
            _bindings[i][1] = new RenderTargetBinding(_resolved);
        }

        _historyValid = false;
    }

    public void Dispose()
    {
        ReleaseTargets();
        _effect.Dispose();
    }

    private void ReleaseTargets()
    {
        for (var i = 0; i < 2; i++)
        {
            _histories[i]?.Dispose();
            _histories[i] = null;
        }

        _resolved?.Dispose();
        _resolved = null;
    }
}

// The camera of this and the previous frame (unjittered), this frame's jitter in pixels and the pre-exposure ratio.
internal readonly record struct PristonTemporalFrame(
    Matrix View,
    Matrix Projection,
    Matrix PreviousView,
    Matrix PreviousProjection,
    Vector2 JitterPixels,
    float PreExposureRatio);
