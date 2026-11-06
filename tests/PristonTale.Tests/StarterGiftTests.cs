using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The gifts of a new character: their order, their pace, perfect copies of the class's specialization worn on a free
// slot or left in the bag, the class's own weapon and off-hand, the chat line of each, the count stored with the
// character and read back, a full bag, and the setting that turns them off.
internal static class StarterGiftTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a new Mechanician receives a perfect claw, armour and shield of its class and three Hopy crystals, seconds apart, worn", GiftsAsync);
        yield return ("each class's weapon is of its own family, and a class without an off-hand beside its weapon skips it", KitAsync);
        yield return ("a new record holds no starter gift, a stored count is read back, and a count out of range is refused", CountAsync);
        yield return ("a full bag leaves the gift for the next offer, and the setting off offers none", WaitAsync);
    }

    private static readonly ItemTemplate Specialized = new()
    {
        Durability = new ItemRange(40, 60),
        DamageMin = new ItemRange(2, 4),
        DamageMax = new ItemRange(5, 9),
        RandomSpecJobs = [(uint)JobBitCode.Mechanician, (uint)JobBitCode.Fighter, (uint)JobBitCode.Archer],
        Spec = new ItemSpecTemplate { Defence = new ItemRange(1, 9) }
    };

    private static readonly ItemDefinition Claw = new(ItemCodes.Parse("WC101"), "Claw", ItemKind.Claw, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 2)
    {
        CatalogKey = "WC101",
        Weight = 5,
        Template = Specialized
    };

    // Its own specialization is the Fighter's: a Mechanician given it would wield a Fighter's weapon.
    private static readonly ItemDefinition Axe = new(ItemCodes.Parse("WA101"), "Stone Axe", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 1, GridHeight: 3)
    {
        CatalogKey = "WA101",
        Template = Specialized with { SpecJob = (uint)JobBitCode.Fighter, RandomSpecJobs = [] }
    };

    private static readonly ItemDefinition Bow = new(ItemCodes.Parse("WS101"), "Short Bow", ItemKind.Bow, EquipmentSlot.Weapon, TwoHanded: true, GridWidth: 2, GridHeight: 3)
    {
        CatalogKey = "WS101",
        Template = Specialized with { SpecJob = (uint)JobBitCode.Archer, RandomSpecJobs = [] }
    };

    private static readonly ItemDefinition Suit = new(ItemCodes.Parse("DA102"), "Battle Suit", ItemKind.Armor, EquipmentSlot.Armor, GridWidth: 3, GridHeight: 4)
    {
        CatalogKey = "DA102",
        Weight = 10,
        Template = Specialized with { DamageMin = default, DamageMax = default, Defence = new ItemRange(5, 12) }
    };

    private static readonly ItemDefinition Shield = new(ItemCodes.Parse("DS101"), "Buckler", ItemKind.Shield, EquipmentSlot.OffHand, GridWidth: 2, GridHeight: 2)
    {
        CatalogKey = "DS101",
        Weight = 4,
        Template = Specialized with { DamageMin = default, DamageMax = default, Defence = new ItemRange(2, 6) }
    };

    private static readonly ItemDefinition Crystal = new(ItemCodes.Parse("GP101"), "Hopy Crystal", ItemKind.Misc, GridWidth: 2, GridHeight: 2) { CatalogKey = "GP101", Weight = 1 };

    private static readonly ItemCatalog Items = new([Claw, Axe, Bow, Suit, Shield, Crystal]);

    private static readonly int OfferTicks = (int)StarterGifts.Interval.TotalSeconds * WorldServer.TickHz;

    private static async Task GiftsAsync()
    {
        var (fixture, gifts, fresh) = await NewcomerAsync();
        var character = fresh.Session.Character!;
        var ticks = new List<int>();
        var effects = new List<ItemEffectView>();
        var tick = 0;
        while (ticks.Count < StarterGifts.Count && tick < (OfferTicks + WorldServer.TickHz) * (StarterGifts.Count + 1))
        {
            fixture.Tick(1);
            tick++;
            while (fresh.Outbox.Reader.TryRead(out var snapshot))
            {
                effects.AddRange(snapshot.ItemEffects.Where(effect => effect.Effect == ItemEffectKind.StarterGift));
            }

            if (fixture.World.StarterGiftRequests.TryRead(out var session))
            {
                ticks.Add(tick);
                await fixture.RunAsync(gifts.GiveAsync(session, CancellationToken.None));
            }
        }

        Assert.True(ticks.Count == StarterGifts.Count && ticks[0] >= StarterGifts.FirstDelay.TotalSeconds * WorldServer.TickHz
            && ticks.Zip(ticks.Skip(1)).All(pair => pair.Second - pair.First >= OfferTicks), $"the gifts came at ticks {string.Join(", ", ticks)}");
        (ItemDefinition Definition, EquipmentSlot Slot)[] worn = [(Claw, EquipmentSlot.Weapon), (Suit, EquipmentSlot.Armor), (Shield, EquipmentSlot.OffHand)];
        Assert.True(worn.All(entry => character.GetEquipped(entry.Slot) is { } item && item.Definition == entry.Definition
                && item.DurabilityMax == entry.Definition.Template!.Durability.Max && item.Roll.SpecJob == (uint)JobBitCode.Mechanician),
            $"the claw, the armour and the shield were not worn, perfect and specialized for the Mechanician alone: "
            + string.Join(", ", worn.Select(entry => $"{character.GetEquipped(entry.Slot)?.Definition.Name} {character.GetEquipped(entry.Slot)?.Roll.SpecJob}")));
        Assert.True(character.Inventory.Occupied().Count(entry => entry.Item.Definition == Crystal) == 3 && character.StarterGiftsReceived == StarterGifts.Count
            && fixture.Characters.Records["Fresh"].StarterGiftsReceived == StarterGifts.Count, "the crystals or the stored count are wrong");
        Assert.True(fresh.Link.Lines.Count(line => line.Line == StarterGifts.Message("Claw")) == 1
            && fresh.Link.Lines.Count(line => line.Line == StarterGifts.Message("Hopy Crystal")) == 3, "each gift was not told in the chat");

        fixture.Tick(2);
        while (fresh.Outbox.Reader.TryRead(out var snapshot))
        {
            effects.AddRange(snapshot.ItemEffects.Where(effect => effect.Effect == ItemEffectKind.StarterGift));
        }

        Assert.True(effects.Select(effect => (effect.EntityId, effect.Variant)).SequenceEqual(Enumerable.Range(0, StarterGifts.Count).Select(index => (fresh.Id, index)))
            && effects[0].ItemCode == Claw.Code, $"the gifts' effects for the beginner's guide are wrong: {string.Join(", ", effects.Select(effect => effect.Variant))}");

        fixture.Tick(OfferTicks * 2);
        Assert.True(!fixture.World.StarterGiftRequests.TryRead(out _), "a character with every gift was offered another");
    }

    private static Task KitAsync()
    {
        var families = Enum.GetValues<PlayerClass>().ToDictionary(playerClass => playerClass, playerClass => StarterGifts.WeaponKey(playerClass)[..2]);
        Assert.True(families[PlayerClass.Fighter] == "WA" && families[PlayerClass.Mechanician] == "WC" && families[PlayerClass.Pikeman] == "WP"
            && families[PlayerClass.Knight] == "WS" && families[PlayerClass.Assassin] == "WD" && families[PlayerClass.Shaman] == "WN"
            && families[PlayerClass.Magician] == "WM", $"a class's weapon is not of its family: {string.Join(", ", families)}");
        Assert.True(StarterGifts.Next(Items, PlayerClass.Mechanician, StarterGifts.OffHand) == StarterGifts.OffHand
            && StarterGifts.Next(Items, PlayerClass.Archer, StarterGifts.OffHand) == StarterGifts.OffHand + 1
            && StarterGifts.Create(Items, PlayerClass.Archer, StarterGifts.Weapon)!.Roll.SpecJob == (uint)JobBitCode.Archer,
            "the archer's two-handed bow took a shield, or the Mechanician had none");
        Assert.True(StarterGifts.Create(Items, PlayerClass.Fighter, StarterGifts.OffHand)!.Roll.SpecJob == (uint)JobBitCode.Fighter
            && StarterGifts.ItemKey(Items, PlayerClass.Magician, StarterGifts.Armour) == "DA202"
            && StarterGifts.ItemKey(Items, PlayerClass.Shaman, StarterGifts.Armour) == "DA202", "the Fighter's shield or the robes are wrong");
        return Task.CompletedTask;
    }

    private static Task CountAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Fresh", 0, PlayerClass.Archer, ChatFixture.Field);
        var restored = classes.CreatePlayer(PlayerClass.Archer, "Fresh");
        CharacterStateCodec.Restore(restored, record, Items);
        var midway = classes.CreatePlayer(PlayerClass.Archer, "Fresh");
        CharacterStateCodec.Restore(midway, record with { StarterGiftsReceived = 3 }, Items);
        Assert.True(record.StarterGiftsReceived == 0 && restored.StarterGiftsReceived == 0 && midway.StarterGiftsReceived == 3,
            "a new record or a stored count did not come back");
        Assert.Throws<ArgumentException>(() => CharacterStateCodec.Restore(classes.CreatePlayer(PlayerClass.Archer, "Fresh"),
            record with { StarterGiftsReceived = -1 }, Items), "a negative count was taken");
        return Task.CompletedTask;
    }

    private static async Task WaitAsync()
    {
        var (fixture, gifts, fresh) = await NewcomerAsync();
        var character = fresh.Session.Character!;
        // A worn weapon keeps the claw off its slot, so only the bag could take it.
        var held = new ItemInstance(Claw);
        character.Equip(EquipmentSlot.Weapon, held);
        foreach (var page in new[] { character.Inventory, character.HiddenBag })
        {
            while (page.TryAdd(new ItemInstance(Suit)) || page.TryAdd(new ItemInstance(Shield)))
            {
            }
        }

        var session = NextOffer(fixture, StarterGifts.FirstDelay);
        await fixture.RunAsync(gifts.GiveAsync(session!, CancellationToken.None));
        Assert.True(character.StarterGiftsReceived == 0 && character.GetEquipped(EquipmentSlot.Weapon) == held && fresh.Link.Lines.Count == 0,
            "a gift without room was given or counted");
        Assert.True(NextOffer(fixture, StarterGifts.Interval) is not null, "a gift without room was not offered again");

        var (quiet, _, _) = await NewcomerAsync();
        await quiet.RunAsync(quiet.World.ApplyOptionsAsync(quiet.World.Options with { GiveStarterGifts = false }));
        Assert.True(NextOffer(quiet, StarterGifts.Interval) is null, "a gift was offered with the setting off");
    }

    // A fixture with the gifts' items and a Mechanician that has received none.
    private static async Task<(ChatFixture Fixture, StarterGiftAppService Gifts, ChatFixture.Player Fresh)> NewcomerAsync()
    {
        var fixture = new ChatFixture();
        var empty = fixture.Data.Current;
        fixture.Data.Current = new WorldData(Items, empty.Monsters, empty.Npcs, empty.FieldSpawns, empty.Motions);
        var gifts = new StarterGiftAppService(fixture.World, new ImmediateSaves(fixture.Characters), fixture.Directory);
        var fresh = await fixture.JoinAsync("Fresh", 0, 0, playerClass: PlayerClass.Mechanician);
        fresh.Session.Character!.RestoreStarterGifts(0);
        return (fixture, gifts, fresh);
    }

    // The session offered a gift within the time and a second more, or null.
    private static PlayerSession? NextOffer(ChatFixture fixture, TimeSpan within)
    {
        for (var tick = 0; tick <= (within.TotalSeconds + 1) * WorldServer.TickHz; tick++)
        {
            fixture.Tick(1);
            if (fixture.World.StarterGiftRequests.TryRead(out var session))
            {
                return session;
            }
        }

        return null;
    }
}
