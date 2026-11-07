using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A clan payment the world prepared: the character with the fee taken, or the reason it was refused.
public sealed record PreparedClanPayment(PlayerCharacter Original, PlayerCharacter? Candidate, CharacterSaveSnapshot? Snapshot, ClanStatus Status,
    long MoneyDelta = 0) : PreparedOperation(Original, Candidate, Snapshot);

// The clan's part of the tick: the gold the Clan Master takes (founding a clan, handing it over), and the clan each player
// shows over its head (WorldPlayer.Clan, part of its looks).
public sealed partial class WorldServer
{
    // Within reach of a Clan Master, at the level, with the fee: the candidate pays it. A refusal has no snapshot.
    public Task<PreparedClanPayment?> PrepareClanPaymentAsync(PlayerSession session, ulong npcEntityId, long fee, int minimumLevel)
    {
        return EnqueueAsync<PreparedClanPayment?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            var player = connection.Player;
            var character = player.Character;
            var reach = NpcServiceTransactions.Check(player, field, new NpcServiceCommand(Guid.Empty, npcEntityId, NpcServiceKind.Clan, NpcServiceAction.Open),
                out _, out _);
            var status = !reach.Ok ? ClanStatus.OutOfReach
                : player.Shop is not null || player.Trade is not null ? ClanStatus.Busy
                : character.Level < minimumLevel ? ClanStatus.LevelTooLow
                : character.Money < fee ? ClanStatus.NotEnoughGold
                : ClanStatus.Ok;
            if (status != ClanStatus.Ok)
            {
                return new PreparedClanPayment(character, null, null, status);
            }

            var candidate = character.CloneForInventoryOperation();
            candidate.TryChangeMoney(-fee);
            var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, field.FieldId, player.Position);
            return new PreparedClanPayment(character, candidate, snapshot, ClanStatus.Ok, -fee);
        });
    }

    public Task<bool> CommitClanPaymentAsync(PlayerSession session, PreparedClanPayment payment)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, payment.Original) || payment.Candidate is null || !payment.Original.CanChangeMoney(payment.MoneyDelta))
            {
                throw new CommitInvariantException("Character ownership changed during a clan payment.");
            }

            payment.Original.CommitInventoryOperation(payment.Candidate);
            payment.Original.TryChangeMoney(payment.MoneyDelta);
            return true;
        }, waitForCapacity: true);
    }

    // The clan the player shows (null for none); its looks change, so viewers are described it again.
    public Task SetClanTagAsync(ulong entityId, ClanTag? tag) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_connections.TryGetValue(entityId, out var connection))
            {
                connection.Player.Clan = tag;
            }

            return true;
        }, waitForCapacity: true);
}
