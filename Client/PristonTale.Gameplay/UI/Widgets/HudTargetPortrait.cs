using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.UI.Widgets;

// What the target frame needs from the world: the renderer and the live actors of an entity (none while it loads).
public interface IHudPortraitScene
{
    PristonRenderer Renderer { get; }

    int CopyActorHandles(ulong entityId, Span<ActorHandle> destination);
}

// The authored camera correction (height, distance) applies to monsters and NPCs; players have none.
public readonly record struct HudPortraitSubject(ulong EntityId, int CorrectionY, int CorrectionZ);

public readonly record struct HudPortraitCamera(Vector3 Position, Vector3 Target, Vector3 Up, float FieldOfView, float AspectRatio, float Near, float Far);

// The target frame's live 3D portrait, rendered into its own target before the world each frame. As in the original the
// camera sits in front of the actor, level with its head bone (taken once per target, at the stand pose) plus three units,
// 100 units plus the head height away, both corrected by the model's authored portrait offset; the projection is the
// screen's actor projection centred on the frame and narrowed to a head close-up, so the framing scales with the HUD.
public sealed class HudTargetPortrait : IDisposable
{
    // Only this name: rigs whose biped is "Bip001" are framed by the authored height, as the original does, and that
    // fallback sits lower than their head bone.
    public const string HeadBone = "Bip01 Head";
    public const float ScreenFieldOfView = MathF.PI / 4.4f;
    public const float BaseDistance = 100f;
    public const float EyeLift = 3f;
    public const float FallbackHeadDrop = 10f;

    // The portrait is a head close-up: the frame shows this much less than the screen's actor projection would.
    public const float CloseUp = 1.15f;
    private const float MinimumDistance = 10f;
    private const int MultiSampleCount = 4;

    // Studio light in units of the metered daylight (a white surface facing the key displays at 1 before exposure).
    private const float LampReferenceDistance = 150f;
    private const float Exposure = 1.45f;
    private static readonly Vector3 KeyColor = new(1.0f, 0.96f, 0.9f);
    private static readonly Vector3 SkyFill = new(0.26f, 0.28f, 0.33f);
    private static readonly Vector3 GroundFill = new(0.09f, 0.08f, 0.07f);
    private static readonly Vector3 RimColor = new(0.7f, 0.74f, 0.82f);

    private readonly GraphicsDevice _device;
    private readonly PristonBoundTargets _previous = new();
    private RenderTarget2D _target;
    private PristonPortraitAnchor _anchor;
    private ulong _anchorEntity;
    private bool _hasAnchor;
    private ulong _drawnEntity;

    public HudTargetPortrait(GraphicsDevice device)
    {
        _device = device;
    }

    public Texture2D Texture(ulong entityId) => entityId != 0 && entityId == _drawnEntity ? _target : null;

    public static HudPortraitCamera Camera(PristonPortraitAnchor anchor, Matrix world, HudPortraitSubject subject, Rectangle frame, int screenHeight)
    {
        var head = anchor.HasBone ? anchor.Bone : new Vector3(0f, anchor.Height - FallbackHeadDrop, 0f);
        var height = head.Y + subject.CorrectionY;
        var distance = MathF.Max(BaseDistance + height + subject.CorrectionZ, MinimumDistance);
        var look = new Vector3(head.X, height + EyeLift, head.Z);
        var eye = look + Vector3.Backward * distance;
        var position = Vector3.Transform(eye, world);
        var target = Vector3.Transform(look, world);
        var up = Vector3.TransformNormal(Vector3.Up, world);
        up = up.LengthSquared() > 1e-8f ? Vector3.Normalize(up) : Vector3.Up;
        var span = MathF.Max(Vector3.Distance(position, target), 1f);
        var focal = MathF.Max(screenHeight, 1) * 0.5f / MathF.Tan(ScreenFieldOfView * 0.5f);
        var fieldOfView = 2f * MathF.Atan(MathF.Max(frame.Height, 1) * 0.5f / (focal * CloseUp));
        var aspect = MathF.Max(frame.Width, 1) / (float)MathF.Max(frame.Height, 1);
        return new HudPortraitCamera(position, target, up, fieldOfView, aspect, MathF.Max(span * 0.1f, 1f), span * 8f + 1000f);
    }

    // Key light from the camera's upper left, a cool sky fill, a warm ground bounce and a rim light behind on the right.
    public static PristonPortraitLighting Lighting(HudPortraitCamera camera)
    {
        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var right = Vector3.Cross(forward, camera.Up);
        right = right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Vector3.Right;
        var up = Vector3.Cross(right, forward);
        var towardKey = Vector3.Normalize(-forward * 0.75f + up * 0.55f - right * 0.45f);
        var span = Vector3.Distance(camera.Position, camera.Target);
        var rimPosition = camera.Target + Vector3.Normalize(forward * 0.8f + up * 0.6f + right * 0.55f) * (span * 0.5f);
        var rimDistance = Vector3.Distance(rimPosition, camera.Target);
        var falloff = rimDistance * rimDistance / (LampReferenceDistance * LampReferenceDistance);
        return new PristonPortraitLighting(-towardKey, KeyColor, SkyFill, GroundFill, rimPosition, RimColor * falloff, rimDistance * 4f, Exposure);
    }

    public void Render(IHudPortraitScene scene, HudPortraitSubject subject, HudLayout layout)
    {
        _drawnEntity = 0;
        if (subject.EntityId != _anchorEntity)
        {
            _anchorEntity = subject.EntityId;
            _hasAnchor = false;
        }

        var renderer = scene?.Renderer;
        if (renderer is null)
        {
            return;
        }

        renderer.PortraitActor = ActorHandle.None;
        if (subject.EntityId == 0)
        {
            return;
        }

        Span<ActorHandle> handles = stackalloc ActorHandle[PlayerActorHandles.Most];
        var count = scene.CopyActorHandles(subject.EntityId, handles);
        if (count <= 0)
        {
            return;
        }

        handles = handles[..Math.Min(count, PlayerActorHandles.Most)];
        // Close up, the crowd's slower pose sampling of a far actor would show as a halting animation.
        renderer.PortraitActor = handles[0];
        if (!_hasAnchor)
        {
            _hasAnchor = renderer.TryGetActorPortraitAnchor(handles[0], HeadBone, out _anchor);
        }

        if (!_hasAnchor || !renderer.TryGetActorWorld(handles[0], out var world))
        {
            return;
        }

        var frame = HudCombat.TargetFrame(layout).Portrait;
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        var camera = Camera(_anchor, world, subject, frame, layout.Height);
        EnsureTarget(frame.Width * layout.Scale, frame.Height * layout.Scale);
        var view = Matrix.CreateLookAt(camera.Position, camera.Target, camera.Up);
        var projection = PristonDepth.Perspective(camera.FieldOfView, camera.AspectRatio, camera.Near, camera.Far);
        var light = Lighting(camera);
        _previous.Save(_device);
        _device.SetRenderTarget(_target);
        try
        {
            _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, PristonDepth.Clear, 0);
            renderer.DrawActorPortrait(handles, view, projection, camera.Position, light);
        }
        finally
        {
            _previous.Restore(_device);
            _previous.Forget();
        }

        _drawnEntity = subject.EntityId;
    }

    private void EnsureTarget(int width, int height)
    {
        if (_target is not null && _target.Width == width && _target.Height == height)
        {
            return;
        }

        _target?.Dispose();
        _target = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, PristonDepth.Format, MultiSampleCount,
            RenderTargetUsage.PreserveContents);
    }

    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
    }
}
