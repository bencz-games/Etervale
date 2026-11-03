using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

// Actor geometry: skinned meshes, bone maps, the per-actor GPU resources and the actor's motion selection.
public sealed partial class PristonRenderer
{
    private ActorRuntime BuildActor(PristonActorInstance instance)
    {
        var actor = instance.Actor;
        var runtime = new ActorRuntime(instance);
        var meshDir = Path.GetDirectoryName(actor.Mesh.Path) ?? ".";
        var prepared = PreparedActorGeometries.GetValue(actor, PrepareActorGeometry);
        runtime.SetBoneMap(prepared.BoneMap);
        var resource = AcquireActorGpuResource(actor, meshDir, prepared);
        runtime.GpuResource = resource;

        try
        {
            foreach (var item in resource.Batches)
            {
                var batch = new RenderBatch(
                    [],
                    [],
                    null,
                    item.Material,
                    ForceCastsShadow: true,
                    ShadowCoveredByAggregate: item.ShadowCoveredByAggregate,
                    IsSkinned: true,
                    SkinnedBuffer: item.VertexBuffer,
                    SkinnedIndexBuffer: item.IndexBuffer,
                    SkinnedPrimitiveCount: item.PrimitiveCount,
                    SkinnedVertexCount: item.VertexCount,
                    SkinnedIndexCount: item.IndexCount,
                    SkinnedBounds: item.Bounds,
                    BoneCount: prepared.BoneMap.Length,
                    IsActor: true);
                batch.UseSkinPalette(runtime.Palette);
                runtime.AddBatch(batch);
            }

            if (resource.ShadowAggregate is not null)
            {
                var item = resource.ShadowAggregate;
                var shadowBatch = new RenderBatch(
                    [],
                    [],
                    null,
                    item.Material,
                    ForceCastsShadow: true,
                    IsSkinned: true,
                    SkinnedBuffer: item.VertexBuffer,
                    SkinnedIndexBuffer: item.IndexBuffer,
                    SkinnedPrimitiveCount: item.PrimitiveCount,
                    SkinnedVertexCount: item.VertexCount,
                    SkinnedIndexCount: item.IndexCount,
                    SkinnedBounds: item.Bounds,
                    BoneCount: prepared.BoneMap.Length,
                    IsActor: true);
                shadowBatch.UseSkinPalette(runtime.Palette);
                runtime.AddShadowBatch(shadowBatch);
            }

            UpdateActor(runtime);
            return runtime;
        }
        catch
        {
            ReleaseActorGpuResource(runtime);
            throw;
        }
    }

    private ActorGpuResource AcquireActorGpuResource(
        PristonActor actor,
        string meshDirectory,
        PreparedActorGeometry prepared)
    {
        if (!_actorGpuResources.TryGetValue(actor, out var resource))
        {
            var materials = new PristonMaterialRuntime[prepared.Batches.Length];
            var aggregateCandidates = new List<PreparedSkinnedMesh>(prepared.Batches.Length);
            PristonMaterialRuntime shadowMaterial = null;
            for (var index = 0; index < prepared.Batches.Length; index++)
            {
                var item = prepared.Batches[index];
                var material = _materialFactory.Create(meshDirectory, item.Material);
                materials[index] = material;
                if (CanAggregateShadow(material))
                {
                    shadowMaterial ??= material;
                    aggregateCandidates.Add(item.Mesh);
                }
            }

            var hasShadowAggregate = aggregateCandidates.Count > 1;
            var batches = new List<ActorGpuBatch>(prepared.Batches.Length);
            ActorGpuBatch shadowAggregate = null;
            try
            {
                for (var index = 0; index < prepared.Batches.Length; index++)
                {
                    var item = prepared.Batches[index];
                    var material = materials[index];
                    var (vertexBuffer, indexBuffer, primitiveCount, vertexCount, indexCount, bounds) = UploadSkinnedMesh(item.Mesh);
                    batches.Add(new ActorGpuBatch(
                        material,
                        vertexBuffer,
                        indexBuffer,
                        primitiveCount,
                        vertexCount,
                        indexCount,
                        bounds,
                        ShadowCoveredByAggregate: hasShadowAggregate && CanAggregateShadow(material)));
                }

                if (hasShadowAggregate)
                {
                    var merged = MergeSkinnedMeshes(aggregateCandidates);
                    var (vertexBuffer, indexBuffer, primitiveCount, vertexCount, indexCount, bounds) = UploadSkinnedMesh(merged);
                    shadowAggregate = new ActorGpuBatch(
                        shadowMaterial,
                        vertexBuffer,
                        indexBuffer,
                        primitiveCount,
                        vertexCount,
                        indexCount,
                        bounds);
                }

                resource = new ActorGpuResource(actor, batches.ToArray(), shadowAggregate);
                _actorGpuResources.Add(actor, resource);
            }
            catch
            {
                DisposeActorGpuBatches(batches);
                DisposeActorGpuBatch(shadowAggregate);
                throw;
            }
        }

        resource.ReferenceCount++;
        return resource;
    }

