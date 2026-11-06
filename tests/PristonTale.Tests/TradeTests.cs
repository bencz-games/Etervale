using PristonTale.Persistence.Abstractions.GameLogs;
using System.Numerics;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Trades;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

// Trades between players: the request and its answer, the offers and locks, the exchange stored for both characters, and
// how a trade ends.
internal static class TradeTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a trade request answered yes within reach opens the trade; a far or busy player is told", OpenAsync);
        yield return ("when both lock, the offers cross at once: items and gold change hands, both characters are stored together", ExchangeAsync);
        yield return ("a changed offer unlocks both sides, and a side cannot lock within two seconds of the other's change", LockAsync);
        yield return ("an offer holds only the side's own bag items and gold, and an offered item cannot leave the bag", OfferRulesAsync);
        yield return ("a side that cannot take what it receives cannot lock; a busy partner or refused storage unlocks the trade", RefusalsAsync);
        yield return ("a cancel, leaving the world or changing place ends the trade for both", EndAsync);
        yield return ("while the exchange is stored, a side that leaves ends the trade silently and it commits; a cancel ends it if storage refuses", StoringAsync);
        yield return ("an offered item another path changed refuses the exchange for both, and NPC services wait for the trade to end", ChangedAsync);
        yield return ("both sides locking at once go through once the partner's gate frees; a pending request is not replaced", ConcurrencyAsync);
        yield return ("trade rules: the grid, what may pass, and an exchange that does not fit names the side", RulesAsync);
        yield return ("trade messages round-trip on the wire and refuse bad counts and answers", WireAsync);
    }

    private sealed record Fixture(ChatFixture World, TradeAppService Trades, SpecialItemTests.ManualClock Clock);

    private static Fixture Create()
    {
        var clock = new SpecialItemTests.ManualClock();
        var world = new ChatFixture(clock: clock);
        return new Fixture(world, new TradeAppService(world.Sessions, new ImmediateSaves(world.Characters), world.Characters, world.World, world.Directory), clock);
    }

    private static Task Request(Fixture fixture, ChatFixture.Player from, ChatFixture.Player to) =>
        fixture.World.RunAsync(fixture.Trades.RequestAsync(from.Session.SessionToken, to.Id));

    private static Task Answer(Fixture fixture, ChatFixture.Player player, ChatFixture.Player from) =>
        fixture.World.RunAsync(fixture.Trades.AnswerAsync(player.Session.SessionToken, from.Id, true));

    private static Task Offer(Fixture fixture, ChatFixture.Player player, long money, params ItemInstance[] items) =>
        fixture.World.RunAsync(fixture.Trades.OfferAsync(player.Session.SessionToken,
            new TradeOffer(items.Select((item, index) => new TradeEntry(item.InstanceId, index * 2)).ToArray(), money)));

    private static Task Lock(Fixture fixture, ChatFixture.Player player) => fixture.World.RunAsync(fixture.Trades.LockAsync(player.Session.SessionToken));

    private static async Task Open(Fixture fixture, ChatFixture.Player from, ChatFixture.Player to)
    {
        await Request(fixture, from, to);
        await Answer(fixture, to, from);
    }

    private static TradeView? View(Fixture fixture, ChatFixture.Player player)
    {
        fixture.World.Tick(1);
        return ChatFixture.Latest(player)?.Trade;
    }

    private static ItemInstance Give(ChatFixture.Player player, ItemDefinition definition, int count = 1)
    {
        var item = new ItemInstance(definition, count);
        Assert.True(player.Session.Character!.Inventory.TryAdd(item), "fixture item not given");
        return item;
    }

    private static async Task OpenAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var cid = await fixture.World.JoinAsync("Cid", 1000, 0);
        await Request(fixture, ann, cid);
        Assert.True(ann.Link.Last == "Cid is too far away to request a trade", $"far reply: {ann.Link.Last}");

        await Request(fixture, ann, bob);
        Assert.True(ann.Link.Last == "Sent a trade request to Bob", $"request reply: {ann.Link.Last}");
        fixture.World.Tick(1);
        Assert.True(ChatFixture.Latest(bob)?.TradeRequest is { FromName: "Ann" } request && request.FromId == ann.Id, "the request did not reach Bob");
        await fixture.World.RunAsync(fixture.Trades.AnswerAsync(bob.Session.SessionToken, 999, true));
        Assert.True(View(fixture, ann) is null, "an answer to nobody's request opened a trade");
        await Answer(fixture, bob, ann);
        Assert.True(View(fixture, ann) is { PartnerId: var partner, PartnerName: "Bob" } && partner == bob.Id && View(fixture, bob)?.PartnerName == "Ann",
            "the trade did not open on both sides");

        var dan = await fixture.World.JoinAsync("Dan", 0, 100);
        await Request(fixture, dan, ann);
        Assert.True(dan.Link.LastLine == TradeMessages.Of(TradeStatus.Busy), "a trading player was asked");
    }

    private static async Task ExchangeAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var axe = Give(ann, ChatFixture.Axe);
        var potions = Give(bob, ChatFixture.Potion, 10);
        Assert.True(ann.Session.Character!.TryChangeMoney(800), "fixture gold");
        await Open(fixture, ann, bob);
        await Offer(fixture, ann, 500, axe);
        Assert.True(bob.Session.Character!.TryChangeMoney(100), "fixture gold");
        await Offer(fixture, bob, 100, potions);
        Assert.True(View(fixture, bob) is { PartnerMoney: 500, PartnerItems: [{ Name: "Stone Axe" } shown] } && shown.Details is not null,
            "Bob does not see Ann's offer with its values");

        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        await Lock(fixture, ann);
        Assert.True(View(fixture, bob) is { PartnerLocked: true, OwnLocked: false }, "Ann's lock did not show");
        var updates = fixture.World.Characters.Updates;
        await Lock(fixture, bob);
        var annBag = ann.Session.Character.Inventory;
        var bobBag = bob.Session.Character!.Inventory;
        Assert.True(annBag.Find(potions.InstanceId) >= 0 && annBag.Find(axe.InstanceId) < 0 && bobBag.Find(axe.InstanceId) >= 0
            && bobBag.Find(potions.InstanceId) < 0, "the items did not cross");
        Assert.True(ann.Session.Character.Money == 400 && bob.Session.Character.Money == 500, "the gold did not cross");
        Assert.True(fixture.World.Characters.Updates == updates + 1 && fixture.World.Characters.Records["Bob"].Money == 500
            && fixture.World.Characters.Records["Ann"].Money == 400, "both characters were not stored together");
        Assert.True(View(fixture, ann) is null && View(fixture, bob) is null, "the trade outlived its exchange");
        var (annId, bobId) = (fixture.World.Characters.Records["Ann"].Id, fixture.World.Characters.Records["Bob"].Id);
        var rows = fixture.World.Log.Rows;
        Assert.True(rows.Count == 4 && rows.All(row => row.Kind == ItemTransferKind.Trade && row.TransferId == rows[0].TransferId)
            && rows.Any(row => row.InstanceId == axe.InstanceId.ToString() && row.ItemCode == "WA101" && row is { Amount: 1 }
                && row.FromCharacterId == annId && row.ToCharacterId == bobId)
            && rows.Any(row => row.InstanceId == potions.InstanceId.ToString() && row.Amount == 10 && row.FromCharacterId == bobId && row.ToCharacterId == annId)
            && rows.Any(row => row.ItemCode is null && row.Amount == 500 && row.FromCharacterId == annId && row.ToCharacterId == bobId)
            && rows.Any(row => row.ItemCode is null && row.Amount == 100 && row.FromCharacterId == bobId && row.ToCharacterId == annId),
            "the exchange was not logged as one transfer of its items and each side's gold");
    }

    private static async Task LockAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var axe = Give(ann, ChatFixture.Axe);
        var potions = Give(bob, ChatFixture.Potion, 10);
        await Open(fixture, ann, bob);
        await Offer(fixture, bob, 0, potions);
        await Lock(fixture, ann);
        Assert.True(View(fixture, ann) is { OwnLocked: false }, "Ann locked within two seconds of Bob's change");
        fixture.Clock.Now += TimeSpan.FromSeconds(2);
        await Lock(fixture, ann);
        Assert.True(View(fixture, ann) is { OwnLocked: true }, "Ann could not lock after the delay");

        await Offer(fixture, ann, 0, axe);
        Assert.True(View(fixture, ann) is { OwnLocked: true, Own.Entries.Count: 0 }, "a locked side changed its offer");
        await Offer(fixture, bob, 0);
        Assert.True(View(fixture, ann) is { OwnLocked: false, PartnerItems.Count: 0 }, "a changed offer did not unlock the other side");
    }

    private static async Task OfferRulesAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var axe = Give(ann, ChatFixture.Axe);
        var theirs = Give(bob, ChatFixture.Axe);
        await Open(fixture, ann, bob);
        await Offer(fixture, ann, 1);
        Assert.True(ann.Link.LastLine == TradeMessages.Of(TradeStatus.NotEnoughGold), "gold Ann does not hold was offered");
        await Offer(fixture, ann, 0, theirs);
        Assert.True(ann.Link.LastLine == TradeMessages.Of(TradeStatus.Changed), "Bob's item was offered by Ann");
        await Offer(fixture, ann, 0, axe);
        Assert.True(View(fixture, ann)?.Own.Holds(axe.InstanceId) == true, "Ann's own item was refused");

        var drop = new InventoryCommand(Guid.NewGuid(), ann.Session.Character!.InventoryRevision, InventoryAction.Drop, axe.InstanceId, 0, 1, default);
        var prepared = await fixture.World.RunAsync(fixture.World.World.PrepareInventoryAsync(ann.Session, drop));
        Assert.True(prepared is { Outcome.Ok: false, Outcome.Message.Id: GameTextId.ItemInTrade }, "an offered item left the bag");
    }

    private static async Task RefusalsAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        Assert.True(ann.Session.Character!.TryChangeMoney(1000), "fixture gold");
        var limit = MoneyLimits.CarryLimit(bob.Session.Character!.Level, bob.Session.Character.Tier);
        Assert.True(bob.Session.Character.TryChangeMoney(limit - 10), "fixture gold");
        await Open(fixture, ann, bob);
        await Offer(fixture, ann, 100);
        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        await Lock(fixture, bob);
        Assert.True(bob.Link.LastLine == TradeMessages.Of(TradeStatus.GoldLimit) && View(fixture, bob) is { OwnLocked: false },
            "Bob locked gold it may not carry");

        await Offer(fixture, ann, 5);
        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        await Lock(fixture, ann);
        await bob.Session.SaveGate.WaitAsync();
        await Lock(fixture, bob);
        bob.Session.SaveGate.Release();
        Assert.True(View(fixture, ann) is { OwnLocked: true, PartnerLocked: false } && ann.Session.Character.Money == 1000,
            "a lock while Bob's own save gate was busy went through");

        await Lock(fixture, ann);
        await ann.Session.SaveGate.WaitAsync();
        await Lock(fixture, bob);
        ann.Session.SaveGate.Release();
        Assert.True(View(fixture, bob) is { OwnLocked: false, PartnerLocked: false } && bob.Link.LastLine == TradeMessages.Of(TradeStatus.Busy)
            && ann.Session.Character.Money == 1000, "a busy partner did not unlock the trade");

        fixture.World.Characters.BeforeUpdateAll = static _ => Task.FromResult(false);
        await Lock(fixture, ann);
        await Lock(fixture, bob);
        Assert.True(View(fixture, ann) is { OwnLocked: false } && ann.Link.LastLine == TradeMessages.Of(TradeStatus.StorageFailed)
            && ann.Session.Character.Money == 1000, "refused storage changed the characters or kept the trade locked");
    }

    private static async Task EndAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        await Open(fixture, ann, bob);
        await fixture.World.RunAsync(fixture.Trades.CancelAsync(bob.Session.SessionToken));
        Assert.True(View(fixture, ann) is null && ann.Link.Last == "Transaction cancelled" && bob.Link.Last == "Transaction cancelled",
            "a cancel did not end the trade for both");

        await Open(fixture, ann, bob);
        var moved = await fixture.World.RunAsync(fixture.World.World.RelocateAsync(0, bob.Id, new GmPlacement(ChatFixture.Field, new Vector3(150, float.NaN, 0))));
        fixture.World.Directory.Deliver(fixture.World.World.TakeTickNotices());
        Assert.True(moved is not null && View(fixture, ann) is null && View(fixture, bob) is null && ann.Link.Last == "Transaction cancelled",
            "changing place did not end the trade with its line");

        await Open(fixture, ann, bob);
        var left = await fixture.World.RunAsync(fixture.World.World.LeaveAsync(ChatFixture.Field, bob.Id, bob.Outbox.Writer));
        fixture.World.Directory.Deliver(left.Notices);
        Assert.True(View(fixture, ann) is null && ann.Link.Last == "Transaction cancelled", "leaving the world did not end the trade");
    }

    private static async Task StoringAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var axe = Give(ann, ChatFixture.Axe);
        await Open(fixture, ann, bob);
        await Offer(fixture, ann, 0, axe);
        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        WorldLeave? left = null;
        fixture.World.Characters.BeforeUpdateAll = async _ =>
        {
            left = await fixture.World.World.LeaveAsync(ChatFixture.Field, bob.Id, bob.Outbox.Writer);
            return true;
        };
        await Lock(fixture, ann);
        ann.Link.Clear();
        await Lock(fixture, bob);
        Assert.True(left is { Left: true, Notices.Length: 0 } && ann.Link.Lines.Count == 0 && View(fixture, ann) is null,
            "a side leaving while the exchange was stored cancelled it");
        Assert.True(bob.Session.Character!.Inventory.Find(axe.InstanceId) >= 0 && ann.Session.Character!.Inventory.Find(axe.InstanceId) < 0,
            "the stored exchange did not commit");

        var cid = await fixture.World.JoinAsync("Cid", 100, 0);
        await Open(fixture, ann, cid);
        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        fixture.World.Characters.BeforeUpdateAll = async _ =>
        {
            fixture.World.Directory.Deliver(await fixture.World.World.CancelTradeAsync(cid.Session));
            return false;
        };
        await Lock(fixture, ann);
        await Lock(fixture, cid);
        Assert.True(View(fixture, ann) is null && View(fixture, cid) is null && cid.Link.Lines.Any(line => line.Line == TradeMessages.Of(TradeStatus.Cancelled)),
            "a cancel while the exchange was stored was lost when storage refused it");
    }

    private static async Task ChangedAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var potions = Give(ann, ChatFixture.Potion, 10);
        await Open(fixture, ann, bob);
        await Offer(fixture, ann, 0, potions);
        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        await Lock(fixture, ann);
        Assert.True(View(fixture, bob) is { PartnerItems: [{ Count: 10 }] }, "Bob did not see the stack offered");
        var bag = ann.Session.Character!.Inventory;
        Assert.True(bag.ConsumeAt(bag.Find(potions.InstanceId)), "fixture use");
        await Lock(fixture, bob);
        Assert.True(bob.Link.LastLine == TradeMessages.Of(TradeStatus.Changed) && ann.Link.LastLine == TradeMessages.Of(TradeStatus.Changed)
            && bob.Session.Character!.Inventory.Find(potions.InstanceId) < 0, "an exchange took a stack another path had used");
        Assert.True(View(fixture, bob) is { OwnLocked: false, PartnerLocked: false, PartnerItems: [{ Count: 9 }] },
            "the refused exchange did not reopen the trade with the stack as it is");

        var sell = new NpcServiceCommand(Guid.NewGuid(), 1, NpcServiceKind.Shop, NpcServiceAction.Sell, ann.Session.Character.InventoryRevision,
            ItemId: potions.InstanceId);
        var prepared = await fixture.World.RunAsync(fixture.World.World.PrepareNpcServiceAsync(ann.Session, sell));
        Assert.True(prepared?.Reply.Outcome is { Message.Id: GameTextId.CloseTradeFirst } && prepared.Candidate is null, "an NPC service ran during a trade");
    }

    private static async Task ConcurrencyAsync()
    {
        var fixture = Create();
        var ann = await fixture.World.JoinAsync("Ann", 0, 0);
        var bob = await fixture.World.JoinAsync("Bob", 100, 0);
        var cid = await fixture.World.JoinAsync("Cid", 0, 100);
        await Request(fixture, ann, bob);
        await Request(fixture, cid, bob);
        Assert.True(cid.Link.LastLine == TradeMessages.Of(TradeStatus.Busy), "a pending request was replaced");
        await Answer(fixture, bob, ann);

        var axe = Give(ann, ChatFixture.Axe);
        await Offer(fixture, ann, 0, axe);
        fixture.Clock.Now += TimeSpan.FromSeconds(3);
        await Lock(fixture, ann);
        await Offer(fixture, ann, 0, axe);
        Assert.True(View(fixture, bob) is { PartnerLocked: true }, "the same offer sent again unlocked the trade");

        await ann.Session.SaveGate.WaitAsync();
        var locking = fixture.Trades.LockAsync(bob.Session.SessionToken);
        _ = Task.Delay(100).ContinueWith(_ => ann.Session.SaveGate.Release(), TaskScheduler.Default);
        await fixture.World.RunAsync(locking);
        Assert.True(bob.Session.Character!.Inventory.Find(axe.InstanceId) >= 0, "a lock while the partner's gate was briefly busy did not go through");
    }

    private static Task RulesAsync()
    {
        var classes = new CharacterClassRegistry();
        var first = classes.CreatePlayer(PlayerClass.Fighter, "First");
        var second = classes.CreatePlayer(PlayerClass.Fighter, "Second");
        var axe = new ItemInstance(ChatFixture.Axe);
        Assert.True(first.Inventory.TryAdd(axe), "fixture item");
        var offer = new TradeOffer([new TradeEntry(axe.InstanceId, 0)], 0);
        Assert.True(TradeRules.Check(first, offer) == TradeStatus.Ok, "a bag item was refused");
        Assert.True(TradeRules.Check(first, offer with { Entries = [new TradeEntry(axe.InstanceId, 0), new TradeEntry(axe.InstanceId, 4)] })
            == TradeStatus.Rejected, "an item was offered twice");
        Assert.True(TradeRules.Check(first, offer with { Entries = [new TradeEntry(axe.InstanceId, TradeLimits.Columns * TradeLimits.Rows - 1)] })
            == TradeStatus.Rejected, "an item past the grid was laid");
        Assert.True(!TradeRules.IsTradable(ItemKind.Quest, "QT101") && !TradeRules.IsTradable(ItemKind.Potion, "CH101")
            && TradeRules.IsTradable(ItemKind.Potion, "PL101") && !WarehouseStorage.CanStore(ItemKind.Potion, "PL101"),
            "potions trade but are not stored; quest and CH1 items do neither");

        while (second.Inventory.TryAdd(new ItemInstance(ChatFixture.Axe)))
        {
        }

        var status = TradeRules.Exchange(first, offer, second, TradeOffer.Empty, out var side);
        Assert.True(status is TradeStatus.NoSpace or TradeStatus.OverWeight && side == 1, $"a full bag took the item ({status}, side {side})");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[TradeStatePacket.MaxSize];
        var offer = TradeOfferPacket.Read(buffer.AsSpan(0, new TradeOfferPacket(70, [new TradeEntryWire(Guid.NewGuid(), 4)]).Write(buffer)));
        var answer = TradeAnswerPacket.Read(buffer.AsSpan(0, new TradeAnswerPacket(3, true).Write(buffer)));
        var request = TradeRequestPacket.Read(buffer.AsSpan(0, new TradeRequestPacket(8).Write(buffer)));
        var signal = TradeSignalPacket.Read(buffer.AsSpan(0, new TradeSignalPacket(EtervaleOpcodes.TradeLock).Write(buffer)));
        var asked = TradeAskedPacket.Read(buffer.AsSpan(0, new TradeAskedPacket(5, "Ann").Write(buffer)));
        Assert.True(offer is { Money: 70, Entries: [{ Cell: 4 }] } && answer is { FromId: 3, Accept: true } && request.TargetId == 8
            && signal.Code == EtervaleOpcodes.TradeLock && asked is { FromId: 5, FromName: "Ann" }, "trade requests did not round-trip");

        var item = new StoreItemEntry(Guid.NewGuid(), 0x0101_0100, 1, 1, 9, 2, 3, "Stone Axe");
        var state = new TradeStatePacket(2, "Bob", TradeStatePacket.PartnerLocked, 5, 6, [new TradeEntryWire(Guid.NewGuid(), 0)], [item]);
        var read = TradeStatePacket.Read(buffer.AsSpan(0, state.Write(buffer)));
        Assert.True(read is { PartnerId: 2, PartnerName: "Bob", Flags: TradeStatePacket.PartnerLocked, OwnMoney: 5, PartnerMoney: 6 }
            && read.Own.SequenceEqual(state.Own) && read.PartnerItems.SequenceEqual(state.PartnerItems), "the trade window did not round-trip");

        var details = TradeDetailsPacket.Read(buffer.AsSpan(0, new TradeDetailsPacket([new ItemDetailEntry { InstanceId = item.InstanceId, DamageMin = 4 }])
            .Write(buffer)));
        Assert.True(details.Items is [{ DamageMin: 4 } detail] && detail.InstanceId == item.InstanceId, "the partner's item values did not round-trip");

        var length = new TradeAnswerPacket(1, false).Write(buffer);
        buffer[length - 1] = 2;
        Assert.Throws<InvalidDataException>(() => TradeAnswerPacket.Read(buffer.AsSpan(0, length)), "an answer that is neither yes nor no was read");
        length = new TradeOfferPacket(0, []).Write(buffer);
        BitConverter.TryWriteBytes(buffer.AsSpan(length - 4), TradeLimits.MaxItems + 1);
        Assert.Throws<InvalidDataException>(() => TradeOfferPacket.Read(buffer.AsSpan(0, length)), "an offer with too many items was read");
        return Task.CompletedTask;
    }
}
