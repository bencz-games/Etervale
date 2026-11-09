using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering.Gltf;

// A glTF actor's timeline (GltfActorSource) posed at a tick: the clip whose range holds the frame plays at the seconds
// since its start (before the first clip, or with none, the rest pose); the transforms are the nodes' worlds, then every
// skin's joint matrices, in actor space.
internal sealed class GltfTimelineAnimator : IActorAnimator
{
    private readonly GltfActorSource _source;
    private readonly GltfAnimator _animator;
    private readonly Matrix[] _transforms;

    public GltfTimelineAnimator(GltfActorSource source)
    {
        _source = source;
        _animator = new GltfAnimator(source.Model);
        _transforms = new Matrix[source.TransformCount];
    }

    public int ObjectCount => _transforms.Length;

    public Matrix[] Evaluate(float frame, float rotationEnd)
    {
        var starts = _source.ClipStartFrames;
        var clip = -1;
        for (var index = 0; index < starts.Length && starts[index] * GameUnits.FrameTicks <= frame; index++)
        {
            clip = index;
        }

        var seconds = clip < 0 ? 0f : (frame - starts[clip] * GameUnits.FrameTicks) / GameUnits.FrameTicksPerSecond;
        _animator.Pose(clip, seconds);
        var root = GltfActorSource.Root;
        var nodes = _animator.NodeWorld;
        for (var node = 0; node < nodes.Length; node++)
        {
            _transforms[node] = nodes[node] * root;
        }

        var skins = _source.Model.Skins;
        for (var skin = 0; skin < skins.Count; skin++)
        {
            var joints = _transforms.AsSpan(_source.SkinOffsets[skin], skins[skin].Joints.Length);
            _animator.SkinMatrices(skin, joints);
            for (var joint = 0; joint < joints.Length; joint++)
            {
                joints[joint] *= root;
            }
        }

        return _transforms;
    }
}
