using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats.Gltf;

namespace PristonTale.Engine3D.Rendering.Gltf;

// Poses a glTF model's nodes at a time of one of its animations, allocating nothing per pose. A channel samples its
// keys as glTF defines (step holds the earlier key; linear interpolates, rotations by the shortest slerp; a cubic
// spline is a Hermite curve whose tangents scale by the key interval); before the first key the first holds, after the
// last the last. Transforms the clip does not animate keep the node's rest value. Node worlds are in model space; a
// skin matrix takes a bind-space vertex to model space (inverse bind x joint world).
public sealed class GltfAnimator
{
    private readonly GltfModel _model;
    private readonly int[] _order;
    private readonly Vector3[] _translation;
    private readonly Quaternion[] _rotation;
    private readonly Vector3[] _scale;
    private readonly Matrix[] _world;
    // Per clip, per node, the clip's channel for each path (-1: none).
    private readonly int[][][] _channels;

    public GltfAnimator(GltfModel model)
    {
        _model = model;
        var count = model.Nodes.Count;
        _order = ParentsFirst(model.Nodes);
        _translation = new Vector3[count];
        _rotation = new Quaternion[count];
        _scale = new Vector3[count];
        _world = new Matrix[count];
        _channels = new int[model.Animations.Count][][];
        for (var clip = 0; clip < _channels.Length; clip++)
        {
            var byNode = new int[count][];
            for (var node = 0; node < count; node++)
            {
                byNode[node] = [-1, -1, -1];
            }

            var channels = model.Animations[clip].Channels;
            for (var index = 0; index < channels.Count; index++)
            {
                byNode[channels[index].Node][(int)channels[index].Path] = index;
            }

            _channels[clip] = byNode;
        }

        Pose(-1, 0f);
    }

    public ReadOnlySpan<Matrix> NodeWorld => _world;

    // clip -1 (or past the model's clips) poses the rest.
    public void Pose(int clip, float seconds)
    {
        var animated = clip >= 0 && clip < _channels.Length;
        for (var node = 0; node < _world.Length; node++)
        {
            var rest = _model.Nodes[node];
            _translation[node] = rest.Translation;
            _rotation[node] = rest.Rotation;
            _scale[node] = rest.Scale;
            if (!animated)
            {
                continue;
            }

            var paths = _channels[clip][node];
            var channels = _model.Animations[clip].Channels;
            if (paths[(int)GltfChannelPath.Translation] is var t and >= 0)
            {
                var value = Sample(channels[t], seconds);
                _translation[node] = new Vector3(value.X, value.Y, value.Z);
            }

            if (paths[(int)GltfChannelPath.Rotation] is var r and >= 0)
            {
                var value = Sample(channels[r], seconds);
                _rotation[node] = new Quaternion(value.X, value.Y, value.Z, value.W);
            }

            if (paths[(int)GltfChannelPath.Scale] is var s and >= 0)
            {
                var value = Sample(channels[s], seconds);
                _scale[node] = new Vector3(value.X, value.Y, value.Z);
            }
        }

        foreach (var node in _order)
        {
            var local = Matrix.CreateScale(_scale[node]) * Matrix.CreateFromQuaternion(_rotation[node]) * Matrix.CreateTranslation(_translation[node]);
            var parent = _model.Nodes[node].Parent;
            _world[node] = parent < 0 ? local : local * _world[parent];
        }
    }

    // destination[j] for each joint j of the skin.
    public void SkinMatrices(int skin, Span<Matrix> destination)
    {
        var joints = _model.Skins[skin].Joints;
        var inverseBinds = _model.Skins[skin].InverseBinds;
        for (var joint = 0; joint < joints.Length; joint++)
        {
            destination[joint] = inverseBinds[joint] * _world[joints[joint]];
        }
    }

    private static Vector4 Sample(GltfChannel channel, float seconds)
    {
        var times = channel.Times;
        var cubic = channel.Interpolation == GltfInterpolation.CubicSpline;
        if (seconds <= times[0] || times.Length == 1)
        {
            return channel.Values[cubic ? 1 : 0];
        }

        var last = times.Length - 1;
        if (seconds >= times[last])
        {
            return channel.Values[cubic ? last * 3 + 1 : last];
        }

        var next = Array.BinarySearch(times, seconds);
        next = next >= 0 ? next + 1 : ~next;
        var key = next - 1;
        if (channel.Interpolation == GltfInterpolation.Step)
        {
            return channel.Values[key];
        }

        var interval = times[next] - times[key];
        var t = (seconds - times[key]) / interval;
        var rotation = channel.Path == GltfChannelPath.Rotation;
        if (!cubic)
        {
            var (a, b) = (channel.Values[key], channel.Values[next]);
            return rotation ? Slerp(a, b, t) : Vector4.Lerp(a, b, t);
        }

        var t2 = t * t;
        var t3 = t2 * t;
        var value = (2f * t3 - 3f * t2 + 1f) * channel.Values[key * 3 + 1]
            + (t3 - 2f * t2 + t) * interval * channel.Values[key * 3 + 2]
            + (-2f * t3 + 3f * t2) * channel.Values[next * 3 + 1]
            + (t3 - t2) * interval * channel.Values[next * 3];
        return rotation ? Vector4.Normalize(value) : value;
    }

    private static Vector4 Slerp(Vector4 a, Vector4 b, float t)
    {
        var rotation = Quaternion.Slerp(new Quaternion(a.X, a.Y, a.Z, a.W), new Quaternion(b.X, b.Y, b.Z, b.W), t);
        return new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
    }

    private static int[] ParentsFirst(IReadOnlyList<GltfNode> nodes)
    {
        var order = new List<int>(nodes.Count);
        var placed = new bool[nodes.Count];
        void Place(int node)
        {
            if (placed[node])
            {
                return;
            }

            if (nodes[node].Parent >= 0)
            {
                Place(nodes[node].Parent);
            }

            placed[node] = true;
            order.Add(node);
        }

        for (var node = 0; node < nodes.Count; node++)
        {
            Place(node);
        }

        return [.. order];
    }
}
