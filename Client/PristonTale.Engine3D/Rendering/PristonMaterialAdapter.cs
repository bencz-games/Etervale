using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// Converts a original material into the inputs of the material model (PristonMaterialDescription). The original format
// describes fixed-function texture stages and blend modes; the assets use (measured over every .smd/.smb, weighted by
// faces) a modulating base stage everywhere, at most one further stage in practice (a multiplied detail or light map on
// stage geometry, an additive sphere-mapped shine on armour and weapons, an additive scrolling glow on a few stage
// surfaces) and a third multiplied stage on nine materials only, which is not carried over. The original assets carry no
// roughness or metalness, so each kind of surface gets a fixed, plausible value.
internal static class PristonMaterialAdapter
{
    // Skin, cloth, hair, leather, wood, stone and snow: rough dielectrics. Only water and the authored shine are smoother;
    // nothing original is metallic (the assets carry no metalness). The ice surface flag is slippery floor for gameplay,
    // set on snow and grass-to-snow tiles alike, so it does not change the look.
    public const float DefaultRoughness = 0.85f;
    public const float DielectricReflectance = 0.04f;
    public const float WaterRoughness = 0.12f;
    public const float WaterReflectance = 0.02f;
    public const float ShineRoughness = 0.4f;
    public const float EmissiveStrength = 1f;
    // Thin, two-sided cut-outs (foliage) let this share of the sun through when lit from behind.
    public const float FoliageTranslucency = 0.45f;

    // Lit, alpha-tested, two-sided materials are leaves and other thin cut-outs.
    public static PristonMaterialDescription WithShape(PristonMaterialDescription description, bool alphaTest, bool twoSided) =>
        alphaTest && twoSided && description.ShadingModel == PristonShadingModel.Lit
            ? description with { Translucency = FoliageTranslucency }
            : description;

    public static PristonMaterialDescription Describe(PristonMaterial material, PristonMaterialLayer layer)
    {
        var (layerMode, layerStrength) = DescribeLayer(layer);
        var shadingModel = ShadingModelOf(material);
        var roughness = DefaultRoughness;
        var reflectance = DielectricReflectance;
        if (material.IsWater)
        {
            roughness = WaterRoughness;
            reflectance = WaterReflectance;
        }
        else if (layerMode == PristonLayerMode.Reflection)
        {
            roughness = ShineRoughness;
        }

        return new PristonMaterialDescription(
            shadingModel,
            Vector4.One,
            roughness,
            0f,
            reflectance,
            shadingModel == PristonShadingModel.Emissive ? EmissiveStrength : 0f,
            layerMode,
            layerStrength,
            material.IsBlinking);
    }

    // Self-illuminated surfaces show their texture at full brightness whatever the light; additive and colour-keyed
    // additive blends add their texture as light. The darkening blends multiply what is behind them by their colour.
    private static PristonShadingModel ShadingModelOf(PristonMaterial material)
    {
        if (material.IsSelfIllum)
        {
            return PristonShadingModel.Emissive;
        }

        return material.BlendType switch
        {
            PristonBlendType.Color or PristonBlendType.Lamp or PristonBlendType.AddColor => PristonShadingModel.Emissive,
            PristonBlendType.Shadow or PristonBlendType.InverseShadow => PristonShadingModel.ColorFactor,
            _ => PristonShadingModel.Lit
        };
    }

    private static (PristonLayerMode Mode, float Strength) DescribeLayer(PristonMaterialLayer layer) =>
        layer?.Texture is null ? (PristonLayerMode.None, 1f) : DescribeLayer(layer.Operation, layer.FormState);

    // What a loaded second layer contributes, by its original stage operation and texture form state.
    internal static (PristonLayerMode Mode, float Strength) DescribeLayer(PristonStageOperation operation, uint formState) =>
        operation switch
        {
            PristonStageOperation.Modulate => (PristonLayerMode.Multiply, 1f),
            PristonStageOperation.Modulate2X => (PristonLayerMode.Multiply, 2f),
            PristonStageOperation.Modulate4X => (PristonLayerMode.Multiply, 4f),
            PristonStageOperation.Add or PristonStageOperation.AddSigned or PristonStageOperation.AddSigned2X =>
                formState == PristonMaterialRuntime.ReflexFormState ? (PristonLayerMode.Reflection, 1f) : (PristonLayerMode.Add, 1f),
            _ => (PristonLayerMode.None, 1f)
        };
}

// Original blend modes of a material (BlendType), by their effect on the colour behind the surface.
internal static class PristonBlendType
{
    public const uint Alpha = 1;
    // Source colour weighted by itself over the inverse: a bright, screen-like blend.
    public const uint Color = 2;
    // Zero, source colour: darkens what is behind.
    public const uint Shadow = 3;
    // Source alpha, one: additive.
    public const uint Lamp = 4;
    // Source colour, one: additive.
    public const uint AddColor = 5;
    // Zero, inverse source colour.
    public const uint InverseShadow = 6;

    // A colour-weighted, darkening or additive mode (everything but plain alpha): drawn with the blended surfaces, never
    // as a cut-out.
    public static bool IsLate(uint blendType) => blendType is >= Color and <= InverseShadow;

    // No blend or plain alpha: the surface covers what is behind it by its own alpha.
    public static bool Covers(uint blendType) => blendType <= Alpha;
}
