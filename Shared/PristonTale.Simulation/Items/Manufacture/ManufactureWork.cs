using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Items.Manufacture;

public enum ManufactureResult : byte
{
    Failed = 0,
    Made = 1
}

// The craftsman makes an item from a recipe (WR1nn for a weapon, DR1nn for armour, nn 1 to 11) and the three runes the
// recipe names, each an A rune (PR3nn) or a B rune (PR4nn). Which runes are B picks the item: a weapon recipe makes the
// axe, talon, hammer, scythe, bow, sword, javelin or staff of its tier; an armour recipe the armour, robe, shield, orb,
// gauntlets, armlets or boots, and all three B runes any of them. The Memory armour recipe makes the class's costume from
// three A runes. Half the attempts succeed; recipe and runes are spent and a third of the item's price is charged either way.
public static class ManufactureWork
{
    public const int Runes = 3;
    public const int SuccessPercent = 50;
    public const int Recipes = 11;

    // The rune numbers each recipe asks for in its three slots.
    private static readonly int[][] RecipeRunes =
        [[1, 2, 3], [2, 3, 4], [3, 4, 5], [3, 4, 5], [3, 4, 5], [3, 4, 5], [4, 5, 6], [5, 6, 7], [6, 7, 8], [9, 10, 11], [12, 13, 14]];

    // The families a weapon or armour recipe makes, by rune combination (bit 2: the first rune is a B rune, bit 1 the second,
    // bit 0 the third), with their numbers at the first recipe; each later recipe makes the next number, recipes 7 and 8
    // one further.
    private static readonly (string Family, int First)[] Weapons =
        [("WA1", 14), ("WC1", 14), ("WH1", 15), ("WP1", 15), ("WS1", 16), ("WS2", 17), ("WT1", 15), ("WM1", 15)];

    private static readonly (string Family, int First)[] Armours =
        [("DA1", 16), ("DA2", 16), ("DS1", 14), ("OM1", 15), ("DG1", 14), ("OA2", 14), ("DB1", 14)];

    private static readonly int[] Steps = [0, 1, 2, 3, 4, 5, 7, 8];

    // The tenth armour recipe's only combination (A, B, A) and what it makes.
    private const int SnowCombination = 0b010;
    private const string SnowArmour = "OA136";
    private const int AllB = 0b111;

    // The recipe's number (1 to 11) and whether it makes armour; 0 for an item that is no recipe.
    public static int RecipeOf(string key, out bool armour)
    {
        armour = key is { Length: 5 } && key.StartsWith("DR1", StringComparison.OrdinalIgnoreCase);
        var weapon = key is { Length: 5 } && key.StartsWith("WR1", StringComparison.OrdinalIgnoreCase);
        return (armour || weapon) && int.TryParse(key.AsSpan(3), out var number) && number is >= 1 and <= Recipes ? number : 0;
    }

    // The rune number the recipe asks for in the slot (0 to 2); 0 for no recipe.
    public static int RuneFor(int recipe, int slot) => recipe is >= 1 and <= Recipes && slot is >= 0 and < Runes ? RecipeRunes[recipe - 1][slot] : 0;

    // A rune's number (PR3nn or PR4nn) and whether it is a B rune; 0 for anything else.
    public static int RuneOf(string key, out bool b)
    {
        var colour = CraftMaterials.Colour(CraftMaterials.RuneB, key);
        b = colour > 0;
        return b ? colour : CraftMaterials.Colour(CraftMaterials.RuneA, key);
    }

    // What the recipe makes from the rune combination; every candidate when the combination picks one at random, none
    // when the recipe makes nothing from it.
    public static IReadOnlyList<string> Products(int recipe, bool armour, int combination, PlayerClass playerClass)
    {
        if (recipe is < 1 or > Recipes || combination is < 0 or > AllB)
        {
            return [];
        }

        if (recipe <= Steps.Length)
        {
            var step = Steps[recipe - 1];
            if (!armour)
            {
                return [Key(Weapons[combination], step)];
            }

            return combination == AllB ? [.. Armours.Select(armourFamily => Key(armourFamily, step))] : [Key(Armours[combination], step)];
        }

        return (recipe, armour, combination) switch
        {
            (10, true, SnowCombination) => [SnowArmour],
            (11, true, 0) => [Costume(playerClass)],
            _ => []
        };
    }

