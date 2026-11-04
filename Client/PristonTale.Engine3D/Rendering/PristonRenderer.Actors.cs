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

// The actor API: adding and removing actors, their motion, world, look, bounds, bones and attachments.
public sealed partial class PristonRenderer
{
    public void SetActorMotion(ActorHandle handle, MotionCode motion)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        runtime.SetMotion(motion);
        UpdateActor(runtime);
    }

    // The weapon variant of a skill clip for the actor's equipment (see PristonMotionSelection.SkillVariant).
    public int ResolveSkillMotion(ActorHandle handle, int motionIndex) =>
        TryResolveActor(handle, out var runtime) ? PristonMotionSelection.SkillVariant(runtime.Instance.Actor.Info, motionIndex, runtime.Instance.MotionContext)
            : motionIndex;

    public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        runtime.ForceMotionIndex(motionIndex);
        UpdateActor(runtime);
    }

    public void RestartActorMotion(ActorHandle handle, MotionCode motion)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;

        runtime.ForceMotion(motion);
        UpdateActor(runtime);
    }

    public void SeekActorMotion(ActorHandle handle, int framesFromStart)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        runtime.SeekMotion(framesFromStart);
        UpdateActor(runtime);
    }

    public void SetActorHidden(ActorHandle handle, bool hidden)
    {
        if (TryResolveActor(handle, out var runtime) && runtime.Hidden != hidden)
        {
            runtime.Hidden = hidden;
            _batchListsDirty = true;
        }
    }

    public void SetActorPlaybackRate(ActorHandle handle, float rateFactor)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;
        runtime.PlaybackRate = MathF.Max(rateFactor, 1e-3f);
    }

    public void SetActorWorld(ActorHandle handle, Matrix world)
    {
        if (!TryResolveActor(handle, out var runtime) || runtime.World == world)
        {
            return;
        }

        runtime.World = world;
        UpdateActor(runtime);
    }

    public void SetActorColorAdd(ActorHandle handle, Vector3 colorAdd)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        var clamped = Vector3.Clamp(colorAdd, Vector3.Zero, Vector3.One);
        foreach (var batch in runtime.Batches)
        {
            batch.ActorColorAdd = clamped;
        }
    }

    // The hover outline (DrawOutlines): colour display-referred, strength 0 to 1 scales its width, 0 removes it.
    public void SetActorOutline(ActorHandle handle, Vector3 color, float strength)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        var outline = strength > 0f ? new Vector4(Vector3.Clamp(color, Vector3.Zero, Vector3.One), MathF.Min(strength, 1f)) : Vector4.Zero;
        foreach (var batch in runtime.Batches)
        {
            batch.Outline = outline;
        }
    }

    public bool TryGetActorBounds(ReadOnlySpan<ActorHandle> handles, out BoundingBox bounds)
    {
        var hasBounds = false;
        bounds = default;
        for (var handleIndex = 0; handleIndex < handles.Length; handleIndex++)
        {
            if (!TryResolveActor(handles[handleIndex], out var runtime))
            {
                continue;
            }

            for (var batchIndex = 0; batchIndex < runtime.Batches.Count; batchIndex++)
            {
                var batchBounds = runtime.Batches[batchIndex].Bounds;
                bounds = hasBounds ? BoundingBox.CreateMerged(bounds, batchBounds) : batchBounds;
                hasBounds = true;
            }
        }

        return hasBounds;
    }

    public void DrawActorPortrait(
        ReadOnlySpan<ActorHandle> handles,
        Matrix view,
        Matrix projection,
        Vector3 cameraPosition,
        Vector3 focusPoint)
    {
        if (!CollectPortraitBatches(handles))
        {
            return;
        }

        var lighting = BuildFrameLighting(focusPoint);
        var frame = new PristonFrameContext(
            view,
            projection,
            cameraPosition,
            _renderTime,
            lighting,
            Matrix.Identity,
            _textureLoader.White,
            false,
            _lampFactor,
            Exposure: _exposure.DisplayScale,
            DisplayOutput: true,
            EmissionScale: _exposure.EmissionScale);
        DrawPortraitBatches(frame);
    }

    public void CopyActorPlayback(ActorHandle source, ActorHandle destination)
    {
        if (!TryResolveActor(source, out var previous) || !TryResolveActor(destination, out var next))
        {
            return;
        }

        next.PlaybackRate = previous.PlaybackRate;
        next.ForceMotion((MotionCode)previous.Motion.State);
        float span = (previous.Motion.EndFrame - previous.Motion.StartFrame) * GameUnits.FrameTicks;
        float phase = span > 0 ? (previous.Frame - previous.Motion.StartFrame * GameUnits.FrameTicks) / span : 0;
        next.SetFrame((next.Motion.StartFrame + phase * (next.Motion.EndFrame - next.Motion.StartFrame)) * GameUnits.FrameTicks);
        UpdateActor(next);
    }

    // The clip a live actor plays and its frame in ticks (160 per frame), for presentation that follows the clip's events.
    public bool TryGetActorMotion(ActorHandle handle, out PristonMotionInfo motion, out float ticks)
    {
        if (!TryResolveActor(handle, out var actor))
        {
            motion = null;
            ticks = 0f;
            return false;
        }

        motion = actor.Motion;
        ticks = actor.Frame;
        return true;
    }

    // World transform of a named bone of a live actor at its current frame, for effects attached to hands and weapons.
    public bool TryGetActorBoneWorld(ActorHandle handle, string boneName, out Matrix world)
    {
        world = Matrix.Identity;
        if (string.IsNullOrEmpty(boneName) || !TryResolveActor(handle, out var actor))
        {
            return false;
        }

        var index = actor.Instance.Actor.Skeleton.ObjectIndex(boneName);
        if (index < 0)
        {
            return false;
        }

        var bones = actor.SampleSkeleton();
        if (index >= bones.Length)
        {
            return false;
        }

        world = bones[index] * actor.World;
        return true;
    }

    // cheerBoneName: the bone the child hangs from while the parent plays Yahoo (a weapon put on the back), or null.
    public void AttachActor(ActorHandle child, ActorHandle parent, string boneName, string cheerBoneName = null)
    {
        if (!TryResolveActor(child, out var item) || !TryResolveActor(parent, out var owner) || child == parent)
        {
            throw new ArgumentException("Invalid equipment attachment handles.");
        }

        item.Parent = parent;
        item.ParentBone = FindAttachBone(owner, boneName);
        item.CheerBone = cheerBoneName is null ? -1 : FindAttachBone(owner, cheerBoneName);
        item.World = AttachedWorld(item, owner);
        UpdateActor(item);
    }

    // Places an attached actor off its bone: the offset in the bone's frame, then the lift upward.
    public void SetActorAttachOffset(ActorHandle child, Matrix offset, float lift)
    {
        if (!TryResolveActor(child, out var item) || !TryResolveActor(item.Parent, out var owner))
        {
            throw new ArgumentException("Invalid equipment attachment handle.");
        }

        item.AttachOffset = offset;
        item.AttachLift = lift;
        item.World = AttachedWorld(item, owner);
        UpdateActor(item);
    }

    private static int FindAttachBone(ActorRuntime owner, string boneName)
    {
        var index = owner.Instance.Actor.Skeleton.ObjectIndex(boneName);
        return index >= 0 ? index : throw new InvalidDataException($"Equipment bone '{boneName}' is missing from '{owner.Instance.Actor.Skeleton.Path}'.");
    }

    private bool TryResolveActor(ActorHandle handle, out ActorRuntime runtime)
    {
        if (handle.Slot >= 0 && handle.Slot < _actors.Count)
        {
            var candidate = _actors[handle.Slot];
            if (candidate.Alive && candidate.Generation == handle.Generation)
            {
                runtime = candidate;
                return true;
            }
        }
        runtime = null;
        return false;
    }

    public ActorHandle AddActor(PristonActorInstance instance)
    {
        var runtime = BuildActor(instance);
        int slot, generation;
        if (_freeActorSlots.Count > 0)
        {
            slot = _freeActorSlots.Pop();
            generation = _actors[slot].Generation + 1;
            runtime.Generation = generation;
            _actors[slot] = runtime;
        }
        else
        {
            slot = _actors.Count;
            generation = 0;
            _actors.Add(runtime);
        }

        _batchListsDirty = true;
        return new ActorHandle(slot, generation);
    }

    public void RemoveActor(ActorHandle handle)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;
        ReleaseActorGpuResource(runtime);
        runtime.Alive = false;
        _freeActorSlots.Push(handle.Slot);
        _batchListsDirty = true;
    }

    public void ClearActors()
    {
        foreach (var actor in _actors)
        {
            if (actor.Alive)
            {
                ReleaseActorGpuResource(actor);
            }
        }

        _actors.Clear();
        _freeActorSlots.Clear();
        _batchListsDirty = true;
    }
}
