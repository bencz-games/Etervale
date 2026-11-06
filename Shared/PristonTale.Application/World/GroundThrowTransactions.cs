using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// What a player puts on the ground, an item (Drop: Count of a bag stack or of the held item) or gold (DropGold: Count of
// it): taken on a copy of the character, saved, then laid where the player stood once the save commits
// (PreparedThrowOperation), with the gold taken from the character there.
internal static class GroundThrowTransactions
{
    public static PreparedCharacterOperation<InventoryOutcome> Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, InventoryCommand command)
    {
        var original = player.Character;
        var gold = command.Action == InventoryAction.DropGold;
        var anchor = gold ? -1 : original.Inventory.Find(command.ItemId);
        var fromHand = !gold && anchor < 0 && original.Held?.InstanceId == command.ItemId;
        var item = anchor >= 0 ? original.Inventory[anchor] : fromHand ? original.Held : null;
        if (command.ExpectedRevision != original.InventoryRevision || command.Count < 1 || command.Count > (gold ? original.Money : item?.Count ?? 0))
        {
            return Refused(original, GameTextId.StaleInventory);
        }

        if (item is not null && !ItemDropRules.CanDrop(original, item))
        {
            return Refused(original, GameTextId.CannotDrop);
        }

        if (field.GroundLoot.Count >= FieldGroundLoot.Capacity)
        {
            return Refused(original, GameTextId.NoRoomOnGround);
        }

        var candidate = original.CloneForInventoryOperation();
        var thrown = gold ? null : fromHand ? candidate.TakeHeld(command.ItemId, command.Count) : candidate.Inventory.TakeFrom(anchor, command.Count);
        if (gold)
        {
            candidate.TryChangeMoney(-command.Count);
            candidate.TouchInventory();
        }

        candidate.RecordInventoryOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        return new PreparedThrowOperation(original, candidate, snapshot, new(true, candidate.InventoryRevision, GameText.None), field,
            thrown?.Copy(thrown.Count, preserveId: true), gold ? command.Count : 0, player.Position, player.EntityId);
    }

    private static PreparedCharacterOperation<InventoryOutcome> Refused(PlayerCharacter original, GameTextId refusal) =>
        new(original, null, null, new(false, original.InventoryRevision, GameText.Of(refusal)));
}
