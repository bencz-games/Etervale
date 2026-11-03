using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The potion belt: what goes into its boxes, what comes out, and how a smaller potion space trims it.
public sealed partial class PlayerCharacter
{
    public PotionBelt Belt { get; } = new();

    // Potions from the bag or another box into a box, as the original sets them: an empty box takes up to the potion space,
    // a box of the same potion fills up to it, and a box of another potion first sends its stack to the bag (from another
    // box the two stacks change places). count 0 moves as many as fit; what the space leaves stays where it was.
    public bool TryPutOnBelt(Guid itemId, int box, int count)
    {
        var anchor = Inventory.Find(itemId);
        var fromBox = anchor < 0 ? Belt.Find(itemId) : -1;
        var source = anchor >= 0 ? Inventory[anchor] : fromBox >= 0 ? Belt[fromBox] : null;
        if (!Belt.Contains(box) || count < 0 || fromBox == box || source is not { Definition: { Kind: ItemKind.Potion, Potion.IsValid: true } })
        {
            return false;
        }

        var held = Belt[box];
        if (held is not null && held.Definition != source.Definition)
        {
            if (fromBox >= 0)
            {
                Belt.Set(box, source);
                Belt.Set(fromBox, held);
                OnInventoryChanged();
                return true;
            }

            if (!Inventory.TryAdd(held))
            {
                return false;
            }

            Belt.TakeAt(box);
            held = null;
        }

        var wanted = count == 0 ? source.Count : Math.Min(count, source.Count);
        var amount = Math.Min(wanted, Derived.PotionSpace - (held?.Count ?? 0));
        // A box already full of this potion: nothing moves (an emptied box of another potion always has room).
        if (amount <= 0)
        {
            return false;
        }

        var moved = fromBox >= 0 ? TakeFromBox(fromBox, amount) : Inventory.TakeFrom(anchor, amount);
        if (held is null)
        {
            Belt.Set(box, moved);
        }
        else
        {
            held.Count += amount;
        }

        OnInventoryChanged();
        return true;
    }

    // A potion the character receives (TryReceive) goes to the belt first: it fills the boxes holding the same potion up to
    // the potion space, then the empty boxes in order, and the bag takes what the belt leaves. The original takes the
    // boxes strictly in order and sends a box's overflow to the bag at once; here every box with room is used. False when
    // the belt takes none of it, or the bag has no room for the rest (then nothing moves).
    private bool TryReceiveOnBelt(ItemInstance item)
    {
        if (item is not { Count: > 0, Definition: { Kind: ItemKind.Potion, Potion.IsValid: true } })
        {
            return false;
        }

        Span<int> amounts = stackalloc int[PotionBelt.Boxes];
        var remaining = item.Count;
        // First pass: boxes of the same potion; second pass: empty boxes.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var box = 0; box < PotionBelt.Boxes && remaining > 0; box++)
            {
                var held = Belt[box];
                if (pass == 0 ? held?.Definition != item.Definition : held is not null)
                {
                    continue;
                }

                var amount = Math.Min(remaining, Derived.PotionSpace - (held?.Count ?? 0));
                if (amount > 0)
                {
                    amounts[box] = amount;
                    remaining -= amount;
                }
            }
        }

        if (remaining == item.Count)
        {
            return false;
        }

        // The received stack is left as it came (callers report its count); its id goes with the first new stack.
        var keepId = true;
        if (remaining > 0)
        {
            if (!Inventory.TryAdd(item.Copy(remaining, preserveId: true)))
            {
                return false;
            }

            keepId = false;
        }

        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            var amount = amounts[box];
            if (amount == 0)
            {
                continue;
            }

            if (Belt[box] is { } held)
            {
                held.Count += amount;
            }
            else
            {
                Belt.Set(box, item.Copy(amount, keepId));
                keepId = false;
            }
        }

        OnInventoryChanged();
        return true;
    }

    // A box's stack into the bag: at the anchor (joining a stack of the same potion there), or wherever it fits when the
    // anchor is -1.
    public bool TryTakeFromBelt(Guid itemId, int anchor)
    {
        var box = Belt.Find(itemId);
        if (box < 0 || anchor >= Inventory.Capacity)
        {
            return false;
        }

        var item = Belt[box];
        var placed = anchor < 0 ? Inventory.TryAdd(item)
            : Inventory[anchor] is not null ? Inventory.TryJoin(anchor, item)
            : Inventory.TryPlace(item, anchor);
        if (!placed)
        {
            return false;
        }

        Belt.TakeAt(box);
        OnInventoryChanged();
        return true;
    }

    // A saved box's stack, as loaded: a valid potion, in an empty box, within the potion space.
    public bool RestoreBelt(int box, ItemInstance item)
    {
        if (!Belt.Contains(box) || Belt[box] is not null || item is not { Count: > 0, Definition: { Kind: ItemKind.Potion, Potion.IsValid: true } }
            || item.Count > Derived.PotionSpace)
        {
            return false;
        }

        Belt.Set(box, item);
        return true;
    }

    public bool TryConsumeFromBelt(Guid itemId)
    {
        var box = Belt.Find(itemId);
        if (box < 0 || Belt[box] is not { Definition.Potion.IsValid: true })
        {
            return false;
        }

        TakeFromBox(box, 1);
        OnInventoryChanged();
        return true;
    }

    // After the potion space falls (gear taken off), a box keeps what fits and the rest goes back to the bag when it has
    // room, as the original resets the belt.
    public void FitBeltToPotionSpace()
    {
        foreach (var (_, item) in Belt.Occupied())
        {
            if (item.Count > Derived.PotionSpace && Inventory.TryAdd(item.Copy(item.Count - Derived.PotionSpace)))
            {
                item.Count = Derived.PotionSpace;
                OnInventoryChanged();
            }
        }
    }

    private ItemInstance TakeFromBox(int box, int amount)
    {
        var item = Belt[box];
        return amount < item.Count ? item.Split(amount) : Belt.TakeAt(box);
    }
}
