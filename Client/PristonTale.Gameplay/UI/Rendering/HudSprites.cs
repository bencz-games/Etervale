using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Gameplay.UI.Rendering;

// Original interface images by asset-relative path. Files are read and decoded on the thread pool;
// Update uploads finished images on the game thread within a fixed byte budget.
public sealed class HudSprites : IDisposable
{
    private const int UploadBudgetBytes = 512 * 1024;
    private const int MaximumDimension = 2048;

    private sealed record Prepared(string Path, DecodedPristonImage Image, bool Missing);

    private sealed class Upload(string path, DecodedPristonImage image)
    {
        public string Path { get; } = path;
        public DecodedPristonImage Image { get; } = image;
        public Texture2D Texture { get; set; }
        public int Row { get; set; }
    }

    private readonly GraphicsDevice _device;
    private readonly string _assetRoot;
    private readonly IClientLog _log;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _queued = [];
    private readonly Queue<Upload> _uploads = new();
    private Task<Prepared[]> _decode;
    private bool _disposed;

    public HudSprites(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        _device = device;
        _assetRoot = assetRoot;
        _log = log;
    }

    public bool Failed { get; private set; }
    public bool IsIdle => _queued.Count == 0 && _decode is null && _uploads.Count == 0;
    public long TextureBytes { get; private set; }

    public Texture2D Get(string path) => path is not null && _textures.TryGetValue(path, out var texture) ? texture : null;
    public bool IsResolved(string path) => _textures.ContainsKey(path) || _missing.Contains(path);
    public bool IsMissing(string path) => _missing.Contains(path);

    public void Request(string path)
    {
        if (_disposed || string.IsNullOrEmpty(path) || !_known.Add(path))
        {
            return;
        }

        _queued.Add(path);
    }

    public void Request(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Request(path);
        }
    }

    public void Release(string path)
    {
        if (path is null || !_known.Remove(path))
        {
            return;
        }

        _queued.Remove(path);
        _missing.Remove(path);
        if (_textures.Remove(path, out var texture))
        {
            TextureBytes -= (long)texture.Width * texture.Height * 4;
            texture.Dispose();
        }
    }

    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        CompleteDecode();
        if (_decode is null && _queued.Count > 0)
        {
            var batch = _queued.ToArray();
            _queued.Clear();
            var root = _assetRoot;
            _decode = Task.Run(() => Decode(root, batch));
            // Observe faults of a batch abandoned by Dispose before completion.
            _ = _decode.ContinueWith(task => _log.Error("HUD sprite decoding failed", task.Exception),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        UploadWithinBudget();
    }

    private void CompleteDecode()
    {
        if (_decode is null || !_decode.IsCompleted)
        {
            return;
        }

        var decode = _decode;
        _decode = null;
        if (!decode.IsCompletedSuccessfully)
        {
            Failed = true;
            return;
        }

        foreach (var prepared in decode.Result)
        {
            if (!_known.Contains(prepared.Path))
            {
                continue;
            }

            if (prepared.Missing)
            {
                _missing.Add(prepared.Path);
                continue;
            }

            _uploads.Enqueue(new Upload(prepared.Path, prepared.Image));
        }
    }

    private void UploadWithinBudget()
    {
        var budget = UploadBudgetBytes;
        while (budget > 0 && _uploads.TryPeek(out var upload))
        {
            if (!_known.Contains(upload.Path))
            {
                upload.Texture?.Dispose();
                _uploads.Dequeue();
                continue;
            }

            try
            {
                var image = upload.Image;
                upload.Texture ??= new Texture2D(_device, image.Width, image.Height, false, SurfaceFormat.Color);
                var rows = Math.Clamp(budget / (image.Width * 4), 1, image.Height - upload.Row);
                upload.Texture.SetData(0, new Rectangle(0, upload.Row, image.Width, rows), image.Pixels, upload.Row * image.Width, rows * image.Width);
                upload.Row += rows;
                budget -= rows * image.Width * 4;
                if (upload.Row < image.Height)
                {
                    continue;
                }

                _uploads.Dequeue();
                if (_textures.Remove(upload.Path, out var previous))
                {
                    TextureBytes -= (long)previous.Width * previous.Height * 4;
                    previous.Dispose();
                }

                _textures.Add(upload.Path, upload.Texture);
                TextureBytes += (long)image.Width * image.Height * 4;
            }
            catch (Exception exception)
            {
                _uploads.Dequeue();
                upload.Texture?.Dispose();
                _missing.Add(upload.Path);
                Failed = true;
                _log.Error($"HUD sprite upload failed: {upload.Path}", exception);
            }
        }
    }

    private static Prepared[] Decode(string assetRoot, string[] paths)
    {
        var result = new Prepared[paths.Length];
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            var file = AssetFiles.Combine(assetRoot, path);
            if (!AssetFiles.Exists(file))
            {
                result[index] = new Prepared(path, DecodedPristonImage.Empty, true);
                continue;
            }

            var extension = Path.GetExtension(file);
            DecodedPristonImage image;
            try
            {
                image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(file), extension, bmpCoverage: true);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
            {
                image = DecodedPristonImage.Empty;
            }

            if (!image.IsValid || image.Width > MaximumDimension || image.Height > MaximumDimension)
            {
                result[index] = new Prepared(path, DecodedPristonImage.Empty, true);
                continue;
            }

            PristonUiPixels.Prepare(image, ColorKeyFor(extension));
            result[index] = new Prepared(path, image, false);
        }

        return result;
    }

    // The original renderer enables an exact black color key for every authored UI bitmap; TGA files carry alpha.
    public static PristonUiColorKey ColorKeyFor(string extension) =>
        extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ? PristonUiColorKey.Black : PristonUiColorKey.None;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        foreach (var upload in _uploads)
        {
            upload.Texture?.Dispose();
        }

        _textures.Clear();
        _uploads.Clear();
        _queued.Clear();
        _decode = null;
        TextureBytes = 0;
    }
}
