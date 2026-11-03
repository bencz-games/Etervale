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

// Drawing the queues: batches, the depth pre-pass, outlines and the temporal blend.
public sealed partial class PristonRenderer
{
    private void DrawQueue(
        PristonRenderQueue<RenderBatch> queue,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics,
        bool intoScene = true)
    {
        // The opaque passes also write the motion and depth of the temporal anti-aliasing; blended passes do not.
        if (intoScene && !_prepassDrawn)
        {
            _sceneTargets.BindTemporal();
            _temporalTargetsBound = _sceneTargets.Temporal;
        }

        if (intoScene && !_neutralActorPass && DebugView == PristonDebugView.Shaded)
        {
            DrawOutlines(queue.Opaque, ref drawCalls, ref triangles);
            DrawOutlines(queue.AlphaTest, ref drawCalls, ref triangles);
        }

        Profiler.Begin(RenderSection.Opaque);
        DrawQueueItems(
            queue.Opaque,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        Profiler.End(RenderSection.Opaque);
        Profiler.Begin(RenderSection.Masked);
        DrawQueueItems(
            queue.AlphaTest,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        Profiler.End(RenderSection.Masked);
        if (_temporalTargetsBound)
        {
            _sceneTargets.Bind();
            _stateCache.InvalidateDeviceState();
            _temporalTargetsBound = false;
        }

        if (intoScene && _prepassDrawn && _screenReflections && _smoothSurfaceVisible)
        {
            Profiler.Begin(RenderSection.Reflections);
            var frame = _reflectionFrame;
            _reflections.Run(
                _sceneTargets.Color,
                _sceneTargets.LinearDepth,
                _sceneTargets.Normals,
                frame.View,
                frame.Projection,
                frame.Environment,
                _textureLoader.White,
                _environment.Sh,
                frame.Shadows.NoiseOffset,
                _sceneTargets.BindAction);
            Profiler.End(RenderSection.Reflections);
            _stateCache.InvalidateDeviceState();
        }

        _drawingTransparent = true;
        Profiler.Begin(RenderSection.Transparent);
        DrawQueueItems(
            queue.Transparent,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        Profiler.End(RenderSection.Transparent);
        _drawingTransparent = false;
    }

    private void DrawQueueItems(
        IReadOnlyList<PristonRenderQueueItem<RenderBatch>> items,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var batch = items[i].Batch;
            if (DebugView == PristonDebugView.Overdraw)
            {
                _stateCache.SetBlendState(BlendState.Additive);
                _stateCache.SetDepthStencilState(DepthStencilState.None);
            }
            else if (_prepassDrawn && !_drawingTransparent && Prepassed(batch))
            {
                _stateCache.SetBlendState(batch.Material.BlendState);
                _stateCache.SetDepthStencilState(PristonDepth.Equal);
            }
            else
            {
                // A translucent actor blends over the scene without writing depth.
                var translucent = batch.Shade is { Translucent: true };
                var writesDepth = batch.Material.WritesDepth && !translucent;
                var blend = translucent && ReferenceEquals(batch.Material.BlendState, BlendState.Opaque) ? BlendState.NonPremultiplied : batch.Material.BlendState;
                _stateCache.SetBlendState(TemporalBlend(blend, writesDepth));
                _stateCache.SetDepthStencilState(writesDepth ? PristonDepth.Write : PristonDepth.Read);
            }

            DrawBatch(
                batch,
                ref staticBatches,
                ref dynamicBatches,
                ref drawCalls,
                ref triangles,
                ref staticVertices,
                ref staticIndices,
                ref normalBatches,
                ref twoSidedBatches,
                ref visibleStaticMeshDiagnostics);
        }
    }

    // The original hover outline: the actor drawn flat in one colour, shifted on screen and pushed back in depth, before
    // the lit actor. The original shifts it 1.4 pixels on an 800x600 screen; here it is shifted that far, scaled with the
    // screen height, in four directions so the rim shows all around. It writes depth so what is drawn after it behind the
    // actor (or shaded again over a pre-pass) does not cover it; the lit actor, nearer, covers it inside the silhouette.
    private void DrawOutlines(IReadOnlyList<PristonRenderQueueItem<RenderBatch>> items, ref int drawCalls, ref int triangles)
    {
        var pixels = OutlinePixels * MathF.Max(1f, _sceneTargets.Height / 600f);
        var push = -_reflectionFrame.Projection.M43 * OutlineDepthPush;
        for (var i = 0; i < items.Count; i++)
        {
            var batch = items[i].Batch;
            if (!batch.IsSkinned || batch.Outline.W <= 0f || batch.Shade is { Translucent: true })
            {
                continue;
            }

            _stateCache.SetBlendState(TemporalBlend(BlendState.Opaque, false));
            _stateCache.SetDepthStencilState(PristonDepth.Write);
            _stateCache.SetRasterizerState(batch.Material.TwoSided ? PristonRasterizerStates.TwoSided : PristonRasterizerStates.SingleSided);
            var shift = pixels * batch.Outline.W;
            var color = new Vector4(batch.Outline.X, batch.Outline.Y, batch.Outline.Z, 1f);
            foreach (var (x, y) in OutlineDirections)
            {
                _shaders.SelectSkinnedSurface(batch.Material, batch.Material.SelectTexture(_renderTime), ComputeUvScrollOffset(batch.Material), batch.SkinPalette,
                    Vector3.Zero, LayerOffset(batch.Material), false, batch.FloorLight, batch.Shade, 0f, WobblePhase, batch.IsActor);
                var parameters = new Vector4(x * shift * 2f / _sceneTargets.Width, y * shift * 2f / _sceneTargets.Height, push, OutlineDepthPush);
                _shaders.SelectSkinnedOutline(batch.Material.AlphaTest, parameters, color).CurrentTechnique.Passes[0].Apply();
                _stateCache.SetVertexBuffer(batch.SkinnedBuffer);
                _stateCache.SetIndexBuffer(batch.SkinnedIndexBuffer);
                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.SkinnedPrimitiveCount);
                drawCalls++;
                triangles += batch.SkinnedPrimitiveCount;
            }
        }
    }

    // With the temporal targets bound, a surface that is blended or writes no depth leaves the motion and depth of what
    // is behind it: the same blend on the colour, no writes to the other targets.
    private BlendState TemporalBlend(BlendState blend, bool writesDepth)
    {
        if (!_temporalTargetsBound || (writesDepth && ReferenceEquals(blend, BlendState.Opaque)))
        {
            return blend;
        }

        if (_temporalBlendStates.TryGetValue(blend, out var temporal))
        {
            return temporal;
        }

        temporal = new BlendState
        {
            Name = blend.Name + " (colour only)",
            IndependentBlendEnable = true,
            BlendFactor = blend.BlendFactor
        };
        temporal[0].ColorSourceBlend = blend.ColorSourceBlend;
        temporal[0].ColorDestinationBlend = blend.ColorDestinationBlend;
        temporal[0].ColorBlendFunction = blend.ColorBlendFunction;
        temporal[0].AlphaSourceBlend = blend.AlphaSourceBlend;
        temporal[0].AlphaDestinationBlend = blend.AlphaDestinationBlend;
        temporal[0].AlphaBlendFunction = blend.AlphaBlendFunction;
        temporal[0].ColorWriteChannels = blend.ColorWriteChannels;
        for (var target = 1; target < 4; target++)
        {
            temporal[target].ColorWriteChannels = ColorWriteChannels.None;
        }

        _temporalBlendStates.Add(blend, temporal);
        return temporal;
    }

    // Surfaces of the opaque and alpha-tested queues that write their depth (also the original alpha-blended ground, whose
    // blending keeps its depth write) and every cut-out outside the blended queue (foliage, also the late-flagged
    // leaves: PristonMaterialRuntimeFactory.Classify; their depth, motion and occlusion come from the pre-pass's alpha
    // test, and the lit pass blends only the front-most leaf): the depth pre-pass draws them, and the lit pass shades
    // them at that depth with their own blend.
    private static bool Prepassed(RenderBatch batch) =>
        batch.Material.WritesDepth || (batch.Material.AlphaTest && !batch.Material.Transparent);

    private static BlendState CreatePrepassBlend()
    {
        var blend = new BlendState { Name = "PristonPrepass", IndependentBlendEnable = true };
        blend[0].ColorWriteChannels = ColorWriteChannels.None;
        return blend;
    }

    // The depth pre-pass over the opaque and alpha-tested queues (front to back as sorted).
    private void DrawPrepass(PristonRenderQueue<RenderBatch> queue)
    {
        _sceneTargets.BindPrepass();
        _stateCache.InvalidateDeviceState();
        _stateCache.SetBlendState(PrepassBlend);
        _stateCache.SetDepthStencilState(PristonDepth.Write);
        _prepassMode = true;
        _smoothSurfaceVisible = false;
        var unused = 0;
        var diagnostics = new PristonMeshDiagnostics();
        for (var pass = 0; pass < 2; pass++)
        {
            var items = pass == 0 ? queue.Opaque : queue.AlphaTest;
            for (var i = 0; i < items.Count; i++)
            {
                var batch = items[i].Batch;
                if (!Prepassed(batch))
                {
                    continue;
                }

                _smoothSurfaceVisible |= batch.Material.Description.Roughness < SmoothRoughness;
                DrawBatch(batch, ref unused, ref unused, ref unused, ref unused, ref unused, ref unused, ref unused, ref unused, ref diagnostics);
            }
        }

        _prepassMode = false;
    }

    private void DrawBatch(
        RenderBatch batch,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics,
        float opacity = 1f,
        bool sky = false)
    {
        if (batch.Material.TwoSided)
        {
            twoSidedBatches++;
        }

        // Back faces are culled unless the material is two-sided, as the original draws them: clothes and armour have inner
        // and back faces that, drawn too, show through and fight the front faces as the model moves.
        _stateCache.SetRasterizerState(batch.Material.TwoSided ? PristonRasterizerStates.TwoSided : PristonRasterizerStates.SingleSided);

        if (batch.IsSkinned)
        {
            var uvScrollOffset = ComputeUvScrollOffset(batch.Material);
            var texture = batch.Material.SelectTexture(_renderTime);
            var effect = _shaders.SelectSkinnedSurface(
                batch.Material,
                texture,
                uvScrollOffset,
                batch.SkinPalette,
                _neutralActorPass ? Vector3.Zero : batch.ActorColorAdd * SelectionRimScale,
                LayerOffset(batch.Material),
                _prepassMode,
                _neutralActorPass ? 1f : batch.FloorLight,
                _neutralActorPass ? null : batch.Shade,
                batch.Shade is { Overlay: not null } overlaid ? TextureUOffset(overlaid.OverlayForm) : 0f,
                WobblePhase,
                batch.IsActor);
            effect.CurrentTechnique.Passes[0].Apply();
            _stateCache.SetVertexBuffer(batch.SkinnedBuffer);
            _stateCache.SetIndexBuffer(batch.SkinnedIndexBuffer);
            _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.SkinnedPrimitiveCount);
            staticBatches++;
            staticVertices += batch.SkinnedVertexCount;
            staticIndices += batch.SkinnedIndexCount;
            normalBatches++;

            drawCalls++;
            triangles += batch.SkinnedPrimitiveCount;
            return;
        }

        // Static / UV-scroll dynamic path.
        var uvScrollOffsetStatic = ComputeUvScrollOffset(batch.Material);
        if (batch.Mesh == null)
        {
            _batchUploader.Ensure(batch);
        }

        var textureStatic = batch.Material.SelectTexture(_renderTime);
        var effectStatic = sky
            ? _shaders.SelectSky(batch.Material, textureStatic, uvScrollOffsetStatic, opacity)
            : _shaders.SelectStaticSurface(
                batch.Material,
                textureStatic,
                uvScrollOffsetStatic,
                ComputeWindParams(batch),
                LayerOffset(batch.Material),
                opacity,
                _prepassMode);

        effectStatic.CurrentTechnique.Passes[0].Apply();
        if (batch.Mesh != null)
        {
            _stateCache.SetVertexBuffer(batch.Mesh.VertexBuffer);
            _stateCache.SetIndexBuffer(batch.Mesh.IndexBuffer);
            _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.Mesh.PrimitiveCount);
            staticBatches++;
            staticVertices += batch.Mesh.VertexCount;
            staticIndices += batch.Mesh.IndexCount;
            visibleStaticMeshDiagnostics += batch.Mesh.Diagnostics;
            if (batch.Mesh.HasNormals)
            {
                normalBatches++;
            }
        }
        else
        {
            _stateCache.SetVertexBuffer(batch.DynamicBuffer);
            _graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, batch.Vertices.Length / 3);
            dynamicBatches++;
            normalBatches++;
        }

        drawCalls++;
        triangles += batch.TriangleCount;
    }

    // Color and depth passes must share this wind anchor to keep shadows aligned.
    private static Vector4 ComputeWindParams(RenderBatch batch)
    {
        if (!batch.Material.Wind)
            return Vector4.Zero;
        var minY = batch.Bounds.Min.Y;
        var invHeight = 1f / MathF.Max(batch.Bounds.Max.Y - minY, 1f);
        return new Vector4(batch.Material.WindAmplitudeX, batch.Material.WindAmplitudeZ, minY, invHeight);
    }

    private Vector2 ComputeUvScrollOffset(PristonMaterialRuntime material)
    {
        // An authored texture form-state scroll code always wins: the authored water and sea surfaces are
        // animated by it (the "slow scroll" codes are deliberately smooth). Decide by the code's presence, not
        // by the computed offset, so the per-wrap zero-crossing never falls through to the water branch and
        // causes a one-frame jump.
        if (material.TextureFormState != 0)
        {
            var uOffset = TextureUOffset(material.TextureFormState);
            return uOffset == 0f ? Vector2.Zero : new Vector2(uOffset, 0f);
        }

        // Water without a form-state code: a gentle diagonal drift, quantized finely (10 bits) so it reads as
        // smooth rather than stepped. An Etervale choice: the authored water uses the form-state codes.
        if (material.Water)
            return new Vector2(
                ((_renderTime >> 6) & 0x3FF) / 1024f,
                ((_renderTime >> 7) & 0x3FF) / 1024f);

        return Vector2.Zero;
    }

    // Scroll of the material's second texture layer, from its own form state.
    private Vector2 LayerOffset(PristonMaterialRuntime material) =>
        material.Layer is { } layer ? new Vector2(TextureUOffset(layer.FormState), 0f) : Vector2.Zero;

    private float TextureUOffset(uint textureFormState)
    {
        if (textureFormState == TexFormScroll)
            return ((_renderTime >> 6) & 0xFF) / 256f;

        if (textureFormState >= TexFormScroll2 && textureFormState <= TexFormScroll10)
            return (((_renderTime >> 6) & 0xFF) / 256f) * (textureFormState - (TexFormScroll2 - 2));

        if (textureFormState >= TexFormScrollSlow1 && textureFormState <= TexFormScrollSlow4)
        {
            var factor = (int)(TexFormScrollSlow4 - textureFormState + 4);
            var mask = 0xFFFFu >> factor;
            if (mask == 0)
                return 0f;
            return ((_renderTime >> 6) & mask) / (float)mask;
        }

        return 0f;
    }
}
