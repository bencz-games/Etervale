using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonSkyLibrary : IDisposable
{
    private readonly PristonTextureLoader _textures;
    private readonly List<PristonGpuMesh> _meshes = [];
    private readonly List<RenderBatch[]> _profiles = [];
    private readonly List<bool> _animated = [];

    private PristonSkyLibrary(GraphicsDevice device, IClientLog log)
    {
        _textures = new PristonTextureLoader(device, log, displayReferred: true);
        _textures.Initialize();
    }

    public int ProfileCount => _profiles.Count;
    public long MeshBytes { get; private set; }
    public long TextureBytes { get; private set; }

    public static PristonSkyLibrary LoadBootstrap(GraphicsDevice device, string assetRoot, IClientLog log = null)
    {
        log ??= ConsoleClientLog.Instance;
        var started = Stopwatch.GetTimestamp();
        var library = new PristonSkyLibrary(device, log);
        try
        {
            library.Load(device, assetRoot);
            log.Info($"[Sky] {library.ProfileCount} profiles ready in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms; "
                + $"meshes {library.MeshBytes} B; textures {library.TextureBytes} B.");
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    private void Load(GraphicsDevice device, string root)
    {
        var manifest = PristonSkyManifest.Read(AssetFiles.Combine(root, "Effect/Sky/test32.txt"));
        var reader = new PristonSmdReader();
        var builder = new PristonGpuMeshBuilder(device);
        var materials = new PristonMaterialRuntimeFactory(_textures);
        var cache = new Dictionary<(string Path, Color Color), RenderBatch[]>();
        var textures = new HashSet<Texture2D>();
        foreach (var profile in manifest)
        {
            var batches = new List<RenderBatch>();
            foreach (var layer in profile)
            {
                // The distributed manifest names the bitmap instead of this compiled model.
                // Restrict the correction to that exact entry; never guess missing model names.
                var relative = layer.ModelPath;
                if (relative.Equals("Effect/Sky/losttemple/day/dayspace02.smd", StringComparison.OrdinalIgnoreCase))
                {
                    relative = "Effect/Sky/losttemple/day/space.smd";
                }

                var path = AssetFiles.Resolve(AssetFiles.Combine(root, relative));
                var key = (path, layer.Color);
                if (!cache.TryGetValue(key, out var prepared))
                {
                    var model = reader.ReadModel(path);
                    var groups = PristonModelGeometry.BuildGroups(model, Matrix.Identity, layer.Color);
                    var modelBatches = new List<RenderBatch>();
                    foreach (var (materialIndex, vertices) in groups)
                    {
                        if ((uint)materialIndex >= (uint)model.Materials.Count)
                        {
                            throw new InvalidDataException($"Invalid sky material {materialIndex} in {path}.");
                        }

                        // The sky dome tints each layer with its RGBA through the vertex colour. Self-illumination would discard
                        // that tint (it forces white diffuse), so sky layers are lit by the sky frame's unit ambient.
                        var authored = model.Materials[materialIndex] with { SelfIllum = 0f, TwoSided = true };
                        if (relative.Equals("Effect/Sky/losttemple/day/space.smd", StringComparison.OrdinalIgnoreCase)
                            && authored.TextureName.Equals("space.bmp", StringComparison.OrdinalIgnoreCase))
                        {
                            authored = authored with { TextureName = "dayspace02.BMP" };
                        }

                        var material = materials.Create(Path.GetDirectoryName(path), authored, softAlpha: true);
                        var mesh = builder.Upload(builder.PrepareStaticMesh(vertices.ToArray()));
                        _meshes.Add(mesh);
                        MeshBytes += mesh.VertexBuffer.VertexDeclaration.VertexStride * (long)mesh.VertexCount;
                        MeshBytes += mesh.IndexCount * (long)(mesh.IndexBuffer.IndexElementSize == IndexElementSize.SixteenBits ? 2 : 4);
                        modelBatches.Add(new RenderBatch([], [], mesh, material));
                        foreach (var texture in material.Textures)
                        {
                            textures.Add(texture);
                        }

                        foreach (var stage in material.Layers)
                        {
                            textures.Add(stage.Texture);
                        }
                    }

                    prepared = modelBatches.ToArray();
                    cache.Add(key, prepared);
                }

                batches.AddRange(prepared);
            }

            _profiles.Add(batches.ToArray());
            _animated.Add(batches.Exists(static batch => IsAnimated(batch.Material)));
        }

        foreach (var texture in textures)
        {
            TextureBytes += PristonTextureLoader.EstimateBytes(texture);
        }
    }

    internal ReadOnlySpan<RenderBatch> GetBatches(int profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(profile);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(profile, _profiles.Count);
        return _profiles[profile];
    }

    // Scrolling, multi-frame or water layers change the composed sky over time even within one profile.
    internal bool IsAnimated(int profile)
    {
        return profile >= 0 && profile < _animated.Count && _animated[profile];
    }

    private static bool IsAnimated(PristonMaterialRuntime material)
    {
        if (material.TextureFormState != 0 || material.Textures.Length > 1 || material.Water)
        {
            return true;
        }

        foreach (var layer in material.Layers)
        {
            if (layer.FormState != 0)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes)
        {
            mesh.Dispose();
        }

        _meshes.Clear();
        _profiles.Clear();
        _animated.Clear();
        _textures.Dispose();
    }
}
