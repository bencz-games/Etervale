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

// Stage and model geometry: groups, animated stage models and their merges, bounds and vertex conversion.
public sealed partial class PristonRenderer
{
    private void BuildStageGeometry(
        PristonStage stage,
        string assetDirectory,
        RenderStageHandle handle,
        StaticShadowCollector shadowCollector)
    {
        AddGroups(BuildStageGroups(stage), stage.Materials, assetDirectory, handle.Batches, shadowCollector);
    }

    private static Dictionary<int, List<PristonCornerVertex>> BuildStageGroups(PristonStage stage)
    {
        var groups = new Dictionary<int, List<PristonCornerVertex>>();
        var normals = PristonStageNormals.Compute(stage);
        for (var index = 0; index < stage.Faces.Count; index++)
        {
            var face = stage.Faces[index];
            if (!face.FitsIn(stage.Vertices.Count))
            {
                continue;
            }

            PristonCornerVertex.AddTriangle(
                GetGroup(groups, face.MaterialIndex),
                stage.Vertices[face.A].Position,
                stage.Vertices[face.B].Position,
                stage.Vertices[face.C].Position,
                stage.Vertices[face.A].Color,
                stage.Vertices[face.B].Color,
                stage.Vertices[face.C].Color,
                stage.TexLinks,
                face.TexLinkIndex,
                normals[index * 3],
                normals[index * 3 + 1],
                normals[index * 3 + 2]);
        }

        return groups;
    }

    private void AddModel(
        PristonModel model,
        string assetDirectory,
        RenderStageHandle handle,
        Matrix world,
        StaticShadowCollector shadowCollector,
        PristonStageBakedLight bakedLight)
    {
        AddGroups(
            WithBakedLight(PristonModelGeometry.BuildGroups(model, world, Color.White), model.Materials, bakedLight),
            model.Materials,
            assetDirectory,
            handle.Batches,
            shadowCollector);
    }

    // A scenery model's surfaces take the stage's authored light for their texture (PristonStageBakedLight).
    private static Dictionary<int, List<PristonCornerVertex>> WithBakedLight(
        Dictionary<int, List<PristonCornerVertex>> groups,
        IReadOnlyList<PristonMaterial> materials,
        PristonStageBakedLight bakedLight)
    {
        foreach (var (materialIndex, vertices) in groups)
        {
            var light = bakedLight.For(materials, materialIndex);
            for (var index = 0; index < vertices.Count; index++)
            {
                vertices[index] = vertices[index] with { Color = light };
            }
        }

        return groups;
    }

    private PreparedAnimatedModel PrepareAnimatedModel(PristonModel model, Matrix world, PristonStageBakedLight bakedLight)
    {
        var groups = new Dictionary<int, List<ModelTriangleRef>>();
        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var obj = model.Objects[objectIndex];
            foreach (var face in obj.Faces)
            {
                if (!face.FitsIn(obj.Vertices.Count))
                {
                    continue;
                }

                var list = GetModelGroup(groups, face.MaterialIndex);
                list.Add(new ModelTriangleRef(objectIndex, face));
            }
        }

        var boneCount = model.Objects.Count;
        if (boneCount > PristonShaderLibrary.MaxBones)
        {
            _log.Warn($"[PristonRenderer] AnimatedModel '{model.Path}' has {boneCount} bones, clamping to {PristonShaderLibrary.MaxBones}.");
            boneCount = PristonShaderLibrary.MaxBones;
        }

        var bindTransforms = BuildBindPoseTransforms(model);
        for (var i = 0; i < bindTransforms.Length; i++)
        {
            bindTransforms[i] *= world;
        }

        var cornerNormals = PristonModelNormals.Build(model, (objectIndex, _) => bindTransforms[Math.Min(objectIndex, bindTransforms.Length - 1)]);

