using System.Numerics;
using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

// Uniform planar buckets over one field snapshot. Entity indices are sorted by cell key so a query is a binary
// search per overlapped column; the original server instead scanned every connection and monster slot. It also indexes,
// once per tick for every receiver, who targets or owns whom and where the NPCs are. A field's presence frames rebuild
// their grid in place every tick.
public sealed class EntityGrid
{
    // Small cells let a receiver in a dense crowd count its neighbourhood without collecting the whole crowd.
    public const float CellSize = 64f;
    private long[] _keys;
    private int[] _indices;
    private readonly Dictionary<ulong, int> _byId;
    // (related entity id << 64 | index) of every entity that targets or is owned by another one, sorted, so the entities
    // related to one id are one contiguous run.
    private UInt128[] _relations = [];
    private int _relationCount;
    private readonly List<int> _npcs = new();

    private EntityGrid(int capacity)
    {
        _keys = new long[capacity];
        _indices = new int[capacity];
        _byId = new Dictionary<ulong, int>(capacity);
    }

    public static EntityGrid Build(EntityView[] entities)
    {
        var grid = new EntityGrid(entities.Length);
        grid.Rebuild(entities);
        return grid;
    }

    public void Rebuild(EntityView[] entities)
    {
        if (_keys.Length != entities.Length)
        {
            _keys = new long[entities.Length];
            _indices = new int[entities.Length];
        }

        _byId.Clear();
        _byId.EnsureCapacity(entities.Length);
        _npcs.Clear();
        _relationCount = 0;
        for (var index = 0; index < entities.Length; index++)
        {
            ref readonly var entity = ref entities[index];
            _keys[index] = KeyOf(entity.Position);
            _indices[index] = index;
            _byId[entity.EntityId] = index;
            if (entity.Kind == EntityKind.Npc)
            {
                _npcs.Add(index);
            }

            AddRelation(entity.AttackTargetId, index);
            AddRelation(entity.OwnerId, index);
        }

        Array.Sort(_keys, _indices);
        Array.Sort(_relations, 0, _relationCount);
    }

    public int Count => _keys.Length;

    // Indices of the NPCs of the snapshot.
    public IReadOnlyList<int> Npcs => _npcs;

    // Appends the indices of the entities that target or are owned by the given one.
    public void Related(ulong entityId, List<int> results)
    {
        var first = (UInt128)entityId << 64;
        var position = Array.BinarySearch(_relations, 0, _relationCount, first);
        position = position < 0 ? ~position : position;
        while (position < _relationCount && (ulong)(_relations[position] >> 64) == entityId)
        {
            results.Add((int)(ulong)_relations[position]);
            position++;
        }
    }

    // The number of entities in the cells the square overlaps, without collecting them.
    public int CountInSquare(Vector3 center, float halfSize)
    {
        var minZ = Cell(center.Z - halfSize);
        var maxZ = Cell(center.Z + halfSize);
        var count = 0;
        for (var x = Cell(center.X - halfSize); x <= Cell(center.X + halfSize); x++)
        {
            count += LowerBound(Key(x, maxZ) + 1) - LowerBound(Key(x, minZ));
        }

        return count;
    }

    private void AddRelation(ulong related, int index)
    {
        if (related == 0)
        {
            return;
        }

        if (_relationCount == _relations.Length)
        {
            Array.Resize(ref _relations, Math.Max(16, _relations.Length * 2));
        }

        _relations[_relationCount++] = (UInt128)related << 64 | (uint)index;
    }

    public bool TryGetIndex(ulong entityId, out int index) => _byId.TryGetValue(entityId, out index);

    // Appends indices of entities in cells overlapping the square around center; callers apply the exact radius.
    public void Query(Vector3 center, float radius, List<int> results)
    {
        var minX = Cell(center.X - radius);
        var maxX = Cell(center.X + radius);
        var minZ = Cell(center.Z - radius);
        var maxZ = Cell(center.Z + radius);
        for (var x = minX; x <= maxX; x++)
        {
            // Cells of one column are adjacent in key order: one search finds the column's first cell in range.
            var position = LowerBound(Key(x, minZ));
            var last = Key(x, maxZ);
            while (position < _keys.Length && _keys[position] <= last)
            {
                results.Add(_indices[position]);
                position++;
            }
        }
    }

    private int LowerBound(long key)
    {
        int low = 0;
        int high = _keys.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (_keys[middle] < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int Cell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

    // The sign flip keeps keys in coordinate order, so the cells of a column are contiguous across zero.
    private static long Key(int x, int z) => (long)x << 32 | (uint)(z ^ int.MinValue);

    private static long KeyOf(Vector3 position) => Key(Cell(position.X), Cell(position.Z));
}
