using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Items.Smelting;

public enum SmeltResult : byte
{
    Failed = 0,
    Smelted = 1
}

// The smelter turns three ores of one colour (PR1nn) and the sheltom the colour asks for into that colour's crystal
// (PR2nn), and three crystals of one colour with their sheltom into one of the colour's two runes (PR3nn or PR4nn, even
// odds). Smelting always succeeds; ores cost 25,000 gold and crystals 50,000. The green crystal (PR208) is not smelted.
public static class SmeltingWork
{
    public const int Pieces = 3;
    public const long OreFee = 25_000;
    public const long CrystalFee = 50_000;

    // The sheltom ordinal each colour asks for, by colour number (PR1nn ores 1-8, PR2nn crystals 1-14); 0 for none.
    private static readonly int[] OreSheltoms = [6, 7, 8, 9, 10, 11, 12, 13];
    private static readonly int[] CrystalSheltoms = [7, 8, 9, 10, 11, 12, 13, 0, 3, 4, 5, 3, 4, 5];

    public static bool IsOre(string key) => Sheltom(OreSheltoms, CraftMaterials.Ore, key) > 0;

    public static bool IsCrystal(string key) => Sheltom(CrystalSheltoms, CraftMaterials.Crystal, key) > 0;

    // The sheltom ordinal three pieces of this ore or crystal need; 0 for anything the smelter does not take.
    public static int SheltomFor(string key) => Math.Max(Sheltom(OreSheltoms, CraftMaterials.Ore, key), Sheltom(CrystalSheltoms, CraftMaterials.Crystal, key));

    // The fee of a smelt of this piece; 0 for anything else.
    public static long Fee(string key) => IsOre(key) ? OreFee : IsCrystal(key) ? CrystalFee : 0;

    public static NpcServiceStatus TrySmelt(PlayerCharacter candidate, IReadOnlyList<Guid> materials, ItemCatalog items, ISimRng rng,
        out ItemWorkResult work, out long fee)
    {
        work = null;
        fee = 0;
        var inventory = candidate.Inventory;
        if (materials is null || materials.Count != Pieces + 1)
        {
            return NpcServiceStatus.WrongMaterials;
        }

        string piece = null;
        var pieces = 0;
        var sheltom = 0;
        foreach (var group in materials.GroupBy(static id => id))
        {
            var at = inventory.Find(group.Key);
            if (at < 0 || inventory[at] is not { } material || material.Count < group.Count())
            {
                return NpcServiceStatus.WrongMaterials;
            }

            var key = material.Definition.CatalogKey;
            if (SheltomFor(key) > 0 && (piece is null || piece.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                piece = key;
                pieces += group.Count();
            }
            else if (AgingRules.SheltomOrdinal(key) is > 0 and var ordinal && sheltom == 0 && group.Count() == 1)
            {
                sheltom = ordinal;
            }
            else
            {
                return NpcServiceStatus.WrongMaterials;
            }
        }

        if (piece is null || pieces != Pieces || sheltom != SheltomFor(piece) || !items.TryGet(ItemCodes.Parse(Product(piece, rng)), out var made))
        {
            return NpcServiceStatus.WrongMaterials;
        }

        fee = Fee(piece);
        if (!candidate.TryChangeMoney(-fee))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        foreach (var id in materials)
        {
            inventory.ConsumeAt(inventory.Find(id));
        }

        var smelted = ItemRoller.Create(made, 1, rng);
        if (!inventory.TryAdd(smelted))
        {
            return NpcServiceStatus.NoSpace;
        }

        work = new ItemWorkResult((byte)SmeltResult.Smelted, smelted.InstanceId, made.Code);
        return NpcServiceStatus.Ok;
    }

    // An ore's crystal, or one of a crystal's two runes.
    private static string Product(string piece, ISimRng rng)
    {
        if (IsOre(piece))
        {
            return CraftMaterials.Key(CraftMaterials.Crystal, CraftMaterials.Colour(CraftMaterials.Ore, piece));
        }

        var colour = CraftMaterials.Colour(CraftMaterials.Crystal, piece);
        return CraftMaterials.Key(rng.NextExclusive(0, 2) == 0 ? CraftMaterials.RuneA : CraftMaterials.RuneB, colour);
    }

    private static int Sheltom(int[] table, string family, string key) =>
        CraftMaterials.Colour(family, key) is var colour && colour >= 1 && colour <= table.Length ? table[colour - 1] : 0;
}
