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

// Stage loading and streaming: textures, batches uploaded in budgeted steps, static shadow aggregates, removal.
public sealed partial class PristonRenderer
{
    public Task PrepareStageTexturesAsync(
        PristonStage stage,
        IReadOnlyList<PristonModel> models,
        CancellationToken cancellationToken = default)
    {
        var pending = new List<Task>
        {
            _textureLoader.PrepareTexturesAsync(Path.GetDirectoryName(stage.Path) ?? ".", PristonMaterialUsage.ForStage(stage))
                .WaitAsync(cancellationToken)
        };
        foreach (var model in models)
        {
            pending.Add(_textureLoader.PrepareTexturesAsync(Path.GetDirectoryName(model.Path) ?? ".", PristonMaterialUsage.ForModel(model))
                .WaitAsync(cancellationToken));
        }

        return Task.WhenAll(pending);
    }

    public Task PrepareActorResourcesAsync(PristonActor actor)
    {
        var textures = _textureLoader.PrepareTexturesAsync(
            Path.GetDirectoryName(actor.Mesh.Path) ?? ".",
            PristonMaterialUsage.ForActor(actor));
        var geometry = PreparedActorGeometries.TryGetValue(actor, out _)
            ? Task.CompletedTask
            : Task.Run(() =>
            {
                PreparedActorGeometries.GetValue(actor, PrepareActorGeometry);
            });
        return Task.WhenAll(textures, geometry);
    }

    public void RequirePreparedTextures() => _textureLoader.RequirePreparedTextures = true;

    public RenderStageHandle AddStage(PristonStage stage, IEnumerable<PristonModel> models) =>
        AddStage(stage, models.Select(static m => new PristonSceneryInstance(m, Matrix.Identity)));

    public RenderStageHandle AddStage(PristonStage stage, IEnumerable<PristonSceneryInstance> models)
    {
        var stageDir = Path.GetDirectoryName(stage.Path) ?? ".";

        PristonPointLight[] stageLights, glowLights;
        if (_overrideLights != null)
        {
            stageLights = glowLights = _overrideLights;
            _log.Info($"[PristonRenderer] Field override: {stageLights.Length} hand-authored light(s).");
        }
        else
        {
            (stageLights, glowLights) = BuildStageLights(stage, _log);
        }

        // Keep override arrays immutable when applying per-stage glow tuning.
        glowLights = ApplyGlowTuning(glowLights, _glowIntensity, _glowScale);
        var handle = new RenderStageHandle(
            stage,
            _lampIntensity,
            _lampRangeScale,
            stageLights,
            glowLights,
            _glowIntensity,
            _glowScale);

        var shadowCollector = new StaticShadowCollector();
        BuildStageGeometry(stage, stageDir, handle, shadowCollector);
        var bakedLight = PristonStageBakedLight.For(stage);
        handle.LightField = PristonStageLightField.Build(stage);
        var animatedModels = new List<PreparedAnimatedInstance>();
        foreach (var instance in models)
        {
            var model = instance.Model;
            var modelDir = Path.GetDirectoryName(model.Path) ?? ".";
            if (model.IsAnimated)
            {
                animatedModels.Add(new PreparedAnimatedInstance(
                    model,
                    modelDir,
                    instance.World,
                    PrepareAnimatedModel(model, instance.World, bakedLight)));
            }
            else
            {
                AddModel(model, modelDir, handle, instance.World, shadowCollector, bakedLight);
            }
        }

        AddAnimatedModelGroups(animatedModels, handle);

        AddStaticShadowAggregate(handle, shadowCollector);

        LogAnimatedStageSummary(handle);
        AssignEmitterGlowColors(handle, stage);

        handle.Bounds = UnionBatchBounds(handle.Batches);
        _stages.Add(handle);
        RebuildRenderBatches();
        return handle;
    }