    private void ReleaseActorGpuResource(ActorRuntime runtime)
    {
        var resource = runtime.GpuResource;
        if (resource is null)
        {
            return;
        }

        runtime.GpuResource = null;
        resource.ReferenceCount--;
        if (resource.ReferenceCount > 0)
        {
            return;
        }

        _actorGpuResources.Remove(resource.Actor);
        DisposeActorGpuBatches(resource.Batches);
        DisposeActorGpuBatch(resource.ShadowAggregate);
    }

    private static void DisposeActorGpuBatches(IEnumerable<ActorGpuBatch> batches)
    {
        foreach (var batch in batches)
        {
            batch.VertexBuffer.Dispose();
            batch.IndexBuffer.Dispose();
        }
    }

    private static void DisposeActorGpuBatch(ActorGpuBatch batch)
    {
        batch?.VertexBuffer.Dispose();
        batch?.IndexBuffer.Dispose();
    }

    private PreparedActorGeometry PrepareActorGeometry(PristonActor actor)
    {
        var groups = new Dictionary<int, List<ActorTriangleRef>>();
        var boneLookup = actor.Skeleton.ObjectIndexByName();
        var meshTransforms = BuildBindPoseTransforms(actor.Mesh);

        // The shared armor mesh contains several variants and LODs; the INX group selects the visible objects.
        var renderFilter = actor.RenderObjectNames.Count > 0
            ? new HashSet<string>(actor.RenderObjectNames, StringComparer.OrdinalIgnoreCase)
            : null;

        for (var objectIndex = 0; objectIndex < actor.Mesh.Objects.Count; objectIndex++)
        {
            var obj = actor.Mesh.Objects[objectIndex];
            if (renderFilter != null && !renderFilter.Contains(obj.Name))
                continue;

            foreach (var face in obj.Faces)
            {
                if (!face.FitsIn(obj.Vertices.Count))
                    continue;

                var list = GetActorGroup(groups, face.MaterialIndex);
                var a = PristonModelGeometry.ResolvePhysiqueBone(obj, face.A, boneLookup);
                var b = PristonModelGeometry.ResolvePhysiqueBone(obj, face.B, boneLookup);
                var c = PristonModelGeometry.ResolvePhysiqueBone(obj, face.C, boneLookup);
                list.Add(new ActorTriangleRef(
                    objectIndex,
                    face,
                    a,
                    b,
                    c,
                    meshTransforms[objectIndex]));
            }
        }

        // Upload only referenced bones so helper nodes do not exhaust the shader palette.
        var boneMap = BuildActorBoneMap(groups.Values, actor.Skeleton.Objects.Count, actor.Skeleton.Path, _log, out var boneRemap);
        var skeletonPose = BuildBindPoseTransforms(actor.Skeleton);
        var cornerNormals = PristonModelNormals.Build(actor.Mesh, (objectIndex, vertexIndex) =>
        {
            var bone = PristonModelGeometry.ResolvePhysiqueBone(actor.Mesh.Objects[objectIndex], vertexIndex, boneLookup);
            return skeletonPose[Math.Min(EffectiveBone(bone, objectIndex), skeletonPose.Length - 1)];
        });
        var batches = new List<PreparedActorBatch>(groups.Count);
        foreach (var materialIndex in groups.Keys.OrderBy(static k => k))
        {
            var triangleRefs = groups[materialIndex].ToArray();
            var material = materialIndex >= 0 && materialIndex < actor.Mesh.Materials.Count ? actor.Mesh.Materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var mesh = PrepareSkinnedActorMesh(actor.Mesh, triangleRefs, cornerNormals, boneRemap);
            batches.Add(new PreparedActorBatch(material, mesh));
        }

        return new PreparedActorGeometry(boneMap, batches.ToArray());
    }

