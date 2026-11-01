using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ProgressionTransactionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("all 40 first-tier skills enforce ten ranks, predecessor and original gold costs", LearningAsync);
        yield return ("later skills open by class tier, need the skill before them, cost the window price and spend final-tier points last",
            LaterTierLearningAsync);
        yield return ("NPC learning is durable, rejects remote service access and replays after reconnect", DurabilityAsync);
        yield return ("a skill master trains only its own town's classes: Ricarten's the Tempskron, not the Morion", HomeMasterAsync);
        yield return ("a gained level and a trained skill rank each give one cue for the effects everyone near sees", ProgressCuesAsync);
    }

    private static Task ProgressCuesAsync()
    {
        var registry = new CharacterClassRegistry();
        var character = registry.CreatePlayer(PlayerClass.Fighter, "Cues", 1);
        var player = new WorldPlayer(1, new Vector3(100, 0, 100), character, false, default);
        player.TakeProgressCues(out var leveled, out var learned);
        Assert.True(!leveled && !learned, "a player that just entered gave a cue");

        character.AwardExperience(ExperienceTable.ForLevel(20));
        player.TakeProgressCues(out leveled, out learned);
        Assert.True(leveled && !learned, "a level gain gave no level-up cue");
        player.TakeProgressCues(out leveled, out _);
        Assert.True(!leveled, "a level-up cue repeated");

        var skill = ClassSkillCatalog.For(PlayerClass.Fighter)[0];
        character.RestoreProgress(character.Experience, 1000000, tier: 0, skillPoints: 5, eliteSkillPoints: 0);
        Assert.True(character.ApplyProgression(new ProgressionCommand(Guid.NewGuid(), character.ProgressionRevision, ProgressionAction.LearnSkill,
            SkillCode: skill.Code, NpcEntityId: 1)), "the skill was not taught");
        player.TakeProgressCues(out leveled, out learned);
        Assert.True(learned && !leveled, "a trained rank gave no skill-learn cue");
        player.TakeProgressCues(out _, out learned);
        Assert.True(!learned, "a skill-learn cue repeated");
        return Task.CompletedTask;
    }

    private static Task LaterTierLearningAsync()
    {
        var registry = new CharacterClassRegistry();
        int[] basePrices = [500, 1000, 1500, 2000, 3000, 5000, 7000, 9000, 10000, 12000, 15000, 20000, 25000, 30000, 35000, 40000];
        int[] rankPrices = [500, 600, 700, 800, 1000, 1200, 1400, 1600, 2000, 2500, 3000, 4000, 5000, 6000, 7000, 8000];
        foreach (var playerClass in new[] { PlayerClass.Mechanician, PlayerClass.Fighter, PlayerClass.Pikeman, PlayerClass.Archer })
        {
            var skills = ClassSkillCatalog.For(playerClass);
            var player = registry.CreatePlayer(playerClass, "Tiers", 100);
            foreach (var skill in skills.Take(4))
            {
                player.Skills.Restore(skill.Code, 1);
            }

            player.RestoreProgress(ExperienceTable.ForLevel(100), 10000000, tier: 0, skillPoints: 200, eliteSkillPoints: 20);
            Assert.True(!SkillLearning.TryQuote(player, skills[4].Code, out _, out _), "a second-tier skill was taught before the class rose a tier");
            for (var tier = 1; tier <= 3; tier++)
            {
                player.RestoreProgress(player.Experience, player.Money, tier: (short)tier, skillPoints: player.SkillPoints,
                    eliteSkillPoints: player.EliteSkillPoints);
                var next = 4 * tier + 4;
                Assert.True(next == 16 || !SkillLearning.CanTrain(skills, next, code => code == skills[next].Code ? 0 : 1, 200, tier, 10, 10),
                    "a later tier opened early");
                for (var index = 4 * tier + 1; index < 4 * tier + 4; index++)
                {
                    Assert.True(!SkillLearning.TryQuote(player, skills[index].Code, out _, out _), $"{skills[index].Name} skipped the skill before it");
                }

                for (var index = 4 * tier; index < 4 * tier + 4; index++)
                {
                    var skill = skills[index];
                    var points = player.SkillPoints;
                    var finalPoints = player.EliteSkillPoints;
                    Assert.True(SkillLearning.TryQuote(player, skill.Code, out var price, out var finalTier) && price == basePrices[index]
                        && finalTier == (index >= 12), $"{skill.Name} first rank price {price} differs from the window price");
                    var money = player.Money;
                    Assert.True(player.ApplyProgression(new ProgressionCommand(Guid.NewGuid(), player.ProgressionRevision, ProgressionAction.LearnSkill,
                        SkillCode: skill.Code, NpcEntityId: 1)), $"{skill.Name} was not taught");
                    Assert.True(player.Money == money - price && player.Skills.RankOf(skill.Code) == 1, $"{skill.Name} rank or gold wrong");
                    Assert.True(index >= 12
                        ? player.EliteSkillPoints == finalPoints - 1 && player.SkillPoints == points
                        : player.SkillPoints == points - 1 && player.EliteSkillPoints == finalPoints, $"{skill.Name} spent the wrong points");
                    Assert.True(SkillLearning.TryQuote(player, skill.Code, out var second, out _) && second == basePrices[index] + rankPrices[index],
                        $"{skill.Name} second rank price differs");
                }
            }

            var golem = skills[15];
            var poor = registry.CreatePlayer(playerClass, "Poor", 100);
            foreach (var skill in skills.Take(15))
            {
                poor.Skills.Restore(skill.Code, 1);
            }

            poor.RestoreProgress(ExperienceTable.ForLevel(100), 10000000, tier: 3, skillPoints: 10, eliteSkillPoints: 0);
            Assert.True(!SkillLearning.TryQuote(poor, golem.Code, out _, out _), "a final-tier skill was taught without final-tier points");
            var young = registry.CreatePlayer(playerClass, "Young", golem.RequiredLevel - 1);
            foreach (var skill in skills.Take(15))
            {
                young.Skills.Restore(skill.Code, 1);
            }

            young.RestoreProgress(ExperienceTable.ForLevel(golem.RequiredLevel - 1), 10000000, tier: 3, skillPoints: 10, eliteSkillPoints: 5);
            Assert.True(!SkillLearning.TryQuote(young, golem.Code, out _, out _), "a final-tier skill was taught below its level");
        }

        var morion = registry.CreatePlayer(PlayerClass.Shaman, "Uncosted", 100);
        var shaman = ClassSkillCatalog.For(PlayerClass.Shaman);
        foreach (var skill in shaman.Take(4))
        {
            morion.Skills.Restore(skill.Code, 1);
        }

        morion.RestoreProgress(ExperienceTable.ForLevel(100), 10000000, tier: 3, skillPoints: 50, eliteSkillPoints: 5);
        Assert.True(shaman[4].ManaByRank.Count == 10 || !SkillLearning.TryQuote(morion, shaman[4].Code, out _, out _), "a skill without rank costs was taught");
        return Task.CompletedTask;
    }

    private static Task LearningAsync()
    {
        var registry = new CharacterClassRegistry();
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            var skills = ClassSkillCatalog.For(playerClass).Where(static skill => skill.Tier == JobTier.Job1).ToArray();
            var player = registry.CreatePlayer(playerClass, "Learning", 40);
            player.RestoreProgress(ExperienceTable.ForLevel(40), 1000000, skillPoints: 40);
            Assert.True(!SkillLearning.TryQuote(player, skills[1].Code, out _, out _), "predecessor requirement bypassed");
            var spent = 0;
            for (var rank = 0; rank < 10; rank++)
            {
                for (var index = 0; index < 4; index++)
                {
                    var expectedPrice = (index + 1) * 500 + rank * (500 + index * 100);
                    Assert.True(SkillLearning.TryQuote(player, skills[index].Code, out var price, out _) && price == expectedPrice, "original learning price differs");
                    var command = new ProgressionCommand(Guid.NewGuid(), player.ProgressionRevision, ProgressionAction.LearnSkill,
                        SkillCode: skills[index].Code, NpcEntityId: 1);
                    Assert.True(player.ApplyProgression(command) && player.Skills.RankOf(skills[index].Code) == rank + 1, "rank did not advance once");
                    Assert.True(!player.ApplyProgression(command), "stale progression command applied twice");
                    spent += expectedPrice;
                }
            }

            Assert.True(player.SkillPoints == 0 && player.Money == 1000000 - spent && player.ProgressionRevision == 40, "gold or point conservation failed");
            Assert.True(!SkillLearning.TryQuote(player, skills[0].Code, out _, out _), "rank 11 accepted");
            var low = registry.CreatePlayer(playerClass, "Level", 10);
            low.RestoreProgress(ExperienceTable.ForLevel(10), 10000, skillPoints: 10);
            Assert.True(low.ApplyProgression(new ProgressionCommand(Guid.NewGuid(), 0, ProgressionAction.LearnSkill,
                SkillCode: skills[0].Code, NpcEntityId: 1)), "initial skill rejected at level 10");
            Assert.True(!SkillLearning.TryQuote(low, skills[0].Code, out _, out _), "rank 2 accepted below level 12");
        }

        return Task.CompletedTask;
    }

    private static async Task HomeMasterAsync()
    {
        foreach (var (playerClass, trains) in new[] { (PlayerClass.Mechanician, true), (PlayerClass.Priestess, false) })
        {
            var classes = new CharacterClassRegistry();
            var starter = CharacterRecordFactory.NewStarter(classes, 1, "Pupil", 0, playerClass, 3) with { Id = 1 };
            var character = classes.CreatePlayer(playerClass, starter.Name, 40);
            character.RestoreProgress(ExperienceTable.ForLevel(40), 10000, 5, skillPoints: 40);
            var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, new Vector3(100, 0, 100)));
            var npc = new NpcDefinition(1, "Trainer", "trainer.inx", [new NpcService(NpcServiceKind.SkillMaster)]) { Dialog = ["Welcome"] };
            var empty = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(empty.Items, empty.Monsters, new NpcCatalog([npc]),
                    new Dictionary<int, FieldSpawnData> { [3] = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 100 * 256, 100 * 256, 1024)] } },
                    empty.Motions)
            };
            var catalog = new WorldCatalog();
            var world = new WorldServer(holder, catalog);
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "owner");
            var service = new SessionAppService(sessions, classes, repository, new ImmediateSaves(repository), catalog, world, holder);
            Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
            var outbox = new WorldOutbox();
            await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbox.Writer, session));
            Assert.True(outbox.Reader.TryRead(out var snapshot), "initial roster missing");
            var master = snapshot.Entities.Single(entity => entity.Kind == EntityKind.Npc).EntityId;
            var skill = ClassSkillCatalog.For(playerClass).Where(static skill => skill.Tier == JobTier.Job1).ToArray()[0];
            var learn = new ProgressionCommand(Guid.NewGuid(), 0, ProgressionAction.LearnSkill, SkillCode: skill.Code, NpcEntityId: master);
            Assert.True((await WorldPump.RunAsync(world, service.ApplyProgressionAsync(session.SessionToken, learn))).Ok == trains,
                $"Ricarten's master {(trains ? "refused" : "trained")} a {playerClass}");
        }
    }

    private static async Task DurabilityAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Training", 0, PlayerClass.Mechanician, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Mechanician, starter.Name, 40);
        character.RestoreProgress(ExperienceTable.ForLevel(40), 10000, 5, skillPoints: 40);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, new Vector3(100, 0, 100)));
        var npc = new NpcDefinition(1, "Trainer", "trainer.inx", [new NpcService(NpcServiceKind.SkillMaster)]) { Dialog = ["Welcome"] };
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, empty.Monsters, new NpcCatalog([npc]),
                new Dictionary<int, FieldSpawnData> { [3] = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 100 * 256, 100 * 256, 1024)] } }, empty.Motions)
        };
        var catalog = new WorldCatalog();
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, new ImmediateSaves(repository), catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
        var outbox = new WorldOutbox();
        var player = WorldPlayerFactory.Create(session);
        await WorldPump.RunAsync(world, world.JoinAsync(3, player, outbox.Writer, session));
        Assert.True(outbox.Reader.TryRead(out var snapshot), "initial roster missing");
        var placed = snapshot.Entities.Single(entity => entity.Kind == EntityKind.Npc);
        Assert.True(placed.Position == new Vector3(100, 0, 100) && Math.Abs(placed.Facing - MathF.PI / 2) < 0.001, "SPC fixed-point placement is wrong");
        Assert.True((await WorldPump.RunAsync(world, world.InteractNpcAsync(session, placed.EntityId))).Ok, "nearby NPC interaction rejected");
        var skill = ClassSkillCatalog.For(PlayerClass.Mechanician).Where(static skill => skill.Tier == JobTier.Job1).ToArray()[0];
        var command = new ProgressionCommand(Guid.NewGuid(), 0, ProgressionAction.LearnSkill, SkillCode: skill.Code, NpcEntityId: placed.EntityId);
        player.Teleport(new Vector3(1000, 0, 1000));
        Assert.True(!(await WorldPump.RunAsync(world, service.ApplyProgressionAsync(session.SessionToken, command))).Ok, "remote learning accepted");
        player.Teleport(new Vector3(100, 0, 100));
        var pending = service.ApplyProgressionAsync(session.SessionToken, command);
        world.Tick(0.05f, 0);
        Assert.True(!pending.IsCompleted && session.Character!.Skills.RankOf(skill.Code) == 0 && session.Character.Money == 10000, "uncommitted learning leaked");
        Assert.True(!(await service.ApplyInventoryAsync(session.SessionToken,
            new InventoryCommand(Guid.NewGuid(), 0, InventoryAction.Move, Guid.NewGuid(), 0, 0, default))).Ok, "inventory bypassed shared save gate");
        Assert.True((await WorldPump.RunAsync(world, pending)).Ok, "learning failed");
        Assert.True(session.Character!.Skills.RankOf(skill.Code) == 1 && session.Character.Money == 9500 && session.Character.SkillPoints == 39, "durable learning mismatch");
        var allocation = new ProgressionCommand(Guid.NewGuid(), 1, ProgressionAction.AllocateAttribute, CharacterAttribute.Strength, 1);
        var oldStrength = session.Character.Attributes.Strength;
        Assert.True((await WorldPump.RunAsync(world, service.ApplyProgressionAsync(session.SessionToken, allocation))).Ok, "attribute allocation failed");
        Assert.True(session.Character.Attributes.Strength == oldStrength + 1 && session.Character.StatPoints == 4, "attribute points not conserved");
        var writes = repository.UpdateCount;
        Assert.True((await WorldPump.RunAsync(world, service.ApplyProgressionAsync(session.SessionToken, command))).Ok && repository.UpdateCount == writes, "replay charged again");
        Assert.True(!(await WorldPump.RunAsync(world, service.ApplyProgressionAsync(session.SessionToken, command with { SkillCode = skill.Code + 1 }))).Ok, "conflicting UUID accepted");
        await WorldPump.RunAsync(world, world.LeaveAsync(3, session.EntityId, outbox.Writer));
        sessions.Remove(session.SessionToken);
        var next = sessions.Create(1, "reconnect");
        Assert.True((await service.EnterWorldAsync(next.SessionToken, starter.Name)).Ok && next.TryBeginPlay(), "reconnect failed");
        await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(next), new WorldOutbox().Writer, next));
        Assert.True((await WorldPump.RunAsync(world, service.ApplyProgressionAsync(next.SessionToken, command))).Ok && repository.UpdateCount == writes, "reconnect lost replay history");
        Assert.True(next.Character!.Skills.RankOf(skill.Code) == 1 && next.Character.Attributes.Strength == oldStrength + 1, "progression was not persisted");
        var owner = OwnerState.Capture(next.Character);
        Assert.True(owner.Skills.Single().Rank == 1 && ReferenceEquals(owner, OwnerState.Capture(next.Character, owner)), "owner rank projection is stale or uncached");
    }
}
