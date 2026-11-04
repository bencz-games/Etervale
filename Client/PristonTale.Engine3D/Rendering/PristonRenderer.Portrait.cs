using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

// An actor's portrait anchor in model space: the named bone at the first frame of its stand motion (the current frame
// without one), the mesh's authored height (the highest object top the model file records), its current bounds in model
// space, and the actor's world transform when it was taken.
public readonly record struct PristonPortraitAnchor(Vector3 Bone, bool HasBone, float Height, BoundingBox Bounds, Matrix World);

// Studio light of an inspection portrait, independent of the world's hour and automatic exposure. Colours are radiance
// relative to a white surface facing the light (1 displays at 1 before Exposure); KeyDirection is the direction the key
// light travels; the rim is a point light whose colour applies at the shaders' lamp reference distance.
public readonly record struct PristonPortraitLighting(
    Vector3 KeyDirection,
    Vector3 KeyColor,
    Vector3 SkyFill,
    Vector3 GroundFill,
    Vector3 RimPosition,
    Vector3 RimColor,
    float RimRange,
    float Exposure);

public sealed partial class PristonRenderer
{
    private readonly List<PristonPointLight> _portraitLights = new(1);
    private bool _neutralActorPass;

    public bool TryGetActorWorld(ActorHandle handle, out Matrix world)
    {
        if (TryResolveActor(handle, out var runtime))
        {
            world = runtime.World;
            return true;
        }

        world = Matrix.Identity;
        return false;
    }

    // False until the actor's geometry is resident, so callers retry while it loads.
    public bool TryGetActorPortraitAnchor(ActorHandle handle, string boneName, out PristonPortraitAnchor anchor)
    {
        anchor = default;
        if (!TryResolveActor(handle, out var runtime) || runtime.Batches.Count == 0)
        {
            return false;
        }

        var bone = runtime.Instance.Actor.Skeleton.ObjectIndex(boneName);
        var position = Vector3.Zero;
        if (bone >= 0)
        {
            var stand = SelectActorMotion(runtime.Instance.Actor, MotionCode.Stand, runtime.Instance.MotionContext);
            var frame = stand.EndFrame > stand.StartFrame ? stand.StartFrame * GameUnits.FrameTicks : runtime.Frame;
            position = runtime.SampleSkeletonAt(frame, stand.EndFrame * GameUnits.FrameTicks)[bone].Translation;
        }

        var bounds = ModelBounds(runtime);
        anchor = new PristonPortraitAnchor(position, bone >= 0, AuthoredHeight(runtime.Instance.Actor.Mesh, bounds.Max.Y), bounds, runtime.World);
        return true;
    }

    // How far a held item reaches along its length (the model's Y) from its grip, in its model units: the authored bounds
    // when the file records them, else the resident geometry's; false until the geometry is resident.
    public bool TryGetActorReach(ActorHandle handle, out float reach)
    {
        reach = 0f;
        if (!TryResolveActor(handle, out var runtime) || runtime.Batches.Count == 0)
        {
            return false;
        }

        var authored = 0;
        foreach (var model in runtime.Instance.Actor.Mesh.Objects)
        {
            if (model.AuthoredBoundsFixed is { } bounds)
            {
                authored = Math.Max(authored, Math.Max(Math.Abs(bounds.MaximumY), Math.Abs(bounds.MinimumY)));
            }
        }

        if (authored > 0)
        {
            reach = authored / GameUnits.FixedScale;
            return true;
        }

        var local = ModelBounds(runtime);
        reach = MathF.Max(MathF.Abs(local.Min.Y), MathF.Abs(local.Max.Y));
        return true;
    }

    private static float AuthoredHeight(PristonModel mesh, float fallback)
    {
        var height = 0;
        foreach (var model in mesh.Objects)
        {
            if (model.AuthoredBoundsFixed is { } bounds)
            {
                height = Math.Max(height, bounds.MaximumY);
            }
        }

        return height > 0 ? height / GameUnits.FixedScale : fallback;
    }

