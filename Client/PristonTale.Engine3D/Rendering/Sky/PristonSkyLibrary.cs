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
    // A profile whose opaque layers stop below this elevation (radians) leaves the zenith open: the rain dome is a
    // cylinder whose rim stands about 22 degrees up.
    public const float OpenTopElevation = 0.8f;

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
        var cache = new Dictionary<(string Path, Color Color), PreparedLayer>();
        var caps = new Dictionary<PreparedLayer, RenderBatch[]>();
        var textures = new HashSet<Texture2D>();
        foreach (var profile in manifest)
        {
            var batches = new List<RenderBatch>();
            var top = float.NegativeInfinity;
            PreparedLayer outer = null;
            var afterOuter = 0;
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
                    var opaque = new List<(PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
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
                        modelBatches.Add(new RenderBatch([], [], Upload(builder, vertices), material));
                        if (PristonBlendType.Covers(material.BlendType))
                        {
                            opaque.Add((material, vertices));
                        }

                        foreach (var texture in material.Textures)
                        {
                            textures.Add(texture);
                        }

                        foreach (var stage in material.Layers)
                        {
                            textures.Add(stage.Texture);
                        }
                    }

                    prepared = new PreparedLayer(modelBatches.ToArray(), opaque);
                    cache.Add(key, prepared);
                }

                batches.AddRange(prepared.Batches);
                top = MathF.Max(top, prepared.Top);
                if (prepared.Opaque.Count > 0 && (outer is null || prepared.Reach > outer.Reach))
                {
                    outer = prepared;
                    afterOuter = batches.Count;
                }
            }

            // A profile that leaves the zenith open (the rain dome's cylinder) is closed with its outermost layer
            // mirrored up over its rim, drawn right after that layer.
            if (outer is not null && top < OpenTopElevation)
            {
                if (!caps.TryGetValue(outer, out var cap))
                {
                    cap = outer.Opaque.Select(group => new RenderBatch([], [], Upload(builder, CapAbove(group.Vertices)), group.Material)).ToArray();
                    caps.Add(outer, cap);
                }

                batches.InsertRange(afterOuter, cap);
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

    private PristonGpuMesh Upload(PristonGpuMeshBuilder builder, List<PristonCornerVertex> vertices)
    {
        var mesh = builder.Upload(builder.PrepareStaticMesh(vertices.ToArray()));
        _meshes.Add(mesh);
        MeshBytes += mesh.VertexBuffer.VertexDeclaration.VertexStride * (long)mesh.VertexCount;
        MeshBytes += mesh.IndexCount * (long)(mesh.IndexBuffer.IndexElementSize == IndexElementSize.SixteenBits ? 2 : 4);
        return mesh;
    }

    // An open layer mirrored up over its rim onto the dome to the zenith: each vertex keeps its direction, distance and
    // texture coordinates, and its elevation below the rim becomes as far above it, stretched so the layer's lowest ring
    // meets at the zenith. The rim's vertices stay where they are, so the cap continues the layer without a seam.
    internal static List<PristonCornerVertex> CapAbove(List<PristonCornerVertex> vertices)
    {
        var rim = HighestElevation(vertices);
        var lowest = float.PositiveInfinity;
        foreach (var vertex in vertices)
        {
            lowest = MathF.Min(lowest, Elevation(vertex.Position));
        }

        var stretch = (MathHelper.PiOver2 - rim) / MathF.Max(rim - lowest, 1e-3f);
        var cap = new List<PristonCornerVertex>(vertices.Count);
        foreach (var vertex in vertices)
        {
            var position = vertex.Position;
            var radial = Radial(position);
            var across = radial > 1e-4f ? new Vector2(position.X, position.Z) / radial : Vector2.UnitX;
            var lifted = MathF.Min(MathHelper.PiOver2, rim + (rim - Elevation(position)) * stretch);
            var distance = position.Length();
            var horizontal = MathF.Cos(lifted) * distance;
            cap.Add(vertex with { Position = new Vector3(across.X * horizontal, MathF.Sin(lifted) * distance, across.Y * horizontal) });
        }

        return cap;
    }

    // The elevation (radians) of a layer's highest vertex seen from the dome's centre.
    internal static float HighestElevation(List<PristonCornerVertex> vertices)
    {
        var highest = float.NegativeInfinity;
        foreach (var vertex in vertices)
        {
            highest = MathF.Max(highest, Elevation(vertex.Position));
        }

        return highest;
    }

    private static float Elevation(Vector3 position) => MathF.Atan2(position.Y, Radial(position));

    // The distance from the dome's vertical axis.
    private static float Radial(Vector3 position) => MathF.Sqrt(position.X * position.X + position.Z * position.Z);

    // One manifest layer once uploaded: its batches, and its opaque groups with how high they reach and how far out they
    // stand (the outermost open layer of a profile is the one its cap mirrors).
    private sealed class PreparedLayer
    {
        public PreparedLayer(RenderBatch[] batches, List<(PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)> opaque)
        {
            Batches = batches;
            Opaque = opaque;
            foreach (var (_, vertices) in opaque)
            {
                Top = MathF.Max(Top, HighestElevation(vertices));
                foreach (var vertex in vertices)
                {
                    Reach = MathF.Max(Reach, Radial(vertex.Position));
                }
            }
        }

        public RenderBatch[] Batches { get; }
        public List<(PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)> Opaque { get; }
        public float Top { get; } = float.NegativeInfinity;
        public float Reach { get; }
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
