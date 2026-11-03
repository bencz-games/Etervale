using PristonTale.Protocol.Packets;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Server.Net;

// Personal shop requests: each is answered by its result, after the shop's listing and details when it has one.
internal sealed partial class ClientSession
{
    private async Task OpenPersonalShopAsync(PersonalShopOpenPacket request)
    {
        var entries = request.Entries.Select(static entry => new PersonalShopEntry(entry.ItemId, entry.UnitPrice, entry.Cell)).ToArray();
        var outcome = _player is null
            ? PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0)
            : await _services.PersonalShops.OpenAsync(RequireToken(), request.Sign, entries);
        SendPersonalShop(request.OperationId, PersonalShopAction.Open, new PersonalShopReply(outcome));
    }

    private async Task ClosePersonalShopAsync(PersonalShopClosePacket request)
    {
        var outcome = _player is null
            ? PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0)
            : await _services.PersonalShops.CloseAsync(RequireToken());
        SendPersonalShop(request.OperationId, PersonalShopAction.Close, new PersonalShopReply(outcome));
    }

    private async Task ViewPersonalShopAsync(PersonalShopViewPacket request)
    {
        var reply = _player is null
            ? new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0))
            : await _services.PersonalShops.ViewAsync(RequireToken(), request.SellerId);
        SendPersonalShop(request.OperationId, PersonalShopAction.View, reply);
    }

    private async Task BuyFromPersonalShopAsync(PersonalShopBuyPacket request)
    {
        var purchase = new PersonalShopPurchase(request.OperationId, request.SellerId, request.ItemId, request.UnitPrice, request.Count, request.ExpectedRevision);
        PersonalShopReply reply;
        try
        {
            reply = _player is null
                ? new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0))
                : await _services.PersonalShops.BuyAsync(RequireToken(), purchase);
        }
        catch (System.Data.Common.DbException)
        {
            reply = new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.StorageFailed, 0));
        }

        SendPersonalShop(request.OperationId, PersonalShopAction.Buy, reply);
    }

    private void SendPersonalShop(Guid operation, PersonalShopAction action, PersonalShopReply reply)
    {
        if (reply.Listing is { } listing)
        {
            var offers = listing.Offers
                .Select(static offer => new PersonalShopOfferWire(ItemWireMapping.StoreItem(offer.Item), offer.UnitPrice))
                .ToArray();
            SendLarge(new PersonalShopListingPacket(operation, listing.SellerId, offers).Write,
                PersonalShopListingPacket.MaxSize);
            var details = listing.Offers.Select(static offer => ItemWireMapping.Detail(offer.Item)).ToArray();
            SendLarge(new PersonalShopDetailsPacket(operation, listing.SellerId, details).Write, PersonalShopDetailsPacket.MaxSize);
        }

        var outcome = reply.Outcome;
        SendLarge(new PersonalShopResultPacket(operation, (byte)action, (ushort)outcome.Status, outcome.Count, outcome.Revision, outcome.UnitPrice,
            outcome.Message).Write, PersonalShopResultPacket.MaxSize);
    }
}
