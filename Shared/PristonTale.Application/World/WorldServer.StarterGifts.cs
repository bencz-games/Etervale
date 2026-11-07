using System.Threading.Channels;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World;

// What a starter gift gave: the item's name, or null when it found no room (it is offered again after the interval).
public sealed record StarterGiftOutcome(string? ItemName, uint ItemCode = 0, int Index = 0);

// The starter gifts of new characters (StarterGifts): the tick hands a playing character whose gift is due to the gift
// service, which gives it as a durable operation; the next comes StarterGifts.Interval after that one ended.
public sealed partial class WorldServer
{
    private readonly Channel<PlayerSession> _starterGifts = SessionRequests();

    private uint _serverTick;

    public ChannelReader<PlayerSession> StarterGiftRequests => _starterGifts.Reader;

    // Work the tick queues for a session and a service runs off the tick (quest upkeep, starter gifts).
    private static Channel<PlayerSession> SessionRequests() => Channel.CreateUnbounded<PlayerSession>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });

    private uint GiftTick(TimeSpan after) => _serverTick + (uint)(after.TotalSeconds * TickHz);

    private void OfferStarterGifts(uint serverTick)
    {
        if (!_options.GiveStarterGifts || serverTick % TickHz != 0)
        {
            return;
        }

        foreach (var connection in _connections.Values)
        {
            if (connection.Session is { IsPlaying: true } session && !connection.GiftRequested && serverTick >= connection.NextGiftTick
                && connection.Player.Character.StarterGiftsReceived < StarterGifts.Count)
            {
                connection.GiftRequested = true;
                _starterGifts.Writer.TryWrite(session);
            }
        }
    }

    // The next gift the class has: a perfect copy worn on the first free slot that takes it, otherwise left in the bag. A
    // gift the catalog lacks counts as given; one without room waits (no snapshot).
    public Task<PreparedCharacterOperation<StarterGiftOutcome>?> PrepareStarterGiftAsync(PlayerSession session)
    {
        return EnqueueAsync<PreparedCharacterOperation<StarterGiftOutcome>?>(NoOwner, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection)
                || connection.Player.Character.StarterGiftsReceived >= StarterGifts.Count)
            {
                return null;
            }

            var original = connection.Player.Character;
            var items = _worldData.Current.Items;
            var index = StarterGifts.Next(items, original.Definition.Class, original.StarterGiftsReceived);
            var item = StarterGifts.Create(items, original.Definition.Class, index);
            var (candidate, snapshot) = Receive(session, connection, item, candidate => candidate.RestoreStarterGifts(index + 1));
            return new PreparedCharacterOperation<StarterGiftOutcome>(original, candidate, snapshot,
                new StarterGiftOutcome(candidate is null ? null : item?.DisplayName, item?.Definition.Code ?? 0, index));
        }, waitForCapacity: true);
    }

    public Task CommitStarterGiftAsync(PlayerSession session, PreparedCharacterOperation<StarterGiftOutcome> operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, operation, "a starter gift");
            operation.Original.CommitInventoryOperation(candidate);
            operation.Original.RestoreStarterGifts(candidate.StarterGiftsReceived);
            if (operation.Outcome.ItemName is not null && TryGetPlaying(session, out var connection) && _fields.TryGetValue(connection.FieldId, out var field))
            {
                field.ItemEvents.Add(new ItemEffectView(connection.Player.EntityId, operation.Outcome.ItemCode,
                    ItemEffectKind.StarterGift, connection.Player.Position, operation.Outcome.Index));
            }

            return true;
        }, waitForCapacity: true);
    }

    // The gift service is done with the session's request: the next gift is due after the interval.
    public Task EndStarterGiftAsync(PlayerSession session)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (TryGetOwnConnection(session, out var connection))
            {
                connection.GiftRequested = false;
                connection.NextGiftTick = GiftTick(StarterGifts.Interval);
            }

            return true;
        }, waitForCapacity: true);
    }

    // Tick only. A copy of the playing character with the item (if any) received, changed by adjust, and its capture;
    // no copy when the item does not fit.
    private (PlayerCharacter? Candidate, CharacterSaveSnapshot? Snapshot) Receive(PlayerSession session, Connection connection, ItemInstance? item,
        Action<PlayerCharacter>? adjust)
    {
        var candidate = connection.Player.Character.CloneForInventoryOperation();
        if (item is not null && !candidate.ReceiveGroundLoot(item, 0))
        {
            return (null, null);
        }

        adjust?.Invoke(candidate);
        return (candidate, CharacterRecordFactory.Capture(session.Loaded!, candidate, connection.FieldId, connection.Player.Position));
    }

    // An item made on the tick (none: the character no longer qualifies) into a playing character's bag, given by the
    // server: Outcome false when it does not fit (nothing is written), null when the character does not play or qualify.
    private Task<PreparedCharacterOperation<bool>?> PrepareIntoBagAsync(ulong owner, PlayerSession session, Func<Connection, ItemInstance?> make,
        bool waitForCapacity)
    {
        return EnqueueAsync<PreparedCharacterOperation<bool>?>(owner, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || make(connection) is not { } item)
            {
                return null;
            }

            var (candidate, snapshot) = Receive(session, connection, item, null);
            return new PreparedCharacterOperation<bool>(connection.Player.Character, candidate, snapshot, candidate is not null);
        }, waitForCapacity);
    }

    private Task<bool> CommitIntoBagAsync(PlayerSession session, PreparedCharacterOperation<bool> operation,
        Persistence.Abstractions.GameLogs.ItemTransferKind kind, string what)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, operation, what);
            if (Transfer(kind, Guid.NewGuid(), session.FieldId) is { } log)
            {
                log.Arrived(operation.Original, candidate, null, CharacterIdOf(session));
                Log(log);
            }

            operation.Original.CommitInventoryOperation(candidate);
            return true;
        }, waitForCapacity: true);
    }

    // Tick only. The candidate of an item received, once the session still owns the character it was copied from.
    private static PlayerCharacter CheckReceived(PlayerSession session, PreparedOperation operation, string what) =>
        ReferenceEquals(session.Character, operation.Original) && operation.Candidate is { } candidate
            ? candidate
            : throw new CommitInvariantException($"Character ownership changed during {what}.");
}
