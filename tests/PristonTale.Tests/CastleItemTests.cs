using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class CastleItemTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("castle scrolls and stones work only in the castle, exclude their premium scroll and replace their own kind", ScrollsAsync);
        yield return ("Defiance and Might stones halve their tower's blows and strengthen strikes on their class", StonesAsync);
        yield return ("castle items end when their holder leaves the castle field, Athanasia's guard with them", LeaveAsync);
        yield return ("a Restoration holds its dead owner in a castle field and revives him after ten seconds", RestorationAsync);
    }

    private static async Task ScrollsAsync()
    {
        var outside = await SpecialItemTests.Harness.CreateAsync(20, "BC102");
        var refused = outside.Use(new ItemUseCommand(Guid.NewGuid(), outside.Session.Character!.InventoryRevision, outside.Id("BC102")));
        await outside.AdvanceAsync(refused);
        Assert.True((await refused).Outcome.Status == ItemUseStatus.NotHere && outside.Item("BC102") is not null, "a castle scroll worked outside the castle");
        Assert.True(!UsableItemCatalog.IsUsable("BC104") && !UsableItemCatalog.IsUsable("BC117") && !UsableItemCatalog.IsUsable("BC136"),
            "castle items without a defined use stay unusable");

        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, "BC102", "BC102", "BC103", "BC101", "BI105");
        var character = harness.Session.Character!;
        var crit = character.Effects.Totals.Flat(StatKind.CriticalChance);
        var edge = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC102")));
        await harness.AdvanceAsync(edge);
        harness.Tick(1);
        Assert.True((await edge).Outcome.Ok && character.Effects.Totals.Flat(StatKind.CriticalChance) == crit + 5, "Deadly Edge critical");
        Assert.True(character.Effects.TryGet(CastleItems.StatusBase + 2, out var running) && running.DurationMs == 30_000f, "Deadly Edge lasts 30 s");
        var twice = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC102")));
        await harness.AdvanceAsync(twice);
        Assert.True((await twice).Outcome.Status == ItemUseStatus.AlreadyRunning, "the same castle item ran twice");
        var evade = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC103")));
        await harness.AdvanceAsync(evade);
        harness.Tick(1);
        Assert.True((await evade).Outcome.Ok && !character.Effects.TryGet(CastleItems.StatusBase + 2, out _)
            && character.Effects.Totals.Flat(StatKind.Evasion) >= 5, "a castle scroll did not replace the running one");

        var athanasia = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC101")));
        await harness.AdvanceAsync(athanasia);
        harness.Tick(1);
        Assert.True((await athanasia).Outcome.Ok && harness.Player.GuardMsLeft > 29_000f && character.Effects.Totals.Percent(StatKind.DamageDealt) <= -50,
            "Athanasia guard and halved damage");
        var eternal = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI105")));
        await harness.AdvanceAsync(eternal);
        Assert.True((await eternal).Outcome.Status == ItemUseStatus.SameAttribute && harness.Item("BI105") is not null, "Eternal Life ran beside Athanasia");
        Assert.True(ItemStatuses.Icon(CastleItems.StatusBase + 1)!.EndsWith("S_Ath.tga") && ItemStatuses.Name(CastleItems.StatusBase + 6) == "Defiance Stone",
            "castle keep icons");
    }

    private static Task StonesAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Stones");
        Assert.True(CastleItems.TryGet("BC106", out var fire), "fire stone missing");
        Assert.True(CastleItems.TryGet("BC113", out var knight), "knight stone missing");
        character.Effects.Apply(fire.Spec(), 0, 60_000f);
        Assert.True(CastleItems.Defend(0x5020, character.Effects, 101) == 50 && CastleItems.Defend(0x5022, character.Effects, 100) == 100,
            "the fire stone halves only the fire tower");
        character.Effects.Apply(knight.Spec(), 0, 60_000f);
        Assert.True(CastleItems.Strike(character.Effects, PlayerClass.Knight, 100) == 120 && CastleItems.Strike(character.Effects, PlayerClass.Archer, 100) == 100,
            "the knight stone strengthens only strikes on knights");
        return Task.CompletedTask;
    }

    private static async Task LeaveAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, "BC109", "BC101");
        var character = harness.Session.Character!;
        var stone = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC109")));
        await harness.AdvanceAsync(stone);
        harness.Tick(1);
        Assert.True((await stone).Outcome.Ok && character.Effects.TryGet(CastleItems.StatusBase + 9, out _), "Might Stone did not start");
        var athanasia = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC101")));
        await harness.AdvanceAsync(athanasia);
        harness.Tick(1);
        Assert.True((await athanasia).Outcome.Ok && harness.Player.GuardMsLeft > PlayerGrace.ArrivalMs, "Athanasia did not guard");
        Assert.True(harness.World.TryGetOpenField(FieldCodes.Castle, out var castle), "castle field missing");
        castle.Remove(harness.Player.EntityId);
        Assert.True(!character.Effects.TryGet(CastleItems.StatusBase + 9, out _) && harness.Player.GuardMsLeft == 0f,
            "the stone or Athanasia's guard outlived leaving the castle");
    }

    private static async Task RestorationAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Castle, "BC105");
        var character = harness.Session.Character!;
        var alive = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC105")));
        await harness.AdvanceAsync(alive);
        Assert.True((await alive).Outcome.Status == ItemUseStatus.NotUsable && harness.Item("BC105") is not null, "Restoration was used alive");

        harness.Player.Kill();
        harness.Tick(20 * 5);
        var early = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC105")));
        await harness.AdvanceAsync(early);
        Assert.True((await early).Outcome.Status == ItemUseStatus.Delay && harness.Player.IsDead, "Restoration before ten seconds");
        harness.Tick(20 * 5);
        Assert.True(harness.Player.IsDead && harness.Player.DeadMs >= CastleItems.RestorationDelayMs, "the dead player respawned before his Restoration");
        var revive = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BC105")));
        await harness.AdvanceAsync(revive);
        Assert.True((await revive).Outcome.Ok && !harness.Player.IsDead && character.Life == character.MaxLife / 2 && harness.Item("BC105") is null
            && harness.Player.GuardMsLeft > 4000f, "Restoration did not revive in place at half life");

        var field = await SpecialItemTests.Harness.CreateAsync(20, "BC105");
        field.Player.Kill();
        field.Tick(20 * 11);
        var outside = field.Use(new ItemUseCommand(Guid.NewGuid(), field.Session.Character!.InventoryRevision, field.Id("BC105")));
        await field.AdvanceAsync(outside);
        Assert.True((await outside).Outcome.Status == ItemUseStatus.NotHere && field.Player.IsDead && field.Item("BC105") is not null,
            "a Restoration revived a player outside the castle");
    }
}
