using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Tests;

// GPU checks of the motion vectors and the post chain: the surfaces' motion target (camera, jitter and skinned
// previous pose), the automatic exposure's histogram and adaptation, the bloom pyramid on a flat image, the display
// transforms and the colour grade against their CPU references, and the temporal resolve of a still scene.
internal sealed partial class ShaderValidationProbe
{
    private const float MotionTolerance = 0.002f;

    private void VerifyPost()
    {
        VerifyMotion(skinned: false);
        VerifyMotion(skinned: true);
        VerifyAutoExposure();
        VerifyBloom();
        foreach (var mode in Enum.GetValues<TonemapperMode>())
        {
            VerifyDisplayTransform(mode);
        }

        VerifyGrade();
        VerifyTemporalResolve();
        VerifyLightCulling();
        VerifyEnvironment();
        VerifyLampOnActor();
        VerifyReflections();
        VerifyFoliageCoverage();
        VerifyNight();
    }

    // Identity view-projection: the previous one moved by 0.2 clip units in x and a jitter of 0.1 clip units, so the
    // unjittered motion is 0.1 clip units, 0.05 in uv. A skinned quad adds the bone's own 0.2 translation since the
    // previous pose.
    private void VerifyMotion(bool skinned)
    {
        using var motionTarget = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        const uint frame = 7;
        var previous = new PristonSkinPalette(1);
        previous.Matrices[0] = Matrix.Identity;
        previous.Pack(frame - 1);
        previous.Matrices[0] = Matrix.CreateTranslation(skinned ? 0.2f : 0f, 0f, 0f);
        previous.Pack(frame);
        var motion = new PristonMotionFrame(Matrix.CreateTranslation(-0.2f, 0f, 0f), new Vector2(0.1f, 0f), 0, true, frame);
        _shaders.ApplyFrame(Frame(0f, 0f) with { Motion = motion });
        var material = Material(_white, selfIllum: true);
        var effect = skinned
            ? _shaders.SelectSkinnedSurface(material, _white, Vector2.Zero, previous, Vector3.Zero, Vector2.Zero)
            : _shaders.SelectStaticSurface(material, _white, Vector2.Zero, Vector4.Zero, Vector2.Zero);
        GraphicsDevice.SetRenderTargets(new RenderTargetBinding(_target), new RenderTargetBinding(motionTarget));
        GraphicsDevice.Clear(Color.Transparent);
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique.Passes[0].Apply();
        var uv = new Vector2(0.25f, 0.5f);
        if (skinned)
        {
            var vertices = Quad.Select(p => new PristonSkinnedVertex(p - new Vector3(0.2f, 0f, 0f), Vector3.Up, Color.White, uv, 0)).ToArray();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        }
        else
        {
            var vertices = Quad.Select(p => new PristonStaticVertex(p, Vector3.Up, Color.White, uv)).ToArray();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        }

        GraphicsDevice.SetRenderTarget(null);
        var pixels = new HalfVector4[TargetSize * TargetSize];
        motionTarget.GetData(pixels);
        var actual = pixels[(TargetSize / 2) * TargetSize + TargetSize / 2].ToVector4();
        var expectedU = skinned ? 0.15f : 0.05f;
        Assert.True(MathF.Abs(actual.X - expectedU) < MotionTolerance && MathF.Abs(actual.Y) < MotionTolerance
                && MathF.Abs(actual.Z - 1f) < MotionTolerance && MathF.Abs(actual.W - 1f) < MotionTolerance,
            $"motion skin={skinned}: expected ({expectedU}, 0, 1, 1), got {actual}");
        _verified++;
    }

