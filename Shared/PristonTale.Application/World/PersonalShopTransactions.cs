using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// A validated purchase from a personal shop: the buyer's change (the base record) and the seller's, stored together; the
// commit applies both and moves Price from the buyer to the seller.
public sealed record PreparedShopPurchase(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    PersonalShopOutcome Outcome,
    PlayerCharacter? Seller = null,
    PlayerCharacter? SellerCandidate = null,
    CharacterSaveSnapshot? SellerSnapshot = null,
    long Price = 0,
    string ItemName = "")
    : PreparedOperation(Original, Candidate, Snapshot);

internal static class PersonalShopTransactions
{
    // A seller is approached and its shop opened as an NPC is.
    public static bool CanReach(WorldPlayer viewer, WorldPlayer seller) =>
        !viewer.IsDead && viewer.EntityId != seller.EntityId && NpcInteraction.WithinReach(viewer.Position, seller.Position);

    public static PersonalShopOutcome Open(WorldPlayer player, string sign, IReadOnlyList<PersonalShopEntry> entries)
    {
        var revision = player.Character.InventoryRevision;
        if (player.Shop is not null)
        {
            return PersonalShopOutcome.Failed(PersonalShopStatus.Selling, revision);
        }

        if (player.IsDead || player.IsBusy || player.Trade is not null)
        {
            return PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, revision);
        }

        var status = PersonalShopRules.TryOpen(player.Character, player.InVillage, sign, entries, out var shop);
        if (status != PersonalShopStatus.Ok)
        {
            return PersonalShopOutcome.Failed(status, revision);
        }

        player.ClearAttackTarget();
        player.OpenShop(shop);
        return Done(revision);
    }

    public static PersonalShopOutcome Close(WorldPlayer player)
    {
        player.CloseShop();
        return Done(player.Character.InventoryRevision);
    }

    public static PreparedShopPurchase Prepare(PlayerSession buyerSession, WorldPlayer buyer, PlayerSession? sellerSession, WorldPlayer? seller, int fieldId,
        PersonalShopPurchase purchase)
    {
        var original = buyer.Character;
        var revision = original.InventoryRevision;
        if (original.ShopPurchases.Find(purchase.OperationId) is { } applied)
        {
            return applied.Purchase == purchase
                ? Final(original, new PersonalShopOutcome(true, PersonalShopStatus.Ok, applied.Revision, GameText.Of(GameTextId.AlreadyApplied)))
                : Final(original, PersonalShopOutcome.Failed(PersonalShopStatus.Conflict, revision));
        }

        if (sellerSession is null || seller?.Shop is not { } shop)
        {
            return Final(original, PersonalShopOutcome.Failed(PersonalShopStatus.Closed, revision));
        }

        if (!CanReach(buyer, seller))
        {
            return Final(original, PersonalShopOutcome.Failed(PersonalShopStatus.OutOfReach, revision));
        }

        if (buyer.Shop is not null)
        {
            return Final(original, PersonalShopOutcome.Failed(PersonalShopStatus.Selling, revision));
        }

        if (purchase.OperationId == Guid.Empty || purchase.ExpectedRevision != revision)
        {
            return Final(original, PersonalShopOutcome.Failed(PersonalShopStatus.Stale, revision));
        }

        var candidate = original.CloneForInventoryOperation();
        var sellerCandidate = seller.Character.CloneForInventoryOperation();
        var anchor = seller.Character.Inventory.Find(purchase.ItemId);
        var item = anchor >= 0 ? seller.Character.Inventory[anchor] : null;
        var status = PersonalShopRules.TrySell(shop, sellerCandidate, candidate, purchase, out var price);
        if (status != PersonalShopStatus.Ok)
        {
            return Final(original, PersonalShopOutcome.Failed(status, revision, shop.Find(purchase.ItemId)?.UnitPrice ?? 0, item?.Count ?? 0));
        }

        candidate.RecordShopPurchase(purchase);
        var snapshot = CharacterRecordFactory.Capture(buyerSession.Loaded!, candidate, fieldId, buyer.Position);
        var sellerSnapshot = CharacterRecordFactory.Capture(sellerSession.Loaded!, sellerCandidate, fieldId, seller.Position);
        return new PreparedShopPurchase(original, candidate, snapshot, Done(candidate.InventoryRevision), seller.Character, sellerCandidate, sellerSnapshot,
            price, item!.DisplayName);
    }

    private static PersonalShopOutcome Done(long revision) => new(true, PersonalShopStatus.Ok, revision, GameText.None);

    private static PreparedShopPurchase Final(PlayerCharacter original, PersonalShopOutcome outcome) => new(original, null, null, outcome);
}
