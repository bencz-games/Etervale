using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

public interface IActorScene
{
    Task PrepareActorResourcesAsync(PristonActor actor) => Task.CompletedTask;
    void CopyActorPlayback(ActorHandle source, ActorHandle destination) { }
    void AttachActor(ActorHandle child, ActorHandle parent, string boneName, string cheerBoneName = null) => throw new NotSupportedException("Actor attachments are not supported by this scene.");
    void SetActorAttachOffset(ActorHandle child, Matrix offset, float lift) { }
    ActorHandle AddActor(PristonActorInstance instance);
    void SetActorWorld(ActorHandle handle, Matrix world);
    void SetActorColorAdd(ActorHandle handle, Vector3 colorAdd) { }
    bool TryGetActorMotion(ActorHandle handle, out PristonMotionInfo motion, out float ticks)
    {
        motion = null;
        ticks = 0f;
        return false;
    }

    void SetActorMotion(ActorHandle handle, MotionCode motion);
    void SetActorMotionIndex(ActorHandle handle, int motionIndex);
    int ResolveSkillMotion(ActorHandle handle, int motionIndex) => motionIndex;
    void SeekActorMotion(ActorHandle handle, int framesFromStart) { }
    void RestartActorMotion(ActorHandle handle, MotionCode motion);
    void SetActorPlaybackRate(ActorHandle handle, float rateFactor);
    void SetActorHidden(ActorHandle handle, bool hidden) { }
    void RemoveActor(ActorHandle handle);
    void SetActorAppearance(ActorHandle handle, in PristonActorAppearance appearance) { }
    ActorHandle AddActorEcho(ActorHandle source, float lagFrames, ActorHandle parent) => ActorHandle.None;
    Task PrepareActorOverlayAsync(string path) => Task.CompletedTask;
}
