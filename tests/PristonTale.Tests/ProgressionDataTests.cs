using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ProgressionDataTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("later-tier class skills take the original codes and save their rank and use counter", AdvancedSkillsAsync);
        yield return ("item footprints follow the item table and a saved item whose footprint grew moves to a free place", FootprintsAsync);
        yield return ("the fifth tier opens its four skills, which share the elite points and continue the price table", FifthTierAsync);
    }

    private static Task FifthTierAsync()
    {
        var classes = new CharacterClassRegistry();
        var skills = classes.Get(PlayerClass.Mechanician).Skills;
        var landMining = skills[16];
        Assert.True(landMining.Code == 0x01100001 && landMining.Tier == JobTier.Job5 && landMining.RequiredLevel == 80, "the fifth tier's first skill moved");

        var player = classes.CreatePlayer(PlayerClass.Mechanician, "Fifth", 90);
        foreach (var skill in skills.Take(16))
        {
            player.Skills.Restore(skill.Code, 1, 0);
        }

        player.RestoreProgress(0, 1_000_000, tier: PlayerCharacter.LastTier - 1, eliteSkillPoints: 2);
        Assert.True(!SkillLearning.TryQuote(player, landMining.Code, out _, out _), "a fifth-tier skill was taught at the fourth tier");
        player.RestoreProgress(0, 1_000_000, tier: PlayerCharacter.LastTier, eliteSkillPoints: 2);
        Assert.True(SkillLearning.TryQuote(player, landMining.Code, out var price, out var elite) && price == 50_000 && elite,
            "a fifth-tier skill did not cost elite points and the continued price");
        Assert.True(SkillLearning.Price(19, 3) == 80_000 + 16_000 * 3, "the last fifth-tier price changed");
        Assert.True(!SkillLearning.TryQuote(player, skills[17].Code, out _, out _), "a fifth-tier skill skipped the one before it");
        player.RestoreProgress(0, 1_000_000, tier: PlayerCharacter.LastTier, eliteSkillPoints: 0);
        Assert.True(!SkillLearning.TryQuote(player, landMining.Code, out _, out _), "a fifth-tier skill was taught without elite points");
        Assert.True(MoneyLimits.CarryLimit(90, PlayerCharacter.LastTier) == 700_000_000 && player.ExperienceLimit == ExperienceTable.Ceiling,
            "the fifth tier's gold or experience limit changed");
        return Task.CompletedTask;
    }

    private static Task AdvancedSkillsAsync()
    {
        var classes = new CharacterClassRegistry();
        var mechanician = classes.Get(PlayerClass.Mechanician).Skills;
        Assert.True(mechanician.Single(skill => skill.Name == "Great Smash").Code == 0x01020001
            && mechanician.Single(skill => skill.Name == "Mechanic Weapon Mastery").Code == 0x01040003
            && mechanician.Single(skill => skill.Name == "Metal Golem").Code == 0x01080004
            && classes.Get(PlayerClass.Fighter).Skills.Single(skill => skill.Name == "Boost Health").Code == 0x02080004,
            "later-tier skills do not take the class group, tier bit and slot");
        Assert.True(mechanician.Count == 20 && mechanician.Select(skill => skill.Code).Distinct().Count() == 20, "class skill codes collide");

        var player = classes.CreatePlayer(PlayerClass.Mechanician, "Trained", 90);
        foreach (var skill in mechanician)
        {
            player.Skills.Restore(skill.Code, 10, LearnedSkills.MaxUses);
        }

        var starter = CharacterRecordFactory.NewStarter(classes, 1, player.Name, 0, PlayerClass.Mechanician, 3);
        var record = CharacterRecordFactory.FromLiveSession(starter, player, 3, System.Numerics.Vector3.Zero);
        var restored = classes.CreatePlayer(PlayerClass.Mechanician, "Trained", 90);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([]));
        Assert.True(mechanician.All(skill => restored.Skills.RankOf(skill.Code) == 10 && restored.Skills.UsesOf(skill.Code) == LearnedSkills.MaxUses),
            "ranks or use counters were not saved");

        var rejected = false;
        try
        {
            classes.CreatePlayer(PlayerClass.Assassin, "Uncoded").Skills.Restore(0, 1);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert.True(rejected, "a skill without a code was learned");
        return Task.CompletedTask;
    }

    private static Task FootprintsAsync()
    {
        var small = new ItemDefinition(0x0101_0001, "Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA102" };
        var other = new ItemDefinition(0x0101_0002, "Potion", ItemKind.Potion) { CatalogKey = "PL101" };
        var classes = new CharacterClassRegistry();
        var player = classes.CreatePlayer(PlayerClass.Fighter, "Packer", 10);
        var axe = new ItemInstance(small);
        var potion = new ItemInstance(other);
        Assert.True(player.Inventory.TryPlace(axe, 0) && player.Inventory.TryPlace(potion, 1), "fixture items were not placed");
        var starter = CharacterRecordFactory.NewStarter(classes, 1, player.Name, 0, PlayerClass.Fighter, 3);
        var record = CharacterRecordFactory.FromLiveSession(starter, player, 3, System.Numerics.Vector3.Zero);

        var grown = small with { GridWidth = 2, GridHeight = 3 };
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Packer", 10);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([grown, other]));
        var axeAnchor = restored.Inventory.Find(axe.InstanceId);
        var potionAnchor = restored.Inventory.Find(potion.InstanceId);
        Assert.True(axeAnchor == 0 && potionAnchor >= 0 && potionAnchor != 1, "the item under the grown footprint did not move");
        return Task.CompletedTask;
    }
}
