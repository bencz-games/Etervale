using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonTextureLoader : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly IClientLog _log;
    private readonly SurfaceFormat _format;
    private readonly ConcurrentDictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<PreparedTexture>>> _preparing = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);
    // The classic alpha-test reference value.
    internal const byte AlphaCutoffByte = 60;
    private Texture2D _white;
    private sealed record PreparedTexture(int Width, int Height, Color[][] Levels);

    // Scene textures upload as sRGB formats, so the sampler decodes them before filtering and shaders read linear
    // colour. displayReferred keeps the encoded values (the sky's layers are composed in sRGB); UI textures always do.
    public PristonTextureLoader(GraphicsDevice graphicsDevice, IClientLog log = null, bool displayReferred = false)
    {
        _graphicsDevice = graphicsDevice;
        _log = log ?? ConsoleClientLog.Instance;
        _format = displayReferred ? SurfaceFormat.Color : SurfaceFormat.ColorSRgb;
    }

    public Texture2D White => _white;
    public bool RequirePreparedTextures { get; set; }

    public void Initialize()
    {
        _white = new Texture2D(_graphicsDevice, 1, 1);
        _white.SetData([Color.White]);
    }

    public Task PrepareTexturesAsync(string directory, IEnumerable<PristonMaterial> materials)
    {
        var pending = new List<Task>();
        foreach (var request in TextureRequests(materials))
        {
            pending.Add(PrepareAsync(directory, request.Name, request.Opacity));
        }

        return Task.WhenAll(pending);
    }

    // Decodes one texture on a worker so a later LoadTexture only uploads it.
    public Task PrepareTextureAsync(string directory, string name) => PrepareAsync(directory, name, string.Empty);

    // Yields the bytes uploaded by each texture so the caller can time and budget every step. A texture uploads whole:
    // an upload costs per call rather than per byte, so slicing it would not shorten the worst step.
    internal IEnumerable<long> UploadPreparedTextures(string directory, IEnumerable<PristonMaterial> materials)
    {
        foreach (var request in TextureRequests(materials))
        {
            var key = CacheKey(directory, request.Name, request.Opacity);
            if (_cache.ContainsKey(key))
            {
                continue;
            }

            var texture = LoadTexture(directory, request.Name, request.Opacity);
            yield return EstimateBytes(texture);
        }
    }

    internal static long EstimateBytes(Texture2D texture)
    {
        var bytes = 0L;
        for (var level = 0; level < texture.LevelCount; level++)
        {
            bytes += Math.Max(1, texture.Width >> level) * (long)Math.Max(1, texture.Height >> level) * 4;
        }

        return bytes;
    }

    internal static IEnumerable<(string Name, string Opacity)> TextureRequests(IEnumerable<PristonMaterial> materials)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var material in materials)
        {
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            // Must request exactly what PristonMaterialRuntimeFactory.LoadLayers uploads.
            for (var stage = 1; stage <= material.CombinedLayerCount; stage++)
            {
                if (seen.Add((material.StageTextureName(stage), string.Empty)))
                {
                    yield return (material.StageTextureName(stage), string.Empty);
                }
            }

            if (material.HasAnimatedTexture)
            {
                foreach (var frame in material.AnimationTextureNames)
                {
                    if (seen.Add((frame, string.Empty)))
                    {
                        yield return (frame, string.Empty);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(material.TextureName) && seen.Add((material.TextureName, material.OpacityName)))
            {
                yield return (material.TextureName, material.OpacityName);
            }
        }
    }

    private Task PrepareAsync(string directory, string name, string opacity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }

        var key = CacheKey(directory, name, opacity);
        if (_cache.ContainsKey(key))
        {
            return Task.CompletedTask;
        }

        return _preparing.GetOrAdd(key, _ => new Lazy<Task<PreparedTexture>>(() => DecodeAsync(directory, name, opacity))).Value;
    }

    private async Task<PreparedTexture> DecodeAsync(string directory, string name, string opacity)
    {
        await DecodeSlots.WaitAsync();
        try
        {
            return await Task.Run(() => Prepare(directory, name, opacity));
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    public Texture2D LoadTexture(string assetDirectory, string textureName, string opacityName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return White;
        }

        var key = CacheKey(assetDirectory, textureName, opacityName);
        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        PreparedTexture prepared;
        if (_preparing.TryGetValue(key, out var preparation) && preparation.Value.IsCompletedSuccessfully)
        {
            prepared = preparation.Value.Result;
        }
        else if (RequirePreparedTextures)
        {
            throw new InvalidOperationException($"Texture was not prepared before GPU upload: {textureName}");
        }
        else
        {
            // Explicit bootstrap path for loading screens and tools, never used by live streaming.
            prepared = Prepare(assetDirectory, textureName, opacityName);
        }

        var texture = Upload(key, prepared, _format);
        _cache[key] = texture;
        _preparing.TryRemove(key, out _);
        return texture;
    }

    // The texture once it is cached or its preparation finished (uploading it then); never decodes on the caller.
    public bool TryLoadPrepared(string assetDirectory, string textureName, out Texture2D texture)
    {
        texture = null;
        var key = CacheKey(assetDirectory, textureName, string.Empty);
        if (_cache.TryGetValue(key, out var existing))
        {
            texture = existing;
            return true;
        }

        if (!_preparing.TryGetValue(key, out var preparation) || !preparation.Value.IsCompleted)
        {
            return false;
        }

        if (!preparation.Value.IsCompletedSuccessfully)
        {
            _preparing.TryRemove(key, out _);
            return false;
        }

        texture = LoadTexture(assetDirectory, textureName, string.Empty);
        return true;
    }

    public Texture2D LoadTexture(string assetDirectory, string textureName)
    {
        return LoadTexture(assetDirectory, textureName, string.Empty);
    }

    public Texture2D LoadUiTexture(
        string assetDirectory,
        string textureName,
        PristonUiColorKey colorKey = PristonUiColorKey.None)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return White;
        }

        var key = CacheKey(assetDirectory, textureName, string.Empty) + $"|ui:{(int)colorKey}";
        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var prepared = PrepareUi(assetDirectory, textureName, colorKey);
        var texture = Upload(key, prepared, SurfaceFormat.Color);
        _cache[key] = texture;
        return texture;
    }

    private Texture2D Upload(string key, PreparedTexture prepared, SurfaceFormat format)
    {
        if (prepared.Levels.Length == 0)
        {
            return White;
        }

        var texture = new Texture2D(_graphicsDevice, prepared.Width, prepared.Height, prepared.Levels.Length > 1, format) { Name = key };
        try
        {
            for (var level = 0; level < prepared.Levels.Length; level++)
            {
                var pixels = prepared.Levels[level];
                texture.SetData(level, null, pixels, 0, pixels.Length);
            }
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        return texture;
    }

    private PreparedTexture PrepareUi(string directory, string name, PristonUiColorKey colorKey)
    {
        var path = ResolvePath(directory, name);
        if (path.Length == 0)
        {
            _log.Warn($"UI texture '{name}' in '{directory}' is missing; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), Path.GetExtension(path), bmpCoverage: true);
        if (!image.IsValid)
        {
            _log.Warn($"UI texture '{path}' is invalid; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        PristonUiPixels.Prepare(image, colorKey);
        return new PreparedTexture(image.Width, image.Height, [image.Pixels]);
    }

    private PreparedTexture Prepare(string directory, string name, string opacity)
    {
        var path = ResolvePath(directory, name);
        if (path.Length == 0)
        {
            _log.Warn($"Texture '{name}' in '{directory}' is missing; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        var image = ReadImage(path);
        if (!image.IsValid)
        {
            _log.Warn($"Texture '{path}' is invalid; using shared white fallback.");
            return new PreparedTexture(0, 0, []);
        }

        if (!image.HasSourceAlpha && !string.IsNullOrWhiteSpace(opacity))
        {
            var alphaPath = ResolvePath(directory, opacity);
            var mask = alphaPath.Length > 0 && !string.Equals(path, alphaPath, StringComparison.OrdinalIgnoreCase) ? ReadImage(alphaPath) : null;
            if (mask is { IsValid: true } && PristonOpacityMask.Applies(path, alphaPath, image.Width, image.Height, mask.Width, mask.Height))
            {
                for (var index = 0; index < image.Pixels.Length; index++)
                {
                    var color = image.Pixels[index];
                    var alpha = mask.Pixels[index];
                    image.Pixels[index] = new Color(color.R, color.G, color.B, PristonOpacityMask.Alpha(alpha.R, alpha.G, alpha.B));
                }
            }
        }

        var cutout = PristonMaterialRuntimeFactory.IsCutOut(path, opacity);
        var levels = PristonMipChain.Build(image.Pixels, image.Width, image.Height, cutout ? AlphaCutoffByte : (byte)0);
        return new PreparedTexture(image.Width, image.Height, levels);
    }

    private static DecodedPristonImage ReadImage(string path)
    {
        return PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), Path.GetExtension(path));
    }

    private static string CacheKey(string directory, string name, string opacity)
    {
        return Path.GetFullPath(directory).Replace('\\', '/') + "|" + name.Replace('\\', '/') + "|" + opacity.Replace('\\', '/');
    }

    // A texture a model names with another extension than the one shipped is looked for under these, in its own folder.
    private static readonly string[] TextureExtensions = [".bmp", ".tga", ".jpg", ".jpeg"];

    internal static string ResolvePath(string directory, string name)
    {
        var named = AssetFiles.Combine(directory, name);
        var resolved = AssetFiles.Resolve(named);
        if (File.Exists(resolved))
        {
            return resolved;
        }

        var stem = Path.ChangeExtension(named, null);
        foreach (var extension in TextureExtensions)
        {
            var candidate = AssetFiles.Resolve(stem + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    public void Dispose()
    {
        foreach (var texture in _cache.Values)
        {
            if (!ReferenceEquals(texture, _white))
            {
                texture.Dispose();
            }
        }

        _cache.Clear();
        _preparing.Clear();
        _white?.Dispose();
    }
}
