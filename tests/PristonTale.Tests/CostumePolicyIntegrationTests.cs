using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class CostumePolicyIntegrationTests
{
    private const string Manifest = """
        {"version":1,"costumes":[
          {"itemKey":"CA547","gender":"any","bodies":{"7":"char/tmABCD/Cmd040.inx"}},
          {"itemKey":"CA647","gender":"any","bodies":{}},
          {"itemKey":"CA548","gender":"any","bodies":{}},
          {"itemKey":"CA531","gender":"male","bodies":{}},
          {"itemKey":"CA533","gender":"female","bodies":{}},
          {"itemKey":"DA131","gender":"male","bodies":{}},
          {"itemKey":"DA133","gender":"female","bodies":{}},
          {"itemKey":"DA147","gender":"any","bodies":{}},
          {"itemKey":"DA130","gender":"any","bodies":{}}
        ]}
        """;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("costume policy: gender restricts sales but visuals do not", ShopPolicyAsync);
        yield return ("costume policy: all flagged DA and CA refuse combat equipment", NeverRealArmorAsync);
        yield return ("costume policy: wardrobe respects gender and preserves armor stats", WardrobePolicyAsync);
        yield return ("costume policy: rejects unknown CA but accepts model-less costumes", ManifestPolicyAsync);
    }

    private static ItemDefinition Of(string key, ItemKind kind = ItemKind.Armor) =>
        new(ItemCodes.Parse(key), key, kind, kind == ItemKind.Armor ? EquipmentSlot.Armor : null)
        {
            CatalogKey = key, Price = 100,
            AllowedJobs = ItemClassRestrictions.AllowedJobs(key)
        };

    private static Task ShopPolicyAsync()
    {
        var policy = CostumeCatalog.FromJson(Manifest);
        var classes = new CharacterClassRegistry();
        var priestess = classes.CreatePlayer(PlayerClass.Priestess, "Priestess", 30);
        var magician = classes.CreatePlayer(PlayerClass.Magician, "Magician", 30);
        priestess.RestoreProgress(0, 1_000);
        magician.RestoreProgress(0, 1_000);

        // Hopy's CA5 and CA6 codes are BOTH unisex, not a male/female pair.
        Assert.True(NpcShopRules.CanBuy(priestess, Of("CA547", ItemKind.Misc), policy)
            && NpcShopRules.CanBuy(priestess, Of("CA647", ItemKind.Misc), policy)
            && NpcShopRules.CanBuy(magician, Of("CA647", ItemKind.Misc), policy),
            "the missing class model prevented buying a unisex Hopy");
        Assert.True(!NpcShopRules.CanBuy(priestess, Of("DA131"), policy)
            && NpcShopRules.CanBuy(magician, Of("DA131"), policy)
            && NpcShopRules.CanBuy(priestess, Of("DA133"), policy)
            && !NpcShopRules.CanBuy(magician, Of("DA133"), policy),
            "DA cosmetic genders were not enforced");
        Assert.True(NpcShopRules.CanBuy(priestess, Of("CA533", ItemKind.Misc), policy)
            && !NpcShopRules.CanBuy(magician, Of("CA533", ItemKind.Misc), policy)
            && NpcShopRules.CanBuy(magician, Of("CA531", ItemKind.Misc), policy),
            "CA cosmetic genders were not enforced");
        Assert.True(NpcShopRules.TryBuy(priestess, Of("DA131"), 1, -1, out _, costumes: policy) == NpcServiceStatus.Rejected
            && NpcShopRules.TryBuy(priestess, Of("CA547", ItemKind.Misc), 1, -1, out _, costumes: policy) == NpcServiceStatus.Ok,
            "purchase ignored gender or a model-less costume");

        // Ordinary armor/robes STILL observe the original class restrictions.
        var robe = ItemEquipmentMetadata.Apply(Of("DA201"));
        var armor = ItemEquipmentMetadata.Apply(Of("DA101"));
        Assert.True(NpcShopRules.CanBuy(priestess, robe) && !NpcShopRules.CanBuy(priestess, armor)
            && NpcShopRules.CanBuy(magician, robe), "ordinary combat armour/robe rules regressed");
        return Task.CompletedTask;
    }

    private static Task NeverRealArmorAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Magician, "Costume", 30);
        foreach (var key in new[] { "DA131", "DA130", "CA547", "CA647" })
        {
            var item = new ItemInstance(Of(key));
            Assert.True(!character.CanHold(EquipmentSlot.Armor, item)
                && !character.CanEquip(EquipmentSlot.Armor, item),
                $"{key} was equippable as real armour");
            Assert.True(character.TryReceive(item)
                && character.GetEquipped(EquipmentSlot.Armor) is null,
                $"{key} was auto-equipped into real armour");
        }
        Assert.True(!CostumeItemPolicy.IsCosmetic("DA101") && CostumeItemPolicy.IsCosmetic("DA130")
            && !CostumeItemPolicy.IsCosmetic("CA101") && CostumeItemPolicy.IsCosmetic("CA648"),
            "normal items or unknown CA were incorrectly reclassified");
        return Task.CompletedTask;
    }

    private static Task WardrobePolicyAsync()
    {
        var policy = CostumeCatalog.FromJson(Manifest);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Priestess, "Priestess", 30);
        var male = new ItemInstance(Of("DA131"));
        var female = new ItemInstance(Of("DA133"));
        var unisex = new ItemInstance(Of("CA647", ItemKind.Misc));
        Assert.True(character.Inventory.TryPlace(male, 0)
            && character.Inventory.TryPlace(female, 1)
            && character.Inventory.TryPlace(unisex, 2), "test costume items do not fit in bag");
        var before = character.Derived;
        Assert.True(!character.TryWearCostume(male.InstanceId, policy), "priestess wore a male costume");
        Assert.True(character.TryWearCostume(female.InstanceId, policy)
            && character.ActiveCostumeKey(policy) == "DA133" && character.Derived == before,
            "female cosmetic attachment changed combat stats");
        Assert.True(character.TryWearCostume(unisex.InstanceId, policy)
            && character.ActiveCostumeKey(policy) == "CA647" && character.Derived == before,
            "unisex costume without a model was refused");
        Assert.True(character.ActiveCostumeKey(CostumeCatalog.Empty) == "", "catalog opt-in was bypassed");
        return Task.CompletedTask;
    }

    private static Task ManifestPolicyAsync()
    {
        var policy = CostumeCatalog.FromJson(Manifest);
        Assert.True(policy.Contains("CA548") && policy.Allows("CA548", true) && policy.Allows("CA548", false)
            && !policy.Supports("CA548", 6) && !policy.Supports("CA548", 7),
            "an empty body map was incorrectly treated as gender restriction");
        foreach (var invalid in new[] {
            """{"version":1,"costumes":[{"itemKey":"CA101","gender":"any","bodies":{}}]}""",
            """{"version":1,"costumes":[{"itemKey":"CA531","gender":"other","bodies":{}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA101","gender":"any","bodies":{}}]}"""
        })
        {
            var refused = false;
            try { CostumeCatalog.FromJson(invalid); }
            catch (InvalidDataException) { refused = true; }
            Assert.True(refused, "an invalid, non-cosmetic or unknown manifest entry was accepted");
        }
        return Task.CompletedTask;
    }
}
