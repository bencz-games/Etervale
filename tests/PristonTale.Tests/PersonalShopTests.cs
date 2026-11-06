using PristonTale.Persistence.Abstractions.GameLogs;
using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class PersonalShopTests
{
    // A drinkable potion that stacks to 100.
    private static readonly ItemDefinition Tonic = ChatFixture.Potion with { Potion = new PotionDefinition(10, 10, 0, 0, 0, 0) };

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a personal shop lists droppable bag items, never puzzle pieces, on the store grid within the gold its seller may carry", ListingRulesAsync);
        yield return ("a purchase moves the item and the gold at once, stores both characters together, tells both and replays without paying twice", PurchaseAsync);
        yield return ("a purchase that met another price, fewer units or a closed shop names what changed; the last unit closes the shop", ChangedShopAsync);
        yield return ("an open shop holds its seller: no walking, attacking or moving items, though its potions may be drunk", SellerHeldAsync);
        yield return ("a seller leaving, alive or dead, while its sale is stored is saved with the sale, not as it left", LeaveDuringTradeAsync);
        yield return ("a busy seller answers Busy, a refused write changes nobody, and a retried purchase answers its first outcome", BusyAndStorageAsync);
        yield return ("a shop closes when its seller is moved, and a mobile shop takes nothing from a seller", ShopEndsAsync);
        yield return ("personal shop messages and the player's shop sign round-trip on the wire", WireAsync);
    }

    private static Task ListingRulesAsync()
    {
        var seller = new CharacterClassRegistry().CreatePlayer(Contracts.Characters.PlayerClass.Fighter, "Seller");
        var axe = new ItemInstance(ChatFixture.Axe);
        var potions = new ItemInstance(Tonic, 10);
        var puzzle = new ItemInstance(new ItemDefinition(ItemCodes.Parse("PZ101"), "Puzzle", ItemKind.Misc) { CatalogKey = "PZ101" });
        var quest = new ItemInstance(new ItemDefinition(ItemCodes.Parse("QT101"), "Letter", ItemKind.Quest) { CatalogKey = "QT101" });
        foreach (var item in new[] { axe, potions, puzzle, quest })
        {
            Assert.True(seller.Inventory.TryPlaceFirstFree(item), "fixture item placed");
        }

        Assert.True(PersonalShopRules.IsListable(seller, axe) && PersonalShopRules.IsListable(seller, potions), "the axe and the potions sell");
        Assert.True(!PersonalShopRules.IsListable(seller, puzzle) && !PersonalShopRules.IsListable(seller, quest), "puzzle pieces and quest items never sell");

        PersonalShopEntry[] entries = [new(axe.InstanceId, 1000, 0), new(potions.InstanceId, 50, 2)];
        Assert.True(PersonalShopRules.TryOpen(seller, inTown: false, "Axe", entries, out _) == PersonalShopStatus.NotInTown, "a shop opens in a town only");
        Assert.True(PersonalShopRules.TryOpen(seller, true, " ", entries, out _) == PersonalShopStatus.Rejected, "a shop needs a sign");
        Assert.True(PersonalShopRules.TryOpen(seller, true, new string('x', PersonalShopLimits.MaxSignBytes + 1), entries, out _) == PersonalShopStatus.Rejected,
            "a sign longer than its bytes is refused");
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Axe", [entries[0], new(potions.InstanceId, 50, 1)], out _) == PersonalShopStatus.Rejected,
            "a potion laid over the axe's cells is refused");
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Axe", [entries[0], entries[0] with { Cell = 4 }], out _) == PersonalShopStatus.Rejected,
            "one item listed twice is refused");
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Axe", [new(puzzle.InstanceId, 10, 4)], out _) == PersonalShopStatus.NotListable,
            "a puzzle piece is refused");
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Axe", [entries[0] with { UnitPrice = 0 }], out _) == PersonalShopStatus.Rejected,
            "a free item is refused");

        // Level 1 carries 200,000: the potions bring 10 x 19,990 and the axe 1,000 more than that.
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Axe", [entries[0], entries[1] with { UnitPrice = 19_990 }], out _) == PersonalShopStatus.GoldLimit,
            "a listing that would bring more gold than the seller may carry is refused");
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Axe and potions", entries, out var shop) == PersonalShopStatus.Ok
            && shop.Sign == "Axe and potions" && shop.Entries.Count == 2, "a valid listing opens");
        Assert.True(PersonalShopRules.Total(seller.Inventory, entries) == 1500, "the listing totals the potions per unit");
        return Task.CompletedTask;
    }

    private static async Task PurchaseAsync()
    {
        var (fixture, shops, seller, buyer) = await OpenAsync();
        var axeId = seller.Session.Character!.Inventory.Items().Single(item => item.Definition == ChatFixture.Axe).InstanceId;
        var view = await fixture.RunAsync(shops.ViewAsync(buyer.Session.SessionToken, seller.Id));
        Assert.True(view.Outcome.Ok && view.Listing is { Offers.Count: 2 } listing && listing.SellerId == seller.Id, "the buyer sees the two offers");
        Assert.True(view.Listing!.Offers.Single(offer => offer.Item.InstanceId == axeId) is { UnitPrice: 1000, Item.Anchor: 0, Item.Details: not null },
            "an offer shows its price, its cell and the copy's values");

        var updates = fixture.Characters.Updates;
        var purchase = new PersonalShopPurchase(Guid.NewGuid(), seller.Id, axeId, 1000, 1, buyer.Session.Character!.InventoryRevision);
        var bought = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase));
        Assert.True(bought.Outcome.Ok, $"the purchase went through: {bought.Outcome.Message}");
        Assert.True(buyer.Session.Character.Inventory.Find(axeId) >= 0 && seller.Session.Character.Inventory.Find(axeId) < 0, "the axe changed bags");
        Assert.True(buyer.Session.Character.Money == 4000 && seller.Session.Character.Money == 1000, "the gold changed hands");
        Assert.True(fixture.Characters.Updates == updates + 1, "both characters were stored in one write");
        Assert.True(fixture.Characters.Records["Buyer"].Money == 4000 && fixture.Characters.Records["Seller"].Money == 1000, "the stored rows hold the trade");
        var (sellerId, buyerId) = (fixture.Characters.Records["Seller"].Id, fixture.Characters.Records["Buyer"].Id);
        Assert.True(fixture.Log.Rows is [var sale, var paid] && sale.Kind == ItemTransferKind.PersonalShop && sale.InstanceId == axeId.ToString()
            && sale.FromCharacterId == sellerId && sale.ToCharacterId == buyerId && paid is { ItemCode: null, Amount: 1000 }
            && paid.FromCharacterId == buyerId && paid.ToCharacterId == sellerId && paid.TransferId == sale.TransferId,
            "the purchase was not logged as the axe to the buyer and the price to the seller");
        Assert.True(bought.Listing is { Offers.Count: 1 }, "the reply shows what remains on sale");
        Assert.True(seller.Link.Last.Contains("Buyer player has bought 1 number of Stone Axe by 1000 Gold") && seller.Link.Last == buyer.Link.Last,
            "both read the sale");

        var replay = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase));
        Assert.True(replay.Outcome is { Ok: true, Message.Id: GameTextId.AlreadyApplied } && buyer.Session.Character.Money == 4000, "a retried purchase pays once");
        var conflict = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase with { Count = 2 }));
        Assert.True(conflict.Outcome.Status == PersonalShopStatus.Conflict, "the same operation id with another purchase is a conflict");
    }

    private static async Task ChangedShopAsync()
    {
        var (fixture, shops, seller, buyer) = await OpenAsync();
        var potionId = seller.Session.Character!.Inventory.Items().Single(item => item.Definition == Tonic).InstanceId;
        PersonalShopPurchase Purchase(long price, int count) =>
            new(Guid.NewGuid(), seller.Id, potionId, price, count, buyer.Session.Character!.InventoryRevision);

        var cheaper = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Purchase(40, 1)));
        Assert.True(cheaper.Outcome is { Status: PersonalShopStatus.PriceChanged, UnitPrice: 50 } && cheaper.Listing is not null,
            "a purchase at another price names the price and shows the shop again");
        var four = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Purchase(50, 4)));
        Assert.True(four.Outcome.Ok && buyer.Session.Character!.Money == 4800 && seller.Session.Character.Money == 200, "four potions sold at 50 each");
        var many = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Purchase(50, 7)));
        Assert.True(many.Outcome is { Status: PersonalShopStatus.CountChanged, Count: 6 }, "asking for more units than remain names how many do");
        var poor = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Purchase(50, 6) with { SellerId = buyer.Id }));
        Assert.True(poor.Outcome.Status == PersonalShopStatus.Closed, "a player without a shop sells nothing");

        Assert.True((await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Purchase(50, 6)))).Outcome.Ok, "the last six sold");
        var axeId = seller.Session.Character.Inventory.Items().Single(item => item.Definition == ChatFixture.Axe).InstanceId;
        Assert.True((await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Purchase(1000, 1) with { ItemId = axeId }))).Outcome.Ok,
            "the axe sold");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(seller)!.Value.Entities.Single(view => view.EntityId == seller.Id).Appearance.ShopSign == "",
            "a shop with nothing left closes and takes its sign down");
        var closed = await fixture.RunAsync(shops.ViewAsync(buyer.Session.SessionToken, seller.Id));
        Assert.True(closed.Outcome.Status == PersonalShopStatus.Closed && closed.Listing is null, "a closed shop shows nothing");
    }

    private static async Task SellerHeldAsync()
    {
        var (fixture, shops, seller, _) = await OpenAsync();
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(seller)!.Value.Entities.Single(view => view.EntityId == seller.Id).Appearance.ShopSign == "Axe and potions",
            "the sign hangs over the seller");

        var start = (await fixture.RunAsync(fixture.World.PositionOfAsync(seller.Id)))!.Value;
        var verdict = await fixture.RunAsync(fixture.World.ReportMovementAsync(ChatFixture.Field, seller.Id, start + new Vector3(200, 0, 0), 0, true, false, 0));
        Assert.True(verdict == MovementVerdict.Rejected && (await fixture.RunAsync(fixture.World.PositionOfAsync(seller.Id)))!.Value == start,
            "a selling player does not walk");
        Assert.True(!await fixture.RunAsync(fixture.World.SetAttackTargetAsync(ChatFixture.Field, seller.Id, 0, false)), "a selling player does not attack");

        var character = seller.Session.Character!;
        var axe = character.Inventory.Items().Single(item => item.Definition == ChatFixture.Axe);
        var sessions = new SessionAppService(fixture.Sessions, new CharacterClassRegistry(), fixture.Characters, new ImmediateSaves(fixture.Characters), fixture.Catalog, fixture.World, fixture.Data);
        var move = await fixture.RunAsync(sessions.ApplyInventoryAsync(seller.Session.SessionToken,
            new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.Move, axe.InstanceId, 20, 1, default)));
        Assert.True(!move.Ok && move.Message == GameText.Of(GameTextId.CloseShopFirst), "a selling player moves no item");

        var potion = character.Inventory.Items().Single(item => item.Definition == Tonic);
        var drink = await fixture.RunAsync(sessions.ApplyInventoryAsync(seller.Session.SessionToken,
            new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.ConsumePotion, potion.InstanceId, -1, 1, default)));
        Assert.True(drink.Ok && character.FindOwnedItem(potion.InstanceId)?.Count == 9, $"a selling player drinks its potions: {drink.Message}");

        Assert.True((await fixture.RunAsync(shops.CloseAsync(seller.Session.SessionToken))).Ok, "the seller closes its shop");
        verdict = await fixture.RunAsync(fixture.World.ReportMovementAsync(ChatFixture.Field, seller.Id, start + new Vector3(20, 0, 0), 0, true, false, 0));
        Assert.True(verdict != MovementVerdict.Rejected, "a closed shop lets the player walk again");
    }

    private static async Task LeaveDuringTradeAsync()
    {
        foreach (var dead in new[] { false, true })
        {
            var (fixture, shops, seller, buyer) = await OpenAsync();
            var storing = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            fixture.Characters.BeforeUpdateAll = async _ =>
            {
                storing.TrySetResult();
                await release.Task;
                return true;
            };
            var axeId = seller.Session.Character!.Inventory.Items().Single(item => item.Definition == ChatFixture.Axe).InstanceId;
            var buy = shops.BuyAsync(buyer.Session.SessionToken, new PersonalShopPurchase(Guid.NewGuid(), seller.Id, axeId, 1000, 1,
                buyer.Session.Character!.InventoryRevision));
            for (var tick = 0; tick < 2000 && !storing.Task.IsCompleted; tick++)
            {
                fixture.Tick(1);
                await Task.Delay(1);
            }

            Assert.True(storing.Task.IsCompleted, "the sale reached its write");
            if (dead)
            {
                WorldPlayer? player = null;
                Assert.True(fixture.World.TryGetOpenField(ChatFixture.Field, out var field) && field.TryGet(seller.Id, out player), "the seller is in its field");
                player!.ApplyDamage(int.MaxValue);
            }

            var leave = await fixture.RunAsync(fixture.World.LeaveAsync(ChatFixture.Field, seller.Id, seller.Outbox.Writer));
            Assert.True(leave.Left && leave.Save is null, "the leave leaves the save to after the sale");
            seller.Session.EndPlay();
            release.SetResult();
            Assert.True((await fixture.RunAsync(buy)).Outcome.Ok, "the sale went through");

            var sessions = new SessionAppService(fixture.Sessions, new CharacterClassRegistry(), fixture.Characters, new ImmediateSaves(fixture.Characters), fixture.Catalog, fixture.World, fixture.Data);
            Assert.True(await fixture.RunAsync(sessions.SaveCharacterAsync(seller.Session.SessionToken)), "the seller was saved");
            var row = fixture.Characters.Records["Seller"];
            Assert.True(row.Money == 1000 && !row.Inventory.ToString().Contains(axeId.ToString(), StringComparison.OrdinalIgnoreCase),
                $"the stored seller keeps the sale (dead {dead})");
            var place = dead ? Simulation.Progression.DeathPenalty.TownField(Contracts.Characters.PlayerClass.Fighter) : ChatFixture.Field;
            Assert.True(row.FieldId == place, $"a seller that left dead is saved at its town (dead {dead}): {row.FieldId}");
        }
    }

    private static async Task BusyAndStorageAsync()
    {
        var (fixture, shops, seller, buyer) = await OpenAsync();
        var axeId = seller.Session.Character!.Inventory.Items().Single(item => item.Definition == ChatFixture.Axe).InstanceId;
        var purchase = new PersonalShopPurchase(Guid.NewGuid(), seller.Id, axeId, 1000, 1, buyer.Session.Character!.InventoryRevision);
        Assert.True(await seller.Session.SaveGate.WaitAsync(0), "the seller's gate is taken");
        Assert.True((await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase))).Outcome.Status == PersonalShopStatus.Busy,
            "a busy seller answers Busy");
        Assert.True((await fixture.RunAsync(shops.CloseAsync(seller.Session.SessionToken))).Status == PersonalShopStatus.Busy,
            "the shop does not change while its seller is busy");
        seller.Session.SaveGate.Release();

        fixture.Characters.BeforeUpdateAll = _ => Task.FromResult(false);
        var refused = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase));
        Assert.True(refused.Outcome.Status == PersonalShopStatus.StorageFailed && buyer.Session.Character.Money == 5000
            && seller.Session.Character.Inventory.Find(axeId) >= 0 && buyer.Session.Character.ShopPurchases.Find(purchase.OperationId) is null,
            "a refused write leaves both characters as they were");
        fixture.Characters.BeforeUpdateAll = null;
        Assert.True((await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase))).Outcome.Ok, "the same purchase then goes through");

        Assert.True(await seller.Session.SaveGate.WaitAsync(0), "the seller's gate is taken again");
        var replay = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, purchase));
        Assert.True(replay.Outcome is { Ok: true, Message.Id: GameTextId.AlreadyApplied }, "a retried purchase answers its first outcome while the seller is busy");
        seller.Session.SaveGate.Release();
    }

    private static async Task ShopEndsAsync()
    {
        var (fixture, shops, seller, buyer) = await OpenAsync();
        var character = seller.Session.Character!;
        var axeId = character.Inventory.Items().Single(item => item.Definition == ChatFixture.Axe).InstanceId;
        seller.Session.MobileShopOpen = true;
        var npc = new NpcAppService(fixture.Sessions, new ImmediateSaves(fixture.Characters), fixture.Warehouses, fixture.World, fixture.Data);
        var sold = await fixture.RunAsync(npc.RequestAsync(seller.Session.SessionToken,
            new NpcServiceCommand(Guid.NewGuid(), 0, NpcServiceKind.Shop, NpcServiceAction.Sell, character.InventoryRevision, ItemId: axeId)));
        Assert.True(!sold.Outcome.Ok && character.Inventory.Find(axeId) >= 0, "a mobile shop takes nothing from a seller");

        await fixture.RunAsync(fixture.World.RelocateAsync(seller.Id, seller.Id,
            new Application.GameMasters.GmPlacement(ChatFixture.Field, new Vector3(300, 0, 300))));
        var closed = await fixture.RunAsync(shops.ViewAsync(buyer.Session.SessionToken, seller.Id));
        Assert.True(closed.Outcome.Status == PersonalShopStatus.Closed, "a moved seller's shop closes");
    }

    private static Task WireAsync()
    {
        var buffer = new byte[8192];
        T RoundTrip<T>(Func<Span<byte>, int> write, Func<ReadOnlySpan<byte>, T> read) => read(buffer.AsSpan(0, write(buffer)));

        var id = Guid.NewGuid();
        var item = Guid.NewGuid();
        PersonalShopEntryWire[] entries = [new(item, 1000, 0), new(Guid.NewGuid(), 50, 40)];
        var open = RoundTrip(new PersonalShopOpenPacket(id, "Axes cheap", entries).Write, PersonalShopOpenPacket.Read);
        Assert.True(open.OperationId == id && open.Sign == "Axes cheap" && open.Entries.SequenceEqual(entries), "the open request round-trips");
        var buy = new PersonalShopBuyPacket(id, 9, item, 50, 4, 17);
        Assert.True(RoundTrip(buy.Write, PersonalShopBuyPacket.Read) == buy, "the purchase round-trips");
        var view = new PersonalShopViewPacket(id, 9);
        Assert.True(RoundTrip(view.Write, PersonalShopViewPacket.Read) == view && RoundTrip(new PersonalShopClosePacket(id).Write, PersonalShopClosePacket.Read).OperationId == id,
            "the view and close requests round-trip");
        var result = new PersonalShopResultPacket(id, (byte)PersonalShopAction.Buy, (ushort)PersonalShopStatus.CountChanged, 6, 18, 50,
            PersonalShopMessages.Of(PersonalShopStatus.CountChanged));
        Assert.True(RoundTrip(result.Write, PersonalShopResultPacket.Read) == result, "the result round-trips");

        PersonalShopOfferWire[] offers = [new(new StoreItemEntry(item, 0x0101_0100, (byte)ItemKind.Axe, 1, 0, 2, 3, "Stone Axe"), 1000)];
        var listing = RoundTrip(new PersonalShopListingPacket(id, 9, offers).Write, PersonalShopListingPacket.Read);
        Assert.True(listing is { SellerId: 9 } && listing.Offers.SequenceEqual(offers), "the listing round-trips");
        var details = RoundTrip(new PersonalShopDetailsPacket(id, 9, [new ItemDetailEntry { InstanceId = item, DamageMin = 4 }]).Write, PersonalShopDetailsPacket.Read);
        Assert.True(details.Items is [{ DamageMin: 4 } detail] && detail.InstanceId == item, "the details round-trip");

        var looks = new DescribedPlayer(Contracts.Characters.PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, true, false, 0, 0) { ShopSign = "Axes cheap" };
        var described = RoundTrip(new EntityDescriptionPacket(9, 2, Contracts.World.EntityKind.Player, 10, 3, 1f, "Seller", "", looks).Write,
            EntityDescriptionPacket.Read);
        Assert.True(described.Player?.ShopSign == "Axes cheap", "the shop sign rides on the player's description");
        return Task.CompletedTask;
    }

    // A town with a seller (an axe at 1,000 and ten potions at 50 each, no gold) and a buyer with 5,000 gold beside it.
    private static async Task<(ChatFixture Fixture, PersonalShopAppService Shops, ChatFixture.Player Seller, ChatFixture.Player Buyer)> OpenAsync()
    {
        var fixture = new ChatFixture(state: FieldState.Village);
        var shops = new PersonalShopAppService(fixture.Sessions, new ImmediateSaves(fixture.Characters), fixture.Characters, fixture.World, fixture.Directory);
        var seller = await fixture.JoinAsync("Seller", 100, 100);
        var buyer = await fixture.JoinAsync("Buyer", 140, 100);
        var axe = new ItemInstance(ChatFixture.Axe);
        var potions = new ItemInstance(Tonic, 10);
        Assert.True(seller.Session.Character!.Inventory.TryPlace(axe, 0) && seller.Session.Character.Inventory.TryPlace(potions, 5), "the seller's goods");
        seller.Session.Character.TryChangeMoney(-seller.Session.Character.Money);
        buyer.Session.Character!.TryChangeMoney(5000 - buyer.Session.Character.Money);
        var opened = await fixture.RunAsync(shops.OpenAsync(seller.Session.SessionToken, "Axe and potions",
            [new PersonalShopEntry(axe.InstanceId, 1000, 0), new PersonalShopEntry(potions.InstanceId, 50, 2)]));
        Assert.True(opened.Ok, $"the shop opened: {opened.Message}");
        return (fixture, shops, seller, buyer);
    }
}
