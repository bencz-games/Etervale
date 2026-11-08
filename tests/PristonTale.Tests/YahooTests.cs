using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class YahooTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a yahoo word in the chat cheers instead of being spoken, and Space cheers within the minute for 3% stamina", ChatAndKeyAsync);
        yield return ("a queued cheer waits for the motion in progress; the tiger capsule cheers at its eating event", QueueAsync);
        yield return ("the cheer is a one-shot that freezes locomotion, travels as its presence motion and its key packet reads back", ClientAndWireAsync);
    }

    private static async Task ChatAndKeyAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 10, 10);
        var character = ann.Session.Character!;

        Assert.True(!await fixture.RunAsync(fixture.World.YahooAsync(ann.Id, key: true)), "Space cheered before a yahoo word opened its window");
        await fixture.SayAsync(ann, "/yahoo");
        Assert.True(bob.Link.Lines.Count == 0 && ann.Link.Lines.Count == 0, "the yahoo word was spoken or answered");
        fixture.Tick(1);
        var cheering = MoveState(ann);
        Assert.True(cheering == MotionState.Yahoo, $"the chat word did not cheer: {cheering}");
        Assert.True(!await fixture.RunAsync(fixture.World.YahooAsync(ann.Id, key: true)), "Space cheered over a cheer in progress");

        fixture.Tick(60);
        Assert.True(MoveState(ann) == MotionState.Stand, "the cheer did not end with its clip");
        var stamina = character.Stamina = character.MaxStamina;
        Assert.True(await fixture.RunAsync(fixture.World.YahooAsync(ann.Id, key: true)), "Space did not cheer inside the window");
        Assert.True(character.Stamina == stamina - YahooRules.KeyStaminaCost(character.MaxStamina), "Space did not take 3% of the stamina");
        fixture.Tick(60);
        character.Stamina = YahooRules.KeyStaminaCost(character.MaxStamina);
        Assert.True(!await fixture.RunAsync(fixture.World.YahooAsync(ann.Id, key: true)), "Space cheered with only the cost left");

        await fixture.SayAsync(bob, "Yes!");
        fixture.Tick(1);
        Assert.True(MoveState(bob) == MotionState.Yahoo && ann.Link.Lines.Count == 0, "\"Yes!\" did not cheer silently");
        await fixture.SayAsync(bob, "yahoo there");
        Assert.True(ann.Link.Last == "Bob: yahoo there", "a line that only starts with the word was not spoken");
        Assert.True(YahooRules.IsWord(" YAHOO ") && !YahooRules.IsWord("yahooo") && !YahooRules.IsWord("/yahoo now"), "word matching");

        var input = new HudChatInput();
        input.Set("yahoo");
        Assert.True(input.Submit("Ann", HudChatTab.All, 0, out _) is { Kind: HudChatSendKind.Command }, "the chat input spoke the yahoo word");
    }

    private static Task QueueAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Cheer");
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        player.BeginSwing(2, Vector3.One, 100, 50);
        player.QueueYahoo(heals: true);
        Assert.True(!player.BeginPendingYahoo(800), "a firecracker cheer cut the swing");
        player.TickAction(100, out _);
        Assert.True(player.BeginPendingYahoo(800) && player.MoveState == MotionState.Yahoo, "the queued cheer did not start after the swing");
        Assert.True(player.ReportMovement(new Vector3(50, 0, 0), 0f, true, true, 0, 1000) != MovementVerdict.Accepted && player.Position == Vector3.Zero,
            "movement interrupted the cheer");
        player.TickAction(800, out _);
        Assert.True(!player.IsBusy && player.MoveState == MotionState.Stand, "the cheer did not end");

        var random = new CombatRng(1);
        player.QueuePotion(new PotionDefinition(0, 0, 0, 0, 0, 0), cheers: true);
        Assert.True(player.BeginPendingPotion(600, 300), "the capsule meal did not start");
        player.TickPotion(299, random);
        Assert.True(player.MoveState == MotionState.Eat && !player.HasPendingYahoo, "the capsule cheered before its event");
        player.TickPotion(1, random);
        Assert.True(!player.IsBusy && !player.HasPendingPotion && player.HasPendingYahoo, "the capsule did not end its meal at the event and queue a cheer");
        Assert.True(player.BeginPendingYahoo(800), "the capsule cheer did not start");

        player.TickAction(800, out _);
        player.QueueYahoo(heals: true);
        player.ApplyDamage(100000);
        Assert.True(player.IsDead && !player.HasPendingYahoo && !player.AskYahoo(false, 0), "a dead player kept or started a cheer");
        return Task.CompletedTask;
    }

    private static Task ClientAndWireAsync()
    {
        var motion = new ActorMotionController();
        Assert.True(motion.ApplyAuthoritative(MotionCode.Yahoo).Kind == MotionCommandKind.Restart && motion.LocomotionFrozen,
            "the cheer was not a one-shot that holds the player");
        Assert.True(motion.ApplyLocomotion(MotionCode.Walk).Kind == MotionCommandKind.None, "walking cut the cheer");

        var buffer = new byte[PresenceWire.HeaderSize + PresenceWire.MaxMotionEntrySize];
        var writer = new PresenceEntriesWriter(buffer, 1, 50);
        writer.Commit(PresenceWire.Write(writer.Reserve(PresenceWire.MaxMotionEntrySize), new PresenceEntry(7, MotionState.Yahoo, default, 0)));
        var read = new List<PresenceEntry>();
        PresenceEntriesReader.Read(writer.Finish(), (in PresenceEntry value) => read.Add(value));
        Assert.True(read.Single().Motion == MotionState.Yahoo, "the cheer motion did not round-trip");

        var packet = new byte[YahooKeyPacket.Size];
        Assert.True(new YahooKeyPacket().Write(packet) == YahooKeyPacket.Size, "yahoo key size");
        YahooKeyPacket.Read(packet);
        Assert.True(BitConverter.ToUInt32(packet, 4) == EtervaleOpcodes.YahooKey, "yahoo key opcode");
        return Task.CompletedTask;
    }

    private static MotionState MoveState(ChatFixture.Player player) =>
        ChatFixture.Latest(player)!.Value.Entities.Single(entity => entity.EntityId == player.Id).MoveState;
}
