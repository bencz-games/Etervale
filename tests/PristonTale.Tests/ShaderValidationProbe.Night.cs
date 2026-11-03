using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Tests;

// GPU checks of the night and the low sun:
//  - a night dome (dark, with a bright horizon band and a moon disk) far below the light model's sky light: the
//    reflections are never brighter than the visible dome, the rest of the sky light is a uniform fill over the upper
//    hemisphere, so a dark rough ground facing the dome's moon reflects no more of the sky, relative to its diffuse light,
//    than under a uniform sky of the same irradiance, and the reflection has the colour of the sky's irradiance;
//  - mesopic vision: a dim grey takes the night's hue through the final pass (against the CPU reference), a day grey
//    stays neutral;
//  - the volumetric light: the composite is scene * transmittance + in-scatter with the exact optical depth of the
//    height fog, and the shafts of a finely striped shadow map accumulate into a smooth in-scatter.
internal sealed partial class ShaderValidationProbe
{
    private static readonly Vector3 NightSky = new(0.24f, 0.29f, 0.43f);
    private static readonly Vector3 NightGround = new(0.17f, 0.2f, 0.3f);

    private void VerifyNight()
    {
        VerifyNightReflections();
        VerifyMesopicVision();
        VerifyVolumetricComposite();
        VerifyVolumetricShafts();
        VerifyVolumetricMistOverSky();
    }

    private void VerifyNightReflections()
    {
        using var triangle = new PristonFullscreenTriangle(GraphicsDevice);
        using var uniformEffect = Content.Load<Effect>(PristonEnvironmentLighting.Asset).Clone();
        using var nightEffect = Content.Load<Effect>(PristonEnvironmentLighting.Asset).Clone();
        using var uniform = new PristonEnvironmentLighting(GraphicsDevice, triangle, uniformEffect);
        using var night = new PristonEnvironmentLighting(GraphicsDevice, triangle, nightEffect);
        using var batch = new SpriteBatch(GraphicsDevice);
        uniform.BeginFaces(new Color(6, 6, 8));
        uniform.Build(0, 1f, NightSky, NightGround);
        night.BeginFaces(new Color(6, 6, 8));
        DrawNightDome(batch);
        night.Build(0, 1f, NightSky, NightGround);

        GraphicsDevice.SetRenderTarget(null);
        var sh = new Vector4[PristonEnvironmentLighting.ShEntries];
        night.Sh.GetData<Vector4>(sh.AsSpan());
        var up = new Vector3(sh[0].X, sh[0].Y, sh[0].Z) * 0.282095f + new Vector3(sh[1].X, sh[1].Y, sh[1].Z) * (0.488603f * 2f / 3f);
        // A dark rough dielectric (albedo 0.05, the original terrain's) seen at n.v = 0.5, reflecting toward the dome's moon.
        var eye = new Vector3(0f, 500f, -866.5f);
        var albedo = PristonColorSpace.SrgbToLinear(63f / 255f);
        using var dark = Texture(new Color(63, 63, 63, 255));
        var material = Material(dark, selfIllum: false);
        var (uniformSpecular, uniformDiffuse) = AmbientSplit(uniform, material, dark, eye, albedo);
        var (nightSpecular, nightDiffuse) = AmbientSplit(night, material, dark, eye, albedo);
        var uniformRatio = Luminance(uniformSpecular) / Luminance(uniformDiffuse);
        var nightRatio = Luminance(nightSpecular) / Luminance(nightDiffuse);
        var hue = nightSpecular.Z / nightSpecular.X;
        var skyHue = up.Z / up.X;
        Assert.True(sh[9].X <= 1.001f, $"night reflections magnify the visible dome by {sh[9].X}");
        Assert.True(MathF.Abs(Luminance(up) / Luminance(NightSky) - 1f) < 0.08f, $"night sky irradiance {up}, light model {NightSky}");
        Assert.True(nightRatio <= uniformRatio * 1.15f,
            $"night ground specular / diffuse {nightRatio:F3}, under a uniform sky {uniformRatio:F3}: the dome's moon and horizon are magnified");
        Assert.True(MathF.Abs(hue / skyHue - 1f) < 0.1f, $"night reflection blue/red {hue:F2}, sky irradiance {skyHue:F2}");
        _verified += 4;
    }

