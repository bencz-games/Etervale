using PristonTale.Application.Sessions;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    public Task<PersonalShopOutcome> OpenPersonalShopAsync(PlayerSession session, string sign, IReadOnlyList<PersonalShopEntry> entries)
    {
        return EnqueueAsync(session.EntityId, () => TryGetPlaying(session, out var connection)
            ? PersonalShopTransactions.Open(connection.Player, sign, entries)
            : PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0));
    }

    public Task<PersonalShopOutcome> ClosePersonalShopAsync(PlayerSession session)
    {
        return EnqueueAsync(session.EntityId, () => TryGetPlaying(session, out var connection)
            ? PersonalShopTransactions.Close(connection.Player)
            : PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0));
    }

    public Task<PersonalShopReply> ViewPersonalShopAsync(PlayerSession session, ulong sellerId)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection))
            {
                return new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0));
            }

            var viewer = connection.Player;
            var revision = viewer.Character.InventoryRevision;
            if (!TryGetShopSeller(connection, sellerId, out var seller, out _))
            {
                return new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Closed, revision));
            }

            return PersonalShopTransactions.CanReach(viewer, seller.Player)
                ? new PersonalShopReply(new PersonalShopOutcome(true, PersonalShopStatus.Ok, revision, GameText.None), Listing(seller, viewer))
                : new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.OutOfReach, revision));
        });
    }

    // The session of a seller in the buyer's field whose shop is open, so the service can hold both characters.
    public Task<PlayerSession?> PersonalShopSellerAsync(PlayerSession buyer, ulong sellerId)
    {
        return EnqueueAsync(buyer.EntityId, () =>
            TryGetPlaying(buyer, out var connection) && TryGetShopSeller(connection, sellerId, out _, out var session) ? session : null);
    }

    // Seller is null when no open shop of that seller was found; a retried purchase still answers its first outcome.
    public Task<PreparedShopPurchase?> PreparePersonalShopPurchaseAsync(PlayerSession buyer, PlayerSession? seller, PersonalShopPurchase purchase)
    {
        return EnqueueAsync<PreparedShopPurchase?>(buyer.EntityId, () =>
        {
            if (buyer.Loaded is null || !TryGetPlaying(buyer, out var connection))
            {
                return null;
            }

            Connection? sellerConnection = null;
            if (seller is { Loaded: not null } && TryGetPlaying(seller, out var playing) && playing.FieldId == connection.FieldId)
            {
                sellerConnection = playing;
            }

            return PersonalShopTransactions.Prepare(buyer, connection.Player, seller, sellerConnection?.Player, connection.FieldId, purchase);
        });
    }

    // Exempt from the quota: both characters are already stored. The reply carries the shop as the buyer sees it now.
    public Task<PersonalShopReply> CommitPersonalShopPurchaseAsync(PlayerSession buyer, PlayerSession seller, PreparedShopPurchase purchase)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(buyer.Character, purchase.Original) || purchase.Seller is not { } sold || !ReferenceEquals(seller.Character, sold)
                || purchase.Candidate is null || purchase.SellerCandidate is null
                || !purchase.Original.CanChangeMoney(-purchase.Price) || !sold.CanChangeMoney(purchase.Price))
            {
                throw new CommitInvariantException("Character ownership changed during a personal shop purchase.");
            }

            if (Transfer(Persistence.Abstractions.GameLogs.ItemTransferKind.PersonalShop, Guid.NewGuid(), seller.FieldId) is { } log)
            {
                log.Left(sold, purchase.SellerCandidate, CharacterIdOf(seller), CharacterIdOf(buyer));
                log.Gold(CharacterIdOf(buyer), CharacterIdOf(seller), purchase.Price);
                Log(log);
            }

            purchase.Original.CommitInventoryOperation(purchase.Candidate);
            purchase.Original.TryChangeMoney(-purchase.Price);
            sold.CommitInventoryOperation(purchase.SellerCandidate);
            sold.TryChangeMoney(purchase.Price);
            if (!TryGetOwnConnection(seller, out var sellerConnection))
            {
                return new PersonalShopReply(purchase.Outcome);
            }

            sellerConnection.Player.PruneShop();
            var listing = TryGetOwnConnection(buyer, out var connection) && sellerConnection.Player.Shop is not null
                ? Listing(sellerConnection, connection.Player)
                : null;
            return new PersonalShopReply(purchase.Outcome, listing);
        }, waitForCapacity: true);
    }

    private bool TryGetShopSeller(Connection viewer, ulong sellerId, out Connection seller, out PlayerSession session)
    {
        session = null!;
        if (!_connections.TryGetValue(sellerId, out seller!) || seller.FieldId != viewer.FieldId || seller.Session is not { } owner)
        {
            return false;
        }

        seller.Player.PruneShop();
        session = owner;
        return seller.Player.Shop is not null;
    }

    private static PersonalShopListing Listing(Connection seller, WorldPlayer viewer) =>
        PersonalShopListing.Of(seller.Player.EntityId, seller.Player.Shop!, seller.Player.Character, viewer.Character);
}
