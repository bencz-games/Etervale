using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Tests;

// GPU checks of the SM 6 shader library on the running backend (Vulkan): every surface technique of the static and
// skinned effects, the material model's inputs from the adapter (base and layer, alpha cutout, UV scroll, emissive,
// rim), the lighting (ambient, sun with its shadow map), the distance fog (flat and sky capture), skinning, the shadow
// casters' depth, the display encoding, the unlit effect and the scene composition. Each draw covers the centre texel
// of a 17x17 float target; expected values come from the same formulas in C#.
internal sealed partial class ShaderValidationProbe : Game
{
    private const int TargetSize = 17;
    // Clear(Color) stores 8-bit values, so the cleared float target holds 63/255.
    private const float ClearValue = 63f / 255f;
    private const float Tolerance = 0.004f;
    private const float AlphaCutoff = 60f / 255f;
    private static readonly Vector3[] Quad =
        [new(-1, -1, 0.5f), new(1, -1, 0.5f), new(1, 1, 0.5f), new(-1, -1, 0.5f), new(1, 1, 0.5f), new(-1, 1, 0.5f)];
    // Above the quad's centre, so the view vector is the surface normal (up).
    private static readonly Vector3 Eye = new(0f, 1000f, 0.5f);

    private readonly Vector4[] _pixels = new Vector4[TargetSize * TargetSize];
    private bool _completed;
    private int _verified;
    private PristonShaderLibrary _shaders = null!;
    private RenderTarget2D _target = null!;
    private Texture2D _white = null!;
    private Texture2D _halves = null!;
    private Texture2D _grey = null!;
    private PristonSkinPalette _palette = null!;

    public ShaderValidationProbe(string contentRoot)
    {
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
            GraphicsProfile = GraphicsProfile.HiDef
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
    }

    private static float Grey => PristonColorSpace.SrgbToLinear(128f / 255f);

    protected override void Draw(GameTime gameTime)
    {
        if (_completed)
        {
            return;
        }

        _white = Texture(Color.White);
        _grey = Texture(new Color(128, 128, 128, 255));
        _halves = new Texture2D(GraphicsDevice, 2, 1, false, SurfaceFormat.ColorSRgb);
        _halves.SetData(new[] { Color.White, Color.Transparent });
        _target = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize, false, SurfaceFormat.Vector4, DepthFormat.None);
        _shaders = new PristonShaderLibrary(Content, ConsoleClientLog.Instance, _white);
        _palette = new PristonSkinPalette(1);
        _palette.Matrices[0] = Matrix.Identity;
        _palette.Pack();

        foreach (var skinned in new[] { false, true })
        {
            VerifyEmissive(skinned);
            VerifyCutoutAndScroll(skinned);
            VerifyLayers(skinned);
            VerifyAmbientAndSun(skinned);
            VerifySilhouetteNormal(skinned);
            VerifyVertexLight(skinned);
            VerifySunShadowReceiver(skinned);
            VerifyFog(skinned);
            VerifyCasters(skinned);
            VerifyDebugAlbedo(skinned);
        }