    // The costume of the Memory recipe: a robe for the classes that wear one, the women's cut for a woman.
    public static string Costume(PlayerClass playerClass) =>
        (ItemClassRestrictions.WearsRobe(playerClass) ? "DA2" : "DA1") + (ClassProfiles.Of(playerClass).Female ? "52" : "51");

    public static long Fee(ItemDefinition made) => made.Price / 3;

    // The rune combination of the three runes in their slots; -1 when one is not the rune the recipe asks for there.
    public static int Combination(int recipe, IReadOnlyList<string> runeKeys)
    {
        if (runeKeys is not { Count: Runes })
        {
            return -1;
        }

        var combination = 0;
        for (var slot = 0; slot < Runes; slot++)
        {
            if (RuneOf(runeKeys[slot], out var b) != RuneFor(recipe, slot) || RuneFor(recipe, slot) == 0)
            {
                return -1;
            }

            combination = combination << 1 | (b ? 1 : 0);
        }

        return combination;
    }

    // The runes come in slot order. Everything a draw could decide is settled before it, so a refusal never follows a roll:
    // the fee is the dearest candidate's, and every candidate must fit the bag and the weight once the recipe and runes are
    // spent. Then the product is drawn and the success rolled.
    public static NpcServiceStatus TryManufacture(PlayerCharacter candidate, Guid recipeId, IReadOnlyList<Guid> runes, ItemCatalog items,
        ISimRng rng, out ItemWorkResult work, out long fee)
    {
        work = null;
        fee = 0;
        var inventory = candidate.Inventory;
        var recipeAt = inventory.Find(recipeId);
        var armour = false;
        var recipe = recipeAt >= 0 && inventory[recipeAt] is { } recipeItem ? RecipeOf(recipeItem.Definition.CatalogKey, out armour) : 0;
        if (recipe == 0)
        {
            return NpcServiceStatus.Rejected;
        }

        if (runes is not { Count: Runes } || runes.Contains(recipeId) || runes.Distinct().Count() != Runes)
        {
            return NpcServiceStatus.WrongMaterials;
        }

        var keys = new List<string>(Runes);
        var spentWeight = CarriedWeight.Of(inventory[recipeAt]!);
        foreach (var id in runes)
        {
            var at = inventory.Find(id);
            if (at < 0 || inventory[at] is not { } rune)
            {
                return NpcServiceStatus.WrongMaterials;
            }

            keys.Add(rune.Definition.CatalogKey);
            spentWeight += CarriedWeight.Of(rune);
        }

        var products = Products(recipe, armour, Combination(recipe, keys), candidate.Class.Class)
            .Select(key => items.TryGet(ItemCodes.Parse(key), out var definition) ? definition : null).OfType<ItemDefinition>().ToArray();
        if (products.Length == 0)
        {
            return NpcServiceStatus.WrongMaterials;
        }

        fee = products.Max(Fee);
        if (candidate.Money < fee)
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        inventory.ConsumeAt(recipeAt);
        foreach (var id in runes)
        {
            inventory.ConsumeAt(inventory.Find(id));
        }

        if (products.Any(product => !inventory.HasRoom(product.GridWidth, product.GridHeight)))
        {
            return NpcServiceStatus.NoSpace;
        }

        if (!CarriedWeight.CanTake(candidate, products.Max(static product => CarriedWeight.Of(product, 1)) - spentWeight))
        {
            return NpcServiceStatus.OverWeight;
        }

        candidate.TryChangeMoney(-fee);
        var made = products[products.Length == 1 ? 0 : rng.NextExclusive(0, products.Length)];

        if (rng.NextExclusive(0, 100) >= SuccessPercent)
        {
            work = new ItemWorkResult((byte)ManufactureResult.Failed, Guid.Empty, made.Code);
            return NpcServiceStatus.Ok;
        }

        var item = ItemRoller.Create(made, 1, rng);
        if (!inventory.TryAdd(item))
        {
            return NpcServiceStatus.NoSpace;
        }

        work = new ItemWorkResult((byte)ManufactureResult.Made, item.InstanceId, made.Code);
        return NpcServiceStatus.Ok;
    }

    private static string Key((string Family, int First) family, int step) => $"{family.Family}{family.First + step:00}";
}
