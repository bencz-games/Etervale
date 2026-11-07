using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Parties;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

internal static class GroundPickupTransactions
{
    // Sharers: the picker's party members in its field and share range (WorldServer.GoldSharers).
    public static PreparedCharacterOperation<InventoryOutcome> Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, InventoryCommand command,
        List<WorldPlayer> sharers)
    {
        var original = player.Character;
        var dropId = command.ItemId;
        if (command.ExpectedRevision != original.InventoryRevision || command.Count != 1 || command.Destination is not (0 or InventoryCommand.PickupToBag)
            || !field.GroundLoot.TryReserve(dropId, command.OperationId, player, out var drop, out var item))
        {
            return new(original, null, null, new(false, original.InventoryRevision, GameText.Of(GameTextId.DropOutOfReach)));
        }

        bool prepared = false;
        try
        {
            var candidate = original.CloneForInventoryOperation();
            if (item is not null && !CarriedWeight.CanTake(candidate, CarriedWeight.Of(item)))
            {
                return new(original, null, null, new(false, original.InventoryRevision, GameText.Of(GameTextId.OverWeight)));
            }

            long gold = drop!.Gold ? drop.Amount : 0;
            var shares = PartyRules.SharesGold(gold, field.GroundLoot.FromMonster(dropId)) ? Shares(gold, sharers, out gold) : [];
            if (!candidate.ReceiveGroundLoot(item, (int)gold, command.Destination == InventoryCommand.PickupToBag))
            {
                return new(original, null, null, new(false, original.InventoryRevision, GameText.Of(GameTextId.PickupNoRoom)));
            }

            candidate.RecordInventoryOperation(command);
            var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
            var result = new PreparedPickupOperation(original, candidate, snapshot,
                new(true, candidate.InventoryRevision, GameText.None), field.GroundLoot, drop.Id, command.OperationId, gold, shares);
            prepared = true;
            return result;
        }
        finally
        {
            if (!prepared)
            {
                field.GroundLoot.Resolve(dropId, command.OperationId, committed: false);
            }
        }
    }

    // A member who cannot carry its part (MoneyLimits.CarryLimit) is left out and the gold is split again among the others.
    private static List<GoldShare> Shares(long gold, List<WorldPlayer> takers, out long pickerPart)
    {
        while (true)
        {
            var (mine, each) = PartyRules.SplitGold(gold, takers.Count + 1);
            if (each == 0)
            {
                pickerPart = gold;
                return [];
            }

            if (takers.RemoveAll(taker => !MoneyLimits.CanCarry(taker.Character, each)) > 0)
            {
                continue;
            }

            pickerPart = mine;
            var shares = new List<GoldShare>(takers.Count);
            foreach (var member in takers)
            {
                shares.Add(new GoldShare(member.EntityId, member.Character, each));
            }

            return shares;
        }
    }
}
