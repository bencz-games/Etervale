using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonModelAnimator
{
    private static readonly ConditionalWeakTable<PristonModel, PreparedRig> PreparedRigs = new();

    private readonly PristonModel _model;
    private readonly PreparedRig _rig;
    private readonly Matrix[] _transforms;
    private readonly int[] _rotationSegments;
    private readonly int[] _positionSegments;
    private readonly int[] _scaleSegments;

    public PristonModelAnimator(PristonModel model)
    {
        _model = model;
        var count = model.Objects.Count;
        _rig = PreparedRigs.GetValue(model, static source => new PreparedRig(source));
        _transforms = new Matrix[count];
        _rotationSegments = new int[count];
        _positionSegments = new int[count];
        _scaleSegments = new int[count];
    }

    public int ObjectCount => _transforms.Length;

    // rotationEnd is the playing motion's last tick: a rotation key past it is not interpolated toward, the key before it
    // holds, so a motion never turns toward the first pose of the motion stored after it. Position and scale keep sampling.
    public Matrix[] Evaluate(float frame, float rotationEnd = float.PositiveInfinity)
    {
        for (var orderIndex = 0; orderIndex < _rig.EvaluationOrder.Length; orderIndex++)
        {
            var index = _rig.EvaluationOrder[orderIndex];
            var obj = _model.Objects[index];
            var parent = _rig.ParentIndices[index];
            if (!obj.IsAnimated || !TrySampleObjectTransform(index, obj, frame, rotationEnd, out var local))
            {
                // Static objects, and animated ones with no keys in the motion segment holding this frame, take their
                // bind pose: stored in absolute space, so relative to the parent's bind transform.
                local = parent < 0 ? obj.Transform : obj.Transform * _rig.ParentInverses[index];
            }

            _transforms[index] = parent < 0 ? local : local * _transforms[parent];
        }

        return _transforms;
    }

    // A skeleton merged from several motion files keeps, per object and channel, the key range of each file. Only the
    // range holding the frame is sampled; a channel with none takes its base value (rotation, position) or no scale.
    private bool TrySampleObjectTransform(int objectIndex, PristonModelObject obj, float frame, float rotationEnd, out Matrix local)
    {
        KeyWindow rotationWindow;
        KeyWindow positionWindow;
        KeyWindow scaleWindow;
        if (obj.RotationSegments.Count > 0)
        {
            rotationWindow = SelectWindow(obj.RotationSegments, obj.RotationKeys, static key => key.Frame, frame);
            positionWindow = SelectWindow(obj.PositionSegments, obj.PositionKeys, static key => key.Frame, frame);
            scaleWindow = SelectWindow(obj.ScaleSegments, obj.ScaleKeys, static key => key.Frame, frame);
            // A scale range that starts at the first key does not make the object active on its own.
            if (rotationWindow.IsEmpty && positionWindow.IsEmpty && (scaleWindow.IsEmpty || scaleWindow.First == 0))
            {
                local = default;
                return false;
            }
        }
        else
        {
            rotationWindow = new KeyWindow(0, obj.RotationKeys.Count);
            positionWindow = new KeyWindow(0, obj.PositionKeys.Count);
            scaleWindow = new KeyWindow(0, obj.ScaleKeys.Count);
        }

        var rotation = SampleRotationMatrix(objectIndex, obj, frame, rotationWindow, rotationEnd);
        var scale = SampleScale(objectIndex, obj.ScaleKeys, frame, scaleWindow);
        local = rotation * Matrix.CreateScale(scale);
        local.Translation = SamplePosition(objectIndex, obj.PositionKeys, frame, positionWindow, obj.BasePosition);
        return true;
    }

    // The keys of the first range holding frame, extended past the range's end until a key reaches it: the sampler brackets
    // with the next key, so a short channel interpolates toward that key before its range ends.
    private static KeyWindow SelectWindow<T>(IReadOnlyList<PristonMotionSegment> segments, IReadOnlyList<T> keys, Func<T, int> frameOf, float frame)
    {
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.KeyCount <= 0 || frame < segment.StartFrame || frame >= segment.EndFrame)
            {
                continue;
            }

            var first = Math.Clamp(segment.KeyIndex, 0, keys.Count);
            var end = Math.Min(keys.Count, first + segment.KeyCount);
            while (end > first && end < keys.Count && frameOf(keys[end - 1]) < segment.EndFrame)
            {
                end++;
            }

            return new KeyWindow(first, end);
        }

        return default;
    }

    // The previous-rotation matrices hold absolute cumulative orientations; interpolating converted deltas would drift.
    private Matrix SampleRotationMatrix(int objectIndex, PristonModelObject obj, float frame, KeyWindow window, float rotationEnd)
    {
        var keys = obj.RotationKeys;
        var prev = obj.PreviousRotationMatrices;
        var quaternions = _rig.RotationQuaternions[objectIndex];
        if (prev.Count == 0)
        {
            return quaternions.Length > 0 ? Matrix.CreateFromQuaternion(quaternions[0]) : obj.BaseRotation;
        }

        var end = Math.Min(window.End, prev.Count);
        if (end <= window.First)
        {
            return obj.BaseRotation;
        }

        if (end - window.First == 1 || frame <= keys[window.First].Frame)
        {
            _rotationSegments[objectIndex] = window.First;
            return prev[window.First];
        }

        var segment = SeekSegment(keys, static key => key.Frame, frame, _rotationSegments[objectIndex], window.First, end);
        _rotationSegments[objectIndex] = segment;
        var next = segment + 1;
        if (frame < keys[next].Frame)
        {
            if (keys[next].Frame > rotationEnd)
            {
                return prev[segment];
            }

            var a = keys[segment];
            var b = keys[next];
            var amount = (frame - a.Frame) / (b.Frame - a.Frame);
            var quaternionIndex = Math.Min(segment, quaternions.Length - 1);
            var qa = quaternions[quaternionIndex];
            var qb = next < quaternions.Length ? quaternions[next] : qa;
            return Matrix.CreateFromQuaternion(Quaternion.Slerp(qa, qb, amount));
        }

        return prev[end - 1];
    }

    private Vector3 SamplePosition(int objectIndex, IReadOnlyList<PristonPositionKey> keys, float frame, KeyWindow window, Vector3 fallback)
    {
        if (window.IsEmpty)
        {
            return fallback;
        }

        if (window.End - window.First == 1 || frame <= keys[window.First].Frame)
        {
            _positionSegments[objectIndex] = window.First;
            return keys[window.First].Position;
        }

        var segment = SeekSegment(keys, static key => key.Frame, frame, _positionSegments[objectIndex], window.First, window.End);
        _positionSegments[objectIndex] = segment;
        var next = segment + 1;
        if (frame < keys[next].Frame)
        {
            var a = keys[segment];
            var b = keys[next];
            var amount = (frame - a.Frame) / (b.Frame - a.Frame);
            return Vector3.Lerp(a.Position, b.Position, amount);
        }

        return keys[window.End - 1].Position;
    }

    private Vector3 SampleScale(int objectIndex, IReadOnlyList<PristonScaleKey> keys, float frame, KeyWindow window)
    {
        if (window.IsEmpty)
        {
            return Vector3.One;
        }

        if (window.End - window.First == 1 || frame <= keys[window.First].Frame)
        {
            _scaleSegments[objectIndex] = window.First;
            return keys[window.First].Scale;
        }

        var segment = SeekSegment(keys, static key => key.Frame, frame, _scaleSegments[objectIndex], window.First, window.End);
        _scaleSegments[objectIndex] = segment;
        var next = segment + 1;
        if (frame < keys[next].Frame)
        {
            var a = keys[segment];
            var b = keys[next];
            var amount = (frame - a.Frame) / (b.Frame - a.Frame);
            return Vector3.Lerp(a.Scale, b.Scale, amount);
        }

        return keys[window.End - 1].Scale;
    }

    // The key pair [segment, segment + 1] bracketing frame within keys[first..end), starting from the last one used.
    private static int SeekSegment<T>(IReadOnlyList<T> keys, Func<T, int> frameOf, float frame, int hint, int first, int end)
    {
        var segment = Math.Clamp(hint, first, end - 2);
        while (segment > first && frame < frameOf(keys[segment]))
        {
            segment--;
        }

        while (segment < end - 2 && frame >= frameOf(keys[segment + 1]))
        {
            segment++;
        }

        return segment;
    }

    private readonly record struct KeyWindow(int First, int End)
    {
        public bool IsEmpty => End <= First;
    }

    private sealed class PreparedRig
    {
        public PreparedRig(PristonModel model)
        {
            var count = model.Objects.Count;
            ParentIndices = new int[count];
            ParentInverses = new Matrix[count];
            RotationQuaternions = new Quaternion[count][];
            var byName = model.ObjectIndexByName();

            for (var index = 0; index < count; index++)
            {
                var obj = model.Objects[index];
                RotationQuaternions[index] = PrepareRotationQuaternions(obj);
                if (obj.ParentName.Length > 0 && byName.TryGetValue(obj.ParentName, out var parent))
                {
                    ParentIndices[index] = parent;
                    ParentInverses[index] = Matrix.Invert(model.Objects[parent].Transform);
                }
                else
                {
                    ParentIndices[index] = -1;
                    ParentInverses[index] = Matrix.Identity;
                }
            }

            EvaluationOrder = BuildEvaluationOrder(ParentIndices, model.Path);
        }

        public int[] ParentIndices { get; }
        public Matrix[] ParentInverses { get; }
        public Quaternion[][] RotationQuaternions { get; }
        public int[] EvaluationOrder { get; }

        private static Quaternion[] PrepareRotationQuaternions(PristonModelObject obj)
        {
            if (obj.PreviousRotationMatrices.Count == 0)
            {
                return obj.RotationKeys.Count == 0 ? [] : [obj.RotationKeys[0].Rotation];
            }

            var quaternions = new Quaternion[obj.PreviousRotationMatrices.Count];
            for (var index = 0; index < quaternions.Length; index++)
            {
                quaternions[index] = Quaternion.CreateFromRotationMatrix(obj.PreviousRotationMatrices[index]);
            }

            return quaternions;
        }

        private static int[] BuildEvaluationOrder(ReadOnlySpan<int> parents, string modelPath)
        {
            var order = new int[parents.Length];
            var state = new byte[parents.Length];
            var count = 0;
            for (var index = 0; index < parents.Length; index++)
            {
                Visit(index, parents, state, order, ref count, modelPath);
            }

            return order;
        }

        private static void Visit(
            int index,
            ReadOnlySpan<int> parents,
            Span<byte> state,
            Span<int> order,
            ref int count,
            string modelPath)
        {
            if (state[index] == 2)
            {
                return;
            }

            if (state[index] == 1)
            {
                throw new InvalidDataException($"Animation hierarchy cycle at object {index} in '{modelPath}'.");
            }

            state[index] = 1;
            var parent = parents[index];
            if (parent >= 0)
            {
                Visit(parent, parents, state, order, ref count, modelPath);
            }

            state[index] = 2;
            order[count] = index;
            count++;
        }
    }
}
