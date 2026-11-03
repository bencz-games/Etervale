using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Client.Net.Presence;
using static PristonTale.Tests.MultiplayerMovementTests;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Session lifecycle seen by other players: leaving, losing the connection, coming back, and two logins racing for one
// character.
internal static class MultiplayerSessionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("multiplayer: leaving the world removes the player for its viewers and a reconnect shows it again where it left", LeaveAsync);
        yield return ("multiplayer: a dropped connection removes the player and frees its character for the next login", DropAsync);
        yield return ("multiplayer: two logins racing for one character admit exactly one and viewers see one avatar", RaceAsync);
        yield return ("multiplayer: a login whose character list storage refuses answers busy, and the connection logs in again", ListRefusedAsync);
        yield return ("multiplayer: a client speaking an older protocol is refused as outdated before it plays", OutdatedAsync);
        yield return ("multiplayer: a new character starts in its class's town, Ricarten for Tempskron, Pillai for Morion", NewCharacterTownAsync);
    }

    private static async Task LeaveAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Returner", PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        var a = await MultiplayerClient.EnterAsync(server, account, "Returner");
        await using var b = await Enter(server, "Stayer", camp + new Vector3(150f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the returner");
        var moved = camp + new Vector3(0f, 0f, 96f);
        await a.WalkAsync(camp, moved);
        await b.WaitUntilAsync(world => Vector3.Distance(Planar(world.Entities[a.EntityId].Position), Planar(moved)) < 0.05f, "the returner's walk");

        var first = a.EntityId;
        await a.LeaveAsync();
        await b.WaitUntilAsync(world => world.Removed.Contains(first) && !world.Entities.ContainsKey(first), "the returner leaving");

        await using var again = await MultiplayerClient.EnterAsync(server, account, "Returner");
        var back = await b.WaitAsync(world => world.Entities.GetValueOrDefault(again.EntityId), "the returner back");
        Assert.True(back.Player?.Name == "Returner" && Vector3.Distance(Planar(back.Position), Planar(moved)) < 1f, "the returner came back elsewhere");
        Assert.True(b.World.Read(world => world.Entities.Values.Count(entity => entity.Player?.Name == "Returner")) == 1, "the viewer shows two returners");
    }

    private static async Task DropAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Flaky", PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        var a = await MultiplayerClient.EnterAsync(server, account, "Flaky");
        await using var b = await Enter(server, "Patient", camp + new Vector3(150f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the flaky player");

        await a.DropAsync();
        await b.WaitUntilAsync(world => world.Removed.Contains(a.EntityId), "the dropped player removed");
        // The character is claimed until the server has saved it; the next login may have to retry briefly.
        await using var again = await MultiplayerClient.LoginAsync(server, account, "Flaky");
        EnterWorldOutcome? entered = null;
        for (var attempt = 0; attempt < 50 && entered is not { Ok: true }; attempt++)
        {
            entered = await again.TryEnterAsync();
            if (!entered.Ok)
            {
                Assert.True(entered.Failure == EnterWorldFailure.AlreadyActive, $"re-entry failed with {entered.Failure}");
                await Task.Delay(100);
            }
        }

        Assert.True(entered is { Ok: true }, "the character stayed claimed after the connection dropped");
        again.Adopt(entered!);
        await b.WaitUntilAsync(world => world.Entities.TryGetValue(again.EntityId, out var value) && value.Player?.Name == "Flaky", "the player back");
    }

    private static async Task RaceAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Contested", PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        await using var b = await Enter(server, "Referee", camp + new Vector3(150f, 0f, 0f));
        await using var first = await MultiplayerClient.LoginAsync(server, account, "Contested");
        await using var second = await MultiplayerClient.LoginAsync(server, account, "Contested");
        var outcomes = await Task.WhenAll(first.TryEnterAsync(), second.TryEnterAsync());
        Assert.True(outcomes.Count(outcome => outcome.Ok) == 1, $"{outcomes.Count(outcome => outcome.Ok)} sessions entered one character");
        Assert.True(outcomes.Single(outcome => !outcome.Ok).Failure == EnterWorldFailure.AlreadyActive, "the loser was refused for another reason");
        var winner = outcomes[0].Ok ? first : second;
        winner.Adopt(outcomes.Single(outcome => outcome.Ok));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(winner.EntityId), "the winner");
        await Task.Delay(300);
        Assert.True(b.World.Read(world => world.Entities.Values.Count(entity => entity.Player?.Name == "Contested")) == 1, "the viewer shows two avatars");
        Assert.True(b.World.Read(world => world.Entities[winner.EntityId].Kind) == EntityKind.Player, "the winner is not a player");
    }

    private static async Task ListRefusedAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var account = await server.CreatePlayerAsync(new PlayerSetup("Patient", PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, MultiplayerServer.Camp));
        await using var connection = new Client.Net.GameServerConnection(server.Address);
        await server.ExecuteAsync("ALTER TABLE characters RENAME TO characters_away");
        var refused = await connection.LoginAsync(account, MultiplayerServer.Password);
        await server.ExecuteAsync("ALTER TABLE characters_away RENAME TO characters");
        var again = await connection.LoginAsync(account, MultiplayerServer.Password);
        Assert.True(refused is { Ok: false, Failure: LoginFailure.Busy } && again.Ok
            && (await connection.ListCharactersAsync()).Any(static character => character.Name == "Patient"),
            $"a refused character list left the connection logged in: {refused.Failure}, then {again.Failure}");
    }

    private static async Task OutdatedAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var account = await server.CreatePlayerAsync(new PlayerSetup("Elder", PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, MultiplayerServer.Camp));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await Protocol.Transport.TlsTransport.ConnectAsync("127.0.0.1", new Uri(server.Address).Port,
            new Protocol.Transport.ServerCertificatePolicy("127.0.0.1"), timeout.Token);
        var answer = new TaskCompletionSource<LoginFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = connection.RunAsync(packet =>
        {
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet.Span[4..]) == Protocol.Binary.EtervaleOpcodes.LoginResult)
            {
                answer.TrySetResult(Protocol.Packets.LoginResultPacket.Read(packet.Span).Status);
            }

            return ValueTask.CompletedTask;
        }, timeout.Token);
        var buffer = new byte[Protocol.Packets.LoginPacket.MaxSize];
        connection.Send(buffer.AsSpan(0, new Protocol.Packets.LoginPacket(Protocol.Packets.ProtocolVersion.Minimum - 1, account, MultiplayerServer.Password).Write(buffer)));
        Assert.True(await answer.Task.WaitAsync(timeout.Token) == LoginFailure.OutdatedClient, "a client of an older protocol was let in");
        connection.Close("test done");
        await run;
    }

    private static async Task NewCharacterTownAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        foreach (var (playerClass, town) in new[] { (PlayerClass.Fighter, Simulation.World.FieldCodes.Ricarten), (PlayerClass.Knight, Simulation.World.FieldCodes.Pillai) })
        {
            var name = "New" + playerClass;
            var account = await server.CreateAccountAsync(name);
            await using var connection = new Client.Net.GameServerConnection(server.Address);
            Assert.True((await connection.LoginAsync(account, MultiplayerServer.Password)).Ok, "the new player could not log in");
            Assert.True((await connection.CreateCharacterAsync(name, playerClass, 0)).Ok, $"a {playerClass} could not be created");
            var entered = await connection.EnterWorldAsync(name);
            Assert.True(entered is { Ok: true } && entered.FieldId == town, $"a new {playerClass} started in field {entered.FieldId}, not {town}");
        }
    }
}
