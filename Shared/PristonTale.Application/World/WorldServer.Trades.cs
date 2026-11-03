using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Trades;

namespace PristonTale.Application.World;

// A validated exchange: the locking side's change (the base record) and its partner's, stored together; the commit applies
// both inventories and moves gold by each side's delta.
public sealed record PreparedTrade(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    WorldNotice[] Notices,
    PlayerCharacter? Partner = null,
    PlayerCharacter? PartnerCandidate = null,
    CharacterSaveSnapshot? PartnerSnapshot = null,
    long MoneyDelta = 0,
    long PartnerMoneyDelta = 0,
    Trade? Trade = null)
    : PreparedOperation(Original, Candidate, Snapshot);

// The lines a lock answered, and the trade with its partner's session when both sides are now locked and the exchange is to
// be stored.
public sealed record TradeLock(WorldNotice[] Notices, Trade? Trade = null, PlayerSession? Partner = null);

// Trades between two players of a field, kept in memory: a request (TradeLimits.RequestMs to answer), the window both
// sides fill (each change unlocks both; a side may not lock within TradeLimits.LockDelayMs of the other's change), and the
// exchange once both lock, stored for both characters at once (TradeAppService). Leaving the world or the field ends it for
// both. While the exchange is stored nothing changes the trade: a cancel then ends it only if the exchange fails, and a side
// that leaves ends it silently (an exchange holding both save gates still commits, and the leaving save waits for it).
public sealed partial class WorldServer
{
    public Task<WorldNotice[]> RequestTradeAsync(PlayerSession session, ulong targetId) =>
        EnqueueAsync(session.EntityId, () => NoticeAction(session, connection => RequestTrade(connection, targetId)));

    public Task<WorldNotice[]> AnswerTradeAsync(PlayerSession session, ulong fromId, bool accept) =>
        EnqueueAsync(session.EntityId, () => NoticeAction(session, connection => AnswerTrade(connection, fromId, accept)));

    public Task<WorldNotice[]> OfferTradeAsync(PlayerSession session, TradeOffer offer) =>
        EnqueueAsync(session.EntityId, () => NoticeAction(session, connection => OfferTrade(connection.Player, offer)));

    // Exempt from the quota: the window is already closed.
    public Task<WorldNotice[]> CancelTradeAsync(PlayerSession session) =>
        EnqueueAsync(NoOwner, () => NoticeAction(session, connection =>
        {
            if (connection.Player.Trade is { Executing: true } executing)
            {
                executing.Cancelling = true;
            }
            else if (connection.Player.Trade is { } trade)
            {
                EndTrade(trade, TradeStatus.Cancelled);
            }
        }), waitForCapacity: true);

    public Task<TradeLock> LockTradeAsync(PlayerSession session)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (TryGetPlaying(session, out var connection) && LockTrade(connection.Player, out var partner) is { } trade)
            {
                return new TradeLock(TakeNotices(), trade, partner);
            }