    // Display values of the faces (+X, -X, +Y, -Y, +Z, -Z, 64 px each): a horizon glow on the side faces, eight rows
    // above the horizon line, and a moon disk about 15 degrees up on +Z.
    private void DrawNightDome(SpriteBatch batch)
    {
        var size = PristonEnvironmentLighting.FaceSize;
        GraphicsDevice.Viewport = new Viewport(0, 0, size * 6, size);
        batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        foreach (var face in new[] { 0, 1, 4, 5 })
        {
            batch.Draw(_white, new Rectangle(face * size, size / 2 - 8, size, 8), new Color(70, 80, 95));
        }

        batch.Draw(_white, new Rectangle(4 * size + size / 2 - 3, 20, 6, 6), new Color(255, 255, 250));
        batch.End();
    }

    // The ambient light of the centre texel without sun: the specular (Specular view) and the diffuse ((Lighting view -
    // specular) * albedo).
    private (Vector3 Specular, Vector3 Diffuse) AmbientSplit(PristonEnvironmentLighting environment, PristonMaterialRuntime material,
        Texture2D texture, Vector3 eye, float albedo)
    {
        var frame = Frame(ambient: 0f, sun: 0f) with
        {
            CameraPosition = eye,
            Environment = new PristonEnvironmentFrame(environment.Atlas, environment.Sh, environment.BrdfLut)
        };
        _shaders.ApplyFrame(frame with { DebugView = PristonDebugView.Specular });
        var specular = DrawCentre(Surface(material, texture, Vector2.Zero, false));
        _shaders.ApplyFrame(frame with { DebugView = PristonDebugView.Lighting });
        var lighting = DrawCentre(Surface(material, texture, Vector2.Zero, false));
        return (specular, (lighting - specular) * albedo);
    }

    private Vector3 DrawCentre(Effect effect)
    {
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(Color.Transparent);
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique.Passes[0].Apply();
        var vertices = Quad.Select(p => new PristonStaticVertex(p, Vector3.Up, Color.White, new Vector2(0.25f, 0.5f))).ToArray();
        GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        GraphicsDevice.SetRenderTarget(null);
        _target.GetData(_pixels);
        var pixel = _pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
        return new Vector3(pixel.X, pixel.Y, pixel.Z);
    }

    // A flat grey at night (EV100 -2, a fixed multiplier of 1) matches the CPU reference of mesopic vision and the display
    // transform, and is bluer than it is red; the same grey by day is neutral.
    private void VerifyMesopicVision()
    {
        var nightHue = new PristonDayNightCycle().NightHue;
        foreach (var (ev, night) in new[] { (-2f, true), (10f, false) })
        {
            var preExposure = PristonPhotometry.ExposureAt(ev);
            var settings = new PristonPostSettings(AntiAliasingMode.Off, BloomQuality.Off, TonemapperMode.PbrNeutral, false, string.Empty, 0f, false);
            var inputs = new PristonExposureInputs(preExposure, ev, 0f, -PristonPhotometry.DayKeyStops, true, nightHue);
            var frame = new PristonPostFrame(settings, inputs, Vector4.Zero, default, 0f, false);
            var grey = new Vector3(0.18f);
            var expected = PristonDisplayTransform.PbrNeutral(PristonDisplayTransform.Mesopic(grey, 1f / preExposure, nightHue));
            var actual = ComposeFlat(grey, frame);
            var expectedBytes = new Vector3(PristonColorSpace.LinearToSrgb(expected.X), PristonColorSpace.LinearToSrgb(expected.Y), PristonColorSpace.LinearToSrgb(expected.Z)) * 255f;
            Assert.True(MathF.Abs(actual.R - expectedBytes.X) <= 1.6f && MathF.Abs(actual.B - expectedBytes.Z) <= 1.6f,
                $"mesopic vision at EV {ev}: expected {expectedBytes}, got {actual}");
            Assert.True(night ? actual.B - actual.R >= 8 : Math.Abs(actual.B - actual.R) <= 1,
                $"mesopic vision at EV {ev}: a grey shows as {actual}");
            _verified += 2;
        }
    }

