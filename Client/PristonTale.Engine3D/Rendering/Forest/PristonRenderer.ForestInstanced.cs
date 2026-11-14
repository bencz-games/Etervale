using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// M1.5.2: optional, experiment-only rigid-glTF GPU instancing path. The normal
// actor renderer is unchanged unless ETV_FOREST_RENDERER=INSTANCED. Hidden actor
// templates own precisely the same materials/palettes/GPU meshes as M1.4.4.
public sealed partial class PristonRenderer
{
    private readonly Dictionary<ActorHandle, ForestGroup> _m152ForestGroups = new();
    private readonly BoundingFrustum _m152ForestFrustum = new(Matrix.Identity);
    public int InstancedForestVisibleNear { get; private set; }
    public int InstancedForestVisibleFar { get; private set; }
    public int InstancedForestDrawCalls { get; private set; }
    public int InstancedForestShadowCalls { get; private set; }

    private sealed class ForestGroup : IDisposable
    {
        public readonly ActorHandle Near;
        public readonly ActorHandle Far;
        public readonly bool NearCastsShadow;
        public readonly bool FarCastsShadow;
        public readonly PristonForestInstanceStream NearVisible;
        public readonly PristonForestInstanceStream FarVisible;
        public readonly PristonForestInstanceStream NearShadows;
        public readonly PristonForestInstanceStream FarShadows;
        public Matrix[] NearWorlds = Array.Empty<Matrix>();
        public Matrix[] FarWorlds = Array.Empty<Matrix>();
        public Matrix[] NearVisibleWorlds = Array.Empty<Matrix>();
        public Matrix[] FarVisibleWorlds = Array.Empty<Matrix>();
        public int NearCount;
        public int FarCount;
        public int VisibleNear;
        public int VisibleFar;
        public bool Disposed;

        public ForestGroup(GraphicsDevice device, ActorHandle near, ActorHandle far, bool nearShadow, bool farShadow)
        {
            Near = near;
            Far = far;
            NearCastsShadow = nearShadow;
            FarCastsShadow = farShadow;
            NearVisible = new PristonForestInstanceStream(device);
            FarVisible = new PristonForestInstanceStream(device);
            NearShadows = new PristonForestInstanceStream(device);
            FarShadows = new PristonForestInstanceStream(device);
        }

        public void Update(Matrix[] near, int nearCount, Matrix[] far, int farCount)
        {
            ArgumentNullException.ThrowIfNull(near);
            ArgumentNullException.ThrowIfNull(far);
            if (nearCount < 0 || nearCount > near.Length || farCount < 0 || farCount > far.Length)
                throw new ArgumentOutOfRangeException(nameof(nearCount), "Invalid forest LOD instance counts");
            if (NearWorlds.Length < nearCount)
                NearWorlds = new Matrix[Math.Max(nearCount, 16)];
            if (FarWorlds.Length < farCount)
                FarWorlds = new Matrix[Math.Max(farCount, 32)];
            Array.Copy(near, NearWorlds, nearCount);
            Array.Copy(far, FarWorlds, farCount);
            NearCount = nearCount;
            FarCount = farCount;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            NearVisible.Dispose();
            FarVisible.Dispose();
            NearShadows.Dispose();
            FarShadows.Dispose();
        }
    }

    public void RegisterInstancedForest(ActorHandle nearTemplate, ActorHandle farTemplate, bool nearShadows, bool farShadows)
    {
        if (_m152ForestGroups.ContainsKey(nearTemplate) ||
            !TryResolveActor(nearTemplate, out var near) || !TryResolveActor(farTemplate, out var far) ||
            near.Instance.Actor.Gltf is null || far.Instance.Actor.Gltf is null)
            throw new InvalidOperationException("Invalid or duplicate rigid glTF forest template");
        if (near.Instance.Actor.Gltf.Model.Skins.Count != 0 ||
            far.Instance.Actor.Gltf.Model.Skins.Count != 0 ||
            near.Instance.Actor.Gltf.Model.Animations.Count != 0 ||
            far.Instance.Actor.Gltf.Model.Animations.Count != 0)
            throw new InvalidOperationException("Instanced forest requires rigid, unanimated GLBs");
        _m152ForestGroups.Add(nearTemplate, new ForestGroup(_graphicsDevice, nearTemplate, farTemplate, nearShadows, farShadows));
    }

