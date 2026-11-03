namespace PristonTale.Server.Net;

// The most overdue position updates of one receiver's tick. The movement budget fits at most Capacity entries, so only
// that many are kept (a bounded min-heap) instead of sorting every due entity of a crowd.
internal sealed class MotionCandidates(int capacity)
{
    private readonly (int Index, float Priority)[] _heap = new (int, float)[capacity];
    private int _count;

    public void Clear() => _count = 0;

    public void Add(int index, float priority)
    {
        if (_count < _heap.Length)
        {
            _heap[_count] = (index, priority);
            SiftUp(_count++);
            return;
        }

        if (priority <= _heap[0].Priority)
        {
            return;
        }

        _heap[0] = (index, priority);
        SiftDown(0);
    }

    // Most overdue first.
    public ReadOnlySpan<(int Index, float Priority)> Ordered()
    {
        var span = _heap.AsSpan(0, _count);
        span.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
        return span;
    }

    private void SiftUp(int position)
    {
        while (position > 0)
        {
            var parent = (position - 1) / 2;
            if (_heap[parent].Priority <= _heap[position].Priority)
            {
                return;
            }

            (_heap[parent], _heap[position]) = (_heap[position], _heap[parent]);
            position = parent;
        }
    }

    private void SiftDown(int position)
    {
        while (true)
        {
            var smallest = position;
            var left = position * 2 + 1;
            var right = left + 1;
            if (left < _count && _heap[left].Priority < _heap[smallest].Priority)
            {
                smallest = left;
            }

            if (right < _count && _heap[right].Priority < _heap[smallest].Priority)
            {
                smallest = right;
            }

            if (smallest == position)
            {
                return;
            }

            (_heap[smallest], _heap[position]) = (_heap[position], _heap[smallest]);
            position = smallest;
        }
    }
}
