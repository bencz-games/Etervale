using PristonTale.Application.Chat;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Application.Services;

// Personal shops outside the tick. A purchase holds the buyer's save gate and then the seller's (neither waits, so two
// players buying from each other never block), stores both characters in one transaction and commits both on the tick.
// Opening and closing hold the seller's gate too, so a listing never changes between a purchase's prepare and commit.
public sealed class PersonalShopAppService(ISessionRegistry sessions, ICharacterSaves saves, ICharacterRepository characters, WorldServer world,
    ChatDirectory? chat = null)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task<PersonalShopOutcome> OpenAsync(string sessionToken, string sign, IReadOnlyList<PersonalShopEntry> entries, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return lease.Failure is { } refused ? Failed(refused) : await world.OpenPersonalShopAsync(lease.Session, sign, entries);
    }

    public async Task<PersonalShopOutcome> CloseAsync(string sessionToken, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return lease.Failure is { } refused ? Failed(refused) : await world.ClosePersonalShopAsync(lease.Session);
    }

    public Task<PersonalShopReply> ViewAsync(string sessionToken, ulong sellerId) =>
        sessions.TryGet(sessionToken, out var session)
            ? world.ViewPersonalShopAsync(session, sellerId)
            : Task.FromResult(new PersonalShopReply(Rejected()));

    public async Task<PersonalShopReply> BuyAsync(string sessionToken, PersonalShopPurchase purchase, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is { } refused)
        {
            return new PersonalShopReply(Failed(refused));
        }

        var buyer = lease.Session;
        var seller = purchase.SellerId == buyer.EntityId ? null : await world.PersonalShopSellerAsync(buyer, purchase.SellerId);
        using var sellerLease = seller is null ? null : await _durable.AdmitAsync(seller, TimeSpan.Zero, ct);
        if (sellerLease?.Failure is not null)
        {
            // A retried purchase still answers its first outcome while the seller is busy.
            var replay = await world.PreparePersonalShopPurchaseAsync(buyer, null, purchase);
            return new PersonalShopReply(replay?.Outcome is { Status: PersonalShopStatus.Ok or PersonalShopStatus.Conflict } outcome
                ? outcome
                : Failed(DurableFailure.Busy));
        }

        PersonalShopListing? listing = null;
        var sold = false;
        var reply = await lease.RunAsync(
            session => world.PreparePersonalShopPurchaseAsync(session, seller, purchase),
            async (session, prepared) =>
            {
                listing = (await world.CommitPersonalShopPurchaseAsync(session, seller!, prepared)).Listing;
                sold = true;
                Announce(buyer, seller!, prepared, purchase.Count);
            },
            prepared => new PersonalShopReply(prepared.Outcome, listing),
            failure => new PersonalShopReply(Failed(failure)),
            async (session, prepared) => await characters.UpdateAllAsync([session.Stamp(prepared.Snapshot!), seller!.Stamp(prepared.SellerSnapshot!)],
                    CancellationToken.None)
                ? null
                : DurableFailure.StorageFailed);
        if (sold || reply.Outcome.Status is not (PersonalShopStatus.SoldOut or PersonalShopStatus.CountChanged or PersonalShopStatus.PriceChanged))
        {
            return reply;
        }

        // What the shop offers now, so the buyer's window shows the change it met.
        return reply with { Listing = (await world.ViewPersonalShopAsync(buyer, purchase.SellerId)).Listing };
    }

    private void Announce(PlayerSession buyer, PlayerSession seller, PreparedShopPurchase prepared, int count)
    {
        var line = new ChatLine(ChatKind.Trade, 0, "", PersonalShopMessages.Sold(prepared.Original.Name, count, prepared.ItemName, prepared.Price));
        foreach (var id in (ReadOnlySpan<ulong>)[buyer.EntityId, seller.EntityId])
        {
            if (chat is not null && chat.TryGet(id, out var member))
            {
                member.Link.Deliver(line);
            }
        }
    }

    private static PersonalShopOutcome Rejected() => PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0);

    private static PersonalShopOutcome Failed(DurableFailure failure) => PersonalShopOutcome.Failed(failure switch
    {
        DurableFailure.Busy => PersonalShopStatus.Busy,
        DurableFailure.StorageFailed => PersonalShopStatus.StorageFailed,
        _ => PersonalShopStatus.Rejected
    }, 0);
}