            return new TradeLock(TakeNotices());
        });
    }

    // Both sides are locked and their save gates held: the exchange on clones, with each side's snapshot. A refusal releases
    // the trade and answers the side it concerns. Exempt from the quota, as the trade must leave Executing.
    public Task<PreparedTrade?> PrepareTradeAsync(PlayerSession session, PlayerSession partnerSession, Trade trade)
    {
        return EnqueueAsync<PreparedTrade?>(NoOwner, () =>
        {
            if (!TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            if (!ReferenceEquals(player.Trade, trade))
            {
                // The partner left meanwhile.
                Reply(player, TradeMessages.Of(TradeStatus.Cancelled));
                return new PreparedTrade(player.Character, null, null, TakeNotices());
            }

            if (session.Loaded is null || partnerSession.Loaded is null || !TryGetPlaying(partnerSession, out var partnerConnection)
                || !ReferenceEquals(partnerConnection.Player.Trade, trade))
            {
                ReleaseTrade(trade);
                return new PreparedTrade(player.Character, null, null, TakeNotices());
            }

            var partner = partnerConnection.Player;
            if (!AsShown(trade, player, partner))
            {
                return new PreparedTrade(player.Character, null, null, TakeNotices());
            }

            var side = trade.SideOf(player.EntityId);
            var candidate = player.Character.CloneForInventoryOperation();
            var partnerCandidate = partner.Character.CloneForInventoryOperation();
            var status = TradeRules.Exchange(candidate, trade.OfferOf(side), partnerCandidate, trade.OfferOf(1 - side), out var failedSide);
            if (status != TradeStatus.Ok)
            {
                ReleaseTrade(trade);
                Reply(failedSide == 0 ? player : partner, TradeMessages.Of(status));
                return new PreparedTrade(player.Character, null, null, TakeNotices());
            }

            return new PreparedTrade(player.Character, candidate,
                CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, player.Position), [],
                partner.Character, partnerCandidate,
                CharacterRecordFactory.Capture(partnerSession.Loaded, partnerCandidate, partnerConnection.FieldId, partner.Position),
                candidate.Money - player.Character.Money, partnerCandidate.Money - partner.Character.Money, trade);
        }, waitForCapacity: true);
    }

    // Exempt from the quota: both characters are already stored.
    public Task<bool> CommitTradeAsync(PlayerSession session, PlayerSession partnerSession, PreparedTrade prepared)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, prepared.Original) || !ReferenceEquals(partnerSession.Character, prepared.Partner)
                || prepared.Candidate is null || prepared.PartnerCandidate is null
                || !prepared.Original.CanChangeMoney(prepared.MoneyDelta) || !prepared.Partner!.CanChangeMoney(prepared.PartnerMoneyDelta))
            {
                throw new CommitInvariantException("Character ownership changed during a trade.");
            }

            if (Transfer(Persistence.Abstractions.GameLogs.ItemTransferKind.Trade, Guid.NewGuid(), session.FieldId) is { } log)
            {
                var (self, partner) = (CharacterIdOf(session), CharacterIdOf(partnerSession));
                log.Left(prepared.Original, prepared.Candidate, self, partner);
                log.Left(prepared.Partner!, prepared.PartnerCandidate, partner, self);
                // Each side's whole offer moves (TradeRules), not the difference.
                var side = prepared.Trade!.SideOf(session.EntityId);
                log.Gold(self, partner, prepared.Trade.OfferOf(side).Money);
                log.Gold(partner, self, prepared.Trade.OfferOf(1 - side).Money);
                Log(log);
            }

            prepared.Original.CommitInventoryOperation(prepared.Candidate);
            prepared.Original.TryChangeMoney(prepared.MoneyDelta);
            prepared.Partner.CommitInventoryOperation(prepared.PartnerCandidate);
            prepared.Partner.TryChangeMoney(prepared.PartnerMoneyDelta);
            EndTrade(prepared.Trade!, null);

            return true;
        }, waitForCapacity: true);
    }

    // The exchange was not stored (the partner's gate stayed busy, or storage refused): both sides read why, and the trade,
    // when no side left or cancelled it, stays open unlocked.
    public Task<WorldNotice[]> ReleaseTradeAsync(Trade trade, TradeStatus status)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (TradeOpen(trade))
            {
                ReleaseTrade(trade);
            }

            Notify(trade.First, ChatKind.Server, TradeMessages.Of(status));
            Notify(trade.Second, ChatKind.Server, TradeMessages.Of(status));
            return TakeNotices();
        }, waitForCapacity: true);
    }

    private static bool InTradeReach(WorldPlayer player, WorldPlayer other) => TradeLimits.InReach(player.Position, other.Position);

    // A player that may start a trade: alive, in none, and not selling from a personal shop.
    private static bool FreeToTrade(WorldPlayer player) => !player.IsDead && player.Trade is null && player.Shop is null;

    // EndTrade clears both sides at once.
    private bool TradeOpen(Trade trade) => _connections.TryGetValue(trade.First, out var first) && ReferenceEquals(first.Player.Trade, trade);

    // Each side's items as the other last saw them: a count another path used up (a restart's scroll) refuses the lock or
    // the exchange for both, and the windows show the items as they are.
    private bool AsShown(Trade trade, WorldPlayer player, WorldPlayer partner)
    {
        if (StillShown(trade, player, partner) && StillShown(trade, partner, player))
        {
            return true;
        }

        ReleaseTrade(trade);
        Notify(trade.First, ChatKind.Server, TradeMessages.Of(TradeStatus.Changed));
        Notify(trade.Second, ChatKind.Server, TradeMessages.Of(TradeStatus.Changed));
        return false;
    }

    private static bool StillShown(Trade trade, WorldPlayer giver, WorldPlayer viewer)
    {
        if (trade.ViewFor(viewer.EntityId) is not { } seen)
        {
            return false;
        }

        foreach (var shown in seen.PartnerItems)
        {
            var anchor = giver.Character.Inventory.Find(shown.InstanceId);
            if (anchor < 0 || giver.Character.Inventory[anchor]?.Count != shown.Count)
            {
                return false;
            }
        }

        return true;
    }

    // A player waiting on one request is busy to the others until it expires.
    private void RequestTrade(Connection connection, ulong targetId)
    {
        var player = connection.Player;
        if (!_connections.TryGetValue(targetId, out var target) || target.FieldId != connection.FieldId || ReferenceEquals(target.Player, player))
        {
            return;
        }

        var name = target.Player.Character.Name;
        if (!InTradeReach(player, target.Player))
        {
            Reply(player, TradeMessages.Format(TradeMessages.TooFar, name));
            return;
        }

        var nowMs = UnixNowMs;
        if (!FreeToTrade(player) || !FreeToTrade(target.Player)
            || target.Player.TradeRequest is { } pending && pending.FromId != player.EntityId && pending.ExpiresMs >= nowMs)
        {
            Reply(player, TradeMessages.Of(TradeStatus.Busy));
            return;
        }

        Reply(player, TradeMessages.Format(TradeMessages.Requested, name));
        target.Player.TradeRequest = new TradeRequest(player.EntityId, player.Character.Name, nowMs + TradeLimits.RequestMs);
    }

    private void AnswerTrade(Connection connection, ulong fromId, bool accept)
    {
        var player = connection.Player;
        if (player.TradeRequest is not { } request || request.FromId != fromId)
        {
            return;
        }

        player.TradeRequest = null;
        if (!accept || request.ExpiresMs < UnixNowMs || !_connections.TryGetValue(fromId, out var from) || from.FieldId != connection.FieldId)
        {
            return;
        }

        if (!InTradeReach(player, from.Player))
        {
            Reply(player, TradeMessages.Format(TradeMessages.TooFar, from.Player.Character.Name));
            return;
        }

        if (!FreeToTrade(player) || !FreeToTrade(from.Player))
        {
            Reply(player, TradeMessages.Of(TradeStatus.Busy));
            return;
        }

        var trade = new Trade(from.Player.EntityId, player.EntityId);
        from.Player.Trade = trade;
        player.Trade = trade;
        RefreshTrade(trade);
    }

    // The same offer sent again changes nothing (no unlock, no lock delay for the partner). A refused one answers the offer
    // the server holds again, so the window drops its guess.
    private void OfferTrade(WorldPlayer player, TradeOffer offer)
    {
        if (player.Trade is not { Executing: false } trade || trade.IsLocked(trade.SideOf(player.EntityId))
            || trade.OfferOf(trade.SideOf(player.EntityId)).SameAs(offer))
        {
            return;
        }

        var status = TradeRules.Check(player.Character, offer);
        if (status != TradeStatus.Ok)
        {
            Reply(player, TradeMessages.Of(status));
            RefreshTrade(trade);
            return;
        }

        trade.SetOffer(trade.SideOf(player.EntityId), offer, UnixNowMs);
        RefreshTrade(trade);
    }

    // As the original checks on its lock button: the side must be able to take what it receives. The trade and the partner's
    // session come back once both are locked.
    private Trade? LockTrade(WorldPlayer player, out PlayerSession? partnerSession)
    {
        partnerSession = null;
        if (player.Trade is not { Executing: false } trade || !_connections.TryGetValue(trade.Of(1 - trade.SideOf(player.EntityId)), out var partner))
        {
            return null;
        }

        var side = trade.SideOf(player.EntityId);
        if (trade.IsLocked(side) || UnixNowMs - trade.ChangedAtMs(1 - side) < TradeLimits.LockDelayMs || !AsShown(trade, player, partner.Player))
        {
            return null;
        }

        var status = TradeRules.Exchange(player.Character.CloneForInventoryOperation(), trade.OfferOf(side),
            partner.Player.Character.CloneForInventoryOperation(), trade.OfferOf(1 - side), out var failedSide);
        if (status != TradeStatus.Ok && failedSide == 0)
        {
            Reply(player, TradeMessages.Of(status));
            return null;
        }

        trade.Lock(side);
        RefreshTrade(trade);
        if (!trade.BothLocked || partner.Session is not { } session)
        {
            return null;
        }

        trade.Executing = true;
        partnerSession = session;
        return trade;
    }

    // The exchange did not go through: the trade opens again unlocked, or ends when a side cancelled it meanwhile.
    private void ReleaseTrade(Trade trade)
    {
        if (trade.Cancelling)
        {
            EndTrade(trade, TradeStatus.Cancelled);
            return;
        }

        trade.Unlock();
        RefreshTrade(trade);
    }

    private void LeaveTrade(WorldPlayer player)
    {
        if (player.Trade is { } trade)
        {
            EndTrade(trade, trade.Executing ? null : TradeStatus.Cancelled);
        }
    }

    // status: the line both sides read, none when the exchange went through.
    private void EndTrade(Trade trade, TradeStatus? status)
    {
        foreach (var id in (ReadOnlySpan<ulong>)[trade.First, trade.Second])
        {
            if (_connections.TryGetValue(id, out var connection) && ReferenceEquals(connection.Player.Trade, trade))
            {
                connection.Player.Trade = null;
            }

            if (status is { } said)
            {
                Notify(id, ChatKind.Server, TradeMessages.Of(said));
            }
        }
    }

    // Tick only: what each side sees.
    private void RefreshTrade(Trade trade)
    {
        if (_connections.TryGetValue(trade.First, out var first) && _connections.TryGetValue(trade.Second, out var second))
        {
            trade.SetViews(TradeViewOf(trade, 0, first.Player, second.Player), TradeViewOf(trade, 1, second.Player, first.Player));
        }
    }

    private static TradeView TradeViewOf(Trade trade, int side, WorldPlayer viewer, WorldPlayer partner)
    {
        var offer = trade.OfferOf(1 - side);
        var items = new List<OwnedItemView>(offer.Entries.Count);
        foreach (var entry in offer.Entries)
        {
            var anchor = partner.Character.Inventory.Find(entry.ItemId);
            if (anchor >= 0 && partner.Character.Inventory[anchor] is { } item)
            {
                items.Add(OwnedItemView.Of(item, entry.Cell, -1) with { Details = ItemDetailView.Of(item, viewer.Character) });
            }
        }

        return new TradeView(partner.EntityId, partner.Character.Name, trade.OfferOf(side), trade.IsLocked(side), items, offer.Money,
            trade.IsLocked(1 - side));
    }
}
