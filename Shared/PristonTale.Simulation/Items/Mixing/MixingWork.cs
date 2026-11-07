using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Items.Mixing;

public enum MixResult : byte
{
    Failed = 0,
    Mixed = 1,
    Reconstructed = 2,
    Refined = 3
}

// Tax: the castle's tax on a mix's price and on an alchemy's force orbs together.
public sealed record MixingSettings(int PricePercent = 100, int AlchemyPricePercent = 100, int Tax = 0)
{
    public static readonly MixingSettings Default = new();
}

public sealed record SealRule(string Key, int MinimumLevel, int MaximumLevel, int Count);

// Every operation runs on a candidate copy: the materials and the price are spent whatever the result. A mix with no
// formula for the item and its sheltoms fails and leaves the item as it was.
public static class MixingWork
{
    public const int MaxMaterials = 12;

    public const int AlchemyMode = 200;
    public const string RespecStone = "BI190";

    public static readonly SealRule[] Seals =
    [
        new("SE101", 0, 105, 2),
        new("SE102", 40, 79, 2),
        new("SE103", 0, 39, 2),
        new("SE104", 0, 80, 2)
    ];

    // The alchemy price of each sheltom, Lucidy first; the alchemist takes up to the eleventh.
    private static readonly int[] AlchemyPrices = [200, 500, 1000, 2000, 4000, 5000, 10000, 20000, 30000, 40000, 50000];

