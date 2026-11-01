using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

// How gameplay changes an actor's look: Tint is the original's colour adjustment as a signed light (1 = a full white
// light, negative takes light away), Opacity blends the actor over the scene, Wobble shakes its mesh, CastsShadow keeps
// it in the shadow maps, Overlay (a texture path) scrolls over the body by the texture form state OverlayForm, and Scale
// grows an attached item about its bone.
public readonly record struct PristonActorAppearance(
    Vector3 Tint,
    float Opacity,
    bool Wobble,
    bool CastsShadow,
    string Overlay,
    uint OverlayForm,
    float Scale)
{
    public static PristonActorAppearance Normal => new(Vector3.Zero, 1f, false, true, "", 0, 1f);
}

// The appearance shared by an actor's batches. Echo actors hide while their lagged frame lies before the clip start.
internal sealed class ActorShade
{
    // World units of the distortion wobble (the original moves camera-space vertices by 2 units).
    public const float WobbleAmplitude = 2f;

    public Vector3 Tint;
    public float Opacity = 1f;
    // A corpse's look over the appearance: Fade multiplies its opacity, Dim takes light away (in the Tint's units).
    public float Fade = 1f;
    public float Dim;
    public bool Wobble;
    public bool CastsShadow = true;
    public Texture2D Overlay;
    public uint OverlayForm;
    public float Scale = 1f;
    public bool EchoHidden;

    public float ShownOpacity => Opacity * Fade;

    public Vector3 ShownTint => Tint - new Vector3(Dim);

    public bool Hidden => ShownOpacity <= 0.004f || EchoHidden;

    public bool Translucent => ShownOpacity < 0.999f;

    public bool Shadowed => CastsShadow && !Hidden && !Translucent;
}

public sealed partial class PristonRenderer
{
    // Decodes an overlay texture (absolute path) on a worker; an appearance shows it once it is ready.
    public Task PrepareActorOverlayAsync(string path) =>
        _textureLoader.PrepareTextureAsync(Path.GetDirectoryName(path) ?? ".", Path.GetFileName(path));

    public void SetActorAppearance(ActorHandle handle, in PristonActorAppearance appearance)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        var shade = ShadeOf(runtime);
        shade.Tint = appearance.Tint;
        shade.Opacity = Math.Clamp(appearance.Opacity, 0f, 1f);
        shade.Wobble = appearance.Wobble;
        shade.CastsShadow = appearance.CastsShadow && !runtime.EchoSource.IsValid;
        shade.OverlayForm = appearance.OverlayForm;
        shade.Overlay = string.IsNullOrEmpty(appearance.Overlay) ? null : PreparedOverlay(appearance.Overlay);
        shade.Scale = appearance.Scale > 0f ? appearance.Scale : 1f;
    }

    // A corpse's darkening and fading, kept apart from the appearance gameplay paints each frame.
    public void SetActorCorpse(ActorHandle handle, float fade, float dim)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        var shade = ShadeOf(runtime);
        shade.Fade = Math.Clamp(fade, 0f, 1f);
        shade.Dim = Math.Max(dim, 0f);
    }

    // A copy of an actor that plays its clip lagFrames behind (an afterimage) or, for an attached item, the same item
    // attached to parent (an afterimage's body) or to the item's own parent (a second, enlarged weapon). Echoes cast
    // no shadow; remove them with RemoveActor.
    public ActorHandle AddActorEcho(ActorHandle source, float lagFrames, ActorHandle parent)
    {
        if (!TryResolveActor(source, out var original))
        {
            return ActorHandle.None;
        }

        var handle = AddActor(original.Instance);
        if (!TryResolveActor(handle, out var echo))
        {
            return ActorHandle.None;
        }

        ShadeOf(echo).CastsShadow = false;
        if (original.Parent.IsValid)
        {
            echo.Parent = parent.IsValid ? parent : original.Parent;
            echo.ParentBone = original.ParentBone;
            echo.CheerBone = original.CheerBone;
            if (TryResolveActor(echo.Parent, out var owner))
            {
                echo.World = AttachedWorld(echo, owner);
            }
        }
        else
        {
            echo.EchoSource = source;
            echo.EchoLagTicks = MathF.Max(0f, lagFrames) * GameUnits.FrameTicks;
            echo.Follow(original);
        }

        UpdateActor(echo);
        return handle;
    }

    private static ActorShade ShadeOf(ActorRuntime runtime)
    {
        if (runtime.Shade is { } shade)
        {
            return shade;
        }

        shade = new ActorShade();
        runtime.Shade = shade;
        foreach (var batch in runtime.Batches)
        {
            batch.Shade = shade;
        }

        foreach (var batch in runtime.ShadowBatches)
        {
            batch.Shade = shade;
        }

        return shade;
    }

    private Texture2D PreparedOverlay(string path) =>
        _textureLoader.TryLoadPrepared(Path.GetDirectoryName(path) ?? ".", Path.GetFileName(path), out var texture) ? texture : null;

    // Echoes copy their source's clip and world after it advanced.
    private void FollowEchoes()
    {
        foreach (var actor in _actors)
        {
            if (actor.Alive && actor.EchoSource.IsValid && TryResolveActor(actor.EchoSource, out var source))
            {
                actor.Follow(source);
                UpdateActor(actor);
            }
        }
    }

    // An attached actor's world: its parent's bone (the cheer bone while the parent plays Yahoo) in the pose of the
    // parent's palette, grown by its appearance's scale about the bone.
    private static Matrix AttachedWorld(ActorRuntime actor, ActorRuntime parent)
    {
        var index = actor.CheerBone >= 0 && parent.Motion?.State == (uint)MotionCode.Yahoo ? actor.CheerBone : actor.ParentBone;
        var bone = parent.ShownPose()[index] * parent.World;
        return actor.Shade is { Scale: not 1f } shade ? Matrix.CreateScale(shade.Scale) * bone : bone;
    }

    private float WobblePhase => _renderTime % GameUnits.AngleTurn * GameUnits.RadiansPerAngle;
}
