using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// The shop a used mobile shop item opens anywhere: no NPC (entityId 0) and no stock, so it buys and repairs only, at the
// prices of a shop NPC. It stays open until the player closes it.
internal static class MobileShopTransactions
{
    public static bool Handles(NpcServiceCommand command) => command.NpcEntityId == 0 && command.Service == NpcServiceKind.Shop;

    public static PreparedNpcOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, NpcServiceCommand command)
    {
        var original = player.Character;
        if (!session.MobileShopOpen || player.IsDead)
        {
            return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.OutOfReach, "no shop is open here", original.InventoryRevision));
        }

        switch (command.Action)
        {
            case NpcServiceAction.Close:
                session.MobileShopOpen = false;
                return new PreparedNpcOperation(original, null, null, new NpcServiceReply(NpcServiceTransactions.Done(original, null, "")));
            case NpcServiceAction.Open:
            case NpcServiceAction.Refresh:
                return new PreparedNpcOperation(original, null, null, new NpcServiceReply(NpcServiceTransactions.Done(original, null, ""))
                {
                    Catalog = command.Action == NpcServiceAction.Open ? [] : null,
                    Quotes = NpcServiceTransactions.Quotes(original)
                });
            case NpcServiceAction.Sell:
            case NpcServiceAction.Repair:
            case NpcServiceAction.RepairAll:
                break;
            default:
                return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, "this shop only buys and repairs", original.InventoryRevision));
        }

        if (NpcServiceTransactions.Settled(original, null, command) is { } settled)
        {
            return settled;
        }

        var candidate = original.CloneForInventoryOperation();
        var status = NpcServiceTransactions.SellOrRepair(candidate, command);
        if (status != NpcServiceStatus.Ok)
        {
            return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(status, NpcServiceTransactions.Describe(status, "the shop refused"), original.InventoryRevision));
        }

        candidate.RecordServiceOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        var reply = new NpcServiceReply(NpcServiceTransactions.Done(candidate, null, "")) { Quotes = NpcServiceTransactions.Quotes(candidate) };
        return new PreparedNpcOperation(original, candidate, snapshot, reply, candidate.Money - original.Money);
    }
}
