using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.PersonalShops;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Tests;

internal static class PersonalShopHudTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the personal shop window lays items down each column, keeps them from the bag and follows the bag", WindowAsync);
        yield return ("the personal shop HUD opens in a town, prices bag items, opens with a sign and buys after asking", ControllerAsync);
    }

    private static readonly OwnedItemView Axe = new(Guid.NewGuid(), ItemCodes.Parse("WA101"), "Stone Axe", ItemKind.Axe, 1, 0, -1, 2, 3, "WA101");
    private static readonly OwnedItemView Potions = new(Guid.NewGuid(), ItemCodes.Parse("PL101"), "Mini Life Potion", ItemKind.Potion, 10, 5, -1, 1, 1, "PL101");

    private static Task WindowAsync()
    {
        var window = new HudPersonalShopWindow();
        window.ShowOwn();
        Assert.True(window.TryList(Axe, 1000) && window.TryList(Potions, 50) && !window.TryList(Potions, 60), "two items list once each");
        Assert.True(window.Offers[0].Item.Anchor == 0 && window.Offers[1].Item.Anchor == 3 * HudStoreLayout.Columns,
            "the potion goes down the first column, under the axe");
        Assert.True(window.Holds(Axe.InstanceId) && window.Total == 1500, "the listing holds its items and totals per unit");

        window.Follow([Potions with { Count = 4 }]);
        Assert.True(window.Offers is [{ Item.Count: 4 }] && !window.Holds(Axe.InstanceId) && window.Total == 200,
            "the listing follows the bag: the axe left it, four potions remain");
        Assert.True(window.Entries() is [{ UnitPrice: 50, Cell: 27 } entry] && entry.ItemId == Potions.InstanceId, "the entries carry the cells");
        window.Hide();
        Assert.True(window.Offers.Count == 0 && !window.Holds(Potions.InstanceId), "a hidden window forgets a listing that does not sell");
        return Task.CompletedTask;
    }

    private static async Task ControllerAsync()
    {
        var client = new FakeShops();
        var notes = new List<string>();
        (string First, long Highest, Action<int> Accept)? asked = null;
        var shop = new HudPersonalShopController(NullClientLog.Instance)
        {
            Client = client,
            Notify = notes.Add,
            AskCount = (first, _, highest, accept) => asked = (first, highest, accept)
        };
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Seller");
        var state = OwnerState.Capture(character) with { Items = [Axe, Potions], Money = 1000, InventoryRevision = 7 };
        shop.Update(state, 0);

        shop.OpenOwn(inTown: false);
        Assert.True(!shop.Window.IsOpen && notes[^1] == ClientText.Format(PersonalShopMessages.Of(PersonalShopStatus.NotInTown)), "a shop opens in a town only");
        shop.OpenOwn(inTown: true);
        shop.Window.Advance(1);
        Assert.True(shop.InterceptBagClick(Potions) && asked is { First: "Selling price per unit", Highest: 19_900 },
            "a stack asks its price per unit within the gold the player may carry");
        asked!.Value.Accept(50);
        Assert.True(shop.Holds(Potions.InstanceId) && shop.InterceptBagClick(Potions), "a listed item stays in place");

        var origin = shop.Window.Origin;
        Assert.True(shop.Click(HudStoreLayout.Repair(origin).Center) && shop.ModalOpen, "the start button asks for the sign");
        shop.Type("Cheap potions", _ => false, static _ => true, static text => text.Length);
        shop.Type([], key => key == Microsoft.Xna.Framework.Input.Keys.Enter, static _ => true, static text => text.Length);
        await WaitAsync(shop, state, () => client.Opened is not null);
        Assert.True(client.Opened is ("Cheap potions", [{ UnitPrice: 50, Cell: 0 }]), "the shop opens with its sign and listing");

        Assert.True(shop.Selling && shop.InterceptBagClick(Axe) && asked!.Value.First == "Selling price per unit",
            "an opened shop sells while its sign is on its way, and nothing new is listed");
        shop.Window.Hide();
        Assert.True(shop.Holds(Potions.InstanceId), "a selling listing survives its window");
        shop.SignShown = true;
        shop.Update(state, 0);
        shop.SignShown = false;
        shop.Update(state, 0);
        Assert.True(!shop.Selling && !shop.InterceptBagClick(Axe), "a shop whose sign came down no longer holds the bag while closed");

        // An opened shop whose sign never arrives counts as closed after 300 of the game's ticks, however fast the frames.
        var waiting = new HudPersonalShopController(NullClientLog.Instance)
        {
            Client = client,
            Notify = notes.Add,
            AskCount = (first, _, highest, accept) => asked = (first, highest, accept)
        };
        waiting.Update(state, 0);
        waiting.OpenOwn(inTown: true);
        waiting.Window.Advance(1);
        waiting.InterceptBagClick(Potions);
        asked!.Value.Accept(50);
        waiting.Click(HudStoreLayout.Repair(waiting.Window.Origin).Center);
        waiting.Type("Wait", _ => false, static _ => true, static text => text.Length);
        waiting.Type([], key => key == Microsoft.Xna.Framework.Input.Keys.Enter, static _ => true, static text => text.Length);
        await WaitAsync(waiting, state, () => waiting.Selling);
        for (var frame = 0; frame < 2000; frame++)
        {
            waiting.Update(state, frame * 0.5);
        }

        Assert.True(waiting.Selling, "the sign's wait ran by frames, not by time");
        waiting.Update(state, 300 * 1000d / PristonTale.Contracts.Units.GameUnits.TicksPerSecond + 1);
        Assert.True(!waiting.Selling, "an opened shop without its sign kept selling past its wait");

        var seller = new OwnedItemView(Guid.NewGuid(), Axe.Code, Axe.Name, Axe.Kind, 1, 0, -1, 2, 3, "WA101");
        var buyer = new HudPersonalShopController(NullClientLog.Instance) { Client = client, Notify = notes.Add };
        buyer.Update(state, 0);
        buyer.Window.Show(new PersonalShopListing(9, [new PersonalShopOffer(seller, 400)]));
        buyer.Window.Advance(1);
        Assert.True(buyer.Click(HudStoreLayout.Cell(buyer.Window.Origin, 0).Center) && buyer.ModalOpen, "one item asks whether to buy it");
        Assert.True(buyer.Click(HudMessageBox.Accept.Center), "the question is answered");
        await WaitAsync(buyer, state, () => client.Bought is not null);
        Assert.True(client.Bought is { SellerId: 9, UnitPrice: 400, Count: 1, ExpectedRevision: 7 } bought && bought.ItemId == seller.InstanceId,
            "the purchase names the seller, the item, the price shown and the revision");
    }

    private static async Task WaitAsync(HudPersonalShopController shop, OwnerState state, Func<bool> done)
    {
        for (var frame = 0; frame < 200 && !done(); frame++)
        {
            shop.Update(state, 0);
            await Task.Delay(1);
        }

        Assert.True(done(), "the request was sent");
        shop.Update(state, 0);
    }

    private sealed class FakeShops : IPersonalShopClient
    {
        public (string Sign, IReadOnlyList<PersonalShopEntry> Entries)? Opened { get; private set; }
        public PersonalShopPurchase? Bought { get; private set; }

        private static Task<PersonalShopReply> Done() => Task.FromResult(new PersonalShopReply(new PersonalShopOutcome(true, PersonalShopStatus.Ok, 8, GameText.None)));

        public Task<PersonalShopReply> OpenPersonalShopAsync(string sign, IReadOnlyList<PersonalShopEntry> entries)
        {
            Opened = (sign, entries);
            return Done();
        }

        public Task<PersonalShopReply> ClosePersonalShopAsync() => Done();

        public Task<PersonalShopReply> ViewPersonalShopAsync(ulong sellerId) => Done();

        public Task<PersonalShopReply> BuyFromPersonalShopAsync(PersonalShopPurchase purchase)
        {
            Bought = purchase;
            return Done();
        }
    }
}
