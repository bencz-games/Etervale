using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.World;

namespace PristonTale.Tests;

internal static class WingGateTests
{
    private const int Ricarten = 3;
    private static readonly Vector3 RicartenStone = new(734, 312, -20119);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("wing gates: the best carried wing sets the reach and fee; Ricarten, Pillai and the current field are free", RulesAsync);
        yield return ("every stone the wing map lists exists, and a trip lands on the exit of the destination's stone", StonesAsync);
        yield return ("a trip from a stone pays the wing's fee, is stored and lands on the destination stone; a boxed wing counts", TravelAsync);
        yield return ("a trip off the stone, beyond the wing, below the level or without the fee changes nothing", RefusalsAsync);
        yield return ("the castle's stone takes a member of the clan holding it, with the wing's fee and the tax, and refuses everyone else", CastleAsync);
        yield return ("the wing gate request and result packets round trip", PacketsAsync);
        yield return ("field and gate level limits: the deserts, the dungeons, the iron, ice and lost fields", LimitsAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(WingGates.BestWing([]) is null && WingGates.Reach(null) == 2, "without a wing the map does not reach Ricarten and Pillai");
        var silver = WingGates.BestWing([ItemCodes.Parse("QW102")]);
        var best = WingGates.BestWing([ItemCodes.Parse("QW104"), ItemCodes.Parse("QW101"), ItemCodes.Parse("QW106")]);
        Assert.True(silver is { Reach: 5, Fee: 300 } && best is { Reach: 9, Fee: 4000 }, "the wings' reach or fee differ");
        WingGates.TryGet(6, out var forbidden);
        WingGates.TryGet(1, out var pillai);
        Assert.True(WingGates.Fee(silver, forbidden, Ricarten) == 300 && WingGates.Fee(silver, pillai, Ricarten) == 0
            && WingGates.Fee(silver, forbidden, forbidden.FieldCode) == 0 && WingGates.Fee(null, pillai, Ricarten) == 0, "the fees differ");
        Assert.True(WingGates.RequiredLevel(forbidden, 0) == 55 && WingGates.RequiredLevel(pillai, 0) == 0
            && !WingGates.TryGet(WingGates.All.Count, out _) && !WingGates.TryGet(-1, out _), "the destinations' levels or bounds differ");
        Assert.True(WingGates.TryGet(WingGates.Destinations.Count, out var castle) && castle == WingGates.Castle && castle.FieldCode == FieldCodes.Castle
            && WingGates.Offers(castle, null, castleOwner: true) && !WingGates.Offers(castle, best, castleOwner: false)
            && WingGates.Offers(pillai, null, castleOwner: false) && !WingGates.Offers(forbidden, null, castleOwner: true),
            "the castle's stone is not offered to its owner clan alone, whatever the wing");
        return Task.CompletedTask;
    }

    private static Task StonesAsync()
    {
        var world = new WorldCatalog();
        foreach (var destination in WingGates.Destinations)
        {
            Assert.True(world.TryGetFieldByCode(destination.FieldCode, out var field) && field.WarpGates.Any(WingGates.IsStone),
                $"{destination.Name} has no warp stone");
        }

        var ricarten = world.GetFieldByCode(Ricarten);
        Assert.True(WingGates.AtStone(ricarten, RicartenStone) && !WingGates.AtStone(ricarten, RicartenStone + new Vector3(300, 0, 0)),
            "the Ricarten stone does not answer at its spot");
        Assert.True(WingGates.Exit(ricarten) == new Vector3(822, 254, -19956), "the Ricarten stone's exit differs");
        return Task.CompletedTask;
    }

