using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using SharpGLTF.Materials;

namespace PristonTale.GltfExport;

internal sealed class MaterialFactory
{
    internal const float RuntimeAlphaCutoff = 60f / 255f;

    private readonly record struct MaterialKey(
        string Texture,
        string Opacity,
        bool TwoSided,
        uint BlendType,
        float Transparency,
        bool RenderLast,
        bool SelfIllum,
        AlphaMode AlphaMode);

    private readonly record struct ImageKey(
        string Texture,
        string Opacity,
        BlendAlpha BlendAlpha);

    // glTF only offers alpha blending, so the original colour blend modes are approximated
    // by deriving coverage from the texture colour when the texture carries no alpha channel.
    private enum BlendAlpha
    {
        Opaque,
        Luminance,
        Shadow,
        InverseShadow
    }

    private readonly string _assetDirectory;
    private readonly string _pngDirectory;
    private readonly bool _forceDoubleSided;
    private readonly bool _unlit;
    private readonly Dictionary<MaterialKey, MaterialBuilder> _materials = [];
    private readonly Dictionary<ImageKey, string> _pngPaths = [];
    private int _missingTextures;
    private int _generatedPngs;

    public MaterialFactory(string assetDirectory, string pngDirectory, bool forceDoubleSided = false, bool unlit = false)
    {
        _assetDirectory = assetDirectory;
        _pngDirectory = pngDirectory;
        _forceDoubleSided = forceDoubleSided;
        _unlit = unlit;
    }

    public int MissingTextures => _missingTextures;
    public int GeneratedPngs => _generatedPngs;

