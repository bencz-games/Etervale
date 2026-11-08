using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Monsters;
using static PristonTale.Tests.MultiplayerMovementTests;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// What players show about each other besides movement: statuses, summons, equipment, premium looks, item effects,
// death and respawn. Each must look the same to every viewer, including viewers that arrive later.
internal static class MultiplayerPresenceTests
{
    private const uint PhysicalAbsorption = 0x01010003;
    private const uint MetalGolem = 0x01080004;
    private const int GolemCode = 40;
    private const int KillerCode = 9003;
    private static readonly Vector3 Spot = MultiplayerServer.Camp + new Vector3(0f, 0f, 300f);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("multiplayer: a player's buff reaches its viewers and players who see it only later", StatusAsync);
        yield return ("multiplayer: a summon shows its owner to every viewer even beside wild monsters of its kind", SummonAsync);
        yield return ("multiplayer: equipment, premium looks, item statuses and item effects of a player reach its viewers", AppearanceAsync);
        yield return ("multiplayer: a player's death and respawn reach the viewers at both places", DeathAsync);
        yield return ("multiplayer: a crystal pet shows its owner to every viewer beside wild monsters of its kind", CrystalPetAsync);
        yield return ("multiplayer: descriptions the server's rate limit dropped are asked again so a crowd still appears", CrowdAsync);
    }

    private static async Task StatusAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Shielded", camp, playerClass: PlayerClass.Mechanician, level: 20,
            customize: setup => setup with { Skills = [(PhysicalAbsorption, 2)] });
        await using var b = await Enter(server, "Beside", camp + new Vector3(120f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the mechanician");

        var cast = await a.CastAsync(PhysicalAbsorption);
        Assert.True(cast.Accepted, $"Physical Absorption refused: {cast.Status}");
        var seen = await b.WaitAsync(world => world.Statuses.FirstOrDefault(status => status.EntityId == a.EntityId && status.StatusCode == PhysicalAbsorption
            && status.Op == StatusEffectOp.Add), "the buff");
        var own = await a.WaitAsync(world => world.Statuses.FirstOrDefault(status => status.EntityId == a.EntityId && status.StatusCode == PhysicalAbsorption
            && status.Op == StatusEffectOp.Add), "its own buff");
        Assert.True(seen == own && seen.DurationMs > 0 && !seen.Harmful, "the viewer saw another buff than its holder");

        // A player that enters the range after the buff started still sees it, with the time already spent.
        await Task.Delay(600);
        await using var late = await Enter(server, "Latecomer", camp + new Vector3(-120f, 0f, 0f));
        var arrived = await late.WaitAsync(world => world.Statuses.FirstOrDefault(status => status.EntityId == a.EntityId && status.StatusCode == PhysicalAbsorption),
            "the running buff");
        Assert.True(arrived.Op == StatusEffectOp.Add && arrived.DurationMs == seen.DurationMs && arrived.RemainingMs < seen.RemainingMs
            && arrived.RemainingMs > seen.RemainingMs - 5000, $"the latecomer saw {arrived}");
        Assert.True(!late.World.Read(world => world.Statuses.Any(status => status.EntityId == late.EntityId && status.StatusCode != PlayerGrace.StatusCode)),
            "the latecomer received statuses it does not have");
        var grace = await late.WaitAsync(world => world.Statuses.FirstOrDefault(status => status.EntityId == late.EntityId && status.StatusCode == PlayerGrace.StatusCode),
            "the latecomer's arrival grace");
        Assert.True(grace.Op == StatusEffectOp.Add && grace.RemainingMs is > 0 and <= (int)PlayerGrace.ArrivalMs, $"the latecomer's grace was {grace}");
    }

    private static async Task SummonAsync()
    {
        var golem = SkillScenario.SummonModel("Metal Golem", GolemCode);
        var world = MultiplayerServer.WorldWith(MultiplayerServer.SpawnBeside(GolemCode, Spot), golem);
        await using var server = await MultiplayerServer.StartAsync(world);
        await using var b = await Enter(server, "GolemFan", Spot + new Vector3(-200f, 0f, 0f), level: 80);
        var wild = await b.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == GolemCode), "the wild golem");
        Assert.True(wild.OwnerId == 0, "the wild golem has an owner");
        await using var a = await Enter(server, "Engineer", Spot + new Vector3(250f, 0f, 0f), playerClass: PlayerClass.Mechanician, level: 80,
            customize: setup => setup with { Skills = [(MetalGolem, 3)] });
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the engineer");

        var cast = await a.CastAsync(MetalGolem);
        Assert.True(cast.Accepted, $"Metal Golem refused: {cast.Status}");
        foreach (var viewer in new[] { a, b })
        {
            var summon = await viewer.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == GolemCode && entity.Id != wild.Id
                && entity.Spawns > 0 && entity.OwnerId != 0), "the owned golem");
            Assert.True(summon.OwnerId == a.EntityId && summon.Kind == EntityKind.Monster, $"{viewer.Name} saw the summon owned by {summon.OwnerId}");
        }

        await Task.Delay(300);
        Assert.True(b.World.Read(world => world.Entities.Values.Count(entity => entity.TemplateId == GolemCode && entity.OwnerId == a.EntityId)) == 1,
            "a wild golem took the summon's owner");
        Assert.True(b.World.Read(world => !world.Entities.TryGetValue(wild.Id, out var value) || value.OwnerId == 0), "the wild golem became owned");

        // A golem summoned while the viewer already knows wild ones is not mistaken for them, and the reverse.
        await using var c = await Enter(server, "LateFan", Spot + new Vector3(-150f, 0f, 80f), level: 80);
        var summonId = a.World.Read(world => world.Entities.Values.First(entity => entity.OwnerId == a.EntityId).Id);
        await c.WaitUntilAsync(world => world.Entities.TryGetValue(summonId, out var value) && value.OwnerId == a.EntityId, "the summon for a later viewer");
        await c.WaitUntilAsync(world => world.Entities.Values.Any(entity => entity.TemplateId == GolemCode && entity.OwnerId == 0), "a wild golem for a later viewer");
    }

    private static async Task AppearanceAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Dandy", camp, playerClass: PlayerClass.Magician, level: 20, customize: setup => setup with
        {
            Weapon = MultiplayerServer.Staff,
            Items = [MultiplayerServer.Firecracker, MultiplayerServer.BigHead, MultiplayerServer.Invulnerability]
        });
        await using var b = await Enter(server, "Admirer", camp + new Vector3(140f, 0f, 0f));
        var first = await b.WaitAsync(world => world.Entities.GetValueOrDefault(a.EntityId), "the magician");
        Assert.True(first.Player?.WeaponKey == "WM101" && first.Player.BigHead == 0, "the viewer missed the staff");

        await a.WaitUntilAsync(world => world.Owner is not null, "its own state");
        var staff = a.World.Read(world => world.Owner!.Items.First(item => item.CatalogKey == "WM101"));
        var revision = a.World.Read(world => world.Owner!.InventoryRevision);
        var unequip = await a.InventoryAsync(new InventoryCommand(Guid.NewGuid(), revision, InventoryAction.Unequip, staff!.InstanceId, -1, 1,
            EquipmentSlot.Weapon));
        Assert.True(unequip.Ok, $"unequip refused: {unequip.Message}");
        await b.WaitUntilAsync(world => world.Entities[a.EntityId].Player?.WeaponKey == "", "the staff put away");

        await a.WaitUntilAsync(world => world.Owner!.InventoryRevision == unequip.Revision, "the new inventory");
        var big = await a.UseItemAsync("BI109");
        Assert.True(big.Ok, $"big head refused: {big.Status}");
        await b.WaitUntilAsync(world => world.Entities[a.EntityId].Player?.BigHead > 0, "the big head");

        await a.WaitUntilAsync(world => world.Owner!.InventoryRevision == big.Revision, "the inventory after the big head");
        var firecracker = await a.UseItemAsync("BI123");
        Assert.True(firecracker.Ok, $"firecracker refused: {firecracker.Status}");
        var burst = await b.WaitAsync(world => world.ItemEffects.FirstOrDefault(effect => effect.Entity == a.EntityId), "the firecracker");
        var ownBurst = await a.WaitAsync(world => world.ItemEffects.FirstOrDefault(effect => effect.Entity == a.EntityId), "its own firecracker");
        Assert.True(burst.Effect == ItemEffectKind.Firecracker && burst == ownBurst, "the viewer saw another firecracker");

        await a.WaitUntilAsync(world => world.Owner!.InventoryRevision == firecracker.Revision, "the inventory after the firecracker");
        var scroll = await a.UseItemAsync("BI105");
        Assert.True(scroll.Ok, $"invulnerability scroll refused: {scroll.Status}");
        var status = await b.WaitAsync(world => world.Statuses.FirstOrDefault(value => value.EntityId == a.EntityId && value.Op == StatusEffectOp.Add
            && value.StatusCode != PlayerGrace.StatusCode), "the scroll's status");
        Assert.True(status.SourceKind == (byte)EffectSourceKind.Item, "the scroll status has another source");
    }

    private static async Task CrystalPetAsync()
    {
        const int hopyCode = 9010;
        var hopy = new MonsterDefinition(hopyCode, "Hopy", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(5, 100, new MonsterRange(1, 2), 6, 40, 5, 0, 0, 2000, ResistanceBlock.Zero), "")
        {
            Key = "HOPY 2",
            Ai = new MonsterAi(6, MonsterNature.Natural, 0, 2, 100, new MonsterRange(1, 1), MonsterActiveHour.Any)
        };
        var world = MultiplayerServer.WorldWith(MultiplayerServer.SpawnBeside(hopyCode, Spot), hopy);
        await using var server = await MultiplayerServer.StartAsync(world);
        await using var b = await Enter(server, "PetWatcher", Spot + new Vector3(-200f, 0f, 0f));
        var wild = await b.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == hopyCode), "the wild hopy");
        await using var a = await Enter(server, "Tamer", Spot + new Vector3(250f, 0f, 0f), level: 20,
            customize: setup => setup with { Items = [MultiplayerServer.HopyCrystal] });
        await a.WaitUntilAsync(world => world.Owner is not null && world.Entities.ContainsKey(wild.Id), "its own state and the wild hopy");

        var used = await a.UseItemAsync("GP101");
        Assert.True(used.Ok, $"the crystal was refused: {used.Status}");
        foreach (var viewer in new[] { a, b })
        {
            var pet = await viewer.WaitAsync(world => world.Entities.Values.FirstOrDefault(entity => entity.TemplateId == hopyCode && entity.OwnerId != 0),
                "the crystal pet");
            Assert.True(pet.OwnerId == a.EntityId && pet.Id != wild.Id, $"{viewer.Name} saw the pet owned by {pet.OwnerId}");
        }

        await Task.Delay(300);
        Assert.True(b.World.Read(world => world.Entities.Values.Count(entity => entity.TemplateId == hopyCode && entity.OwnerId == a.EntityId)) == 1,
            "a wild hopy took the pet's owner");
        Assert.True(b.World.Read(world => !world.Entities.TryGetValue(wild.Id, out var value) || value.OwnerId == 0), "the wild hopy became owned");
    }

    private static async Task DeathAsync()
    {
        var killer = new MonsterDefinition(KillerCode, "Executioner", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(60, 5000, new MonsterRange(5000, 5000), 6, 60, 0, 0, 0, 1_000_000, ResistanceBlock.Zero), "")
        {
            Ai = new MonsterAi(6, MonsterNature.Evil, 250, 4, 100, new MonsterRange(1, 1), MonsterActiveHour.Any)
        };
        var world = MultiplayerServer.WorldWith(MultiplayerServer.SpawnBeside(KillerCode, Spot), killer);
        await using var server = await MultiplayerServer.StartAsync(world);
        // Forest 1's start point nearest to the camp is where the dead stand up in the field; it lies beyond the view range of
        // the camp.
        var respawn = new Vector3(1350f, 0f, -13672f);
        await using var a = await Enter(server, "Victim", Spot + new Vector3(60f, 0f, 0f), level: 1);
        await using var b = await Enter(server, "Mourner", Spot + new Vector3(-650f, 0f, 0f));
        await using var c = await Enter(server, "Healer", respawn + new Vector3(100f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the victim");

        await b.WaitUntilAsync(world => world.Entities.TryGetValue(a.EntityId, out var victim) && victim.Life <= 0 && victim.Motion == MotionState.Dead,
            "the victim dead", (int)PlayerGrace.ArrivalMs + 9000);
        var killing = b.World.Read(world => world.Combat.Select(entry => entry.Event).First(value => value.TargetId == a.EntityId && value.Killed));
        Assert.True(a.World.Read(world => world.Combat.Any(entry => entry.Event == killing)), "the victim saw another killing blow");
        await Task.Delay(500);
        Assert.True(b.World.Read(world => world.Entities.TryGetValue(a.EntityId, out var down) && down.Life <= 0), "the victim stood up without choosing");
        var restart = await a.RestartAsync(RestartChoice.Field);
        Assert.True(restart.Ok && restart.ExperienceLost == 0 && restart.GoldLost == 0, $"a level 1 field restart failed or cost: {restart}");
        await c.WaitAsync(world => world.Entities.TryGetValue(a.EntityId, out var victim) && victim.Life > 0
            && Vector3.Distance(Planar(victim.Position), Planar(respawn)) < 1f ? victim : null, "the victim respawning", 9000);
        await b.WaitUntilAsync(world => world.Entities.TryGetValue(a.EntityId, out var victim) && victim.Life > 0
            && Vector3.Distance(Planar(victim.Position), Planar(respawn)) < 1f, "the victim respawning at the start point");
    }

    private static async Task CrowdAsync()
    {
        // A two-request bucket refilled twice a second: most first requests of a crowd entering view are dropped.
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith(), ("Network:PlayerInfoBurst", "2"),
            ("Network:PlayerInfoPerSecond", "2"));
        var camp = MultiplayerServer.Camp;
        var crowd = new List<MultiplayerClient>();
        try
        {
            for (var index = 0; index < 6; index++)
            {
                crowd.Add(await Enter(server, $"Crowd{index}", camp + new Vector3(40f * index, 0f, 0f)));
            }

            await using var viewer = await Enter(server, "Newcomer", camp + new Vector3(0f, 0f, 120f));
            await viewer.WaitUntilAsync(world => crowd.All(member => world.Entities.TryGetValue(member.EntityId, out var entity)
                && entity.Player?.Name == member.Name), "every member of the crowd", 9000);
        }
        finally
        {
            foreach (var member in crowd)
            {
                await member.DisposeAsync();
            }
        }
    }
}
