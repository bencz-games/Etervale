using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Items.Usable;

// The puzzle collector's exchange: the finished board leaves the inventory and one reward joins it.
public static class PuzzleExchange
{
    // The finished board of the revealed (unsealed) pieces in the bag.
    public static IReadOnlyList<int> Board(Inventory inventory) =>
        Puzzles.Board(inventory.Occupied().Where(static slot => !slot.Item.Sealed).Select(static slot => (slot.Item.Definition.CatalogKey, slot.Anchor)),
            inventory.Width);

    // Takes the board and adds the drawn reward (thirty Mystic Mana Potions when the reward's item is unknown). The
    // exchange needs 120 free weight and room for the reward.
    public static NpcServiceStatus TryExchange(PlayerCharacter candidate, ItemCatalog items, ISimRng rng, out ItemInstance reward)
    {
        reward = null;
        var inventory = candidate.Inventory;
        var board = Board(inventory);
        if (board.Count != 8 || !Puzzles.IsComplete(Puzzles.Value(board.Select(anchor => inventory[anchor].Definition.CatalogKey))))
        {
            return NpcServiceStatus.WrongPuzzle;
        }

        if (candidate.CarriedWeight + Puzzles.ExchangeWeight > candidate.Derived.MaxWeight)
        {
            return NpcServiceStatus.OverWeight;
        }

        foreach (var anchor in board)
        {
            inventory.RemoveAt(anchor);
        }

        var drawn = Puzzles.Draw(rng.NextInclusive(0, 10000));
        if (!TryMake(items, drawn, rng, out reward) && !TryMake(items, Puzzles.Fallback, rng, out reward) || !inventory.TryAdd(reward))
        {
            return NpcServiceStatus.NoSpace;
        }

        return NpcServiceStatus.Ok;
    }

    private static bool TryMake(ItemCatalog items, PuzzleReward entry, ISimRng rng, out ItemInstance item)
    {
        item = null;
        if (!ItemCodes.TryParse(entry.CatalogKey, out var code) || !items.TryGet(code, out var definition))
        {
            return false;
        }

        var count = Math.Clamp(entry.Count, 1, Math.Max(1, definition.MaxStack));
        item = definition.IsStackable ? new ItemInstance(definition, count) : ItemRoller.Create(definition, 1, rng);
        return true;
    }
}
