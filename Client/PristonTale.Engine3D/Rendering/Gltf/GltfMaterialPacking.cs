using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats.Gltf;

namespace PristonTale.Engine3D.Rendering.Gltf;

// The textures a glTF material is drawn with. The renderer has six texture slots for them, so the maps are packed at
// load time into fixed channels; a texture none of whose maps the material has is absent and the shader takes the
// factor alone.
public enum GltfPackedTexture
{
    // sRGB colour, alpha the coverage.
    BaseColor,
    // RG the surface normal's XY, BA the clearcoat normal's XY (the shader rebuilds Z); flat is 0.5.
    Normals,
    // R occlusion, G roughness, B metallic, A clearcoat.
    Surface,
    // sRGB emission, A clearcoat roughness.
    Emissive,
    // sRGB sheen colour, A sheen roughness.
    Sheen,
    // sRGB diffuse transmission colour, A diffuse transmission.
    Transmission
}

// One packed texture: its mip chain and how it is read (the UV set, wraps and transform of the first of its maps the
// material has; a later map read otherwise is left out, the factor alone standing for it).
public sealed record GltfPackedImage(int Width, int Height, Color[][] Levels, bool Srgb, GltfTextureRef Read);

public sealed record GltfMaterialPack(GltfMaterial Material, GltfPackedImage[] Images)
{
    public GltfPackedImage this[GltfPackedTexture texture] => Images[(int)texture];
}

// Packs a material's maps (glTF channel layouts: metallic-roughness G and B, occlusion R, clearcoat R, clearcoat
// roughness G, sheen roughness and diffuse transmission A). The maps of one packed texture are resampled to the largest
// of them; a channel whose map is missing holds the neutral value (1, or a flat normal). Colour builds its mips in
// linear light (the base colour's alpha keeps a masked material's coverage; another texture's alpha is data, averaged
// as stored, as are the data textures).
public static class GltfMaterialPacking
{
    public const int TextureCount = (int)GltfPackedTexture.Transmission + 1;
    private const byte Flat = 128;

    private enum Kind
    {
        Data,
        NormalPairs,
        ColourAndData
    }

    public static GltfMaterialPack Pack(GltfMaterial material, Func<int, DecodedPristonImage> image)
    {
        var images = new GltfPackedImage[TextureCount];
        images[(int)GltfPackedTexture.BaseColor] = BaseColour(material, image);
        images[(int)GltfPackedTexture.Normals] = Packed(image, Kind.NormalPairs,
            (material.NormalTexture, static (texel, into) => into with { R = texel.R, G = texel.G }),
            (material.ClearcoatNormalTexture, static (texel, into) => into with { B = texel.R, A = texel.G }));
        images[(int)GltfPackedTexture.Surface] = Packed(image, Kind.Data,
            (material.MetallicRoughnessTexture, static (texel, into) => into with { G = texel.G, B = texel.B }),
            (material.OcclusionTexture, static (texel, into) => into with { R = texel.R }),
            (material.ClearcoatTexture, static (texel, into) => into with { A = texel.R }));
        images[(int)GltfPackedTexture.Emissive] = Packed(image, Kind.ColourAndData,
            (material.EmissiveTexture, static (texel, into) => into with { R = texel.R, G = texel.G, B = texel.B }),
            (material.ClearcoatRoughnessTexture, static (texel, into) => into with { A = texel.G }));
        images[(int)GltfPackedTexture.Sheen] = Packed(image, Kind.ColourAndData,
            (material.SheenColorTexture, static (texel, into) => into with { R = texel.R, G = texel.G, B = texel.B }),
            (material.SheenRoughnessTexture, static (texel, into) => into with { A = texel.A }));
        images[(int)GltfPackedTexture.Transmission] = Packed(image, Kind.ColourAndData,
            (material.DiffuseTransmissionColorTexture, static (texel, into) => into with { R = texel.R, G = texel.G, B = texel.B }),
            (material.DiffuseTransmissionTexture, static (texel, into) => into with { A = texel.A }));
        return new GltfMaterialPack(material, images);
    }