        var batches = new List<PreparedAnimatedBatch>(groups.Count);
        foreach (var (materialIndex, triangles) in groups.OrderBy(pair => pair.Key))
        {
            var material = materialIndex >= 0 && materialIndex < model.Materials.Count ? model.Materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var mesh = PrepareSkinnedAnimatedMesh(
                model,
                triangles.ToArray(),
                cornerNormals,
                boneCount,
                bakedLight.For(material));
            batches.Add(new PreparedAnimatedBatch(material, mesh));
        }

        return new PreparedAnimatedModel(boneCount, batches.ToArray());
    }

    private void AddAnimatedModelGroups(
        IReadOnlyList<PreparedAnimatedInstance> models,
        RenderStageHandle handle)
    {
        foreach (var step in AddAnimatedModelGroupsIncrementally(models, handle))
        {
        }
    }

    private IEnumerable<StreamingWork> AddAnimatedModelGroupsIncrementally(
        IReadOnlyList<PreparedAnimatedInstance> models,
        RenderStageHandle handle)
    {
        var chunk = new List<PreparedAnimatedInstance>();
        var chunkBoneCount = 0;
        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            if (model.Prepared.Batches.Length == 0)
            {
                continue;
            }

            if (chunk.Count > 0
                && chunkBoneCount + model.Prepared.BoneCount > PristonShaderLibrary.MaxBones)
            {
                foreach (var step in AddAnimatedStageGroupSteps(chunk, chunkBoneCount, handle))
                {
                    yield return step;
                }

                chunk = [];
                chunkBoneCount = 0;
            }

            chunk.Add(model);
            chunkBoneCount += model.Prepared.BoneCount;
        }

        if (chunk.Count > 0)
        {
            foreach (var step in AddAnimatedStageGroupSteps(chunk, chunkBoneCount, handle))
            {
                yield return step;
            }
        }
    }

    private IEnumerable<StreamingWork> AddAnimatedStageGroupSteps(
        IReadOnlyList<PreparedAnimatedInstance> models,
        int boneCount,
        RenderStageHandle handle)
    {
        var group = new AnimatedStageGroupRuntime(boneCount);
        var pending = new List<PendingAnimatedBatch>();
        var merged = new Dictionary<AnimatedMergeKey, PendingAnimatedBatch>();
        var shadowMeshes = new List<PreparedSkinnedMesh>();
        var shadowModels = new List<AnimatedModelRuntime>();
        PristonMaterialRuntime shadowMaterial = null;
        var paletteOffset = 0;
        for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            var source = models[modelIndex];
            var runtime = new AnimatedModelRuntime(
                source.Model,
                group.Palette,
                paletteOffset)
            {
                World = source.World
            };
            group.Models.Add(runtime);
            handle.AnimatedModels.Add(runtime);

            foreach (var sourceBatch in source.Prepared.Batches)
            {
                runtime.IncludeBindBounds(sourceBatch.Mesh.Bounds);
                var material = _materialFactory.Create(source.Directory, sourceBatch.Material);
                var mesh = OffsetBoneIndices(sourceBatch.Mesh, paletteOffset);
                if (CanMergeAnimated(material))
                {
                    var key = CreateAnimatedMergeKey(material);
                    if (!merged.TryGetValue(key, out var batch))
                    {
                        batch = new PendingAnimatedBatch(material, [], []);
                        merged.Add(key, batch);
                        pending.Add(batch);
                    }

                    batch.Meshes.Add(mesh);
                    if (!batch.Models.Contains(runtime))
                    {
                        batch.Models.Add(runtime);
                    }
                }
                else
                {
                    pending.Add(new PendingAnimatedBatch(material, [mesh], [runtime]));
                }
            }

            paletteOffset += source.Prepared.BoneCount;
        }

        foreach (var item in pending)
        {
            var shadowCoveredByAggregate = CanAggregateShadow(item.Material);
            if (shadowCoveredByAggregate)
            {
                shadowMaterial ??= item.Material;
                shadowMeshes.AddRange(item.Meshes);
                foreach (var model in item.Models)
                {
                    if (!shadowModels.Contains(model))
                    {
                        shadowModels.Add(model);
                    }
                }
            }

            var mesh = item.Meshes.Count == 1
                ? item.Meshes[0]
                : MergeSkinnedMeshes(item.Meshes);
            var (vb, ib, primCount, vertCount, idxCount, bounds) = UploadSkinnedMesh(mesh);
            var batch = new RenderBatch(
                [],
                [],
                null,
                item.Material,
                ForceCastsShadow: true,
                ShadowCoveredByAggregate: shadowCoveredByAggregate,
                IsSkinned: true,
                SkinnedBuffer: vb,
                SkinnedIndexBuffer: ib,
                SkinnedPrimitiveCount: primCount,
                SkinnedVertexCount: vertCount,
                SkinnedIndexCount: idxCount,
                SkinnedBounds: bounds,
                BoneCount: group.Palette.Count);
            batch.UseSkinPalette(group.Palette);
            group.Batches.Add(new AnimatedStageBatchRuntime(batch, item.Models.ToArray()));
            group.ColorBatchCount++;
            handle.Batches.Add(batch);
            yield return new StreamingWork(StreamingStep.AnimatedMeshUpload, (long)vertCount * PristonSkinnedVertex.VertexDeclaration.VertexStride);
        }

        if (shadowMeshes.Count > 0)
        {
            var mesh = shadowMeshes.Count == 1
                ? shadowMeshes[0]
                : MergeSkinnedMeshes(shadowMeshes);
            var (vb, ib, primitiveCount, vertexCount, indexCount, bounds) = UploadSkinnedMesh(mesh);
            var batch = new RenderBatch(
                [],
                [],
                null,
                shadowMaterial,
                ForceCastsShadow: true,
                IsSkinned: true,
                SkinnedBuffer: vb,
                SkinnedIndexBuffer: ib,
                SkinnedPrimitiveCount: primitiveCount,
                SkinnedVertexCount: vertexCount,
                SkinnedIndexCount: indexCount,
                SkinnedBounds: bounds,
                BoneCount: group.Palette.Count);
            batch.UseSkinPalette(group.Palette);
            group.Batches.Add(new AnimatedStageBatchRuntime(batch, shadowModels.ToArray()));
            group.ShadowAggregateCount++;
            handle.ShadowBatches.Add(batch);
            yield return new StreamingWork(StreamingStep.ShadowAggregateUpload, (long)vertexCount * PristonSkinnedVertex.VertexDeclaration.VertexStride);
        }

        handle.AnimatedGroups.Add(group);
        UpdateAnimatedStageGroup(group);
    }

    private static PreparedSkinnedMesh OffsetBoneIndices(
        PreparedSkinnedMesh mesh,
        int paletteOffset)
    {
        if (paletteOffset == 0)
        {
            return mesh;
        }

        var vertices = (PristonSkinnedVertex[])mesh.Vertices.Clone();
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index].BoneIndex += paletteOffset;
        }

        return mesh with { Vertices = vertices };
    }

    private static bool CanMergeAnimated(PristonMaterialRuntime material)
    {
        return material.TextureFormState == 0
            && !material.Transparent
            && !material.HasLayers
            && material.Textures.Length == 1;
    }

    private static AnimatedMergeKey CreateAnimatedMergeKey(PristonMaterialRuntime material)
    {
        return new AnimatedMergeKey(
            material.Textures[0],
            material.AlphaTest,
            material.TwoSided,
            material.SelfIllum,
            material.Blinking,
            material.Water,
            material.Wind,
            material.Reflex,
            material.BlendState);
    }

    private static PreparedSkinnedMesh MergeSkinnedMeshes(IReadOnlyList<PreparedSkinnedMesh> meshes)
    {
        var vertexCount = 0;
        var indexCount = 0;
        var triangleCount = 0;
        var bounds = meshes[0].Bounds;
        for (var index = 0; index < meshes.Count; index++)
        {
            var mesh = meshes[index];
            vertexCount = checked(vertexCount + mesh.Vertices.Length);
            indexCount = checked(indexCount + mesh.Indices.Length);
            triangleCount = checked(triangleCount + mesh.TriangleCount);
            bounds = BoundingBox.CreateMerged(bounds, mesh.Bounds);
        }

        var vertices = new PristonSkinnedVertex[vertexCount];
        var indices = new int[indexCount];
        var vertexOffset = 0;
        var indexOffset = 0;
        for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
        {
            var mesh = meshes[meshIndex];
            mesh.Vertices.AsSpan().CopyTo(vertices.AsSpan(vertexOffset));
            for (var index = 0; index < mesh.Indices.Length; index++)
            {
                indices[indexOffset + index] = checked(mesh.Indices[index] + vertexOffset);
            }

            vertexOffset += mesh.Vertices.Length;
            indexOffset += mesh.Indices.Length;
        }

        return new PreparedSkinnedMesh(vertices, indices, triangleCount, bounds);
    }

    private void UpdateAnimatedStageGroup(AnimatedStageGroupRuntime group)
    {
        var changed = false;
        foreach (var runtime in group.Models)
        {
            if (runtime.PaletteIsCurrent())
            {
                continue;
            }

            // Most stage objects are authored in world space (World == Identity); an object authored
            // in local space carries an explicit world placement. Each source model retains its own
            // loop length while writing into a shared shader-palette slice.
            var transforms = runtime.Animator.Evaluate(runtime.Frame);
            var world = runtime.World;
            var palette = group.Palette.Matrices;
            for (var index = 0; index < runtime.PaletteCount; index++)
            {
                palette[runtime.PaletteOffset + index] = transforms[index] * world;
            }

            var worldBounds = TransformBoundsByPalette(
                runtime.BindBounds,
                palette.AsSpan(runtime.PaletteOffset, runtime.PaletteCount));
            runtime.SetWorldBounds(worldBounds);
            runtime.MarkPaletteCurrent();
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        group.Palette.Pack(_motionFrame);
        foreach (var batch in group.Batches)
        {
            var bounds = batch.Models[0].WorldBounds;
            for (var index = 1; index < batch.Models.Length; index++)
            {
                bounds = BoundingBox.CreateMerged(bounds, batch.Models[index].WorldBounds);
            }

            batch.Batch.SetWorldBounds(bounds);
        }
    }

    // The stage's authored light on the floor under a point, the same light the floor itself is tinted by; full light
    // where no stage floor lies under it.
    private float FloorLightAt(Vector3 point)
    {
        if (!ActorFloorLight)
        {
            return 1f;
        }

        foreach (var stage in _stages)
        {
            if (stage.LightField is not null && stage.LightField.TryLightAt(point, out var light))
            {
                return light;
            }
        }

        return 1f;
    }

    private static void ApplyWorldBounds(IReadOnlyList<RenderBatch> batches, BoundingBox worldBounds)
    {
        for (var index = 0; index < batches.Count; index++)
        {
            batches[index].SetWorldBounds(worldBounds);
        }
    }

    // World-space AABB of a rigidly-skinned bind-pose box under a bone palette. For each affine bone,
    // transform the centre once and obtain the exact transformed extents from abs(linearMatrix).
    // This is equivalent to transforming all eight corners but removes seven point transforms per bone.
    internal static BoundingBox TransformBoundsByPalette(BoundingBox bind, ReadOnlySpan<Matrix> palette)
    {
        var center = (bind.Min + bind.Max) * 0.5f;
        var extent = (bind.Max - bind.Min) * 0.5f;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var index = 0; index < palette.Length; index++)
        {
            var bone = palette[index];
            var transformedCenter = Vector3.Transform(center, bone);
            var transformedExtent = new Vector3(
                extent.X * MathF.Abs(bone.M11) + extent.Y * MathF.Abs(bone.M21) + extent.Z * MathF.Abs(bone.M31),
                extent.X * MathF.Abs(bone.M12) + extent.Y * MathF.Abs(bone.M22) + extent.Z * MathF.Abs(bone.M32),
                extent.X * MathF.Abs(bone.M13) + extent.Y * MathF.Abs(bone.M23) + extent.Z * MathF.Abs(bone.M33));
            min = Vector3.Min(min, transformedCenter - transformedExtent);
            max = Vector3.Max(max, transformedCenter + transformedExtent);
        }

        return new BoundingBox(min, max);
    }

    private void AddGroups(
        Dictionary<int, List<PristonCornerVertex>> groups,
        IReadOnlyList<PristonMaterial> materials,
        string assetDirectory,
        List<RenderBatch> target,
        StaticShadowCollector shadowCollector)
    {
        var merged = new Dictionary<StaticMergeKey, (PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
        foreach (var (materialIndex, vertices) in groups)
        {
            if (vertices.Count == 0)
            {
                continue;
            }

            var material = materialIndex >= 0 && materialIndex < materials.Count ? materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var runtimeMaterial = _materialFactory.Create(assetDirectory, material);
            if (runtimeMaterial.Invisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var shadowCoveredByAggregate = CanAggregateShadow(runtimeMaterial);
            if (shadowCoveredByAggregate)
            {
                shadowCollector.Add(runtimeMaterial, vertices);
            }

            if (ShadowDiagnostics && !runtimeMaterial.CastsShadow)
            {
                _log.Info($"[ShadowDiag] non-caster tex='{material.TextureName}' opacity='{material.OpacityName}' " +
                    $"blend={material.BlendType} transp={material.Transparency:0.###} " +
                    $"alphaTest={runtimeMaterial.AlphaTest} transparent={runtimeMaterial.Transparent} " +
                    $"selfIllum={runtimeMaterial.SelfIllum} dir='{Path.GetFileName(assetDirectory)}'");
            }

            var mergeable = CanMerge(runtimeMaterial);
            if (mergeable)
            {
                var key = new StaticMergeKey(
                    runtimeMaterial.Textures[0],
                    runtimeMaterial.AlphaTest,
                    runtimeMaterial.TwoSided,
                    runtimeMaterial.SelfIllum,
                    runtimeMaterial.BlendState);
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = (runtimeMaterial, []);
                    merged.Add(key, entry);
                }

                entry.Vertices.AddRange(vertices);
            }
            else
            {
                var vertexArray = vertices.ToArray();
                var mesh = runtimeMaterial.TextureFormState == 0 ? _meshBuilder.CreateStaticMesh(vertexArray) : null;
                var dynamicVertices = mesh is null ? ToDynamicVertices(vertexArray) : [];
                target.Add(new RenderBatch(
                    dynamicVertices,
                    [],
                    mesh,
                    runtimeMaterial,
                    ShadowCoveredByAggregate: shadowCoveredByAggregate));
            }
        }

        foreach (var (_, entry) in merged)
        {
            var vertexArray = entry.Vertices.ToArray();
            var mesh = _meshBuilder.CreateStaticMesh(vertexArray);
            target.Add(new RenderBatch(
                [],
                [],
                mesh,
                entry.Material,
                ShadowCoveredByAggregate: CanAggregateShadow(entry.Material)));
        }
    }

    // Unwelded per-triangle vertices for UV-form-state materials.
    private static PristonStaticVertex[] ToDynamicVertices(PristonCornerVertex[] vertices)
    {
        var dynamicVertices = new PristonStaticVertex[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            dynamicVertices[i] = new PristonStaticVertex(
                vertex.Position,
                Vector3.Up,
                vertex.Color,
                vertex.Uv0,
                vertex.Uv1,
                vertex.Uv2);
        }

        PristonVertexNormals.RecalculateTriangleNormals(dynamicVertices);
        return dynamicVertices;
    }

    private static List<PristonCornerVertex> GetGroup(Dictionary<int, List<PristonCornerVertex>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static List<ModelTriangleRef> GetModelGroup(Dictionary<int, List<ModelTriangleRef>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static List<ActorTriangleRef> GetActorGroup(Dictionary<int, List<ActorTriangleRef>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }
}
