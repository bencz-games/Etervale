using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Contracts.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

using PristonTale.Contracts.Social;

namespace PristonTale.Tests;

internal static class BellatraGameTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a Bellatra team booth sells a ticket at dawn into its team's room; rounds open monsters by the game clock, kills score, the best "
            + "team goes on, the game ends with the prize less the tax and the entrants return to Navisko", GameAsync);
        yield return ("a Bellatra booth refuses a closed Bellatra, a low level and the hours past dawn, and the ticket's gold is in the currency report",
            RefusalsAsync);
        yield return ("a Bellatra booth refuses a full team, a second ticket and a game still ending", BoothAsync);
        yield return ("an entrant away from the arena at round 1 loses its place and the game ends with nobody; a day without entrants opens nothing",
            AbsentAsync);
    }

    private static async Task GameAsync()
    {
        var game = await Arena.CreateAsync(BellatraMode.Always, 50);
        var opened = await WorldPump.RunAsync(game.World, game.Npc.RequestAsync(game.Session.SessionToken, game.Booth(NpcServiceAction.Open)));
        Assert.True(opened.Outcome.Ok && opened.Outcome.Message == GameText.Of(GameTextId.BellatraTicket, BellatraRules.Fee(50))
            && opened.Destinations is [{ FieldCode: BellatraArena.Field, Price: var fee, MinimumLevel: BellatraRules.MinLevel }] && fee == BellatraRules.Fee(50),
            "the booth did not offer a ticket as its one destination");
        var bought = await WorldPump.RunAsync(game.World, game.Npc.RequestAsync(game.Session.SessionToken, game.Booth(NpcServiceAction.EnterBellatra)));
        Assert.True(bought.Outcome.Ok && game.Session.FieldId == BellatraArena.Field && game.Session.Character!.Money == Arena.Gold - BellatraRules.Fee(50)
            && BellatraTests.Inside(BellatraArena.Rooms[0], game.Player.Position), "the ticket did not take the fee and enter the team's room");

        game.AtHour(6);
        game.Seconds(BellatraRounds.Delay + 3);
        Assert.True(game.World.TryGetOpenField(BellatraArena.Field, out var arena), "the arena is not open");
        var monsters = arena.MonstersForInspection.Where(static monster => monster.BellatraRoom >= 0).ToList();
        Assert.True(monsters.Count >= 4 && monsters.Select(static monster => monster.BellatraRoom).Distinct().Order().SequenceEqual([0, 1, 2, 3])
            && monsters.All(static monster => BellatraTests.Inside(BellatraArena.Rooms[monster.BellatraRoom], monster.Position)), "round 1 did not open monsters in its four rooms");
        var prey = monsters.First(static monster => monster.BellatraRoom == 0);
        arena.ScoreBellatra(prey, game.Player.EntityId);
        game.Seconds(1);
        Assert.True(game.LastBoard() is { Round: 1, Team: 0, Score: 100, Outcome: BellatraOutcome.Playing } board && board.TeamScores is [100, 0, 0, 0],
            "the entrant's board did not show the round, its score and the teams'");

        foreach (var hour in new[] { 8, 10, 12 })
        {
            game.AtHour(hour);
        }

        game.Seconds(10);
        Assert.True(game.LastBoard() is { Round: 4, Outcome: BellatraOutcome.GoesOn }, "the board did not show the team going on");
        Assert.True(BellatraTests.Inside(BellatraArena.Rooms[4], game.Player.Position) && !arena.MonstersForInspection.Any(monster => monster.BellatraRoom is >= 0 and < 4 && monster.IsAlive),
            "the best team did not go on to room 5 before round 4, or round 3's monsters still stand");
        foreach (var hour in new[] { 15, 18, 21 })
        {
            game.AtHour(hour);
        }

        game.Seconds(10);
        Assert.True(BellatraTests.Inside(BellatraArena.Rooms[6], game.Player.Position), "the best team did not go on to the top room before round 7");
        game.AtHour(0);
        Assert.True(game.World.BellatraPrizes.TryRead(out var prize) && prize.Gold == BellatraRules.Prize(100, BellatraRules.DefaultTax)
            && prize.Text == GameText.Of(GameTextId.BellatraCleared, prize.Gold), "the game did not skip the final below its need and pay the score less the tax");
        var money = game.Session.Character!.Money;
        await WorldPump.RunAsync(game.World, new BellatraAppService(game.World, game.Saves, new Application.Chat.ChatDirectory()).PayAsync(prize!, CancellationToken.None));
        Assert.True(game.Session.Character.Money == money + prize!.Gold, "the prize was not paid into the player's gold");
        game.Seconds(1);
        Assert.True(game.LastBoard() is { Round: 0, Outcome: BellatraOutcome.Cleared }, "the board did not show the game over");
        game.Seconds(BellatraRules.OutSeconds + 1);
        Assert.True(game.Session.FieldId == BellatraArena.Village && game.LastBoard() is null, "the entrants did not go back to Navisko, or kept the board");
    }

    private static async Task AbsentAsync()
    {
        var game = await Arena.CreateAsync(BellatraMode.Always, 50);
        await WorldPump.RunAsync(game.World, game.Npc.RequestAsync(game.Session.SessionToken, game.Booth(NpcServiceAction.EnterBellatra)));
        game.Player.Teleport(new Vector3(0, 1154, 0));
        game.AtHour(6);
        game.Seconds(BellatraRounds.Delay + 3);
        Assert.True(game.World.TryGetOpenField(BellatraArena.Field, out var arena) && !arena.MonstersForInspection.Any(static monster => monster.BellatraRoom >= 0)
            && !game.World.BellatraPrizes.TryRead(out _), "an entrant away from the arena kept its place, or the game went on without anyone");

        var empty = await Arena.CreateAsync(BellatraMode.Always, 50);
        empty.AtHour(6);
        empty.Seconds(BellatraRounds.Delay + 3);
        Assert.True(!empty.World.TryGetOpenField(BellatraArena.Field, out var none) || !none.MonstersForInspection.Any(static monster => monster.BellatraRoom >= 0),
            "a day without entrants opened monsters");
    }

    private static Task BoothAsync()
    {
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Booth", 50);
        var game = new BellatraGame();
        var booth = new BellatraBooth(true, BellatraRules.TicketHour, game);
        Assert.True(booth.Refusal(character, 0) == NpcServiceStatus.Ok, "an open booth refused at dawn");
        game.TeamLevels[0] = BellatraRules.TeamLevels + 1;
        Assert.True(booth.Refusal(character, 0) == NpcServiceStatus.BellatraTeamFull && booth.Refusal(character, 1) == NpcServiceStatus.Ok, "a full team still sold");
        game.Entrants[character.Name] = new BellatraGame.Entrant(1);
        Assert.True(booth.Refusal(character, 1) == NpcServiceStatus.BellatraEntered, "an entrant bought a second ticket");
        game.Entrants.Clear();
        game.OutAtMs = 1;
        Assert.True(booth.Refusal(character, 1) == NpcServiceStatus.BellatraNoTickets
            && booth.Answer(character, 1, new WorldCatalog(), out var ticket) == NpcServiceTransactions.Describe(NpcServiceStatus.BellatraNoTickets) && ticket is null,
            "a booth sold while a game ended");
        return Task.CompletedTask;
    }

    private static async Task RefusalsAsync()
    {
        var closed = await Arena.CreateAsync(BellatraMode.Off, 50);
        var refused = await WorldPump.RunAsync(closed.World, closed.Npc.RequestAsync(closed.Session.SessionToken, closed.Booth(NpcServiceAction.EnterBellatra)));
        Assert.True(refused.Outcome.Status == NpcServiceStatus.BellatraClosed && closed.Session.Character!.Money == Arena.Gold, "a closed Bellatra sold a ticket");

        var young = await Arena.CreateAsync(BellatraMode.Always, 30);
        var low = await WorldPump.RunAsync(young.World, young.Npc.RequestAsync(young.Session.SessionToken, young.Booth(NpcServiceAction.Open)));
        Assert.True(low.Outcome.Message == GameText.Of(GameTextId.BellatraLevel, BellatraRules.MinLevel) && low.Destinations is null or [],
            "a low level was offered a ticket");

        var late = await Arena.CreateAsync(BellatraMode.Always, 50);
        late.AtHour(7);
        var past = await WorldPump.RunAsync(late.World, late.Npc.RequestAsync(late.Session.SessionToken, late.Booth(NpcServiceAction.EnterBellatra)));
        Assert.True(past.Outcome.Status == NpcServiceStatus.BellatraNoTickets, "a ticket was sold outside the dawn");

        var twice = await Arena.CreateAsync(BellatraMode.Always, 50);
        await WorldPump.RunAsync(twice.World, twice.Npc.RequestAsync(twice.Session.SessionToken, twice.Booth(NpcServiceAction.EnterBellatra)));
        Assert.True(twice.World.Currency[Application.GameMasters.CurrencySink.BellatraTickets] == BellatraRules.Fee(50), "the ticket was not in the currency report");
    }

    // A level's character at a Navisko team booth (team 1) at game hour 4, with Bellatra in the given mode.
    internal sealed class Arena
    {
        public const long Gold = 1_000_000;

        private uint _tick = 100;

        public required WorldServer World { get; init; }
        public required PlayerSession Session { get; init; }
        public required WorldPlayer Player { get; init; }
        public required NpcAppService Npc { get; init; }
        public required ulong BoothId { get; init; }
        public required GameClock Clock { get; init; }
        public required ImmediateSaves Saves { get; init; }
        public required SessionRegistry Sessions { get; init; }
        public required WorldOutbox Outbox { get; init; }

        private BellatraView? _board;

        // The board of the latest snapshot the player was sent (null once it is hidden).
        public BellatraView? LastBoard()
        {
            while (Outbox.Reader.TryRead(out var snapshot))
            {
                _board = snapshot.Bellatra;
            }

            return _board;
        }

        public NpcServiceCommand Booth(NpcServiceAction action) =>
            new(Guid.NewGuid(), BoothId, NpcServiceKind.EventBooth, action, Session.Character!.InventoryRevision);

        // The clock moves forward to the start of the game hour, and the world runs a tick there.
        public void AtHour(int hour)
        {
            var dayMs = (long)WorldClock.MinuteMs * WorldClock.MinutesPerDay;
            var now = Clock.Now.ToUnixTimeMilliseconds();
            var target = now - now % dayMs + hour * 60L * WorldClock.MinuteMs + 1000;
            Clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(target <= now ? target + dayMs : target);
            Tick();
        }

        public void Seconds(int seconds)
        {
            for (var second = 0; second < seconds * 2; second++)
            {
                Clock.Now += TimeSpan.FromMilliseconds(500);
                Tick();
            }
        }

        private void Tick() => World.Tick(0.05f, _tick++);

        // booth: the Navisko booth's value (1 a team booth, BellatraRules.SecretaryBooth the secretary).
        public static async Task<Arena> CreateAsync(BellatraMode mode, int level, int booth = 1)
        {
            var classes = new CharacterClassRegistry();
            var starter = CharacterRecordFactory.NewStarter(classes, 1, "Entrant", 0, PlayerClass.Fighter, BellatraArena.Village) with { Id = 1 };
            var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name, level);
            character.RestoreProgress(ExperienceTable.ForLevel(level), Gold);
            var characters = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, BellatraArena.Village, new Vector3(100, 0, 100)));
            var npc = new NpcDefinition(1, "Kasa", "sod_01.npc", [new NpcService(NpcServiceKind.EventBooth, booth)]) { DisplayName = "Kasa", Dialog = ["Welcome."] };
            MonsterDefinition[] monsters =
            [
                .. new[] { "Mephit", "NorthGoblin", "Devilish Tree", "Bargon", "Buma" }.Select(static (name, index) =>
                    ChatFixture.Hopy with { Code = 700 + index, Key = name, Name = name, ExpReward = 3500 })
            ];
            var empty = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(empty.Items, new MonsterCatalog(monsters), new NpcCatalog([npc]),
                    new Dictionary<int, FieldSpawnData> { [BellatraArena.Village] = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 100 * 256, 100 * 256, 0)] } },
                    empty.Motions)
            };
            var clock = new GameClock();
            var catalog = new WorldCatalog();
            var world = new WorldServer(holder, catalog, clock, options: WorldServerOptions.Default with { Bellatra = BellatraOptions.Default with { Mode = mode } });
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "entrant");
            var saves = new ImmediateSaves(characters);
            var entry = new SessionAppService(sessions, classes, characters, saves, catalog, world, holder, clock);
            Assert.True((await entry.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
            var outbox = new WorldOutbox();
            var player = WorldPlayerFactory.Create(session);
            await WorldPump.RunAsync(world, world.JoinAsync(BellatraArena.Village, player, outbox.Writer, session));
            Assert.True(outbox.Reader.TryRead(out var snapshot), "roster missing");
            var arena = new Arena
            {
                World = world,
                Session = session,
                Player = player,
                Npc = new NpcAppService(sessions, saves, new MemoryWarehouses(), world, holder),
                BoothId = snapshot.Entities.Single(static entity => entity.Kind == EntityKind.Npc).EntityId,
                Clock = clock,
                Saves = saves,
                Sessions = sessions,
                Outbox = outbox
            };
            arena.AtHour(BellatraRules.TicketHour);
            return arena;
        }
    }

    internal sealed class GameClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