        VerifySkinning();
        VerifyRim();
        VerifySky();
        VerifyDisplayOutput();
        VerifyUnlit();
        VerifyComposition();
        VerifyPost();
        Console.WriteLine($"SHADERS {_verified}/{_verified} GPU checks passed; {GraphicsDevice.Adapter.DeviceName}; Content={Content.RootDirectory}");
        _target.Dispose();
        _white.Dispose();
        _grey.Dispose();
        _halves.Dispose();
        _completed = true;
        Exit();
    }

    // Self-illuminated: the albedo is emitted as is, whatever the light; blink multiplies it by the pulse.
    private void VerifyEmissive(bool skinned)
    {
        _shaders.ApplyFrame(Frame(ambient: 0.7f, sun: 0.9f));
        var effect = Surface(Material(_grey, selfIllum: true), _grey, Vector2.Zero, skinned);
        Verify(effect, skinned, new Vector3(Grey), $"emissive skin={skinned}");
    }

    // Masked coverage: base alpha times vertex opacity against the 60/255 cutoff; the scroll moves the base layer.
    private void VerifyCutoutAndScroll(bool skinned)
    {
        _shaders.ApplyFrame(Frame(ambient: 0f, sun: 0f));
        foreach (var alphaTest in new[] { false, true })
        {
            foreach (var scroll in new[] { 0f, 0.5f })
            {
                var effect = Surface(Material(_halves, selfIllum: true, alphaTest: alphaTest), _halves, new Vector2(scroll, 0f), skinned);
                var texel = scroll == 0f ? 1f : 0f;
                var expected = alphaTest && scroll != 0f ? new Vector3(ClearValue) : new Vector3(texel);
                Verify(effect, skinned, expected, $"cutout skin={skinned} alpha={alphaTest} scroll={scroll}");
            }
        }
    }

    // Second layer: multiplied into the albedo, or added to it.
    private void VerifyLayers(bool skinned)
    {
        _shaders.ApplyFrame(Frame(ambient: 0f, sun: 0f));
        var multiply = Material(_white, selfIllum: true, layer: new PristonMaterialLayer(_grey, PristonStageOperation.Modulate, 0));
        Verify(Surface(multiply, _white, Vector2.Zero, skinned), skinned, new Vector3(Grey), $"layer multiply skin={skinned}");
        var add = Material(_grey, selfIllum: true, layer: new PristonMaterialLayer(_grey, PristonStageOperation.Add, 0));
        Verify(Surface(add, _grey, Vector2.Zero, skinned), skinned, new Vector3(2f * Grey), $"layer add skin={skinned}");
    }

    // Lit, default roughness: hemispherical ambient plus the sun along the normal (view = light = normal).
    private void VerifyAmbientAndSun(bool skinned)
    {
        foreach (var (ambient, sun) in new[] { (0.5f, 0f), (0f, 1f), (0.3f, 0.8f) })
        {
            _shaders.ApplyFrame(Frame(ambient, sun));
            var material = Material(_grey, selfIllum: false);
            var expected = LitReference(Grey, ambient, sun, sunVisibility: 1f, material.Description.Roughness);
            Verify(Surface(material, _grey, Vector2.Zero, skinned), skinned, new Vector3(expected), $"lit skin={skinned} ambient={ambient} sun={sun}");
        }
    }

    // Near a silhouette a smooth vertex normal leans away from the viewer while staying on the visible side of its face.
    // It must keep its direction: flipping it by the view lit the far side of limbs and boxes as if they faced the sun.
    // The quad faces +z and is seen at a grazing angle; the normal leans down, away from the sun overhead, so it is unlit.
    private void VerifySilhouetteNormal(bool skinned)
    {
        var eye = new Vector3(0f, 1000f, 200f);
        var frame = new PristonFrameContext(Matrix.Identity, Matrix.Identity, eye, 0,
            new PristonLighting(Vector3.Zero, new PristonDirectionalLight(Vector3.Down, Vector3.One), []),
            Matrix.Identity, _white, false, 0, PristonDistanceFog.Disabled);
        _shaders.ApplyFrame(frame);
        var leaning = Vector3.Normalize(new Vector3(0f, -0.3f, 1f));
        var material = Material(_grey, selfIllum: false);
        Verify(Surface(material, _grey, Vector2.Zero, skinned), skinned, Vector3.Zero, $"silhouette normal keeps its side skin={skinned}", normal: leaning);
    }

    // The vertex colour (the stage's authored light; scenery models take it too, PristonStageBakedLight) scales the
    // albedo in linear light on both paths, so an animated model matches the stage surface beside it.
    private void VerifyVertexLight(bool skinned)
    {
        _shaders.ApplyFrame(Frame(0.3f, 0.8f));
        var material = Material(_grey, selfIllum: false);
        var light = new Color(150, 150, 150);
        var linear = PristonColorSpace.SrgbToLinear(150f / 255f);
        var roughness = material.Description.Roughness;
        // The authored light also occludes the sky's reflection (the ambient's f0 term), viewed along the normal.
        var occlusion = Math.Clamp(MathF.Pow(1f + linear, MathF.Pow(2f, -16f * roughness - 1f)) - 1f + linear, 0f, 1f);
        var expected = LitReference(Grey * linear, 0.3f, 0.8f, sunVisibility: 1f, roughness) - 0.3f * 0.04f * (1f - occlusion);
        Verify(Surface(material, _grey, Vector2.Zero, skinned), skinned, new Vector3(expected), $"vertex light skin={skinned}", vertexColor: light);
    }

    // A shadow map cleared to depth 0 occludes the receiver: no sunlight reaches it, the ambient remains.
    private void VerifySunShadowReceiver(bool skinned)
    {
        using var occluder = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize, false, SurfaceFormat.Single, DepthFormat.None);
        GraphicsDevice.SetRenderTarget(occluder);
        GraphicsDevice.Clear(Color.Black);
        GraphicsDevice.SetRenderTarget(null);
        _shaders.ApplyFrame(Frame(0.2f, 1f) with { ShadowMap = occluder, ShadowsEnabled = true });
        var material = Material(_grey, selfIllum: false);
        var expected = LitReference(Grey, 0.2f, 1f, sunVisibility: 0f, material.Description.Roughness);
        Verify(Surface(material, _grey, Vector2.Zero, skinned), skinned, new Vector3(expected), $"sun shadow receiver skin={skinned}");
    }

    // Opacity cap * (1 - exp(-density * d)) beyond the start, toward the flat colour or the captured sky behind.
    private void VerifyFog(bool skinned)
    {
        // The identity projection gives a view depth of 1; the start puts the surface 1000 units into the fog.
        var fog = PristonDistanceFog.Atmospheric with { StartDistance = -999f, Color = new Vector3(0.5f) };
        var opacity = fog.Level(1f);
        Assert.Near(0.85f * (1f - MathF.Exp(-0.3f)), opacity, 1e-5f);
        _shaders.ApplyFrame(Frame(0f, 0f) with { Fog = fog });
        var material = Material(_white, selfIllum: true);
        Verify(Surface(material, _white, Vector2.Zero, skinned), skinned, new Vector3(1f + (0.5f - 1f) * opacity), $"flat fog skin={skinned}");

        // The capture is display-referred (8-bit sRGB); the fog decodes it.
        using var capture = new RenderTarget2D(GraphicsDevice, 128, 32, false, SurfaceFormat.Color, DepthFormat.None);
        GraphicsDevice.SetRenderTarget(capture);
        var captured = new Color(51, 102, 153);
        GraphicsDevice.Clear(captured);
        GraphicsDevice.SetRenderTarget(null);
        var sky = new Vector3(PristonColorSpace.SrgbToLinear(51f / 255f), PristonColorSpace.SrgbToLinear(102f / 255f), PristonColorSpace.SrgbToLinear(153f / 255f));
        _shaders.ApplyFrame(Frame(0f, 0f) with { Fog = fog, FogSky = new PristonFogSky(capture, true, Vector3.Up, Vector3.Zero) });
        material = Material(_white, selfIllum: true);
        Verify(Surface(material, _white, Vector2.Zero, skinned), skinned, Vector3.One + (sky - Vector3.One) * opacity, $"sky capture fog skin={skinned}");
    }

    // The orthographic sun stores projected z; a perspective triangle interpolates z/w linearly in screen space.
    private void VerifyCasters(bool skinned)
    {
        foreach (var alphaTest in new[] { false, true })
        {
            foreach (var scroll in new[] { 0f, 0.5f })
            {
                _shaders.ApplyShadowFrame(Matrix.Identity, 0);
                var caster = Material(_halves, selfIllum: false, alphaTest: alphaTest);
                var effect = Caster(caster, _halves, new Vector2(scroll, 0f), skinned);
                var expected = alphaTest && scroll != 0f ? new Vector3(ClearValue) : new Vector3(0.5f, 0f, 0f);
                Verify(effect, skinned, expected, $"caster skin={skinned} alpha={alphaTest} scroll={scroll}");
            }
        }

        // Odd target dimensions put the sample at NDC (0, 0): screen barycentrics (1/4, 1/4, 1/2).
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, 1, 20);
        var nearClip = Vector4.Transform(new Vector4(0, 0, -2, 1), projection);
        var farClip = Vector4.Transform(new Vector4(0, 0, -10, 1), projection);
        var expectedDepth = 0.5f * (nearClip.Z / nearClip.W + farClip.Z / farClip.W);
        Vector3[] triangle = [new(-2, -2, -2), new(2, -2, -2), new(0, 10, -10)];
        _shaders.ApplyShadowFrame(projection, 0);
        var opaque = Caster(Material(_white, selfIllum: false), _white, Vector2.Zero, skinned);
        Verify(opaque, skinned, new Vector3(expectedDepth, 0f, 0f), $"perspective caster depth skin={skinned}", triangle);
    }

    private void VerifyDebugAlbedo(bool skinned)
    {
        _shaders.ApplyFrame(Frame(0.5f, 0.5f) with { DebugView = PristonDebugView.Albedo });
        var effect = Surface(Material(_grey, selfIllum: false), _grey, Vector2.Zero, skinned);
        Verify(effect, skinned, new Vector3(Grey), $"debug albedo skin={skinned}");
    }

    // A bone translation moves the skinned quad: off the sample, then back with the identity palette.
    private void VerifySkinning()
    {
        _shaders.ApplyFrame(Frame(0f, 0f));
        var moved = new PristonSkinPalette(1);
        moved.Matrices[0] = Matrix.CreateTranslation(3f, 0f, 0f);
        moved.Pack();
        var material = Material(_white, selfIllum: true);
        var effect = _shaders.SelectSkinnedSurface(material, _white, Vector2.Zero, moved, Vector3.Zero, Vector2.Zero);
        Verify(effect, true, new Vector3(ClearValue), "skinning translates the vertex");
        effect = _shaders.SelectSkinnedSurface(material, _white, Vector2.Zero, _palette, Vector3.Zero, Vector2.Zero);
        Verify(effect, true, Vector3.One, "skinning with the identity bone");
    }

    // The gameplay highlight is a rim: zero facing the eye, full at grazing view.
    private void VerifyRim()
    {
        _shaders.ApplyFrame(Frame(0f, 0f));
        var rim = new Vector3(0.2f, 0.1f, 0.05f);
        var material = Material(_grey, selfIllum: true);
        var effect = _shaders.SelectSkinnedSurface(material, _grey, Vector2.Zero, _palette, rim, Vector2.Zero);
        Verify(effect, true, new Vector3(Grey), "rim facing the eye");
        effect = _shaders.SelectSkinnedSurface(material, _grey, Vector2.Zero, _palette, rim, Vector2.Zero);
        Verify(effect, true, new Vector3(Grey) + rim, "rim at grazing view", normal: Vector3.Right);

        // A screen's renderer leaves its selection highlight in its effect; the next screen's library loads the same asset
        // from the same content manager and must not inherit it (character select to field: every actor glowed).
        using var next = new PristonShaderLibrary(Content, ConsoleClientLog.Instance, _white);
        next.ApplyFrame(Frame(0f, 0f));
        effect = next.SelectSkinnedSurface(material, _grey, Vector2.Zero, _palette, Vector3.Zero, Vector2.Zero);
        Verify(effect, true, new Vector3(Grey), "a new library does not inherit the previous highlight", normal: Vector3.Right);
    }

    // Sky dome layers: the sRGB texel times the sRGB tint, display-referred (they are blended in the 8-bit sky target).
    private void VerifySky()
    {
        _shaders.ApplyFrame(Frame(0f, 0f));
        var effect = _shaders.SelectSky(Material(_white, selfIllum: false), _white, Vector2.Zero, 1f);
        Verify(effect, false, new Vector3(128f / 255f), "sky layer tint", vertexColor: new Color(128, 128, 128, 255));
    }

    // Portraits draw display-referred: PBR Neutral, then the sRGB curve.
    private void VerifyDisplayOutput()
    {
        _shaders.ApplyFrame(Frame(0f, 0f) with { DisplayOutput = true });
        var effect = Surface(Material(_grey, selfIllum: true), _grey, Vector2.Zero, skinned: false);
        Verify(effect, false, new Vector3(DisplayEncode(Grey)), "display-referred output");
    }

    private void VerifyUnlit()
    {
        using var unlit = new PristonUnlitEffect(Content) { Texture = _grey, Intensity = 2f, Alpha = 1f };
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(new Vector4(ClearValue)));
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        unlit.Apply();
        var vertices = Quad.Select(static p => new VertexPositionColorTexture(p, Color.White, new Vector2(0.5f))).ToArray();
        GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        Read(new Vector3(2f * Grey / PristonUnlitEffect.SceneExposure), "unlit sRGB decode, intensity and exposure");
    }

    // The scene composition: a flat HDR scene through the post chain (fixed exposure of 1, no bloom, no grade) into an
    // 8-bit target.
    private void VerifyComposition()
    {
        using var output = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize, false, SurfaceFormat.Color, DepthFormat.None);
        using var scene = new PristonSceneTargets(GraphicsDevice, Content, ConsoleClientLog.Instance);
        GraphicsDevice.SetRenderTarget(output);
        scene.Begin(0, false);
        scene.Fill(false, 1f, new Vector3(Grey));
        scene.Compose(UnitPostFrame(), default, null);
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[TargetSize * TargetSize];
        output.GetData(pixels);
        var actual = pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
        var expected = DisplayEncode(Grey) * 255f;
        Assert.True(MathF.Abs(actual.R - expected) <= 1f && actual.R == actual.G && actual.G == actual.B,
            $"composition: expected {expected:F1}, got {actual}");
        _verified++;
    }

    // A post frame whose fixed exposure multiplies the scene by exactly 1: the user compensation cancels the day key.
    private static PristonPostFrame UnitPostFrame(TonemapperMode tonemapper = TonemapperMode.PbrNeutral)
    {
        const float ev = 10f;
        var settings = new PristonPostSettings(AntiAliasingMode.Off, BloomQuality.Off, tonemapper, false, string.Empty, 0f, false);
        var inputs = new PristonExposureInputs(PristonPhotometry.ExposureAt(ev), ev, 0f, -PristonPhotometry.DayKeyStops, true);
        return new PristonPostFrame(settings, inputs, Vector4.Zero, default, 0f, false);
    }

    private Effect Surface(PristonMaterialRuntime material, Texture2D texture, Vector2 scroll, bool skinned) =>
        skinned
            ? _shaders.SelectSkinnedSurface(material, texture, scroll, _palette, Vector3.Zero, Vector2.Zero)
            : _shaders.SelectStaticSurface(material, texture, scroll, Vector4.Zero, Vector2.Zero);

    private Effect Caster(PristonMaterialRuntime material, Texture2D texture, Vector2 scroll, bool skinned) =>
        skinned
            ? _shaders.SelectSkinnedShadow(material, texture, _palette, scroll)
            : _shaders.SelectStaticShadow(material, texture, Vector4.Zero, scroll);

    private void Verify(
        Effect effect,
        bool skinned,
        Vector3 expected,
        string scenario,
        Vector3[]? positions = null,
        Vector3? normal = null,
        Color? vertexColor = null)
    {
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(new Vector4(ClearValue)));
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        positions ??= Quad;
        var surfaceNormal = normal ?? Vector3.Up;
        var color = vertexColor ?? Color.White;
        var uv = new Vector2(0.25f, 0.5f);
        effect.CurrentTechnique.Passes[0].Apply();
        if (skinned)
        {
            var vertices = positions.Select(position => new PristonSkinnedVertex(position, surfaceNormal, color, uv, 0)).ToArray();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
        }
        else
        {
            var vertices = positions.Select(position => new PristonStaticVertex(position, surfaceNormal, color, uv)).ToArray();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
        }

        Read(expected, scenario);
    }

    private void Read(Vector3 expected, string scenario)
    {
        GraphicsDevice.SetRenderTarget(null);
        _target.GetData(_pixels);
        var pixel = _pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
        var actual = new Vector3(pixel.X, pixel.Y, pixel.Z);
        var error = Vector3.Max(Vector3.Max(actual - expected, expected - actual), Vector3.Zero);
        Assert.True(float.IsFinite(actual.X + actual.Y + actual.Z) && MathF.Max(error.X, MathF.Max(error.Y, error.Z)) < Tolerance,
            $"{scenario}: expected {expected}, got {actual}");
        _verified++;
    }

    private PristonFrameContext Frame(float ambient, float sun) =>
        new(Matrix.Identity, Matrix.Identity, Eye, 0,
            new PristonLighting(new Vector3(ambient), new PristonDirectionalLight(Vector3.Down, new Vector3(sun)), []),
            Matrix.Identity, _white, false, 0, PristonDistanceFog.Disabled);

    // Lit reference for n = v = l = up: ambient (sky above) with its specular share, and the sun's diffuse and GGX lobe.
    private static float LitReference(float albedo, float ambient, float sun, float sunVisibility, float roughness)
    {
        const float f0 = 0.04f;
        var a = MathF.Max(roughness * roughness, 0.002f);
        var light = sun * sunVisibility;
        var diffuse = (1f - f0) * albedo * light + ambient * albedo;
        var ggx = 1f / (MathF.PI * a * a);
        var specular = MathF.PI * ggx * 0.25f * f0 * light + ambient * f0;
        return diffuse + specular;
    }

    // Khronos PBR Neutral for a grey value below its compression start, then the sRGB curve.
    private static float DisplayEncode(float grey)
    {
        var offset = grey < 0.08f ? grey - 6.25f * grey * grey : 0.04f;
        return PristonColorSpace.LinearToSrgb(grey - offset);
    }

    private Texture2D Texture(Color color)
    {
        var texture = new Texture2D(GraphicsDevice, 1, 1, false, SurfaceFormat.ColorSRgb);
        texture.SetData(new[] { color });
        return texture;
    }

    private static PristonMaterialRuntime Material(
        Texture2D texture,
        bool selfIllum,
        bool alphaTest = false,
        PristonMaterialLayer? layer = null)
    {
        PristonMaterialLayer[] layers = layer is null ? [] : [layer];
        var description = MaterialAdapterTests.Describe(selfIllum, layer);
        return new PristonMaterialRuntime([texture], twoSided: true, transparent: false, alphaTest: alphaTest,
            blendType: 0, transparency: 0, textureFormState: 0, frameMask: 0, shiftFrameSpeed: 0, animationFrame: 0,
            selfIllum: selfIllum, invisible: false, water: false, blinking: false, windAmplitudeX: 0, windAmplitudeZ: 0,
            layers: layers, description: description);
    }
}
