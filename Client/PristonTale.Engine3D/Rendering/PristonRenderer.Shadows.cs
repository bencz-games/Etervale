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

// Shadow maps: cascades, the sun and lamp shadow passes and their casters.
public sealed partial class PristonRenderer
{
    private void InvalidateShadowAfterTeleport(Vector3 focus)
    {
        var thresholdSquared = ShadowTeleportRefreshDistance * ShadowTeleportRefreshDistance;
        if (_sunShadowFocusValid && Vector3.DistanceSquared(focus, _sunShadowFocus) > thresholdSquared)
        {
            _sunShadowRefresh.Invalidate();
            _fullShadowRefresh = true;
        }
    }

    private static string FormatUpdateRate(int rate)
    {
        return rate == 0 ? "every frame" : $"{rate} Hz";
    }

    // The moonlight is dim and diffuse enough that the plain filter serves it; the soft shadows are the sun's.
    private ShadowFilterMode ShadowFilterOfHour() => SunLights ? _shadowFilter : ShadowFilterMode.Pcf;

    // Refits the cascades this refresh updates; a new light direction or a teleport refits them all.
    private void FitCascades(PristonCamera camera, Vector3 lightDirection)
    {
        var direction = Vector3.Normalize(lightDirection);
        var full = _fullShadowRefresh || Vector3.Dot(direction, _shadowLightDirection) < 0.99999f;
        _cascadeMask = _cascades.UpdateMask(full);
        for (var cascade = 0; cascade < _cascades.Count; cascade++)
        {
            if ((_cascadeMask & (1 << cascade)) != 0)
            {
                _cascades.Fit(cascade, camera.View, camera.Projection, direction);
            }
        }

        _shadowLightDirection = direction;
        _fullShadowRefresh = false;
    }

    // Renders the cascades of this refresh into their atlas tiles; each tile is cleared by a far-depth triangle over its
    // viewport, so the others keep their contents.
    private void RenderShadowMap()
    {
        _graphicsDevice.SetRenderTarget(_shadowMap);
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        for (var cascade = 0; cascade < _cascades.Count; cascade++)
        {
            if ((_cascadeMask & (1 << cascade)) == 0)
            {
                continue;
            }

            var tile = _cascades.TileRectangle(cascade);
            _graphicsDevice.Viewport = new Viewport(tile.X, tile.Y, tile.Width, tile.Height);
            _sceneTargets.Triangle.Draw(_shaders.SelectShadowClear(), ShadowClearDepth);
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            var viewProjection = _cascades.ViewProjections[cascade];
            _shaders.ApplyShadowFrame(viewProjection, _renderTime);
            DrawShadowQueueItems(_renderQueueBuilder.Queue.ShadowCasters, viewProjection);
        }

        _graphicsDevice.SetRenderTarget(null);
    }

    // Redraws the cube faces of the lamp rows this frame needs (PristonLampShadows.Assign). Only actors and animated props
    // cast: the stage's own occlusion of its lamps is part of the authored lighting (and a lamp's housing would hide it).
    private void RenderLampShadows()
    {
        _graphicsDevice.SetRenderTarget(_lampShadows.Atlas);
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        for (var row = 0; row < _lampShadows.Lamps; row++)
        {
            if ((_lampShadowRedraw & (1 << row)) == 0)
            {
                continue;
            }

            _lampShadowMapsUpdated++;
            var position = _lampShadows.RowPosition(row);
            var radius = _lampShadows.RowRadius(row);
            var casterLight = new Vector4(position, 1f / MathF.Max(radius, 1f));
            for (var face = 0; face < PristonCubeFaces.Count; face++)
            {
                var tile = _lampShadows.FaceRectangle(row, face);
                _graphicsDevice.Viewport = new Viewport(tile.X, tile.Y, tile.Width, tile.Height);
                _sceneTargets.Triangle.Draw(_shaders.SelectShadowClear(), ShadowClearDepth);
                _graphicsDevice.DepthStencilState = DepthStencilState.Default;
                var viewProjection = PristonLampShadows.FaceViewProjection(position, radius, face);
                _shaders.ApplyShadowFrame(viewProjection, _renderTime, casterLight);
                DrawShadowQueueItems(_renderQueueBuilder.Queue.ShadowCasters, viewProjection, skinnedOnly: true);
            }
        }

        _shaders.ApplyShadowFrame(Matrix.Identity, _renderTime);
        _graphicsDevice.SetRenderTarget(null);
    }

    // Reads the batches, not the queue: a frame that redraws no shadow leaves the queue without casters, and underground
    // (no sun) the lamps would then never see a character walk into their reach again.
    private bool LampHasCasters(Vector3 position, float radius)
    {
        var sphere = new BoundingSphere(position, radius);
        for (var i = 0; i < _renderBatches.Count; i++)
        {
            var batch = _renderBatches[i];
            if (batch.IsSkinned && batch.Shade is not { Hidden: true } && RenderQueueBuilder.CastsShadow(batch) && sphere.Intersects(batch.Bounds))
            {
                return true;
            }
        }

        for (var i = 0; i < _shadowBatches.Count; i++)
        {
            var batch = _shadowBatches[i];
            if (batch.IsSkinned && batch.Shade is not { Shadowed: false } && sphere.Intersects(batch.Bounds))
            {
                return true;
            }
        }

        return false;
    }

    // skinnedOnly: actors and animated props only (the lamps' cube faces).
    private void DrawShadowQueueItems(IReadOnlyList<PristonRenderQueueItem<RenderBatch>> items, Matrix lightViewProjection, bool skinnedOnly = false)
    {
        _shadowCasterFrustum.Matrix = lightViewProjection;
        // Thin/single-sided casters need both faces in every shadow draw. This state is invariant for
        // the whole queue and the colour pass invalidates its cache immediately after shadow rendering.
        _graphicsDevice.RasterizerState = PristonRasterizerStates.TwoSided;
        for (var i = 0; i < items.Count; i++)
        {
            var batch = items[i].Batch;
            if ((skinnedOnly && !batch.IsSkinned) || !_shadowCasterFrustum.Intersects(batch.Bounds))
            {
                continue;
            }

            _shadowDrawCalls++;

            var casterTexture = batch.Material.AlphaTest ? batch.Material.SelectTexture(_renderTime) : null;
            var casterUvOffset = batch.Material.AlphaTest ? ComputeUvScrollOffset(batch.Material) : Vector2.Zero;
            if (batch.IsSkinned)
            {
                var effect = _shaders.SelectSkinnedShadow(batch.Material, casterTexture, batch.SkinPalette, casterUvOffset);
                effect.CurrentTechnique.Passes[0].Apply();
                _graphicsDevice.SetVertexBuffer(batch.SkinnedBuffer);
                _graphicsDevice.Indices = batch.SkinnedIndexBuffer;
                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.SkinnedPrimitiveCount);
            }
            else
            {
                var effect = _shaders.SelectStaticShadow(batch.Material, casterTexture, ComputeWindParams(batch), casterUvOffset);

                if (batch.Mesh == null)
                {
                    _batchUploader.Ensure(batch);
                }

                effect.CurrentTechnique.Passes[0].Apply();
                if (batch.Mesh != null)
                {
                    _graphicsDevice.SetVertexBuffer(batch.Mesh.VertexBuffer);
                    _graphicsDevice.Indices = batch.Mesh.IndexBuffer;
                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.Mesh.PrimitiveCount);
                }
                else
                {
                    _graphicsDevice.SetVertexBuffer(batch.DynamicBuffer);
                    _graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, batch.Vertices.Length / 3);
                }
            }
        }
    }
}
