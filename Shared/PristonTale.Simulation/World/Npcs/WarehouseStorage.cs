using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World.Npcs;

// Account storage kept by warehouse NPCs: the store grid (StoreGrid), at most 100 items and 1000 weight.
public sealed class WarehouseStorage
{
    public const int MaxItems = 100;
    public const int MaxWeight = 1000;
    public const long MaxMoney = 100_000_000;

    public WarehouseStorage(long revision = 0, long money = 0)
    {
        if (revision < 0 || money < 0 || money > MaxMoney)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        Revision = revision;
        Money = money;
    }

    public Inventory Grid { get; } = StoreGrid.Create();
    public long Money { get; private set; }
    public long Revision { get; private set; }

    public int Weight
    {
        get
        {
            var weight = 0;
            foreach (var item in Grid.Items())
            {
                weight += CarriedWeight.Of(item);
            }

            return weight;
        }
    }

    public int Count => Grid.Items().Count();

    // What BoundItems.CanHandOver lets go, potions apart.
    public static bool CanStore(PlayerCharacter character, ItemInstance item) =>
        BoundItems.CanHandOver(character, item) && !RefusedOnSetDown(item.Definition.Kind);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool CanStore(ItemKind kind, string key) => BoundItems.CanHandOver(kind, key) && !RefusedOnSetDown(kind);

    // The original places a potion over the grid as any item and refuses it only when it is set down, with
    // GameTextId.PotionNotStored; the other items CanStore refuses show red where they would land.
    public static bool RefusedOnSetDown(ItemKind kind) => kind == ItemKind.Potion;

    public WarehouseStorage Clone()
    {
        var clone = new WarehouseStorage(Revision, Money);
        clone.Grid.CopyFrom(Grid);
        return clone;
    }

    public bool Restore(ItemInstance item, int anchor) => Count < MaxItems && Grid.TryPlace(item, anchor);

