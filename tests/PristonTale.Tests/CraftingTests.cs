using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Manufacture;
using PristonTale.Simulation.Items.Mixing;
using PristonTale.Simulation.Items.Smelting;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// The smelter, the craftsman and the mixture reset.
internal static class CraftingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("smelting turns three ores and their sheltom into the crystal for 25,000 and three crystals into a rune for 50,000", SmeltAsync);
        yield return ("smelting refuses mixed colours, the wrong sheltom, a missing piece, the green crystal and a short purse", SmeltRefusalsAsync);
        yield return ("a recipe's runes pick the weapon or armour of its tier; all B armour runes pick any, Memory makes the class costume", ProductsAsync);
        yield return ("manufacture spends recipe and runes and a third of the item's price; half the attempts make the item", ManufactureAsync);
        yield return ("manufacture settles the dearest candidate's fee, bag room and weight before it draws, whatever the draw", ManufactureBeforeDrawAsync);
        yield return ("the mixture reset remakes a mixed item under its identity with its specialization and spends the stone", MixtureResetAsync);
    }

    private static ItemDefinition Def(string code, ItemKind kind = ItemKind.Material, int price = 0) =>
        WorldDataFactory.ToItemDefinition(ItemRecords.Blank(code) with { Kind = (int)kind, Price = price });

    private static PlayerCharacter Character(PlayerClass playerClass = PlayerClass.Fighter, long money = 1_000_000)
    {
        var character = new CharacterClassRegistry().CreatePlayer(playerClass, "Crafter", 100);
        character.TryChangeMoney(money);
        return character;
    }

    private static List<Guid> Place(PlayerCharacter character, params ItemDefinition[] items)
    {
        var ids = new List<Guid>();
        foreach (var definition in items)
        {
            var item = new ItemInstance(definition);
            Assert.True(character.Inventory.TryAdd(item), "a material did not fit");
            ids.Add(item.InstanceId);
        }

        return ids;
    }

    private static string[] Held(PlayerCharacter character) =>
        [.. character.Inventory.Items().Select(static item => item.Definition.CatalogKey).Order()];

    private static Task SmeltAsync()
    {
        var items = new ItemCatalog([Def("PR201"), Def("PR301"), Def("PR401")]);
        var character = Character();
        var ore = Def("PR101");
        var materials = Place(character, ore, ore, ore, Def("OS106", ItemKind.Sheltom));
        var money = character.Money;
        Assert.True(SmeltingWork.TrySmelt(character, materials, items, BoundRng.Low, out var work, out var fee) == NpcServiceStatus.Ok
            && work.Result == (byte)SmeltResult.Smelted && fee == SmeltingWork.OreFee && character.Money == money - SmeltingWork.OreFee
            && Held(character).SequenceEqual(["PR201"]), "three violet ores did not make the violet crystal");

        var crystal = Def("PR201");
        foreach (var (rng, rune) in new (ISimRng, string)[] { (BoundRng.Low, "PR301"), (BoundRng.High, "PR401") })
        {
            var smelter = Character();
            var pieces = Place(smelter, crystal, crystal, crystal, Def("OS107", ItemKind.Sheltom));
            Assert.True(SmeltingWork.TrySmelt(smelter, pieces, items, rng, out _, out var crystalFee) == NpcServiceStatus.Ok
                && crystalFee == SmeltingWork.CrystalFee && Held(smelter).SequenceEqual([rune]), $"three violet crystals did not make {rune}");
        }

        Assert.True(SmeltingWork.SheltomFor("PR108") == 13 && SmeltingWork.SheltomFor("PR212") == 3 && SmeltingWork.SheltomFor("PR214") == 5,
            "the sheltom a colour asks for differs");
        return Task.CompletedTask;
    }

    private static Task SmeltRefusalsAsync()
    {
        var items = new ItemCatalog([Def("PR201"), Def("PR202"), Def("PR308"), Def("PR408")]);
        var sheltom = Def("OS106", ItemKind.Sheltom);
        var violet = Def("PR101");
        var cases = new (string Name, ItemDefinition[] Materials, NpcServiceStatus Status)[]
        {
            ("mixed colours", [violet, violet, Def("PR102"), sheltom], NpcServiceStatus.WrongMaterials),
            ("the wrong sheltom", [violet, violet, violet, Def("OS107", ItemKind.Sheltom)], NpcServiceStatus.WrongMaterials),
            ("two ores", [violet, violet, sheltom], NpcServiceStatus.WrongMaterials),
            ("no sheltom", [violet, violet, violet, violet], NpcServiceStatus.WrongMaterials),
            ("the green crystal", [Def("PR208"), Def("PR208"), Def("PR208"), Def("OS114", ItemKind.Sheltom)], NpcServiceStatus.WrongMaterials)
        };
        foreach (var (name, materials, status) in cases)
        {
            var character = Character();
            var ids = Place(character, materials);
            var before = Held(character);
            Assert.True(SmeltingWork.TrySmelt(character, ids, items, BoundRng.Low, out _, out _) == status && Held(character).SequenceEqual(before),
                $"{name} was smelted or spent");
        }

        var poor = Character(money: SmeltingWork.OreFee - 1);
        var owned = Place(poor, violet, violet, violet, sheltom);
        Assert.True(SmeltingWork.TrySmelt(poor, owned, items, BoundRng.Low, out _, out _) == NpcServiceStatus.NotEnoughGold, "a short purse smelted");
        return Task.CompletedTask;
    }

    private static Task ProductsAsync()
    {
        Assert.True(ManufactureWork.RecipeOf("WR101", out var armour) == 1 && !armour && ManufactureWork.RecipeOf("DR111", out armour) == 11 && armour
            && ManufactureWork.RecipeOf("PR301", out _) == 0, "recipes are not recognised");
        Assert.True(ManufactureWork.Combination(1, ["PR301", "PR302", "PR303"]) == 0 && ManufactureWork.Combination(1, ["PR401", "PR302", "PR403"]) == 0b101
            && ManufactureWork.Combination(1, ["PR302", "PR301", "PR303"]) == -1, "rune combinations differ");
        Assert.True(ManufactureWork.Products(1, false, 0, PlayerClass.Fighter).SequenceEqual(["WA114"])
            && ManufactureWork.Products(1, false, 0b111, PlayerClass.Fighter).SequenceEqual(["WM115"])
            && ManufactureWork.Products(7, false, 0, PlayerClass.Fighter).SequenceEqual(["WA121"])
            && ManufactureWork.Products(8, false, 0b101, PlayerClass.Fighter).SequenceEqual(["WS225"]), "weapon recipes differ");
        Assert.True(ManufactureWork.Products(1, true, 0b001, PlayerClass.Fighter).SequenceEqual(["DA216"])
            && ManufactureWork.Products(8, true, 0b110, PlayerClass.Fighter).SequenceEqual(["DB122"])
            && ManufactureWork.Products(3, true, 0b111, PlayerClass.Fighter)
                .SequenceEqual(["DA118", "DA218", "DS116", "OM117", "DG116", "OA216", "DB116"]), "armour recipes differ");
        Assert.True(ManufactureWork.Products(9, false, 0, PlayerClass.Fighter).Count == 0 && ManufactureWork.Products(11, false, 0, PlayerClass.Fighter).Count == 0
            && ManufactureWork.Products(11, true, 0b001, PlayerClass.Fighter).Count == 0, "a recipe without a product made something");
        Assert.True(ManufactureWork.Products(11, true, 0, PlayerClass.Fighter).SequenceEqual(["DA151"])
            && ManufactureWork.Products(11, true, 0, PlayerClass.Atalanta).SequenceEqual(["DA152"])
            && ManufactureWork.Products(11, true, 0, PlayerClass.Magician).SequenceEqual(["DA251"])
            && ManufactureWork.Products(11, true, 0, PlayerClass.Priestess).SequenceEqual(["DA252"]), "the Memory costume differs");
        return Task.CompletedTask;
    }

    private static Task ManufactureAsync()
    {
        var axe = Def("WA114", ItemKind.Axe, 30_000);
        var items = new ItemCatalog([axe]);
        foreach (var (rng, made) in new (ISimRng, bool)[] { (BoundRng.Low, true), (BoundRng.High, false) })
        {
            var character = Character();
            var ids = Place(character, Def("WR101", ItemKind.Quest), Def("PR301"), Def("PR302"), Def("PR303"));
            var money = character.Money;
            Assert.True(ManufactureWork.TryManufacture(character, ids[0], ids[1..], items, rng, out var work, out var fee) == NpcServiceStatus.Ok
                && fee == 10_000 && character.Money == money - 10_000 && work.Result == (byte)(made ? ManufactureResult.Made : ManufactureResult.Failed)
                && Held(character).SequenceEqual(made ? ["WA114"] : []), made ? "the axe was not made" : "a failed attempt kept something");
        }

        var wrong = Character();
        var wrongIds = Place(wrong, Def("WR101", ItemKind.Quest), Def("PR302"), Def("PR301"), Def("PR303"));
        Assert.True(ManufactureWork.TryManufacture(wrong, wrongIds[0], wrongIds[1..], items, BoundRng.Low, out _, out _) == NpcServiceStatus.WrongMaterials
            && Held(wrong).Length == 4, "runes out of their slots were spent");

        var poor = Character(money: 9_999);
        var poorIds = Place(poor, Def("WR101", ItemKind.Quest), Def("PR301"), Def("PR302"), Def("PR303"));
        Assert.True(ManufactureWork.TryManufacture(poor, poorIds[0], poorIds[1..], items, BoundRng.Low, out _, out _) == NpcServiceStatus.NotEnoughGold,
            "a short purse manufactured");
        return Task.CompletedTask;
    }

    private static Task ManufactureBeforeDrawAsync()
    {
        // Three B runes on the third armour recipe pick one of seven products at random; the dearest's fee is 16,000.
        var candidates = ManufactureWork.Products(3, true, 0b111, PlayerClass.Fighter);
        ItemCatalog Catalog(int width = 1, int weight = 0) => new([.. candidates.Select((key, index) => WorldDataFactory.ToItemDefinition(
            ItemRecords.Blank(key) with { Kind = (int)ItemKind.Armor, Price = 30_000 + index * 3_000, GridWidth = index == 3 ? width : 1, Weight = index == 3 ? weight : 0 }))]);
        NpcServiceStatus Try(ItemCatalog items, BoundRng rng, long money, out long fee)
        {
            var character = Character(money: money);
            var ids = Place(character, Def("DR103", ItemKind.Quest), Def("PR403"), Def("PR404"), Def("PR405"));
            return ManufactureWork.TryManufacture(character, ids[0], ids[1..], items, rng, out _, out fee);
        }

        foreach (var rng in new[] { BoundRng.Low, BoundRng.High })
        {
            Assert.True(Try(Catalog(), rng, 1_000_000, out var fee) == NpcServiceStatus.Ok && fee == 16_000, "the fee was not the dearest candidate's");
            Assert.True(Try(Catalog(), rng, 15_999, out _) == NpcServiceStatus.NotEnoughGold, "a purse short of the dearest fee depended on the draw");
            Assert.True(Try(Catalog(width: 40), rng, 1_000_000, out _) == NpcServiceStatus.NoSpace, "a candidate without room depended on the draw");
            Assert.True(Try(Catalog(weight: 1_000_000), rng, 1_000_000, out _) == NpcServiceStatus.OverWeight, "a candidate too heavy depended on the draw");
        }

        return Task.CompletedTask;
    }

    private static Task MixtureResetAsync()
    {
        var sword = WorldDataFactory.ToItemDefinition(ItemRecords.Blank("WS201") with
        {
            Kind = (int)ItemKind.Sword, Price = 1000, DamageMinLow = 10, DamageMinHigh = 20, DamageMaxLow = 30, DamageMaxHigh = 40,
            DurabilityMin = 100, DurabilityMax = 200
        });
        var character = Character();
        var item = new ItemInstance(sword) { MixSheltom = 6, Roll = new ItemInstance(sword).Roll with { SpecJob = 0 } };
        Assert.True(character.Inventory.TryAdd(item), "the sword did not fit");
        var stone = Place(character, Def(MixtureReset.Stone, ItemKind.Misc));
        Assert.True(MixtureReset.TryReset(character, item.InstanceId, [], BoundRng.Low, out _) == NpcServiceStatus.WrongMaterials,
            "a reset without the stone");
        Assert.True(MixtureReset.TryReset(character, item.InstanceId, stone, BoundRng.Low, out var work) == NpcServiceStatus.Ok
            && work.Result == MixtureReset.Reset && character.Inventory.Find(item.InstanceId) is var at && at >= 0
            && character.Inventory[at] is { Mixed: false, Roll.SpecJob: 0 } && Held(character).SequenceEqual(["WS201"]),
            "the sword was not remade plain under its identity or the stone was kept");

        var unmixed = Character();
        var plain = Place(unmixed, sword, Def(MixtureReset.Stone, ItemKind.Misc));
        Assert.True(MixtureReset.TryReset(unmixed, plain[0], plain[1..], BoundRng.Low, out _) == NpcServiceStatus.Rejected, "an unmixed item was reset");
        return Task.CompletedTask;
    }
}