    private static async Task<SpecialItemTests.Harness> OnStone(int level, long gold, params string[] keys)
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(Ricarten, keys);
        var character = harness.Session.Character!;
        character.RestoreProgress(0, 0, tier: 1);
        character.AwardExperience(ExperienceTable.ForLevel(level));
        Assert.True(character.TryChangeMoney(gold), "fixture gold");
        harness.Player.EnterField(RicartenStone, true);
        return harness;
    }

    private static async Task<WingGateReply> Travel(SpecialItemTests.Harness harness, int destination)
    {
        var service = new WingGateAppService(harness.Sessions, new ImmediateSaves(harness.Repository), harness.World);
        var reply = service.TravelAsync(harness.Session.SessionToken, new WingGateCommand(Guid.NewGuid(), destination));
        await harness.AdvanceAsync(reply);
        return await reply;
    }

    private static async Task TravelAsync()
    {
        var harness = await OnStone(60, 1300, "QW104");
        var character = harness.Session.Character!;
        var trip = await Travel(harness, 6);
        Assert.True(trip.Outcome.Ok && trip.Outcome.Fee == 1000 && character.Money == 300, $"the trip did not pay the dia wing's fee: {trip.Outcome}");
        var exit = WingGates.Exit(new WorldCatalog().GetFieldByCode(12));
        Assert.True(trip.Arrival is { FieldCode: 12 } && harness.Session.FieldId == 12
            && Vector2.Distance(new Vector2(harness.Player.Position.X, harness.Player.Position.Z), new Vector2(exit.X, exit.Z)) < 1f,
            "the player did not land on the Forbidden Land stone");
        Assert.True(harness.Repository.Record.FieldId == 12 && harness.Repository.Record.Money == 300, "the trip was not stored");
        Assert.True(harness.World.Currency[Application.GameMasters.CurrencySink.Teleports] == 1000, "the fee was not counted as a teleport");

        // A wing kept in the special item box sets the reach as one in the bag does.
        var boxed = await OnStone(60, 1300, "QW104");
        var flyer = boxed.Session.Character!;
        Assert.True(flyer.Inventory.Items().FirstOrDefault(item => WingGates.IsWing(item.Definition.Code)) is { } wing && flyer.TryStoreQuestItem(wing.InstanceId)
            && flyer.QuestBox.Occupied().Any(), "the fixture wing did not go to the special item box");
        Assert.True((await Travel(boxed, 6)).Outcome.Ok && boxed.Session.FieldId == 12, "a wing in the special item box did not reach the destination");

        var free = await OnStone(1, 0);
        var pillai = await Travel(free, 1);
        Assert.True(pillai.Outcome.Ok && pillai.Outcome.Fee == 0 && free.Session.FieldId == 21, $"the free trip to Pillai failed: {pillai.Outcome}");

        var home = await OnStone(60, 0, "QW106");
        var same = await Travel(home, 0);
        Assert.True(same.Outcome.Ok && same.Outcome.Fee == 0 && home.Session.FieldId == Ricarten
            && Vector2.Distance(new Vector2(home.Player.Position.X, home.Player.Position.Z), new Vector2(822, -19956)) < 1f,
            $"the trip within the field was not free or did not land on the exit: {same.Outcome}");
    }

    private static async Task RefusalsAsync()
    {
        var bare = await OnStone(60, 10_000);
        var silver = await OnStone(60, 10_000, "QW102");
        Assert.True((await Travel(bare, 2)).Outcome.Status == WingGateStatus.NotReached && bare.Session.FieldId == Ricarten
            && (await Travel(silver, 5)).Outcome.Status == WingGateStatus.NotReached, "a trip beyond the map's reach was accepted");

        var low = await OnStone(50, 10_000, "QW105");
        Assert.True((await Travel(low, 6)).Outcome.Status == WingGateStatus.LevelTooLow && low.Session.Character!.Money == 10_000,
            "a level 50 player reached the Forbidden Land");

        var poor = await OnStone(60, 999, "QW104");
        var refused = await Travel(poor, 6);
        Assert.True(refused.Outcome.Status == WingGateStatus.NotEnoughGold && poor.Session.Character!.Money == 999 && poor.Session.FieldId == Ricarten,
            $"a trip without the fee changed the character: {refused.Outcome}");

        var away = await OnStone(60, 10_000, "QW106");
        away.Player.EnterField(RicartenStone + new Vector3(600, 0, 0), true);
        Assert.True((await Travel(away, 1)).Outcome.Status == WingGateStatus.NotAtStone && away.Session.FieldId == Ricarten,
            "a trip away from the stone was accepted");
        Assert.True((await Travel(away, 12)).Outcome.Status == WingGateStatus.Rejected, "an unknown destination was accepted");
    }

    private static async Task CastleAsync()
    {
        var outsider = await OnStone(60, 10_000);
        outsider.World.SetOwner(7, Simulation.Social.Castle.CastleClanSkill.None, 0);
        Assert.True((await Travel(outsider, WingGates.Castle.Index)).Outcome.Status == WingGateStatus.NotReached && outsider.Session.FieldId == Ricarten,
            "a player outside the owner clan reached the castle's stone");

        var member = await OnStone(60, 10_000, "QW102");
        member.World.SetOwner(7, Simulation.Social.Castle.CastleClanSkill.None, 10);
        member.World.Tick(0.05f, 1);
        member.Player.Clan = new Simulation.Social.Clans.ClanTag(7, "Lions", 0, Registered: true);
        var trip = await Travel(member, WingGates.Castle.Index);
        Assert.True(trip.Outcome.Ok && trip.Outcome.Fee == 330 && member.Session.FieldId == FieldCodes.Castle && member.Session.Character!.Money == 9_670
            && member.World.TakeTaxGold() == 30, $"the owner clan's member did not reach the castle for the wing's fee and the tax, gathered: {trip.Outcome}");
    }

    private static Task PacketsAsync()
    {
        var buffer = new byte[64];
        var operation = Guid.NewGuid();
        var request = new WingGateRequestPacket(operation, 7);
        Assert.True(request.Write(buffer) == WingGateRequestPacket.Size && WingGateRequestPacket.Read(buffer.AsSpan(0, WingGateRequestPacket.Size)) == request,
            "the wing gate request does not round trip");
        var result = new WingGateResultPacket(operation, (byte)WingGateStatus.NotEnoughGold, 7, 4000);
        Assert.True(result.Write(buffer) == WingGateResultPacket.Size && WingGateResultPacket.Read(buffer.AsSpan(0, WingGateResultPacket.Size)) == result,
            "the wing gate result does not round trip");
        return Task.CompletedTask;
    }

    private static Task LimitsAsync()
    {
        var world = new WorldCatalog();
        (int Code, int Level)[] fields = [(11, 55), (27, 80), (28, 85), (29, 90), (31, 90), (34, 70), (35, 90), (38, 100), (40, 100), (41, 100), (12, 55), (36, 90)];
        foreach (var (code, level) in fields)
        {
            Assert.True(world.GetFieldByCode(code).LimitLevel == level, $"field {code} asks level {world.GetFieldByCode(code).LimitLevel}, not {level}");
        }

        var temple = world.GetFieldByCode(23).WarpGates.Where(gate => gate.LimitLevel == 80).ToArray();
        Assert.True(temple.Length == 2 && temple.All(gate => gate.DestinationFieldId == Dungeon6Field.Definition.Id),
            "the second temple's exits do not lead to the third temple at level 80");
        Assert.True(world.GetFieldByCode(13).WarpGates.Single(gate => gate.DestinationFieldId == "dun-2").LimitLevel == 50
            && world.GetFieldByCode(0).WarpGates.Any(gate => gate.LimitLevel == 55), "the dungeon and cave gate levels differ");
        return Task.CompletedTask;
    }
}
