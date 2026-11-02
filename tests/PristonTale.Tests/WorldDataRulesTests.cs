using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The game rules of the world data keys: death gold, daily drop limits, item sets and the hammer event.
internal static class WorldDataRulesTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a monster's death gold falls on top of its drop table with its chance", DeathGoldAsync);
        yield return ("a daily-limited item stops dropping at its limit until the date changes", DailyLimitAsync);
        yield return ("an item set gives its bonus once its pieces are worn", SetBonusAsync);
        yield return ("ten hammer blows in a row give the player a big head and turn the monster into its event form", HammerAsync);
        yield return ("the game clock runs a day in 19.2 minutes with night from 23:00 to 4:00", ClockAsync);
        yield return ("night creatures walk at night and come back when killed, their NPCs leave, and both turn at dawn", NightCreaturesAsync);
        yield return ("a night creature opens at the field's first active spawn point a player is near, else at its first active point", NightCreaturePointsAsync);
        yield return ("a nightmare field spawns only at night, its people turn into the night creature and return at dawn", NightmareAsync);
        yield return ("a monster's magic resistance cuts a spell's damage from -100 (double) to 100 (only the least hit), not a weapon skill's", MagicResistanceAsync);
    }

    private static Task DeathGoldAsync()
    {
        var definition = Monster(1, MonsterClass.Normal) with
        {
            Drops = new DropTable([DropEntry.NothingSlot(100)], 1, false),
            DeathGold = new MonsterRange(8, 15),
            DeathGoldChance = 100,
            Combat = new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero)
        };
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(7));
        var player = new WorldPlayer(1, Vector3.Zero, new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Killer"), false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(24, 0, 0), -1, 0);
        Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "fixture target rejected");
        for (var index = 0; index < 400 && monster.IsAlive; index++)
        {
            field.Step(0.05f);
        }

        var drops = field.GroundLoot.Snapshot();
        Assert.True(!monster.IsAlive && drops.Length == 1 && drops[0].View.Gold && drops[0].View.Amount is >= 8 and <= 15,
            "the death gold did not fall in its range");
        return Task.CompletedTask;
    }

    private static Task DailyLimitAsync()
    {
        var limited = new ItemDefinition(ItemCodes.Parse("DA116"), "Limited", ItemKind.Armor) { CatalogKey = "DA116", DailyDropLimit = 2 };
        var free = new ItemDefinition(ItemCodes.Parse("DA115"), "Free", ItemKind.Armor) { CatalogKey = "DA115" };
        var clock = new ManualClock();
        var ledger = new DailyDropLedger(new ItemCatalog([limited, free]), clock);
        Assert.True(ledger.TryTake(limited.Code) && ledger.TryTake(limited.Code) && !ledger.TryTake(limited.Code),
            "the daily limit was not kept");
        Assert.True(Enumerable.Range(0, 10).All(_ => ledger.TryTake(free.Code)), "an unlimited item was limited");
        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(ledger.TryTake(limited.Code), "the limit did not reset with the date");
        return Task.CompletedTask;
    }

    private static Task SetBonusAsync()
    {
        var set = new ItemSetDefinition(1, 2, 400, 2f, 1.5f, 0f, 0f, 0f);
        var axe = new ItemDefinition(ItemCodes.Parse("WA130"), "Verus Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA130", SetCode = 1 };
        var armor = new ItemDefinition(ItemCodes.Parse("DA302"), "Verus Armor", ItemKind.Armor, EquipmentSlot.Armor) { CatalogKey = "DA302", SetCode = 1 };
        var catalog = new ItemCatalog([axe, armor], sets: [set]);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Wearer", 50);
        Assert.True(Equip(character, new ItemInstance(catalog.Get(axe.Code)), EquipmentSlot.Weapon), "the axe was not worn");
        var one = character.EquipmentTotals;
        Assert.True(one.AddLife == 0 && one.Block == 0, "one piece gave the set bonus");
        Assert.True(Equip(character, new ItemInstance(catalog.Get(armor.Code)), EquipmentSlot.Armor), "the armor was not worn");
        var both = character.EquipmentTotals;
        Assert.True(both.AddLife == 400 && Math.Abs(both.Block - 1.5f) < 0.001f && Math.Abs(both.Absorb - one.Absorb - 2f) < 0.001f,
            "two pieces did not give the set bonus");
        return Task.CompletedTask;
    }

    private static Task HammerAsync()
    {
        var hammer = Monster(1, MonsterClass.Hammer) with { EventModelInxPath = "char\\monster\\Hob\\Hob-b.inx", EventFormCode = 2 };
        var form = hammer with { Code = 2, Class = MonsterClass.Normal, ModelInxPath = hammer.EventModelInxPath, EventModelInxPath = "", EventFormCode = 0 };
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([hammer, form]), new EntityIdAllocator(), new CombatRng(11));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Target", 100);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(hammer, new Vector3(20, 0, 0), -1, 0);
        for (var index = 0; index < 4000 && character.Premium.BigHeadMode == 0; index++)
        {
            character.Life = character.Derived.MaxLife;
            field.Step(0.05f);
        }

        Assert.True(character.Premium.BigHeadMode is 1 or 2, "ten hammer blows gave no big head");
        field.Step(0.05f);
        var now = field.MonstersForInspection.Single();
        Assert.True(now.Def.Code == form.Code && now.HammerSpent && now.EntityId != monster.EntityId, "the monster did not take its event form");
        return Task.CompletedTask;
    }

    private static Task ClockAsync()
    {
        Assert.True(WorldClock.HoursAt(0) == 0f && Math.Abs(WorldClock.HoursAt(60L * WorldClock.MinuteMs) - 1f) < 0.0001f
            && Math.Abs(WorldClock.HoursAt(WorldClock.MinutesPerDay * (long)WorldClock.MinuteMs + 30L * WorldClock.MinuteMs) - 0.5f) < 0.0001f,
            "the game clock does not run 800 ms a minute");
        Assert.True(WorldClock.IsNight(23f) && WorldClock.IsNight(3.99f) && !WorldClock.IsNight(4f) && !WorldClock.IsNight(22.99f),
            "night does not run from 23:00 to 4:00");
        return Task.CompletedTask;
    }

    private static Task NightCreaturesAsync()
    {
        var wolverine = Monster(1, MonsterClass.Normal) with { EventCode = 100, EventInfo = 1, SoundCode = 0x11E0, Ai = MonsterAi.Default };
        var pet = new NpcDefinition(1, "Wolverine pet", "", [new NpcService(NpcServiceKind.EventMonster, 100, 1)]);
        var data = new FieldSpawnData([new SpawnPoint(500, 500, true)], [], [], [new FixedNpc(1, 0, 0, 0)]);
        var field = new FieldInstance(1, data, new MonsterCatalog([wolverine]), new EntityIdAllocator(), new CombatRng(3), npcs: new NpcCatalog([pet]));
        field.Step(0.05f);
        Assert.True(!field.MonstersForInspection.Any() && field.Npcs.Count == 1, "a night creature walked by day or its NPC left");

        field.Night = true;
        field.Step(0.05f);
        var walking = field.MonstersForInspection.Single();
        Assert.True(walking.Def.Code == wolverine.Code && Vector3.Distance(walking.Position, new Vector3(500, 0, 500)) < 64 && field.Npcs.Count == 0,
            "the night creature did not walk at its point, or its NPC stayed");

        walking.ApplyDamage(walking.Life);
        field.Combat.BeginCorpse(walking.EntityId);
        for (var index = 0; index < 200 && field.MonstersForInspection.All(monster => monster.EntityId == walking.EntityId); index++)
        {
            field.Step(0.05f);
        }

        var again = field.MonstersForInspection.Single();
        Assert.True(again.EntityId != walking.EntityId && again.IsAlive, "a killed night creature did not come back");

        field.Night = false;
        field.Step(0.05f);
        Assert.True(again.VanishOnWarp && again.Motion == MotionState.Warp && field.Npcs.Count == 1, "dawn did not warp the creature or return its NPC");
        for (var index = 0; index < 200 && field.MonstersForInspection.Any(); index++)
        {
            field.Step(0.05f);
        }

        Assert.True(!field.MonstersForInspection.Any(), "the night creature did not leave after its warp");
        return Task.CompletedTask;
    }

    private static Task NightCreaturePointsAsync()
    {
        var wolverine = Monster(1, MonsterClass.Normal) with { EventCode = 100, EventInfo = 1, SoundCode = 0x11E0, Ai = MonsterAi.Default };
        var points = new[] { new SpawnPoint(900, 900, false), new SpawnPoint(500, 500, true), new SpawnPoint(4000, 4000, true), new SpawnPoint(8000, 8000, true) };
        var data = new FieldSpawnData(points, [], [], []);
        var empty = new FieldInstance(1, data, new MonsterCatalog([wolverine]), new EntityIdAllocator(), new CombatRng(3)) { Night = true };
        empty.Step(0.05f);
        Assert.True(Vector3.Distance(empty.MonstersForInspection.Single().Position, new Vector3(500, 0, 500)) < 64,
            "with nobody near, the creature did not open at the field's first active point");

        var watched = new FieldInstance(1, data, new MonsterCatalog([wolverine]), new EntityIdAllocator(), new CombatRng(3)) { Night = true };
        watched.Add(new WorldPlayer(1, new Vector3(8000, 0, 8100), new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Hunter", 30), false, default));
        watched.Step(0.05f);
        var creature = watched.MonstersForInspection.Single(monster => monster.Def.Code == wolverine.Code);
        Assert.True(Vector3.Distance(creature.Position, new Vector3(8000, 0, 8000)) < 64,
            $"the creature opened at {creature.Position}, not at the active point near the player");
        return Task.CompletedTask;
    }

    private static Task NightmareAsync()
    {
        var creature = Monster(1, MonsterClass.Normal) with { ModelInxPath = "char\\monster\\Wolverin\\Wolverin_A.inx" };
        var bandit = Monster(2, MonsterClass.Normal) with { Ai = MonsterAi.Default };
        var villager = new NpcDefinition(1, "Villager", "", []);
        var data = new FieldSpawnData([new SpawnPoint(800, 800, true)], [new SpawnTableEntry(2, 100)], [], [new FixedNpc(1, 100 * 256, 100 * 256, 0)])
        {
            MaxConcurrentMonsters = 5
        };
        var field = new FieldInstance(9, data, new MonsterCatalog([creature, bandit]), new EntityIdAllocator(), new CombatRng(5),
            npcs: new NpcCatalog([villager])) { Event = FieldEventKind.Nightmare };
        var player = new WorldPlayer(1, new Vector3(800, 0, 800), new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Watcher", 100), false,
            default);
        field.Add(player);
        for (var index = 0; index < 200; index++)
        {
            field.Step(0.05f);
        }

        Assert.True(!field.MonstersForInspection.Any() && field.Npcs.Count == 1, "a nightmare field spawned by day");
        field.Night = true;
        field.Step(0.05f);
        Assert.True(field.Npcs.Count == 0 && field.MonstersForInspection.Any(monster => monster.Def.Code == creature.Code
                && Vector3.Distance(monster.Position, new Vector3(100, 0, 100)) < 64),
            "the villager did not turn into the night creature where it stood: npcs " + field.Npcs.Count + ", monsters "
            + string.Join(";", field.MonstersForInspection.Select(monster => monster.Def.Code + "@" + monster.Position)));

        field.Night = false;
        field.Step(0.05f);
        Assert.True(field.Npcs.Count == 1 && field.MonstersForInspection.All(monster => !monster.IsAlive), "dawn did not end the night");
        return Task.CompletedTask;
    }

    private static Task MagicResistanceAsync()
    {
        var spell = new Simulation.Skills.SkillDamageRule { SpiritMinDivisor = 4, SpiritMaxDivisor = 2 };
        var blade = new Simulation.Skills.SkillDamageRule();
        var none = Simulation.Modifiers.ModifierTotals.Empty;
        int Against(Simulation.Skills.SkillDamageRule rule, int resistance) =>
            Simulation.Skills.SkillFormulas.PowerAgainst(400, rule, Monster(1, MonsterClass.Normal) with { MagicResistance = resistance }, none, none, 1);
        Assert.True(spell.Magic && !blade.Magic, "a spell or a weapon skill is classed wrongly");
        Assert.True(Against(spell, 0) == 400 && Against(spell, 50) == 200 && Against(spell, 100) == 1 && Against(spell, -100) == 800
            && Against(spell, 150) == 1, $"magic resistance does not scale a spell's damage: {Against(spell, 0)} {Against(spell, 50)} {Against(spell, 100)} {Against(spell, -100)} {Against(spell, 150)}");
        Assert.True(Against(blade, 50) == 400, "magic resistance cut a weapon skill");
        return Task.CompletedTask;
    }

    private static MonsterDefinition Monster(int code, MonsterClass monsterClass) => new(code, "Hammer fixture", MonsterFaction.Enemy, monsterClass,
        MonsterBrood.Normal, new MonsterCombatStats(60, 100000, new MonsterRange(1, 1), 9, 60, 0, 0, 0, 5000, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(5, MonsterNature.Evil, 400, 1, 100, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static bool Equip(PlayerCharacter character, ItemInstance item, EquipmentSlot slot)
    {
        for (var cell = 0; cell < character.Inventory.Capacity; cell++)
        {
            if (character.Inventory.TryPlace(item, cell))
            {
                return character.TryEquipFromInventory(cell, slot);
            }
        }

        return false;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 22, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
