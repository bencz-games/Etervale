using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class DomainTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("saved attributes survive entry/save and level-up grants allocation points", AttributesAsync);
        yield return ("session and character ownership are exclusive", SessionsAsync);
        yield return ("a character saved in a field its level does not open logs in at its home town", EntryLevelAsync);
        yield return ("monster ids are 64-bit and start above every player id", MonsterIdsAsync);
        yield return ("world mailbox defers mutations and protects writer ownership", MailboxAsync);
        yield return ("field transfers use catalog placement and refresh respawn/village; entering the world and a warp guard the player, a gate does not", TransfersAsync);
        yield return ("authoritative field transfer keeps a ground move in progress", RunningTransferAsync);
        yield return ("movement rejects non-finite vectors and ignores client height", MovementAsync);
        yield return ("AI integrates each time interval once", AiAsync);
        yield return ("inventory respects stack limits and atomic capacity", InventoryAsync);
    }

    private static async Task AttributesAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(
            classes,
            1,
            "Saved",
            0,
            PlayerClass.Fighter,
            3) with
        {
            Id = 1,
            Strength = 123,
            Spirit = 124,
            Talent = 125,
            Agility = 126,
            Health = 127
        };
        var repository = new MemoryCharacters(record);
        var registry = new SessionRegistry();
        var session = registry.Create(1, "test");
        var service = new SessionAppService(registry, classes, repository, new ImmediateSaves(repository), new WorldCatalog());
        Assert.True((await service.EnterWorldAsync(session.SessionToken, record.Name)).Ok, "entry failed");
        Assert.True(await service.SaveCharacterAsync(session.SessionToken), "save failed");
        Assert.True(repository.Record.Strength == 123 && repository.Record.Health == 127, "saved attributes changed");
        var character = session.Character!;
        character.LevelUp();
        Assert.True(character.Attributes.Strength == 123 && character.StatPoints == 5, "level up redistributed attributes instead of granting points");
    }

    private static async Task EntryLevelAsync()
    {
        var classes = new CharacterClassRegistry();
        foreach (var (field, expected) in new[] { (11, FieldCodes.Ricarten), (0, 0) })
        {
            var record = CharacterRecordFactory.NewStarter(classes, 1, "Wanderer", 0, PlayerClass.Fighter, 3) with { Id = 1, FieldId = field };
            var repository = new MemoryCharacters(record);
            var registry = new SessionRegistry();
            var session = registry.Create(1, "test");
            var service = new SessionAppService(registry, classes, repository, new ImmediateSaves(repository), new WorldCatalog());
            var entry = await service.EnterWorldAsync(session.SessionToken, record.Name);
            Assert.True(entry.Ok && entry.FieldId == expected, $"a level 1 character saved in field {field} entered field {entry.FieldId}");
        }
    }

    private static Task MonsterIdsAsync()
    {
        var ids = new EntityIdAllocator();
        var first = ids.NextMonsterId();
        var second = ids.NextMonsterId();
        Assert.True(first == EntityIdAllocator.MonsterIdBase && second == first + 1 && first > uint.MaxValue,
            "monster ids must start above every 32-bit player id and count up");
        var session = new SessionRegistry().Create(1, "a");
        Assert.True(session.EntityId is > 0 and < EntityIdAllocator.MonsterIdBase, $"player id {session.EntityId} is not below the monster range");
        return Task.CompletedTask;
    }

    private static async Task SessionsAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(
            classes,
            1,
            "Owned",
            0,
            PlayerClass.Fighter,
            3) with
        {
            Id = 7
        };
        var registry = new SessionRegistry();
        var repository = new MemoryCharacters(record);
        var service = new SessionAppService(registry, classes, repository, new ImmediateSaves(repository), new WorldCatalog());
        var first = registry.Create(1, "same-account");
        var second = registry.Create(1, "same-account");
        var outcomes = await Task.WhenAll(
            service.EnterWorldAsync(first.SessionToken, record.Name),
            service.EnterWorldAsync(second.SessionToken, record.Name));
        Assert.True(outcomes.Count(outcome => outcome.Ok) == 1, "two sessions acquired the same character");
        var owner = outcomes[0].Ok ? first : second;
        var other = outcomes[0].Ok ? second : first;
        var original = owner.Character;
        Assert.True(!(await service.EnterWorldAsync(owner.SessionToken, record.Name)).Ok, "repeat entry accepted");
        Assert.True(ReferenceEquals(original, owner.Character), "repeat entry replaced live character");
        Assert.True(owner.TryBeginPlay() && !owner.TryBeginPlay(), "multiple Play leases accepted");
        registry.Remove(owner.SessionToken);
        Assert.True((await service.EnterWorldAsync(other.SessionToken, record.Name)).Ok, "character lease was not released");

        var closingRegistry = new ClosingRegistry();
        var closingSession = closingRegistry.Create(1, "closing");
        var closingService = new SessionAppService(closingRegistry, classes, repository, new ImmediateSaves(repository), new WorldCatalog());
        var closedEntry = await closingService.EnterWorldAsync(closingSession.SessionToken, record.Name);
        Assert.True(!closedEntry.Ok && !closingSession.TryBeginPlay(), "closed session became ready after hydration");
        var replacement = closingRegistry.Create(1, "replacement");
        Assert.True((await closingService.EnterWorldAsync(replacement.SessionToken, record.Name)).Ok, "entry/close race leaked character ownership");
    }

    private sealed class ClosingRegistry : ISessionRegistry
    {
        private readonly SessionRegistry _inner = new();
        private int _reads;
        public PlayerSession Create(long accountId, string userId) => _inner.Create(accountId, userId);
        public bool TryGet(string token, out PlayerSession session)
        {
            var found = _inner.TryGet(token, out session);
            if (++_reads == 2)
            {
                // Close immediately after the application's final lookup, before hydration completes.
                _inner.Remove(token);
            }

            return found;
        }

        public bool TryClaimCharacter(long id, string token) => _inner.TryClaimCharacter(id, token);
        public void ReleaseCharacter(long id, string token) => _inner.ReleaseCharacter(id, token);
        public void Remove(string token) => _inner.Remove(token);
    }

    private static WorldCatalog Catalog()
    {
        var first = new FieldDef(
            "first",
            "First",
            FieldState.Village,
            new FieldBackdrop("", "", ""),
            "",
            [])
        {
            FieldCode = 3,
            StartPoints = [new FieldPoint(100, 100)],
            Gates = [new FieldGate("second", 100, 100, 0)],
            WarpGates = [new FieldWarpGate(
                100,
                100,
                0,
                48,
                0,
                "third",
                9000,
                8000,
                77,
                0)]
        };
        var second = first with
        {
            Id = "second",
            State = FieldState.Forest,
            FieldCode = 4,
            StartPoints = [new FieldPoint(5000, 6000)],
            Gates = [],
            WarpGates = []
        };
        var third = second with
        {
            Id = "third",
            FieldCode = 5,
            LimitLevel = 10
        };
        return new WorldCatalog([first, second, third]);
    }

    private static WorldPlayer Player(ulong id = 1, int level = 1)
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Probe", level);
        return new WorldPlayer(id, new Vector3(100, 0, 100), character, true, default);
    }

    private static async Task<T> Tick<T>(WorldServer world, Task<T> pending)
    {
        world.Tick(0.05f, 1);
        return await pending;
    }

    private static async Task MailboxAsync()
    {
        var world = new WorldServer(new WorldDataHolder(), Catalog());
        var player = Player();
        var first = Channel.CreateUnbounded<WorldSnapshot>();
        var second = Channel.CreateUnbounded<WorldSnapshot>();
        var join = world.JoinAsync(3, player, first.Writer);
        Assert.True(!join.IsCompleted && world.PlayerCount == 0, "caller mutated world outside tick");
        Assert.True(await Tick(world, join), "join rejected");
        Assert.True(!await Tick(world, world.JoinAsync(3, player, second.Writer)), "duplicate writer replaced owner");
        Assert.True(!(await Tick(world, world.LeaveAsync(3, player.EntityId, second.Writer))).Left, "non-owner removed player");
        Assert.True(world.PlayerCount == 1, "wrong roster after rejected cleanup");
        Assert.True((await Tick(world, world.LeaveAsync(3, player.EntityId, first.Writer))).Left, "owner could not leave");
        Assert.True(world.PlayerCount == 0, "leave not published");
    }

    private static async Task TransfersAsync()
    {
        var world = new WorldServer(new WorldDataHolder(), Catalog());
        var player = Player();
        var writer = Channel.CreateUnbounded<WorldSnapshot>().Writer;
        await Tick(world, world.JoinAsync(3, player, writer));
        Assert.True(player.GuardMsLeft > PlayerGrace.ArrivalMs - 100f, "entering the world did not guard the player");
        player.TickGuard(PlayerGrace.ArrivalMs);
        Assert.True(!await Tick(world, world.MoveToFieldAsync(3, int.MaxValue, player, Vector3.Zero, writer)), "unknown field accepted");
        Assert.True(!await Tick(world, world.MoveToFieldAsync(3, 3, player, new Vector3(9000), writer)), "same-field teleport accepted");
        Assert.True(!await Tick(world, world.MoveToFieldAsync(3, 5, player, Vector3.Zero, writer)), "level gate bypassed");
        Assert.True(await Tick(world, world.MoveToFieldAsync(3, 4, player, new Vector3(99999), writer)), "valid gate rejected");
        Assert.True(player.Position == new Vector3(100, 0, 100) && player.GuardMsLeft == 0f, "gate trusted requested placement or guarded the player");
        Assert.True(await Tick(world, world.MoveToFieldAsync(3, 4, player, new Vector3(99999), writer)) && player.Position == new Vector3(100, 0, 100),
            "a report still naming the field the player already moved to was refused or moved it again");
        Assert.True(player.ReportMovement(new Vector3(150, 0, 100), 0f, true, false, 0, 1000) == MovementVerdict.Accepted && player.Position.X == 150,
            "walk report after a gate rejected");
        player.ApplyDamage(int.MaxValue);
        player.TickDead(60_000f);
        Assert.True(player.IsDead && player.Position.X == 150, "a dead player stood up without choosing a restart");
        var high = Player(2, 10);
        var highWriter = Channel.CreateUnbounded<WorldSnapshot>().Writer;
        await Tick(world, world.JoinAsync(3, high, highWriter));
        var origin = high.Position;
        high.TickGuard(PlayerGrace.ArrivalMs);
        Assert.True(await Tick(world, world.MoveToFieldAsync(3, 5, high, Vector3.Zero, highWriter)) && high.GuardMsLeft > PlayerGrace.ArrivalMs - 100f,
            "valid warp rejected or the warp did not guard the player");
        Assert.True(high.Position == new Vector3(9000, 77, 8000), "warp did not preserve the catalog exit height");
        Assert.True(high.ReportMovement(origin, 0f, true, true, 0, 100) == MovementVerdict.Rejected, "warp accepted a pre-warp position");
    }

    private static async Task RunningTransferAsync()
    {
        var world = new WorldServer(new WorldDataHolder(), Catalog());
        var player = Player();
        var writer = Channel.CreateUnbounded<WorldSnapshot>().Writer;
        await Tick(world, world.JoinAsync(3, player, writer));
        var target = new Vector3(150, 0, 100);
        Assert.True(await Tick(world, world.ReportMovementAsync(3, player.EntityId, target, 0f, true, true, 0)) == MovementVerdict.Accepted,
            "run report rejected");
        Assert.True(await Tick(world, world.MoveToFieldAsync(3, 4, player, new Vector3(99999), writer)), "gate rejected");
        Assert.True(player.Position == target && player.MoveState == MotionState.Run, "gate stopped reported running");
        for (uint tick = 2; tick < 62; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(player.Position == target && player.MoveState == MotionState.Stand, "a silent client kept running after the gate");
    }

    private static Task MovementAsync()
    {
        foreach (var value in new[]
        {
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity,
            float.MaxValue
        }

        )
        {
            var player = Player();
            var before = player.Position;
            Assert.True(player.ReportMovement(new Vector3(value, 0, 100), 0f, true, false, 0, 100) == MovementVerdict.Rejected, "invalid report accepted");
            if (!float.IsFinite(value))
            {
                Assert.True(player.ReportMovement(before, value, true, false, 0, 100) == MovementVerdict.Rejected, "invalid facing accepted");
            }

            Assert.True(player.Position == before, "invalid input moved player");
        }

        var elevated = Player();
        var lifted = elevated.Position + new Vector3(10, 9000, 0);
        Assert.True(elevated.ReportMovement(lifted, 0f, true, false, 0, 100) == MovementVerdict.Accepted, "planar report rejected");
        Assert.Near(0, elevated.Position.Y);
        return Task.CompletedTask;
    }

    private static Task AiAsync()
    {
        var definition = new MonsterDefinition(
            1,
            "Probe",
            MonsterFaction.Enemy,
            MonsterClass.Normal,
            MonsterBrood.Normal,
            new MonsterCombatStats(
                1,
                10,
                new MonsterRange(1, 2),
                1,
                1,
                0,
                0,
                0,
                100,
                ResistanceBlock.Zero),
            "")
        {
            Ai = new MonsterAi(
                0,
                MonsterNature.Natural,
                10000,
                1,
                10000,
                MonsterRange.Zero,
                MonsterActiveHour.Any)
        };
        var prey = new SinglePrey();
        MonsterInstance Create()
        {
            return new MonsterInstance(2, definition, Vector3.Zero, 0, 0)
            {
                TargetId = 1,
                AiState = MonsterAiState.Chase,
                Motion = MotionState.Walk,
                MoveTarget = prey.Candidates[0].Position
            };
        }

        var brain = new MonsterAiBrain(new CombatRng(1));
        var tuning = new MonsterAiTuning(definition);
        var split = Create();
        var whole = Create();
        brain.Tick(split, tuning, prey, 50);
        brain.Tick(split, tuning, prey, 50);
        brain.Tick(whole, tuning, prey, 100);
        Assert.Near(whole.Position.X, split.Position.X);
        return Task.CompletedTask;
    }

    private static Task InventoryAsync()
    {
        var potion = new ItemDefinition(1, "Potion", ItemKind.Potion, MaxStack: 10);
        var inventory = new Inventory(2);
        Assert.True(inventory.TryAdd(new ItemInstance(potion, 9)), "initial add failed");
        Assert.True(inventory.TryAdd(new ItemInstance(potion, 5)), "split add failed");
        Assert.True(inventory[0].Count == 10 && inventory[1].Count == 4, "stack limit not respected");
        Assert.True(!inventory.TryAdd(new ItemInstance(potion, 7)), "overflow should be atomic failure");
        Assert.True(inventory[0].Count == 10 && inventory[1].Count == 4, "failed add mutated inventory");
        Assert.True(!inventory.TryAdd(new ItemInstance(potion, -2)), "negative quantity accepted");
        Assert.True(!inventory.TryAdd(inventory[1]), "owned instance duplicated");
        return Task.CompletedTask;
    }

    public static async Task BenchmarkAsync()
    {
        Console.WriteLine($"CPU tick benchmark; Release; runtime={Environment.Version}; CPUs={Environment.ProcessorCount}; 128 players; no GPU/assets/database");
        for (var run = 0; run < 5; run++)
        {
            var world = new WorldServer(new WorldDataHolder(), Catalog());
            var joins = new List<Task<bool>>();
            for (ulong id = 1; id <= 128; id++)
            {
                var outbound = Channel.CreateBounded<WorldSnapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
                var player = Player(id);
                player.Teleport(new Vector3((id % 16) * 200, 0, (id / 16) * 200));
                joins.Add(world.JoinAsync(3, player, outbound.Writer));
            }

            world.Tick(0.05f, 1);
            await Task.WhenAll(joins);
            var moves = new List<Task<MovementVerdict>>();
            for (ulong id = 1; id <= 128; id++)
            {
                moves.Add(world.ReportMovementAsync(3, id, new Vector3((id % 16) * 200 + 20, 0, (id / 16) * 200), 0f, true, true, 0));
            }

            world.Tick(0.05f, 2);
            await Task.WhenAll(moves);
            for (uint tick = 0; tick < 100; tick++)
            {
                world.Tick(0.05f, tick);
            }

            var durations = new double[1000];
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (uint tick = 0; tick < durations.Length; tick++)
            {
                var start = Stopwatch.GetTimestamp();
                world.Tick(0.05f, tick);
                durations[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Array.Sort(durations);
            Console.WriteLine($"run={run + 1} median={durations[500]:F3}ms p95={durations[950]:F3}ms max={durations[^1]:F3}ms bytes/tick={bytes / durations.Length}");
        }
    }

    private sealed class SinglePrey : IPreyView
    {
        public IReadOnlyList<PreyCandidate> Candidates { get; } = [new(1, new Vector3(1000, 0, 0), true)];
        public bool TryGet(ulong entityId, out PreyCandidate prey)
        {
            prey = Candidates[0];
            return entityId == prey.EntityId;
        }
    }
}
