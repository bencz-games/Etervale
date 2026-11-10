using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering.Gltf;

namespace PristonTale.Engine3D.Rendering;

// Actors drawn from a glTF model (PristonActor.Gltf): one batch per GltfActorBatch with its own compact palette and
// material, drawn with the GLTF variant of Surface.fx. The GPU buffers are shared by every actor of the same source and
// go when the last of them does; the materials' textures, uploaded once (the source then drops its packed maps), stay
// with the renderer, as the legacy textures stay in the texture loader's cache.
public sealed partial class PristonRenderer
{
    private readonly Dictionary<GltfActorSource, GltfGpuResource> _gltfGpuResources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GltfActorSource, GltfMaterialRuntime[]> _gltfMaterials = new(ReferenceEqualityComparer.Instance);

    private void BuildGltfBatches(ActorRuntime runtime, GltfActorSource source)
    {
        if (!_gltfGpuResources.TryGetValue(source, out var resource))
        {
            resource = CreateGltfGpuResource(source);
            _gltfGpuResources.Add(source, resource);
        }

        resource.ReferenceCount++;
        runtime.GltfResource = resource;
        foreach (var item in resource.Batches)
        {
            var batch = new RenderBatch(
                [],
                [],
                null,
                item.Material.Queue,
                ForceCastsShadow: true,
                IsSkinned: true,
                SkinnedBuffer: item.VertexBuffer,
                SkinnedIndexBuffer: item.IndexBuffer,
                SkinnedPrimitiveCount: item.IndexCount / 3,
                SkinnedVertexCount: item.VertexCount,
                SkinnedIndexCount: item.IndexCount,
                SkinnedBounds: item.Bounds,
                BoneCount: item.Bones.Length,
                IsActor: true,
                Gltf: item.Material);
            batch.UseSkinPalette(new PristonSkinPalette(item.Bones.Length));
            runtime.AddGltfBatch(batch, item.Bones);
        }
    }

    private GltfGpuResource CreateGltfGpuResource(GltfActorSource source)
    {
        var materials = GltfMaterials(source);
        var batches = new List<GltfGpuBatch>();
        try
        {
            foreach (var batch in source.Batches)
            {
                VertexBuffer vertices = null;
                IndexBuffer indices = null;
                try
                {
                    vertices = new VertexBuffer(_graphicsDevice, GltfGpuVertex.VertexDeclaration, batch.Vertices.Length, BufferUsage.WriteOnly);
                    vertices.SetData(batch.Vertices);
                    indices = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, batch.Indices.Length, BufferUsage.WriteOnly);
                    indices.SetData(batch.Indices);
                }
                catch
                {
                    vertices?.Dispose();
                    indices?.Dispose();
                    throw;
                }

                batches.Add(new GltfGpuBatch(materials[batch.Material], vertices, indices, batch.Vertices.Length, batch.Indices.Length, batch.Bones,
                    batch.Bounds));
            }
        }
        catch
        {
            DisposeGltf(batches);
            throw;
        }

        return new GltfGpuResource(source, [.. batches]);
    }

    private GltfMaterialRuntime[] GltfMaterials(GltfActorSource source)
    {
        if (_gltfMaterials.TryGetValue(source, out var materials))
        {
            return materials;
        }

        var packs = source.Packs;
        materials = new GltfMaterialRuntime[packs.Length];
        try
        {
            for (var index = 0; index < packs.Length; index++)
            {
                materials[index] = GltfMaterialRuntime.Create(_graphicsDevice, packs[index], _textureLoader.White);
            }
        }
        catch
        {
            DisposeGltfMaterials(materials);
            throw;
        }

        _gltfMaterials.Add(source, materials);
        source.ReleasePacks();
        return materials;
    }

    private void ReleaseGltfGpuResource(ActorRuntime runtime)
    {
        var resource = runtime.GltfResource;
        if (resource is null)
        {
            return;
        }

        runtime.GltfResource = null;
        if (--resource.ReferenceCount > 0)
        {
            return;
        }

        _gltfGpuResources.Remove(resource.Source);
        DisposeGltf(resource.Batches);
    }

    private static void DisposeGltf(IEnumerable<GltfGpuBatch> batches)
    {
        foreach (var batch in batches)
        {
            batch.VertexBuffer.Dispose();
            batch.IndexBuffer.Dispose();
        }
    }

    private static void DisposeGltfMaterials(GltfMaterialRuntime[] materials)
    {
        foreach (var material in materials)
        {
            material?.Dispose();
        }
    }

    private void DisposeAllGltfMaterials()
    {
        foreach (var materials in _gltfMaterials.Values)
        {
            DisposeGltfMaterials(materials);
        }

        _gltfMaterials.Clear();
    }

    // Fills and packs every glTF batch's palette from the actor's transforms (attached or not); the actor's world bounds
    // are the union of each batch's bind bounds under its own palette.
    private BoundingBox UpdateGltfPalettes(ActorRuntime runtime, Matrix[] transforms, Matrix world)
    {
        var bounds = default(BoundingBox);
        var first = true;
        foreach (var (batch, bones) in runtime.GltfBatches)
        {
            var palette = batch.SkinPalette;
            PristonActorPalette.Fill(transforms, bones, world, attached: false, palette.Matrices);
            palette.Pack(_motionFrame);
            var batchBounds = TransformBoundsByPalette(batch.SkinnedBounds, palette.Matrices);
            bounds = first ? batchBounds : BoundingBox.CreateMerged(bounds, batchBounds);
            first = false;
        }

        return bounds;
    }

    internal sealed record GltfGpuBatch(GltfMaterialRuntime Material, VertexBuffer VertexBuffer, IndexBuffer IndexBuffer, int VertexCount, int IndexCount,
        int[] Bones, BoundingBox Bounds);

    internal sealed class GltfGpuResource(GltfActorSource source, GltfGpuBatch[] batches)
    {
        public GltfActorSource Source { get; } = source;
        public GltfGpuBatch[] Batches { get; } = batches;
        public int ReferenceCount { get; set; }
    }
}
