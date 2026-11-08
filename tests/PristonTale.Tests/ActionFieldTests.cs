using PristonTale.Contracts.Motion;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Monsters.Skills;

namespace PristonTale.Tests;

internal static class ActionFieldTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("Space with nothing ahead in an action field swings at nothing the way the player faces, keeping the monster its summons fight; "
            + "never while busy, stunned, holding an attack or dead, nor in another field; its request crosses the wire", SwingAtNothingAsync);
    }

    private static async Task SwingAtNothingAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(FieldCodes.FallGame, [], []);
        var player = harness.Player;
        var facing = player.Facing;
        player.CombatTargetId = 42;
        Assert.True(await Swing(harness), "Space with nothing ahead did not swing");
        Assert.True(player.MoveState == MotionState.Attack && player.Facing == facing && player.CombatTargetId == 42,
            "the swing at nothing turned the player or dropped the monster its summons fight");
        Assert.True(!await Swing(harness), "a second swing started over the first");
        harness.Tick(60);
        Assert.True(player.MoveState != MotionState.Attack && !player.IsBusy, "the swing at nothing did not end");

        player.Character.Effects.Apply(MonsterStatuses.StunSpec(MonsterStatuses.Stun), 1, 5_000);
        Assert.True(!await Swing(harness), "a stunned player swung");
        harness.Tick(120);
        player.SetAttackTarget(77, sustained: false);
        Assert.True(!await Swing(harness), "a player holding an attack swung at nothing");
        player.SetAttackTarget(0, sustained: false);
        Assert.True(await Swing(harness), "the swing did not come back once the stun and the attack were over");

        var town = await SpecialItemTests.Harness.CreateAsync(FieldCodes.Ricarten, [], []);
        var elsewhere = town.World.SwingAtNothingAsync(FieldCodes.Ricarten, town.Player.EntityId);
        await town.AdvanceAsync(elsewhere);
        Assert.True(!await elsewhere && town.Player.MoveState != MotionState.Attack, "a swing at nothing started outside an action field");

        harness.Tick(60);
        player.ApplyDamage(int.MaxValue / 2);
        Assert.True(player.IsDead && !await Swing(harness), "a dead player swung");

        var buffer = new byte[SwingRequestPacket.Size];
        Assert.True(new SwingRequestPacket().Write(buffer) == SwingRequestPacket.Size, "the swing request's size differs");
        SwingRequestPacket.Read(buffer);
    }

    private static async Task<bool> Swing(SpecialItemTests.Harness harness)
    {
        var swing = harness.World.SwingAtNothingAsync(FieldCodes.FallGame, harness.Player.EntityId);
        await harness.AdvanceAsync(swing);
        return await swing;
    }
}
