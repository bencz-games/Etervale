using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats.Gltf;

namespace PristonTale.Engine3D.Rendering.Gltf;

// A glTF material on the GPU: its packed textures (the shared neutral texture where it has none) and the constants of
// Materials/GltfMaterialDecl.hlsli, plus the legacy runtime the render queues read (Queue: a masked material is alpha
// tested, a blended one transparent with an alpha blend, a double-sided one drawn without culling; an unlit one is
// emissive). The textures it uploads are its own and go with Dispose.
internal sealed class GltfMaterialRuntime : IDisposable
{
    private const float DielectricReflectance = 0.04f;

    private readonly List<Texture2D> _owned;

    private GltfMaterialRuntime(GltfMaterial material, Texture2D[] textures, Vector4[] reads, Vector4[] sizes, List<Texture2D> owned)
    {
        Material = material;
        Textures = textures;
        Reads = reads;
        Sizes = sizes;
        _owned = owned;
        var description = new PristonMaterialDescription(
            material.Unlit ? PristonShadingModel.Emissive : PristonShadingModel.Lit,
            material.BaseColor,
            material.Roughness,
            material.Metallic,
            DielectricReflectance,
            material.Unlit ? 1f : material.EmissiveStrength,
            PristonLayerMode.None,
            1f,
            false);
        Queue = new PristonMaterialRuntime(
            [textures[(int)GltfPackedTexture.BaseColor]],
            twoSided: material.DoubleSided,
            transparent: material.Alpha == GltfAlphaMode.Blend,
            alphaTest: material.Alpha == GltfAlphaMode.Mask,
            blendType: material.Alpha == GltfAlphaMode.Blend ? PristonBlendType.Alpha : 0u,
            transparency: 0f,
            textureFormState: 0,
            frameMask: 0,
            shiftFrameSpeed: 0,
            animationFrame: 0,
            selfIllum: material.Unlit,
            invisible: false,
            water: false,
            blinking: false,
            windAmplitudeX: 0f,
            windAmplitudeZ: 0f,
            description: description);
    }

    public GltfMaterial Material { get; }
    public PristonMaterialRuntime Queue { get; }
    // In GltfPackedTexture order.
    public Texture2D[] Textures { get; }
    // GltfTextureReads: per texture the UV transform's 2x2 part, then offset, UV set (-1 absent) and wraps (S + 3 T).
    public Vector4[] Reads { get; }
    // GltfTextureSizes: per texture its width and height in texels.
    public Vector4[] Sizes { get; }
    public Vector4 Emission => new(Material.Emissive, Material.OcclusionStrength);
    public Vector4 NormalScales => new(Material.NormalScale, Material.ClearcoatNormalScale, Material.Clearcoat, Material.ClearcoatRoughness);
    public Vector4 Sheen => new(Material.SheenColor, Material.SheenRoughness);
    public Vector4 Transmission => new(Material.DiffuseTransmissionColor, Material.DiffuseTransmission);
    public float AlphaCutoff => Material.Alpha == GltfAlphaMode.Mask ? Material.AlphaCutoff : -1f;

    public static GltfMaterialRuntime Create(GraphicsDevice device, GltfMaterialPack pack, Texture2D neutral)
    {
        var textures = new Texture2D[GltfMaterialPacking.TextureCount];
        var reads = new Vector4[GltfMaterialPacking.TextureCount * 2];
        var sizes = new Vector4[GltfMaterialPacking.TextureCount];
        var owned = new List<Texture2D>();
        try
        {
            for (var index = 0; index < GltfMaterialPacking.TextureCount; index++)
            {
                var image = pack.Images[index];
                if (image is null)
                {
                    textures[index] = neutral;
                    reads[index * 2] = new Vector4(1f, 0f, 0f, 1f);
                    reads[index * 2 + 1] = new Vector4(0f, 0f, -1f, 0f);
                    continue;
                }

                textures[index] = Upload(device, image, $"{pack.Material.Name}:{(GltfPackedTexture)index}");
                owned.Add(textures[index]);
                reads[index * 2] = Transform(image.Read);
                reads[index * 2 + 1] = new Vector4(image.Read.Offset, image.Read.TexCoord, (int)image.Read.WrapS + 3 * (int)image.Read.WrapT);
                sizes[index] = new Vector4(image.Width, image.Height, 0f, 0f);
            }
        }
        catch
        {
            foreach (var texture in owned)
            {
                texture.Dispose();
            }

            throw;
        }

        return new GltfMaterialRuntime(pack.Material, textures, reads, sizes, owned);
    }

    // KHR_texture_transform's scale then rotation as rows of uv' = (dot(uv, xy), dot(uv, zw)); its rotation turns
    // (1, 0) towards (0, -1).
    internal static Vector4 Transform(GltfTextureRef read)
    {
        var (sin, cos) = MathF.SinCos(read.Rotation);
        return new Vector4(cos * read.Scale.X, sin * read.Scale.Y, -sin * read.Scale.X, cos * read.Scale.Y);
    }

    private static Texture2D Upload(GraphicsDevice device, GltfPackedImage image, string name)
    {
        var texture = new Texture2D(device, image.Width, image.Height, image.Levels.Length > 1, image.Srgb ? SurfaceFormat.ColorSRgb : SurfaceFormat.Color)
        {
            Name = name
        };
        try
        {
            for (var level = 0; level < image.Levels.Length; level++)
            {
                texture.SetData(level, null, image.Levels[level], 0, image.Levels[level].Length);
            }
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _owned)
        {
            texture.Dispose();
        }

        _owned.Clear();
    }
}
