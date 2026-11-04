using PristonTale.Persistence.Abstractions.GameLogs;
using System.Numerics;
using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Npcs;
using PristonTale.WorldImport;
using PristonTale.Contracts.World;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class NpcServiceTests
{
    private static readonly uint AxeCode = ItemCodes.Parse("WA101");
    private static readonly uint PotionCode = ItemCodes.Parse("PL101");
    private static readonly uint QuestCode = ItemCodes.Parse("QT101");

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("NPC import types every service key, makes every definition file an NPC keyed in English, and places the file a field names", ImportAsync);
        yield return ("shop prices, sell wear, repair cost, carry limit and warehouse rules follow the original", RulesAsync);
        yield return ("the warehouse keeps out what the original keeps out and exchanges the item a set-down covers", WarehouseExchangeAsync);
        yield return ("NPC service messages round-trip their layouts", ProtocolAsync);
        yield return ("shop buy, sell and repair are durable, idempotent and validated on the server", ShopAsync);
        yield return ("warehouse deposit, withdraw, move and gold persist with the character and reject conflicts", WarehouseAsync);
        yield return ("teleporters charge, move the character and honour field level limits", TeleportAsync);
        yield return ("SQLite warehouse repository writes the character and warehouse together under a revision", RepositoryAsync);
    }

    private static async Task ImportAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-npc-services-{Guid.NewGuid():N}");
        var source = Path.Combine(directory, "GameServer");
        var items = Path.Combine(source, "OpenItem");
        var npcs = Path.Combine(source, "NPC");
        var names = Path.Combine(npcs, "name");
        var fields = Path.Combine(source, "Field");
        Directory.CreateDirectory(items);
        Directory.CreateDirectory(names);
        Directory.CreateDirectory(fields);
        Directory.CreateDirectory(Path.Combine(source, "Monster"));
        await WriteCp949Async(Path.Combine(items, "PL101.txt"), "*코드 PL101\n*이름 \"Mini Life Potion\"\n*가격 25\n");
        await WriteCp949Async(Path.Combine(npcs, "Store.npc"),
            "*이름 \"잡화점\"\n*계급 1001\n*잡화판매 PL101 없음\n*아이템보관\n*텔레포트\n*아이템연금\n*퀘스트이벤트\n*소켓\n//*대화 \"주석\"\n" +
            "*연결파일 \"name\\store.zhoon\"\n");
        await WriteCp949Async(Path.Combine(names, "store.zhoon"),
            "*B_NAME \"Miscellaneous Store\"\n*B_CHAT \"Red potions   restore HP.\"\n*b_chat \"Second line.\"\n");
        await WriteCp949Async(Path.Combine(npcs, "Guard1.npc"), "*이름 \"경비병\"\n*모양파일 \"char\\npc\\a\\a.ini\"\n*대화 \"한국어\"\n");
        await WriteCp949Async(Path.Combine(npcs, "Guard2.npc"), "*이름 \"경비병\"\n*모양파일 \"char\\npc\\b\\b.ini\"\n*E_CHAT \"Hello.\"\n*스킬수련\n*직업전환\n");
        await File.WriteAllBytesAsync(Path.Combine(fields, "village-2.ase.spc"), PlacementFile(("GameServer\\NPC\\Guard2.npc", 2004, 1)));
        // A stage no field uses places Guard2 again with another class (the first placement's wins) and the store, whose
        // own *계급 wins while it takes the size it lacks.
        await File.WriteAllBytesAsync(Path.Combine(fields, "zz-unused.spc"),
            PlacementFile(("GameServer\\NPC\\Guard2.npc", 2005, 1), ("GameServer\\NPC\\Store.npc", 2006, 2)));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            var importer = new Importer(connections, source);
            var report = await importer.RunAsync();
            var again = await importer.RunAsync();
            // The store's seven, and the skill master and placement quest code of Guard2.
            Assert.True(report.NpcServiceRows == 9 && again.NpcServiceRows == 9, $"typed service rows differ: {report.NpcServiceRows}");

            await using var connection = await connections.OpenAsync();
            var rows = (await connection.QueryAsync<(long Kind, long Value, string Text)>(
                "SELECT s.kind, s.value, s.text FROM npc_services s JOIN npcs n ON n.id = s.npc_id WHERE n.key = 'Miscellaneous Store' ORDER BY s.ordinal")).ToArray();
            NpcServiceKind[] expected =
            [
                NpcServiceKind.QuestGiver, NpcServiceKind.Shop, NpcServiceKind.Warehouse, NpcServiceKind.ItemMix, NpcServiceKind.QuestEvent,
                NpcServiceKind.Teleporter, NpcServiceKind.Unported
            ];
            Assert.True(rows.Select(row => (NpcServiceKind)row.Kind).SequenceEqual(expected), "services lost their kind or click order");
            Assert.True(rows[0].Value == 1001 && rows[3].Value == 200 && rows[4].Value == 2 && rows[5].Value == 1 && rows[6].Text == "Socket",
                "service values or defaults differ from the authored keys");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_import_unresolved_refs WHERE raw_reference = 'none'") == 1, "empty shop marker was not retained");

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var definitions = WorldDataFactory.ToNpcDefinitions(await repository.GetAllNpcsAsync());
            var store = definitions.Single(definition => definition.Name == "Miscellaneous Store");
            Assert.True(store.Label == "Miscellaneous Store" && store.Dialog.SequenceEqual(["Red potions restore HP.", "Second line."]),
                "localized name or dialog was not imported");
            Assert.True(store.Has(NpcServiceKind.Shop) && store.Shop.MiscItemCodes.Single() == PotionCode, "runtime shop differs from the stock");
            // Every definition file is an NPC; unregistered files take the next ids in path order, and one without an English
            // name is keyed by its id.
            var next = IdentityRegistry.Npcs().LastId + 1;
            var guard = definitions.Single(definition => definition.Code == next);
            var placed = definitions.Single(definition => definition.Code == next + 1);
            Assert.True(definitions.Count == 3 && guard.Name == $"NPC {next}" && placed.Name == $"NPC {next + 1}" && store.Code == next + 2,
                $"the NPC files were keyed {string.Join(", ", definitions.Select(static definition => $"{definition.Code} {definition.Name}"))}");
            Assert.True(report.Warnings.Count(static warning => warning.StartsWith("Identities/npcs.json lacks", StringComparison.Ordinal)) == 3,
                "the import did not list the NPCs to register");
            Assert.True(placed.ModelInxPath.Replace('\\', '/').Contains("/b/", StringComparison.OrdinalIgnoreCase) && placed.Dialog.Single() == "Hello."
                && guard.ModelInxPath.Replace('\\', '/').Contains("/a/", StringComparison.OrdinalIgnoreCase) && guard.Dialog.Count == 0,
                "a file sharing an identity did not keep its own model and lines, or an untranslated Korean line was kept");
            Assert.True(placed.Find(NpcServiceKind.SkillMaster)?.Value == 1, "class change default was not carried by the skill master");
            // A file without *계급 or *크기 keeps the class and size of its placement; one never placed keeps neither.
            Assert.True(placed.Services[0] is { Kind: NpcServiceKind.QuestGiver, Value: 2004 } && placed.ShadowSizeLevel == 1
                && guard.Find(NpcServiceKind.QuestGiver) is null && guard.ShadowSizeLevel == 0,
                "the placement's quest code or shadow size did not reach the file that lacks them");
            Assert.True(store.Find(NpcServiceKind.QuestGiver)?.Value == 1001 && store.ShadowSizeLevel == 2,
                "a file's own class lost to its placement's, or it did not take the size it lacks");
            Assert.True(report.Warnings.Contains("npc Guard2.npc: its placements give different classes or sizes it lacks; the first is used")
                && !report.Warnings.Any(static warning => warning.StartsWith("npc Store.npc: its placements", StringComparison.Ordinal)),
                "placements that disagree were not reported, or a disagreement the file overrides was");
            Assert.True(report.Warnings.Contains("npc Guard1.npc: 1 *대화 line(s) without an English translation (EnglishNames.Dialog) are not shown"),
                "an untranslated line was not reported");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT npc_id FROM field_npcs WHERE stage = 'village-2'") == next + 1,
                "the placement does not name the file it places");
            var exported = Path.Combine(directory, "placements.json");
            NpcPlacementExport.Write(source, exported);
            using (var placements = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(exported)))
            {
                var row = placements.RootElement.EnumerateArray().Single();
                Assert.True(row.GetProperty("NpcId").GetInt32() == next + 1 && row.GetProperty("NpcKey").GetString() == placed.Name,
                    "the placement export keys an NPC differently from the import");
            }

            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM npc_dialog_lines WHERE npc_id = @Id AND language = 'en'", new { Id = next + 2 }) == 2,
                "the English dialog lines were not stored");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task RulesAsync()
    {
        var axe = Axe();
        var item = new ItemInstance(axe);
        Assert.True(NpcShopRules.SellPrice(item, 1) == 12 && NpcShopRules.RepairCost(item) == 0, "intact item price differs");
        item.DurabilityCurrent = 10;
        Assert.True(NpcShopRules.SellPrice(item, 1) == 5 && NpcShopRules.RepairCost(item) == 4, "worn sell or repair price differs");
        item.DurabilityCurrent = 0;
        Assert.True(NpcShopRules.WornValue(60, 0, 36) == 16, "zero durability must count as one");
        var potions = new ItemInstance(Potion() with { MaxStack = 20 }, 7);
        Assert.True(NpcShopRules.SellPrice(potions, 7) == 35 && NpcShopRules.SellPrice(potions, 3) == 15, "stack sell price differs");
        Assert.True(MoneyLimits.CarryLimit(5, 0) == 200_000 && MoneyLimits.CarryLimit(20, 0) == 2_200_000 && MoneyLimits.CarryLimit(80, 3) == 500_000_000
            && MoneyLimits.CarryLimit(90, 4) == 700_000_000,
            "carry limit differs");

        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Rules");
        character.RestoreProgress(0, 100);
        Assert.True(NpcShopRules.TryBuy(character, axe, 2, -1, out _) == NpcServiceStatus.Rejected, "a second unstackable item was sold at once");
        Assert.True(NpcShopRules.TryBuy(character, axe, 1, -1, out var cost) == NpcServiceStatus.Ok && cost == 60 && character.Money == 40, "buy failed");
        Assert.True(NpcShopRules.TryBuy(character, axe, 1, -1, out _) == NpcServiceStatus.NotEnoughGold && character.Money == 40, "overdraft accepted");
        var storage = new WarehouseStorage();
        var quest = new ItemInstance(new ItemDefinition(QuestCode, "Letter", ItemKind.Quest));
        character.Inventory.TryAdd(quest);
        Assert.True(storage.TryDeposit(character, quest.InstanceId, -1) == NpcServiceStatus.Rejected, "quest item stored");
        var heavy = new ItemInstance(axe with { Weight = WarehouseStorage.MaxWeight + 1 });
        character.Inventory.TryAdd(heavy);
        Assert.True(storage.TryDeposit(character, heavy.InstanceId, -1) == NpcServiceStatus.NoSpace && storage.Revision == 0, "weight limit ignored");
        Assert.True(storage.TryDepositGold(character, 41) == NpcServiceStatus.NotEnoughGold && storage.TryDepositGold(character, 40) == NpcServiceStatus.Ok
            && storage.Money == 40 && character.Money == 0 && storage.Revision == 1, "gold deposit is not conserved");
        return Task.CompletedTask;
    }

    private static Task WarehouseExchangeAsync()
    {
        foreach (var (kind, key, storable) in new[]
                 {
                     (ItemKind.Axe, "WA101", true), (ItemKind.Misc, "GF102", true), (ItemKind.Misc, "SP201", true), (ItemKind.Potion, "PL101", false),
                     (ItemKind.Material, "MA101", false), (ItemKind.Material, "MA202", false), (ItemKind.Misc, "SP101", false),
                     (ItemKind.Misc, "CH101", false), (ItemKind.Misc, "GF101", false), (ItemKind.Misc, "QW101", false), (ItemKind.Quest, "QT107", false)
                 })
        {
            Assert.True(WarehouseStorage.CanStore(kind, key) == storable, $"{key} storable should be {storable}");
        }

        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Keeper");
        var storage = new WarehouseStorage();
        var potion = new ItemInstance(Potion() with { MaxStack = 20 }, 3);
        Assert.True(character.Inventory.TryPlace(potion, 30), "the potion fixture was not placed");
        Assert.True(storage.TryDeposit(character, potion.InstanceId, 0) == NpcServiceStatus.Rejected, "a potion was stored");

        var carried = new ItemInstance(Axe());
        var stored = new ItemInstance(Axe());
        var other = new ItemInstance(Axe());
        Assert.True(character.Inventory.TryPlace(carried, 0) && storage.Restore(stored, 0) && storage.Restore(other, 3), "the fixture was not placed");
        Assert.True(storage.TryDeposit(character, carried.InstanceId, 0, stored.InstanceId) == NpcServiceStatus.Ok
            && Is(storage.Grid[0], carried) && Is(character.Inventory[0], stored) && storage.Revision == 1, "deposit over a stored item did not exchange");
        Assert.True(storage.TryWithdraw(character, carried.InstanceId, 0, stored.InstanceId) == NpcServiceStatus.Ok
            && Is(character.Inventory[0], carried) && Is(storage.Grid[0], stored) && storage.Revision == 2, "withdraw over a bag item did not exchange");
        Assert.True(storage.TryMove(stored.InstanceId, 3, other.InstanceId) == NpcServiceStatus.Ok && Is(storage.Grid[3], stored) && Is(storage.Grid[0], other),
            "move over a stored item did not exchange");

        Assert.True(storage.TryWithdraw(character, other.InstanceId, 30, potion.InstanceId) == NpcServiceStatus.Rejected, "a potion was exchanged into storage");
        Assert.True(storage.TryWithdraw(character, other.InstanceId, 11, carried.InstanceId) == NpcServiceStatus.Rejected
            && Is(storage.Grid[0], other) && Is(character.Inventory[0], carried) && storage.Revision == 3, "an item the set-down does not cover was exchanged");
        Assert.True(storage.TryDeposit(character, carried.InstanceId, -1, other.InstanceId) == NpcServiceStatus.Rejected && Is(character.Inventory[0], carried),
            "an exchange without a cell was accepted");
        // A ring set down over a stored item too wide for the bag: neither side changes.
        var wide = new ItemInstance(Axe() with { GridWidth = 9, GridHeight = 6 });
        var ring = new ItemInstance(new ItemDefinition(ItemCodes.Parse("OR101"), "Ring", ItemKind.Ring) { Weight = 1, CatalogKey = "OR101" });
        Assert.True(storage.Restore(wide, 27) && character.Inventory.TryPlace(ring, 11), "the exchange fixture was not placed");
        Assert.True(storage.TryDeposit(character, ring.InstanceId, 27, wide.InstanceId) == NpcServiceStatus.NoSpace && Is(storage.Grid[27], wide)
            && Is(character.Inventory[11], ring) && storage.Revision == 3, "an exchange whose covered item does not fit changed something");
        return Task.CompletedTask;

        static bool Is(ItemInstance? item, ItemInstance expected) => item?.InstanceId == expected.InstanceId;
    }

    private static Task ProtocolAsync()
    {
        var buffer = new byte[Protocol.Binary.FrameCodec.MaxFrameSize];
        var dialog = new NpcDialogPacket(7, true, "Skill Master, Verkan", "", [new NpcServiceEntry(2, true, 4), new NpcServiceEntry(4, false, 1003)],
            ["Stout body and noble mind.", "스킬"]);
        var readDialog = NpcDialogPacket.Read(buffer.AsSpan(0, dialog.Write(buffer)));
        Assert.True(readDialog.Name == dialog.Name && readDialog.Services.SequenceEqual(dialog.Services) && readDialog.Lines.SequenceEqual(dialog.Lines),
            "dialog round trip differs");

        var request = new NpcServiceRequestPacket(Guid.NewGuid(), 7, 1, 10, 3, 4, AxeCode, Guid.NewGuid(), 5, 1, 99, Covered: Guid.NewGuid());
        Assert.True(NpcServiceRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request && request.Write(buffer) == NpcServiceRequestPacket.Size,
            "request round trip differs");
        var result = new NpcServiceResultPacket(request.OperationId, 7, 1, 10, (ushort)NpcServiceStatus.NotEnoughGold, false, 3, 4, "not enough gold");
        Assert.True(NpcServiceResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "result round trip differs");

        var catalog = new ShopCatalogPacket(request.OperationId, 7, Enumerable.Range(0, ShopCatalogPacket.MaxItems)
            .Select(index => new ShopCatalogEntry(AxeCode, (byte)(index % 3), 1, (ushort)index, 60, 2, 3, 1, "A rather long item name for packing"))
            .ToArray());
        var readCatalog = ShopCatalogPacket.Read(buffer.AsSpan(0, catalog.Write(buffer)));
        Assert.True(readCatalog.Items.SequenceEqual(catalog.Items), "a full shop catalog does not fit one frame");
        var quotes = new ShopQuotesPacket(request.OperationId, 7, 12, [new ShopQuoteEntry(Guid.NewGuid(), 12, 4, 10, 36)]);
        Assert.True(ShopQuotesPacket.Read(buffer.AsSpan(0, quotes.Write(buffer))).Items.SequenceEqual(quotes.Items), "quotes round trip differs");
        var warehouse = new WarehousePacket(request.OperationId, 7, 3, 500, 40, 1000, 9, 9, Enumerable.Range(0, WarehousePacket.MaxItems)
            .Select(index => new StoreItemEntry(Guid.NewGuid(), AxeCode, 1, 1, index, 2, 3, "Warehouse item with a long name")).ToArray());
        var readWarehouse = WarehousePacket.Read(buffer.AsSpan(0, warehouse.Write(buffer)));
        Assert.True(readWarehouse.Items.SequenceEqual(warehouse.Items) && readWarehouse.Money == 500 && readWarehouse.Rows == 9,
            "a full warehouse does not fit one frame");
        var teleports = new TeleportListPacket(request.OperationId, 7, [new TeleportEntry(1, 0, 100, 0, "Mushroom Cave")]);
        Assert.True(TeleportListPacket.Read(buffer.AsSpan(0, teleports.Write(buffer))).Destinations.SequenceEqual(teleports.Destinations),
            "teleport list round trip differs");
        var truncated = buffer.AsSpan(0, dialog.Write(buffer)).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(truncated, truncated.Length - 4);
        var rejected = false;
        try
        {
            NpcDialogPacket.Read(truncated.AsSpan(0, truncated.Length - 4));
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.True(rejected, "a truncated dialog was accepted");
        return Task.CompletedTask;
    }

    private static async Task ShopAsync()
    {
        var fixture = await Fixture.CreateAsync(1000, [new NpcService(NpcServiceKind.Shop), new NpcService(NpcServiceKind.Clan)]);
        var (world, npc, session, service) = (fixture.World, fixture.NpcId, fixture.Session, fixture.Npc);
        var interaction = await WorldPump.RunAsync(world, world.InteractNpcAsync(session, npc));
        Assert.True(interaction.Ok && interaction.Offers(NpcServiceKind.Shop) && !interaction.Offers(NpcServiceKind.Clan)
            && interaction.Dialog.Count == 1 && Fixture.Lines.Contains(interaction.Dialog[0]), "typed dialog differs");

        var open = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Shop,
            NpcServiceAction.Open)));
        Assert.True(open.Outcome.Ok && open.Catalog!.Single(offer => offer.Code == AxeCode).Price == 60 && open.Quotes is not null,
            "shop catalog does not carry server prices");
        var clan = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Clan,
            NpcServiceAction.Open)));
        Assert.True(clan.Outcome.Status == NpcServiceStatus.NotAvailable, "an unported service did not answer not available");

        var character = session.Character!;
        var buy = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Shop, NpcServiceAction.Buy, character.InventoryRevision, ItemCode: AxeCode);
        var bought = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, buy));
        var found = character.Inventory.FindByDefinition(Axe(), out var axe);
        Assert.True(bought.Outcome.Ok && found && character.Money == 940 && fixture.Characters.Record.Money == 940, "buy was not durable");
        var writes = fixture.Characters.UpdateCount;
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, buy))).Outcome.Message == "already committed"
            && fixture.Characters.UpdateCount == writes && character.Money == 940, "a replayed buy charged again");
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, buy with { ItemCode = PotionCode }))).Outcome.Status
            == NpcServiceStatus.Conflict, "a reused operation id was accepted for another purchase");
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, buy with { OperationId = Guid.NewGuid() }))).Outcome.Status
            == NpcServiceStatus.Stale, "a stale revision bought");
        var notStocked = buy with { OperationId = Guid.NewGuid(), ExpectedRevision = character.InventoryRevision, ItemCode = QuestCode };
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, notStocked))).Outcome.Status == NpcServiceStatus.Rejected,
            "an item outside the stock was sold");

        fixture.Player.Teleport(new Vector3(1000, 0, 1000));
        var far = buy with { OperationId = Guid.NewGuid(), ExpectedRevision = character.InventoryRevision };
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, far))).Outcome.Status == NpcServiceStatus.OutOfReach,
            "a remote shop accepted a purchase");
        fixture.Player.Teleport(new Vector3(100, 0, 100));

        axe!.DurabilityCurrent = 10;
        var repair = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Shop, NpcServiceAction.Repair, character.InventoryRevision, ItemId: axe.InstanceId);
        var repaired = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, repair));
        var owned = character.FindOwnedItem(axe.InstanceId);
        Assert.True(repaired.Outcome.Ok && character.Money == 936 && owned.DurabilityCurrent == owned.DurabilityMax, "repair cost or durability differs");

        var sell = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Shop, NpcServiceAction.Sell, character.InventoryRevision, ItemId: axe.InstanceId);
        var sold = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, sell));
        Assert.True(sold.Outcome.Ok && character.FindOwnedItem(axe.InstanceId) is null && character.Money == 948
            && sold.Quotes!.Items.All(quote => quote.ItemId != axe.InstanceId), "sale proceeds or quotes differ");
        var reload = Reload(fixture);
        Assert.True(reload.Money == 948 && reload.ServiceOperations.Count == 3, "operation history was not persisted");
    }

    private static async Task WarehouseAsync()
    {
        var fixture = await Fixture.CreateAsync(500, [new NpcService(NpcServiceKind.Warehouse)]);
        var (world, npc, session, service) = (fixture.World, fixture.NpcId, fixture.Session, fixture.Npc);
        var character = session.Character!;
        var axe = new ItemInstance(Axe());
        character.Inventory.TryAdd(axe);
        var early = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Warehouse, NpcServiceAction.Deposit, character.InventoryRevision,
            ItemId: axe.InstanceId, Anchor: 0);
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, early))).Outcome.Status == NpcServiceStatus.NotOpen,
            "a deposit bypassed opening the warehouse");

        var open = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Warehouse,
            NpcServiceAction.Open)));
        Assert.True(open.Outcome.Ok && open.Warehouse!.Items.Count == 0 && open.Warehouse.MaxWeight == 1000, "empty warehouse did not open");
        var deposit = early with { OperationId = Guid.NewGuid(), ExpectedRevision = character.InventoryRevision, ExpectedStorageRevision = 0, Anchor = 3 };
        var stored = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, deposit));
        Assert.True(stored.Outcome.Ok && stored.Warehouse!.Items.Single().Anchor == 3 && character.FindOwnedItem(axe.InstanceId) is null
            && fixture.Warehouses.Record?.Revision == 1, "deposit was not durable");
        Assert.True(fixture.Log.Rows.Single() is { Kind: ItemTransferKind.WarehouseDeposit, ItemCode: "WA101", ToCharacterId: null } logged
            && logged.InstanceId == axe.InstanceId.ToString() && logged.FromCharacterId == fixture.Characters.Record.Id
            && logged.AccountId == session.AccountId, "the deposit was not logged from the character to its account's warehouse");
        var move = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Warehouse, NpcServiceAction.Move, character.InventoryRevision, 1,
            ItemId: axe.InstanceId, Anchor: 40);
        var moved = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, move));
        Assert.True(moved.Outcome.Ok && moved.Warehouse!.Items.Single().Anchor == 40, "move inside the warehouse failed");
        var gold = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Warehouse, NpcServiceAction.DepositGold, character.InventoryRevision, 2,
            Amount: 200);
        var deposited = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, gold));
        Assert.True(deposited.Outcome.Ok && deposited.Warehouse!.Money == 200 && character.Money == 300 && fixture.Characters.Record.Money == 300,
            "gold was not moved with the character");
        Assert.True(fixture.Log.Rows[^1] is { Kind: ItemTransferKind.WarehouseDeposit, ItemCode: null, Amount: 200, ToCharacterId: null }
            && fixture.Log.Rows[^1].AccountId == session.AccountId, "the gold deposit was not logged");

        fixture.Warehouses.Bump();
        var conflicted = gold with { OperationId = Guid.NewGuid(), ExpectedRevision = character.InventoryRevision, ExpectedStorageRevision = 3 };
        Assert.True((await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, conflicted))).Outcome.Status == NpcServiceStatus.Conflict
            && character.Money == 300, "a warehouse changed elsewhere was overwritten");
        var reopened = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Warehouse,
            NpcServiceAction.Open)));
        var revision = reopened.Warehouse!.Revision;
        var withdraw = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Warehouse, NpcServiceAction.Withdraw, character.InventoryRevision, revision,
            ItemId: axe.InstanceId, Anchor: 0);
        var taken = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, withdraw));
        Assert.True(taken.Outcome.Ok && character.Inventory[0]?.InstanceId == axe.InstanceId && taken.Warehouse!.Items.Count == 0 && taken.Warehouse.Money == 200,
            "withdraw did not restore the item");
        Assert.True(fixture.Log.Rows[^1] is { Kind: ItemTransferKind.WarehouseWithdrawal, ItemCode: "WA101", FromCharacterId: null, Amount: 1 } withdrawn
            && withdrawn.InstanceId == axe.InstanceId.ToString() && withdrawn.ToCharacterId == fixture.Characters.Record.Id && fixture.Log.Rows.Count == 3,
            "the withdrawal was not logged, or a move inside the warehouse was");
        var reload = WarehouseStateCodec.Restore(fixture.Warehouses.Record, fixture.Items);
        Assert.True(reload.Count == 0 && reload.Money == 200 && reload.Revision == revision + 1, "warehouse state was not persisted");
    }

    private static async Task TeleportAsync()
    {
        var catalog = new WorldCatalog(new WorldCatalog().AllFields.Select(field => field.FieldCode == 12 ? field with { TravelLevel = 70 } : field));
        var fixture = await Fixture.CreateAsync(1000, [new NpcService(NpcServiceKind.Teleporter, 1)], catalog);
        var (world, npc, session, service) = (fixture.World, fixture.NpcId, fixture.Session, fixture.Npc);
        var open = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Teleporter,
            NpcServiceAction.Open)));
        Assert.True(open.Outcome.Ok && open.Destinations!.Select(destination => destination.Price).SequenceEqual([100L, 100, 500, 1000])
            && open.Destinations[3].MinimumLevel == 70, "teleport destinations differ");
        var character = session.Character!;
        var gated = new NpcServiceCommand(Guid.NewGuid(), npc, NpcServiceKind.Teleporter, NpcServiceAction.Teleport, character.InventoryRevision, Anchor: 4);
        Assert.True(!(await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, gated))).Outcome.Ok && character.Money == 1000,
            "the field level limit was ignored");
        // Run the source field's clock well ahead of the destination's, which starts at zero.
        for (uint tick = 0; tick < 100; tick++)
        {
            world.Tick(0.05f, tick);
        }

        const uint skill = 0x01010001;
        fixture.Player.Cooldowns.Start(skill, 60_000, world.FieldNowMs(3));
        var trip = gated with { OperationId = Guid.NewGuid(), Anchor = 1 };
        var moved = await WorldPump.RunAsync(world, service.RequestAsync(session.SessionToken, trip));
        Assert.True(moved.Outcome.Ok && moved.Arrival?.FieldCode == 0 && session.FieldId == 0 && character.Money == 900
            && fixture.Characters.Record.FieldId == 0 && fixture.Characters.Record.Money == 900, "teleport did not charge and move durably");
        var remaining = fixture.Player.Cooldowns.RemainingMs(skill, world.FieldNowMs(0));
        Assert.True(remaining is > 59_000 and <= 60_000, $"teleport did not carry the cooldown to the new field clock ({remaining} ms left)");
    }

    private static async Task RepositoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-warehouse-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            await using (var connection = await connections.OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash) VALUES ('owner', 'x')");
            }

            var classes = new CharacterClassRegistry();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, 1, "Keeper", 0, PlayerClass.Fighter, 3));
            var warehouses = provider.GetRequiredService<IWarehouseRepository>();
            Assert.True(await warehouses.FindAsync(1) is null, "a new account already has a warehouse");
            var storage = new WarehouseStorage(1, 50);
            storage.Restore(new ItemInstance(Axe()), 4);
            var record = WarehouseStateCodec.Capture(1, storage);
            Assert.True(await warehouses.SaveWithCharacterAsync(created with { Money = 77 }, record, 0), "first warehouse write failed");
            Assert.True(!await warehouses.SaveWithCharacterAsync(created with { Money = 1 }, record with { Revision = 2 }, 0),
                "a stale first write was accepted");
            Assert.True((await characters.FindByNameForAccountAsync(1, "Keeper"))!.Money == 77, "a rejected write changed the character");
            Assert.True(await warehouses.SaveWithCharacterAsync(created with { Money = 5 }, record with { Revision = 2, Money = 60 }, 1),
                "a current write failed");
            var loaded = await warehouses.FindAsync(1);
            var restored = WarehouseStateCodec.Restore(loaded, new ItemCatalog([Axe()]));
            Assert.True(loaded!.Revision == 2 && restored.Money == 60 && restored.Grid[4] is not null
                && (await characters.FindByNameForAccountAsync(1, "Keeper"))!.Money == 5, "warehouse and character were not written together");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PlayerCharacter Reload(Fixture fixture)
    {
        var record = fixture.Characters.Record;
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, record.Name, record.Level);
        CharacterStateCodec.Restore(character, record, fixture.Items);
        return character;
    }

    private static bool FindByDefinition(this Inventory inventory, ItemDefinition definition, out ItemInstance? item)
    {
        for (var anchor = 0; anchor < inventory.Capacity; anchor++)
        {
            if (inventory[anchor]?.Definition.Code == definition.Code)
            {
                item = inventory[anchor];
                return true;
            }
        }

        item = null;
        return false;
    }

    private static ItemDefinition Axe() =>
        new(AxeCode, "Stone Axe", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3) { Price = 60, MaxDurability = 36, Weight = 5, CatalogKey = "WA101" };

    private static ItemDefinition Potion() =>
        new(PotionCode, "Mini Life Potion", ItemKind.Potion) { Price = 25, Weight = 1, CatalogKey = "PL101" };

    // One live record of the fixed NPC file: size, a non-zero code and the configuration path at offset 104.
    // A field's placement file: one live record per placement, naming its NPC file, class and size.
    private static byte[] PlacementFile(params (string Path, ushort Class, int SizeLevel)[] placements)
    {
        var bytes = new byte[504 * 100];
        for (var index = 0; index < placements.Length; index++)
        {
            var record = bytes.AsSpan(index * 504, 504);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record, 504);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record[4..], 1);
            Cp949.Encoding.GetBytes(placements[index].Path).CopyTo(record[104..]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record[180..], placements[index].SizeLevel);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record[392..], placements[index].Class);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record[476..], 100 * 256);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record[484..], 100 * 256);
        }

        return bytes;
    }

    private static Task WriteCp949Async(string path, string contents) => File.WriteAllBytesAsync(path, Cp949.Encoding.GetBytes(contents));


    private sealed class Fixture
    {
        public static readonly string[] Lines = ["Welcome.", "Take a look."];

        public required WorldServer World { get; init; }
        public required ulong NpcId { get; init; }
        public required PlayerSession Session { get; init; }
        public required NpcAppService Npc { get; init; }
        public required MemoryCharacters Characters { get; init; }
        public required MemoryWarehouses Warehouses { get; init; }
        public required RecordingLog Log { get; init; }
        public required WorldPlayer Player { get; init; }
        public required ItemCatalog Items { get; init; }

        public static async Task<Fixture> CreateAsync(long money, IReadOnlyList<NpcService> services, WorldCatalog? catalog = null)
        {
            var classes = new CharacterClassRegistry();
            var starter = CharacterRecordFactory.NewStarter(classes, 1, "Trader", 0, PlayerClass.Fighter, 3) with { Id = 1 };
            var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
            character.RestoreProgress(0, money);
            var characters = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, new Vector3(100, 0, 100)));
            var warehouses = new MemoryWarehouses(characters);
            var stock = new NpcShopStock([AxeCode], [], [PotionCode]);
            var npc = new NpcDefinition(1, "Merchant", "merchant.inx", services) { DisplayName = "Merchant", Dialog = Lines, Shop = stock };
            var items = new ItemCatalog([Axe(), Potion(), new ItemDefinition(QuestCode, "Letter", ItemKind.Quest)]);
            var empty = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(items, empty.Monsters, new NpcCatalog([npc]),
                    new Dictionary<int, FieldSpawnData> { [3] = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 100 * 256, 100 * 256, 0)] } },
                    empty.Motions)
            };
            catalog ??= new WorldCatalog();
            var log = new RecordingLog();
            var world = new WorldServer(holder, catalog, log: log);
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "owner");
            var sessionService = new SessionAppService(sessions, classes, characters, new ImmediateSaves(characters), catalog, world, holder);
            Assert.True((await sessionService.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
            var outbox = Channel.CreateUnbounded<WorldSnapshot>();
            var player = WorldPlayerFactory.Create(session);
            await WorldPump.RunAsync(world, world.JoinAsync(3, player, outbox.Writer, session));
            Assert.True(outbox.Reader.TryRead(out var snapshot), "roster missing");
            var placed = snapshot.Entities.Single(entity => entity.Kind == EntityKind.Npc);
            return new Fixture
            {
                World = world,
                NpcId = placed.EntityId,
                Session = session,
                Npc = new NpcAppService(sessions, new ImmediateSaves(characters), warehouses, world, holder),
                Characters = characters,
                Warehouses = warehouses,
                Log = log,
                Player = player,
                Items = items
            };
        }
    }

    private sealed class MemoryWarehouses(MemoryCharacters characters) : IWarehouseRepository
    {
        public WarehouseRecord? Record { get; private set; }

        public Task<WarehouseRecord?> FindAsync(long accountId, CancellationToken ct = default) => Task.FromResult(Record);

        // Another session of the account changes the warehouse.
        public void Bump() => Record = Record! with { Revision = Record.Revision + 1 };

        public async Task<bool> SaveWithCharacterAsync(CharacterRecord character, WarehouseRecord warehouse, long expectedRevision,
            CancellationToken ct = default)
        {
            if ((Record?.Revision ?? 0) != expectedRevision)
            {
                return false;
            }

            if (!await characters.UpdateAsync(character, ct))
            {
                return false;
            }

            Record = warehouse;
            return true;
        }
    }
}