    // The batches hold the animated world bounds; the anchor wants them in the actor's model space.
    private static BoundingBox ModelBounds(ActorRuntime runtime)
    {
        var world = runtime.Batches[0].Bounds;
        for (var index = 1; index < runtime.Batches.Count; index++)
        {
            world = BoundingBox.CreateMerged(world, runtime.Batches[index].Bounds);
        }

        var inverse = Matrix.Invert(runtime.World);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector3(
                (corner & 1) == 0 ? world.Min.X : world.Max.X,
                (corner & 2) == 0 ? world.Min.Y : world.Max.Y,
                (corner & 4) == 0 ? world.Min.Z : world.Max.Z);
            point = Vector3.Transform(point, inverse);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return new BoundingBox(min, max);
    }

    // Draws live actors into the bound target with a studio light and without their gameplay highlight or floor light.
    public void DrawActorPortrait(ReadOnlySpan<ActorHandle> handles, Matrix view, Matrix projection, Vector3 cameraPosition, in PristonPortraitLighting light)
    {
        if (!CollectPortraitBatches(handles))
        {
            return;
        }

        _portraitLights.Clear();
        if (light.RimColor != Vector3.Zero && light.RimRange > 0f)
        {
            _portraitLights.Add(new PristonPointLight(light.RimPosition, light.RimRange, light.RimColor, 1f));
        }

        var key = light.KeyDirection == Vector3.Zero ? Vector3.Down : Vector3.Normalize(light.KeyDirection);
        var lighting = new PristonLighting(light.SkyFill, new PristonDirectionalLight(key, light.KeyColor), _portraitLights, 0, light.GroundFill);
        var exposure = MathF.Max(light.Exposure, 1e-3f);
        var frame = new PristonFrameContext(
            view,
            projection,
            cameraPosition,
            _renderTime,
            lighting,
            Matrix.Identity,
            _textureLoader.White,
            false,
            0f,
            Exposure: exposure,
            DisplayOutput: true,
            EmissionScale: 1f / exposure);
        _neutralActorPass = true;
        try
        {
            DrawPortraitBatches(frame);
        }
        finally
        {
            _neutralActorPass = false;
        }
    }

    private bool CollectPortraitBatches(ReadOnlySpan<ActorHandle> handles)
    {
        _portraitBatches.Clear();
        for (var handleIndex = 0; handleIndex < handles.Length; handleIndex++)
        {
            if (TryResolveActor(handles[handleIndex], out var runtime))
            {
                _portraitBatches.AddRange(runtime.Batches);
            }
        }

        return _portraitBatches.Count > 0;
    }

    // The portrait has no depth prepass of its own; the world's flag from its frame would select the prepassed depth
    // test (equal, without write) against the portrait's cleared depth, and nothing opaque would be drawn.
    private void DrawPortraitBatches(PristonFrameContext frame)
    {
        var prepassDrawn = _prepassDrawn;
        _prepassDrawn = false;
        try
        {
            DrawPortraitQueue(frame);
        }
        finally
        {
            _prepassDrawn = prepassDrawn;
        }
    }

    private void DrawPortraitQueue(PristonFrameContext frame)
    {
        _portraitQueueBuilder.Build(_portraitBatches, [], frame, includeShadowCasters: false);

        _stateCache.BeginFrame();
        _stateCache.InvalidateDeviceState();
        _stateCache.SetDepthStencilState(PristonDepth.Write);
        _stateCache.SetSamplerState0(PristonSamplerStates.AnisotropicWrap);
        _shaders.ApplyFrame(frame);

        var staticBatches = 0;
        var dynamicBatches = 0;
        var drawCalls = 0;
        var triangles = 0;
        var staticVertices = 0;
        var staticIndices = 0;
        var normalBatches = 0;
        var twoSidedBatches = 0;
        var diagnostics = new PristonMeshDiagnostics();
        DrawQueue(
            _portraitQueueBuilder.Queue,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref diagnostics,
            intoScene: false);
    }
}
