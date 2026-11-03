using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class AgingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("aging asks its level's sheltoms, costs (level + 1) x price / 2 and refuses mixed, maturing, unique and costume copies", RulesAsync);
        yield return ("an aging roll fails by the level's chance, doubles below +10 and copper ores turn a loss into a fall", RollAsync);
        yield return ("an aging starts the next level's maturity; a fall takes the stats down and starts half way", ApplyAsync);
        yield return ("worn aging copies count their events up to the goal and the upkeep gives the level", MaturityAsync);
        yield return ("an aged item marks the stats its family raises, bows and javelins their mana too", RaisedStatsAsync);
        yield return ("the aging NPC spends the sheltoms, the stone and the price on the candidate and refuses wrong materials", WorkAsync);
        yield return ("the aging ring at +6 and +8 lightens the death's share", DeathPenaltyAsync);
        yield return ("aging requests, results, progress and saved maturities round-trip", WireAsync);
    }

    private static ItemTemplateRecord Record(string code) => ItemRecords.Blank(code);

    private static ItemDefinition Def(string code, Func<ItemTemplateRecord, ItemTemplateRecord>? change = null) =>
        WorldDataFactory.ToItemDefinition(change is null ? Record(code) : change(Record(code)));

    private static ItemInstance Axe(int price = 1000) =>
        new(Def("WA101", record => record with
        {
            Kind = (int)ItemKind.Axe, Price = price, DamageMinLow = 10, DamageMinHigh = 10, DamageMaxLow = 20, DamageMaxHigh = 20,
            AttackRatingMin = 50, AttackRatingMax = 50, DurabilityMin = 200, DurabilityMax = 200
        }));

    private static ItemInstance Sheltom(int ordinal, int count = 1) =>
        new(Def($"OS1{ordinal:00}", record => record with { Kind = (int)ItemKind.Sheltom }), count);

    private static PlayerCharacter Fighter(int level = 50) => new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Aging", level);

    private static Task RulesAsync()
    {
        Assert.True(AgingRules.SheltomsFor(0).SequenceEqual([3, 3, 4, 4, 5]) && AgingRules.SheltomsFor(19).Count == 12,
            "the sheltoms of a level differ from the original table");
        Assert.True(AgingRules.SheltomsMatch(0, [5, 4, 3, 4, 3]) && !AgingRules.SheltomsMatch(0, [3, 3, 4, 4]) && !AgingRules.SheltomsMatch(0, [3, 3, 4, 4, 6]),
            "sheltoms match by kind and count, not by order");
        var axe = Axe(1000);
        axe.UpgradeLevel = 3;
        Assert.True(AgingRules.Price(axe) == 2000, "the aging price is not (level + 1) x price / 2");
        Assert.True(AgingRules.SheltomOrdinal("OS114") == 14 && AgingRules.SheltomOrdinal("OS115") == 0 && AgingRules.StoneOf("BI111") == AgingStone.CopperOre,
            "sheltom ordinals or stone kinds differ");

        Assert.True(AgingRules.Ageable(Axe()), "an axe is not ageable");
        var mixed = Axe();
        mixed.MixSheltom = 3;
        var maturing = Axe();
        maturing.Maturity = new AgingMaturity(0, 100, AgingStone.None);
        var top = Axe();
        top.UpgradeLevel = AgingRules.MaxLevel;
        var costume = new ItemInstance(Def("DA131", record => record with { Kind = (int)ItemKind.Armor }));
        var ring = new ItemInstance(Def("OR201", record => record with { Kind = (int)ItemKind.Ring }));
        var amulet = new ItemInstance(Def("OA101", record => record with { Kind = (int)ItemKind.Amulet }));
        Assert.True(!AgingRules.Ageable(mixed) && !AgingRules.Ageable(maturing) && !AgingRules.Ageable(top) && !AgingRules.Ageable(costume),
            "a mixed, maturing, +20 or costume copy was ageable");
        Assert.True(AgingRules.Ageable(ring) && !AgingRules.Ageable(amulet), "the aging ring or an amulet has the wrong ageability");
        Assert.True(AgingRules.CounterOf(Axe()) == AgingCounter.Attack && AgingRules.CounterOf(ring) == AgingCounter.Time
            && AgingRules.CounterOf(new ItemInstance(Def("DS101"))) == AgingCounter.Block
            && AgingRules.CounterOf(new ItemInstance(Def("WC101"))) == AgingCounter.Critical
            && AgingRules.CounterOf(new ItemInstance(Def("DB101"))) == AgingCounter.Hit, "counters differ by family");
        return Task.CompletedTask;
    }

    private static Task RollAsync()
    {
        // Rolls: destroy, down, plus (0..100), then the copper ore's 0..99.
        Assert.True(AgingRules.Roll(0, AgingStone.None, new ScriptedRng(50, 50, 29)) == AgingResult.Double, "a +0 aging under 30 did not double");
        Assert.True(AgingRules.Roll(0, AgingStone.None, new ScriptedRng(50, 50, 30)) == AgingResult.Success, "a +0 aging at 30 doubled");
        Assert.True(AgingRules.Roll(0, AgingStone.AgingStone, new ScriptedRng(50, 50, 59)) == AgingResult.Double, "the aging stone did not double the chance");
        Assert.True(AgingRules.Roll(0, AgingStone.SuperAgingStone, new ScriptedRng(50, 50, 0)) == AgingResult.Success, "a super stone doubled");
        Assert.True(AgingRules.Roll(12, AgingStone.None, new ScriptedRng(10, 20, 0)) == AgingResult.DownOne, "a +12 failure under 30 did not fall one");
        Assert.True(AgingRules.Roll(12, AgingStone.None, new ScriptedRng(10, 40, 0)) == AgingResult.DownTwo, "a +12 failure under 50 did not fall two");
        Assert.True(AgingRules.Roll(12, AgingStone.None, new ScriptedRng(10, 60, 0)) == AgingResult.Destroyed, "a +12 failure above 50 was not lost");
        Assert.True(AgingRules.Roll(12, AgingStone.None, new ScriptedRng(45, 60, 0)) == AgingResult.Success, "a +12 roll at the chance failed");
        Assert.True(AgingRules.Roll(12, AgingStone.CopperOre, new ScriptedRng(10, 60, 0, 80)) == AgingResult.DownOne
            && AgingRules.Roll(12, AgingStone.CopperOre, new ScriptedRng(10, 60, 0, 81)) == AgingResult.DownTwo, "copper ore did not turn the loss into a fall");
        Assert.True(AgingRules.Roll(12, AgingStone.AgingStone, new ScriptedRng(28, 60, 0)) == AgingResult.Destroyed
            && AgingRules.Roll(12, AgingStone.AgingStone, new ScriptedRng(29, 60, 0)) == AgingResult.Success, "the aging stone table differs");
        Assert.True(AgingRules.Roll(3, AgingStone.None, new ScriptedRng(0, 99, 50)) == AgingResult.Success, "an aging below +5 failed");
        return Task.CompletedTask;
    }

    private static Task ApplyAsync()
    {
        var axe = Axe();
        AgingRules.Apply(axe, AgingResult.Success, AgingStone.None);
        Assert.True(axe.UpgradeLevel == 0 && axe.Maturity == new AgingMaturity(0, 100, AgingStone.None), "a success did not start the maturity at once");

        var doubled = Axe();
        AgingRules.Apply(doubled, AgingResult.Double, AgingStone.AgingStone);
        Assert.True(doubled.UpgradeLevel == 1 && doubled.Roll.DamageMin == 11 && doubled.Roll.DamageMax == 21 && doubled.Roll.AttackRating == 60
            && doubled.DurabilityMax == 197 && doubled.Maturity == new AgingMaturity(0, 130, AgingStone.AgingStone), "a double did not gain a level at once");

        var fallen = Axe();
        for (var level = 0; level < 6; level++)
        {
            AgingRules.LevelUp(fallen, AgingStone.None);
        }

        var required = fallen.Roll.RequiredLevel;
        AgingRules.Apply(fallen, AgingResult.DownOne, AgingStone.None);
        Assert.True(fallen.UpgradeLevel == 4 && fallen.Roll.DamageMin == 14 && fallen.Roll.AttackRating == 90
            && fallen.Maturity == new AgingMaturity(284 / 2, 284, AgingStone.None) && fallen.Roll.RequiredLevel == required - 1,
            "a fall did not take the stats down and start half way");

        var aged = Axe();
        aged.Roll = aged.Roll with { RequiredLevel = 100 };
        AgingRules.LevelUp(aged, AgingStone.None);
        AgingRules.LevelUp(aged, AgingStone.None);
        var elder = Axe();
        AgingRules.LevelUp(elder, AgingStone.ElderCopperOre);
        AgingRules.LevelUp(elder, AgingStone.ElderCopperOre);
        var plain = Axe();
        AgingRules.LevelUp(plain, AgingStone.None);
        AgingRules.LevelUp(plain, AgingStone.None);
        Assert.True(aged.Roll.RequiredLevel == 100 && elder.Roll.RequiredLevel == 0 && plain.Roll.RequiredLevel == 1,
            "the level requirement does not follow the even levels and the exceptions");
        return Task.CompletedTask;
    }

    private static Task MaturityAsync()
    {
        var character = Fighter();
        var carried = Axe();
        carried.Maturity = new AgingMaturity(98, 100, AgingStone.None);
        Assert.True(character.Inventory.TryPlace(carried, 0) && character.TryEquipFromInventory(0, EquipmentSlot.Weapon), "the axe did not equip");
        var axe = character.Equipment[EquipmentSlot.Weapon];
        var revision = character.AgingRevision;
        Assert.True(!character.TallyAging(AgingCounter.Critical) && character.AgingRevision == revision, "a critical counted on an attack weapon");
        Assert.True(!character.TallyAging(AgingCounter.Attack) && character.AgingRevision == revision + 1 && axe.Maturity.Count == 99,
            "an attack did not count");
        var inventoryRevision = character.InventoryRevision;
        Assert.True(character.TallyAging(AgingCounter.Attack, 5) && axe.Maturity.Count == 100 && axe.UpgradeLevel == 0
            && character.InventoryRevision == inventoryRevision && character.HasMaturedItem, "the goal did not wait for the upkeep");
        var candidate = character.CloneForInventoryOperation();
        Assert.True(candidate.MatureAgedItems() && candidate.InventoryRevision > character.InventoryRevision, "the upkeep did not store the level");
        var matured = candidate.Equipment[EquipmentSlot.Weapon];
        Assert.True(matured.UpgradeLevel == 1 && matured.Maturity is null && matured.Roll.DamageMin == 11 && !candidate.HasMaturedItem,
            "the matured copy did not gain its level");

        var ring = new ItemInstance(Def("OR201", record => record with { Kind = (int)ItemKind.Ring }))
        {
            Maturity = new AgingMaturity(0, 1200, AgingStone.None)
        };
        var wearer = Fighter();
        Assert.True(wearer.Inventory.TryPlace(ring, 0) && wearer.TryEquipFromInventory(0, EquipmentSlot.RingLeft), "the ring did not equip");
        wearer.TallyAging(AgingCounter.Time, 60);
        Assert.True(wearer.Equipment[EquipmentSlot.RingLeft].Maturity.Count == 60, "the ring did not count worn seconds");

        // An Aging Master of the shield-or-orb group finishes a worn orb's aging.
        var orb = new ItemInstance(Def("OM101", record => record with { Kind = (int)ItemKind.Orb }))
        {
            Maturity = new AgingMaturity(0, 1200, AgingStone.None)
        };
        var caster = new CharacterClassRegistry().CreatePlayer(PlayerClass.Magician, "Orb", 50);
        Assert.True(caster.Inventory.TryPlace(orb, 0) && caster.TryEquipFromInventory(0, EquipmentSlot.OffHand), "the orb did not equip");
        Assert.True(PristonTale.Simulation.Items.Usable.UsableItemCatalog.TryGet("BI137", out var master)
            && PristonTale.Simulation.Items.Usable.UsableItemEffects.Check(caster, master, 0) == PristonTale.Simulation.Items.Usable.ItemUseStatus.Used,
            "the shield-or-orb Aging Master refused a worn orb");
        return Task.CompletedTask;
    }

    private static Task WorkAsync()
    {
        var character = Fighter();
        character.TryChangeMoney(10_000);
        var axe = Axe(1000);
        Assert.True(character.Inventory.TryPlace(axe, 0), "the axe did not fit");
        ItemInstance[] sheltoms = [Sheltom(3), Sheltom(3), Sheltom(4), Sheltom(4), Sheltom(5)];
        var anchor = 12;
        foreach (var sheltom in sheltoms)
        {
            Assert.True(character.Inventory.TryPlace(sheltom, anchor++), "a sheltom did not fit");
        }

        var stone = new ItemInstance(Def("BI110", record => record with { Kind = (int)ItemKind.Misc }));
        Assert.True(character.Inventory.TryPlace(stone, anchor), "the stone did not fit");
        Guid[] materials = [.. sheltoms.Select(static sheltom => sheltom.InstanceId)];

        Assert.True(AgingWork.TryAge(character, axe.InstanceId, materials[..4], new ScriptedRng(50, 50, 99), out _, out _) == NpcServiceStatus.WrongMaterials,
            "missing sheltoms were accepted");
        Assert.True(AgingWork.TryAge(character, axe.InstanceId, [.. materials[..4], sheltoms[0].InstanceId], new ScriptedRng(50, 50, 99), out _, out _)
            == NpcServiceStatus.WrongMaterials, "one sheltom used twice was accepted");
        Assert.True(AgingWork.TryAge(character, axe.InstanceId, [.. materials, axe.InstanceId], new ScriptedRng(50, 50, 99), out _, out _)
            == NpcServiceStatus.WrongMaterials, "the aged item was taken as a material");

        var poor = Fighter();
        var poorAxe = Axe(1000);
        poor.Inventory.TryPlace(poorAxe, 0);
        ItemInstance[] poorSheltoms = [Sheltom(3), Sheltom(3), Sheltom(4), Sheltom(4), Sheltom(5)];
        for (var index = 0; index < poorSheltoms.Length; index++)
        {
            poor.Inventory.TryPlace(poorSheltoms[index], 12 + index);
        }

        Assert.True(AgingWork.TryAge(poor, poorAxe.InstanceId, [.. poorSheltoms.Select(static sheltom => sheltom.InstanceId)],
                new ScriptedRng(50, 50, 99), out _, out _) == NpcServiceStatus.NotEnoughGold && poor.Inventory.Find(poorSheltoms[0].InstanceId) >= 0,
            "an aging without the price was carried out");

        var money = character.Money;
        var status = AgingWork.TryAge(character, axe.InstanceId, [.. materials, stone.InstanceId], new ScriptedRng(50, 50, 99), out var work, out var price);
        Assert.True(status == NpcServiceStatus.Ok && price == 500 && character.Money == money - 500 && work.Result == (byte)AgingResult.Success
            && work.ItemId == axe.InstanceId && character.Inventory[0].Maturity == new AgingMaturity(0, 100, AgingStone.AgingStone), "the aging was not carried out");
        Assert.True(sheltoms.All(sheltom => character.Inventory.Find(sheltom.InstanceId) < 0) && character.Inventory.Find(stone.InstanceId) < 0,
            "the materials were not spent");
        Assert.True(AgingWork.TryAge(character, axe.InstanceId, materials, new ScriptedRng(50, 50, 99), out _, out _) == NpcServiceStatus.Rejected,
            "a maturing copy was aged again");

        var lost = Fighter();
        lost.TryChangeMoney(1_000_000);
        var doomed = Axe(1000);
        for (var level = 0; level < 12; level++)
        {
            AgingRules.LevelUp(doomed, AgingStone.None);
        }

        lost.Inventory.TryPlace(doomed, 0);
        var asked = AgingRules.SheltomsFor(12);
        var ids = new List<Guid>();
        var cell = 24;
        foreach (var ordinal in asked)
        {
            var sheltom = Sheltom(ordinal);
            lost.Inventory.TryPlace(sheltom, cell++);
            ids.Add(sheltom.InstanceId);
        }

        Assert.True(AgingWork.TryAge(lost, doomed.InstanceId, ids, new ScriptedRng(10, 60, 0), out var gone, out _) == NpcServiceStatus.Ok
            && gone.ItemId == Guid.Empty && gone.Result == (byte)AgingResult.Destroyed && lost.Inventory.Find(doomed.InstanceId) < 0,
            "a lost aging kept the item");
        return Task.CompletedTask;
    }

    private static Task DeathPenaltyAsync()
    {
        var character = Fighter(90);
        Assert.True(DeathPenalty.Divisor(character, FieldState.Forest) == DeathPenalty.StandardDivisor, "the plain divisor differs");
        var ring = new ItemInstance(Def("OR201", record => record with { Kind = (int)ItemKind.Ring })) { UpgradeLevel = 6 };
        Assert.True(character.Inventory.TryPlace(ring, 0) && character.TryEquipFromInventory(0, EquipmentSlot.RingRight), "the ring did not equip");
        Assert.True(DeathPenalty.Divisor(character, FieldState.Forest) == DeathPenalty.ReducedDivisor, "the +6 ring did not lighten the share");
        character.Equipment[EquipmentSlot.RingRight].UpgradeLevel = 8;
        Assert.True(DeathPenalty.Divisor(character, FieldState.Forest) == DeathPenalty.FieldDivisor && DeathPenalty.Divisor(character, FieldState.Castle) == 0,
            "the +8 ring did not take a hundredth");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[4096];
        Guid[] materials = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var request = new NpcServiceRequestPacket(Guid.NewGuid(), 9, (byte)NpcServiceKind.ItemAging, (byte)NpcServiceAction.Age, 4, 0, 0, Guid.NewGuid(),
            -1, 1, 0, materials);
        var length = request.Write(buffer);
        var read = NpcServiceRequestPacket.Read(buffer.AsSpan(0, length));
        Assert.True(read == request && length == NpcServiceRequestPacket.Size + 1 + 16 * materials.Length && read.Materials!.SequenceEqual(materials),
            "the aging request does not round-trip its materials");

        var plain = request with { Materials = null };
        Assert.True(plain.Write(buffer) == NpcServiceRequestPacket.Size && NpcServiceRequestPacket.Read(buffer.AsSpan(0, NpcServiceRequestPacket.Size)) == plain,
            "a request without materials changed size");

        var work = new ItemWorkPacket(Guid.NewGuid(), 9, (byte)NpcServiceKind.ItemAging, (byte)AgingResult.Double, Guid.NewGuid(), ItemCodes.Parse("WA101"));
        Assert.True(ItemWorkPacket.Read(buffer.AsSpan(0, work.Write(buffer))) == work, "the work result does not round-trip");

        AgingProgressEntry[] entries = [new(Guid.NewGuid(), 5, 100), new(Guid.NewGuid(), 600, 1200)];
        var progress = AgingProgressPacket.Read(buffer.AsSpan(0, new AgingProgressPacket(entries).Write(buffer)));
        Assert.True(progress.Entries.SequenceEqual(entries), "aging progress does not round-trip");

        var command = new NpcServiceCommand(Guid.NewGuid(), 9, NpcServiceKind.ItemAging, NpcServiceAction.Age, Materials: materials);
        Assert.True(command == command with { Materials = [.. materials] } && command != command with { Materials = [materials[0]] },
            "commands do not compare their materials");

        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Aging", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        var axe = Axe();
        axe.Maturity = new AgingMaturity(7, 100, AgingStone.CopperOre);
        character.Inventory.TryPlace(axe, 0);
        var encoded = CharacterRecordFactory.Capture(record, character, 3, System.Numerics.Vector3.Zero).Encode();
        var restored = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(restored, encoded, new ItemCatalog([axe.Definition]));
        Assert.True(restored.Inventory[0].Maturity == axe.Maturity, "the maturity was not saved");
        return Task.CompletedTask;
    }

    private static Task RaisedStatsAsync()
    {
        string[] Of(AgedStats stat) => stat switch
        {
            AgedStats.Damage => ["WA1", "WC1", "WH1", "WM1", "WP1", "WS1", "WS2", "WT1"],
            AgedStats.Critical => ["WC1", "WP1", "WS1", "WT1", "WS2"],
            AgedStats.Defence => ["OM1", "DA1", "DA2", "DB1", "DG1", "OA2"],
            AgedStats.AttackRating => ["WA1", "WC1", "WM1", "WH1", "WP1", "WS2", "OA2"],
            AgedStats.Absorb => ["OM1", "DA1", "DS1", "DA2", "DB1", "DG1"],
            AgedStats.Block => ["DS1"],
            AgedStats.Integrity => ["WA1", "WH1", "WS1", "DS1", "OM1", "DA1", "DA2", "DB1", "DG1", "OA2"],
            _ => ["WM1", "WS1", "WT1"]
        };

        string[] families = ["WA1", "WC1", "WH1", "WM1", "WP1", "WS1", "WS2", "WT1", "DA1", "DS1", "OM1", "DA2", "DG1", "DB1", "OA2", "OR2"];
        foreach (var family in families)
        {
            foreach (var stat in Enum.GetValues<AgedStats>().Where(static stat => stat != AgedStats.None))
            {
                var marked = (AgingRules.RaisedBy(family + "01") & stat) != 0;
                Assert.True(marked == Of(stat).Contains(family), $"{family} marks {stat} wrongly");
            }
        }

        return Task.CompletedTask;
    }
}
