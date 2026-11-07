using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.World.Npcs;

// Shop prices of the original: an item is bought at its catalog price as its lowest form; a copy sells for a fifth of
// its own value (a specialized drop costs a fifth more) after wear, where the worn share of the price keeps a quarter of
// its value; a repair costs an eighth of the value lost to wear. A durability of zero counts as one. Stackable goods
// sell per unit at a fifth, as many units as the seller asks. A purchase that would carry the character over the weight
// limit is refused.
public static class NpcShopRules
{
    public const int SellDivisor = 5;
    public const int RepairDivisor = 8;
    private const float WornShareValue = 0.25f;

    public static int WornValue(int price, int durability, int maxDurability)
    {
        float current = durability == 0 ? 1 : durability;
        float maximum = maxDurability == 0 ? 1 : maxDurability;
        var intact = price * (current / maximum);
        return (int)(intact + (price - intact) * WornShareValue);
    }

    // The castle's tax is on each unit, as the catalog offers it.
    public static long BuyPrice(ItemDefinition definition, int count, int tax = 0) =>
        Social.Castle.CastleRules.WithTax(Math.Max(0, definition.Price), tax) * count;

    // What count units of the item sell for (a stack's units; an item sells whole).
    public static long SellPrice(ItemInstance item, int count)
    {
        var price = Math.Max(0, item.Price);
        if (item.Definition.IsStackable)
        {
            return (long)(price / SellDivisor) * count;
        }

        var worn = WornValue(price, item.DurabilityCurrent, item.DurabilityMax) / SellDivisor;
        return Math.Min(price / SellDivisor, worn);
    }

    public static long RepairCost(ItemInstance item)
    {
        if (!NeedsRepair(item))
        {
            return 0;
        }

        var price = Math.Max(0, item.Price);
        return Math.Max(0, price - WornValue(price, item.DurabilityCurrent, item.DurabilityMax)) / RepairDivisor;
    }

    public static bool NeedsRepair(ItemInstance item) => item.DurabilityMax > 0 && item.DurabilityCurrent < item.DurabilityMax;

    public static bool CanSell(ItemInstance item) => item.Definition.Kind != ItemKind.Quest;

    public static bool IsValidCount(ItemDefinition definition, int count) =>
        count >= 1 && (definition.IsStackable ? count <= definition.MaxStack : count == 1);

    public static NpcServiceStatus TryBuy(PlayerCharacter character, ItemDefinition definition, int count, int anchor, out long cost, int tax = 0)
    {
        cost = 0;
        if (!IsValidCount(definition, count))
        {
            return NpcServiceStatus.Rejected;
        }

        cost = BuyPrice(definition, count, tax);
        if (character.Money < cost)
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        if (!CarriedWeight.CanTake(character, CarriedWeight.Of(definition, count)))
        {
            return NpcServiceStatus.OverWeight;
        }

        var item = new ItemInstance(definition, count);
        var placed = anchor >= 0 ? character.Inventory.TryPlace(item, anchor) : character.Inventory.TryAdd(item);
        if (!placed)
        {
            return NpcServiceStatus.NoSpace;
        }

        return character.TryChangeMoney(-cost) ? NpcServiceStatus.Ok : NpcServiceStatus.Rejected;
    }

    public static NpcServiceStatus TrySell(PlayerCharacter character, Guid itemId, int count, out long proceeds)
    {
        proceeds = 0;
        var anchor = character.Inventory.Find(itemId);
        if (anchor < 0 || character.Inventory[anchor] is not { } item || !CanSell(item) || Quests.RankUpByQuestWeapon.IsBound(character, item)
            || !IsValidCount(item.Definition, count) || count > item.Count)
        {
            return NpcServiceStatus.Rejected;
        }

        proceeds = SellPrice(item, count);
        if (!MoneyLimits.CanCarry(character, proceeds))
        {
            return NpcServiceStatus.GoldLimit;
        }

        character.Inventory.TakeFrom(anchor, count);
        return character.TryChangeMoney(proceeds) ? NpcServiceStatus.Ok : NpcServiceStatus.GoldLimit;
    }

    public static NpcServiceStatus TryRepair(PlayerCharacter character, Guid itemId, out long cost)
    {
        cost = 0;
        var item = character.FindOwnedItem(itemId);
        if (item is null || !NeedsRepair(item))
        {
            return NpcServiceStatus.Rejected;
        }

        cost = RepairCost(item);
        return Charge(character, cost, [itemId]);
    }

    public static NpcServiceStatus TryRepairAll(PlayerCharacter character, out long cost)
    {
        cost = 0;
        var damaged = new List<Guid>();
        foreach (var item in character.OwnedItems())
        {
            if (NeedsRepair(item))
            {
                cost += RepairCost(item);
                damaged.Add(item.InstanceId);
            }
        }

        return damaged.Count == 0 ? NpcServiceStatus.Rejected : Charge(character, cost, damaged);
    }

    private static NpcServiceStatus Charge(PlayerCharacter character, long cost, IReadOnlyList<Guid> items)
    {
        // The original repairs only while gold remains after paying.
        if (cost > 0 && character.Money <= cost)
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        foreach (var id in items)
        {
            character.RestoreDurability(id);
        }

        return character.TryChangeMoney(-cost) ? NpcServiceStatus.Ok : NpcServiceStatus.NotEnoughGold;
    }
}

public static class MoneyLimits
{
    // Gold a character may carry, by level and class tier, as the original checks before selling.
    public static long CarryLimit(int level, int tier)
    {
        if (level <= 10)
        {
            return 200_000;
        }

        return tier switch
        {
            0 => Math.Max(200_000L, level * 200_000L - 1_800_000L),
            1 => 10_000_000,
            2 => 50_000_000,
            3 => 500_000_000,
            _ => 700_000_000
        };
    }

    public static bool CanCarry(PlayerCharacter character, long gold) => character.Money <= CarryLimit(character.Level, character.Tier) - gold;
}