    // An item set down on the grid, as the original sets it: on free cells (-1: the first that fit), or over one stored
    // item, which goes to the item's bag cell (or wherever the bag has room) and is carried in turn. covered is the item
    // the client saw under it, or empty.
    public NpcServiceStatus TryDeposit(PlayerCharacter character, Guid itemId, int anchor, Guid covered = default)
    {
        var source = character.Inventory.Find(itemId);
        if (source < 0 || character.Inventory[source] is not { } item || !CanStore(character, item) || !TryCovered(Grid, anchor, item, covered, out var displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        if (displaced is null && Count >= MaxItems)
        {
            return NpcServiceStatus.NoSpace;
        }

        return Weigh(character, item, displaced) is var weighed && weighed != NpcServiceStatus.Ok ? weighed
            : SetDown(character.Inventory, source, Grid, anchor, displaced) ? Changed() : NpcServiceStatus.NoSpace;
    }

    // A stored item set down in the bag: on free cells, or over one bag item, which goes to the stored item's cell (or
    // wherever the grid has room) and is carried in turn.
    public NpcServiceStatus TryWithdraw(PlayerCharacter character, Guid itemId, int anchor, Guid covered = default)
    {
        var source = Grid.Find(itemId);
        if (source < 0 || Grid[source] is not { } item || !TryCovered(character.Inventory, anchor, item, covered, out var displaced)
            || displaced is not null && !CanStore(character, displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        return Weigh(character, displaced, item) is var weighed && weighed != NpcServiceStatus.Ok ? weighed
            : SetDown(Grid, source, character.Inventory, anchor, displaced) ? Changed() : NpcServiceStatus.NoSpace;
    }

    // A stored item set down elsewhere on the grid, over free cells or one other stored item (carried in turn).
    public NpcServiceStatus TryMove(Guid itemId, int anchor, Guid covered = default)
    {
        var source = Grid.Find(itemId);
        if (source < 0 || anchor < 0 || Grid[source] is not { } item || !CanStore(item.Definition.Kind, item.Definition.CatalogKey)
            || !TryCovered(Grid, anchor, item, covered, out var displaced))
        {
            return NpcServiceStatus.Rejected;
        }

        return (displaced is null ? Grid.TryMove(source, anchor) : Grid.TrySwap(source, anchor)) ? Changed() : NpcServiceStatus.Rejected;
    }

    public NpcServiceStatus TryDepositGold(PlayerCharacter character, long amount)
    {
        if (amount <= 0 || amount > MaxMoney - Money)
        {
            return amount <= 0 ? NpcServiceStatus.Rejected : NpcServiceStatus.GoldLimit;
        }

        if (!character.TryChangeMoney(-amount))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        Money += amount;
        Revision++;
        character.TouchInventory();
        return NpcServiceStatus.Ok;
    }

    public NpcServiceStatus TryWithdrawGold(PlayerCharacter character, long amount)
    {
        if (amount <= 0 || amount > Money)
        {
            return amount <= 0 ? NpcServiceStatus.Rejected : NpcServiceStatus.NotEnoughGold;
        }

        if (!MoneyLimits.CanCarry(character, amount) || !character.TryChangeMoney(amount))
        {
            return NpcServiceStatus.GoldLimit;
        }

        Money -= amount;
        Revision++;
        character.TouchInventory();
        return NpcServiceStatus.Ok;
    }

    // Whether the warehouse can bear `stored` coming in and the character `taken` coming out to it (either may be null);
    // a side whose load falls is never refused, even over its limit.
    private NpcServiceStatus Weigh(PlayerCharacter character, ItemInstance stored, ItemInstance taken)
    {
        var change = (stored is null ? 0 : CarriedWeight.Of(stored)) - (taken is null ? 0 : CarriedWeight.Of(taken));
        if (change > 0 && Weight + change > MaxWeight)
        {
            return NpcServiceStatus.NoSpace;
        }

        return change < 0 && !CarriedWeight.CanTake(character, -change) ? NpcServiceStatus.OverWeight : NpcServiceStatus.Ok;
    }

    private NpcServiceStatus Changed()
    {
        Revision++;
        return NpcServiceStatus.Ok;
    }

    // The one item of `to` that `item` set down at anchor lies over (null on free cells). False when it lies over more,
    // or over another item than the client saw (its view was stale). Anchor -1 (the first free cells) covers nothing.
    private static bool TryCovered(Inventory to, int anchor, ItemInstance item, Guid expected, out ItemInstance displaced)
    {
        displaced = null;
        if (anchor < 0)
        {
            return expected == Guid.Empty;
        }

        var under = to.Covered(anchor, item.Definition, to.Find(item.InstanceId));
        if (under == Inventory.Several)
        {
            return false;
        }

        displaced = under >= 0 ? to[under] : null;
        return (displaced?.InstanceId ?? Guid.Empty) == expected;
    }

    // The item at source of `from` set down at anchor of `to`; the displaced item of `to` goes to the source cell or
    // wherever `from` has room. Both sides are checked before either changes.
    private static bool SetDown(Inventory from, int source, Inventory to, int anchor, ItemInstance displaced)
    {
        var item = from[source];
        if (displaced is null)
        {
            return (anchor >= 0 ? to.TryPlace(item, anchor) : to.TryAdd(item)) && from.TakeAt(source) is not null;
        }

        var under = to.Find(displaced.InstanceId);
        if (!to.CanExchange(under, item, anchor) || !from.CanExchange(source, displaced))
        {
            return false;
        }

        return to.TryExchange(under, item, anchor) && from.TryExchange(source, displaced);
    }

    public WarehouseView View()
    {
        var items = Grid.Occupied().Select(static cell => OwnedItemView.Of(cell.Item, cell.Anchor, -1)).ToArray();
        return new WarehouseView(Revision, Money, Weight, MaxWeight, StoreGrid.Columns, StoreGrid.Rows, items);
    }
}
