using System.Numerics;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

public sealed class FieldGroundLoot(ItemCatalog catalog, FieldTerrain? terrain = null)
{
    public const float PickupReach = 32f;
    public const int Capacity = 1024;
    // Drops fall on the cells of a square ring around where they came from, CellSpacing units apart, the nearest ring
    // first: north, then clockwise round the eight cells.
    public const float CellSpacing = 24f;
    private const int Rings = 3;
    private static readonly Vector2[] RingCells =
    [
        new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1)
    ];

    private readonly FieldTerrain _terrain = terrain ?? FieldTerrain.None;
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly List<Guid> _expired = [];
    private GroundDropState[] _snapshot = [];
    private bool _dirty;
    private long _nowMs;
    private int _turn;

    public int Count => _entries.Count;

    // Each dropped item is made when it falls: its values are rolled from the item's ranges with the field's stream.
    public void Place(KillPayout payout, Vector3 position, bool publicDrop, long nowMs, ISimRng rng)
    {
        foreach (var drop in payout.Drops)
        {
            if (_entries.Count >= Capacity)
            {
                break;
            }

            ItemDefinition? definition = null;
            if (drop.Kind == DropKind.Item && !catalog.TryGet(drop.ItemCode, out definition))
            {
                throw new InvalidDataException($"Unresolved rolled item {drop.ItemCode}.");
            }

            if (drop.ToInventory)
            {
                throw new NotSupportedException("Quest/event inventory delivery requires its own durable reward transaction.");
            }

            bool gold = drop.Kind == DropKind.Gold;
            var id = Guid.NewGuid();
            var spot = Spot(position);
            // A sealed puzzle piece lies on the ground as the lucky bag.
            var view = drop.Sealed
                ? new GroundDropView(id, ItemCodes.Parse(Puzzles.LuckyBagIcon), Puzzles.LuckyBagIcon, Puzzles.LuckyBagName, drop.Amount, false, spot)
                : new GroundDropView(id, drop.ItemCode, gold ? ItemCodes.GoldKey : definition!.CatalogKey, gold ? ItemCodes.GoldName : definition!.Name, drop.Amount, gold, spot);
            var item = gold ? null : ItemRoller.Create(definition!, drop.Amount, rng);
            if (item is not null)
            {
                item.Sealed = drop.Sealed;
            }

            _entries.Add(id, new Entry(view, item, payout.CreditedPlayerId, publicDrop ? nowMs : nowMs + OwnedDropMs, nowMs + LifetimeMs(definition), NoThrower)
            {
                FromMonster = true
            });
            _dirty = true;
        }
    }

    // An item copy or gold a game master made at a position; it is public at once and lasts as a monster drop does.
    public bool PlaceCreated(ItemInstance? item, int gold, Vector3 position, ulong ownerId, long nowMs)
    {
        if (_entries.Count >= Capacity || item is null && gold <= 0)
        {
            return false;
        }

        Add(item, gold, position, ownerId, nowMs);
        return true;
    }

    // One unit of a catalog item made at a position for anyone, as a monster drop lasts (none for an item the catalog
    // lacks).
    public bool PlaceItem(uint code, Vector3 position, long nowMs, ISimRng rng) =>
        catalog.TryGet(code, out var definition) && PlaceCreated(ItemRoller.Create(definition, 1, rng), 0, position, 0, nowMs);

    // The character id a drop nobody threw names (ThrownBy).
    public const long NoThrower = 0;

    // An item or gold a player threw, laid once its removal is saved: the room was checked when the throw was prepared, so
    // the few throws in flight may pass the capacity rather than lose what was saved. Public at once, and remembered by
    // the thrower's character id so its pickup names where it came from.
    public Guid PlaceThrown(ItemInstance? item, int gold, Vector3 position, ulong ownerId, long nowMs, long thrownBy) =>
        Add(item, gold, position, ownerId, nowMs, thrownBy);

    private Guid Add(ItemInstance? item, int gold, Vector3 position, ulong ownerId, long nowMs, long thrownBy = NoThrower)
    {
        var id = Guid.NewGuid();
        var spot = Spot(position);
        var view = item is null
            ? new GroundDropView(id, 0u, ItemCodes.GoldKey, ItemCodes.GoldName, gold, true, spot)
            : new GroundDropView(id, item.Definition.Code, item.Definition.CatalogKey, item.Definition.Name, item.Count, false, spot);
        _entries.Add(id, new Entry(view, item, ownerId, nowMs, nowMs + LifetimeMs(item?.Definition), thrownBy));
        _dirty = true;
        return id;
    }

    // The character id that threw a drop; NoThrower for a monster's drop or one that is gone.
    public long ThrownBy(Guid id) => _entries.TryGetValue(id, out var entry) ? entry.ThrownBy : NoThrower;

    // Whether a drop came from a monster's kill (Place); false for a thrown or created one, or one that is gone.
    public bool FromMonster(Guid id) => _entries.TryGetValue(id, out var entry) && entry.FromMonster;

    // Where a drop from the origin (a corpse, the thrower's feet) lies, as the original lays drops: on a ring cell taken in
    // turn with one counter for the whole field, whose floor is within DropTolerance of the origin. The original meant to
    // skip a cell another drop holds but never did, so a ninth drop fell on the first; here a held cell, or one behind a
    // wall, is passed over, and when the nearest ring is full the next one out is tried. With every cell held the first
    // cell with floor is taken, and with none the origin settled on its floor.
    private Vector3 Spot(Vector3 origin)
    {
        Vector3? fallback = null;
        for (var ring = 1; ring <= Rings; ring++)
        {
            for (var tries = 0; tries < RingCells.Length; tries++)
            {
                _turn++;
                var cell = RingCells[_turn & (RingCells.Length - 1)] * (CellSpacing * ring);
                var candidate = origin + new Vector3(cell.X, 0f, cell.Y);
                if (!_terrain.TryFloorNear(candidate, FieldTerrain.DropTolerance, out var height)
                    || MathF.Abs(height - origin.Y) >= FieldTerrain.DropTolerance)
                {
                    continue;
                }

                candidate.Y = height;
                if (_terrain.CrossesWall(origin, candidate))
                {
                    continue;
                }

                if (!Held(candidate))
                {
                    return candidate;
                }

                fallback ??= candidate;
            }
        }

        return fallback ?? _terrain.SettleNear(origin, FieldTerrain.DropTolerance);
    }

    // A drop lies within half a cell of the point.
    private bool Held(Vector3 point)
    {
        const float reach = CellSpacing / 2f;
        foreach (var entry in _entries.Values)
        {
            var delta = entry.View.Position - point;
            if (delta.X * delta.X + delta.Z * delta.Z < reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    public void Step(long nowMs)
    {
        _nowMs = nowMs;
        _expired.Clear();
        foreach (var (id, entry) in _entries)
        {
            if (entry.Reservation == Guid.Empty && nowMs >= entry.ExpiresAt)
            {
                _expired.Add(id);
            }
            else if (!entry.Public && nowMs >= entry.PublicAt)
            {
                entry.Public = true;
                _dirty = true;
            }
        }

        foreach (var id in _expired)
        {
            RemoveEntry(id);
        }
    }

    public GroundDropState[] Snapshot()
    {
        if (_dirty)
        {
            _snapshot = _entries.Values.Select(entry => new GroundDropState(entry.View, entry.OwnerId, entry.Public)).ToArray();
            _dirty = false;
        }

        return _snapshot;
    }

    public bool TryReserve(Guid id, Guid operation, WorldPlayer player, out GroundDropView? view, out ItemInstance? item)
    {
        view = null;
        item = null;
        if (operation == Guid.Empty || player.IsDead || !_entries.TryGetValue(id, out var entry)
            || _nowMs >= entry.ExpiresAt || entry.Reservation != Guid.Empty || (!entry.Public && entry.OwnerId != player.EntityId))
        {
            return false;
        }

        var delta = player.Position - entry.View.Position;
        if (delta.X * delta.X + delta.Z * delta.Z > PickupReach * PickupReach)
        {
            return false;
        }

        entry.Reservation = operation;
        view = entry.View;
        if (entry.Item is not null)
        {
            item = entry.Item.Copy(entry.Item.Count, entry.View.Id);
        }

        return true;
    }

    public void Resolve(Guid id, Guid operation, bool committed)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.Reservation != operation)
        {
            throw new CommitInvariantException("Ground-item reservation changed during storage.");
        }

        if (committed)
        {
            RemoveEntry(id);
        }
        else
        {
            entry.Reservation = Guid.Empty;
        }
    }

    // Gold (no definition), life and stamina potions lie for ShortLifetimeMs; mana potions and other items for
    // LongLifetimeMs.
    private const long ShortLifetimeMs = 90_000;
    // A drop that is not public belongs to the credited player this long.
    private const long OwnedDropMs = 5000;
    private const long LongLifetimeMs = 180_000;

    private static long LifetimeMs(ItemDefinition? definition) =>
        definition is null || PotionDefinition.FamilyOf(definition.CatalogKey) is PotionResource.Life or PotionResource.Stamina
            ? ShortLifetimeMs
            : LongLifetimeMs;

    private void RemoveEntry(Guid id)
    {
        if (_entries.Remove(id))
        {
            _dirty = true;
        }
    }

    private sealed class Entry(GroundDropView view, ItemInstance? item, ulong ownerId, long publicAt, long expiresAt, long thrownBy)
    {
        public long ThrownBy { get; } = thrownBy;
        public bool FromMonster { get; init; }
        public GroundDropView View { get; } = view;
        public ItemInstance? Item { get; } = item;
        public ulong OwnerId { get; } = ownerId;
        public long PublicAt { get; } = publicAt;
        public long ExpiresAt { get; } = expiresAt;
        public bool Public { get; set; }
        public Guid Reservation { get; set; }
    }
}
