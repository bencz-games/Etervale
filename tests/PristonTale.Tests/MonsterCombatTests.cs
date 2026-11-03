using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class MonsterCombatTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("monster attack entry and impact preserve separate original reach rules", ReachAsync);
        yield return ("escaping during windup prevents damage and does not restart a distant melee swing", EscapeAsync);
        yield return ("authority rejects distant, dead and mismatched monster hit events", AuthorityAsync);
        yield return ("monster damage uses incoming accuracy and flat player absorption without level-based criticals", DamageAsync);
        yield return ("30-unit melee reaches the player through field collision and resumes after a first player attack", ShortReachAsync);
    }

    private static MonsterDefinition Definition() => new(
        1, "Combat fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(1, 1000, new MonsterRange(10, 10), 6, 80, 0, 0, 0, 100, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(6, MonsterNature.Evil, 2000, 1, 2000, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static Task ReachAsync()
    {
        var monster = Definition();
        Assert.True(MonsterAttackReach.CanBegin(monster, Vector3.Zero, new Vector3(79, 0, 0)), "authored entry range was reduced");
        Assert.True(!MonsterAttackReach.CanBegin(monster, Vector3.Zero, new Vector3(80, 0, 0)), "strict original range gained padding");
        Assert.True(!MonsterAttackReach.CanBegin(monster, Vector3.Zero, new Vector3(1, 65, 0)), "attack crosses a different floor");
        Assert.True(MonsterAttackReach.CanLandBasic(monster, Vector3.Zero, new Vector3(110, 0, 0)), "packet impact tolerance lost");
        Assert.True(!MonsterAttackReach.CanLandBasic(monster, Vector3.Zero, new Vector3(111, 0, 0)), "melee impact exceeds original packet reach");
        var ranged = monster with { SoundCode = 0x1180 };
        Assert.True(MonsterAttackReach.CanLandBasic(ranged, Vector3.Zero, new Vector3(459, 0, 0)), "Figon's ranged exception lost");
        Assert.True(!MonsterAttackReach.CanLandBasic(ranged, Vector3.Zero, new Vector3(460, 0, 0)), "ranged impact became unbounded");
        Assert.True(!MonsterAttackReach.CanBegin(monster, Vector3.Zero, new Vector3(float.NaN, 0, 0)), "NaN entered an attack");
        return Task.CompletedTask;
    }

    private static Task EscapeAsync()
    {
        var definition = Definition();
        var monster = new MonsterInstance(2, definition, Vector3.Zero, 0, 0)
        {
            TargetId = 1,
            AiState = MonsterAiState.Chase
        };
        var prey = new MutablePrey(new Vector3(70, 0, 0));
        var tuning = new MonsterAiTuning(definition);
        var brain = new MonsterAiBrain(new CombatRng(1));
        brain.Tick(monster, tuning, prey, 60);
        Assert.True(monster.AiState == MonsterAiState.Attack, "fixture did not begin a swing");
        prey.Move(new Vector3(800, 0, 0));
        Assert.True(brain.Tick(monster, tuning, prey, tuning.WindupMs) is null, "windup hit followed escaping prey across the map");
        brain.Tick(monster, tuning, prey, tuning.CooldownMs);
        monster.SwingJustBegan = false;
        brain.Tick(monster, tuning, prey, 60);
        Assert.True(!monster.SwingJustBegan && monster.Motion.IsMoving(), "distant target restarted attack instead of pursuit");
        return Task.CompletedTask;
    }

    private static Task AuthorityAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Receiver");
        var player = new WorldPlayer(1, new Vector3(800, 0, 0), character, false, default);
        var monster = new MonsterInstance(2, Definition(), Vector3.Zero, 0, 0);
        var host = new CombatHost(player, monster);
        var combat = new FieldCombat(host, new CombatRng(7));
        var initialLife = character.Life;
        for (var index = 0; index < 100; index++)
        {
            combat.ResolveMonsterHit(monster, new MonsterAttackEvent(2, 1, false));
        }

        Assert.True(character.Life == initialLife && host.EventCount == 0, "authority trusted an out-of-range hit event");
        player.Teleport(new Vector3(70, 0, 0));
        combat.ResolveMonsterHit(monster, new MonsterAttackEvent(999, 1, false));
        monster.ApplyDamage(100);
        combat.ResolveMonsterHit(monster, new MonsterAttackEvent(2, 1, false));
        Assert.True(character.Life == initialLife, "invalid/dead attacker inflicted damage");
        return Task.CompletedTask;
    }

    private static Task DamageAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Receiver");
        var defender = CombatProfileFactory.ForPlayerDefender(character) with { Absorption = 7, Defence = 1000, Level = 1 };
        var attack = CombatProfileFactory.ForMonsterBasicAttack(Definition().Combat) with { AttackerLevel = 100 };
        var rng = new CombatRng(9);
        var landed = 0;
        for (var index = 0; index < 100; index++)
        {
            var result = CombatResolver.ResolveMonsterAttack(rng, attack, defender, false);
            if (result.Hit)
            {
                landed++;
                Assert.True(result.Damage == 3 && !result.Crit, "incoming attack used percent absorption or invented criticals");
            }
        }

        Assert.True(landed > 0, "fixture never hit");
        var equalLevel = attack with { AttackerLevel = 1, AttackRating = 951 };
        Assert.True(CombatResolver.MonsterHitChance(equalLevel, defender) == 65, "monster accuracy used outgoing 1.4 multiplier or lower bracket");
        return Task.CompletedTask;
    }

    private static Task ShortReachAsync()
    {
        // Hopy's source reach/speed/IQ, with extra life on both sides so this tests repeated cycles.
        var definition = Definition() with
        {
            Combat = Definition().Combat with { MeleeRange = 30, AttackSpeed = 8, MaxLife = 10000 },
            Ai = new MonsterAi(5, MonsterNature.Evil, 240, 1, 256, MonsterRange.Zero, MonsterActiveHour.Any)
        };
        var field = new FieldInstance(3, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(2));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Collision fixture", 100);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(90, 0, 0), 0, 0);
        var attackEntries = 0;
        for (var tick = 0; tick < 300; tick++)
        {
            field.Step(0.05f);
            attackEntries += (field.SnapshotEvents() ?? []).Count(value => value.Kind == CombatEventKind.SwingBegin && value.AttackerId == monster.EntityId);
        }

        Assert.True(attackEntries >= 3, $"short melee stopped outside its reach: distance={monster.Position.Length()}, swings={attackEntries}");
        Assert.True(!player.IsDead, "fixture died before checking player attacks");
        var previousLife = monster.Life;
        var input = new AttackInputController();
        for (var attack = 0; attack < 3; attack++)
        {
            var press = input.Press(monster.EntityId, false, false);
            Assert.True(field.SetAttackTarget(1, press.TargetId, press.Sustained, press.Sequence), "repeated press was rejected");
            var release = input.Release(false, false)!.Value;
            Assert.True(field.SetAttackTarget(1, release.TargetId, release.Sustained, release.Sequence), "release was rejected");
            for (var tick = 0; tick < 80; tick++)
            {
                field.Step(0.05f);
                input.Reconcile(player.AttackSequence, player.AttackTargetId);
            }

            Assert.True(input.TargetId == 0, "client kept presenting attacks after server completion");
            Assert.True(player.AttackTargetId == 0 && monster.Life < previousLife, "tap did not complete or combat stopped after its first hit");
            previousLife = monster.Life;
        }

        return Task.CompletedTask;
    }

    private sealed class MutablePrey(Vector3 position) : IPreyView
    {
        private readonly PreyCandidate[] _candidates = [new(1, position, true)];
        public IReadOnlyList<PreyCandidate> Candidates => _candidates;
        public void Move(Vector3 target) => _candidates[0] = new PreyCandidate(1, target, true);
        public bool TryGet(ulong entityId, out PreyCandidate prey)
        {
            prey = _candidates[0];
            return entityId == prey.EntityId;
        }
    }

    private sealed class CombatHost(WorldPlayer player, MonsterInstance monster) : IFieldCombatHost
    {
        public int EventCount { get; private set; }
        public Dictionary<ulong, MonsterInstance>.ValueCollection Monsters { get; } = new Dictionary<ulong, MonsterInstance> { [monster.EntityId] = monster }.Values;
        public IReadOnlyList<PreyCandidate> NearbyPlayers(Vector3 origin, float radius) => [new(player.EntityId, player.Position, !player.IsDead)];
        public void DespawnMonster(ulong entityId) { }
        public MonsterAiTuning TuningFor(MonsterDefinition definition) => new(definition);
        public (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer target) => (1000, 400);
        public void EmitCombatEvent(in CombatEventView combatEvent) => EventCount++;
        public bool TryGetPlayer(ulong entityId, [MaybeNullWhen(false)] out WorldPlayer target)
        {
            target = player;
            return entityId == player.EntityId;
        }

        public bool TryGetMonster(ulong entityId, [MaybeNullWhen(false)] out MonsterInstance target)
        {
            target = monster;
            return entityId == monster.EntityId;
        }
    }
}
