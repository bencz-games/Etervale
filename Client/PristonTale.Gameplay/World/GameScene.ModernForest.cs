using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World;

public sealed partial class GameScene
{
    // Different actor models for near and far geometry. All 99 placements are
    // preserved; one GPU upload per frame. These are actor LODs, NOT GPU instancing.
    private static readonly int ModernForestNearBudget = ReadForestInt("ETV_FOREST_NEAR_COUNT", 8, 0, 16);
    private static readonly int ModernForestFarBudget = ReadForestInt("ETV_FOREST_FAR_COUNT", 36, 0, 70);
    private static readonly float ModernForestNearRadius = ReadForestInt("ETV_FOREST_NEAR_RADIUS", 950, 450, 1600);
    private static readonly float ModernForestFarRadius = ReadForestInt("ETV_FOREST_FAR_RADIUS", 3100, 1300, 5200);
    private const int ModernForestUploadPerFrame = 1;
    // Opt-in experiment: normal/production path is the existing actor renderer.
    private static readonly bool ModernForestUseInstancing =
        Environment.GetEnvironmentVariable("ETV_FOREST_RENDERER") == "INSTANCED";
    // Opt-in measurements. The normal game does no string formatting or diagnostics work.
    private static readonly bool ModernForestDiagnosticsEnabled =
        Environment.GetEnvironmentVariable("ETV_FOREST_DIAG") == "1";
    private long _modernForestDiagnosticsLastTick;
    private int _modernForestDiagnosticsUpdates;

    // M1.4.4 opt-in tree-only shadow comparison: ALL (default), NEAR_ONLY, NONE.
    private static readonly string ModernForestShadowPolicy = ReadForestShadowPolicy();
    private static string ReadForestShadowPolicy()
    {
        var policy = Environment.GetEnvironmentVariable("ETV_FOREST_SHADOW_POLICY")?.Trim().ToUpperInvariant();
        return policy is "NONE" or "NEAR_ONLY" ? policy : "ALL";
    }

