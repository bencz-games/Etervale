using PristonTale.Application.GameMasters;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

// A trip through the wing warp gates (WingGateTransactions): the tick checks the stone, the wing, the level and the fee,
// the character is stored, then the tick takes the fee and moves the player to the destination stone.
public sealed partial class WorldServer
{
    public Task<PreparedWingGateOperation?> PrepareWingGateAsync(PlayerSession session, WingGateCommand command)
    {
        return EnqueueAsync<PreparedWingGateOperation?>(session.EntityId, () =>
            TryGetPlaying(session, out var connection) && _fields.TryGetValue(connection.FieldId, out var field)
                ? WingGateTransactions.Prepare(session, connection.Player, field, _catalog, command, Volatile.Read(ref _castleOwnerClanId))
                : null);
    }

    // Exempt from the player's quota: the trip is already stored.
    public Task<bool> CommitWingGateAsync(PlayerSession session, PreparedWingGateOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var fee = operation.Outcome.Fee;
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Arrival is not { } arrival
                || !operation.Original.CanChangeMoney(-fee))
            {
                throw new CommitInvariantException("Character ownership changed during wing gate commit.");
            }

            operation.Original.TryChangeMoney(-fee);
            RecordSpending(CurrencySink.Teleports, fee, Simulation.Social.Castle.CastleRules.TaxIn(fee, operation.Tax));
            Arrive(session, arrival);
            return true;
        }, waitForCapacity: true);
    }
}
