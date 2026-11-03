using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Tests;

// The original material adapter: what each kind of original material becomes in the material model.
internal static class MaterialAdapterTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("material adapter keeps plain stage materials lit and rough", PlainAsync);
        yield return ("material adapter turns self-illumination and additive blends into emission", EmissiveAsync);
        yield return ("material adapter makes darkening blends colour factors", ColorFactorAsync);
        yield return ("material adapter maps the second texture layer by operation and form state", LayerAsync);
        yield return ("material adapter gives water and shine their own roughness, slippery floor the ground's", SurfaceKindsAsync);
        yield return ("two-sided alpha-tested lit materials transmit light as foliage", FoliageAsync);
        yield return ("late-flagged opaque cut-outs stay in the alpha-tested queue", LateCutoutAsync);
        yield return ("scenery models take the stage's authored light for their texture", StageBakedLightAsync);
    }

    private static Task FoliageAsync()
    {
        var plain = PristonMaterialAdapter.Describe(StageMaterial(), null);
        Assert.True(PristonMaterialAdapter.WithShape(plain, alphaTest: true, twoSided: true).Translucency > 0f, "foliage is not translucent");
        Assert.True(PristonMaterialAdapter.WithShape(plain, alphaTest: true, twoSided: false).Translucency == 0f, "one-sided cut-outs transmit");
        var emissive = PristonMaterialAdapter.Describe(StageMaterial(selfIllum: 1f), null);
        Assert.True(PristonMaterialAdapter.WithShape(emissive, alphaTest: true, twoSided: true).Translucency == 0f, "emission transmits");
        return Task.CompletedTask;
    }

    private static Task LateCutoutAsync()
    {
        var leaf = StageMaterial(flags: PristonMaterialSurfaceFlags.RenderLast) with { TextureName = "leaf.tga", TwoSided = true };
        var (alphaTest, transparent) = PristonMaterialRuntimeFactory.Classify(leaf);
        Assert.True(alphaTest && !transparent, "a late-flagged foliage cut-out is drawn in the blended queue");
        Assert.True(PristonMaterialRuntimeFactory.Classify(leaf with { Transparency = 0.4f }).Transparent, "a see-through cut-out left the blended queue");
        Assert.True(PristonMaterialRuntimeFactory.Classify(leaf with { BlendType = PristonBlendType.Lamp }).Transparent, "an additive cut-out left the blended queue");
        Assert.True(PristonMaterialRuntimeFactory.Classify(leaf with { TextureName = "glass.bmp" }).Transparent, "a late-flagged blend left the blended queue");
        return Task.CompletedTask;
    }

    private static Task StageBakedLightAsync()
    {
        var metal = StageMaterial() with { TextureName = "metal.bmp" };
        var wood = StageMaterial() with { TextureName = "wood.bmp" };
        var dark = new Color(100, 100, 100);
        var vertices = new[]
        {
            new PristonStageVertex(Vector3.Zero, dark), new PristonStageVertex(Vector3.UnitX, dark), new PristonStageVertex(Vector3.UnitZ, dark),
            new PristonStageVertex(Vector3.One, Color.White), new PristonStageVertex(Vector3.UnitY, Color.White),
            new PristonStageVertex(-Vector3.UnitX, Color.White)
        };
        var stage = new PristonStage("fixture.smd", [metal, wood], vertices, [new(0, 1, 2, 0, -1), new(3, 4, 5, 1, -1)], [], [], default);
        var light = PristonStageBakedLight.For(stage);
        Assert.True(light.For(metal) == dark && light.For(wood) == Color.White, $"per-texture light {light.For(metal)} / {light.For(wood)}");
        var unknown = light.For(StageMaterial() with { TextureName = "cloth.bmp" });
        // The mean of 100/255 and white in linear light, encoded again: about 198.
        Assert.True(unknown.R is >= 196 and <= 200 && unknown.R == unknown.G && unknown.G == unknown.B, $"stage mean light {unknown}");
        Assert.True(PristonStageBakedLight.None.For(metal) == Color.White, "without a stage the scenery is not lit at full white");
        return Task.CompletedTask;
    }

    // A original material with one texture; the parameters the adapter reads are set by name.
    public static PristonMaterial StageMaterial(
        uint blendType = PristonBlendType.Alpha,
        float selfIllum = 0f,
        PristonMaterialSurfaceFlags flags = PristonMaterialSurfaceFlags.None) =>
        new(true, 1, 0, blendType, false, Vector3.One, 0f, 0, flags, "base.bmp", string.Empty, [], [], 0, 0, 0, selfIllum);

    // The description the GPU probe draws with: a plain or self-illuminated material, with an optional second layer.
    public static PristonMaterialDescription Describe(bool selfIllum, PristonMaterialLayer? layer) =>
        PristonMaterialAdapter.Describe(StageMaterial(selfIllum: selfIllum ? 1f : 0f), layer!);

    private static Task PlainAsync()
    {
        var description = PristonMaterialAdapter.Describe(StageMaterial(), null);
        Assert.True(description.ShadingModel == PristonShadingModel.Lit && description.LayerMode == PristonLayerMode.None,
            $"plain material became {description.ShadingModel}/{description.LayerMode}");
        Assert.True(description.Metallic == 0f && description.Roughness == PristonMaterialAdapter.DefaultRoughness
            && description.Reflectance == PristonMaterialAdapter.DielectricReflectance && description.EmissiveStrength == 0f,
            "plain material is not a rough dielectric without emission");
        return Task.CompletedTask;
    }

    private static Task EmissiveAsync()
    {
        foreach (var material in new[]
                 {
                     StageMaterial(selfIllum: 1f),
                     StageMaterial(PristonBlendType.Lamp),
                     StageMaterial(PristonBlendType.AddColor),
                     StageMaterial(PristonBlendType.Color)
                 })
        {
            var description = PristonMaterialAdapter.Describe(material, null);
            Assert.True(description.ShadingModel == PristonShadingModel.Emissive && description.EmissiveStrength == 1f,
                $"blend {material.BlendType} self-illum {material.SelfIllum} became {description.ShadingModel} x{description.EmissiveStrength}");
        }

        var blinking = PristonMaterialAdapter.Describe(StageMaterial(selfIllum: 1f, flags: PristonMaterialSurfaceFlags.Blinking), null);
        Assert.True(blinking.Blink, "blinking emission lost its pulse");
        return Task.CompletedTask;
    }

    private static Task ColorFactorAsync()
    {
        foreach (var blend in new[] { PristonBlendType.Shadow, PristonBlendType.InverseShadow })
        {
            var description = PristonMaterialAdapter.Describe(StageMaterial(blend), null);
            Assert.True(description.ShadingModel == PristonShadingModel.ColorFactor, $"blend {blend} became {description.ShadingModel}");
        }

        return Task.CompletedTask;
    }

    private static Task LayerAsync()
    {
        var texture = (Texture2D)null!;
        var cases = new (PristonStageOperation Operation, uint FormState, PristonLayerMode Mode, float Strength)[]
        {
            (PristonStageOperation.Modulate, 0, PristonLayerMode.Multiply, 1f),
            (PristonStageOperation.Modulate2X, 6, PristonLayerMode.Multiply, 2f),
            (PristonStageOperation.Add, 6, PristonLayerMode.Add, 1f),
            (PristonStageOperation.Add, PristonMaterialRuntime.ReflexFormState, PristonLayerMode.Reflection, 1f),
            (PristonStageOperation.SelectTexture, 0, PristonLayerMode.None, 1f)
        };
        foreach (var (operation, formState, mode, strength) in cases)
        {
            // A layer without a texture adds nothing; the operation decides only for a loaded layer.
            var missing = PristonMaterialAdapter.Describe(StageMaterial(), new PristonMaterialLayer(texture, operation, formState));
            Assert.True(missing.LayerMode == PristonLayerMode.None, $"{operation} without a texture still uses the layer");
        }

        foreach (var (operation, formState, mode, strength) in cases)
        {
            var description = PristonMaterialAdapter.DescribeLayer(operation, formState);
            Assert.True(description.Mode == mode && description.Strength == strength,
                $"{operation} form {formState} became {description.Mode} x{description.Strength}, expected {mode} x{strength}");
        }

        return Task.CompletedTask;
    }

    private static Task SurfaceKindsAsync()
    {
        var water = PristonMaterialAdapter.Describe(StageMaterial(flags: PristonMaterialSurfaceFlags.Water), null);
        Assert.True(water.Roughness == PristonMaterialAdapter.WaterRoughness && water.Reflectance == PristonMaterialAdapter.WaterReflectance,
            "water is not smooth with the reflectance of water");
        // The ice flag marks slippery floor for gameplay: the ice fields set it on snow, footprints and the grass-to-snow
        // tiles but not on the grass and soil beside them, so a smoother look shows those tiles as squares.
        var ice = PristonMaterialAdapter.Describe(StageMaterial(flags: PristonMaterialSurfaceFlags.Ice), null);
        Assert.True(ice.Roughness == PristonMaterialAdapter.DefaultRoughness && ice.Reflectance == PristonMaterialAdapter.DielectricReflectance,
            $"slippery floor looks different from the ground beside it (roughness {ice.Roughness})");
        var shine = PristonMaterialAdapter.DescribeLayer(PristonStageOperation.Add, PristonMaterialRuntime.ReflexFormState);
        Assert.True(shine.Mode == PristonLayerMode.Reflection, "the sphere-mapped shine is not a reflection");
        return Task.CompletedTask;
    }
}