    // A wall 2000 units ahead of a camera at the fog's reference height, its radiance known, an unshadowed sun ahead and
    // up: the centre pixel is scene * T + E * phase * (1 - T), T = exp(-density * distance).
    private void VerifyVolumetricComposite()
    {
        var sun = Vector3.Normalize(new Vector3(0.2f, 0.25f, 1f));
        var (result, _) = RunVolumetric(striped: false, sun, firstFrame: 1, frames: 1);
        const float distance = 2000f;
        var transmittance = MathF.Exp(-PristonVolumetricLight.Density * distance);
        var g = PristonVolumetricLight.Anisotropy;
        var cosine = Vector3.Dot(Vector3.UnitZ, sun);
        var phase = (1f - g * g) / (4f * MathF.PI * MathF.Pow(1f + g * g - 2f * g * cosine, 1.5f));
        var expected = VolumetricScene * transmittance + VolumetricSun * (MathF.PI * phase * (1f - transmittance));
        Assert.True(Vector3.Distance(result, expected) < 0.01f * expected.Length(),
            $"volumetric composite: expected {expected} (T {transmittance:F3}), got {result}");
        _verified++;
    }

    // The sky already holds the air's own haze: without a mist the volumetric pass leaves an unshadowed sky as it is; a
    // mist of four times the medium veils and dims it by the three quarters the sky lacks, T = exp(-density * scale * d)
    // over a march of 2000 units (farther, the quarter-resolution texels' rays cross the height falloff unevenly):
    // sky * T^(3/4) + E * phase * (T^(1/4) - T).
    private void VerifyVolumetricMistOverSky()
    {
        var sun = Vector3.Normalize(new Vector3(0.2f, 0.25f, 1f));
        const float march = 2000f;
        var (clear, _) = RunVolumetric(striped: false, sun, firstFrame: 1, frames: 1, depthValue: PristonSceneTargets.SkyDepth, marchDistance: march);
        Assert.True(Vector3.Distance(clear, VolumetricScene) < 0.01f * VolumetricScene.Length(), $"volumetric over a clear sky: expected {VolumetricScene}, got {clear}");
        const float mist = 4f;
        var (misty, _) = RunVolumetric(striped: false, sun, firstFrame: 1, frames: 1, depthValue: PristonSceneTargets.SkyDepth, densityScale: mist, marchDistance: march);
        var transmittance = MathF.Exp(-PristonVolumetricLight.Density * mist * march);
        var g = PristonVolumetricLight.Anisotropy;
        var cosine = Vector3.Dot(Vector3.UnitZ, sun);
        var phase = (1f - g * g) / (4f * MathF.PI * MathF.Pow(1f + g * g - 2f * g * cosine, 1.5f));
        var expected = VolumetricScene * MathF.Pow(transmittance, 1f - 1f / mist)
            + VolumetricSun * (MathF.PI * phase * (MathF.Pow(transmittance, 1f / mist) - transmittance));
        Assert.True(Vector3.Distance(misty, expected) < 0.01f * expected.Length(), $"volumetric mist over the sky: expected {expected}, got {misty}");
        _verified += 2;
    }

