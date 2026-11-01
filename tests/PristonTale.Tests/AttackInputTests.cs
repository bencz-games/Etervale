using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace PristonTale.Tests;

internal static class AttackInputTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("attack gestures distinguish tap, hold, Shift, release and stale acknowledgements", GesturesAsync);
        yield return ("a queued click survives the preceding swing and release does not queue an extra blow", QueuedAsync);
        yield return ("each swing turns toward its own target, one behind the player included", SwingFacingAsync);
        yield return ("leaving a Shift-latched attack tells the server to stop chasing, and the server lets go", StopAsync);
    }

    private static Task GesturesAsync()
    {
        var input = new AttackInputController();
        var press = input.Press(2, false, false);
        Assert.True(press.Sustained && input.Release(true, false) is null, "holding did not maintain attack");
        var release = input.Release(false, false)!.Value;
        Assert.True(!release.Sustained && release.Sequence == press.Sequence, "release created a new attack gesture");
        input.Reconcile(0, 0);
        Assert.True(input.TargetId == 2, "old idle snapshot cancelled a pending click");
        input.Reconcile(press.Sequence, 0);
        Assert.True(input.TargetId == 0, "completed tap kept inventing local attacks");
        var next = input.Press(2, true, true);
        Assert.True(next.Sequence > press.Sequence && input.Release(false, false) is null, "Shift did not latch across release");
        input.Reconcile(press.Sequence, 0);
        Assert.True(input.TargetId == 2, "old completion cancelled a fresh click on the same monster");
        input.Cancel();
        input.Reconcile(next.Sequence, 2);
        Assert.True(input.TargetId == 0, "late attack state undid movement cancellation");
        return Task.CompletedTask;
    }

    private static Task QueuedAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Queued");
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        player.AcceptAttackSequence(1);
        player.SetAttackTarget(2, true);
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.SetAttackTarget(2, false); // Release of gesture 1.
        player.TickAction(100, out _);
        Assert.True(player.AttackTargetId == 0, "release queued an unwanted second attack");

        player.AcceptAttackSequence(2);
        player.SetAttackTarget(2, false);
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.AcceptAttackSequence(3);
        player.SetAttackTarget(2, false); // A fresh press during the preceding swing.
        player.TickAction(100, out _);
        Assert.True(player.AttackTargetId == 2, "end of previous swing discarded a queued click");
        Assert.True(!player.AcceptAttackSequence(2), "stale command was accepted");
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.TickAction(100, out _);
        Assert.True(player.AttackTargetId == 0, "queued tap did not end");

        var presence = new SubscriberPresence(1);
        presence.Diff([player.ToView()]);
        player.AcceptAttackSequence(4);
        var diff = presence.Diff([player.ToView()]);
        Assert.True(diff.States.Count == 1 && diff.States[0].AttackSequence == 4,
            "idle suppression dropped the acknowledgement of a rejected/finished command");
        return Task.CompletedTask;
    }

    private static async Task StopAsync()
    {
        var input = new AttackInputController();
        var latched = input.Press(2, true, false);
        Assert.True(input.Release(false, false) is null && input.TargetId == 2, "Shift did not latch the attack");
        var stop = input.Stop() ?? throw new AssertionException("leaving the latched attack did not ask the server to stop");
        Assert.True(stop is { TargetId: 0, Sustained: false } && stop.Sequence > latched.Sequence && input.TargetId == 0,
            "the stop is not an attack on no one as the next gesture");
        Assert.True(input.Stop() is null, "a second stop with no target asked again");

        var fixture = new ChatFixture();
        var gm = await fixture.JoinAsync("Latched", 0, 0, PristonTale.Application.GameMasters.GameMasterLevel.Guide);
        await fixture.SayAsync(gm, "/monster Hopy");
        fixture.Tick(1);
        var monster = ChatFixture.Latest(gm)!.Value.Entities.Single(entity => entity.Kind == PristonTale.Contracts.World.EntityKind.Monster).EntityId;
        Assert.True(await fixture.RunAsync(fixture.World.SetAttackTargetAsync(ChatFixture.Field, gm.Id, monster, true, latched.Sequence)),
            "the latched attack was refused");
        await fixture.RunAsync(fixture.World.SetAttackTargetAsync(ChatFixture.Field, gm.Id, stop.TargetId, stop.Sustained, stop.Sequence));
        Assert.True(fixture.World.FieldFor(ChatFixture.Field)!.TryGet(gm.Id, out var player) && player.AttackTargetId == 0 && !player.AttackSustained,
            "the server kept chasing the latched target after the stop");
    }

    private static Task SwingFacingAsync()
    {
        var player = new XnaVector2(100f, 100f);
        var forward = new XnaVector2(1f, 0f);
        var behind = AttackPresentation.FacingToward(player, new XnaVector2(70f, 100f), forward);
        Assert.True(XnaVector2.Distance(behind, new XnaVector2(-1f, 0f)) < 1e-5f, $"a swing at a target behind faces {behind}");
        var far = AttackPresentation.FacingToward(player, new XnaVector2(100f, 400f), forward);
        Assert.True(XnaVector2.Distance(far, new XnaVector2(0f, 1f)) < 1e-5f, $"a swing at a target past the reach faces {far}");
        var same = AttackPresentation.FacingToward(player, player, forward);
        Assert.True(same == forward, $"a target on the player's spot turned it to {same}");
        return Task.CompletedTask;
    }
}
