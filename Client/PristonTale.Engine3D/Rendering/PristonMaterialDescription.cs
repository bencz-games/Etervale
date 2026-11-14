using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// How a surface turns light into radiance (Shaders/Materials/MaterialDecl.hlsli ShadingModel).
internal enum PristonShadingModel
{
    // Metallic-roughness lighting by the sun, the local lights and the ambient.
    Lit = 0,
    // The albedo is emitted radiance: self-illuminated surfaces and additive blends.
    Emissive = 1,
    // Multiplicative blends: the albedo is written as the blend factor.
    ColorFactor = 2
}

// What the second texture layer contributes (Shaders/Materials/MaterialDecl.hlsli LayerMode).
internal enum PristonLayerMode
{
    None = 0,
    // Detail or light map multiplied into the albedo.
    Multiply = 1,
    // Added to the albedo (a lit glow layer, often scrolling).
    Add = 2,
    // Sphere-map reflection added to the specular radiance (the original shine of armour and weapons).
    Reflection = 3
}

// The material model's inputs for one original material, decided once at load time by PristonMaterialAdapter. Translucency
// is the share of the sun that passes through a thin surface lit from behind (leaves, cloth cut-outs).
internal sealed record PristonMaterialDescription(
    PristonShadingModel ShadingModel,
    Vector4 BaseColorFactor,
    float Roughness,
    float Metallic,
    float Reflectance,
    float EmissiveStrength,
    PristonLayerMode LayerMode,
    float LayerStrength,
    bool Blink,
    float Translucency = 0f)
{
    public Texture2D PbrNormal { get; init; }
    public Texture2D PbrOrm { get; init; }

    public static PristonMaterialDescription Default { get; } = new(
        PristonShadingModel.Lit,
        Vector4.One,
        PristonMaterialAdapter.DefaultRoughness,
        0f,
        PristonMaterialAdapter.DielectricReflectance,
        0f,
        PristonLayerMode.None,
        1f,
        false);
}