    public MaterialBuilder? Create(PristonMaterial material, int index)
    {
        if (material.IsInvisible || material.HasNoRenderableTexture)
        {
            return null;
        }

        var source = SelectBaseTexture(material);
        // The engine's queue decision, so a late-flagged foliage cut-out exports as MASK, drawn opaque as the game does.
        var (alphaTest, transparent) = PristonMaterialRuntimeFactory.Classify(material);
        var alphaMode = AlphaMode.OPAQUE;
        if (transparent)
        {
            alphaMode = AlphaMode.BLEND;
        }
        else if (alphaTest)
        {
            alphaMode = AlphaMode.MASK;
        }

        var key = new MaterialKey(
            Normalize(source.TextureName),
            Normalize(source.OpacityName),
            material.TwoSided,
            material.BlendType,
            material.Transparency,
            material.RenderLast,
            material.IsSelfIllum,
            alphaMode);
        if (_materials.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var pngPath = BakePng(source, index, BlendAlphaOf(material, alphaMode));
        var builder = new MaterialBuilder($"mat{index}_{Path.GetFileNameWithoutExtension(source.TextureName)}")
            .WithDoubleSide(_forceDoubleSided || material.TwoSided);
        if (_unlit)
        {
            builder.WithUnlitShader();
        }
        else
        {
            builder.WithMetallicRoughnessShader().WithMetallicRoughness(0f, 1f);
        }

        builder.WithChannelImage(KnownChannel.BaseColor, pngPath);
        ApplySampler(builder, KnownChannel.BaseColor);
        if (!_unlit && material.IsSelfIllum)
        {
            builder.WithChannelImage(KnownChannel.Emissive, pngPath);
            builder.WithEmissive(System.Numerics.Vector3.One);
            ApplySampler(builder, KnownChannel.Emissive);
        }

        if (alphaMode == AlphaMode.MASK)
        {
            builder.WithAlpha(AlphaMode.MASK, RuntimeAlphaCutoff);
        }
        else if (alphaMode == AlphaMode.BLEND)
        {
            builder.WithAlpha(AlphaMode.BLEND);
        }

        _materials.Add(key, builder);
        return builder;
    }

    private static void ApplySampler(MaterialBuilder builder, KnownChannel channel)
    {
        builder.GetChannel(channel)?.Texture?.WithSampler(
            SharpGLTF.Schema2.TextureWrapMode.REPEAT,
            SharpGLTF.Schema2.TextureWrapMode.REPEAT,
            SharpGLTF.Schema2.TextureMipMapFilter.LINEAR_MIPMAP_LINEAR,
            SharpGLTF.Schema2.TextureInterpolationFilter.LINEAR);
    }

    private static BlendAlpha BlendAlphaOf(PristonMaterial material, AlphaMode alphaMode)
    {
        if (alphaMode != AlphaMode.BLEND)
        {
            return BlendAlpha.Opaque;
        }

        // Model vertices are drawn with alpha 255 and stage transparency is already baked into the
        // stage vertex alpha, so alpha-blend and plain transparent materials keep an opaque texture.
        return material.BlendType switch
        {
            PristonBlendType.Color or PristonBlendType.Lamp or PristonBlendType.AddColor => BlendAlpha.Luminance,
            PristonBlendType.Shadow => BlendAlpha.Shadow,
            PristonBlendType.InverseShadow => BlendAlpha.InverseShadow,
            _ => BlendAlpha.Opaque
        };
    }

    private string BakePng(TextureSource source, int index, BlendAlpha blendAlpha)
    {
        var imageKey = new ImageKey(
            Normalize(source.TextureName),
            Normalize(source.OpacityName),
            blendAlpha);
        if (_pngPaths.TryGetValue(imageKey, out var existing))
        {
            return existing;
        }

        var identity = JsonSerializer.Serialize(new
        {
            imageKey.Texture,
            imageKey.Opacity,
            Mode = (int)imageKey.BlendAlpha
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var sourceName = string.IsNullOrWhiteSpace(source.TextureName)
            ? $"diffuse_{index}"
            : source.TextureName;
        var safeName = Sanitize(Path.GetFileNameWithoutExtension(sourceName.Replace('\\', '/')));
        var pngPath = Path.Combine(_pngDirectory, $"{safeName}_{hash}.png");
        var image = TryDecode(source.TextureName, out var sourcePath, out var reason);

        if (image.IsValid)
        {
            PrepareAlpha(ref image, sourcePath, source.OpacityName, blendAlpha);
            PngWriter.Write(pngPath, image.Width, image.Height, image.Rgba);
        }
        else
        {
            ReportTextureIssue(source.TextureName, reason);
            WriteWhiteFallback(pngPath);
        }

        _generatedPngs++;
        _pngPaths.Add(imageKey, pngPath);
        return pngPath;
    }

    private void PrepareAlpha(
        ref DecodedImage image,
        string sourcePath,
        string opacityName,
        BlendAlpha blendAlpha)
    {
        if (image.HasSourceAlpha)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(opacityName))
        {
            ApplyOpacity(ref image, sourcePath, opacityName);
            return;
        }

        switch (blendAlpha)
        {
            case BlendAlpha.Luminance:
                SetLuminanceAlpha(image);
                break;
            case BlendAlpha.Shadow:
                SetShadowAlpha(image, inverse: false);
                break;
            case BlendAlpha.InverseShadow:
                SetShadowAlpha(image, inverse: true);
                break;
            default:
                SetOpaque(image);
                break;
        }
    }

    private void ApplyOpacity(ref DecodedImage image, string sourcePath, string opacityName)
    {
        var mask = TryDecode(opacityName, out var maskPath, out var reason);
        if (!mask.IsValid)
        {
            ReportTextureIssue(opacityName, reason);
            SetOpaque(image);
            return;
        }

        if (string.Equals(sourcePath, maskPath, StringComparison.OrdinalIgnoreCase))
        {
            SetOpaque(image);
            return;
        }

        if (mask.Width != image.Width || mask.Height != image.Height)
        {
            ReportTextureIssue(
                opacityName,
                $"opacity dimensions {mask.Width}x{mask.Height} do not match {image.Width}x{image.Height}");
            SetOpaque(image);
            return;
        }

        for (var index = 0; index < image.Rgba.Length; index += 4)
        {
            image.Rgba[index + 3] = PristonOpacityMask.Alpha(mask.Rgba[index], mask.Rgba[index + 1], mask.Rgba[index + 2]);
        }
    }

    private DecodedImage TryDecode(
        string textureName,
        out string resolvedPath,
        out string reason)
    {
        resolvedPath = string.Empty;
        reason = "no texture name";
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return default;
        }

        resolvedPath = PristonTextureLoader.ResolvePath(_assetDirectory, textureName);
        if (resolvedPath.Length == 0)
        {
            reason = "file not found";
            return default;
        }

        var extension = Path.GetExtension(resolvedPath);
        var image = ImageDecode.Decode(File.ReadAllBytes(resolvedPath), extension);
        reason = image.IsValid ? "ok" : "decode failed";
        return image;
    }

    private static TextureSource SelectBaseTexture(PristonMaterial material)
    {
        if (!material.HasAnimatedTexture)
        {
            return new TextureSource(material.TextureName, material.OpacityName);
        }

        var frame = material.AnimationFrame == 0x100
            ? 0u
            : material.AnimationFrame;
        var index = (int)(frame % material.AnimationTextureNames.Count);
        return new TextureSource(
            material.AnimationTextureNames[index],
            string.Empty);
    }


    private void ReportTextureIssue(string textureName, string reason)
    {
        _missingTextures++;
        if (Environment.GetEnvironmentVariable("PTGLTF_DEBUG") == "1")
        {
            Console.Error.WriteLine($"  texture fallback: '{textureName}' ({reason})");
        }
    }

    private static void SetLuminanceAlpha(DecodedImage image)
    {
        for (var index = 0; index < image.Rgba.Length; index += 4)
        {
            image.Rgba[index + 3] = Math.Max(
                image.Rgba[index],
                Math.Max(image.Rgba[index + 1], image.Rgba[index + 2]));
        }
    }

    // Shadow multiplies the destination by the source colour and inverse shadow by its complement; both
    // are expressed as black coverage over the destination.
    private static void SetShadowAlpha(DecodedImage image, bool inverse)
    {
        for (var index = 0; index < image.Rgba.Length; index += 4)
        {
            var brightness = Math.Max(
                image.Rgba[index],
                Math.Max(image.Rgba[index + 1], image.Rgba[index + 2]));
            image.Rgba[index] = 0;
            image.Rgba[index + 1] = 0;
            image.Rgba[index + 2] = 0;
            image.Rgba[index + 3] = inverse ? brightness : (byte)(255 - brightness);
        }
    }

    private static void SetOpaque(DecodedImage image)
    {
        for (var index = 3; index < image.Rgba.Length; index += 4)
        {
            image.Rgba[index] = 255;
        }
    }

    private static void WriteWhiteFallback(string pngPath)
    {
        PngWriter.Write(pngPath, 1, 1, [255, 255, 255, 255]);
    }

    private static string Normalize(string name)
    {
        return name.Replace('\\', '/').ToUpperInvariant();
    }

    private static string Sanitize(string name)
    {
        var buffer = name.ToCharArray();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        for (var index = 0; index < buffer.Length; index++)
        {
            if (Array.IndexOf(invalidCharacters, buffer[index]) >= 0)
            {
                buffer[index] = '_';
            }
        }

        var sanitized = new string(buffer);
        return string.IsNullOrWhiteSpace(sanitized) ? "tex" : sanitized;
    }

    private readonly record struct TextureSource(
        string TextureName,
        string OpacityName);
}
