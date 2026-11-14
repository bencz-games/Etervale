using System.Numerics;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Tests;

internal static class CostumeWardrobeTests
{
    private const string Manifest = """
        {"version":1,"costumes":[{"itemKey":"DA147","bodies":{"1":"char/tmABCD/Ca001.inx"}}]}
        """;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("cosmetic server manifest refuses deferred CA and duplicates", CatalogAsync);
        yield return ("Hopy costumes require explicit opt-in and keep combat equipment unchanged", HopyAsync);
        yield return ("cosmetic operation requires ownership, server permission and current revision, without combat changes", OwnershipAsync);
        yield return ("Hopy costume cannot replace armour equipment slot", HopyArmorAsync);
        yield return ("cosmetic binding survives a clone and a saved inventory relog", PersistenceAsync);
        yield return ("cosmetic identity round trips across roster, owner status and remote description with clan fields", WireAsync);
        yield return ("cosmetic binding expires when its item leaves the bag", RemovalAsync);
    }

    private static Task CatalogAsync()
    {
        var catalog = CostumeCatalog.FromJson(Manifest);
        Assert.True(catalog.Contains("da147") && !catalog.Contains("DA148") && !CostumeCatalog.Empty.Contains("DA147"),
            "cosmetic permission was not an opt-in allowlist");
        var hopy = CostumeCatalog.FromJson("""
            {"version":1,"costumes":[{"itemKey":"CA547","bodies":{"7":"char/tmABCD/mD002.inx"}},
                                       {"itemKey":"CA647","bodies":{"7":"char/tmABCD/mD002.inx"}}]}
            """);
        Assert.True(hopy.Contains("ca547") && hopy.Contains("CA647")
            && !hopy.Contains("CA101") && !CostumeCatalog.Empty.Contains("CA547"),
            "CA547/CA647 must require a manifest and never enable deferred CA codes");
        foreach (var invalid in new[] {
            """{"version":1,"costumes":[{"itemKey":"CA101","bodies":{"0":"x.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"CA124","bodies":{"0":"x.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"CA125","bodies":{"0":"x.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"CA126","bodies":{"0":"x.inx"}}]}""",
            """{"version":1,"costumes":[{"itemKey":"DA147","bodies":{"0":"x.inx"}},{"itemKey":"DA147","bodies":{"0":"y.inx"}}]}"""
        })
        {
            var refused = false;
            try { CostumeCatalog.FromJson(invalid); } catch (InvalidDataException) { refused = true; }
            Assert.True(refused, "forbidden or duplicate manifest entry accepted");
        }
        Assert.True(ReferenceEquals(CostumeCatalog.Load(null!), CostumeCatalog.Empty), "empty server configuration enabled costumes");
        return Task.CompletedTask;
    }

    private static Task HopyAsync()
    {
        foreach (var key in new[] { "CA547", "CA647" })
        {
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Magician, "Hopy", 18);
            var hopy = new ItemInstance(new ItemDefinition(ItemCodes.Parse(key), "Hopy Costume(1Day)", ItemKind.Armor)
            {
                CatalogKey = key
            });
            Assert.True(character.Inventory.TryPlace(hopy, 0), "Hopy did not fit in the bag");
            var permitted = CostumeCatalog.FromJson($$$"""
                {"version":1,"costumes":[{"itemKey":"{{{key}}}","bodies":{"7":"char/tmABCD/mD002.inx"}}]}
                """);
            var before = character.Derived;
            var wear = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision,
                InventoryAction.WearCostume, hopy.InstanceId, 0, 0, default);
            Assert.True(InventoryCommands.Apply(character, wear, permitted)
                && character.ActiveCostumeKey(permitted) == key && character.Derived == before,
                "Hopy failed to attach cosmetically or modified combat stats");
            Assert.True(character.TryClearCostume() && character.ActiveCostumeKey(permitted) == "",
                "Hopy cosmetic binding could not be cleared");
        }
        return Task.CompletedTask;
    }

    private static (PlayerCharacter Character, ItemDefinition Cosmetic, ItemDefinition Armor, ItemInstance Item) Setup()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        var cosmetic = new ItemDefinition(ItemCodes.Parse("DA147"), "Vanity", ItemKind.Armor) { CatalogKey = "DA147" };
        var armor = new ItemDefinition(ItemCodes.Parse("DA108"), "Armour", ItemKind.Armor, EquipmentSlot.Armor) { CatalogKey = "DA108" };
        var item = new ItemInstance(cosmetic);
        Assert.True(character.Inventory.TryPlace(item, 0), "test costume does not fit in bag");
        character.Equip(EquipmentSlot.Armor, new ItemInstance(armor));
        return (character, cosmetic, armor, item);
    }

    private static Task HopyArmorAsync()
    {
        foreach (var key in new[] { "CA547", "CA647" })
        {
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Magician, "Hopy", 18);
            var definition = new ItemDefinition(ItemCodes.Parse(key), "Hopy Costume(1Day)", ItemKind.Armor, EquipmentSlot.Armor)
            {
                CatalogKey = key
            };
            var item = new ItemInstance(definition);
            Assert.True(character.Inventory.TryPlace(item, 0), "test Hopy did not fit into bag");
            var command = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision,
                InventoryAction.Equip, item.InstanceId, 0, 0, EquipmentSlot.Armor);
            Assert.True(!InventoryCommands.Apply(character, command)
                && character.GetEquipped(EquipmentSlot.Armor) is null,
                "a Hopy costume was equipped as a real armour item");
        }
        return Task.CompletedTask;
    }

    private static Task OwnershipAsync()
    {
        var (character, _, armor, item) = Setup();
        var catalog = CostumeCatalog.FromJson(Manifest);
        var before = character.Derived;
        Assert.True(!InventoryCommands.Apply(character, new InventoryCommand(Guid.NewGuid(), character.InventoryRevision,
            InventoryAction.WearCostume, Guid.NewGuid(), 0, 0, default), catalog), "a stranger's item was accepted");
        Assert.True(!InventoryCommands.Apply(character, new InventoryCommand(Guid.NewGuid(), character.InventoryRevision,
            InventoryAction.WearCostume, item.InstanceId, 0, 0, default), CostumeCatalog.Empty), "unlisted item was accepted");
        var wear = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.WearCostume, item.InstanceId, 0, 0, default);
        Assert.True(InventoryCommands.Apply(character, wear, catalog), "owned allowed costume was refused");
        Assert.True(character.GetEquipped(EquipmentSlot.Armor)?.Definition.Code == armor.Code
            && character.ActiveCostumeKey(catalog) == "DA147" && before == character.Derived, "cosmetic changed combat equipment or derived stats");
        Assert.True(!InventoryCommands.Apply(character, wear, catalog), "stale revision accepted");
        Assert.True(InventoryCommands.Apply(character, new InventoryCommand(Guid.NewGuid(), character.InventoryRevision,
            InventoryAction.ClearCostume, Guid.Empty, 0, 0, default), catalog) && character.ActiveCostumeKey(catalog) == "",
            "cosmetic clear was refused");
        return Task.CompletedTask;
    }

    private static Task PersistenceAsync()
    {
        var (character, cosmetic, armor, item) = Setup();
        var catalog = CostumeCatalog.FromJson(Manifest);
        Assert.True(InventoryCommands.Apply(character, new InventoryCommand(Guid.NewGuid(), character.InventoryRevision,
            InventoryAction.WearCostume, item.InstanceId, 0, 0, default), catalog), "wear failed");
        var clone = character.CloneForInventoryOperation();
        Assert.True(clone.ActiveCostumeItemId(catalog) == item.InstanceId, "clone lost cosmetic binding");
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Wardrobe", 0, PlayerClass.Fighter, 3);
        var stored = CharacterRecordFactory.Capture(record, clone, 3, Vector3.Zero).Encode();
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Wardrobe", 50);
        CharacterStateCodec.Restore(restored, stored, new ItemCatalog([cosmetic, armor]));
        Assert.True(restored.ActiveCostumeKey(catalog) == "DA147" && restored.GetEquipped(EquipmentSlot.Armor)?.Definition.Code == armor.Code,
            "relog lost the cosmetic or swapped armor");
        Assert.True(restored.ActiveCostumeKey(CostumeCatalog.Empty) == "", "a disabled catalog still displayed a cosmetic");
        return Task.CompletedTask;
    }

    private static Task RemovalAsync()
    {
        var (character, _, _, item) = Setup();
        var catalog = CostumeCatalog.FromJson(Manifest);
        Assert.True(character.TryWearCostume(item.InstanceId, catalog), "wear failed");
        Assert.True(character.Inventory.TakeAt(0)?.InstanceId == item.InstanceId, "test item removal failed");
        Assert.True(character.ActiveCostumeItemId(catalog) == Guid.Empty && character.ActiveCostumeKey(catalog) == "",
            "a removed item was still worn");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var cosmetic = ItemCodes.Parse("DA147");
        var owner = new OwnerStatusPacket(50, 0, 0, 1, 0, 100, 100, 100, 100, 100, 100, 0, 0, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1f, 4, 1, 1, 0, 0, 0, 0, Guid.NewGuid(), cosmetic);
        var ownerBuffer = new byte[OwnerStatusPacket.Size];
        var readOwner = OwnerStatusPacket.Read(ownerBuffer.AsSpan(0, owner.Write(ownerBuffer)));
        Assert.True(readOwner.CostumeItemId == owner.CostumeItemId && readOwner.CostumeCode == cosmetic,
            "owner status omitted vanity identity");
        var entry = new CharacterListEntry("Wardrobe", PlayerClass.Fighter, 50, 0, 0, 0, 0, 0, 0, true, "Guild")
            { CostumeCode = cosmetic };
        var list = new CharacterListPacket([entry]);
        var roster = new byte[CharacterListPacket.MaxSize];
        var copy = CharacterListPacket.Read(roster.AsSpan(0, list.Write(roster))).Characters[0];
        Assert.True(copy.CostumeCode == cosmetic && copy.ClanName == "Guild", "roster overwrote the guild or costume");
        var look = new DescribedPlayer(PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, true, false, 0, 0)
            { Costume = cosmetic, ClanId = 7UL, ClanName = "Guild", ClanMark = 2 };
        var packet = new EntityDescriptionPacket(42, 1, EntityKind.Player, 50, 3, 1f, "Wardrobe", "", look);
        var wire = new byte[EntityDescriptionPacket.MaximumSize];
        var result = EntityDescriptionPacket.Read(wire.AsSpan(0, packet.Write(wire)));
        Assert.True(result.Player?.Costume == cosmetic && result.Player.ClanId == 7UL && result.Player.ClanName == "Guild"
            && result.Player.ClanMark == 2, "description v4 lost costume or clan");
        return Task.CompletedTask;
    }
}
