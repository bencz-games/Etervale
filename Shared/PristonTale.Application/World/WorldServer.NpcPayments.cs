using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public enum NpcPaymentStatus : byte
{
    Ok = 0,
    OutOfReach = 1,
    Busy = 2,
    LevelTooLow = 3,
    NotEnoughGold = 4,
    TooMuchGold = 5
}

// Gold a service of an NPC takes (or gives, a negative fee) that the world prepared: the character with the gold
// changed, or the reason it was refused (then without a snapshot).
public sealed record PreparedNpcPayment(PlayerCharacter Original, PlayerCharacter? Candidate, CharacterSaveSnapshot? Snapshot, NpcPaymentStatus Status,
    long MoneyDelta = 0) : PreparedOperation(Original, Candidate, Snapshot);

// The gold an NPC's service takes or gives (founding or handing over a clan at a Clan Master, Bless Castle's owner at its
// butler, a clan's Bellatra income at the secretary): the player stands within the reach of the service (of that value,
// when given), out of a shop and a trade, at the level, and the gold fits.
public sealed partial class WorldServer
{
    public Task<PreparedNpcPayment?> PrepareNpcPaymentAsync(PlayerSession session, ulong npcEntityId, NpcServiceKind service, long fee, int minimumLevel = 0,
        int? serviceValue = null)
    {
        return EnqueueAsync<PreparedNpcPayment?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            var player = connection.Player;
            var character = player.Character;
            var reach = NpcServiceTransactions.Check(player, field, new NpcServiceCommand(Guid.Empty, npcEntityId, service, NpcServiceAction.Open), out _,
                out var offered);
            var status = !reach.Ok || serviceValue is { } value && offered?.Value != value ? NpcPaymentStatus.OutOfReach
                : player.Shop is not null || player.Trade is not null ? NpcPaymentStatus.Busy
                : character.Level < minimumLevel ? NpcPaymentStatus.LevelTooLow
                : character.Money < fee ? NpcPaymentStatus.NotEnoughGold
                : !character.CanChangeMoney(-fee) ? NpcPaymentStatus.TooMuchGold
                : NpcPaymentStatus.Ok;
            if (status != NpcPaymentStatus.Ok)
            {
                return new PreparedNpcPayment(character, null, null, status);
            }

            var candidate = character.CloneForInventoryOperation();
            candidate.TryChangeMoney(-fee);
            var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, field.FieldId, player.Position);
            return new PreparedNpcPayment(character, candidate, snapshot, NpcPaymentStatus.Ok, -fee);
        });
    }

    public Task<bool> CommitNpcPaymentAsync(PlayerSession session, PreparedNpcPayment payment)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, payment.Original) || payment.Candidate is null || !payment.Original.CanChangeMoney(payment.MoneyDelta))
            {
                throw new CommitInvariantException("Character ownership changed during an NPC payment.");
            }

            payment.Original.CommitInventoryOperation(payment.Candidate);
            payment.Original.TryChangeMoney(payment.MoneyDelta);
            return true;
        }, waitForCapacity: true);
    }
}
