namespace PristonTale.Simulation.Items;

public sealed class Inventory
{
    public const int DefaultWidth = 12;
    public const int DefaultHeight = 6;
    private readonly ItemInstance[] _slots;
    private readonly uint[] _rows;

    public Inventory(int capacity = DefaultWidth * DefaultHeight)
    {
        if (capacity < 1 || capacity > DefaultWidth * DefaultHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _slots = new ItemInstance[capacity];
        Width = Math.Min(DefaultWidth, capacity);
        Height = (capacity + Width - 1) / Width;
        _rows = new uint[Height];
    }

    // A full grid of the given size; occupancy rows are 32-bit masks.
    public Inventory(int width, int height)
    {
        if (width < 1 || width > 32 || height < 1 || height > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        _slots = new ItemInstance[width * height];
        Width = width;
        Height = height;
        _rows = new uint[Height];
    }

    public int Width { get; }
    public int Height { get; }
    public int Capacity => _slots.Length;
    public long Version { get; private set; }
    public ItemInstance this[int index] => _slots[index];
    public event Action Changed;

    // The items with their anchors, in cell order.
    public IEnumerable<(int Anchor, ItemInstance Item)> Occupied()
    {
        for (var anchor = 0; anchor < _slots.Length; anchor++)
        {
            if (_slots[anchor] is { } item)
            {
                yield return (anchor, item);
            }
        }
    }

    public IEnumerable<ItemInstance> Items()
    {
        foreach (var (_, item) in Occupied())
        {
            yield return item;
        }
    }

    // Copies of another grid's items (ids kept) at the same anchors, into this empty grid of the same size.
    public void CopyFrom(Inventory source)
    {
        foreach (var (anchor, item) in source.Occupied())
        {
            TryPlace(item.Copy(item.Count, preserveId: true), anchor);
        }
    }

    public int Find(Guid instanceId)
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            if (_slots[index]?.InstanceId == instanceId)
            {
                return index;
            }
        }

        return -1;
    }

