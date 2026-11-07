using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Ai;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Characters, items and quest contexts for the quest regression cases.
internal static class QuestFixture
{
    public static readonly CharacterClassRegistry Classes = new();
    public static readonly ItemCatalog Items = CreateItems();

    public static uint Code(string key) => ItemCodes.Parse(key);

    public static PlayerCharacter Character(PlayerClass playerClass, string name, int level, short tier = 0, long money = 0)
    {
        var character = Classes.CreatePlayer(playerClass, name, level);
        character.RestoreProgress(0, money, tier: tier);
        var skills = character.Class.Skills;
        var opened = Math.Min(SkillLearning.OpenedBy(tier), skills.Count);
        for (var index = 0; index < opened; index++)
        {
            if (skills[index].Code != 0)
            {
                character.Skills.Restore(skills[index].Code, 1);
            }
        }

        return character;
    }

    public static QuestContext Context(PlayerCharacter character, QuestNpc npc = default, int field = 3, long now = 0, int seed = 1) =>
        new(character, npc, new CatalogQuestItemSource(Items), new SeededSimRng(seed), now, field);

    public static QuestContext Talk(PlayerCharacter character, QuestNpc npc, int field = 3, long now = 0)
    {
        var context = Context(character, npc, field, now);
        Assert.True(QuestEngine.Run(context, Command(QuestAction.Talk)), "a talk failed");
        return context;
    }

    public static QuestContext Run(PlayerCharacter character, QuestNpc npc, QuestAction action, QuestCode code, int state, int choice = 0, int field = 3,
        long now = 0)
    {
        var context = Context(character, npc, field, now);
        QuestEngine.Run(context, Command(action, code, state, choice));
        return context;
    }

    public static QuestContext Expect(PlayerCharacter character, QuestNpc npc, QuestAction action, QuestCode code, int state, string failure,
        int choice = 0, int field = 3, long now = 0)
    {
        var context = Context(character, npc, field, now);
        Assert.True(QuestEngine.Run(context, Command(action, code, state, choice)), $"{failure}: {context.Status} {context.Message}");
        return context;
    }

    public static QuestCommand Command(QuestAction action, QuestCode code = QuestCode.None, int state = 0, int choice = 0) =>
        new(Guid.NewGuid(), 1, action, code, state, choice);

    public static void Give(PlayerCharacter character, uint code, int count = 1)
    {
        Assert.True(Items.TryGet(code, out var definition), $"fixture item {code:X8} missing");
        Assert.True(QuestInventory.TryGiveAll(character, [new ItemInstance(definition, count)]), "fixture pack is full");
    }

    // Kills credited on the world tick.
    public static QuestContext Kill(PlayerCharacter character, int monsterCode, int times = 1, int field = 3)
    {
        var context = Context(character, QuestNpc.None, field);
        for (var kill = 0; kill < times; kill++)
        {
            QuestTick.OnKill(context, monsterCode);
        }

        return context;
    }

    // The quest upkeep storing drops of the given codes.
    public static void Drop(PlayerCharacter character, uint code, int count = 1, int monsterCode = 0, QuestDropSource source = QuestDropSource.MainTable,
        long now = 0)
    {
        QuestDrops.Upkeep(Context(character, QuestNpc.None, now: now), [new PendingQuestDrop(code, count, monsterCode, source)]);
    }

