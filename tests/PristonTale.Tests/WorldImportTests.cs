using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.WorldImport;
using PristonTale.Assets;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

internal static class WorldImportTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("world import follows linked data, retains variants and unresolved references; a drop line's weight is shared by the items the catalog knows, three bonus lines roll, and a boss line takes the last file of its name", ImportAsync);
        yield return ("every key of the monster, item and NPC files reaches the world data, and unknown keys are reported", EveryKeyAsync);
        yield return ("set bonuses are read from the comment that states them", SetBonusAsync);
        yield return ("names the data gives only in Portuguese are imported in English", EnglishNamesAsync);
        yield return ("every monster and NPC has one id and one English key, and every monster a crystal or a skill calls is registered", RegistryAsync);
        yield return ("a named unique keeps its number by code and name, and the registry refuses a number or name given twice", NamedUniqueRegistryAsync);
    }

    private static Task SetBonusAsync()
    {
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("체력+400", out var life) && life.Life == 400, "a life bonus was not read");
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("흡수력, 블럭율+2.0", out var guard) && guard.Absorb == 2f && guard.Block == 2f,
            "a value shared by two terms was not read");
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("체, 근, 기, 재생+2.0", out var regen) && regen.LifeRegen == 2f && regen.StaminaRegen == 2f
            && regen.ManaRegen == 2f && regen.Life == 0, "abbreviated regeneration was not read");
        Assert.True(WorldImport.Parsers.SetBonusParser.TryParse("체력+200, 흡수력+20, 근기생 재생+30", out var full) && full.Life == 200 && full.Absorb == 20f
            && full.LifeRegen == 30f && full.ManaRegen == 30f && full.StaminaRegen == 30f, "a mixed bonus was not read");
        Assert.True(!WorldImport.Parsers.SetBonusParser.TryParse("행운+3", out _) && !WorldImport.Parsers.SetBonusParser.TryParse("", out _), "an unknown bonus was accepted");
        return Task.CompletedTask;
    }

    private static Task NamedUniqueRegistryAsync()
    {
        var registry = new NamedUniqueRegistry([new("DA112", 1, "Vega"), new("DA112", 2, "Transcendence")]);
        Assert.True(registry.Resolve("da112", "Vega") == 1 && registry.Resolve("DA112", "Wings") == 3 && registry.Resolve("WA101", "Axe") == 1,
            "a code in another case lost its number, or a new unique did not take the next number of its code");
        Assert.True(registry.Unmet.Single() is { Code: "DA112", Number: 2 } && registry.Added.Count == 2, "met and unmet entries were mixed up");
        Assert.Throws<InvalidDataException>(() => new NamedUniqueRegistry([new("DA112", 1, "Vega"), new("da112", 1, "Other")]), "a number given twice was accepted");
        Assert.Throws<InvalidDataException>(() => new NamedUniqueRegistry([new("DA112", 1, "Vega"), new("DA112", 2, "Vega")]), "a name given twice was accepted");
        return Task.CompletedTask;
    }

    private static async Task EveryKeyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-world-keys-{Guid.NewGuid():N}");
        var source = Path.Combine(directory, "GameServer");
        Directory.CreateDirectory(Path.Combine(source, "OpenItem"));
        Directory.CreateDirectory(Path.Combine(source, "Monster"));
        Directory.CreateDirectory(Path.Combine(source, "NPC"));
        await WriteCp949Async(Path.Combine(source, "OpenItem", "WA130.txt"), Lines("*코드 WA130", "*이름 \"Verus Axe\"", "*E_NAME \"Verus Axe\"",
            "*B_NAME \"Machado\"", "*J_NAME \"x\"", "*유니크색상 220 80 0 0 9 1.5", "*이펙트설정 1", "*발생제한 5", "//////// 세트 효과 ////////",
            "// 체력+400", "*세트아이템 1 2"));
        await WriteCp949Async(Path.Combine(source, "OpenItem", "_Uwa130_Rage.txt"),
            Lines("*코드 WA130", "*Name \"Rage\"", "*유니크", "*유니크색상 0 180 255 0 9"));
        // A named unique keeps its registered number whatever its path order; one the registry lacks takes the next.
        await WriteCp949Async(Path.Combine(source, "OpenItem", "WA105.txt"), Lines("*코드 WA105", "*Name \"Hand Axe\""));
        await WriteCp949Async(Path.Combine(source, "OpenItem", "_Uwa105_A.txt"), Lines("*코드 WA105", "*Name \"Blade Crasher\"", "*유니크"));
        await WriteCp949Async(Path.Combine(source, "OpenItem", "_Uwa105_B.txt"), Lines("*코드 WA105", "*Name \"Bone Crasher\"", "*유니크"));
        await WriteCp949Async(Path.Combine(source, "Monster", "Wolverine.inf"), Lines("*이름 \"Wolverine\"", "*E_NAME \"Bronze Wolverine\"",
            "*속성 적", "*크기 중대형", "*매직 -5", "*이동타입 2", "*특수공격률 20", "*골드 8 15 80", "*보스",
            @"*예비모델 ""char\monster\Wolf\Wolf-b.ini""", "*이벤트코드 100", "*이벤트정보 1", "*언데드 유", "*수수께끼 1"));
        await WriteCp949Async(Path.Combine(source, "NPC", "Guard.npc"),
            Lines("*이름 \"Guard\"", "*모양파일 \"guard.ini\"", "*속성 적", "*레벨 100", "*크기 대형", "*출현간격 1 2"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var report = await new Importer(provider.GetRequiredService<IDbConnectionFactory>(), source).RunAsync();
            Assert.True(report.Warnings.Any(static warning => warning.Contains("unknown key monster *수수께끼", StringComparison.Ordinal))
                && !report.Warnings.Any(static warning => warning.Contains("unknown key", StringComparison.Ordinal) && !warning.Contains("*수수께끼", StringComparison.Ordinal)),
                "unknown keys were not reported, or known keys were: " + string.Join(" | ", report.Warnings.Where(static w => w.Contains("unknown", StringComparison.Ordinal))));

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var itemRecords = await repository.GetAllItemTemplatesAsync();
            var items = WorldDataFactory.ToItemDefinitions(itemRecords);
            var axe = items.Single(static item => item.CatalogKey == "WA130");
            Assert.True(axe.Glow == new ItemGlow(220, 80, 0, 0, 9, 1.5f) && axe.DisplayEffect == 1 && axe.DailyDropLimit == 5 && axe.SetCode == 1,
                "the glow, effect setting, daily limit or set of an item was lost");
            var uniques = WorldDataFactory.ToNamedUniqueDefinitions(itemRecords);
            var rage = uniques.Single(static unique => unique.Name == "Rage");
            Assert.True(rage.UniqueVariant == 1 && rage.Code == axe.Code && rage.SavedUnique == ItemDefinition.NamedUniqueBase + 1,
                "the named unique was not kept as a variant");
            Assert.True(uniques.Single(static unique => unique.Name == "Bone Crasher").UniqueVariant == 1
                && uniques.Single(static unique => unique.Name == "Blade Crasher").UniqueVariant == 2,
                "a registered named unique lost its number, or an unregistered one did not take the next");
            Assert.True(report.Warnings.Count(static warning => warning.StartsWith("Identities/named-uniques.json lacks", StringComparison.Ordinal)) == 2,
                "the import did not list the named uniques to register");
            var set = WorldDataFactory.ToItemSets(await repository.GetAllItemSetsAsync()).Single();
            Assert.True(set.SetCode == 1 && set.Pieces == 2 && set.Life == 400, "the set bonus was not imported");

            var monsters = WorldDataFactory.ToMonsterDefinitions(await repository.GetAllMonstersAsync());
            var monster = monsters[0];
            Assert.True(monsters.Count == 2 && monster.EventFormCode == monsters[1].Code && monsters[1].Class == Simulation.World.Monsters.MonsterClass.Normal
                && monsters[1].ModelInxPath.EndsWith("Wolf-b.inx", StringComparison.OrdinalIgnoreCase), "the event form of a monster was not built");
            Assert.True(monster.Name == "Bronze Wolverine" && monster.IsHostile && monster.ShadowSizeLevel == 2 && monster.MagicResistance == -5
                && monster.MoveType == 2 && monster.SpecialAttackPercent == 20 && monster.DeathGold == new Simulation.World.Monsters.MonsterRange(8, 15)
                && monster.DeathGoldChance == 80 && monster.IsBoss && monster.EventModelInxPath.EndsWith("Wolf-b.inx", StringComparison.OrdinalIgnoreCase)
                && monster.EventCode == 100 && monster.EventInfo == 1 && monster.Brood == Simulation.World.Monsters.MonsterBrood.Undead,
                "a monster key was not imported");

            var npc = WorldDataFactory.ToNpcDefinitions(await repository.GetAllNpcsAsync()).Single();
            Assert.True(npc.Hostile && npc.Level == 100 && npc.ShadowSizeLevel == 3 && npc.AppearSlot == 1 && npc.AppearCycle == 2
                && npc.AppearsFor(3) && !npc.AppearsFor(4), "an NPC key was not imported");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ImportAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-world-import-{Guid.NewGuid():N}");
        var source = Path.Combine(directory, "GameServer");
        var itemDirectory = Path.Combine(source, "OpenItem");
        var nameDirectory = Path.Combine(itemDirectory, "name");
        var monsterDirectory = Path.Combine(source, "Monster");
        var npcDirectory = Path.Combine(source, "NPC");
        Directory.CreateDirectory(nameDirectory);
        Directory.CreateDirectory(monsterDirectory);
        Directory.CreateDirectory(Path.Combine(npcDirectory, "name"));

        await WriteCp949Async(Path.Combine(itemDirectory, "2_WA101.txt"), "*코드 WA101\n*이름 \"Variant Axe\"\n");
        await WriteCp949Async(Path.Combine(itemDirectory, "WA101.txt"), "*코드 WA101\n*이름 \"Original Axe\"\n*연결파일 name\\WA101.zhoon\n");
        await WriteCp949Async(Path.Combine(itemDirectory, "BI319.txt"), "*코드 BI319\n*이름 \"Unmapped Blue Stone\"\n");
        await WriteCp949Async(Path.Combine(nameDirectory, "wa101.ZHOON"),
            "*NAME \"Linked Axe\"\n*최대수량 20\n*보유공간 3\n*마법기술숙련도 1.5\n");
        await WriteCp949Async(Path.Combine(monsterDirectory, "Wolf.inf"),
            "*이름 \"Wolf\"\n*Name \"WOLF\"\n*화면보정 -3 30\n*모델 \"wolf.ini\"\n*효과음 WOLVERLIN\n*모델크기 1.4\n*이동속도 12\n*아이템 100 ZZ999\n*아이템 100 BI319\n"
            + "*아이템 7 BI319 ZZ999 WA101\n*추가아이템 10 BI319\n*추가아이템 20 ZZ999\n*추가아이템 30 BI319\n*추가아이템 40 WA101\n*추가아이템 50 BI319\n");
        // A second file of the name, loaded after the first ("_" sorts after the letters): the boss line's master and escort.
        await WriteCp949Async(Path.Combine(monsterDirectory, "Wolf_Alpha.inf"), "*이름 \"Wolf\"\n*Name \"Lobo\"\n*모델 \"wolf.ini\"\n*레벨 30\n");
        Directory.CreateDirectory(Path.Combine(source, "Field"));
        await WriteCp949Async(Path.Combine(source, "Field", "den.ase.spm"), "*출연자 \"Wolf\" 10\n*출연자두목 \"Wolf\" \"Wolf\" 2 12\n");
        await WriteCp949Async(Path.Combine(npcDirectory, "Merchant.npc"),
            "*이름 \"상인\"\n*모델 \"merchant.ini\"\n*화면보정 0 70\n*효과음 WOLVERLIN\n*무기판매 WA101 BI319 ZZ999\n*연결파일 name\\missing.zhoon\n");
        // A variant with the same authored name whose linked text supplies the localized name.
        await WriteCp949Async(Path.Combine(npcDirectory, "Merchant2.npc"),
            "*이름 \"상인\"\n*모델 \"merchant.ini\"\n*Name \"Mr Lim\"\n*연결파일 name\\merchant.zhoon\n");
        await WriteCp949Async(Path.Combine(npcDirectory, "name", "merchant.zhoon"), "*B_NAME \"Travelling Merchant\"\n");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));

        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            var importer = new Importer(connections, source);
            var first = await importer.RunAsync();
            // A monster the source no longer defines (or one keyed again under another id) leaves on the next import.
            await using (var stale = await connections.OpenAsync())
            {
                await stale.ExecuteAsync("INSERT INTO monsters (id, key) VALUES (9999, 'Stale')");
            }

            var second = await importer.RunAsync();

            Assert.True(first.ItemRows == 2 && second.ItemRows == 2, "the base items were not selected consistently");
            Assert.True(first.SourceFiles == 10 && first.EntityVariants == 7, "an authored file or variant was omitted");
            Assert.True(first.UnresolvedReferenceRows == 4 && second.UnresolvedReferenceRows == 4,
                "unresolved references were dropped or duplicated");

            await using var connection = await connections.OpenAsync();
            var item = await connection.QuerySingleAsync<(string Name, int MaxStack, int PotionSpace, float MagicMastery)>(
                """
                SELECT t.name, d.max_stack, d.potion_space, d.magic_mastery_min FROM item_definitions d
                JOIN item_texts t ON t.definition_id = d.id AND t.language = 'en' WHERE d.item_code = 'WA101' AND d.variant = 0
                """);
            Assert.True(item.Name == "Linked Axe", "linked display name was not imported");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE code IN ('WA101', 'BI319')") == 2,
                "an item of a family without other items is missing");
            Assert.True(item.MaxStack == 20 && item.PotionSpace == 3 && Math.Abs(item.MagicMastery - 1.5f) < 0.001f,
                "linked item properties were not imported");

            var monster = await connection.QuerySingleAsync<(int SoundCode, float ModelScale, int MoveSpeed, int SizeLevel)>(
                "SELECT sound_code, model_scale, move_speed, size_level FROM monsters WHERE key = 'WOLF'");
            Assert.True(monster.SizeLevel == 0, "a monster without a size key does not cast the small shadow");
            Assert.True(monster.SoundCode == CharacterSoundCodes.Wolverine, "monster sound code was not imported");
            Assert.True(Math.Abs(monster.ModelScale - 1.4f) < 0.001f && monster.MoveSpeed == 12,
                "monster scale or movement was not imported");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_import_unresolved_refs WHERE raw_reference = 'ZZ999'") == 4,
                "unresolved source references were not preserved");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM world_entity_variants WHERE kind = 'item' AND is_catalog_entry = 0") == 1,
                "the duplicate item definition was lost");
            var merchantJson = await connection.ExecuteScalarAsync<string>(
                "SELECT data_json FROM world_entity_variants WHERE kind = 'npc' AND source_path LIKE '%Merchant.npc'");
            Assert.True(merchantJson is not null && merchantJson.Contains("\"Item3\":\"WA101\"", StringComparison.Ordinal),
                "the preserved NPC definition lost its shop stock");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "world import broke foreign keys");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM monsters WHERE id = 9999") == 0,
                "a monster the source no longer defines stayed in the catalog");
            var den = await connection.QuerySingleAsync<(string Spawned, string Boss, string Escort)>(
                """
                SELECT s.key, b.key, e.key FROM field_monster_spawns f JOIN monsters s ON s.id = f.monster_id
                JOIN field_bosses fb ON fb.stage = f.stage JOIN monsters b ON b.id = fb.monster_id JOIN monsters e ON e.id = fb.escort_monster_id
                WHERE f.stage = 'den'
                """);
            Assert.True(den == ("WOLF", "WOLF 2", "WOLF 2")
                && await connection.ExecuteScalarAsync<string>("SELECT t.name FROM monster_texts t JOIN monsters m ON m.id = t.monster_id WHERE m.key = 'WOLF 2'") == "WOLF",
                $"the spawn did not take the first file of its name, or the boss line the last, under the first file's English name: {den}");

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var items = WorldDataFactory.ToItemDefinitions(await repository.GetAllItemTemplatesAsync());
            var monsters = WorldDataFactory.ToMonsterDefinitions(await repository.GetAllMonstersAsync());
            var monstersWithCatalog = WorldDataFactory.ToMonsterDefinitions(
                await repository.GetAllMonstersAsync(),
                items.ToDictionary(static item => item.CatalogKey, static item => item.Code, StringComparer.OrdinalIgnoreCase));
            var npcs = WorldDataFactory.ToNpcDefinitions(await repository.GetAllNpcsAsync());
            Assert.True(items.Count == 2 && items.Single(static item => item.CatalogKey == "WA101").MaxStack == 20,
                "runtime item catalog lost the imported stack size or an item family");
            var wolf = monsters.Single(static monster => monster.Key == "WOLF");
            Assert.True(monsters.Count == 2 && wolf.SoundCode == CharacterSoundCodes.Wolverine,
                "runtime monster catalog lost the imported sound code");
            Assert.True(Math.Abs(wolf.ModelScale - 1.4f) < 0.001f,
                "runtime monster catalog lost the imported model scale");
            var drops = monstersWithCatalog.Single(static monster => monster.Key == "WOLF").Drops;
            Assert.True(drops.MainEntries.Select(static entry => (entry.ItemCode, entry.Weight))
                    .SequenceEqual([(ItemCodes.Parse("BI319"), 100), (ItemCodes.Parse("BI319"), 4), (ItemCodes.Parse("WA101"), 3)])
                && drops.MainWeightTotal == 107,
                "an unknown authored item leaked into the runtime drop table, or a line did not keep its weight among the known items");
            Assert.True(drops.BonusEntries.Select(static entry => entry.Weight).SequenceEqual([10, 30, 40]),
                "the bonus drops are not the first three whose item the catalog knows");
            Assert.True(npcs[0].Shop.WeaponItemCodes.SequenceEqual([ItemCodes.Parse("WA101"), ItemCodes.Parse("BI319")]),
                "runtime NPC shop uses a different item identity from the catalog");
            Assert.True(npcs.Count == 2 && npcs[0].Label == "Travelling Merchant" && npcs[0].PortraitOffsetZ == 70,
                "a file without a name did not take its identity's English name, or lost its portrait correction");
            Assert.True(wolf.PortraitOffsetY == -3 && wolf.PortraitOffsetZ == 30,
                "the monster portrait correction was not imported");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task EnglishNamesAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-english-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var item = Path.Combine(directory, "DA130.txt");
            var monster = Path.Combine(directory, "Cristal_Hopy.inf");
            var npc = Path.Combine(directory, "Ribbonhopy.npc");
            await WriteCp949Async(item, Lines("*코드 DA130", "*이름 \"임페리얼 아머\"", "*B_NAME \"Armadura Imperial\""));
            await WriteCp949Async(monster, Lines("*이름 \" 호피 \"", "*E_NAME \"Hopi\""));
            await WriteCp949Async(npc, Lines("*이름 \"리본 호피\"", "*E_NAME \"Ribbon Hopi\""));
            var english = await WriteAndParse();
            Assert.True(english == ("Imperial Armor", "HOPY", "Ribbon Hopy"), $"the Portuguese names stayed: {english}");

            await WriteCp949Async(item, Lines("*코드 DA101", "*이름 \"배틀 슈트\"", "*B_NAME \"Battle Suit\""));
            Assert.True((await WriteAndParse()).Item == "Battle Suit", "a name the data gives in English was replaced");

            // An item whose only English name is its *이름 takes it; a Korean one leaves the code. Magic mastery keeps its range.
            await WriteCp949Async(item, Lines("*코드 DA403", "*이름 \"Abyssal Robe\"", "*마법기술숙련도 15 19"));
            var robe = new PristonTale.WorldImport.Parsers.ItemTxtParser().Parse(item);
            Assert.True(robe is { Name: "Abyssal Robe", MagicMasteryMin: 15f, MagicMasteryMax: 19f }, "the English *이름 or the magic mastery range was lost");
            await WriteCp949Async(item, Lines("*코드 DA404", "*이름 \"버텍스 로브\""));
            Assert.True(new PristonTale.WorldImport.Parsers.ItemTxtParser().Parse(item).Name == "DA404", "a Korean *이름 became the name");

            // A Korean line is read in its English translation; one without a translation is not kept.
            await WriteCp949Async(npc, Lines("*이름 \"상인\"", "*대화 \"다음에 꼭 들러주세요\"", "*대화 \"번역 없음\""));
            var merchant = new PristonTale.WorldImport.Parsers.NpcParser().Parse(npc);
            Assert.True(merchant.Dialog.SequenceEqual(["Please come by again."]) && merchant.UntranslatedDialog.SequenceEqual(["번역 없음"]),
                "a Korean line was not translated, or an untranslated one was kept or not reported");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Task<(string Item, string Monster, string Npc)> WriteAndParse() => Task.FromResult((
            new PristonTale.WorldImport.Parsers.ItemTxtParser().Parse(Path.Combine(directory, "DA130.txt")).Name,
            new PristonTale.WorldImport.Parsers.MonsterInfParser().Parse(Path.Combine(directory, "Cristal_Hopy.inf")).DisplayName,
            new PristonTale.WorldImport.Parsers.NpcParser().Parse(Path.Combine(directory, "Ribbonhopy.npc")).DisplayName));
    }

    private static Task RegistryAsync()
    {
        var monsters = IdentityRegistry.Monsters();
        var npcs = IdentityRegistry.Npcs();
        Assert.True(monsters.TryGet(" 호피 ", out var crystalHopy) && crystalHopy.Key == "HOPY 2" && monsters.TryGet("호피", out var hopy) && hopy.Key == "HOPY"
            && npcs.TryGet("j_zinjya.NPC", out var zinjya) && zinjya is { Id: 127, Key: "Zinjya" }, "a registered identity lost its id or key");

        var called = Simulation.Items.Usable.MonsterCrystals.All.Where(static crystal => crystal.Kind != Simulation.Items.Usable.CrystalKind.SoulStone)
            .SelectMany(static crystal => crystal.Monsters.Concat(crystal.EventMonsters)).Select(static monster => monster.Key)
            .Concat(Simulation.Skills.SkillRuleCatalog.All.Where(static rule => rule.Summon is not null).Select(static rule => rule.Summon.Monster));
        var missing = called.Where(key => !monsters.HasKey(key)).Distinct().ToArray();
        Assert.True(missing.Length == 0, $"crystals or skills call unregistered monsters: {string.Join(", ", missing)}");

        // An unregistered identity takes the next id and its label as key, numbered when the key is taken.
        var registry = new IdentityRegistry("monsters.json", "Monster", StringComparer.Ordinal,
            [new(1, "Fury", "퓨리"), new(7, "Fury 2", "퓨리 두목")]);
        Assert.True(registry.Resolve("새 몬스터", "fury") is { Id: 8, Key: "Fury 3" } && registry.Resolve("다른 몬스터", "") is { Id: 9, Key: "Monster 9" }
            && registry.Added.Count == 2, "an unregistered identity was not keyed as the registry promises");
        return Task.CompletedTask;
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static Task WriteCp949Async(string path, string contents)
    {
        return File.WriteAllBytesAsync(path, Cp949.Encoding.GetBytes(contents));
    }
}
