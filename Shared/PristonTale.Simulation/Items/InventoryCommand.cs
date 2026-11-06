using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Items;

public enum InventoryAction
{
    Move,
    Split,
    Merge,
    Equip,
    Unequip,
    ConsumePotion,
    Pickup,
    // Sets the item down over exactly one other bag item, which takes its place (Inventory.TrySwap).
    Swap,
    // Puts Count of a bag item on the ground where the player stands (handled by the Application, as Pickup is).
    Drop,
    // Potions from the bag or a box into belt box Destination, Count of them (0: as many as fit; PlayerCharacter.TryPutOnBelt).
    BeltPut,
    // A belt box's potions into the bag at Destination (-1: wherever they fit).
    BeltTake,
    // Shows bag page Destination (0 A, 1 B) and hides the other (PlayerCharacter.TrySwitchBagPage); ItemId, when set, is the
    // bag item on the cursor, which goes along to the page shown.
    SwitchBagPage,
    // Holds weapon pair Destination (0 A, 1 B) and stores the other (PlayerCharacter.TrySwitchWeaponSet); ItemId is unused.
    SwitchWeaponSet,
    // Puts Count gold on the ground where the player stands, as Drop does an item; ItemId is unused.
    DropGold,
    // A special item left in the bag into the first free box of the special item box (PlayerCharacter.TryStoreQuestItem).
    StoreQuestItem
}

public sealed record InventoryCommand(Guid OperationId, long ExpectedRevision, InventoryAction Action,
    Guid ItemId, int Destination, int Count, EquipmentSlot Slot)
{
    // A pickup's Destination with the inventory open: the item goes into the bag as a stack of its own, for the client to
    // carry, as the original puts it on the mouse; 0 receives it as any item (PlayerCharacter.TryReceive).
    public const int PickupToBag = 1;
}

public sealed record InventoryOutcome(bool Ok, long Revision, GameText Message);
public sealed record AppliedInventoryOperation(InventoryCommand Command, long Revision) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Command?.OperationId ?? Guid.Empty;
}

public static class InventoryCommands
{
    public static bool Apply(PlayerCharacter character, InventoryCommand command)
    {
        var sideSwitch = command.Action is InventoryAction.SwitchBagPage or InventoryAction.SwitchWeaponSet;
        if (command.OperationId == Guid.Empty || command.ItemId == Guid.Empty && !sideSwitch || !Enum.IsDefined(command.Action)
            || command.ExpectedRevision != character.InventoryRevision)
        {
            return false;
        }

        var anchor = character.Inventory.Find(command.ItemId);
        // The held item (on neither page) is set down by the same three actions, wherever it lands.
        var held = anchor < 0 && character.Held?.InstanceId == command.ItemId;
        return command.Action switch
        {
            InventoryAction.Move or InventoryAction.Merge or InventoryAction.Swap when held => character.TryPutDownHeld(command.ItemId, command.Destination),
            InventoryAction.Move => character.Inventory.TryMove(anchor, command.Destination),
            InventoryAction.Split => character.Inventory.TrySplit(anchor, command.Destination, command.Count),
            InventoryAction.Merge => character.Inventory.TryMerge(anchor, command.Destination),
            InventoryAction.Swap => character.Inventory.TrySwap(anchor, command.Destination),
            InventoryAction.Equip => Fitted(character, character.TryEquipFromInventory(anchor, command.Slot)),
            InventoryAction.Unequip => character.GetEquipped(command.Slot)?.InstanceId == command.ItemId && Fitted(character, character.TryUnequip(command.Slot)),
            InventoryAction.ConsumePotion => command.Count == 1 && character.Life > 0 && (anchor >= 0
                ? character.Inventory[anchor]?.Definition is { Kind: ItemKind.Potion, Potion.IsValid: true } && character.Inventory.ConsumeAt(anchor)
                : character.TryConsumeFromBelt(command.ItemId)),
            InventoryAction.BeltPut => character.TryPutOnBelt(command.ItemId, command.Destination, command.Count),
            InventoryAction.StoreQuestItem => character.TryStoreQuestItem(command.ItemId),
            InventoryAction.BeltTake => character.TryTakeFromBelt(command.ItemId, command.Destination),
            InventoryAction.SwitchBagPage => character.TrySwitchBagPage(command.Destination, command.ItemId),
            InventoryAction.SwitchWeaponSet => Fitted(character, character.TrySwitchWeaponSet(command.Destination)),
            _ => false
        };
    }

    // Gear changes the potion space, which the belt then fits (only after a change that went through).
    private static bool Fitted(PlayerCharacter character, bool changed)
    {
        if (changed)
        {
            character.FitBeltToPotionSpace();
        }

        return changed;
    }

    // The item rule that refuses a command, for the player's message; null when the command fails for another reason.
    public static GameText Refusal(PlayerCharacter character, InventoryCommand command)
    {
        switch (command.Action)
        {
            case InventoryAction.Equip:
                var anchor = character.Inventory.Find(command.ItemId);
                return anchor >= 0 && character.Inventory[anchor] is { } item && item.Definition.IsEquippable && !ItemUsability.CanUse(character, item)
                    ? GameText.Of(GameTextId.CannotUseYet)
                    : GameText.None;
            case InventoryAction.Unequip:
                return CarriedWeight.IsOverweight(character) ? GameText.Of(GameTextId.OverWeight) : GameText.None;
            default:
                return GameText.None;
        }
    }
}