    // Shadow stripes two texels wide alias into grain in one frame's 16 jittered samples. Against the mean of 64 frames
    // of jitter (the converged in-scatter), the accumulated in-scatter of 24 successive frames deviates less than 40 % as
    // much as a single frame does.
    private void VerifyVolumetricShafts()
    {
        var sun = Vector3.Normalize(new Vector3(0.6f, 0.35f, 1f));
        const int jitters = 64;
        // Each run is a fresh history: one frame's march.
        var single = RunVolumetric(striped: true, sun, firstFrame: 1, frames: 1).Accumulated;
        var reference = new float[single.Length];
        for (var k = 0; k < jitters; k++)
        {
            var march = k == 0 ? single : RunVolumetric(striped: true, sun, firstFrame: (uint)(k + 1), frames: 1).Accumulated;
            for (var i = 0; i < march.Length; i++)
            {
                reference[i] += march[i] / jitters;
            }
        }

        var accumulated = RunVolumetric(striped: true, sun, firstFrame: 1, frames: 24).Accumulated;
        var level = reference.Average();
        var singleError = single.Zip(reference, (a, b) => MathF.Abs(a - b)).Average();
        var accumulatedError = accumulated.Zip(reference, (a, b) => MathF.Abs(a - b)).Average();
        Assert.True(singleError > 0.02f * level, $"the striped shadow map gives no grain to test ({singleError / level:P1})");
        Assert.True(accumulatedError < 0.4f * singleError,
            $"volumetric shafts: error {accumulatedError / level:P2} of the in-scatter accumulated, {singleError / level:P2} in one frame");
        _verified += 2;
    }

    private static readonly Vector3 VolumetricScene = new(0.2f, 0.3f, 0.4f);
    private static readonly Vector3 VolumetricSun = new(2f, 1.2f, 0.6f);

    // Runs the volumetric light over a 128x128 scene of the given radiance at view depth 2000 for some successive frames;
    // returns the centre pixel and the luminance of the accumulated quarter-resolution in-scatter.
    private (Vector3 Centre, float[] Accumulated) RunVolumetric(bool striped, Vector3 towardSun, uint firstFrame, int frames, float depthValue = 2000f,
        float densityScale = 1f, float marchDistance = PristonVolumetricLight.MaxDistance)
    {
        const int size = 128;
        using var triangle = new PristonFullscreenTriangle(GraphicsDevice);
        using var effect = Content.Load<Effect>(PristonVolumetricLight.Asset).Clone();
        using var volumetric = new PristonVolumetricLight(GraphicsDevice, triangle, effect);
        using var scene = new RenderTarget2D(GraphicsDevice, size, size, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        using var depth = new Texture2D(GraphicsDevice, size, size, false, SurfaceFormat.Single);
        depth.SetData(Enumerable.Repeat(depthValue, size * size).ToArray());
        const int shadowSize = 256;
        using var shadow = new Texture2D(GraphicsDevice, shadowSize, shadowSize, false, SurfaceFormat.Single);
        var stored = new float[shadowSize * shadowSize];
        for (var i = 0; i < stored.Length; i++)
        {
            stored[i] = striped && (i % shadowSize / 2 + i / shadowSize / 2) % 2 == 0 ? 0f : 1f;
        }

        shadow.SetData(stored);
        var position = new Vector3(0f, 100f, 0f);
        var view = Matrix.CreateLookAt(position, position + Vector3.UnitZ, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(1f, 1f, 1f, 20000f);
        var cascades = new PristonShadowCascades();
        cascades.Configure(1, shadowSize);
        cascades.Fit(0, view, projection, -towardSun);
        var focus = position + new Vector3(0f, PristonVolumetricLight.ReferenceBelowFocus, 0f);
        for (var frame = firstFrame; frame < firstFrame + frames; frame++)
        {
            GraphicsDevice.SetRenderTarget(scene);
            GraphicsDevice.Clear(ClearOptions.Target, new Vector4(VolumetricScene, 1f), 1f, 0);
            volumetric.Run(depth, shadow, cascades, view, projection, position, focus, towardSun, VolumetricSun,
                marchDistance, frame, () => GraphicsDevice.SetRenderTarget(scene), densityScale);
        }

        GraphicsDevice.SetRenderTarget(null);
        var pixels = new HalfVector4[size * size];
        scene.GetData(pixels);
        var centre = pixels[(size / 2) * size + size / 2].ToVector4();
        var low = volumetric.Accumulated;
        var texels = new HalfVector4[low.Width * low.Height];
        low.GetData(texels);
        return (new Vector3(centre.X, centre.Y, centre.Z), texels.Select(t => Luminance(t.ToVector4())).ToArray());
    }

    private static float Luminance(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    private static float Luminance(Vector4 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
}
