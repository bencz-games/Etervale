using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

internal sealed class RenderQueueBuilder
{
    private readonly PristonRenderQueue<RenderBatch> _queue = new();
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);

    public PristonRenderQueue<RenderBatch> Queue => _queue;

    // A visible batch that draws into shadow maps on its own (not through a static aggregate).
    public static bool CastsShadow(RenderBatch batch) =>
        !batch.ShadowCoveredByAggregate
        && batch.Shade is null or { Shadowed: true }
        && (batch.Material.CastsShadow || batch.ForceCastsShadow);

    public int Build(
        IReadOnlyList<RenderBatch> batches,
        IReadOnlyList<RenderBatch> shadowBatches,
        PristonFrameContext frame,
        bool includeShadowCasters = true)
    {
        _queue.Clear();
        _frustum.Matrix = frame.View * frame.Projection;
        var culled = 0;
        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            if (batch.Shade is { Hidden: true })
            {
                culled++;
                continue;
            }

            var sortKey = batch.Material.SortKey;
            var sortDepth = SortDepth(frame.CameraPosition, batch.Bounds);
            if (includeShadowCasters && CastsShadow(batch))
            {
                _queue.AddShadowCaster(batch, sortKey, sortDepth);
            }

            if (!_frustum.Intersects(batch.Bounds))
            {
                culled++;
                continue;
            }

            var pass = RenderPass(batch);
            _queue.Add(batch, pass, sortKey, sortDepth);
        }

        if (includeShadowCasters)
        {
            for (var i = 0; i < shadowBatches.Count; i++)
            {
                var batch = shadowBatches[i];
                if (batch.Shade is { Shadowed: false })
                {
                    continue;
                }

                _queue.AddShadowCaster(
                    batch,
                    batch.Material.SortKey,
                    SortDepth(frame.CameraPosition, batch.Bounds));
            }
        }

        _queue.Sort(includeShadowCasters);
        return culled;
    }

    private static PristonRenderPass RenderPass(RenderBatch batch)
    {
        if (batch.Material.Transparent || batch.Shade is { Translucent: true })
            return PristonRenderPass.Transparent;

        return batch.Material.AlphaTest ? PristonRenderPass.AlphaTest : PristonRenderPass.Opaque;
    }

    private static float SortDepth(Vector3 cameraPosition, BoundingBox bounds)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        return Vector3.DistanceSquared(cameraPosition, center);
    }

}
