using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Loot;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class EventItemTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("event foods are eaten like potions and restore their authored life, mana and stamina", FoodAsync);
        yield return ("gift boxes need room and weight, draw their prize table and land the prize in the inventory", GiftBoxAsync);
        yield return ("chocolates grant Virtual Life at their level to the gender they are meant for, never in town or twice", ChocolateAsync);
        yield return ("the soccer-ball potion changes the head for three hours, refuses over the big head and ends with a burst", SoccerHeadAsync);
        yield return ("boosters raise the maximum by 15%, stack by kind, show in the effects bar and pause while offline", BoostersAsync);
        yield return ("the skill-delay item recharges every skill in half a second", SkillDelayAsync);
        yield return ("item events replace drop rolls with their items in order and seal puzzle pieces; the action field gives its own", EventDropsAsync);
    }

    private static async Task FoodAsync()
    {
        var food = Food("SP102", 250, 500, 200, 350, 200, 350);
        var harness = await SpecialItemTests.Harness.CreateAsync(20, [], [food], "SP102", "SP104");
        var character = harness.Session.Character!;
        character.Life = 1;
        character.Mana = 0;
        character.Stamina = 0;
        var eat = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("SP102")));
        await harness.AdvanceAsync(eat);
        Assert.True((await eat).Outcome.Ok && harness.Item("SP102") is null && harness.Player.HasPendingPotion, "food was not queued to eat");
        harness.Tick(80);
        Assert.True(character.Life >= Math.Min(character.MaxLife, 251) && character.Mana > 0 && character.Stamina > 0, "food restored nothing");
        Assert.True(!UsableItemCatalog.IsUsable("SP104") && UsableItemCatalog.IsUsable("SP142"), "chestnuts have no defined use");
    }

    private static async Task GiftBoxAsync()
    {
        Assert.True(GiftBoxes.Draw(1, 0).CatalogKey == "PM104" && GiftBoxes.Draw(1, 1999).Count == 10 && GiftBoxes.Draw(1, 2000).CatalogKey == "BI108"
            && GiftBoxes.Draw(6, 9999).CatalogKey == "BI119" && GiftBoxes.Draw(GiftBoxes.TigerCapsule, 9999).CatalogKey == "SE101", "prize tables");
        for (var grade = 1; grade <= GiftBoxes.TigerCapsule; grade++)
        {
            Assert.True(GiftBoxes.Prizes(grade).Sum(static prize => prize.Weight) == 10000, $"grade {grade} weights");
        }

        Assert.True(GiftBoxes.BoxForMonsterLevel(19) == "SP110" && GiftBoxes.BoxForMonsterLevel(20) == "SP109" && GiftBoxes.BoxForMonsterLevel(100) == "SP105",
            "box grade by monster level");

        var prizes = GiftBoxes.Prizes(1).Select(static prize => prize.CatalogKey).Distinct()
            .Select(static key => new ItemDefinition(ItemCodes.Parse(key), key, ItemKind.Misc, MaxStack: key.StartsWith("PM") ? 100 : 1) { CatalogKey = key })
            .ToArray();
        var harness = await SpecialItemTests.Harness.CreateAsync(20, [], prizes, "SP105");
        var character = harness.Session.Character!;
        var open = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("SP105")));
        await harness.AdvanceAsync(open);
        var reply = await open;
        Assert.True(reply.Outcome.Ok && harness.Item("SP105") is null && character.OwnedItems().Count() == 1 && reply.Outcome.Message.Id is GameTextId.ItemReceived or GameTextId.ItemsReceived,
            "the box did not turn into its prize");
        Assert.True(harness.Repository.Record.Inventory.Value.Contains(character.OwnedItems().Single().InstanceId.ToString(), StringComparison.Ordinal),
            "the prize was not stored with the use");

        var full = await SpecialItemTests.Harness.CreateAsync(20, [], prizes, "SP105");
        var crowded = full.Session.Character!;
        var filler = new ItemDefinition(ItemCodes.Parse("WA101"), "Filler", ItemKind.Axe, GridWidth: 1, GridHeight: 1) { CatalogKey = "WA101" };
        while (crowded.Inventory.TryAdd(new ItemInstance(filler)))
        {
        }

        var refused = full.Use(new ItemUseCommand(Guid.NewGuid(), crowded.InventoryRevision, full.Id("SP105")));
        await full.AdvanceAsync(refused);
        Assert.True((await refused).Outcome.Status == ItemUseStatus.NoSpace && full.Item("SP105") is not null, "a full inventory opened the box");
    }

    private static async Task ChocolateAsync()
    {
        Assert.True(Chocolates.TryGet("CH103", out var heart) && heart.Level == 4 && Chocolates.DurationMs(4) == 150_000f && Chocolates.DurationMs(10) == 270_000f,
            "chocolate levels");
        var male = await SpecialItemTests.Harness.CreateAsync(20, "CH101", "CH102", "CH102");
        var fighter = male.Session.Character!;
        var black = male.Use(new ItemUseCommand(Guid.NewGuid(), fighter.InventoryRevision, male.Id("CH101")));
        await male.AdvanceAsync(black);
        Assert.True((await black).Outcome.Status == ItemUseStatus.MaleCannotUse && male.Item("CH101") is not null, "a fighter ate a female chocolate");
        var white = male.Use(new ItemUseCommand(Guid.NewGuid(), fighter.InventoryRevision, male.Id("CH102")));
        await male.AdvanceAsync(white);
        male.Tick(2);
        Assert.True((await white).Outcome.Ok && fighter.Effects.TryGet(Chocolates.VirtualLifeCode, out var life) && life.DurationMs == 270_000f,
            "white chocolate did not grant Virtual Life 10");
        Assert.True(fighter.Effects.Totals.Percent(StatKind.MaxLife) == 30 && fighter.Effects.Totals.Percent(StatKind.DamageTaken) == -30,
            "Virtual Life 10 modifiers");
        Assert.True(male.StatusCodes().Contains(Chocolates.VirtualLifeCode), "the status was not published");
        var twice = male.Use(new ItemUseCommand(Guid.NewGuid(), fighter.InventoryRevision, male.Id("CH102")));
        await male.AdvanceAsync(twice);
        Assert.True((await twice).Outcome.Status == ItemUseStatus.AlreadyRunning, "a second Virtual Life stacked");

        var town = await SpecialItemTests.Harness.CreateAsync(3, "CH102");
        var inTown = town.Use(new ItemUseCommand(Guid.NewGuid(), town.Session.Character!.InventoryRevision, town.Id("CH102")));
        await town.AdvanceAsync(inTown);
        Assert.True((await inTown).Outcome.Status == ItemUseStatus.NotHere, "chocolate worked in town");
    }

    private static async Task SoccerHeadAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, "SP140", "SP140");
        var character = harness.Session.Character!;
        var use = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("SP140")));
        await harness.AdvanceAsync(use);
        Assert.True((await use).Outcome.Ok && character.Premium.BigHeadMode == PremiumTimers.SoccerHeadMode
            && character.Premium.BigHeadExpiresAtSeconds == harness.NowSeconds + EventFoods.SoccerHeadSeconds, "soccer head not started");
        Assert.True(harness.Player.GuardMsLeft > 4000f, "the head change did not guard");
        Assert.True(CharacterHead.ApplyBigHead(new CharacterVisualSet(new("body"), new("head"), 0, 0, 0), PlayerClass.Fighter, 3).Head.InxPath.EndsWith("TmhB-18t.inx"), "soccer head model");
        var again = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("SP140")));
        await harness.AdvanceAsync(again);
        Assert.True((await again).Outcome.Status == ItemUseStatus.SameAttribute, "a second head change was allowed");
        harness.Clock.Now = harness.Clock.Now.AddSeconds(EventFoods.SoccerHeadSeconds);
        harness.Tick(2);
        Assert.True(character.Premium.BigHeadMode == 0 && harness.ItemEffects().Any(effect => effect.Effect == ItemEffectKind.HeadChange), "the head did not end");
    }

    private static async Task BoostersAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, "BC121", "BC124", "BC122");
        var character = harness.Session.Character!;
        var life = character.MaxLife;
        var mana = character.MaxMana;
        var lifeBooster = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC121")));
        await harness.AdvanceAsync(lifeBooster);
        var manaBooster = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC124")));
        await harness.AdvanceAsync(manaBooster);
        Assert.True((await lifeBooster).Outcome.Ok && (await manaBooster).Outcome.Ok, "boosters of two kinds refused");
        Assert.True(character.MaxLife == life + life * 15 / 100 && character.MaxMana == mana + mana * 15 / 100, "boosters did not raise the maxima");
        var same = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC122")));
        await harness.AdvanceAsync(same);
        Assert.True((await same).Outcome.Status == ItemUseStatus.AlreadyRunning, "a second life booster ran");
        Assert.True(harness.ItemEffects().Count(effect => effect.Effect == ItemEffectKind.Drink) == 2, "booster drink sound missing");
        Assert.True(ItemStatuses.Icon(ItemStatuses.PremiumKeepCode(PremiumEffectKind.LifeBooster))!.EndsWith("Booster_01.tga")
            && ItemStatuses.Name(ItemStatuses.PremiumKeepCode(PremiumEffectKind.StaminaBooster)) == "Stamina Booster", "booster keep icons");

        harness.Tick(1);
        var saved = SavedPremium.Capture(character);
        var restored = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Restored");
        var later = harness.NowSeconds + 7200;
        SavedPremium.Restore(restored, CharacterJson.Write(saved), later);
        var booster = restored.Premium.Find(PremiumEffectKind.LifeBooster);
        Assert.True(booster is { } timer && timer.ExpiresAtSeconds == harness.NowSeconds + 3600 + 7200, "the booster did not pause while offline");

        var town = await SpecialItemTests.Harness.CreateAsync(3, "BC121");
        var inTown = town.Use(new ItemUseCommand(Guid.NewGuid(), town.Session.Character!.InventoryRevision, town.Id("BC121")));
        await town.AdvanceAsync(inTown);
        Assert.True((await inTown).Outcome.Status == ItemUseStatus.NotHere, "booster worked in town");
    }

    private static Task SkillDelayAsync()
    {
        var timers = new PremiumTimers();
        Assert.True(PremiumRules.SkillCooldownMs(4000, timers) == 4000, "cooldown without the item");
        timers.Start(PremiumEffectKind.SkillDelay, 0, 300, 0);
        Assert.True(PremiumRules.SkillCooldownMs(4000, timers) == 500 && PremiumRules.SkillCooldownMs(300, timers) == 300, "skill delay cooldown");
        Assert.True(UsableItemCatalog.TryGet("BC130", out var hour) && hour.Grants[0].DurationSeconds == 300
            && UsableItemCatalog.TryGet("BC132", out var day) && day.Grants[0].DurationSeconds == UsableItemCatalog.Day, "skill delay durations");
        return Task.CompletedTask;
    }

    private static Task EventDropsAsync()
    {
        var rng = new CombatRng(5);
        Assert.True(ItemEventDrops.Roll(rng, ItemEventRates.None, 50, 50) is null, "no event runs by default");
        var puzzle = new ItemEventRates { Puzzle = 10000 };
        Assert.True(ItemEventDrops.Roll(rng, puzzle, 50, 50) is { Sealed: true } drop && Puzzles.IsPiece(drop.CatalogKey), "puzzle event");
        Assert.True(ItemEventDrops.Roll(rng, puzzle, 50, 70) is null, "a far player got a puzzle");
        var boxes = new ItemEventRates { GiftBox = 10000 };
        Assert.True(ItemEventDrops.Roll(rng, boxes, 85, 90) is { CatalogKey: "SP106" }, "gift box grade");
        var valentine = new ItemEventRates { ValentineDay = 10000, GiftBox = 10000 };
        Assert.True(ItemEventDrops.Roll(rng, valentine, 40, 40) is { CatalogKey: "CH101" or "CH102" }, "events run in order");
        var horns = Enumerable.Range(0, 20000).Count(_ => ItemEventDrops.Roll(rng, ItemEventRates.None, 10, 130, actionField: true) is
            { CatalogKey: ItemEventDrops.ActionFieldItem });
        Assert.True(horns is > 200 and < 400, $"the action field's item dropped {horns} times in 20000 kills");
        var table = new DropTable([DropEntry.GoldSlot(100, 1, 1)], 1, false);
        var drops = DropRoller.Roll(rng, table, eventRoll: () => RolledDrop.Item(ItemCodes.Parse("SP110")) with { FromMainTable = true });
        Assert.True(drops.Count == 1 && drops[0].ItemCode == ItemCodes.Parse("SP110"), "the event did not replace the drop roll");
        var counts = Enumerable.Range(0, 10000).Select(Puzzles.DropPiece).GroupBy(static key => key).ToDictionary(static group => group.Key, static group => group.Count());
        Assert.True(counts["PZ101"] == 743 && counts["PZ107"] == 380 && counts["PZ208"] == 500 && counts.Count == 16, "puzzle drop weights");
        return Task.CompletedTask;
    }

    private static ItemDefinition Food(string key, int lifeMin, int lifeMax, int manaMin, int manaMax, int staminaMin, int staminaMax) =>
        new(ItemCodes.Parse(key), key, ItemKind.Misc) { CatalogKey = key, Potion = new PotionDefinition(lifeMin, lifeMax, manaMin, manaMax, staminaMin, staminaMax) };
}