    public IEnumerator<RenderStageHandle> AddStageIncrementally(PristonStage stage, IReadOnlyList<PristonModel> models)
    {
        var (stageLights, glowLights) = _overrideLights is null ? BuildStageLights(stage, _log) : (_overrideLights, _overrideLights);
        glowLights = ApplyGlowTuning(glowLights, _glowIntensity, _glowScale);
        var handle = new RenderStageHandle(
            stage,
            _lampIntensity,
            _lampRangeScale,
            stageLights,
            glowLights,
            _glowIntensity,
            _glowScale);
        var published = false;
        var stats = new PristonStreamingStats(Path.GetFileName(stage.Path));
        using var steps = StageUploadSteps(stage, models, handle).GetEnumerator();
        try
        {
            while (true)
            {
                var started = Stopwatch.GetTimestamp();
                if (!steps.MoveNext())
                {
                    break;
                }

                stats.Record(steps.Current.Step, Stopwatch.GetElapsedTime(started).TotalMilliseconds, steps.Current.Bytes);
                yield return null;
            }

            var publishStarted = Stopwatch.GetTimestamp();
            LogAnimatedStageSummary(handle);
            AssignEmitterGlowColors(handle, stage);
            handle.Bounds = UnionBatchBounds(handle.Batches);
            _stages.Add(handle);
            RebuildRenderBatches();
            stats.Record(StreamingStep.Publish, Stopwatch.GetElapsedTime(publishStarted).TotalMilliseconds);
            LastStreamingStats = stats;
            _log.Info($"[Stream] upload steps {stats.Source}: {stats.Summary()}");
            published = true;
            yield return handle;
        }
        finally
        {
            if (!published)
            {
                handle.DisposeGpu();
            }
        }
    }

