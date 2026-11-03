using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Server.Net;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;
using PristonTale.WorldImport;
using PristonTale.WorldImport.Parsers;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ItemRulesTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every wearable family equips in its own slot, aged copies included", WearableSlotsAsync);
        yield return ("every item key has its own code, so new item families need no registration", CodesAsync);
        yield return ("an item's kind follows its key the same way for the importer and the runtime", KindsAsync);
        yield return ("unmet requirements refuse equipping and a worn item that stops meeting them adds nothing", RequirementsAsync);
        yield return ("a two-handed weapon sends the off-hand item to the bag and an off-hand item sends a held two-handed weapon there", TwoHandedAsync);
        yield return ("drops roll every value inside its authored range and specialize four times in ten", DropRollsAsync);
        yield return ("the shop sells the lowest form and keeps a fixed specialization with the middle requirement shift", ShopFormAsync);
        yield return ("magic mastery rolls whole points inside its authored range: the shop form takes the low end, the best copy the top", MagicMasteryAsync);
        yield return ("worn items feed absorb, critical, block, speed, regeneration, pools, resistances and specialization bonuses", DerivedStatsAsync);
        yield return ("worn block ratings block monster blows, monsters never block, and the cap needs a shield", BlockAsync);
        yield return ("carried weight slows movement, stops running and refuses pickups, purchases, withdrawals and unequipping", WeightAsync);
        yield return ("durability never takes an item's stats away", DurabilityAsync);
        yield return ("item copies keep their values through saves and older saves take the shop form with their specialization", PersistenceAsync);
        yield return ("item details and the equipment status reach the owner over the wire", WireAsync);
        yield return ("the item reader keeps the known specialization classes and every special bonus", ImportAsync);
        yield return ("potions stack in the bag, so a shop sells them by the count onto one stack and buys back part of it", PotionStacksAsync);
    }

    private static ItemTemplateRecord Record(string code) => ItemRecords.Blank(code);

    private static ItemDefinition Def(string code, Func<ItemTemplateRecord, ItemTemplateRecord>? change = null) =>
        WorldDataFactory.ToItemDefinition(change is null ? Record(code) : change(Record(code)));

    private static Task PotionStacksAsync()
    {
        var potion = Def("PL101", record => record with { Kind = (int)ItemKind.Potion, Price = 25, Weight = 1 });
        Assert.True(potion.MaxStack == PotionDefinition.StackSize && Def("WA101").MaxStack == 1, "potions stack to their stack size; other items keep the authored one");

        var character = Fighter();
        character.TryChangeMoney(1000 - character.Money);
        Assert.True(NpcShopRules.TryBuy(character, potion, 30, -1, out var cost) == NpcServiceStatus.Ok && cost == 750, "thirty potions were bought");
        Assert.True(NpcShopRules.TryBuy(character, potion, 9, -1, out _) == NpcServiceStatus.Ok, "nine more were bought");
        var stacks = character.Inventory.Items().Where(item => item.Definition == potion).ToArray();
        Assert.True(stacks is [{ Count: 39 }] && character.Money == 25, $"the potions share one stack: {string.Join(",", stacks.Select(item => item.Count))}");

        var stack = stacks[0];
        Assert.True(NpcShopRules.TrySell(character, stack.InstanceId, 40, out _) == NpcServiceStatus.Rejected, "more units than the stack holds sell");
        Assert.True(NpcShopRules.TrySell(character, stack.InstanceId, 10, out var proceeds) == NpcServiceStatus.Ok && proceeds == 50
            && character.Inventory.Items().Single(item => item.Definition == potion).Count == 29 && character.Money == 75, "ten of the potions were sold");
        return Task.CompletedTask;
    }

    private static PlayerCharacter Fighter(int level = 50) => new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Items", level);

    private static bool Equip(PlayerCharacter character, ItemInstance item, EquipmentSlot slot)
    {
        var anchor = -1;
        for (var cell = 0; cell < character.Inventory.Capacity && anchor < 0; cell++)
        {
            if (character.Inventory.TryPlace(item, cell))
            {
                anchor = cell;
            }
        }

        return anchor >= 0 && character.TryEquipFromInventory(anchor, slot);
    }

    private static Task WearableSlotsAsync()
    {
        (string Code, EquipmentSlot Slot, ItemKind Kind)[] families =
        [
            ("WA101", EquipmentSlot.Weapon, ItemKind.Axe), ("WD101", EquipmentSlot.Weapon, ItemKind.Dagger),
            ("WN101", EquipmentSlot.Weapon, ItemKind.Phantom), ("WV101", EquipmentSlot.Weapon, ItemKind.Vambrace),
            ("DA101", EquipmentSlot.Armor, ItemKind.Armor), ("DB101", EquipmentSlot.Boots, ItemKind.Boots),
            ("DG101", EquipmentSlot.Gloves, ItemKind.Gloves), ("DS101", EquipmentSlot.OffHand, ItemKind.Shield),
            ("OA101", EquipmentSlot.Amulet, ItemKind.Amulet), ("OA201", EquipmentSlot.Bracelet, ItemKind.Bracelet),
            ("OR101", EquipmentSlot.RingLeft, ItemKind.Ring), ("OR201", EquipmentSlot.RingRight, ItemKind.Ring),
            ("OS101", EquipmentSlot.Sheltom, ItemKind.Sheltom)
        ];
        var character = Fighter();
        foreach (var (code, slot, kind) in families)
        {
            var definition = Def(code);
            Assert.True(definition.Kind == kind && definition.IsEquippable, $"{code} is not a wearable {kind}");
            var item = new ItemInstance(definition) { UpgradeLevel = code == "OA101" ? 3 : 0 };
            Assert.True(Equip(character, item, slot) && character.GetEquipped(slot)?.InstanceId == item.InstanceId, $"{code} did not equip in {slot}");
        }

        var orb = Def("OM101");
        Assert.True(orb.Kind == ItemKind.Orb && orb.EquipSlot == EquipmentSlot.OffHand && orb.AllowedJobs != 0, "orbs are not off-hand caster items");
        Assert.True(Def("WA105").TwoHanded && !Def("WA101").TwoHanded && Def("WS101").TwoHanded && !Def("WT101").TwoHanded,
            "two-handed weapons differ from the item table");
        Assert.True(!Def("FO101").IsEquippable, "force orbs became wearable");
        Assert.True(ItemCodes.TryFormat(Def("WD105").Code, out var key) && key == "WD105", "dagger codes do not round-trip");
        Assert.True(Def("DA304").Kind == ItemKind.Armor && Def("DA304").EquipSlot == EquipmentSlot.Armor
            && Def("DA401").EquipSlot == EquipmentSlot.Armor, "armours and robes of the newer families are not worn as armour");
        return Task.CompletedTask;
    }

    // Every key of two letters, a letter or digit and two digits has its own code, whatever its family, and formats back.
    private static Task KindsAsync()
    {
        foreach (var (key, kind) in new[] { ("OM101", ItemKind.Orb), ("WV101", ItemKind.Vambrace), ("WS101", ItemKind.Bow), ("WS201", ItemKind.Sword),
            ("OA201", ItemKind.Bracelet), ("DA401", ItemKind.Armor), ("OS105", ItemKind.Sheltom), ("PL101", ItemKind.Potion), ("FO101", ItemKind.ForceOrb),
            ("QT101", ItemKind.Quest), ("MA101", ItemKind.Material), ("GP101", ItemKind.Misc), ("WR101", ItemKind.Misc) })
        {
            Assert.True(ItemKinds.Classify(key) == kind, $"{key} is {ItemKinds.Classify(key)}, not {kind}");
        }

        return Task.CompletedTask;
    }

    private static Task CodesAsync()
    {
        var codes = new HashSet<uint>();
        foreach (var key in new[] { "WA101", "DA304", "DA401", "CA624", "OE101", "SP299", "BI501", "RS108", "ZZ900", "AA000" })
        {
            Assert.True(ItemCodes.TryParse(key, out var code) && code != 0 && codes.Add(code), $"{key} has no code of its own");
            Assert.True(ItemCodes.TryFormat(code, out var formatted) && formatted == key, $"{key} does not format back");
        }

        Assert.True(ItemCodes.Parse("sp201") == ItemCodes.Parse("SP201"), "item keys are case-sensitive");
        Assert.True(ItemCodes.Family(ItemCodes.Parse("WS201")) == ItemCodes.Family("WS2")
            && ItemCodes.Family(ItemCodes.Parse("WS201")) != ItemCodes.Family(ItemCodes.Parse("WS101")), "families are not told apart");
        Assert.True(!ItemCodes.TryParse("WA1", out _) && !ItemCodes.TryParse("1A101", out _) && !ItemCodes.TryParse("WA1X1", out _)
            && !ItemCodes.TryParse(null, out _) && !ItemCodes.TryFormat(0, out _), "malformed keys or codes were accepted");
        return Task.CompletedTask;
    }

    private static Task RequirementsAsync()
    {
        var character = Fighter(10);
        var heavy = new ItemInstance(Def("WA110", record => record with { ReqLevel = 40, AttackRatingMin = 50 }));
        Assert.True(character.Inventory.TryPlace(heavy, 0) && !character.TryEquipFromInventory(0, EquipmentSlot.Weapon), "an item above the level equipped");
        var command = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.Equip, heavy.InstanceId, 0, 0, EquipmentSlot.Weapon);
        Assert.True(InventoryCommands.Refusal(character, command) == ItemRuleMessages.CannotUse, "the refusal does not name the unmet requirement");
        var robe = new ItemInstance(Def("DA201"));
        Assert.True(character.Inventory.TryPlace(robe, 20) && !character.TryEquipFromInventory(20, EquipmentSlot.Armor), "a fighter wore a robe");

        // A saved worn item whose requirement is no longer met stays worn but adds nothing.
        var before = character.Derived.AttackRating;
        character.Inventory.TakeAt(0);
        character.Equip(EquipmentSlot.Weapon, heavy);
        Assert.True(character.GetEquipped(EquipmentSlot.Weapon)?.InstanceId == heavy.InstanceId && character.Derived.AttackRating == before, "an unusable worn item added its stats");
        Assert.True(!character.CanEquip(EquipmentSlot.Weapon, heavy) && character.CanHold(EquipmentSlot.Weapon, heavy), "usable and holdable were mixed up");
        return Task.CompletedTask;
    }

    private static Task TwoHandedAsync()
    {
        var character = Fighter();
        var shield = new ItemInstance(Def("DS101"));
        var sword = new ItemInstance(Def("WA101"));
        var great = new ItemInstance(Def("WA105"));
        Assert.True(Equip(character, sword, EquipmentSlot.Weapon) && Equip(character, shield, EquipmentSlot.OffHand), "one-handed set failed");
        Assert.True(Equip(character, great, EquipmentSlot.Weapon), "a two-handed weapon was refused while a shield was worn");
        Assert.True(character.GetEquipped(EquipmentSlot.Weapon)?.InstanceId == great.InstanceId && character.GetEquipped(EquipmentSlot.OffHand) is null,
            "the two-handed weapon does not hold both hands");
        Assert.True(character.Inventory.Find(shield.InstanceId) >= 0, "the off-hand item did not go to the bag");
        Assert.True(character.Inventory.Find(sword.InstanceId) >= 0, "the replaced weapon did not go to the bag");
        Assert.True(character.TryEquipFromInventory(character.Inventory.Find(shield.InstanceId), EquipmentSlot.OffHand), "a shield was refused beside a two-handed weapon");
        Assert.True(character.GetEquipped(EquipmentSlot.OffHand)?.InstanceId == shield.InstanceId && character.GetEquipped(EquipmentSlot.Weapon) is null
            && character.Inventory.Find(great.InstanceId) >= 0, "the two-handed weapon did not go to the bag");
        return Task.CompletedTask;
    }

    private static Task DropRollsAsync()
    {
        var definition = Def("WA110", record => record with
        {
            DurabilityMin = 60, DurabilityMax = 80, DamageMinLow = 10, DamageMinHigh = 14, DamageMaxLow = 20, DamageMaxHigh = 26,
            AttackRatingMin = 30, AttackRatingMax = 40, BlockMin = 1.5f, BlockMax = 3.25f, AbsorbMin = 0.4f, AbsorbMax = 1.2f,
            ResFireMin = 2, ResFireMax = 9, Price = 1000, ReqStrength = 100, ReqSpirit = 40, ReqAgility = 80, SpecClass = (long)JobBitCode.Fighter,
            SpecBonuses = [new ItemSpecBonusRecord(0, (short)ItemSpecBonusKind.Defence, 3, 7), new ItemSpecBonusRecord(0, (short)ItemSpecBonusKind.LevelAttackRating, 2, 4)],
            RandomSpecClasses = [(long)JobBitCode.Knight, (long)JobBitCode.Pikeman]
        });
        var rng = new SeededSimRng(7);
        var specialized = 0;
        const int copies = 4000;
        for (var index = 0; index < copies; index++)
        {
            var item = ItemRoller.Create(definition, 1, rng);
            var roll = item.Roll;
            Assert.True(item.DurabilityMax is >= 60 and <= 80 && item.DurabilityCurrent >= item.DurabilityMax / 2 && item.DurabilityCurrent <= item.DurabilityMax,
                "durability rolled outside its range");
            Assert.True(roll.DamageMin is >= 10 and <= 14 && roll.DamageMax is >= 20 and <= 26 && roll.AttackRating is >= 30 and <= 40,
                "damage or rating rolled outside its range");
            Assert.True(roll.Block >= 1.5f && roll.Block <= 3.2501f && roll.Absorb >= 0.4f && roll.Absorb <= 1.2001f, "block or absorb rolled outside its range");
            Assert.True(MathF.Abs(roll.Absorb * 10 - MathF.Round(roll.Absorb * 10)) < 0.001f, "absorb kept more than one decimal");
            Assert.True(roll.Resistances.Fire is >= 2 and <= 9, "a resistance rolled outside its range");
            if (roll.SpecJob == 0)
            {
                Assert.True(roll.Price == 1000 && roll.Spec == ItemSpecBonus.None && roll.Requirements == definition.Requirements, "an unspecialized copy changed");
                continue;
            }

            specialized++;
            Assert.True(roll.Price == 1200, "a specialized copy does not cost a fifth more");
            Assert.True(roll.Spec.Defence is >= 3 and <= 7 && roll.Spec.LevelAttackRating is >= 2 and <= 4, "specialization bonuses outside their range");
            if (roll.SpecJob == (uint)JobBitCode.Fighter)
            {
                Assert.True(roll.Requirements.Strength is >= 110 and <= 115 && roll.Requirements.Spirit is >= 32 and <= 34
                    && roll.Requirements.Agility is >= 64 and <= 68, "the fighter requirement shift is wrong");
            }
            else
            {
                Assert.True(roll.SpecJob is (uint)JobBitCode.Knight or (uint)JobBitCode.Pikeman, "a copy took a class outside its lists");
            }
        }

        var share = specialized / (float)copies;
        Assert.True(share > 0.36f && share < 0.44f, $"specialization share {share:0.000} is not four in ten");
        return Task.CompletedTask;
    }

    private static Task ShopFormAsync()
    {
        var definition = Def("DA110", record => record with
        {
            DurabilityMin = 40, DurabilityMax = 60, DefenceMin = 30, DefenceMax = 50, AbsorbMin = 1.1f, AbsorbMax = 2.0f, Price = 500,
            ReqStrength = 100, ReqAgility = 40, SpecClass = (long)JobBitCode.Archer, SpecBonuses = [new ItemSpecBonusRecord(0, (short)ItemSpecBonusKind.Defence, 4, 9)]
        });
        var item = new ItemInstance(definition);
        Assert.True(item.DurabilityMax == 40 && item.DurabilityCurrent == 40 && item.Roll.Defence == 30 && MathF.Abs(item.Roll.Absorb - 1.1f) < 0.0001f,
            "the shop form is not the lowest form");
        Assert.True(item.Roll.SpecJob == (uint)JobBitCode.Archer && item.Roll.Spec.Defence == 4 && item.Price == 500, "the shop form lost its specialization");
        Assert.True(item.Roll.Requirements.Strength == 80 && item.Roll.Requirements.Agility == 48, "the middle requirement shift was not applied");
        return Task.CompletedTask;
    }

    private static Task MagicMasteryAsync()
    {
        var definition = Def("WM101", static record => record with { MagicMasteryMin = 15f, MagicMasteryMax = 19f });
        Assert.True(new ItemInstance(definition).MagicMastery == 15f && ItemRoller.CreatePerfect(definition, 1, 0).MagicMastery == 19f,
            "the shop form or the best copy does not take its end of the range");
        var rng = new SeededSimRng(5);
        var seen = new HashSet<float>();
        for (var index = 0; index < 400; index++)
        {
            var mastery = ItemRoller.Create(definition, 1, rng).MagicMastery;
            Assert.True(mastery is >= 15f and <= 19f && mastery == MathF.Round(mastery), $"magic mastery rolled {mastery}");
            seen.Add(mastery);
        }

        Assert.True(seen.Count == 5, "a whole point of the range was never rolled");
        var fixedMastery = Def("WM102", static record => record with { MagicMasteryMin = 8f });
        Assert.True(ItemRoller.Create(fixedMastery, 1, rng).MagicMastery == 8f, "a single authored value did not stay fixed");
        return Task.CompletedTask;
    }

    private static Task DerivedStatsAsync()
    {
        var character = Fighter(10);
        var bare = character.Derived;
        var bareLife = character.MaxLife;
        var armor = new ItemInstance(Def("DA102", record => record with { AbsorbMin = 1.0f, DefenceMin = 20 }));
        var boots = new ItemInstance(Def("DB103", record => record with { AbsorbMin = 0.5f, MoveSpeedMin = 1.5f }));
        var shield = new ItemInstance(Def("DS101", record => record with { BlockMin = 10.5f }));
        var axe = new ItemInstance(Def("WA101", record => record with { Critical = 30, DamageMinLow = 5, DamageMinHigh = 6, DamageMaxLow = 9, DamageMaxHigh = 10, MagicMasteryMin = 3f }));
        var amulet = new ItemInstance(Def("OA101", record => record with { Critical = 30, AddManaMin = 12 }));
        var ring = new ItemInstance(Def("OR101", record => record with { ResFireMin = 10, AddLifeMin = 20, LifeRegenMin = 1.2f, AddStaminaMin = 7 }));
        axe.Roll = axe.Roll with
        {
            SpecJob = (uint)JobBitCode.Fighter,
            Spec = ItemSpecBonus.None with { LevelLife = 2, LevelAttackRating = 1, LevelDamage = 5, Block = 2f, ManaRegen = 0.8f },
            MagicMasteryBonus = 2f
        };
        foreach (var (item, slot) in new[] { (armor, EquipmentSlot.Armor), (boots, EquipmentSlot.Boots), (shield, EquipmentSlot.OffHand),
            (axe, EquipmentSlot.Weapon), (amulet, EquipmentSlot.Amulet), (ring, EquipmentSlot.RingLeft) })
        {
            Assert.True(Equip(character, item, slot), $"{item.Definition.CatalogKey} did not equip");
        }

        var totals = character.EquipmentTotals;
        Assert.Near(0.3f + 1.0f + 0.2f + 0.5f + 0.5f, totals.Absorb, 0.001f);
        Assert.True(totals.AbsorbItems == 2, "absorb items were not counted by their own absorb");
        var derived = character.Derived;
        Assert.True(character.MaxLife == bareLife + 20 + 10 / 2, "added life or the level life bonus is missing");
        Assert.True(derived.AttackRating == bare.AttackRating + 10, "the level attack rating bonus is missing");
        Assert.True(derived.CriticalChance == DerivedStats.MaxCriticalChance && CombatProfileFactory.CriticalChance(character) == 50, "critical is not capped at 50");
        Assert.True(CombatProfileFactory.ForPlayerDefender(character).BlockChance == 12, "worn and specialization block were not summed and truncated");
        Assert.True(CombatProfileFactory.Resistances(character).Fire == 10, "item resistances do not reach combat");
        Assert.Near(1.2f, derived.LifeRegen, 0.001f);
        Assert.Near(0.4f, derived.ManaRegen, 0.001f);
        Assert.True(derived.MaxStamina == bare.MaxStamina + 7 && derived.MaxMana == bare.MaxMana + 12, "added pools are missing");
        Assert.True(derived.MoveSpeed >= bare.MoveSpeed + 1, "boot speed does not raise the move speed");
        Assert.Near(5f, derived.MagicMastery, 0.001f);
        var weapon = WeaponDamage.Compute(character);
        var attributes = character.Attributes;
        var expectedMax = 4 + 9 * (attributes.Strength + 130) / 130 + (attributes.Talent + attributes.Agility) / 40 + 10 / 6 + 10 / 5;
        Assert.True(weapon.Max == expectedMax, "the level damage bonus is not added to the maximum");
        var expectedAbsorption = (int)(derived.Defence / 100f + 10 / 10f + totals.Absorb + (attributes.Strength + attributes.Talent) / 40f) + 1 + 1;
        Assert.True(derived.Absorption == expectedAbsorption, "absorption does not follow the item formula");
        return Task.CompletedTask;
    }

    private static Task BlockAsync()
    {
        var character = Fighter(50);
        var defender = CombatProfileFactory.ForPlayerDefender(character) with { BlockChance = 100 };
        var attack = new AttackProfile(10_000, 10, 10, 50, Element.Bionic, 0, 0, 0);
        var rng = new CombatRng(3);
        var blocked = 0;
        for (var index = 0; index < 200; index++)
        {
            blocked += CombatResolver.ResolveMonsterAttack(rng, attack, defender, defenderHasShield: false).Blocked ? 1 : 0;
        }

        Assert.True(blocked == 200, "a full block rating without a shield did not block every blow");
        blocked = 0;
        for (var index = 0; index < 2000; index++)
        {
            blocked += CombatResolver.ResolveMonsterAttack(rng, attack, defender, defenderHasShield: true).Blocked ? 1 : 0;
        }

        Assert.True(blocked is > 880 and < 1120, "the shield cap of 50 does not apply");
        var monster = new DefenderProfile(0, 0, 100, 50, ResistanceBlock.Zero, 100, 100, IsMonster: true);
        for (var index = 0; index < 200; index++)
        {
            Assert.True(!CombatResolver.Resolve(rng, attack, monster, alwaysHit: true).Blocked, "a monster blocked a player's hit");
        }

        var shield = new ItemInstance(Def("DS101", record => record with { BlockMin = 15 }));
        Assert.True(Equip(character, shield, EquipmentSlot.OffHand) && CombatProfileFactory.ForPlayerDefender(character).BlockChance == 15,
            "the shield block rating is not the player's block chance");
        return Task.CompletedTask;
    }

    private static Task WeightAsync()
    {
        var character = Fighter(10);
        var max = character.Derived.MaxWeight;
        var crate = Def("BI101", record => record with { Weight = max - 5 });
        Assert.True(character.ReceiveGroundLoot(new ItemInstance(crate), 0) && character.CarriedWeight == max - 5, "carried weight is not tracked");
        var a = character.Attributes;
        var loaded = (int)((a.Talent + a.Health + character.Level + 60f) / 150f - (max - 5) / (float)max) + 1;
        Assert.True(character.Derived.MoveSpeed == loaded, "carried weight does not weigh on speed");
        var stone = Def("BI102", record => record with { Weight = 10 });
        Assert.True(!character.ReceiveGroundLoot(new ItemInstance(stone), 0), "a pickup over the limit was accepted");
        var potion = new ItemDefinition(ItemCodes.Parse("PL101"), "Potion", ItemKind.Potion, MaxStack: 100) { Weight = 7, Price = 1 };
        character.TryChangeMoney(1000);
        Assert.True(NpcShopRules.TryBuy(character, potion, 5, -1, out _) == NpcServiceStatus.Ok && character.CarriedWeight == max,
            "a potion did not weigh one per unit");
        Assert.True(NpcShopRules.TryBuy(character, potion, 1, -1, out _) == NpcServiceStatus.OverWeight, "a purchase over the limit was accepted");
        var storage = new WarehouseStorage();
        var stored = new ItemInstance(stone);
        Assert.True(storage.Restore(stored, 0) && storage.TryWithdraw(character, stored.InstanceId, -1) == NpcServiceStatus.OverWeight,
            "a withdrawal over the limit was accepted");

        var ring = new ItemInstance(Def("OR101"));
        Assert.True(Equip(character, ring, EquipmentSlot.RingLeft), "the ring did not equip");
        character.Inventory.TryPlace(new ItemInstance(stone), 60);
        Assert.True(CarriedWeight.IsOverweight(character) && !character.TryUnequip(EquipmentSlot.RingLeft), "unequipping while overweight was allowed");
        var command = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.Unequip, ring.InstanceId, 0, 0, EquipmentSlot.RingLeft);
        Assert.True(InventoryCommands.Refusal(character, command) == ItemRuleMessages.OverWeight, "the refusal does not name the weight");

        var drain = -Regeneration.StaminaPerSecond(character, RegenerationPosture.Running);
        var expected = (1000 + character.CarriedWeight) / (character.Derived.MaxWeight + character.Attributes.Strength / 2 + 500) + 0.4f;
        Assert.Near(expected, drain, 0.001f);

        var player = new WorldPlayer(1, new Vector3(100, 0, 100), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(102, 0, 100), 0f, true, true, 0, 1000) == MovementVerdict.Accepted
            && player.MoveState == MotionState.Walk, "an overweight character ran");
        return Task.CompletedTask;
    }

    private static Task DurabilityAsync()
    {
        var character = Fighter(20);
        var armor = new ItemInstance(Def("DA101", record => record with { DurabilityMin = 50, DefenceMin = 40, Price = 1000 }));
        Assert.True(Equip(character, armor, EquipmentSlot.Armor), "the armor did not equip");
        var defence = character.Derived.Defence;
        armor = character.GetEquipped(EquipmentSlot.Armor)!;
        armor.DurabilityCurrent = 0;
        character.TouchInventory();
        Assert.True(character.Derived.Defence == defence && armor.TotalStats.Defence == 40, "a worn-out item lost its stats");
        Assert.True(NpcShopRules.RepairCost(armor) > 0 && character.TryChangeMoney(100_000) && NpcShopRules.TryRepair(character, armor.InstanceId, out _) == NpcServiceStatus.Ok
            && armor.DurabilityCurrent == 50, "repair did not restore durability");
        return Task.CompletedTask;
    }

    private static Task PersistenceAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Rolls", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        var definition = Def("WA110", item => item with { DamageMinLow = 10, DamageMinHigh = 14, DamageMaxLow = 20, DamageMaxHigh = 26, SpecClass = (long)JobBitCode.Fighter,
            MagicMasteryMin = 5f, MagicMasteryMax = 9f });
        var catalog = new ItemCatalog([definition]);
        ItemInstance rolled;
        var rng = new SeededSimRng(11);
        do
        {
            rolled = ItemRoller.Create(definition, 1, rng);
        }
        while (rolled.Roll.SpecJob == 0 || rolled.Roll.MagicMasteryBonus == 0);

        Assert.True(character.Inventory.TryPlace(rolled, 0), "the rolled copy did not fit");
        var encoded = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero).Encode();
        var restored = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(restored, encoded, catalog);
        Assert.True(restored.Inventory[0].Roll == rolled.Roll && restored.Inventory[0].DurabilityMax == rolled.DurabilityMax, "the copy's values were not saved");

        var older = encoded with
        {
            Inventory = new JsonText(
                $"{{\"Version\":1,\"Items\":[{{\"InstanceId\":\"{Guid.NewGuid()}\",\"Code\":{definition.Code},\"Count\":1,\"Anchor\":0,\"Slot\":null," +
                "\"UpgradeLevel\":2,\"DurabilityCurrent\":0,\"DurabilityMax\":0,\"JobSpecMask\":2}]}")
        };
        var old = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        CharacterStateCodec.Restore(old, older, catalog);
        var copy = old.Inventory[0];
        Assert.True(copy.Roll.DamageMin == 10 && copy.Roll.SpecJob == (uint)JobBitCode.Fighter && copy.UpgradeLevel == 2, "an older save did not take the shop form");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var character = Fighter(30);
        var ring = new ItemInstance(Def("OR101", record => record with { ResFireMin = 12, AddLifeMin = 9, ReqLevel = 25, Price = 300 }));
        ring.Roll = ring.Roll with { SpecJob = (uint)JobBitCode.Fighter, Spec = ItemSpecBonus.None with { LevelLife = 3, Absorb = 0.7f } };
        Assert.True(Equip(character, ring, EquipmentSlot.RingLeft), "the ring did not equip");
        // The owner packets in the order the presence encoder sends them.
        var state = OwnerState.Capture(character);
        var sink = new PacketList();
        var scratch = new byte[Protocol.Binary.FrameCodec.MaxFrameSize];
        sink.Send(scratch.AsSpan(0, ItemWireMapping.Status(state).Write(scratch)));
        sink.Send(scratch.AsSpan(0, WireMapping.Status(state).Write(scratch)));
        ItemWireMapping.SendDetails(state, sink, scratch);
        sink.Send(scratch.AsSpan(0, new InventoryPartPacket(state.InventoryRevision, 0, 1, state.Items.Select(WireMapping.Item).ToArray()).Write(scratch)));
        sink.Send(scratch.AsSpan(0, new SkillsPacket(state.SkillsRevision, []).Write(scratch)));
        var decoder = new PresenceDecoder(new WorldRenderCatalog(), static _ => { });
        OwnerState? owner = null;
        foreach (var packet in sink.Packets)
        {
            if (decoder.TryHandle(packet, out var update) && update?.Owner is { } received)
            {
                owner = received;
            }
        }

        Assert.True(owner is not null, "the owner state did not arrive");
        var details = owner!.Items.Single().Details;
        Assert.True(details is not null && details.Roll == ring.Roll && details.ClassAllowed && details.Usable(owner.Level, new CharacterAttributes(
            owner.Strength, owner.Spirit, owner.Talent, owner.Agility, owner.Health)), "item details did not round-trip");
        Assert.True(owner.Resistances.Fire == 12 && owner.MaxWeight == character.Derived.MaxWeight && owner.CarriedWeight == character.CarriedWeight
            && owner.BlockChance == CombatProfileFactory.ForPlayerDefender(character).BlockChance, "the equipment status did not round-trip");
        var buffer = new byte[ItemDetailsPartPacket.HeaderSize + ItemDetailEntry.Size * ItemDetailsPartPacket.MaxItemsPerPart];
        var many = Enumerable.Range(0, ItemDetailsPartPacket.MaxItemsPerPart).Select(_ => new ItemDetailEntry { InstanceId = Guid.NewGuid() }).ToArray();
        var written = new ItemDetailsPartPacket(5, 0, 1, many).Write(buffer);
        Assert.True(written <= Protocol.Binary.FrameCodec.MaxFrameSize && ItemDetailsPartPacket.Read(buffer.AsSpan(0, written)).Items.Count == many.Length,
            "a full item details part does not fit one frame");
        return Task.CompletedTask;
    }

    private sealed class PacketList : Protocol.Transport.IPacketSink
    {
        public List<byte[]> Packets { get; } = [];

        public bool Send(ReadOnlySpan<byte> packet)
        {
            Packets.Add(packet.ToArray());
            return true;
        }
    }

    private static Task ImportAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"priston-item-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, Cp949.Encoding.GetBytes(
            "*코드 \"WD120\"\n*Name \"Plunder Dagger\"\n*내구력 90 125\n*블럭율 14\n**특화랜덤 Assassin MartialArtist Fighter\n**공격력 0 6\n" +
            "**명중력 1 3\n**크리티컬 4\n**LifeRegenRange 2.6 2.8\n**특화 Knight Atalanter\n"));
        try
        {
            var parsed = new ItemTxtParser().Parse(path);
            Assert.True(parsed.JobRandom.SequenceEqual([(long)JobBitCode.Assassin, (long)JobBitCode.Fighter]), "unknown classes were stored or known ones lost");
            Assert.True(parsed.JobMask == (long)JobBitCode.Atalanta, "the fixed specialization did not keep the last class, or the misspelled Atalanter was not Atalanta");
            var record = Record("WD120") with
            {
                SpecClass = parsed.JobMask,
                SpecBonuses = parsed.Specials.Select(static special => new ItemSpecBonusRecord(0, (short)special.Bonus, special.Min, special.Max)).ToList(),
                RandomSpecClasses = parsed.JobRandom
            };
            var template = WorldDataFactory.ToItemTemplate(record);
            Assert.True(template.Spec.LevelDamage == 6 && template.Spec.LevelAttackRating == new ItemRange(1, 3) && template.Spec.Critical == 4
                && template.Spec.LifeRegenRange == new ItemFloatRange(2.6f, 2.8f), "special bonuses were not mapped");
            Assert.True(template.RandomSpecJobs.SequenceEqual([(uint)JobBitCode.Assassin, (uint)JobBitCode.Fighter]), "random classes were not kept in order");
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