    public void SetInstancedForest(ActorHandle nearTemplate, Matrix[] near, int nearCount, Matrix[] far, int farCount)
    {
        if (!_m152ForestGroups.TryGetValue(nearTemplate, out var group))
            throw new InvalidOperationException("Instanced forest group was not registered");
        group.Update(near, nearCount, far, farCount);
    }

    public void RemoveInstancedForest(ActorHandle nearTemplate)
    {
        if (_m152ForestGroups.Remove(nearTemplate, out var group))
            group.Dispose();
    }

    private void DisposeInstancedForestGroups()
    {
        foreach (var group in _m152ForestGroups.Values)
            group.Dispose();
        _m152ForestGroups.Clear();
    }

    // Frustum culling is per tree, not per GLB batch. Upload a compact visible
    // transform stream once per frame; shadows retain the NEAR_ONLY group.
    private void PrepareInstancedForest(Matrix viewProjection)
    {
        InstancedForestVisibleNear = 0;
        InstancedForestVisibleFar = 0;
        InstancedForestDrawCalls = 0;
        InstancedForestShadowCalls = 0;
        if (_m152ForestGroups.Count == 0) return;
        _m152ForestFrustum.Matrix = viewProjection;
        foreach (var group in _m152ForestGroups.Values)
        {
            if (!TryResolveActor(group.Near, out var near) || !TryResolveActor(group.Far, out var far))
                continue;
            var nearBounds = near.Instance.Actor.Mesh.Bounds;
            var farBounds = far.Instance.Actor.Mesh.Bounds;
            if (group.NearVisibleWorlds.Length < group.NearCount)
                group.NearVisibleWorlds = new Matrix[group.NearCount];
            if (group.FarVisibleWorlds.Length < group.FarCount)
                group.FarVisibleWorlds = new Matrix[group.FarCount];
            var visibleNear = group.NearVisibleWorlds;
            var visibleFar = group.FarVisibleWorlds;
            var n = 0;
            var f = 0;
            for (var i = 0; i < group.NearCount; i++)
            {
                var world = group.NearWorlds[i];
                if (_m152ForestFrustum.Intersects(TransformForestBounds(nearBounds, world)))
                    visibleNear[n++] = world;
            }
            for (var i = 0; i < group.FarCount; i++)
            {
                var world = group.FarWorlds[i];
                if (_m152ForestFrustum.Intersects(TransformForestBounds(farBounds, world)))
                    visibleFar[f++] = world;
            }
            group.VisibleNear = n;
            group.VisibleFar = f;
            InstancedForestVisibleNear += n;
            InstancedForestVisibleFar += f;
            group.NearVisible.Upload(visibleNear.AsSpan(0, n));
            group.FarVisible.Upload(visibleFar.AsSpan(0, f));
            group.NearShadows.Upload(group.NearCastsShadow ? group.NearWorlds.AsSpan(0, group.NearCount) : ReadOnlySpan<Matrix>.Empty);
            group.FarShadows.Upload(group.FarCastsShadow ? group.FarWorlds.AsSpan(0, group.FarCount) : ReadOnlySpan<Matrix>.Empty);
        }
    }