    private static PreparedSkinnedMesh PrepareSkinnedAnimatedMesh(
        PristonModel model,
        ModelTriangleRef[] triangleRefs,
        Dictionary<PristonModelFace, Vector3[]>[] cornerNormals,
        int boneCount,
        Color light)
    {
        var uniqueVerts = new List<PristonSkinnedVertex>();
        var vertLookup = new Dictionary<SkinnedVertKey, int>();
        var indices = new List<int>(triangleRefs.Length * 3);
        var minPos = new Vector3(float.MaxValue);
        var maxPos = new Vector3(float.MinValue);

        foreach (var tri in triangleRefs)
        {
            var obj = model.Objects[tri.ObjectIndex];
            var face = tri.Face;
            var stages = PristonStageLinks.Resolve(obj.TexLinks, face.TexLinkIndex);
            var boneIdx = Math.Min(tri.ObjectIndex, boneCount - 1);
            var boneF = (float)boneIdx;
            var normals = cornerNormals[tri.ObjectIndex][face];

            // Animated scenery takes the stage's authored light for its texture (PristonStageBakedLight).
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.A].Position, normals[0], light, stages, 0, boneF);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.B].Position, normals[1], light, stages, 1, boneF);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.C].Position, normals[2], light, stages, 2, boneF);
        }

        return PrepareSkinnedMesh(uniqueVerts, indices, minPos, maxPos, triangleRefs.Length);
    }

    private static PreparedSkinnedMesh PrepareSkinnedActorMesh(
        PristonModel mesh,
        ActorTriangleRef[] triangleRefs,
        Dictionary<PristonModelFace, Vector3[]>[] cornerNormals,
        Dictionary<int, int> boneRemap)
    {
        var uniqueVerts = new List<PristonSkinnedVertex>();
        var vertLookup = new Dictionary<SkinnedVertKey, int>();
        var indices = new List<int>(triangleRefs.Length * 3);
        var minPos = new Vector3(float.MaxValue);
        var maxPos = new Vector3(float.MinValue);

        foreach (var tri in triangleRefs)
        {
            var obj = mesh.Objects[tri.ObjectIndex];
            var face = tri.Face;
            var stages = PristonStageLinks.Resolve(obj.TexLinks, face.TexLinkIndex);
            var normals = cornerNormals[tri.ObjectIndex][face];

            var boneA = CompactBone(tri.ABoneIndex, tri.ObjectIndex, boneRemap);
            var boneB = CompactBone(tri.BBoneIndex, tri.ObjectIndex, boneRemap);
            var boneC = CompactBone(tri.CBoneIndex, tri.ObjectIndex, boneRemap);

            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.A].Position, normals[0], Color.White, stages, 0, (float)boneA);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.B].Position, normals[1], Color.White, stages, 1, (float)boneB);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.C].Position, normals[2], Color.White, stages, 2, (float)boneC);
        }

        return PrepareSkinnedMesh(uniqueVerts, indices, minPos, maxPos, triangleRefs.Length);
    }

    private static int EffectiveBone(int physiqueBoneIndex, int objectIndex) =>
        physiqueBoneIndex >= 0 ? physiqueBoneIndex : objectIndex;

    private static int CompactBone(int physiqueBoneIndex, int objectIndex, Dictionary<int, int> boneRemap) =>
        boneRemap.TryGetValue(EffectiveBone(physiqueBoneIndex, objectIndex), out var slot) ? slot : 0;

    private static int[] BuildActorBoneMap(
        IEnumerable<List<ActorTriangleRef>> groups,
        int skeletonBoneCount,
        string skeletonPath,
        IClientLog log,
        out Dictionary<int, int> remap)
    {
        var used = new SortedSet<int>();
        var maxBone = Math.Max(0, skeletonBoneCount - 1);
        foreach (var list in groups)
            foreach (var tri in list)
            {
                used.Add(Math.Min(EffectiveBone(tri.ABoneIndex, tri.ObjectIndex), maxBone));
                used.Add(Math.Min(EffectiveBone(tri.BBoneIndex, tri.ObjectIndex), maxBone));
                used.Add(Math.Min(EffectiveBone(tri.CBoneIndex, tri.ObjectIndex), maxBone));
            }

        var map = used.ToArray();
        if (map.Length > PristonShaderLibrary.MaxBones)
        {
            log.Warn($"[PristonRenderer] Actor skeleton '{skeletonPath}' skins to {map.Length} distinct bones, clamping to {PristonShaderLibrary.MaxBones}.");
            map = map[..PristonShaderLibrary.MaxBones];
        }
        else if (map.Length == 0)
        {
            map = [0];
        }

        remap = new Dictionary<int, int>(map.Length);
        for (var i = 0; i < map.Length; i++)
            remap[map[i]] = i;
        return map;
    }

    private static void AddSkinnedVertex(
        List<PristonSkinnedVertex> uniqueVerts,
        Dictionary<SkinnedVertKey, int> lookup,
        List<int> indices,
        ref Vector3 minPos,
        ref Vector3 maxPos,
        Vector3 position,
        Vector3 normal,
        Color color,
        PristonStageLinks stages,
        int corner,
        float boneIndex)
    {
        var uv0 = stages.Stage0.Corner(corner);
        var uv1 = stages.Stage1.Corner(corner);
        var uv2 = stages.Stage2.Corner(corner);
        var key = new SkinnedVertKey(position, normal, uv0, uv1, uv2, boneIndex);
        if (!lookup.TryGetValue(key, out var idx))
        {
            idx = uniqueVerts.Count;
            uniqueVerts.Add(new PristonSkinnedVertex(position, normal, color, uv0, boneIndex, uv1, uv2));
            lookup.Add(key, idx);
            minPos = Vector3.Min(minPos, position);
            maxPos = Vector3.Max(maxPos, position);
        }
        indices.Add(idx);
    }

    private static PreparedSkinnedMesh PrepareSkinnedMesh(
        List<PristonSkinnedVertex> vertices, List<int> indices, Vector3 minimum, Vector3 maximum, int triangleCount)
    {
        var bounds = vertices.Count > 0 ? new BoundingBox(minimum, maximum) : new BoundingBox(Vector3.Zero, Vector3.Zero);
        return new PreparedSkinnedMesh(vertices.ToArray(), indices.ToArray(), triangleCount, bounds);
    }

    private (VertexBuffer, IndexBuffer, int, int, int, BoundingBox) UploadSkinnedMesh(PreparedSkinnedMesh prepared)
    {
        var vertices = prepared.Vertices;
        var indices = prepared.Indices;
        var vertexBuffer = new VertexBuffer(_graphicsDevice, PristonSkinnedVertex.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
        IndexBuffer indexBuffer = null;
        try
        {
            vertexBuffer.SetData(vertices);
            if (vertices.Length <= ushort.MaxValue)
            {
                var shortIndices = new ushort[indices.Length];
                for (var index = 0; index < indices.Length; index++)
                {
                    shortIndices[index] = (ushort)indices[index];
                }

                indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(shortIndices);
            }
            else
            {
                indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(indices);
            }

            return (vertexBuffer, indexBuffer, prepared.TriangleCount, vertices.Length, indices.Length, prepared.Bounds);
        }
        catch
        {
            vertexBuffer.Dispose();
            indexBuffer?.Dispose();
            throw;
        }
    }

    // refresh: sample the pose again (false keeps the last one, CrowdAnimationLod); the world always follows.
    private void UpdateActor(ActorRuntime runtime, bool refresh = true)
    {
        if (runtime.PaletteIsCurrent(refresh))
        {
            return;
        }

        var skeletonTransforms = runtime.Parent.IsValid ? Array.Empty<Matrix>() : runtime.SamplePose(refresh);
        var world = runtime.World;

        var palette = runtime.Palette.Matrices;
        var boneMap = runtime.BoneMap;
        var paletteSize = palette.Length;
        PristonActorPalette.Fill(skeletonTransforms, boneMap, world, runtime.Parent.IsValid, palette);

        runtime.Palette.Pack(_motionFrame);
        // Batches share the packed palette. Refresh their WORLD-space bounds from the
        // animated bone positions, so frustum culling follows the actor (otherwise the
        // bind-pose AABB sits at the origin and the actor pops out when the camera moves).
        if (paletteSize > 0)
        {
            var worldBounds = TransformBoundsByPalette(runtime.BindBounds, palette.AsSpan(0, paletteSize));
            ApplyWorldBounds(runtime.Batches, worldBounds);
            ApplyWorldBounds(runtime.ShadowBatches, worldBounds);
            runtime.WorldBounds = worldBounds;
            runtime.HasWorldBounds = true;
            var centre = (worldBounds.Min + worldBounds.Max) * 0.5f;
            var floorLight = FloorLightAt(new Vector3(centre.X, worldBounds.Min.Y, centre.Z));
            foreach (var batch in runtime.Batches)
            {
                batch.FloorLight = floorLight;
            }
        }

        runtime.MarkPaletteCurrent();
    }

    private static Matrix[] BuildBindPoseTransforms(PristonModel model) =>
        new PristonModelAnimator(model).Evaluate(0f);

    private static PristonMotionInfo SelectActorMotion(PristonActor actor)
    {
        var info = actor.Info;
        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var motion = info.Motions[i];
            if (motion.State == (uint)MotionCode.Stand && motion.Repeat != 0 && motion.EndFrame > motion.StartFrame)
                return ApplyMotionFrameOffset(actor, motion);
        }

        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var motion = info.Motions[i];
            if (motion.Repeat != 0 && motion.EndFrame > motion.StartFrame)
                return ApplyMotionFrameOffset(actor, motion);
        }

        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var motion = info.Motions[i];
            if (motion.EndFrame > motion.StartFrame)
                return ApplyMotionFrameOffset(actor, motion);
        }

        return new PristonMotionInfo(
            0,
            0,
            1,
            [],
            0,
            0,
            0,
            1,
            0,
            0);
    }

    private static PristonMotionInfo SelectActorMotion(PristonActor actor, MotionCode desiredMotion, PristonMotionContext context)
    {
        if (desiredMotion == MotionCode.Any)
        {
            return SelectActorMotion(actor);
        }

        var selected = PristonMotionSelection.Select(actor.Info, desiredMotion, context);
        return selected is null ? SelectActorMotion(actor) : ApplyMotionFrameOffset(actor, selected);
    }

    private static PristonMotionInfo ApplyMotionFrameOffset(PristonActor actor, PristonMotionInfo motion)
    {
        var rangeIndex = motion.MotionFrame - 1;
        if (rangeIndex < 0 || rangeIndex >= actor.Skeleton.FrameRanges.Count)
            return motion;

        var range = actor.Skeleton.FrameRanges[rangeIndex];
        var offset = range.StartFrame / GameUnits.FrameTicks;
        if (offset == 0)
            return motion;

        return new PristonMotionInfo(
            motion.State,
            motion.StartFrame + (uint)offset,
            motion.EndFrame + (uint)offset,
            motion.EventFrames,
            motion.ItemCodeCount,
            motion.JobCodeBit,
            motion.MapPosition,
            motion.Repeat,
            motion.KeyCode,
            motion.MotionFrame);
    }
}
