using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonMaterialRuntimeFactory
{
    private readonly PristonTextureLoader _textureLoader;

    public PristonMaterialRuntimeFactory(PristonTextureLoader textureLoader)
    {
        _textureLoader = textureLoader;
    }

    public PristonMaterialRuntime Create(string assetDirectory, PristonMaterial material, bool softAlpha = false)
    {
        var (alphaTest, transparent) = Classify(material, softAlpha);
        var (windX, windZ) = WindAmplitudes(material.SurfaceFlags);
        // Multi-texture slots carry independent form states; a single-slot material takes the first authored one.
        var formState = material.RenderedStageCount > 1 ? material.StageFormState(0) : FirstTextureFormState(material);
        var layers = LoadLayers(assetDirectory, material);
        return new PristonMaterialRuntime(
            LoadMaterialTextures(assetDirectory, material),
            material.TwoSided,
            transparent,
            alphaTest,
            material.BlendType,
            material.Transparency,
            formState,
            material.FrameMask,
            material.ShiftFrameSpeed,
            material.AnimationFrame,
            material.IsSelfIllum,
            material.IsInvisible,
            material.IsWater,
            material.IsBlinking,
            windX,
            windZ,
            PristonMaterialLayer.Decode(material.StageState(0)),
            layers,
            PristonMaterialAdapter.WithShape(
                PristonMaterialAdapter.Describe(material, layers.Length > 0 ? layers[0] : null),
                alphaTest,
                material.TwoSided));
    }

    private PristonMaterialLayer[] LoadLayers(string assetDirectory, PristonMaterial material)
    {
        // The material model uses one layer above the base (PristonMaterialAdapter); further stages are not loaded.
        var count = Math.Min(material.CombinedLayerCount, 1);
        if (count == 0)
        {
            return [];
        }

        var layers = new PristonMaterialLayer[count];
        for (var index = 0; index < count; index++)
        {
            var stage = index + 1;
            var texture = _textureLoader.LoadTexture(assetDirectory, material.StageTextureName(stage), string.Empty);
            layers[index] = new PristonMaterialLayer(
                texture,
                PristonMaterialLayer.Decode(material.StageState(stage)),
                material.StageFormState(stage));
        }

        return layers;
    }

    // A bare Move flag is the smaller Z-axis wind sway, not a UV scroll.
    private const float WindSmall = 8f;
    private const float WindLarge = 28f;

    private static (float x, float z) WindAmplitudes(PristonMaterialSurfaceFlags sf)
    {
        var x = (sf & PristonMaterialSurfaceFlags.MoveX2) != 0 ? WindLarge
              : (sf & PristonMaterialSurfaceFlags.MoveX1) != 0 ? WindSmall : 0f;
        var z = (sf & PristonMaterialSurfaceFlags.MoveZ2) != 0 ? WindLarge
              : (sf & (PristonMaterialSurfaceFlags.MoveZ1 | PristonMaterialSurfaceFlags.Move)) != 0 ? WindSmall : 0f;
        return (x, z);
    }

    private Texture2D[] LoadMaterialTextures(string assetDirectory, PristonMaterial material)
    {
        if (material.HasAnimatedTexture)
        {
            var textures = new Texture2D[material.AnimationTextureNames.Count];
            for (var i = 0; i < textures.Length; i++)
                textures[i] = _textureLoader.LoadTexture(assetDirectory, material.AnimationTextureNames[i], string.Empty);
            return textures;
        }

        return [_textureLoader.LoadTexture(assetDirectory, material.TextureName, material.OpacityName)];
    }

    // Whether the material is cut out by its texture alpha, and whether it is drawn in the late, blended queue.
    internal static (bool AlphaTest, bool Transparent) Classify(PristonMaterial material, bool softAlpha = false)
    {
        var alphaTest = !softAlpha && UsesAlphaTest(material);

        // The alpha blend (1) with zero transparency is opaque in the original game and remains mergeable.
        var lateBlend = PristonBlendType.IsLate(material.BlendType);
        // Most late-flagged materials are foliage cut-outs drawn opaque with their depth: they stay in the alpha-tested
        // queue, so the depth pre-pass gives them depth, motion and occlusion like any other leaf. Drawn late, their
        // leaves would take the depth, motion and fog of the surface behind them (a ghosted, see-through canopy).
        var opaqueCutout = alphaTest && !lateBlend && material.Transparency <= PristonMaterialRuntime.SolidTransparencyThreshold;
        var transparent = (material.RenderLast && !opaqueCutout)
            || (!(alphaTest && !lateBlend) && (lateBlend || material.Transparency > 0.001f));
        return (alphaTest, transparent);
    }

    // A texture with an opacity map, or a TGA (which carries its own alpha), is cut out by that alpha.
    internal static bool IsCutOut(string textureName, string opacityName) =>
        !string.IsNullOrWhiteSpace(opacityName) || textureName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);

    // A material whose texture, or any frame of its animated texture, is cut out.
    internal static bool UsesAlphaTest(PristonMaterial material)
    {
        if (IsCutOut(material.TextureName, material.OpacityName))
            return true;

        for (var i = 0; i < material.AnimationTextureNames.Count; i++)
            if (IsCutOut(material.AnimationTextureNames[i], ""))
                return true;

        return false;
    }

    private static uint FirstTextureFormState(PristonMaterial material)
    {
        for (var i = 0; i < material.TextureFormStates.Count; i++)
            if (material.TextureFormStates[i] != 0)
                return material.TextureFormStates[i];

        return 0;
    }
}
