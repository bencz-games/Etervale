using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Movement between real clients over TCP: what one player does reaches exactly the players that see it.
internal static class MultiplayerMovementTests
{
    // Fixed-point positions (1/256 unit) plus the server's floor: what a viewer sees of a report.
    private const float PositionTolerance = 0.05f;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("multiplayer: a player's walk, run, facing and stop reach every viewer at the server's position", WalkRunStopAsync);
        yield return ("multiplayer: viewers outside the view range or in other fields never hear of a player; crossing the range spawns and removes it",
            InterestAsync);
        yield return ("multiplayer: a gate field change removes the player from the old field's viewers and spawns it for the new field's", GateAsync);
        yield return ("multiplayer: a warp gate moves the player to its exit for the destination's viewers", WarpAsync);
        yield return ("multiplayer: a rejected report corrects only the mover and viewers keep the authoritative position", CorrectionAsync);
    }

    private static async Task WalkRunStopAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Mover", camp);
        await using var b = await Enter(server, "WatcherB", camp + new Vector3(150f, 0f, 0f));
        await using var c = await Enter(server, "WatcherC", camp + new Vector3(-150f, 0f, 80f));

        foreach (var viewer in new[] { b, c })
        {
            var seen = await viewer.WaitAsync(world => world.Entities.GetValueOrDefault(a.EntityId), "the mover spawn");
            Assert.True(seen.Kind == EntityKind.Player && seen.Player?.Name == "Mover" && seen.Level == 10, $"{viewer.Name} spawned the mover wrongly");
            Assert.True(Vector3.Distance(Planar(seen.Position), Planar(camp)) < PositionTolerance, $"{viewer.Name} spawned the mover away from its position");
        }

        var north = camp + new Vector3(0f, 0f, 240f);
        await a.WalkAsync(camp, north);
        foreach (var viewer in new[] { b, c })
        {
            await viewer.WaitUntilAsync(world => world.Entities.TryGetValue(a.EntityId, out var entity) && entity.Motion == MotionState.Walk
                && Vector3.Distance(Planar(entity.Position), Planar(north)) < PositionTolerance, "the mover walking to the north point");
            var entity = viewer.World.Read(world => world.Entities[a.EntityId])!;
            Assert.Near(0f, AngleDelta(entity.Facing, 0f), 0.01f);
        }

        var east = north + new Vector3(240f, 0f, 0f);
        await a.WalkAsync(north, east, running: true);
        await b.WaitUntilAsync(world => world.Entities.TryGetValue(a.EntityId, out var entity) && entity.Motion == MotionState.Run
            && Vector3.Distance(Planar(entity.Position), Planar(east)) < PositionTolerance, "the mover running east");
        Assert.Near(0f, AngleDelta(b.World.Read(world => world.Entities[a.EntityId])!.Facing, MathF.PI / 2f), 0.01f);

        await a.StopAsync(east, MathF.PI);
        foreach (var viewer in new[] { b, c })
        {
            await viewer.WaitUntilAsync(world => world.Entities.TryGetValue(a.EntityId, out var entity) && entity.Motion == MotionState.Stand
                && MathF.Abs(AngleDelta(entity.Facing, MathF.PI)) < 0.01f, "the mover stopping and turning");
        }

        var authoritative = await server.World.PositionOfAsync(a.EntityId);
        Assert.True(authoritative is { } position && Vector3.Distance(Planar(position), Planar(east)) < PositionTolerance, "the server holds another position");
        Assert.True(a.World.Read(world => world.Warps.All(warp => !warp.Forced)), "a legal walk was corrected");
        // Viewers see each other too: every pair is symmetric.
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(c.EntityId), "the other watcher");
        await a.WaitUntilAsync(world => world.Entities.ContainsKey(b.EntityId) && world.Entities.ContainsKey(c.EntityId), "both watchers");
    }

    private static async Task InterestAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Local", camp);
        // Just beyond the forget range (1.15 x the connect range of about 1086 units).
        var outside = camp + new Vector3(1270f, 0f, 0f);
        await using var far = await Enter(server, "Faraway", outside);
        await using var town = await Enter(server, "Townsman", camp, MultiplayerServer.Village);

        await a.WalkAsync(camp, camp + new Vector3(0f, 0f, 120f));
        await Task.Delay(500);
        Assert.True(!far.World.Read(world => world.Mentions(a.EntityId)), "a viewer beyond the view range heard of the player");
        Assert.True(!town.World.Read(world => world.Mentions(a.EntityId)), "a viewer in another field heard of the player");
        Assert.True(!a.World.Read(world => world.Mentions(far.EntityId) || world.Mentions(town.EntityId)), "the player heard of viewers it cannot see");

        var inside = camp + new Vector3(1050f, 0f, 0f);
        await far.WalkAsync(outside, inside, running: true);
        await a.WaitUntilAsync(world => world.Entities.ContainsKey(far.EntityId), "the approaching player");
        await far.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the player it approached");

        var gone = camp + new Vector3(1300f, 0f, 0f);
        await far.WalkAsync(inside, gone, running: true);
        await a.WaitUntilAsync(world => world.Removed.Contains(far.EntityId) && !world.Entities.ContainsKey(far.EntityId), "the leaving player removed");
        await far.WaitUntilAsync(world => world.Removed.Contains(a.EntityId), "the player it left removed");
        Assert.True(!town.World.Read(world => world.Mentions(a.EntityId) || world.Mentions(far.EntityId)), "the other field heard of the hunters");
    }

    private static async Task GateAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Traveller", camp);
        await using var forest = await Enter(server, "Forester", camp + new Vector3(100f, 0f, 0f));
        await using var town = await Enter(server, "Villager", camp + new Vector3(-100f, 0f, 0f), MultiplayerServer.Village);
        await forest.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the traveller before the gate");
        Assert.True(!town.World.Read(world => world.Mentions(a.EntityId)), "the town saw a forest player");

        await a.MoveAsync(camp, 0f, field: MultiplayerServer.Village);
        await a.WaitUntilAsync(world => world.FieldId == MultiplayerServer.Village && world.Entities.ContainsKey(town.EntityId)
            && !world.Entities.ContainsKey(forest.EntityId), "the town and not the forest after the gate");
        await forest.WaitUntilAsync(world => world.Removed.Contains(a.EntityId), "the traveller removed from the forest");
        var arrived = await town.WaitAsync(world => world.Entities.GetValueOrDefault(a.EntityId), "the traveller in town");
        Assert.True(Vector3.Distance(Planar(arrived.Position), Planar(camp)) < PositionTolerance, "a gate moved the traveller");
        Assert.True(arrived.Player?.InVillage == true, "the town shows the traveller outside the village");
    }

    private static async Task WarpAsync()
    {
        const int cave = 24;
        const int forest = 0;
        var gate = new Vector3(118869f, 0f, 26017f);
        var exit = new Vector3(-16490f, 0f, -6930f);
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        // The cave opens at level 55: a character below it would log in at its home town.
        await using var a = await Enter(server, "Warper", gate, cave, level: 55);
        await using var caveViewer = await Enter(server, "CaveMate", gate + new Vector3(120f, 0f, 0f), cave, level: 55);
        await using var exitViewer = await Enter(server, "ExitMate", exit + new Vector3(120f, 0f, 0f), forest);
        await caveViewer.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the warper at the gate");

        await a.MoveAsync(gate, 0f, field: forest);
        var warp = await a.WaitAsync(world => world.Warps.LastOrDefault(value => value.FieldId == forest), "the warp confirmation");
        Assert.True(Vector3.Distance(Planar(warp.Position), Planar(exit)) < 1f, $"the warp landed at {warp.Position}, not the exit");
        await caveViewer.WaitUntilAsync(world => world.Removed.Contains(a.EntityId), "the warper removed from the cave");
        var arrived = await exitViewer.WaitAsync(world => world.Entities.GetValueOrDefault(a.EntityId), "the warper at the exit");
        Assert.True(Vector3.Distance(Planar(arrived.Position), Planar(exit)) < PositionTolerance, "the exit viewer saw the warper elsewhere");
    }

    private static async Task CorrectionAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Cheater", camp);
        await using var b = await Enter(server, "Witness", camp + new Vector3(150f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the mover");

        var teleport = camp + new Vector3(0f, 0f, 900f);
        await a.MoveAsync(teleport, 0f, running: true);
        var correction = await a.WaitAsync(world => world.Warps.LastOrDefault(warp => warp.Forced), "the correction");
        Assert.True(Vector3.Distance(Planar(correction.Position), Planar(camp)) < PositionTolerance, "the correction did not restore the server position");
        await Task.Delay(400);
        Assert.True(b.World.Read(world => world.Entities[a.EntityId].Samples.All(sample => Vector3.Distance(Planar(sample.Position), Planar(camp)) < 1f)),
            "a viewer saw the rejected position");
        Assert.True(!b.World.Read(world => world.Warps.Any(warp => warp.Forced)), "a viewer received the mover's correction");

        // After the correction the mover continues legally from the authoritative position.
        var legal = camp + new Vector3(0f, 0f, 96f);
        await a.WalkAsync(camp, legal);
        await b.WaitUntilAsync(world => Vector3.Distance(Planar(world.Entities[a.EntityId].Position), Planar(legal)) < PositionTolerance,
            "the legal move after the correction");
    }

    public static async Task<MultiplayerClient> Enter(MultiplayerServer server, string name, Vector3 position, int field = MultiplayerServer.HuntingField,
        PlayerClass playerClass = PlayerClass.Fighter, int level = 10, Func<PlayerSetup, PlayerSetup>? customize = null)
    {
        var setup = new PlayerSetup(name, playerClass, level, field, position);
        var account = await server.CreatePlayerAsync(customize?.Invoke(setup) ?? setup);
        return await MultiplayerClient.EnterAsync(server, account, name);
    }

    public static Vector3 Planar(Vector3 value) => new(value.X, 0f, value.Z);

    public static float AngleDelta(float actual, float expected)
    {
        var delta = (actual - expected) % (2f * MathF.PI);
        if (delta > MathF.PI)
        {
            delta -= 2f * MathF.PI;
        }
        else if (delta < -MathF.PI)
        {
            delta += 2f * MathF.PI;
        }

        return delta;
    }
}
