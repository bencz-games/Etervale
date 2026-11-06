using PristonTale.Application.Sessions;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.Trades;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Application.Services;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Touched only by the tick thread.
    private readonly Random _dialogRandom = new();

    // A click answers with the NPC's typed services and one of its lines, chosen at random as the original does.
    public Task<NpcInteraction> InteractNpcAsync(PlayerSession session, ulong npcId)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field)
                || !field.TryGetNpc(npcId, out var npc) || !NpcServiceTransactions.CanReach(connection.Player, npc))
            {
                return NpcInteraction.Rejected(GameText.Of(GameTextId.NpcOutOfReach));
            }

            var definition = npc.Definition;
            var localizedDefinition = _localizedData.Get(session.Language, _worldData.Current).Npcs.TryGet(definition.Code, out var translated)
                ? translated
                : definition;
            var offers = definition.Services
                .Select(service => new NpcServiceOffer(service.Kind, service.Value, NpcServiceAvailability.IsAvailable(service), PricePercent(service)))
                .ToArray();
            string[] line = localizedDefinition.Dialog.Count == 0 ? [] : [localizedDefinition.Dialog[_dialogRandom.Next(localizedDefinition.Dialog.Count)]];
            return new NpcInteraction(true, npc.EntityId, localizedDefinition.Label, offers, line, GameText.None);
        });
    }

    private int PricePercent(NpcService service) => service.Kind switch
    {
        NpcServiceKind.ItemAging => _options.Aging.PricePercent,
        NpcServiceKind.ItemMix when service.Value == Simulation.Items.Mixing.MixingWork.AlchemyMode => _options.Mixing.AlchemyPricePercent,
        NpcServiceKind.ItemMix => _options.Mixing.PricePercent,
        _ => 100
    };

    public Task<NpcServiceOutcome> CheckNpcServiceAsync(PlayerSession session, NpcServiceCommand command)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, DurableFailures.Text(DurableFailure.NotPlaying));
            }

            return NpcServiceTransactions.Check(connection.Player, field, command, out _, out _);
        });
    }

    public Task<PreparedNpcOperation?> PrepareNpcServiceAsync(PlayerSession session, NpcServiceCommand command)
    {
        return EnqueueAsync<PreparedNpcOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            // A trade's offered items and gold change only through its exchange.
            if (connection.Player.Shop is not null || connection.Player.Trade is not null)
            {
                var revision = connection.Player.Character.InventoryRevision;
                var message = GameText.Of(connection.Player.Shop is not null ? GameTextId.CloseShopFirst : GameTextId.CloseTradeFirst);
                return new PreparedNpcOperation(connection.Player.Character, null, null, new NpcServiceReply(NpcServiceOutcome.Failed(NpcServiceStatus.Rejected,
                    message, revision)));
            }

            return NpcServiceTransactions.Prepare(session, connection.Player, field, command, _worldData.Current.Items, _catalog);
        });
    }

    public Task<bool> CommitNpcServiceAsync(PlayerSession session, PreparedNpcOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            // SaveGate remains held through commit, including when the connection leaves during storage I/O.
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null
                || !operation.Original.CanChangeMoney(operation.MoneyDelta))
            {
                throw new CommitInvariantException("Character ownership changed during NPC service commit.");
            }

            // A warehouse change moves items and gold between the character and its account's warehouse.
            if (operation.Warehouse is not null
                && Transfer(Persistence.Abstractions.GameLogs.ItemTransferKind.WarehouseDeposit, Guid.NewGuid(), session.FieldId) is { } deposit
                && Transfer(Persistence.Abstractions.GameLogs.ItemTransferKind.WarehouseWithdrawal, Guid.NewGuid(), session.FieldId) is { } withdrawal)
            {
                var self = CharacterIdOf(session);
                deposit.Left(operation.Original, operation.Candidate, self, null, session.AccountId);
                deposit.Gold(self, null, -operation.MoneyDelta, session.AccountId);
                withdrawal.Arrived(operation.Original, operation.Candidate, null, self, session.AccountId);
                withdrawal.Gold(null, self, operation.MoneyDelta, session.AccountId);
                Log(deposit);
                Log(withdrawal);
            }

            operation.Original.CommitInventoryOperation(operation.Candidate);
            operation.Original.TryChangeMoney(operation.MoneyDelta);
            RecordSpending(operation);
            if (operation.Warehouse is not null)
            {
                session.Warehouse = operation.Warehouse;
            }

            if (operation.Reply.Arrival is { } arrival)
            {
                Arrive(session, arrival);
            }

            return true;
        }, waitForCapacity: true);
    }

    private void Arrive(PlayerSession session, TeleportArrival arrival)
    {
        if (!TryGetOwnConnection(session, out var connection)
            || !_catalog.TryGetFieldByCode(arrival.FieldCode, out var destination))
        {
            session.FieldId = arrival.FieldCode;
            session.SpawnPosition = arrival.Position;
            return;
        }

        var position = GetField(arrival.FieldCode).Terrain.PlaceOnFloor(arrival.Position);
        var inVillage = destination.State == FieldState.Village;
        Relocate(connection, arrival.FieldCode, position, inVillage);
        connection.Player.EnterField(position, inVillage);
    }
}
