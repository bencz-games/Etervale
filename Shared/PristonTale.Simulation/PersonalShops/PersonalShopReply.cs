using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.PersonalShops;

// Carried on the wire; append values only.
public enum PersonalShopAction : byte
{
    Open = 0,
    Close = 1,
    View = 2,
    Buy = 3
}

// What a request came to. Revision is the requester's inventory revision; UnitPrice and Count name what the shop offers
// now when a purchase met another price or fewer units.
public sealed record PersonalShopOutcome(bool Ok, PersonalShopStatus Status, long Revision, string Message, long UnitPrice = 0, int Count = 0)
{
    public static PersonalShopOutcome Failed(PersonalShopStatus status, long revision, long unitPrice = 0, int count = 0) =>
        new(false, status, revision, PersonalShopMessages.Of(status), unitPrice, count);
}

// An item a shop offers as its viewer sees it: the copy's values (the viewer's class decides what it may use) at its store
// grid cell (the view's anchor), and the price of one unit.
public sealed record PersonalShopOffer(OwnedItemView Item, long UnitPrice);

public sealed record PersonalShopListing(ulong SellerId, IReadOnlyList<PersonalShopOffer> Offers)
{
    public static PersonalShopListing Of(ulong sellerId, PersonalShop shop, PlayerCharacter seller, PlayerCharacter viewer)
    {
        var offers = new List<PersonalShopOffer>(shop.Entries.Count);
        foreach (var entry in shop.Entries)
        {
            var anchor = seller.Inventory.Find(entry.ItemId);
            if (anchor >= 0 && seller.Inventory[anchor] is { } item)
            {
                offers.Add(new PersonalShopOffer(OwnedItemView.Of(item, entry.Cell, -1) with { Details = ItemDetailView.Of(item, viewer) }, entry.UnitPrice));
            }
        }

        return new PersonalShopListing(sellerId, offers);
    }
}

public sealed record PersonalShopReply(PersonalShopOutcome Outcome, PersonalShopListing Listing = null);

// The texts a player sees, as the original words them.
public static class PersonalShopMessages
{
    public static string Of(PersonalShopStatus status) => status switch
    {
        PersonalShopStatus.Ok => "",
        PersonalShopStatus.NotInTown => "A personal shop opens only in a town",
        PersonalShopStatus.NotListable => "The item can not be sold to personal shop.",
        PersonalShopStatus.GoldLimit => ItemRuleMessages.GoldLimit,
        PersonalShopStatus.Closed => "The seller's private store is closed",
        PersonalShopStatus.SoldOut => "The item was sold already",
        PersonalShopStatus.CountChanged => "The number of items are wrong",
        PersonalShopStatus.PriceChanged => "Item price was changed",
        PersonalShopStatus.NotEnoughGold => ItemRuleMessages.NotEnoughGold,
        PersonalShopStatus.NoSpace => ItemRuleMessages.NoSpace,
        PersonalShopStatus.OverWeight => ItemRuleMessages.OverWeight,
        PersonalShopStatus.Busy => "A trade is still pending; try again",
        PersonalShopStatus.StorageFailed => ItemRuleMessages.TradeStorageFailed,
        PersonalShopStatus.Stale => ItemRuleMessages.StaleInventory,
        PersonalShopStatus.OutOfReach => "The seller is not found",
        PersonalShopStatus.Conflict => "operation identity conflict",
        PersonalShopStatus.Selling => "Close your personal shop first",
        _ => "The personal shop refused"
    };

    // The line the seller and the buyer read when a sale goes through.
    public static string Sold(string buyer, int count, string item, long price) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{buyer} player has bought {count} number of {item} by {price} Gold");
}