    private static ItemCatalog CreateItems()
    {
        var definitions = new List<ItemDefinition>();
        foreach (var key in new[]
                 {
                     "QT101", "QT102", "QT103", "QT104", "QT105", "QT106", "QT107", "QT108", "QT109", "QT110", "QT111", "QT112", "QT113", "QT114",
                     "QT115", "QT116", "QT125", "QT126", "QT127", "QT128", "QT129", "QT130", "QT131", "QT132", "QT133", "QT136", "QT137", "QT138",
                     "QT139", "QT140", "QT141", "QT142", "QT143"
                 })
        {
            definitions.Add(new ItemDefinition(Code(key), key, ItemKind.Quest) { CatalogKey = key });
        }

        definitions.Add(new ItemDefinition(Code("MA101"), "Glass Bottle", ItemKind.Material) { CatalogKey = "MA101" });
        definitions.Add(new ItemDefinition(Code("MA201"), "Honey", ItemKind.Material) { CatalogKey = "MA201" });
        definitions.Add(new ItemDefinition(Code("MA202"), "Oil", ItemKind.Material) { CatalogKey = "MA202" });
        for (var crystal = 1; crystal <= 8; crystal++)
        {
            var key = $"GP1{crystal:D2}";
            definitions.Add(new ItemDefinition(Code(key), key, ItemKind.Misc) { CatalogKey = key });
        }

        definitions.Add(new ItemDefinition(Code("GP141"), "Dragon Crystal", ItemKind.Misc) { CatalogKey = "GP141" });

        for (var wing = 1; wing <= 6; wing++)
        {
            var key = $"QW10{wing}";
            definitions.Add(new ItemDefinition(Code(key), key, ItemKind.Misc) { CatalogKey = key });
        }

        foreach (var key in new[] { "PL101", "PS101", "PM101", "PL103", "PS103", "PM103" })
        {
            definitions.Add(new ItemDefinition(Code(key), key, ItemKind.Potion, MaxStack: 100) { CatalogKey = key });
        }

        foreach (var key in new[] { "WA102", "WA108", "WA120", "WC120", "WH121", "WP121", "WS122", "WS223", "WT121", "WM121" })
        {
            definitions.Add(new ItemDefinition(Code(key), key, ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3) { CatalogKey = key });
        }

        // Heavier than any character can carry.
        definitions.Add(new ItemDefinition(Code("WA197"), "Anvil", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3)
        {
            CatalogKey = "WA197",
            Weight = 100_000
        });

        // A weapon strong enough to bring an arena master down quickly.
        definitions.Add(new ItemDefinition(Code("WA199"), "Trial Axe", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3)
        {
            CatalogKey = "WA199",
            BaseStats = ItemStatBlock.Zero with { DamageMin = 150, DamageMax = 150, AttackRating = 5000 }
        });
        definitions.Add(new ItemDefinition(Code("OA202"), "OA202", ItemKind.Amulet, EquipmentSlot.Amulet) { CatalogKey = "OA202" });
        definitions.Add(new ItemDefinition(Code("OA106"), "OA106", ItemKind.Amulet, EquipmentSlot.Amulet) { CatalogKey = "OA106" });
        definitions.Add(new ItemDefinition(Code("OR106"), "OR106", ItemKind.Ring, EquipmentSlot.RingLeft) { CatalogKey = "OR106" });
        definitions.Add(new ItemDefinition(Code("OR203"), "OR203", ItemKind.Ring, EquipmentSlot.RingLeft) { CatalogKey = "OR203" });
        definitions.Add(new ItemDefinition(Code("FO104"), "FO104", ItemKind.ForceOrb) { CatalogKey = "FO104" });
        definitions.Add(new ItemDefinition(Code("FO106"), "FO106", ItemKind.ForceOrb) { CatalogKey = "FO106" });
        var variants = new[]
        {
            new ItemDefinition(Code("WA108"), "Quest Axe", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3)
            {
                CatalogKey = "WA108", UniqueItem = ItemDefinition.QuestUnique
            },
            new ItemDefinition(Code("OR203"), "Emperor Ring", ItemKind.Ring, EquipmentSlot.RingLeft) { CatalogKey = "OR203", UniqueItem = ItemDefinition.QuestUnique },
            new ItemDefinition(Code("WA120"), "Minotaur Axe", ItemKind.Axe, EquipmentSlot.Weapon, GridWidth: 2, GridHeight: 3)
            {
                CatalogKey = "WA120", UniqueItem = ItemDefinition.QuestUnique
            }
        };
        return new ItemCatalog(definitions, variants);
    }
}
