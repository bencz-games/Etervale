using System.Collections.Concurrent;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Client.Net.Online;

// Personal shop requests and their answers: the listing and the details of a view or a purchase precede its result on
// the same connection, so they are collected under its operation id until the result completes the request.
internal sealed class PersonalShopRequests(Action<IFramePacket> send)
{
    private readonly PendingReplies<Guid, PersonalShopReply> _pending = new();
    private readonly ConcurrentDictionary<Guid, PersonalShopListing> _listings = new();

    public Task<PersonalShopReply> OpenAsync(string sign, IReadOnlyList<PersonalShopEntry> entries, CancellationToken ct)
    {
        var packet = new PersonalShopOpenPacket(Guid.NewGuid(), sign,
            entries.Select(static entry => new PersonalShopEntryWire(entry.ItemId, entry.UnitPrice, entry.Cell)).ToArray());
        return RequestAsync(packet.OperationId, packet, ct);
    }

    public Task<PersonalShopReply> CloseAsync(CancellationToken ct)
    {
        var packet = new PersonalShopClosePacket(Guid.NewGuid());
        return RequestAsync(packet.OperationId, packet, ct);
    }

    public Task<PersonalShopReply> ViewAsync(ulong sellerId, CancellationToken ct)
    {
        var packet = new PersonalShopViewPacket(Guid.NewGuid(), sellerId);
        return RequestAsync(packet.OperationId, packet, ct);
    }

    public Task<PersonalShopReply> BuyAsync(PersonalShopPurchase purchase, CancellationToken ct) =>
        RequestAsync(purchase.OperationId, new PersonalShopBuyPacket(purchase.OperationId, purchase.SellerId, purchase.ItemId, purchase.UnitPrice,
            purchase.Count, purchase.ExpectedRevision), ct);

    public void Handle(uint code, ReadOnlySpan<byte> packet)
    {
        switch (code)
        {
            case EtervaleOpcodes.PersonalShopListing:
            {
                var listing = PersonalShopListingPacket.Read(packet);
                if (_pending.IsWaiting(listing.OperationId))
                {
                    var offers = listing.Offers.Select(static offer => new PersonalShopOffer(StoreItems.View(offer.Item), offer.UnitPrice)).ToArray();
                    _listings[listing.OperationId] = new PersonalShopListing(listing.SellerId, offers);
                }

                break;
            }
            case EtervaleOpcodes.PersonalShopDetails:
            {
                var details = PersonalShopDetailsPacket.Read(packet);
                if (_listings.TryGetValue(details.OperationId, out var listing))
                {
                    var views = details.Items.ToDictionary(static entry => entry.InstanceId, OwnerItemDecoder.View);
                    _listings[details.OperationId] = listing with
                    {
                        Offers = listing.Offers
                            .Select(offer => views.TryGetValue(offer.Item.InstanceId, out var view) ? offer with { Item = offer.Item with { Details = view } } : offer)
                            .ToArray()
                    };
                }

                break;
            }
            case EtervaleOpcodes.PersonalShopResult:
            {
                var result = PersonalShopResultPacket.Read(packet);
                _listings.TryRemove(result.OperationId, out var listing);
                var outcome = new PersonalShopOutcome(result.Ok, (PersonalShopStatus)result.Status, result.Revision, result.Message, result.UnitPrice,
                    result.Count);
                _pending.Complete(result.OperationId, new PersonalShopReply(outcome, listing));
                break;
            }
        }
    }

    public void FailAll(Exception error)
    {
        _listings.Clear();
        _pending.FailAll(error);
    }

    // A listing gathered for a request that is cancelled or times out is dropped with it, not kept until the connection ends.
    private async Task<PersonalShopReply> RequestAsync(Guid operationId, IFramePacket packet, CancellationToken ct)
    {
        try
        {
            return await _pending.RequestAsync(operationId, () => send(packet), ct);
        }
        finally
        {
            _listings.TryRemove(operationId, out _);
        }
    }
}