    private static int ReadForestInt(string key, int fallback, int min, int max)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(key), out var parsed)
            ? Math.Clamp(parsed, min, max) : fallback;
    }

    private readonly struct ForestDistance : IComparable<ForestDistance>
    {
        public ForestDistance(int index, float squared)
        {
            Index = index;
            Squared = squared;
        }
        public int Index { get; }
        public float Squared { get; }
        public int CompareTo(ForestDistance other)
        {
            var order = Squared.CompareTo(other.Squared);
            return order == 0 ? Index.CompareTo(other.Index) : order;
        }
    }

    private sealed class ModernForestResidency
    {
        public ModernForestResidency(PristonActorInstance[] far)
        {
            FarInstances = far;
            InstancedNear = new Matrix[far.Length];
            InstancedFar = new Matrix[far.Length];
            Handles = new ActorHandle[far.Length];
            Array.Fill(Handles, ActorHandle.None);
            Tiers = new byte[far.Length];
            Desired = new byte[far.Length];
            Order = new ForestDistance[far.Length];
        }
        public PristonActorInstance[] FarInstances { get; }
        public ActorHandle NearTemplate { get; set; } = ActorHandle.None;
        public ActorHandle FarTemplate { get; set; } = ActorHandle.None;
        public Matrix[] InstancedNear { get; }
        public Matrix[] InstancedFar { get; }
        public ActorHandle[] Handles { get; }
        public byte[] Tiers { get; }
        public byte[] Desired { get; }
        public ForestDistance[] Order { get; }
        public bool Failed { get; set; }
    }

    private readonly Dictionary<LoadedField, ModernForestResidency> _modernForestResidents = new();

    private void QueueModernForest(LoadedField field)
    {
        if (field.ModernScenery.Length == 0 || _modernForestResidents.ContainsKey(field))
        {
            return;
        }
        if (ModernForestNearBudget == 0 && ModernForestFarBudget == 0)
        {
            // Diagnostic baseline: never publish a modern tree actor to the renderer.
            // The overlay may still have parsed the near GLB on the CPU during field preparation.
            _log.Info("[ETV] M1.4.3 zero forest: near <= 0; far <= 0; tree GPU actors = 0.");
            return;
        }
        try
        {
            // Overlay.Prepare checked both model SHA-256 values against the manifest.
            var lowActor = ModernForestOverlay.LoadFar(_assetRoot);
            var near = field.ModernScenery;
            var far = new PristonActorInstance[near.Length];
            for (var i = 0; i < near.Length; i++)
            {
                far[i] = new PristonActorInstance(lowActor, near[i].World, MotionCode.Stand);
            }
            var state = new ModernForestResidency(far);
            if (ModernForestUseInstancing)
            {
                // Two hidden actor templates own glTF GPU mesh/material/palette resources.
                // Their geometry is NOT submitted through the ordinary actor render queue.
                var nearTemplate = ActorHandle.None;
                var farTemplate = ActorHandle.None;
                try
                {
                    nearTemplate = _renderer.AddActor(new PristonActorInstance(near[0].Actor, Matrix.Identity, MotionCode.Stand));
                    farTemplate = _renderer.AddActor(new PristonActorInstance(lowActor, Matrix.Identity, MotionCode.Stand));
                    _renderer.SetActorHidden(nearTemplate, true);
                    _renderer.SetActorHidden(farTemplate, true);
                    var castNear = ModernForestShadowPolicy != "NONE";
                    var castFar = ModernForestShadowPolicy == "ALL";
                    _renderer.RegisterInstancedForest(nearTemplate, farTemplate, castNear, castFar);
                    state.NearTemplate = nearTemplate;
                    state.FarTemplate = farTemplate;
                }
                catch
                {
                    if (farTemplate.IsValid) _renderer.RemoveActor(farTemplate);
                    if (nearTemplate.IsValid) _renderer.RemoveActor(nearTemplate);
                    throw;
                }
            }
            _modernForestResidents.Add(field, state);
            _log.Info($"[ETV] M1.4 dual LOD: {near.Length} placements; " +
                $"near <= {ModernForestNearBudget} at {ModernForestNearRadius} units; " +
                $"far <= {ModernForestFarBudget} at {ModernForestFarRadius} units; " +
                $"upload <= {ModernForestUploadPerFrame}/frame; tree_shadows={ModernForestShadowPolicy}; " +
                $"forest_renderer={(ModernForestUseInstancing ? "INSTANCED" : "ACTORS")}.");
        }
        catch (Exception error)
        {
            _log.Error($"[ETV] M1.4 could not prepare the two tree LODs: {error}");
            throw;
        }
    }

    private void UpdateModernForest()
    {
        if (_playerBody is null || _renderer is null) return;
        var px = _playerBody.Position.X;
        var pz = _playerBody.Position.Z;
        var uploadsLeft = ModernForestUploadPerFrame;
        foreach (var (field, state) in _modernForestResidents)
        {
            if (state.Failed) continue;
            var trees = field.ModernScenery;
            var nearCount = 0;
            var farCount = 0;
            var nearSquared = ModernForestNearRadius * ModernForestNearRadius;
            var farSquared = ModernForestFarRadius * ModernForestFarRadius;
            var nearHysteresisSquared = (ModernForestNearRadius + 140f) * (ModernForestNearRadius + 140f);
            var farHysteresisSquared = (ModernForestFarRadius + 450f) * (ModernForestFarRadius + 450f);

            for (var i = 0; i < trees.Length; i++)
            {
                var position = trees[i].World.Translation;
                var dx = position.X - px;
                var dz = position.Z - pz;
                state.Order[i] = new ForestDistance(i, dx * dx + dz * dz);
            }
            Array.Sort(state.Order);
            Array.Fill(state.Desired, (byte)0);
            // Reserve the nearest places for detailed trees first.
            foreach (var candidate in state.Order)
            {
                var i = candidate.Index;
                var limit = state.Tiers[i] == 2 ? nearHysteresisSquared : nearSquared;
                if (nearCount < ModernForestNearBudget && candidate.Squared <= limit)
                {
                    state.Desired[i] = 2;
                    nearCount++;
                }
            }
            // Fill the surrounding landscape with cheaper, foliage-bearing trees.
            foreach (var candidate in state.Order)
            {
                var i = candidate.Index;
                if (state.Desired[i] != 0) continue;
                var limit = state.Tiers[i] == 1 ? farHysteresisSquared : farSquared;
                if (farCount < ModernForestFarBudget && candidate.Squared <= limit)
                {
                    state.Desired[i] = 1;
                    farCount++;
                }
            }
            if (ModernForestUseInstancing)
            {
                // Preserve the exact M1.4.4 distance selection and hysteresis.
                // Unlike actors, all visible transforms are uploaded in two batches.
                var nearInstances = 0;
                var farInstances = 0;
                for (var i = 0; i < trees.Length; i++)
                {
                    state.Tiers[i] = state.Desired[i];
                    if (state.Desired[i] == 2)
                        state.InstancedNear[nearInstances++] = trees[i].World;
                    else if (state.Desired[i] == 1)
                        state.InstancedFar[farInstances++] = trees[i].World;
                }
                _renderer.SetInstancedForest(state.NearTemplate,
                    state.InstancedNear, nearInstances, state.InstancedFar, farInstances);
                continue;
            }
            // Evict obsolete LODs, then publish at most one new actor this frame.
            for (var i = 0; i < trees.Length; i++)
            {
                if (state.Tiers[i] == state.Desired[i]) continue;
                if (state.Handles[i].IsValid)
                {
                    _renderer.RemoveActor(state.Handles[i]);
                    state.Handles[i] = ActorHandle.None;
                }
                state.Tiers[i] = 0;
            }
            if (uploadsLeft == 0) continue;
            try
            {
                for (var tier = 2; tier >= 1; tier--)
                {
                    foreach (var candidate in state.Order)
                    {
                        var i = candidate.Index;
                        if (state.Desired[i] != tier || state.Tiers[i] != 0) continue;
                        var actor = tier == 2 ? trees[i] : state.FarInstances[i];
                        state.Handles[i] = _renderer.AddActor(actor);
                        // Only the forest actors are affected; gameplay actors retain shadows.
                        _renderer.SetActorShadowCasting(state.Handles[i],
                            ModernForestShadowPolicy == "ALL" ||
                            (ModernForestShadowPolicy == "NEAR_ONLY" && tier == 2));
                        state.Tiers[i] = (byte)tier;
                        uploadsLeft--;
                        break;
                    }
                    if (uploadsLeft == 0) break;
                }
            }
            catch (Exception error)
            {
                state.Failed = true;
                _log.Error($"[ETV] M1.4 actor rendering failed in {field.Definition.Id}: {error}");
                for (var i = 0; i < state.Handles.Length; i++)
                {
                    if (!state.Handles[i].IsValid) continue;
                    _renderer.RemoveActor(state.Handles[i]);
                    state.Handles[i] = ActorHandle.None;
                    state.Tiers[i] = 0;
                }
            }
        }
        if (ModernForestDiagnosticsEnabled)
        {
            EmitModernForestDiagnostics();
        }
    }

    // These totals include the entire renderer (terrain, characters, UI-independent scene),
    // not only tree calls. Compare zero and dense at the same location to isolate differences.
    private void EmitModernForestDiagnostics()
    {
        _modernForestDiagnosticsUpdates++;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_modernForestDiagnosticsLastTick == 0)
        {
            _modernForestDiagnosticsLastTick = now;
            _modernForestDiagnosticsUpdates = 0;
            _renderer.Profiler.ResetAverages();
            return;
        }
        var elapsed = (double)(now - _modernForestDiagnosticsLastTick) / System.Diagnostics.Stopwatch.Frequency;
        if (elapsed < 3.0) return;

        var residentNear = 0;
        var residentFar = 0;
        var desiredNear = 0;
        var desiredFar = 0;
        foreach (var state in _modernForestResidents.Values)
        {
            foreach (var tier in state.Tiers)
            {
                if (tier == 2) residentNear++;
                else if (tier == 1) residentFar++;
            }
            foreach (var tier in state.Desired)
            {
                if (tier == 2) desiredNear++;
                else if (tier == 1) desiredFar++;
            }
        }

        var stats = _renderer.LastFrameStats;
        var profiler = _renderer.Profiler;
        var cpu = profiler.CpuMeans();
        var gpu = profiler.GpuMeans();
        var updatesHz = (_modernForestDiagnosticsUpdates / elapsed).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        var x = _playerBody.Position.X.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        var z = _playerBody.Position.Z.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        _log.Info("ETV_M143_SAMPLE "
            + $"near_budget={ModernForestNearBudget} far_budget={ModernForestFarBudget} "
            + $"resident_near={residentNear} resident_far={residentFar} "
            + $"desired_near={desiredNear} desired_far={desiredFar} "
            + $"update_hz={updatesHz} player_x={x} player_z={z} "
            + $"draw_calls={stats.DrawCalls} triangles={stats.Triangles} "
            + $"visible_batches={stats.VisibleBatches} culled_batches={stats.CulledBatches} "
            + $"shadow_calls={stats.ShadowDrawCalls} sun_shadow_updated={(stats.SunShadowUpdated ? 1 : 0)} "
            + $"lamp_shadow_updates={stats.LampShadowMapsUpdated} two_sided_batches={stats.TwoSidedBatches} "
            + $"cpu_frame_ms={ForestMetric(cpu, "Frame")} cpu_queue_ms={ForestMetric(cpu, "Queue")} "
            + $"cpu_shadow_ms={ForestMetric(cpu, "SunShadow")} cpu_lamp_ms={ForestMetric(cpu, "LampShadows")} "
            + $"cpu_masked_ms={ForestMetric(cpu, "Masked")} cpu_opaque_ms={ForestMetric(cpu, "Opaque")} "
            + $"cpu_prepass_ms={ForestMetric(cpu, "Prepass")} "
            + $"gpu_timers={(profiler.GpuTimersAvailable ? 1 : 0)} "
            + $"gpu_frame_ms={ForestMetric(gpu, "Frame")} gpu_shadow_ms={ForestMetric(gpu, "SunShadow")} "
            + $"gpu_lamp_ms={ForestMetric(gpu, "LampShadows")} "
            + $"gpu_masked_ms={ForestMetric(gpu, "Masked")} gpu_opaque_ms={ForestMetric(gpu, "Opaque")} "
            + $"gpu_prepass_ms={ForestMetric(gpu, "Prepass")} "
            + $"gpu_queries_missed={profiler.GpuResultsMissed} " +
            $"m152_mode={(ModernForestUseInstancing ? "INSTANCED" : "ACTORS")} " +
            $"m152_visible_near={_renderer.InstancedForestVisibleNear} " +
            $"m152_visible_far={_renderer.InstancedForestVisibleFar} " +
            $"m152_draw_calls={_renderer.InstancedForestDrawCalls} " +
            $"m152_shadow_calls={_renderer.InstancedForestShadowCalls}");

        profiler.ResetAverages();
        _modernForestDiagnosticsLastTick = now;
        _modernForestDiagnosticsUpdates = 0;
    }

    private static string ForestMetric(System.Collections.Generic.IReadOnlyDictionary<string, double> values, string key)
    {
        return values.TryGetValue(key, out var value)
            ? value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "na";
    }

    private void ReleaseModernForest(LoadedField field)
    {
        if (!_modernForestResidents.Remove(field, out var state)) return;
        if (state.NearTemplate.IsValid)
        {
            _renderer.RemoveInstancedForest(state.NearTemplate);
            if (state.FarTemplate.IsValid) _renderer.RemoveActor(state.FarTemplate);
            _renderer.RemoveActor(state.NearTemplate);
        }
        foreach (var handle in state.Handles)
        {
            if (handle.IsValid) _renderer.RemoveActor(handle);
        }
    }
}