    // Timed and clan items of mixable slots that the NPC refuses.
    private static readonly HashSet<string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        "OR206", "OR207", "OR208", "OR209", "OR210", "OR211", "OR212", "OR213", "OR214", "OR215", "OR216", "OR217", "OR218", "OR219",
        "OR220", "OR221", "OR222", "OR223", "OR224", "OR225", "OR227", "OR228", "OR229", "OR230", "OR231", "OR232", "OR233", "OR234",
        "OR235", "OR236", "OR237", "OR238", "OR239", "OR240", "OA132", "OA133", "OA134", "OA135", "OA136", "OA137", "OA138", "OA139",
        "OA140", "OA141", "OA142", "OA231", "OA232", "OA233", "OA234", "DB131", "DB132", "DB133", "DB134"
    };

    public static bool Mixable(string key, ItemKind kind, bool wearable, int upgradeLevel, bool maturing, int mixSheltom) =>
        wearable && kind != ItemKind.Quest && !Refused.Contains(key) && upgradeLevel == 0 && !maturing && mixSheltom == 0;

    public static bool Mixable(ItemInstance item) =>
        Mixable(item.Definition.CatalogKey, item.Definition.Kind, item.Definition.EquipSlot.HasValue, item.UpgradeLevel, item.Maturity is not null,
            item.MixSheltom);

    public static int SheltomOrdinal(string key) => Aging.AgingRules.SheltomOrdinal(key);

    public static SealRule SealOf(string key) => Seals.FirstOrDefault(seal => seal.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static long Price(long itemPrice, MixingSettings settings = null)
    {
        settings ??= MixingSettings.Default;
        return Social.Castle.CastleRules.WithTax(itemPrice * Math.Max(0, settings.PricePercent) / 100, settings.Tax);
    }

    // The force orbs' price together, from the sheltoms' alchemy prices.
    public static long AlchemyTotal(long sheltomPrices, MixingSettings settings = null) =>
        Social.Castle.CastleRules.WithTax(sheltomPrices, (settings ?? MixingSettings.Default).Tax);

    public static long AlchemyPrice(int ordinal, MixingSettings settings = null) =>
        ordinal >= 1 && ordinal <= AlchemyPrices.Length
            ? (long)AlchemyPrices[ordinal - 1] * Math.Max(0, (settings ?? MixingSettings.Default).AlchemyPricePercent) / 100
            : -1;

    public static NpcServiceStatus TryMix(PlayerCharacter candidate, Guid itemId, IReadOnlyList<Guid> materials, ISimRng rng,
        out ItemWorkResult work, out long price, MixingSettings settings = null)
    {
        work = null;
        price = 0;
        var inventory = candidate.Inventory;
        var anchor = inventory.Find(itemId);
        if (anchor < 0 || inventory[anchor] is not { } item || !Mixable(item))
        {
            return NpcServiceStatus.Rejected;
        }

        if (materials is null || materials.Count == 0 || materials.Count > MaxMaterials + 1)
        {
            return NpcServiceStatus.WrongMaterials;
        }

        var counts = new int[MixFormulas.SheltomKinds];
        var sheltoms = 0;
        SealRule seal = null;
        var seals = 0;
        var stone = false;
        foreach (var group in materials.GroupBy(static id => id))
        {
            var at = inventory.Find(group.Key);
            if (group.Key == itemId || at < 0 || inventory[at] is not { } material || material.Count < group.Count())
            {
                return NpcServiceStatus.WrongMaterials;
            }

            var key = material.Definition.CatalogKey;
            var ordinal = SheltomOrdinal(key);
            if (ordinal > 0)
            {
                sheltoms += group.Count();
                if (ordinal <= MixFormulas.SheltomKinds)
                {
                    counts[ordinal - 1] += group.Count();
                }

                continue;
            }

            if (SealOf(key) is { } found && (seal is null || seal == found))
            {
                seal = found;
                seals += group.Count();
                continue;
            }

            if (key.Equals(RespecStone, StringComparison.OrdinalIgnoreCase) && !stone && group.Count() == 1)
            {
                stone = true;
                continue;
            }

            return NpcServiceStatus.WrongMaterials;
        }

        var reconstruct = seal is not null;
        if (sheltoms + seals > MaxMaterials || reconstruct && (sheltoms > 0 || seals != seal.Count || item.Roll.RequiredLevel < seal.MinimumLevel
                || item.Roll.RequiredLevel > seal.MaximumLevel || item.Definition.UniqueItem != 0)
            || !reconstruct && (stone || sheltoms == 0))
        {
            return NpcServiceStatus.WrongMaterials;
        }

        price = Price(item.Roll.Price, settings);
        if (!candidate.TryChangeMoney(-price))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        foreach (var id in materials)
        {
            inventory.ConsumeAt(inventory.Find(id));
        }

        var code = item.Definition.Code;
        if (reconstruct)
        {
            var remade = Reconstruct(item, candidate, stone, rng);
            inventory.TakeAt(anchor);
            if (!inventory.TryPlace(remade, anchor))
            {
                return NpcServiceStatus.NoSpace;
            }

            work = new ItemWorkResult((byte)MixResult.Reconstructed, remade.InstanceId, code);
            return NpcServiceStatus.Ok;
        }

        if (MixFormulas.Find(ItemCodes.FamilyOf(item.Definition.CatalogKey), counts) is not { } formula)
        {
            work = new ItemWorkResult((byte)MixResult.Failed, item.InstanceId, code);
            return NpcServiceStatus.Ok;
        }

        MixFormulas.Apply(item, formula, counts);
        work = new ItemWorkResult((byte)MixResult.Mixed, item.InstanceId, code);
        return NpcServiceStatus.Ok;
    }

    public static NpcServiceStatus TryRefine(PlayerCharacter candidate, IReadOnlyList<Guid> materials, ItemCatalog items, ISimRng rng,
        out ItemWorkResult work, out long price, MixingSettings settings = null)
    {
        work = null;
        price = 0;
        var inventory = candidate.Inventory;
        if (materials is null || materials.Count == 0 || materials.Count > MaxMaterials)
        {
            return NpcServiceStatus.WrongMaterials;
        }

        var orbs = new List<ItemDefinition>();
        foreach (var group in materials.GroupBy(static id => id))
        {
            var at = inventory.Find(group.Key);
            if (at < 0 || inventory[at] is not { } material || material.Count < group.Count())
            {
                return NpcServiceStatus.WrongMaterials;
            }

            var ordinal = SheltomOrdinal(material.Definition.CatalogKey);
            var cost = AlchemyPrice(ordinal, settings);
            if (cost < 0 || !items.TryGet(ItemCodes.Parse($"FO1{ordinal:00}"), out var orb))
            {
                return NpcServiceStatus.WrongMaterials;
            }

            price += cost * group.Count();
            orbs.AddRange(Enumerable.Repeat(orb, group.Count()));
        }

        price = AlchemyTotal(price, settings);
        if (!candidate.TryChangeMoney(-price))
        {
            return NpcServiceStatus.NotEnoughGold;
        }

        foreach (var id in materials)
        {
            inventory.ConsumeAt(inventory.Find(id));
        }

        foreach (var orb in orbs)
        {
            if (!inventory.TryAdd(ItemRoller.Create(orb, 1, rng)))
            {
                return NpcServiceStatus.NoSpace;
            }
        }

        work = new ItemWorkResult((byte)MixResult.Refined, Guid.Empty, orbs[0].Code);
        return NpcServiceStatus.Ok;
    }

    // A new copy of the item under the same identity. With the respec stone, half the time it takes the owner's class
    // specialization and otherwise any other class's.
    private static ItemInstance Reconstruct(ItemInstance item, PlayerCharacter owner, bool stone, ISimRng rng)
    {
        var definition = item.Definition;
        ItemInstance remade;
        var own = (uint)JobBits.Of(owner.Class.Class);
        if (stone && rng.NextInclusive(1, 100) < 51 && ItemRoller.CanTakeSpec(definition, own))
        {
            remade = ItemRoller.Create(definition, 1, rng, new ItemRollOverrides(own));
        }
        else
        {
            remade = ItemRoller.Create(definition, 1, rng);
            for (var attempt = 0; stone && remade.Roll.SpecJob == own && attempt < 32; attempt++)
            {
                remade = ItemRoller.Create(definition, 1, rng);
            }
        }

        return new ItemInstance(definition, 1)
        {
            InstanceId = item.InstanceId,
            DurabilityCurrent = remade.DurabilityCurrent,
            DurabilityMax = remade.DurabilityMax,
            Roll = remade.Roll
        };
    }
}
