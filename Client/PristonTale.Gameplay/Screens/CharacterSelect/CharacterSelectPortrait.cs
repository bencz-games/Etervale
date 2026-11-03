using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal sealed class CharacterSelectPortrait : IDisposable
{
    private const float VerticalFieldOfView = 30f * MathF.PI / 180f;
    private const float VisibleHeightRatio = 0.66f;
    private const float FocusHeightRatio = 0.70f;

    private readonly GraphicsDevice _graphicsDevice;
    private RenderTarget2D _target;

    public CharacterSelectPortrait(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    public Texture2D Render(PristonRenderer renderer, PlayerActorHandles actor, int width, int height)
    {
        Span<ActorHandle> handles = stackalloc ActorHandle[PlayerActorHandles.Most];
        var count = 0;
        AddHandle(actor.Body, handles, ref count);
        AddHandle(actor.Head, handles, ref count);
        AddHandle(actor.Weapon, handles, ref count);
        AddHandle(actor.OffHand, handles, ref count);
        var activeHandles = handles[..count];
        if (count == 0 || !renderer.TryGetActorBounds(activeHandles, out var bounds))
        {
            return null;
        }

        EnsureTarget(width, height);
        var size = bounds.Max - bounds.Min;
        var center = new Vector3(
            (bounds.Min.X + bounds.Max.X) * 0.5f,
            bounds.Min.Y + size.Y * FocusHeightRatio,
            (bounds.Min.Z + bounds.Max.Z) * 0.5f);
        var aspect = width / (float)height;
        var halfVerticalTangent = MathF.Tan(VerticalFieldOfView * 0.5f);
        var visibleHalfHeight = size.Y * VisibleHeightRatio * 0.5f;
        var verticalDistance = visibleHalfHeight / halfVerticalTangent;
        var horizontalDistance = size.X * 0.58f / (halfVerticalTangent * aspect);
        var distance = MathF.Max(verticalDistance, horizontalDistance) + MathF.Max(size.Z * 0.25f, 1f);
        distance = MathF.Max(distance, 10f);
        var cameraPosition = center + Vector3.Backward * distance;
        var view = Matrix.CreateLookAt(cameraPosition, center, Vector3.Up);
        var projection = PristonDepth.Perspective(
            VerticalFieldOfView,
            aspect,
            0.1f,
            distance + MathF.Max(size.Length() * 4f, 100f));

        _graphicsDevice.SetRenderTarget(_target);
        try
        {
            _graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, PristonDepth.Clear, 0);
            renderer.DrawActorPortrait(activeHandles, view, projection, cameraPosition, center);
        }
        finally
        {
            _graphicsDevice.SetRenderTarget(null);
        }

        return _target;
    }

    private void EnsureTarget(int width, int height)
    {
        if (_target != null && _target.Width == width && _target.Height == height)
        {
            return;
        }

        _target?.Dispose();
        _target = new RenderTarget2D(
            _graphicsDevice,
            width,
            height,
            false,
            SurfaceFormat.Color,
            PristonDepth.Format,
            0,
            RenderTargetUsage.PreserveContents);
    }

    private static void AddHandle(ActorHandle handle, Span<ActorHandle> handles, ref int count)
    {
        if (!handle.IsValid)
        {
            return;
        }

        handles[count] = handle;
        count++;
    }

    public void Dispose()
    {
        _target?.Dispose();
    }
}
