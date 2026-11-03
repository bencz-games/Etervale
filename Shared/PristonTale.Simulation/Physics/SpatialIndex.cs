using System.Numerics;

namespace PristonTale.Simulation.Physics;

public readonly record struct SpatialEntry(ulong Id, Vector3 Position, int Order);

public sealed class SpatialIndex
{
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Z), List<SpatialEntry>> _cells = new();
    private readonly Dictionary<ulong, (int X, int Z)> _locations = new();
    private readonly Stack<List<SpatialEntry>> _pool = new();

    public SpatialIndex(float cellSize = 256f)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _cellSize = cellSize;
    }

    public void Clear()
    {
        foreach (var cell in _cells.Values)
        {
            cell.Clear();
            _pool.Push(cell);
        }

        _cells.Clear();
        _locations.Clear();
    }

    public void Put(ulong id, Vector3 position, int order = -1)
    {
        var key = Cell(position);
        if (_locations.TryGetValue(id, out var previousKey))
        {
            var previous = _cells[previousKey];
            for (var index = 0; index < previous.Count; index++)
            {
                if (previous[index].Id != id)
                {
                    continue;
                }

                order = previous[index].Order;
                if (previousKey == key)
                {
                    previous[index] = new SpatialEntry(id, position, order);
                    return;
                }

                previous.RemoveAt(index);
                if (previous.Count == 0)
                {
                    _cells.Remove(previousKey);
                    _pool.Push(previous);
                }

                break;
            }
        }

        if (!_cells.TryGetValue(key, out var bucket))
        {
            bucket = _pool.TryPop(out var pooled) ? pooled : new List<SpatialEntry>();
            _cells.Add(key, bucket);
        }

        bucket.Add(new SpatialEntry(id, position, order));
        _locations[id] = key;
    }

    // Every entry of the cells the square around the circle overlaps, in insertion order (a broad phase).
    public void Query(Vector3 center, float radius, List<SpatialEntry> results) => Query(center, radius, results, within: false);

    // Only the entries inside the planar circle, in insertion order. In a crowd the overlapped cells hold far more than the
    // circle, and ordering all of them for every mover dominated the field step.
    public void QueryWithin(Vector3 center, float radius, List<SpatialEntry> results) => Query(center, radius, results, within: true);

    private void Query(Vector3 center, float radius, List<SpatialEntry> results, bool within)
    {
        results.Clear();
        if (!float.IsFinite(radius) || radius < 0f || !float.IsFinite(center.X) || !float.IsFinite(center.Z))
        {
            return;
        }

        var minimum = Cell(center - new Vector3(radius, 0, radius));
        var maximum = Cell(center + new Vector3(radius, 0, radius));
        var visits = ((long)maximum.X - minimum.X + 1) * ((long)maximum.Z - minimum.Z + 1);
        var radiusSq = within ? radius * radius : float.PositiveInfinity;
        if (visits > _cells.Count * 2L)
        {
            foreach (var cell in _cells)
            {
                if (cell.Key.X >= minimum.X && cell.Key.X <= maximum.X && cell.Key.Z >= minimum.Z && cell.Key.Z <= maximum.Z)
                {
                    AddWithin(cell.Value, center, radiusSq, results);
                }
            }
        }
        else
        {
            for (var z = minimum.Z; z <= maximum.Z; z++)
            {
                for (var x = minimum.X; x <= maximum.X; x++)
                {
                    if (_cells.TryGetValue((x, z), out var bucket))
                    {
                        AddWithin(bucket, center, radiusSq, results);
                    }
                }
            }
        }

        if (results.Count > 1)
        {
            results.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        }
    }

    // True when any entry lies inside the planar circle, without collecting or ordering the entries.
    public bool AnyWithin(Vector3 center, float radius)
    {
        var minimum = Cell(center - new Vector3(radius, 0, radius));
        var maximum = Cell(center + new Vector3(radius, 0, radius));
        var radiusSq = radius * radius;
        for (var z = minimum.Z; z <= maximum.Z; z++)
        {
            for (var x = minimum.X; x <= maximum.X; x++)
            {
                if (!_cells.TryGetValue((x, z), out var bucket))
                {
                    continue;
                }

                foreach (var entry in bucket)
                {
                    if (Planar.DistanceSquared(entry.Position, center) < radiusSq)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void AddWithin(List<SpatialEntry> bucket, Vector3 center, float radiusSq, List<SpatialEntry> results)
    {
        foreach (var entry in bucket)
        {
            if (Planar.DistanceSquared(entry.Position, center) <= radiusSq)
            {
                results.Add(entry);
            }
        }
    }

    private (int X, int Z) Cell(Vector3 position)
    {
        // Saturated edge buckets preserve candidates while bounding hostile or corrupt coordinates/radii.
        const int cellLimit = 1_000_000;
        return ((int)Math.Clamp(MathF.Floor(position.X / _cellSize), -cellLimit, cellLimit),
            (int)Math.Clamp(MathF.Floor(position.Z / _cellSize), -cellLimit, cellLimit));
    }
}