    // A flat image of known luminance: the histogram puts all its weight in one quarter-stop bin, the adaptation snaps
    // to it (no incident share) and stores the multiplier of PristonPhotometry for that EV.
    private void VerifyAutoExposure()
    {
        using var scene = new RenderTarget2D(GraphicsDevice, 64, 36, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        using var effect = Content.Load<Effect>(PristonAutoExposure.Asset).Clone();
        using var exposure = new PristonAutoExposure(GraphicsDevice, effect);
        const float preExposure = 0.01f;
        const float radiance = 0.5f;
        GraphicsDevice.SetRenderTarget(scene);
        GraphicsDevice.Clear(ClearOptions.Target, new Vector4(radiance, radiance, radiance, 1f), 1f, 0);
        GraphicsDevice.SetRenderTarget(null);
        var inputs = new PristonExposureInputs(preExposure, 12f, 0f, 0f, false);
        exposure.Run(scene, inputs, 0.016f, null);
        var state = new Vector4[1];
        exposure.State.GetData<Vector4>(state.AsSpan());
        var expectedEv = MathF.Log2(radiance / preExposure * 100f / 12.5f);
        var multiplier = MathF.Pow(2f, PristonPhotometry.CompensationStops(state[0].X, false)) * PristonPhotometry.ExposureAt(state[0].X) / preExposure;
        Assert.True(MathF.Abs(state[0].X - expectedEv) <= 0.13f && MathF.Abs(state[0].Z - state[0].X) < 1e-4f && state[0].W == 1f,
            $"auto exposure: expected EV {expectedEv:F3}, got {state[0]}");
        Assert.True(MathF.Abs(state[0].Y / multiplier - 1f) < 0.002f, $"auto exposure multiplier {state[0].Y}, CPU {multiplier}");
        _verified += 2;

        // Dusk: the light model falls 4 stops in one frame and the pre-exposed scene keeps its value. The adapted EV follows
        // the meter at once; the slow dark adaptation would leave it 4 stops high and the picture 16 times too dark.
        var before = state[0].X;
        var dusk = new PristonExposureInputs(preExposure * 16f, 8f, 0.6f, 0f, false);
        exposure.Run(scene, dusk, 0.016f, null);
        exposure.State.GetData<Vector4>(state.AsSpan());
        Assert.True(MathF.Abs(state[0].X - (before - 4f)) <= 0.13f, $"auto exposure at dusk: expected EV {before - 4f:F3}, got {state[0].X:F3}");
        _verified++;
    }

    // The pyramid is a convex combination of blurs: a flat image stays flat at every level.
    private void VerifyBloom()
    {
        using var scene = new RenderTarget2D(GraphicsDevice, 128, 128, false, SurfaceFormat.HalfVector4, DepthFormat.None);
        using var triangle = new PristonFullscreenTriangle(GraphicsDevice);
        using var effect = Content.Load<Effect>(PristonBloom.Asset).Clone();
        using var bloom = new PristonBloom(GraphicsDevice, triangle, effect);
        GraphicsDevice.SetRenderTarget(scene);
        GraphicsDevice.Clear(ClearOptions.Target, new Vector4(2f, 1f, 0.5f, 1f), 1f, 0);
        bloom.Run(scene);
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new HalfVector4[bloom.Top.Width * bloom.Top.Height];
        bloom.Top.GetData(pixels);
        var centre = pixels[(bloom.Top.Height / 2) * bloom.Top.Width + bloom.Top.Width / 2].ToVector4();
        Assert.True(Vector3.Distance(new Vector3(centre.X, centre.Y, centre.Z), new Vector3(2f, 1f, 0.5f)) < 0.01f,
            $"bloom of a flat image: expected (2, 1, 0.5), got {centre}");
        _verified++;
    }

    // A flat scene through the final pass at a fixed multiplier of 1 (no bloom, grade or sharpening) matches the CPU
    // display transform within the dither.
    private void VerifyDisplayTransform(TonemapperMode mode)
    {
        foreach (var value in new[] { 0.18f, 0.9f, 4f })
        {
            var expected = PristonDisplayTransform.Apply(mode, new Vector3(value));
            var actual = ComposeFlat(new Vector3(value), UnitPostFrame(mode));
            var expectedByte = PristonColorSpace.LinearToSrgb(expected.X) * 255f;
            Assert.True(MathF.Abs(actual.R - expectedByte) <= 1.6f, $"display transform {mode} at {value}: expected {expectedByte:F1}, got {actual}");
            _verified++;
        }
    }

    // The built-in grade's LUT against its CPU function (trilinear interpolation of a 32^3 table).
    private void VerifyGrade()
    {
        var settings = UnitPostFrame() with { Settings = UnitPostFrame().Settings with { ColorGrading = true } };
        foreach (var value in new[] { 0.05f, 0.18f, 0.5f })
        {
            var encoded = PristonColorSpace.LinearToSrgb(PristonDisplayTransform.PbrNeutral(new Vector3(value)).X);
            var expected = PristonColorGrading.BuiltInLook(new Vector3(encoded)).X * 255f;
            var actual = ComposeFlat(new Vector3(value), settings);
            Assert.True(MathF.Abs(actual.R - expected) <= 2.5f, $"colour grade at {value}: expected {expected:F1}, got {actual}");
            _verified++;
        }
    }

    // Three frames of a still, flat scene through the temporal resolve keep its value.
    private void VerifyTemporalResolve()
    {
        using var output = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize, false, SurfaceFormat.Color, DepthFormat.None);
        using var scene = new PristonSceneTargets(GraphicsDevice, Content, ConsoleClientLog.Instance);
        var unit = UnitPostFrame();
        var frame = unit with { Settings = unit.Settings with { AntiAliasing = AntiAliasingMode.Taa } };
        var projection = Matrix.CreatePerspectiveFieldOfView(1f, 1f, 1f, 100f);
        var temporal = new PristonTemporalFrame(Matrix.Identity, projection, Matrix.Identity, projection, new Vector2(0.25f, -0.25f), 1f);
        var pixels = new Color[TargetSize * TargetSize];
        for (var i = 0; i < 3; i++)
        {
            GraphicsDevice.SetRenderTarget(output);
            scene.Begin(0, true);
            scene.Fill(false, 1f, new Vector3(Grey));
            scene.Compose(frame, temporal, null);
        }

        GraphicsDevice.SetRenderTarget(null);
        output.GetData(pixels);
        var actual = pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
        var expected = DisplayEncode(Grey) * 255f;
        Assert.True(MathF.Abs(actual.R - expected) <= 1.6f, $"temporal resolve of a still scene: expected {expected:F1}, got {actual}");
        _verified++;
    }

    private Color ComposeFlat(Vector3 radiance, PristonPostFrame frame)
    {
        using var output = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize, false, SurfaceFormat.Color, DepthFormat.None);
        using var scene = new PristonSceneTargets(GraphicsDevice, Content, ConsoleClientLog.Instance);
        GraphicsDevice.SetRenderTarget(output);
        scene.Begin(0, false);
        scene.Fill(false, 1f, radiance);
        scene.Compose(frame, default, null);
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[TargetSize * TargetSize];
        output.GetData(pixels);
        return pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
    }
}
