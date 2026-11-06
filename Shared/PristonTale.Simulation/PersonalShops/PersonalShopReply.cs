using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

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
public sealed record PersonalShopOutcome(bool Ok, PersonalShopStatus Status, long Revision, GameText Message, long UnitPrice = 0, int Count = 0)
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
    public static GameText Of(PersonalShopStatus status) => status == PersonalShopStatus.Ok ? GameText.None : GameText.Of(status switch
    {
        PersonalShopStatus.NotInTown => GameTextId.ShopNotInTown,
        PersonalShopStatus.NotListable => GameTextId.ShopNotListable,
        PersonalShopStatus.GoldLimit => GameTextId.GoldLimit,
        PersonalShopStatus.Closed => GameTextId.SellerShopClosed,
        PersonalShopStatus.SoldOut => GameTextId.ShopSoldOut,
        PersonalShopStatus.CountChanged => GameTextId.ShopCountChanged,
        PersonalShopStatus.PriceChanged => GameTextId.ShopPriceChanged,
        PersonalShopStatus.NotEnoughGold => GameTextId.NotEnoughGold,
        PersonalShopStatus.NoSpace => GameTextId.NoSpace,
        PersonalShopStatus.OverWeight => GameTextId.OverWeight,
        PersonalShopStatus.Busy => GameTextId.ShopTradePending,
        PersonalShopStatus.StorageFailed => GameTextId.StorageRejected,
        PersonalShopStatus.Stale => GameTextId.StaleInventory,
        PersonalShopStatus.OutOfReach => GameTextId.SellerNotFound,
        PersonalShopStatus.Conflict => GameTextId.OperationConflict,
        PersonalShopStatus.Selling => GameTextId.CloseShopFirst,
        _ => GameTextId.PersonalShopRefused
    });

    // The line the seller and the buyer read when a sale goes through.
    public static GameText Sold(string buyer, int count, string item, long price) => GameText.Of(GameTextId.ShopSold, buyer,
        count.ToString(System.Globalization.CultureInfo.InvariantCulture), item, price.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
