using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RendererActorScene(PristonRenderer renderer) : IActorScene
{
    private readonly PristonRenderer _renderer = renderer;

    public Task PrepareActorResourcesAsync(PristonActor actor) => _renderer.PrepareActorResourcesAsync(actor);

    public void CopyActorPlayback(ActorHandle source, ActorHandle destination) => _renderer.CopyActorPlayback(source, destination);
    public void AttachActor(ActorHandle child, ActorHandle parent, string boneName, string cheerBoneName = null) =>
        _renderer.AttachActor(child, parent, boneName, cheerBoneName);
    public void SetActorAttachOffset(ActorHandle child, Matrix offset, float lift) => _renderer.SetActorAttachOffset(child, offset, lift);
    public ActorHandle AddActor(PristonActorInstance instance) => _renderer.AddActor(instance);
    public void SetActorWorld(ActorHandle handle, Matrix world) => _renderer.SetActorWorld(handle, world);
    public void SetActorColorAdd(ActorHandle handle, Vector3 colorAdd) => _renderer.SetActorColorAdd(handle, colorAdd);
    public bool TryGetActorMotion(ActorHandle handle, out PristonMotionInfo motion, out float ticks) => _renderer.TryGetActorMotion(handle, out motion, out ticks);
    public void SetActorMotion(ActorHandle handle, MotionCode motion) => _renderer.SetActorMotion(handle, motion);
    public void SetActorMotionIndex(ActorHandle handle, int motionIndex) => _renderer.SetActorMotionIndex(handle, motionIndex);
    public int ResolveSkillMotion(ActorHandle handle, int motionIndex) => _renderer.ResolveSkillMotion(handle, motionIndex);
    public void SeekActorMotion(ActorHandle handle, int framesFromStart) => _renderer.SeekActorMotion(handle, framesFromStart);
    public void RestartActorMotion(ActorHandle handle, MotionCode motion) => _renderer.RestartActorMotion(handle, motion);
    public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => _renderer.SetActorPlaybackRate(handle, rateFactor);
    public void SetActorHidden(ActorHandle handle, bool hidden) => _renderer.SetActorHidden(handle, hidden);
    public void RemoveActor(ActorHandle handle) => _renderer.RemoveActor(handle);
    public void SetActorAppearance(ActorHandle handle, in PristonActorAppearance appearance) => _renderer.SetActorAppearance(handle, appearance);
    public ActorHandle AddActorEcho(ActorHandle source, float lagFrames, ActorHandle parent) => _renderer.AddActorEcho(source, lagFrames, parent);
    public Task PrepareActorOverlayAsync(string path) => _renderer.PrepareActorOverlayAsync(path);
}