    // An opaque material's alpha means nothing, so it must not weight the colour of its mips.
    private static GltfPackedImage BaseColour(GltfMaterial material, Func<int, DecodedPristonImage> image)
    {
        var read = material.BaseColorTexture;
        if (!read.IsSet || image(read.Image) is not { IsValid: true } decoded)
        {
            return null;
        }

        var cutoff = material.Alpha == GltfAlphaMode.Mask ? (byte)Math.Clamp(material.AlphaCutoff * 255f + 0.5f, 1f, 255f) : (byte)0;
        Color[] pixels = material.Alpha == GltfAlphaMode.Opaque ? [.. decoded.Pixels.Select(static pixel => pixel with { A = 255 })] : decoded.Pixels;
        return new GltfPackedImage(decoded.Width, decoded.Height, PristonMipChain.Build(pixels, decoded.Width, decoded.Height, cutoff), Srgb: true,
            read);
    }

    private static GltfPackedImage Packed(Func<int, DecodedPristonImage> image, Kind kind,
        params (GltfTextureRef Read, Func<Color, Color, Color> Into)[] maps)
    {
        var present = new List<(DecodedPristonImage Image, GltfTextureRef Read, Func<Color, Color, Color> Into)>();
        foreach (var (read, into) in maps)
        {
            if (read.IsSet && (present.Count == 0 || SameRead(present[0].Read, read)) && image(read.Image) is { IsValid: true } decoded)
            {
                present.Add((decoded, read, into));
            }
        }

        if (present.Count == 0)
        {
            return null;
        }

        var width = present.Max(static map => map.Image.Width);
        var height = present.Max(static map => map.Image.Height);
        var pixels = new Color[width * height];
        Array.Fill(pixels, kind == Kind.NormalPairs ? new Color(Flat, Flat, Flat, Flat) : new Color(255, 255, 255, 255));
        foreach (var (decoded, _, into) in present)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    pixels[index] = into(Sample(decoded, (x + 0.5f) / width, (y + 0.5f) / height), pixels[index]);
                }
            }
        }

        var levels = kind switch
        {
            Kind.NormalPairs => PristonMipChain.BuildNormalPairs(pixels, width, height),
            Kind.ColourAndData => PristonMipChain.BuildColourAndData(pixels, width, height),
            _ => PristonMipChain.BuildData(pixels, width, height)
        };
        return new GltfPackedImage(width, height, levels, Srgb: kind == Kind.ColourAndData, present[0].Read);
    }

    private static bool SameRead(GltfTextureRef first, GltfTextureRef other) => first with { Image = 0 } == other with { Image = 0 };

    // Bilinear, at texel centres, clamped to the edge.
    private static Color Sample(DecodedPristonImage image, float u, float v)
    {
        if (image.Width == 1 && image.Height == 1)
        {
            return image.Pixels[0];
        }

        var x = Math.Clamp(u * image.Width - 0.5f, 0f, image.Width - 1f);
        var y = Math.Clamp(v * image.Height - 0.5f, 0f, image.Height - 1f);
        var (x0, y0) = ((int)x, (int)y);
        var (x1, y1) = (Math.Min(x0 + 1, image.Width - 1), Math.Min(y0 + 1, image.Height - 1));
        var (fx, fy) = (x - x0, y - y0);
        var top = Vector4.Lerp(image.Pixels[y0 * image.Width + x0].ToVector4(), image.Pixels[y0 * image.Width + x1].ToVector4(), fx);
        var bottom = Vector4.Lerp(image.Pixels[y1 * image.Width + x0].ToVector4(), image.Pixels[y1 * image.Width + x1].ToVector4(), fx);
        return new Color(Vector4.Lerp(top, bottom, fy));
    }
}
