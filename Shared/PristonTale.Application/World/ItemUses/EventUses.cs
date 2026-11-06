using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Texts;
using System.Globalization;

namespace PristonTale.Application.World.ItemUses;

// Event items: foods and gift boxes are eaten like potions (one at a time, never while dead or eating); a gift box needs
// room for a 2 x 2 item and spare weight, then its prize lands in the inventory. Chocolates grant Virtual Life to the
// gender they are meant for; a puzzle piece sealed as a lucky bag is revealed for 1000 gold.

internal sealed class FoodUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (use.Item.Definition.Potion is not { IsValid: true })
        {
            return ItemUseStatus.NotUsable;
        }

        return use.Context.Player.HasPendingPotion ? ItemUseStatus.Delay : ItemUseStatus.Used;
    }

    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate) =>
        new(ItemUseStatus.Used, Food: use.Item.Definition.Potion);

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        if (use.Operation.Food is { } food)
        {
            use.Player.QueuePotion(food);
        }

        return ItemUseEffect.None;
    }
}

internal sealed class GiftBoxUse : ItemUseAction
{
    // An eat motion that restores nothing: gift boxes and capsules are eaten for show.
    private static readonly PotionDefinition Meal = new(0, 0, 0, 0, 0, 0);

    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        var character = use.Character;
        if (use.Context.Player.HasPendingPotion)
        {
            return ItemUseStatus.Delay;
        }

        if (!character.Inventory.HasRoom(GiftBoxes.RoomWidth, GiftBoxes.RoomHeight))
        {
            return ItemUseStatus.NoSpace;
        }

        return character.CarriedWeight + GiftBoxes.WeightMargin(use.Rule.Argument) >= character.Derived.MaxWeight
            ? ItemUseStatus.OverWeight
            : ItemUseStatus.Used;
    }

    // The prize joins the candidate after the box left it; an unknown prize item or a full inventory refuses the box.
    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate)
    {
        var prize = GiftBoxes.Draw(use.Rule.Argument, use.Context.Pick);
        if (!TryCreate(use.Context.Items, prize.CatalogKey, prize.Count, out var item))
        {
            return new ItemUseExtra(ItemUseStatus.NotUsable);
        }

        if (!candidate.TryReceive(item))
        {
            return new ItemUseExtra(ItemUseStatus.NoSpace);
        }

        var message = item.Count > 1
            ? GameText.Of(GameTextId.ItemsReceived, item.Definition.Name, item.Count.ToString(CultureInfo.InvariantCulture))
            : GameText.Of(GameTextId.ItemReceived, item.Definition.Name);
        return new ItemUseExtra(ItemUseStatus.Used, Food: Meal, Message: message);
    }

    // The tiger capsule's eater cheers.
    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        if (use.Operation.Food is { } meal)
        {
            use.Player.QueuePotion(meal, cheers: use.Rule.Argument == GiftBoxes.TigerCapsule);
        }

        return new ItemUseEffect(ItemEffectKind.GiftBox);
    }

    // A new copy of a catalog item in its shop form; stackable items come in the given count.
    private static bool TryCreate(ItemCatalog items, string catalogKey, int count, out ItemInstance item)
    {
        item = null!;
        if (!ItemCodes.TryParse(catalogKey, out var code) || !items.TryGet(code, out var definition))
        {
            return false;
        }

        item = new ItemInstance(definition, Math.Clamp(count, 1, Math.Max(1, definition.MaxStack)));
        return true;
    }
}

internal sealed class ChocolateUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (!Chocolates.TryGet(use.Item.Definition.CatalogKey, out var chocolate))
        {
            return ItemUseStatus.NotUsable;
        }

        return use.Context.Player.InVillage ? ItemUseStatus.NotHere : Chocolates.Check(chocolate, use.Character);
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        var argument = use.Rule.Argument;
        use.Field.QueueItemEffect(use.Player.EntityId, Chocolates.Spec(argument), Chocolates.DurationMs(argument), argument);
        return ItemUseEffect.None;
    }
}

// A revealed puzzle piece stays in the inventory.
internal sealed class PuzzleBagUse : ItemUseAction
{
    public override bool Consumes => false;

    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (!use.Item.Sealed)
        {
            return ItemUseStatus.NothingToChange;
        }

        return use.Character.Money < Puzzles.RevealCost ? ItemUseStatus.NotEnoughGold : ItemUseStatus.Used;
    }

    public override ItemUseExtra Extend(in ItemUseRequest use, PlayerCharacter candidate)
    {
        var anchor = candidate.Inventory.Find(use.Item.InstanceId);
        if (anchor < 0 || !candidate.TryChangeMoney(-Puzzles.RevealCost))
        {
            return new ItemUseExtra(ItemUseStatus.NotUsable);
        }

        candidate.Inventory[anchor].Sealed = false;
        candidate.TouchInventory();
        return new ItemUseExtra(ItemUseStatus.Used, Money: -Puzzles.RevealCost, Message: GameText.Of(GameTextId.LuckyBagHeld, use.Item.Definition.Name));
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use) => new(ItemEffectKind.Drink);
}
