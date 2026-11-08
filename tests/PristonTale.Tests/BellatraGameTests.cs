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
            + "team cheers and goes on, the live and the final results list the entrant's score and kills, the game ends with the prize less the tax, a "
            + "cheer and a potion for each monster standing, and the entrants return to Navisko", GameAsync);
        yield return ("a Bellatra booth refuses a closed Bellatra, a low level and the hours past dawn, and the ticket's gold is in the currency report",
            RefusalsAsync);
        yield return ("a Bellatra booth refuses a full team, a second ticket and a game still ending", BoothAsync);
        yield return ("Bellatra's seals: a picked surprise breaks at once in the entrant's room while a round is played (quake, stun, points), the "
            + "team is told, one picked before the round stays on the ground, and others may not take what lies in the arena", SealsAsync);
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
        arena.ScoreBellatra(prey, game.Player.EntityId, prey.Position);
        game.Seconds(1);
        Assert.True(game.LastBoard() is { Round: 1, Team: 0, Score: 100, Outcome: BellatraOutcome.Playing } board && board.TeamScores is [100, 0, 0, 0],
            "the entrant's board did not show the round, its score and the teams'");
        var live = await WorldPump.RunAsync(game.World, game.World.BellatraResultAsync(game.Player.EntityId, 1));
        var otherTeam = await WorldPump.RunAsync(game.World, game.World.BellatraResultAsync(game.Player.EntityId, 2));
        var everyTeam = await WorldPump.RunAsync(game.World, game.World.BellatraResultAsync(game.Player.EntityId, 0));
        Assert.True(live is { Final: false, Lines: [{ Name: "Entrant", Class: PlayerClass.Fighter, Score: 100, Kills: 1 }] } && otherTeam.Lines.Count == 0
            && everyTeam is { Final: false, Lines: [{ Name: "Entrant" }] }, "a team's live result did not list its entrant's score and kills, or another team's listed it");

        foreach (var hour in new[] { 8, 10, 12 })
        {
            game.AtHour(hour);
        }

        Assert.True(game.Player.HasPendingYahoo, "the team going on did not cheer");

        game.Seconds(10);
        Assert.True(game.LastBoard() is { Round: 4, Outcome: BellatraOutcome.GoesOn, StageTeams: 0b0001, GoingOn: 0b0001 },
            "the board did not show the team going on, the teams that played the stage or those going on");
        Assert.True(BellatraTests.Inside(BellatraArena.Rooms[4], game.Player.Position) && !arena.MonstersForInspection.Any(monster => monster.BellatraRoom is >= 0 and < 4 && monster.IsAlive)
            && Potions(arena) == 0, "the best team did not go on to room 5 before round 4, or round 3's monsters still stand or left potions");
        foreach (var hour in new[] { 15, 18, 21 })
        {
            game.AtHour(hour);
        }

        game.Seconds(10);
        Assert.True(BellatraTests.Inside(BellatraArena.Rooms[6], game.Player.Position), "the best team did not go on to the top room before round 7");
        var standing = 0;
        for (var second = 0; second < 60 && standing == 0; second++)
        {
            game.Seconds(1);
            standing = arena.MonstersForInspection.Count(static monster => monster.BellatraRoom >= 0 && monster.IsAlive);
        }

        game.AtHour(0);
        Assert.True(game.Player.HasPendingYahoo, "the entrant did not cheer at the end");
        Assert.True(standing > 0 && Potions(arena) == standing && !arena.MonstersForInspection.Any(static monster => monster.BellatraRoom >= 0 && monster.IsAlive),
            "the end after round 7, the final skipped, did not leave a potion for each round monster standing");
        var final = await WorldPump.RunAsync(game.World, game.World.BellatraResultAsync(game.Player.EntityId, 0));
        var booth = await WorldPump.RunAsync(game.World, game.World.BellatraResultAsync(game.Player.EntityId, 1));
        Assert.True(final is { Final: true, Lines: [{ Name: "Entrant", Score: 100, Kills: 1 }] } && booth is { Final: false },
            "the end did not keep the game's final result for its entrant, or a team booth answered it");
        Assert.True(game.World.BellatraPrizes.TryRead(out var prize) && prize.Gold == BellatraRules.Prize(100, BellatraRules.DefaultTax)
            && prize.Text == GameText.Of(GameTextId.BellatraCleared, prize.Gold), "the game did not skip the final below its need and pay the score less the tax");
        var money = game.Session.Character!.Money;
        await WorldPump.RunAsync(game.World, new BellatraAppService(game.World, game.Saves, new Application.Chat.ChatDirectory()).PayAsync(prize!, CancellationToken.None));
        Assert.True(game.Session.Character.Money == money + prize!.Gold, "the prize was not paid into the player's gold");
        game.Seconds(1);
        Assert.True(game.LastBoard() is { Round: 0, Outcome: BellatraOutcome.Cleared }, "the board did not show the game over");
        game.Seconds(BellatraRules.OutSeconds + 1);
        Assert.True(game.Session.FieldId == BellatraArena.Village && game.LastBoard() is null, "the entrants did not go back to Navisko, or kept the board");
        var reset = await WorldPump.RunAsync(game.World, game.World.BellatraResultAsync(game.Player.EntityId, 0));
        Assert.True(reset is { Final: false, Lines.Count: 0 }, "the game's reset kept its final result");
    }

    private static async Task SealsAsync()
    {
        var game = await Arena.CreateAsync(BellatraMode.Always, 50);
        await WorldPump.RunAsync(game.World, game.Npc.RequestAsync(game.Session.SessionToken, game.Booth(NpcServiceAction.EnterBellatra)));
        Assert.True(game.World.TryGetOpenField(BellatraArena.Field, out var arena), "the arena is not open");
        var early = await game.PickAsync(arena, "SD207", BellatraArena.Rooms[0].Centre);
        Assert.True(!early.Picked.Ok && early.Picked.Message == GameText.Of(GameTextId.BellatraSealIdle) && early.Lying && game.LastBoard() is not { Score: > 0 },
            "a seal broke before the first round, or its surprise left the ground");

        game.AtHour(6);
        game.Seconds(BellatraRounds.Delay + 9);
        var room = arena.MonstersForInspection.Where(static monster => monster.BellatraRoom == 0 && monster.IsAlive).ToList();
        Assert.True(room.Count > 0 && room.All(monster => BellatraArena.Rooms[0].Holds(monster.Position)), "round 1 opened no monster in the team's room");
        game.Notices();
        var quake = await game.PickAsync(arena, "SD201", BellatraArena.Rooms[0].Centre);
        var stun = await game.PickAsync(arena, "SD202", BellatraArena.Rooms[0].Centre);
        Assert.True(quake is { Picked.Ok: true, Lying: false } && stun is { Picked.Ok: true, Lying: false }
            && room.All(monster => monster.Life <= monster.Def.Combat.MaxLife - monster.Def.Combat.MaxLife / BellatraRules.QuakeShare
                && monster.Effects.Has(Simulation.Modifiers.ControlFlags.Stun)
                && arena.Statuses.Snapshot.For(monster.EntityId).Any(static status => status.Status.StatusCode == Simulation.World.Monsters.Skills.MonsterStatuses.LightningStun)),
            "the quake or the stun did not reach the room's monsters, or the stun was not shown");
        Assert.True(game.Notices() is [GameTextId.BellatraQuake, GameTextId.BellatraStun], "the team was not told the seals broken");

        var points = await game.PickAsync(arena, "SD207", BellatraArena.Rooms[0].Centre);
        Assert.True(points is { Picked.Ok: true, Lying: false } && game.LastBoard() is { Score: BellatraRules.PointsBonus }
            && !game.Session.Character!.Inventory.Items().Any(static item => item.Definition.CatalogKey.StartsWith("SD2", StringComparison.Ordinal)),
            "a picked surprise did not add its points at once, or reached the bag");

        var stranger = new WorldPlayer(999, BellatraArena.Rooms[0].Centre, new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Stranger", 50), false, default);
        Assert.True(!arena.MayTakeBellatraLoot(stranger), "a player who did not enter took what lies in the arena");
        stranger.Teleport(stranger.Position with { Y = 900 });
        Assert.True(arena.MayTakeBellatraLoot(stranger), "out of the arena a player who did not enter may not take a drop");
    }

    private static int Potions(FieldInstance arena) => arena.GroundLoot.Snapshot().Count(static drop => drop.View.ItemCode == BellatraRules.EndPotion);

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
        game.Entrants[character.Name] = new BellatraGame.Entrant(1, PlayerClass.Fighter);
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
        public required SessionAppService Service { get; init; }
        public required WorldOutbox Outbox { get; init; }

        private BellatraView? _board;
        private readonly List<GameTextId> _notices = [];

        // The board of the latest snapshot the player was sent (null once it is hidden).
        public BellatraView? LastBoard()
        {
            while (Outbox.Reader.TryRead(out var snapshot))
            {
                _board = snapshot.Bellatra;
            }

            return _board;
        }

        // The lines the ticks sent the player since the last call.
        public GameTextId[] Notices()
        {
            GameTextId[] notices = [.. _notices];
            _notices.Clear();
            return notices;
        }

        // A surprise laid near the position and picked up by the player standing on it; whether it still lies after.
        public async Task<(Simulation.Items.InventoryOutcome Picked, bool Lying)> PickAsync(FieldInstance arena, string key, Vector3 near)
        {
            var before = arena.GroundLoot.Snapshot().Select(static drop => drop.View.Id).ToHashSet();
            Assert.True(arena.GroundLoot.PlaceItem(Simulation.Items.ItemCodes.Parse(key), near, arena.NowMs, new Simulation.Combat.CombatRng((int)_tick)),
                "the surprise was not laid");
            var lying = arena.GroundLoot.Snapshot().Single(drop => !before.Contains(drop.View.Id)).View;
            Player.Teleport(lying.Position);
            Seconds(1);
            var picked = await WorldPump.RunAsync(World, Service.ApplyInventoryAsync(Session.SessionToken,
                new Simulation.Items.InventoryCommand(Guid.NewGuid(), Session.Character!.InventoryRevision, Simulation.Items.InventoryAction.Pickup, lying.Id, 0, 1, default)));
            Seconds(1);
            return (picked, arena.GroundLoot.Snapshot().Any(drop => drop.View.Id == lying.Id));
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

        private void Tick()
        {
            World.Tick(0.05f, _tick++);
            foreach (var notice in World.TakeTickNotices())
            {
                if (notice.To == Player.EntityId)
                {
                    _notices.Add(notice.Text.Id);
                }
            }
        }

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
                .. new[] { "Mephit", "NorthGoblin", "Devilish Tree", "Bargon", "Buma", "Dawlin" }.Select(static (name, index) =>
                    ChatFixture.Hopy with { Code = 700 + index, Key = name, Name = name, ExpReward = 3500 })
            ];
            var empty = WorldData.Empty;
            var items = new Simulation.Items.ItemCatalog([.. BellatraRules.Surprises.Select(static surprise => new Simulation.Items.ItemDefinition(
                    Simulation.Items.ItemCodes.Parse(surprise.Key), surprise.Key, Simulation.Items.ItemKind.Misc) { CatalogKey = surprise.Key }),
                new Simulation.Items.ItemDefinition(BellatraRules.EndPotion, "Mystic Mana Potion", Simulation.Items.ItemKind.Potion) { CatalogKey = "PM104", MaxStack = 100 }]);
            var holder = new WorldDataHolder
            {
                Current = new WorldData(items, new MonsterCatalog(monsters), new NpcCatalog([npc]),
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
                Service = entry,
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