    // Each yielded item describes the main-thread work done just before it, so the caller can time every step.
    private IEnumerable<StreamingWork> StageUploadSteps(PristonStage stage, IReadOnlyList<PristonModel> models, RenderStageHandle handle)
    {
        var cpuBuilder = new PristonGpuMeshBuilder(null);
        var shadowCollector = new StaticShadowCollector();
        var bakedLight = PristonStageBakedLight.For(stage);
        var lightField = Task.Run(() => PristonStageLightField.Build(stage));
        var animatedPreparation = Task.Run(() =>
        {
            // By reference: a cached model listed twice is prepared once, and the record is not hashed by value.
            var prepared = new Dictionary<PristonModel, PreparedAnimatedModel>(ReferenceEqualityComparer.Instance);
            foreach (var model in models)
            {
                if (model.IsAnimated && !prepared.ContainsKey(model))
                {
                    prepared.Add(model, PrepareAnimatedModel(model, Matrix.Identity, bakedLight));
                }
            }

            return prepared;
        });
        var stageDirectory = Path.GetDirectoryName(stage.Path) ?? ".";
        foreach (var bytes in _textureLoader.UploadPreparedTextures(stageDirectory, PristonMaterialUsage.ForStage(stage)))
        {
            yield return new StreamingWork(StreamingStep.TextureUpload, bytes);
        }

        foreach (var model in models)
        {
            var directory = Path.GetDirectoryName(model.Path) ?? ".";
            foreach (var bytes in _textureLoader.UploadPreparedTextures(directory, PristonMaterialUsage.ForModel(model)))
            {
                yield return new StreamingWork(StreamingStep.TextureUpload, bytes);
            }
        }

        var groupsTask = Task.Run(() => BuildStageGroups(stage));
        while (!groupsTask.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        foreach (var step in AddGroupsIncrementally(
            CompletedResult(groupsTask),
            stage.Materials,
            stageDirectory,
            handle,
            cpuBuilder,
            shadowCollector))
        {
            yield return step;
        }

        var animatedModels = new List<PreparedAnimatedInstance>();
        foreach (var model in models)
        {
            var directory = Path.GetDirectoryName(model.Path) ?? ".";
            if (model.IsAnimated)
            {
                while (!animatedPreparation.IsCompleted)
                {
                    yield return StreamingWork.Wait;
                }

                var prepared = CompletedResult(animatedPreparation)[model];
                animatedModels.Add(new PreparedAnimatedInstance(
                    model,
                    directory,
                    Matrix.Identity,
                    prepared));
            }
            else
            {
                var modelTask = Task.Run(() => WithBakedLight(PristonModelGeometry.BuildGroups(model, Matrix.Identity, Color.White), model.Materials, bakedLight));
                while (!modelTask.IsCompleted)
                {
                    yield return StreamingWork.Wait;
                }

                foreach (var step in AddGroupsIncrementally(
                    CompletedResult(modelTask),
                    model.Materials,
                    directory,
                    handle,
                    cpuBuilder,
                    shadowCollector))
                {
                    yield return step;
                }
            }
        }

        foreach (var step in AddAnimatedModelGroupsIncrementally(animatedModels, handle))
        {
            yield return step;
        }

        while (!lightField.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        handle.LightField = CompletedResult(lightField);
        foreach (var step in AddStaticShadowAggregateIncrementally(handle, shadowCollector, cpuBuilder))
        {
            yield return step;
        }
    }

    private static BoundingBox UnionBatchBounds(IReadOnlyList<RenderBatch> batches)
    {
        if (batches.Count == 0)
        {
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        var bounds = batches[0].Bounds;
        for (var index = 1; index < batches.Count; index++)
        {
            bounds = BoundingBox.CreateMerged(bounds, batches[index].Bounds);
        }

        return bounds;
    }

    private static T CompletedResult<T>(Task<T> task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Stage preparation failed.", task.Exception);
        }

        return task.Result;
    }

    private void LogAnimatedStageSummary(RenderStageHandle handle)
    {
        var boneCount = 0;
        var batchCount = 0;
        var shadowAggregateCount = 0;
        foreach (var group in handle.AnimatedGroups)
        {
            boneCount += group.Palette.Count;
            batchCount += group.ColorBatchCount;
            shadowAggregateCount += group.ShadowAggregateCount;
        }

        if (handle.AnimatedModels.Count > 0)
        {
            _log.Info(
                $"[Rendering] stage animation: models={handle.AnimatedModels.Count}; " +
                $"bones={boneCount}; batches={batchCount}; shadow aggregates={shadowAggregateCount}.");
        }
    }

    private IEnumerable<StreamingWork> AddGroupsIncrementally(
        Dictionary<int, List<PristonCornerVertex>> groups,
        IReadOnlyList<PristonMaterial> materials,
        string directory,
        RenderStageHandle handle,
        PristonGpuMeshBuilder cpuBuilder,
        StaticShadowCollector shadowCollector)
    {
        var merged = new Dictionary<StaticMergeKey, (PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
        var pending = new List<(PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
        foreach (var (index, vertices) in groups)
        {
            var material = index >= 0 && index < materials.Count ? materials[index] : FallbackMaterial;
            if (vertices.Count == 0 || material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var runtime = _materialFactory.Create(directory, material);
            if (vertices.Count == 0 || runtime.Invisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var shadowCoveredByAggregate = CanAggregateShadow(runtime);
            if (shadowCoveredByAggregate)
            {
                shadowCollector.Add(runtime, vertices);
            }

            if (CanMerge(runtime))
            {
                var key = new StaticMergeKey(runtime.Textures[0], runtime.AlphaTest, runtime.TwoSided, runtime.SelfIllum, runtime.BlendState);
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = (runtime, []);
                    merged.Add(key, entry);
                }

                entry.Vertices.AddRange(vertices);
            }
            else
            {
                pending.Add((runtime, vertices));
            }

            yield return new StreamingWork(StreamingStep.MaterialSetup, 0);
        }

        pending.AddRange(merged.Values);
        var preparation = Task.Run(() =>
        {
            var batches = new List<PreparedStaticBatch>(pending.Count);
            foreach (var (material, vertices) in pending)
            {
                var array = vertices.ToArray();
                var mesh = material.TextureFormState == 0 ? cpuBuilder.PrepareStaticMesh(array) : null;
                var dynamicVertices = mesh is null ? ToDynamicVertices(array) : [];
                batches.Add(new PreparedStaticBatch(material, mesh, dynamicVertices, CanAggregateShadow(material)));
            }

            return batches;
        });
        while (!preparation.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        foreach (var prepared in CompletedResult(preparation))
        {
            var uploaded = prepared.Mesh is null ? null : _meshBuilder.Upload(prepared.Mesh);
            handle.Batches.Add(new RenderBatch(
                prepared.Vertices,
                [],
                uploaded,
                prepared.Material,
                ShadowCoveredByAggregate: prepared.ShadowCoveredByAggregate));
            yield return new StreamingWork(StreamingStep.StaticMeshUpload, PristonGpuMeshBuilder.EstimateBytes(prepared.Mesh));
        }
    }

    private static bool CanMerge(PristonMaterialRuntime material)
    {
        return material.TextureFormState == 0
            && !material.Transparent
            && !material.Water
            && !material.Blinking
            && !material.Wind
            && !material.HasLayers
            && material.Textures.Length == 1;
    }

    private static bool CanAggregateShadow(PristonMaterialRuntime material)
    {
        return material.CastsShadow
            && !material.AlphaTest
            && !material.Water
            && !material.Wind;
    }

    private void AddStaticShadowAggregate(RenderStageHandle handle, StaticShadowCollector collector)
    {
        if (collector.Count == 0)
        {
            return;
        }

        foreach (var cell in collector.OrderedCells())
        {
            var mesh = _meshBuilder.CreateStaticMesh(cell.ToArray());
            handle.ShadowBatches.Add(new RenderBatch([], [], mesh, collector.Material, ForceCastsShadow: true));
        }
    }

    private IEnumerable<StreamingWork> AddStaticShadowAggregateIncrementally(
        RenderStageHandle handle,
        StaticShadowCollector collector,
        PristonGpuMeshBuilder cpuBuilder)
    {
        if (collector.Count == 0)
        {
            yield break;
        }

        var cells = collector.OrderedCells().Select(static cell => cell.ToArray()).ToArray();
        var preparation = Task.Run(() =>
        {
            var prepared = new PristonGpuMeshBuilder.PreparedMesh[cells.Length];
            for (var index = 0; index < cells.Length; index++)
            {
                prepared[index] = cpuBuilder.PrepareStaticMesh(cells[index]);
            }

            return prepared;
        });
        while (!preparation.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        foreach (var prepared in CompletedResult(preparation))
        {
            var mesh = _meshBuilder.Upload(prepared);
            handle.ShadowBatches.Add(new RenderBatch([], [], mesh, collector.Material, ForceCastsShadow: true));
            yield return new StreamingWork(StreamingStep.ShadowAggregateUpload, PristonGpuMeshBuilder.EstimateBytes(prepared));
        }
    }

    public void RemoveStage(RenderStageHandle handle)
    {
        if (handle is null || !_stages.Remove(handle))
            return;
        handle.DisposeGpu();
        RebuildRenderBatches();
    }

    private void RemoveAllStages()
    {
        foreach (var stage in _stages)
            stage.DisposeGpu();
        _stages.Clear();
    }

    // Stage residency changes the static casters, so the sun shadow map must be redrawn.
    private void RebuildRenderBatches()
    {
        RebuildBatchLists();
        StageBounds = UnionStageBounds();
        _sunShadowRefresh.Invalidate();
    }

    // Actor spawns and despawns only mark the lists dirty: bursts coalesce into one rebuild before the next draw,
    // and actors reach the shadow maps on their next scheduled refresh instead of forcing one per event.
    private void EnsureBatchLists()
    {
        if (_batchListsDirty)
        {
            RebuildBatchLists();
        }
    }

    private void RebuildBatchLists()
    {
        _renderBatches.Clear();
        _shadowBatches.Clear();
        foreach (var stage in _stages)
        {
            _renderBatches.AddRange(stage.Batches);
            _shadowBatches.AddRange(stage.ShadowBatches);
        }

        foreach (var actor in _actors)
        {
            if (actor.Alive && actor.Instance.VisibleInMainPass && !actor.Hidden)
            {
                _renderBatches.AddRange(actor.Batches);
                _shadowBatches.AddRange(actor.ShadowBatches);
            }
        }

        Bounds = UnionBatchBounds(_renderBatches);
        _batchListsDirty = false;
        BatchListRebuilds++;
    }

    private BoundingBox UnionStageBounds()
    {
        if (_stages.Count == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        var box = _stages[0].Source.Bounds;
        for (var i = 1; i < _stages.Count; i++)
            box = BoundingBox.CreateMerged(box, _stages[i].Source.Bounds);
        return box;
    }
}
