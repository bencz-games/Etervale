using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using static PristonTale.Tests.MultiplayerMovementTests;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Combat between real clients over TCP: hits, kills, drops and pickups, skills and monster behaviour are decided once on
// the server, so every viewer must see the same outcome.
internal static class MultiplayerCombatTests
{
    private const int DummyCode = 9001;
    private const int HunterCode = 9002;
    private const uint FireBolt = 0x08010002;
    private static readonly Vector3 Spot = MultiplayerServer.Camp + new Vector3(0f, 0f, 300f);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("multiplayer: a basic attack's swings, hits (without the skill flag), kill, drop, pickup and level up reach a watching player identically", AttackAsync);
        yield return ("multiplayer: a skill cast on a monster reaches a watching player as the same start, impact and damage, its hit flagged as a skill's", SkillAsync);
        yield return ("multiplayer: a monster's movement and attacks are the same for every player watching it", MonsterAsync);
    }

    public static MonsterDefinition Dummy(int life = 12) => new(DummyCode, "Practice dummy", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(10, 100, new MonsterRange(1, 1), 6, 40, 0, 0, 0, life, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(6, MonsterNature.Natural, 0, 0, 0, new MonsterRange(1, 1), MonsterActiveHour.Any),
        Drops = new DropTable([DropEntry.GoldSlot(1, 50, 50)], 1, AllPlayersSeeDrops: true),
        ExpReward = 2_000_000
    };

    private static MonsterDefinition Hunter() => new(HunterCode, "Hunter", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(10, 2000, new MonsterRange(1, 1), 6, 40, 0, 0, 0, 1_000_000, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(6, MonsterNature.Evil, 600, 4, 300, new MonsterRange(1, 1), MonsterActiveHour.Any)
    };

    private static async Task AttackAsync()
    {
        var world = MultiplayerServer.WorldWith(MultiplayerServer.SpawnBeside(DummyCode, Spot), Dummy());
        await using var server = await MultiplayerServer.StartAsync(world);
        var attackerSpot = Spot + new Vector3(30f, 0f, 0f);
        await using var a = await Enter(server, "Striker", attackerSpot);
        await using var b = await Enter(server, "Onlooker", Spot + new Vector3(-150f, 0f, 0f));

        var dummy = await a.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == DummyCode), "the dummy");
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(dummy.Id), "the dummy at the onlooker");
        var level = a.World.Read(world => world.Owner?.Level ?? 0);
        var hunt = a.HuntAsync(dummy.Id, world => world.Combat.Any(entry => entry.Event.Killed && entry.Event.TargetId == dummy.Id));

        await hunt;
        var kill = await b.WaitAsync(world => world.Combat.Select(entry => entry.Event).FirstOrDefault(value => value.Killed && value.TargetId == dummy.Id),
            "the killing blow", 12000);
        Assert.True(kill.AttackerId == a.EntityId && kill.Kind == CombatEventKind.Hit, "the kill was credited to another attacker");
        await hunt;
        var seenByA = a.World.Read(world => Blows(world, a.EntityId, dummy.Id))!;
        var seenByB = b.World.Read(world => Blows(world, a.EntityId, dummy.Id))!;
        Assert.True(seenByA.Count > 1 && seenByA.SequenceEqual(seenByB), $"the players saw different blows: {string.Join(",", seenByA)} / {string.Join(",", seenByB)}");
        Assert.True(seenByA.Where(blow => blow.Kind == CombatEventKind.Hit).Sum(blow => blow.Damage) >= 12, "the hits do not add up to the dummy's life");
        Assert.True(b.World.Read(world => world.Combat.All(entry => entry.Event.AttackerId != a.EntityId || !entry.Event.Skill)),
            "a basic attack's hit came flagged as a skill's");
        Assert.True(a.World.Read(world => world.Combat.Any(entry => entry.Event.Kind == CombatEventKind.SwingBegin && entry.Event.AttackerId == a.EntityId)),
            "the swings were not shown");

        foreach (var viewer in new[] { a, b })
        {
            await viewer.WaitUntilAsync(world => !world.Entities.TryGetValue(dummy.Id, out var entity) || entity.Life <= 0 || entity.Motion == MotionState.Dead,
                "the dead dummy");
        }

        var drop = await b.WaitAsync(world => world.Ground.Values.FirstOrDefault(), "the public drop");
        var dropAtA = await a.WaitAsync(world => world.Ground.GetValueOrDefault(drop.Id), "the drop at the killer");
        Assert.True(drop.Gold && drop.Amount == 50 && dropAtA.Amount == 50 && Vector3.Distance(drop.Position, dropAtA.Position) < 0.01f,
            "the players saw different drops");

        var promoted = await b.WaitAsync(world => world.Entities.TryGetValue(a.EntityId, out var entity) && entity.Level > level ? entity : null,
            "the striker's level up");
        Assert.True(a.World.Read(world => world.Owner?.Level) == promoted.Level, "the striker's own level differs from what others see");

        await a.WaitUntilAsync(world => world.Entities[a.EntityId].Motion != MotionState.Attack, "the striker's last swing ending");
        await a.WalkAsync(a.World.Read(world => world.Entities[a.EntityId].Position), drop.Position);
        var revision = a.World.Read(world => world.Owner!.InventoryRevision);
        var money = a.World.Read(world => world.Owner!.Money);
        var pickup = await a.InventoryAsync(new InventoryCommand(Guid.NewGuid(), revision, InventoryAction.Pickup, drop.Id, 0, 1, EquipmentSlot.Weapon));
        Assert.True(pickup.Ok, $"pickup refused: {pickup.Message}");
        await b.WaitUntilAsync(world => world.GroundRemoved.Contains(drop.Id) && !world.Ground.ContainsKey(drop.Id), "the picked drop removed");
        await a.WaitUntilAsync(world => !world.Ground.ContainsKey(drop.Id) && world.Owner!.Money == money + 50, "the gold in the striker's purse");
        await b.WaitUntilAsync(world => world.Removed.Contains(dummy.Id), "the corpse removed", 9000);
    }

    private static async Task SkillAsync()
    {
        var world = MultiplayerServer.WorldWith(MultiplayerServer.SpawnBeside(DummyCode, Spot), Dummy(life: 20_000));
        await using var server = await MultiplayerServer.StartAsync(world);
        var casterSpot = Spot + new Vector3(150f, 0f, 0f);
        await using var a = await Enter(server, "Caster", casterSpot, playerClass: PlayerClass.Magician, level: 20,
            customize: setup => setup with { Skills = [(FireBolt, 5)], Weapon = MultiplayerServer.Staff });
        await using var b = await Enter(server, "Spectator", Spot + new Vector3(-150f, 0f, 0f));
        var dummy = await a.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == DummyCode), "the dummy");
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(dummy.Id), "the dummy at the spectator");

        var result = await a.CastAsync(FireBolt, dummy.Id);
        Assert.True(result.Accepted, $"Fire Bolt refused: {result.Status}");
        foreach (var viewer in new[] { a, b })
        {
            await viewer.WaitUntilAsync(world => world.Skills.Any(value => value.SkillCode == FireBolt && value.Phase == SkillEventPhase.Impact)
                && world.Combat.Any(entry => entry.Event.AttackerId == a.EntityId && entry.Event.TargetId == dummy.Id && entry.Event.Kind == CombatEventKind.Hit),
                "the bolt's impact and hit");
        }

        var skillsA = a.World.Read(world => world.Skills.Where(value => value.CasterId == a.EntityId).ToList())!;
        var skillsB = b.World.Read(world => world.Skills.Where(value => value.CasterId == a.EntityId).ToList())!;
        Assert.True(skillsA.Count == 2 && skillsA.SequenceEqual(skillsB), "the spectator saw another skill timeline");
        Assert.True(skillsA[0].Phase == SkillEventPhase.Start && skillsA[0].TargetId == dummy.Id && skillsA[0].Rank == 5, "the start event is wrong");
        var hitA = a.World.Read(world => Blows(world, a.EntityId, dummy.Id))!;
        var hitB = b.World.Read(world => Blows(world, a.EntityId, dummy.Id))!;
        Assert.True(hitA.Count == 1 && hitA.SequenceEqual(hitB) && hitA[0].Damage > 0, "the bolt's damage differs between the players");
        Assert.True(b.World.Read(world => world.Combat.Any(entry => entry.Event.AttackerId == a.EntityId && entry.Event.Kind == CombatEventKind.Hit && entry.Event.Skill)),
            "the bolt's hit did not come flagged as a skill's");
        await b.WaitUntilAsync(world => world.Entities[dummy.Id].Life == 20_000 - hitA[0].Damage, "the dummy's life after the bolt");
        await a.WaitUntilAsync(world => world.Entities[dummy.Id].Life == 20_000 - hitA[0].Damage, "the caster's view of the dummy's life");
        await b.WaitUntilAsync(world => world.Entities[a.EntityId].Motion == MotionState.Skill
            || world.Entities[a.EntityId].Samples.Any(sample => sample.Motion == MotionState.Skill), "the caster's skill motion");
    }

    private static async Task MonsterAsync()
    {
        var world = MultiplayerServer.WorldWith(MultiplayerServer.SpawnBeside(HunterCode, Spot), Hunter());
        await using var server = await MultiplayerServer.StartAsync(world);
        await using var a = await Enter(server, "PreyA", Spot + new Vector3(200f, 0f, 0f));
        await using var b = await Enter(server, "PreyB", Spot + new Vector3(-200f, 0f, 60f));
        await using var c = await Enter(server, "PreyC", Spot + new Vector3(0f, 0f, -420f));
        var clients = new[] { a, b, c };
        var hunter = await a.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == HunterCode), "the hunter");
        await a.WaitUntilAsync(world => world.Combat.Count(entry => entry.Event.AttackerId == hunter.Id && entry.Event.Kind == CombatEventKind.Hit) >= 2,
            "the hunter's hits", 12000);
        await Task.Delay(500);

        // Viewers met the hunter at different ticks: each one's attacks must run in step with the first viewer's from the
        // first attack both saw.
        var attacks = clients.Select(client => client.World.Read(world => world.Combat.Where(entry => entry.Event.AttackerId == hunter.Id).ToList())!).ToArray();
        for (var index = 1; index < attacks.Length; index++)
        {
            Assert.True(InStep(attacks[0], attacks[index]) >= 2, $"{clients[index].Name} saw other monster attacks: {Dump(attacks[0])} vs {Dump(attacks[index])}");
        }

        var samples = clients.Select(client => client.World.Read(world => world.Entities[hunter.Id].Samples
            .Where(sample => sample.TimeMs is not null).GroupBy(sample => sample.TimeMs!.Value).ToDictionary(group => group.Key, group => group.Last()))!).ToArray();
        var moved = samples[0].Values.Select(sample => sample.Position).Distinct().Count();
        Assert.True(moved >= 3, "the hunter did not move");
        var compared = 0;
        for (var first = 0; first < samples.Length; first++)
        {
            for (var second = first + 1; second < samples.Length; second++)
            {
                foreach (var (time, sample) in samples[first])
                {
                    if (samples[second].TryGetValue(time, out var other))
                    {
                        compared++;
                        Assert.True(Vector3.Distance(sample.Position, other.Position) < 0.01f && sample.Motion == other.Motion,
                            $"{clients[first].Name} and {clients[second].Name} disagree on the hunter at {time} ms");
                    }
                }
            }
        }

        Assert.True(compared >= 6, $"only {compared} shared samples of the hunter");
    }

    // The number of events two viewers share once aligned on the first event (and its server tick) they both saw; 0 when
    // they disagree.
    private static int InStep(List<(double? TimeMs, CombatEventInfo Event)> first, List<(double? TimeMs, CombatEventInfo Event)> second)
    {
        var (longer, shorter) = first.Count >= second.Count ? (first, second) : (second, first);
        for (var skip = 0; skip < shorter.Count; skip++)
        {
            var start = longer.IndexOf(shorter[skip]);
            if (start < 0)
            {
                continue;
            }

            var shared = Math.Min(shorter.Count - skip, longer.Count - start);
            return shorter.Skip(skip).Take(shared).SequenceEqual(longer.Skip(start).Take(shared)) ? shared : 0;
        }

        return 0;
    }

    private static string Dump(List<(double? TimeMs, CombatEventInfo Event)> events) =>
        string.Join(" ", events.Select(entry => $"{entry.TimeMs}:{entry.Event.Kind}>{entry.Event.TargetId}={entry.Event.Damage}"));

    // Blows of one attacker (on one target, or any target with 0), in server order, without the arrival time.
    private static List<CombatEventInfo> Blows(ObservedWorld world, ulong attacker, ulong target) => world.Combat
        .Select(entry => entry.Event)
        .Where(value => value.AttackerId == attacker && (target == 0 || value.TargetId == target))
        .ToList();
}