    public bool TryAdd(ItemInstance stack)
    {
        if (stack is null || stack.Count <= 0 || stack.Definition.MaxStack < 1 || Find(stack.InstanceId) >= 0)
        {
            return false;
        }

        var definition = stack.Definition;
        if (!definition.IsStackable && stack.Count != 1)
        {
            return false;
        }

        var remaining = stack.Count;
        foreach (var existing in _slots)
        {
            if (existing is not null && Compatible(existing, stack))
            {
                remaining -= Math.Min(remaining, Math.Max(0, definition.MaxStack - existing.Count));
            }
        }

        Span<uint> plannedRows = stackalloc uint[Height];
        _rows.CopyTo(plannedRows);
        Span<int> anchors = stackalloc int[Capacity];
        var planned = 0;
        while (remaining > 0)
        {
            var anchor = FindSpace(definition, plannedRows);
            if (anchor < 0)
            {
                return false;
            }

            anchors[planned++] = anchor;
            SetFootprint(plannedRows, anchor, definition, true);
            remaining -= Math.Min(remaining, definition.MaxStack);
        }

        remaining = stack.Count;
        foreach (var existing in _slots)
        {
            if (existing is not null && Compatible(existing, stack))
            {
                var amount = Math.Min(remaining, Math.Max(0, definition.MaxStack - existing.Count));
                existing.Count += amount;
                remaining -= amount;
            }
        }

        for (var index = 0; index < planned; index++)
        {
            var amount = Math.Min(remaining, definition.MaxStack);
            var item = stack.Copy(amount, preserveId: index == 0);
            _slots[anchors[index]] = item;
            remaining -= amount;
        }

        plannedRows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    // True when an item of this footprint would fit somewhere.
    public bool HasRoom(int width, int height) => FindSpace(new ItemDefinition(0, "", ItemKind.Misc, GridWidth: width, GridHeight: height), _rows) >= 0;

    public bool TryPlace(ItemInstance item, int anchor)
    {
        if (item is null || item.Count <= 0 || item.Count > item.Definition.MaxStack || Find(item.InstanceId) >= 0
            || !Fits(anchor, item.Definition, _rows))
        {
            return false;
        }

        _slots[anchor] = item.Copy(item.Count, preserveId: true);
        SetFootprint(_rows, anchor, item.Definition, true);
        NotifyChanged();
        return true;
    }

    public bool TryMove(int from, int to)
    {
        if (from < 0 || from >= Capacity || _slots[from] is not { } item)
        {
            return false;
        }

        Span<uint> rows = stackalloc uint[Height];
        _rows.CopyTo(rows);
        SetFootprint(rows, from, item.Definition, false);
        if (!Fits(to, item.Definition, rows))
        {
            return false;
        }

        SetFootprint(rows, to, item.Definition, true);
        _slots[from] = null;
        _slots[to] = item;
        rows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    // Covered returns this when an item set down would lie over more than one item.
    public const int Several = -2;

    // The anchor of the one item an item of this footprint set down at `anchor` would lie over (except the item at
    // `except`, the one being moved): -1 on free cells, Several over more than one.
    public int Covered(int anchor, ItemDefinition definition, int except = -1)
    {
        var covered = -1;
        var x = anchor % Width;
        var y = anchor / Width;
        for (var index = 0; index < Capacity; index++)
        {
            if (index == except || _slots[index] is not { } other || !Overlaps(index, other.Definition, x, y, definition))
            {
                continue;
            }

            if (covered >= 0)
            {
                return Several;
            }

            covered = index;
        }

        return covered;
    }

    // Sets the item at `from` down with its top-left at `to` over exactly one other item, which moves to where the first
    // one was, or to the first free space: the bag side of the original's exchange, where the covered item is picked up.
    public bool TrySwap(int from, int to)
    {
        if (from < 0 || from >= Capacity || _slots[from] is not { } item || to < 0 || to >= Capacity
            || Covered(to, item.Definition, from) is var covered && covered < 0)
        {
            return false;
        }

        var displaced = _slots[covered];
        Span<uint> rows = stackalloc uint[Height];
        _rows.CopyTo(rows);
        SetFootprint(rows, from, item.Definition, false);
        SetFootprint(rows, covered, displaced.Definition, false);
        if (!Fits(to, item.Definition, rows))
        {
            return false;
        }

        SetFootprint(rows, to, item.Definition, true);
        var destination = Destination(rows, from, displaced.Definition);
        if (destination < 0)
        {
            return false;
        }

        SetFootprint(rows, destination, displaced.Definition, true);
        _slots[from] = null;
        _slots[covered] = null;
        _slots[to] = item;
        _slots[destination] = displaced;
        rows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    // Whether TryExchange would succeed, changing nothing.
    public bool CanExchange(int anchor, ItemInstance replacement, int at = -1)
    {
        Span<uint> rows = stackalloc uint[Height];
        return PlanExchange(rows, anchor, replacement, at) >= 0;
    }

    // Takes the item at `anchor` out and sets `replacement` (when given) at `at`, or with `at` -1 where the item was or in
    // the first free space. Nothing changes when the replacement does not fit.
    public bool TryExchange(int anchor, ItemInstance replacement, int at = -1)
    {
        Span<uint> rows = stackalloc uint[Height];
        var destination = PlanExchange(rows, anchor, replacement, at);
        if (destination < 0)
        {
            return false;
        }

        _slots[anchor] = null;
        if (replacement is not null)
        {
            _slots[destination] = replacement.Copy(replacement.Count, preserveId: true);
        }

        rows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    // The rows after the exchange and where the replacement goes (the anchor when there is none); -1 when it fails.
    private int PlanExchange(Span<uint> rows, int anchor, ItemInstance replacement, int at)
    {
        if (anchor < 0 || anchor >= Capacity || _slots[anchor] is not { } outgoing
            || replacement is not null && (replacement.Count < 1 || replacement.Count > replacement.Definition.MaxStack || Find(replacement.InstanceId) >= 0))
        {
            return -1;
        }

        _rows.CopyTo(rows);
        SetFootprint(rows, anchor, outgoing.Definition, false);
        if (replacement is null)
        {
            return anchor;
        }

        var destination = at >= 0 ? (Fits(at, replacement.Definition, rows) ? at : -1) : Destination(rows, anchor, replacement.Definition);
        if (destination >= 0)
        {
            SetFootprint(rows, destination, replacement.Definition, true);
        }

        return destination;
    }

    // Places an item, without joining stacks, at the first cells that fit.
    public bool TryPlaceFirstFree(ItemInstance item) => item is not null && FindSpace(item.Definition, _rows) is var anchor && anchor >= 0 && TryPlace(item, anchor);

    public bool TrySplit(int from, int to, int amount)
    {
        if (from < 0 || from >= Capacity || _slots[from] is not { } item || !item.Definition.IsStackable
            || amount <= 0 || amount >= item.Count || !Fits(to, item.Definition, _rows))
        {
            return false;
        }

        _slots[to] = item.Split(amount);
        SetFootprint(_rows, to, item.Definition, true);
        NotifyChanged();
        return true;
    }

    public bool TryMerge(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Capacity || to >= Capacity
            || _slots[from] is not { } source || _slots[to] is not { } destination || !Compatible(source, destination))
        {
            return false;
        }

        var amount = Math.Min(source.Count, destination.Definition.MaxStack - destination.Count);
        if (amount <= 0)
        {
            return false;
        }

        destination.Count += amount;
        source.Count -= amount;
        if (source.Count == 0)
        {
            SetFootprint(_rows, from, source.Definition, false);
            _slots[from] = null;
        }

        NotifyChanged();
        return true;
    }

    // Takes `amount` of the item at `index` out of the bag: the item itself when that is all of it, otherwise a new stack
    // split from it. Null when there is no such item or amount.
    public ItemInstance TakeFrom(int index, int amount)
    {
        if (index < 0 || index >= Capacity || _slots[index] is not { } item || amount < 1 || amount > item.Count)
        {
            return null;
        }

        if (amount == item.Count)
        {
            return TakeAt(index);
        }

        var taken = item.Split(amount);
        NotifyChanged();
        return taken;
    }

    // Adds all of a stack to the compatible stack at `anchor`, when the stack limit allows it.
    public bool TryJoin(int anchor, ItemInstance item)
    {
        if (anchor < 0 || anchor >= Capacity || _slots[anchor] is not { } there || !Compatible(there, item)
            || there.Count + item.Count > there.Definition.MaxStack)
        {
            return false;
        }

        there.Count += item.Count;
        NotifyChanged();
        return true;
    }

    public ItemInstance TakeAt(int index)
    {
        if (index < 0 || index >= Capacity || _slots[index] is not { } item)
        {
            return null;
        }

        SetFootprint(_rows, index, item.Definition, false);
        _slots[index] = null;
        NotifyChanged();
        return item;
    }

    public void RemoveAt(int index)
    {
        TakeAt(index);
    }

    public bool ConsumeAt(int index)
    {
        if (index < 0 || index >= Capacity || _slots[index] is not { } item)
        {
            return false;
        }

        if (item.Count == 1)
        {
            TakeAt(index);
        }
        else
        {
            item.Count--;
            NotifyChanged();
        }

        return true;
    }

    // Where an item moved aside goes, as the original's exchange puts it: back at `preferred`, or the first free space.
    private int Destination(ReadOnlySpan<uint> rows, int preferred, ItemDefinition definition) =>
        Fits(preferred, definition, rows) ? preferred : FindSpace(definition, rows);

    private int FindSpace(ItemDefinition definition, ReadOnlySpan<uint> rows) =>
        ItemGrid.FirstFit(rows[..Height], Width, Capacity, definition.GridWidth, definition.GridHeight);

    private bool Fits(int anchor, ItemDefinition definition, ReadOnlySpan<uint> rows) =>
        ItemGrid.Fits(rows[..Height], Width, Capacity, anchor, definition.GridWidth, definition.GridHeight);

    private bool Overlaps(int anchor, ItemDefinition definition, int x, int y, ItemDefinition other)
    {
        var left = anchor % Width;
        var top = anchor / Width;
        return left < x + other.GridWidth && x < left + definition.GridWidth && top < y + other.GridHeight && y < top + definition.GridHeight;
    }

    private void SetFootprint(Span<uint> rows, int anchor, ItemDefinition definition, bool occupied)
    {
        var x = anchor % Width;
        var y = anchor / Width;
        var mask = ((1u << definition.GridWidth) - 1u) << x;
        for (var row = y; row < y + definition.GridHeight; row++)
        {
            rows[row] = occupied ? rows[row] | mask : rows[row] & ~mask;
        }
    }

    private void NotifyChanged()
    {
        Version++;
        Changed?.Invoke();
    }

    private static bool Compatible(ItemInstance left, ItemInstance right)
    {
        return left.Definition.IsStackable && left.Definition == right.Definition
            && left.UpgradeLevel == right.UpgradeLevel && left.DurabilityCurrent == right.DurabilityCurrent
            && left.DurabilityMax == right.DurabilityMax && left.Roll == right.Roll && left.Maturity == right.Maturity
            && left.MixSheltom == right.MixSheltom && left.MixStats == right.MixStats;
    }
}
