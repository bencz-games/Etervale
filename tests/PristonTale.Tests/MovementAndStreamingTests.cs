using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class MovementAndStreamingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("client-reported movement at 5 Hz is accepted and stops at the reported point", ArrivalAsync);
        yield return ("reports beyond run speed or during busy actions keep the authoritative position", BlockedArrivalAsync);
        yield return ("a game master may dash at the original's debug speed, a player may not", GameMasterDashAsync);
        yield return ("streaming follows the movement destination without loading a distant adjacent field while idle", PrefetchAsync);
        yield return ("NPC approach stops locally before authority confirms interaction range", NpcApproachAsync);
        yield return ("continuous gates keep reported running while warps reject stale positions", GateMovementAsync);
    }

    private static Task ArrivalAsync()
    {
        foreach (var offset in new[] { new Vector2(1, 0), new Vector2(5, -5), new Vector2(-11, 0), new Vector2(300, 170) })
        {
            foreach (var running in new[] { false, true })
            {
                var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Arrival");
                var start = new Vector3(100, 0, 100);
                var target = new Vector2(start.X, start.Z) + offset;
                var player = new WorldPlayer(1, start, character, true, default);
                var predicted = new Vector2(start.X, start.Z);
                var speed = running
                    ? Locomotion.RunUnitsPerSecond(character.Derived.MoveSpeed)
                    : Locomotion.WalkUnitsPerSecond(character.Derived.MoveSpeed, true);
                for (var frame = 1; frame <= 600; frame++)
                {
                    var step = DestinationMovement.Advance(predicted, target, speed, 1f / 60f);
                    predicted = step.Position;
                    if (frame % 12 == 0 || step.Arrived)
                    {
                        var verdict = player.ReportMovement(new Vector3(predicted.X, 0, predicted.Y), 0f, !step.Arrived, running, 0, frame * 1000 / 60);
                        Assert.True(verdict == MovementVerdict.Accepted, "a report within run speed was rejected");
                    }

                    if (step.Arrived)
                    {
                        break;
                    }
                }

                Assert.True(predicted == target, "prediction stopped before the clicked point");
                Assert.True(new Vector2(player.Position.X, player.Position.Z) == target, "authority stopped at another point");
                Assert.True(player.MoveState == MotionState.Stand, "arrival did not publish idle");
            }
        }

        var unchanged = DestinationMovement.Advance(Vector2.One, new Vector2(20), 100, float.NaN);
        Assert.True(unchanged.Position == Vector2.One && !unchanged.Arrived, "invalid delta time moved prediction");
        Assert.True(DestinationMovement.WithinDistance(Vector2.Zero, new Vector2(23, 0), 24)
            && !DestinationMovement.WithinDistance(Vector2.Zero, new Vector2(25, 0), 24), "cursor body-radius stop is incorrect");
        return Task.CompletedTask;
    }

    private static Task GameMasterDashAsync()
    {
        var registry = new CharacterClassRegistry();
        var dash = Locomotion.GameMasterDashUnitsPerSecond;
        Assert.True(dash > Locomotion.RunUnitsPerSecond(9) * 4, $"dash speed {dash} is not far above running");
        var gm = new WorldPlayer(1, new Vector3(100, 0, 100), registry.CreatePlayer(PlayerClass.Fighter, "Gm"), true, default) { GameMasterLevel = 1 };
        var player = new WorldPlayer(2, new Vector3(100, 0, 100), registry.CreatePlayer(PlayerClass.Fighter, "Runner"), true, default);
        Assert.True(gm.ReportMovement(new Vector3(100, 0, 100), 0f, false, false, 0, 0) == MovementVerdict.Accepted
            && player.ReportMovement(new Vector3(100, 0, 100), 0f, false, false, 0, 0) == MovementVerdict.Accepted, "first reports rejected");
        // Half a second of dashing.
        var far = new Vector3(100 + dash * 0.5f, 0, 100);
        Assert.True(gm.ReportMovement(far, 0f, true, true, 0, 500) == MovementVerdict.Accepted, "a game master's dash was rejected");
        Assert.True(player.ReportMovement(far, 0f, true, true, 0, 500) == MovementVerdict.Rejected, "a player dashed");
        return Task.CompletedTask;
    }

    private static Task BlockedArrivalAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Blocked");
        var player = new WorldPlayer(1, new Vector3(100, 0, 100), character, true, default);
        Assert.True(player.ReportMovement(new Vector3(140, 0, 100), 0f, true, true, 0, 100) == MovementVerdict.Accepted, "short report rejected");
        Assert.True(player.ReportMovement(new Vector3(2000, 0, 100), 0f, true, true, 0, 300) == MovementVerdict.Rejected
            && player.Position == new Vector3(140, 0, 100), "a teleporting report moved the avatar");
        player.BeginSwing(2, new Vector3(150, 0, 100), 500, 250);
        Assert.True(player.ReportMovement(new Vector3(150, 0, 100), 0f, true, false, 2, 1300) == MovementVerdict.Ignored
            && player.Position == new Vector3(140, 0, 100) && player.MoveState == MotionState.Attack, "a swing accepted locomotion");
        Assert.True(player.ReportMovement(new Vector3(800, 0, 100), 0f, true, false, 2, 1400) == MovementVerdict.Rejected,
            "large drift during a swing was not corrected");
        player.ExpireStaleMotion(10_000);
        Assert.True(player.MoveState == MotionState.Attack, "stale-motion expiry overrode a busy action");
        return Task.CompletedTask;
    }

    private static Task PrefetchAsync()
    {
        Assert.True(FieldStreamingPolicy.MaximumPendingFields == 2,
            "streaming must reserve one priority destination and one neighbour");
        Assert.True(FieldStreamingPolicy.MaximumUploadSteps == 32 && FieldStreamingPolicy.UploadBudgetMilliseconds == 2d,
            "teleport streaming must not raise the per-frame GPU upload budget");
        var forest = Forest1Field.Definition;
        var spawn = forest.StartPoints[0];
        var gate = forest.Gates.Single(value => value.DestinationFieldId == "village-2");
        var player = new Vector2(spawn.X, spawn.Z);
        var portal = new Vector2(gate.X, gate.Z);
        var distanceSquared = Vector2.DistanceSquared(player, portal);
        Assert.True(distanceSquared > FieldStreamingPolicy.IdlePrefetchDistance * FieldStreamingPolicy.IdlePrefetchDistance,
            "Forest 1 fixture no longer distinguishes idle and approach prefetch");
        Assert.True(!FieldStreamingPolicy.ShouldPrefetch(player, null, portal, 0f),
            "stationary Forest 1 spawn loaded Ricarten");
        var walking = FieldStreamingPolicy.PrefetchDistance(100f);
        var sprinting = FieldStreamingPolicy.PrefetchDistance(300f);
        var towardPortal = Vector2.Lerp(player, portal, 0.5f);
        var awayFromPortal = player - (portal - player);
        Assert.True(FieldStreamingPolicy.ShouldPrefetch(player, towardPortal, portal, 100f),
            "movement toward Ricarten did not start prefetch");
        Assert.True(!FieldStreamingPolicy.ShouldPrefetch(player, awayFromPortal, portal, 300f),
            "movement away from Ricarten loaded it");
        Assert.True(walking > 1400f && sprinting > walking, "prefetch does not account for approach speed");
        Assert.True(FieldStreamingPolicy.RetentionDistance(300f) > sprinting, "streaming hysteresis is missing");
        Assert.True(FieldStreamingPolicy.PrefetchDistance(float.NaN) == 1400f, "invalid speed contaminated streaming radii");
        Assert.True(FieldStreamingPolicy.PrefetchDistance(float.MaxValue) == 4500f, "prefetch radius is unbounded");
        return Task.CompletedTask;
    }

    private static Task NpcApproachAsync()
    {
        var target = new Microsoft.Xna.Framework.Vector2(100, 0);
        var local = new Microsoft.Xna.Framework.Vector2(45, 0);
        var server = new Microsoft.Xna.Framework.Vector2(0, 0);
        var first = NpcApproach.Evaluate(local, server, target, false);
        Assert.True(first.StopLocally && first.SendStop && !first.OpenDialog, "near NPC did not stop prediction while authority caught up");

        var waiting = NpcApproach.Evaluate(local, server, target, true);
        Assert.True(waiting.StopLocally && !waiting.SendStop && !waiting.OpenDialog, "NPC wait resent movement every frame");

        var confirmed = NpcApproach.Evaluate(local, new Microsoft.Xna.Framework.Vector2(20, 0), target, true);
        Assert.True(confirmed.StopLocally && confirmed.OpenDialog, "authority-confirmed NPC range did not open interaction");

        var far = NpcApproach.Evaluate(Microsoft.Xna.Framework.Vector2.Zero, Microsoft.Xna.Framework.Vector2.Zero, target, false);
        Assert.True(far.BeginApproach && !far.StopLocally && far.Destination.X > 0 && far.Destination.X < target.X,
            "distant NPC did not produce a bounded approach destination");
        return Task.CompletedTask;
    }

    private static Task GateMovementAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "GateRunner");
        var player = new WorldPlayer(1, new Vector3(100, 0, 100), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(150, 0, 100), 0f, true, true, 0, 100) == MovementVerdict.Accepted, "run report rejected");
        player.CrossGate(true);
        Assert.True(player.MoveState == MotionState.Run && player.Position == new Vector3(150, 0, 100), "crossing a gate stopped the run");
        Assert.True(player.ReportMovement(new Vector3(200, 0, 100), 0f, true, true, 0, 500) == MovementVerdict.Accepted, "run after a gate rejected");
        player.ExpireStaleMotion(2100);
        Assert.True(player.MoveState == MotionState.Stand, "a silent client kept running");
        var warp = new Vector3(9000, 0, 8000);
        player.EnterField(warp, true);
        Assert.True(player.ReportMovement(new Vector3(250, 0, 100), 0f, true, true, 0, 2200) == MovementVerdict.Rejected && player.Position == warp,
            "warp accepted a position from the old field");
        return Task.CompletedTask;
    }
}