    private static BoundingBox TransformForestBounds(BoundingBox source, Matrix world)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var corner = 0; corner != 8; corner++)
        {
            var point = new Vector3(
                (corner & 1) == 0 ? source.Min.X : source.Max.X,
                (corner & 2) == 0 ? source.Min.Y : source.Max.Y,
                (corner & 4) == 0 ? source.Min.Z : source.Max.Z);
            var transformed = Vector3.Transform(point, world);
            min = Vector3.Min(min, transformed);
            max = Vector3.Max(max, transformed);
        }
        return new BoundingBox(min, max);
    }

    // pass: 0 opaque, 1 masked, 2 transparent, 3 depth prepass.
    private void DrawInstancedForestSurface(int pass, ref int drawCalls, ref int triangles)
    {
        if (_m152ForestGroups.Count == 0) return;
        foreach (var group in _m152ForestGroups.Values)
        {
            DrawForestTemplate(group.Near, group.NearVisible, pass, ref drawCalls, ref triangles);
            DrawForestTemplate(group.Far, group.FarVisible, pass, ref drawCalls, ref triangles);
        }
    }

    private void DrawForestTemplate(ActorHandle template, PristonForestInstanceStream stream, int pass,
        ref int drawCalls, ref int triangles)
    {
        if (stream.Count == 0 || !TryResolveActor(template, out var runtime)) return;
        foreach (var batch in runtime.Batches)
        {
            if (batch.Gltf is not { } material) continue;
            var transparent = batch.Material.Transparent;
            var masked = batch.Material.AlphaTest && !transparent;
            if (pass == 0 && (masked || transparent) ||
                pass == 1 && !masked ||
                pass == 2 && !transparent ||
                pass == 3 && !(batch.Material.WritesDepth || masked))
                continue;
            _stateCache.SetRasterizerState(batch.Material.TwoSided ? PristonRasterizerStates.TwoSided : PristonRasterizerStates.SingleSided);
            if (pass == 3)
            {
                _stateCache.SetBlendState(PrepassBlend);
                _stateCache.SetDepthStencilState(PristonDepth.Write);
            }
            else if (_prepassDrawn && pass != 2 && (batch.Material.WritesDepth || masked))
            {
                _stateCache.SetBlendState(batch.Material.BlendState);
                _stateCache.SetDepthStencilState(PristonDepth.Equal);
            }
            else
            {
                var depth = batch.Material.WritesDepth;
                _stateCache.SetBlendState(TemporalBlend(batch.Material.BlendState, depth));
                _stateCache.SetDepthStencilState(depth ? PristonDepth.Write : PristonDepth.Read);
            }
            var effect = _shaders.SelectForestSurface(material, batch.SkinPalette, pass == 3);
            // Stream.Draw installs both vertex streams with frequency 1 for
            // matrices, then binds the derived ForestInstanced effect.
            if (stream.Draw(batch.SkinnedBuffer, batch.SkinnedIndexBuffer, effect.CurrentTechnique.Passes[0], batch.SkinnedPrimitiveCount))
            {
                if (pass != 3)
                {
                    drawCalls++;
                    InstancedForestDrawCalls++;
                    triangles += stream.Count * batch.SkinnedPrimitiveCount;
                }
                _stateCache.InvalidateDeviceState();
            }
        }
    }

    // Called inside the existing sun-cascade render pass. The hidden actor
    // templates are absent from the ordinary shadow queue, avoiding duplicates.
    private void DrawInstancedForestSunShadows()
    {
        if (_m152ForestGroups.Count == 0) return;
        _graphicsDevice.RasterizerState = PristonRasterizerStates.TwoSided;
        foreach (var group in _m152ForestGroups.Values)
        {
            DrawForestShadowTemplate(group.Near, group.NearShadows);
            DrawForestShadowTemplate(group.Far, group.FarShadows);
        }
    }

    private void DrawForestShadowTemplate(ActorHandle template, PristonForestInstanceStream stream)
    {
        if (stream.Count == 0 || !TryResolveActor(template, out var runtime)) return;
        foreach (var batch in runtime.Batches)
        {
            if (batch.Gltf is not { } material) continue;
            var effect = _shaders.SelectForestShadow(material, batch.SkinPalette);
            if (stream.Draw(batch.SkinnedBuffer, batch.SkinnedIndexBuffer, effect.CurrentTechnique.Passes[0], batch.SkinnedPrimitiveCount))
            {
                _shadowDrawCalls++;
                InstancedForestShadowCalls++;
            }
        }
    }
}
