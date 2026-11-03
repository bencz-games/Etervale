using PristonTale.Contracts.Social;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Trades;

// A bag item a side offers, at its cell of the trade grid (row-major, TradeLimits.Columns wide).
public readonly record struct TradeEntry(Guid ItemId, int Cell);

// One side's offer: bag items laid on the trade grid and gold. The items stay in the bag until the exchange.
public sealed record TradeOffer(IReadOnlyList<TradeEntry> Entries, long Money)
{
    public static readonly TradeOffer Empty = new([], 0);

    public bool Holds(Guid itemId)
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            if (Entries[index].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    public bool SameAs(TradeOffer other) => Money == other.Money && Entries.SequenceEqual(other.Entries);
}

// Why a trade change was refused, or why the trade ended.
public enum TradeStatus : ushort
{
    Ok = 0,
    Rejected = 1,
    OutOfReach = 2,
    Busy = 3,
    NotTradable = 4,
    TooMany = 5,
    NotEnoughGold = 6,
    OverWeight = 7,
    NoSpace = 8,
    GoldLimit = 9,
    Changed = 10,
    Cancelled = 11,
    StorageFailed = 12
}

// The original's trade: each side lays up to 20 bag items it may hand over (BoundItems.CanHandOver) on its 9x4 grid and
// gold it holds; when both lock, the offers cross at once, each side taking the other's items into its bag and its gold
// within what it may carry (MoneyLimits.CarryLimit).
public static class TradeRules
{
    public static Inventory Grid() => new(TradeLimits.Columns, TradeLimits.Rows);

    public static bool IsTradable(PlayerCharacter character, ItemInstance item) => BoundItems.CanHandOver(character, item);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool IsTradable(ItemKind kind, string key) => BoundItems.CanHandOver(kind, key);

    // Whether the giver can make the offer now: its own bag items, each once, laid on the grid without overlap, and gold it
    // holds.
    public static TradeStatus Check(PlayerCharacter giver, TradeOffer offer)
    {
        if (offer.Entries.Count > TradeLimits.MaxItems)
        {
            return TradeStatus.TooMany;
        }

        if (offer.Money < 0)
        {
            return TradeStatus.Rejected;
        }

        if (offer.Money > giver.Money)
        {
            return TradeStatus.NotEnoughGold;
        }

        var grid = Grid();
        foreach (var entry in offer.Entries)
        {
            var anchor = giver.Inventory.Find(entry.ItemId);
            if (anchor < 0)
            {
                return TradeStatus.Changed;
            }

            var item = giver.Inventory[anchor];
            if (!IsTradable(giver, item))
            {
                return TradeStatus.NotTradable;
            }

            if (grid.Find(entry.ItemId) >= 0 || !grid.TryPlace(item, entry.Cell))
            {
                return TradeStatus.Rejected;
            }
        }

        return TradeStatus.Ok;
    }

    // Crosses both offers on candidates of the two sides. failedSide names the side (0 first, 1 second) whose offer no
    // longer stands or which cannot take what it receives; -1 when the exchange went through.
    public static TradeStatus Exchange(PlayerCharacter first, TradeOffer firstOffer, PlayerCharacter second, TradeOffer secondOffer, out int failedSide)
    {
        failedSide = 0;
        var status = Check(first, firstOffer);
        if (status == TradeStatus.Ok)
        {
            failedSide = 1;
            status = Check(second, secondOffer);
        }

        if (status != TradeStatus.Ok)
        {
            return status;
        }

        var fromFirst = Take(first, firstOffer);
        var fromSecond = Take(second, secondOffer);
        failedSide = 0;
        status = Receive(first, fromSecond, secondOffer.Money);
        if (status == TradeStatus.Ok)
        {
            failedSide = 1;
            status = Receive(second, fromFirst, firstOffer.Money);
        }

        if (status == TradeStatus.Ok)
        {
            failedSide = -1;
        }

        return status;
    }

    private static List<ItemInstance> Take(PlayerCharacter giver, TradeOffer offer)
    {
        var items = new List<ItemInstance>(offer.Entries.Count);
        foreach (var entry in offer.Entries)
        {
            items.Add(giver.Inventory.TakeAt(giver.Inventory.Find(entry.ItemId)));
        }

        giver.TryChangeMoney(-offer.Money);
        return items;
    }

    private static TradeStatus Receive(PlayerCharacter receiver, List<ItemInstance> items, long money)
    {
        var weight = 0;
        foreach (var item in items)
        {
            weight += CarriedWeight.Of(item);
        }

        if (!CarriedWeight.CanTake(receiver, weight))
        {
            return TradeStatus.OverWeight;
        }

        foreach (var item in items)
        {
            if (!receiver.Inventory.TryAdd(item))
            {
                return TradeStatus.NoSpace;
            }
        }

        if (!MoneyLimits.CanCarry(receiver, money) || !receiver.TryChangeMoney(money))
        {
            return TradeStatus.GoldLimit;
        }

        return TradeStatus.Ok;
    }
}

// The texts a player sees, as the original words them.
public static class TradeMessages
{
    public const string Requested = "Sent a trade request to {0}";
    public const string TooFar = "{0} is too far away to request a trade";
    public const string InTrade = "The item is in the trade window";
    public const string Trading = "Close the trade window first";

    public static string Of(TradeStatus status) => status switch
    {
        TradeStatus.Ok => "",
        TradeStatus.OutOfReach => "The player is too far away",
        TradeStatus.Busy => "The player is busy",
        TradeStatus.NotTradable => "This item cannot be traded",
        TradeStatus.TooMany => "Exceeds maximum number of transaction items",
        TradeStatus.NotEnoughGold => ItemRuleMessages.NotEnoughGold,
        TradeStatus.OverWeight => ItemRuleMessages.OverWeight,
        TradeStatus.NoSpace => ItemRuleMessages.NoSpace,
        TradeStatus.GoldLimit => ItemRuleMessages.GoldLimit,
        TradeStatus.Changed => "Item has been changed. Please check",
        TradeStatus.Cancelled => "Transaction cancelled",
        TradeStatus.StorageFailed => ItemRuleMessages.TradeStorageFailed,
        _ => "The trade was refused"
    };

    public static string Format(string message, string name) => string.Format(System.Globalization.CultureInfo.InvariantCulture, message, name);
}
