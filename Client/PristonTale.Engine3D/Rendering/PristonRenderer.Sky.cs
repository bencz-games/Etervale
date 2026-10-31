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

// The sky: its frame, layers, fog colour and the environment capture.
public sealed partial class PristonRenderer
{
    private PristonFrameContext CreateSkyFrame(PristonFrameContext frame, Matrix view, Matrix projection, Vector3 skyPosition)
    {
        return frame with
        {
            View = view,
            Projection = projection,
            CameraPosition = skyPosition,
            ShadowsEnabled = false,
            LampFactor = 0f,
            Fog = PristonDistanceFog.Disabled,
            FogSky = new PristonFogSky(_textureLoader.White, false, Vector3.Up, Vector3.Zero)
        };
    }

    private int DrawSkyLayers(
        int currentProfile,
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
        var count = 0;
        for (var layer = 0; layer < 2; layer++)
        {
            var profile = layer == 0 ? currentProfile : _previousSkyProfile;
            var opacity = layer == 0 ? 1f : 1f - _skyBlend;
            if (profile < 0 || opacity <= 0f)
            {
                continue;
            }

            foreach (var batch in Sky.GetBatches(profile))
            {
                var blend = batch.Material.BlendType <= 1 ? BlendState.NonPremultiplied : batch.Material.BlendState;
                _stateCache.SetBlendState(blend);
                DrawBatch(batch, ref staticBatches, ref dynamicBatches, ref drawCalls, ref triangles,
                    ref staticVertices, ref staticIndices, ref normalBatches, ref twoSidedBatches,
                    ref visibleStaticMeshDiagnostics, opacity, sky: true);
                count++;
            }
        }

        return count;
    }

    // Atmospheric fog takes its colour from a small HDR copy of the composed sky, refreshed here, before the scene target
    // is cleared, because binding another render target may discard it.
    private PristonFogSky PrepareFogSky(PristonFrameContext frame, bool skyVisible, Vector3 skyPosition)
    {
        if (!frame.Fog.Enabled)
        {
            return new PristonFogSky(_textureLoader.White, false, _towardSun, Vector3.Zero);
        }

        Texture2D capture = null;
        if (skyVisible)
        {
            var currentProfile = SkyProfileNow;
            var transition = _previousSkyProfile >= 0 && _skyBlend < 1f;
            var animated = PhysicalSkyShown || Sky.IsAnimated(currentProfile) || (transition && Sky.IsAnimated(_previousSkyProfile));
            if (_skyCapture.ShouldRefresh(currentProfile, _previousSkyProfile, _skyBlend, skyPosition.Y, animated, _renderTime))
            {
                Profiler.Begin(RenderSection.SkyCapture);
                RenderSkyCapture(frame, skyPosition, currentProfile);
                Profiler.End(RenderSection.SkyCapture);
            }

            capture = _skyCapture.Texture;
        }

        // The in-scatter follows the sun down to about 7 degrees below the horizon; it is zero at night and underground.
        var elevation = MathHelper.Clamp((_sunElevation + 0.12f) / 0.17f, 0f, 1f);
        var gate = elevation * elevation * (3f - 2f * elevation);
        var inScatter = _fogSunColour * (frame.Fog.InScatter * gate);
        return new PristonFogSky(capture ?? _textureLoader.White, capture is not null, _towardSun, inScatter);
    }

    private void RenderSkyCapture(PristonFrameContext frame, Vector3 skyPosition, int currentProfile)
    {
        var staticBatches = 0;
        var dynamicBatches = 0;
        var drawCalls = 0;
        var triangles = 0;
        var staticVertices = 0;
        var staticIndices = 0;
        var normalBatches = 0;
        var twoSidedBatches = 0;
        var diagnostics = new PristonMeshDiagnostics();
        var projection = PristonSkyCapture.TileProjection();
        _skyCapture.Begin(SkyColor, _renderTime);
        _stateCache.InvalidateDeviceState();
        for (var tile = 0; tile < PristonSkyCapture.TileCount; tile++)
        {
            _skyCapture.BeginTile(tile);
            _shaders.ApplyFrame(CreateSkyFrame(frame, PristonSkyCapture.TileView(tile, skyPosition), projection, skyPosition));
            _stateCache.SetDepthStencilState(DepthStencilState.None);
            if (DrawPhysicalSky(PristonSkyCapture.TileView(tile, skyPosition), projection))
            {
                continue;
            }

            DrawSkyLayers(
                currentProfile,
                ref staticBatches,
                ref dynamicBatches,
                ref drawCalls,
                ref triangles,
                ref staticVertices,
                ref staticIndices,
                ref normalBatches,
                ref twoSidedBatches,
                ref diagnostics);
        }

        _skyCapture.End();
    }

    // The dome's six cube faces, then the environment built from them (Lighting/PristonEnvironmentLighting).
    private void RenderEnvironment(PristonFrameContext frame, bool skyVisible, Vector3 skyPosition)
    {
        _environment.BeginFaces(SkyColor);
        if (skyVisible)
        {
            var staticBatches = 0;
            var dynamicBatches = 0;
            var drawCalls = 0;
            var triangles = 0;
            var staticVertices = 0;
            var staticIndices = 0;
            var normalBatches = 0;
            var twoSidedBatches = 0;
            var diagnostics = new PristonMeshDiagnostics();
            var projection = PristonEnvironmentLighting.FaceProjection();
            _stateCache.InvalidateDeviceState();
            for (var face = 0; face < 6; face++)
            {
                _graphicsDevice.Viewport = PristonEnvironmentLighting.FaceViewport(face);
                _shaders.ApplyFrame(CreateSkyFrame(frame, PristonEnvironmentLighting.FaceView(face, skyPosition), projection, skyPosition));
                _stateCache.SetDepthStencilState(DepthStencilState.None);
                if (DrawPhysicalSky(PristonEnvironmentLighting.FaceView(face, skyPosition), projection))
                {
                    continue;
                }

                DrawSkyLayers(
                    SkyProfileNow,
                    ref staticBatches,
                    ref dynamicBatches,
                    ref drawCalls,
                    ref triangles,
                    ref staticVertices,
                    ref staticIndices,
                    ref normalBatches,
                    ref twoSidedBatches,
                    ref diagnostics);
            }
        }

        _environment.Build(_renderTime, frame.Exposure, frame.Lighting.AmbientColor, frame.Lighting.GroundAmbientColor);
        _stateCache.InvalidateDeviceState();
    }
}
