using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Loot;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ProgressionAndOwnershipTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original XP thresholds, multi-level rewards and job gate", ProgressionAsync);
        yield return ("inventory footprints, split identities and rejected swaps are atomic", GridAsync);
        yield return ("an item set down over one other takes its place and the other moves where it was or to free space", SwapAsync);
        yield return ("items received fill the bag down each column before the next, as the original sets them", FillAsync);
        yield return ("the second bag page and weapon pair hide and return their items, still carried and saved", SidesAsync);
        yield return ("a bag item on the cursor goes along to the page shown, and a page without room for it refuses the switch", CarriedPageAsync);
        yield return ("quest items go to the special item box down each column, the bag takes them when it is full, and quests find them there",
            QuestBoxAsync);
        yield return ("equipment validates requirements, handedness and original weapon formula", EquipmentAsync);
        yield return ("a received item goes on its empty slot when the character can wear it there, else to the bag", ReceivedWornAsync);
        yield return ("owned state round-trips detached copies and rejects unknown formats", PersistenceAsync);
    }

    private static Task ProgressionAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Progress");
        var attributes = character.Attributes;
        Assert.True(ExperienceTable.ForLevel(2) == 1100 && ExperienceTable.ForLevel(3) == 2620, "the curve's first thresholds changed");
        Assert.True(character.AwardExperience(2620) == 2 && character.Level == 3, "multi-level award did not resolve all thresholds");
        Assert.True(character.StatPoints == 10 && character.Attributes == attributes, "level-up silently redistributed attributes");
        Assert.True(character.TryAllocateAttribute(CharacterAttribute.Strength, 5), "available points rejected");
        Assert.True(!character.TryAllocateAttribute(CharacterAttribute.Health, 6), "unavailable points spent");
        Assert.True(character.Attributes.Strength == attributes.Strength + 5 && character.StatPoints == 5, "attribute allocation changed wrong state");
        character.AwardExperience(ExperienceTable.ForLevel(12) - character.Experience);
        Assert.True(character.Level == 12 && character.SkillPoints == 2, "even-level skill points were lost in a multi-level award");
        Assert.True(character.Life == character.MaxLife && character.Mana == character.MaxMana && character.Stamina == character.MaxStamina, "level-up pools not restored");
        character.RestoreProgress(ExperienceTable.ForLevel(100), 0);
        character.AwardExperience(1);
        Assert.True(character.Experience == ExperienceTable.ForLevel(100), "job quest XP gate bypassed");
        character.RestoreProgress(ulong.MaxValue, 0, tier: 3);
        character.AwardExperience(ulong.MaxValue);
        Assert.True(character.Experience == ulong.MaxValue, "overflow or loss of saved XP above cap");
        character.RestoreProgress(ExperienceTable.ForLevel(110), 0, tier: 3);
        character.AwardExperience(ulong.MaxValue);
        Assert.True(character.Experience == ExperienceTable.Ceiling && character.Level == ExperienceTable.MaximumLevel && ExperienceTable.MaximumLevel == 180,
            "a 64-bit award wrapped or the 180 levels are not reachable");
        Assert.True(ExperienceTable.ForLevel(121) == 21_744_927_060UL && ExperienceTable.ForLevel(180) == 8_127_624_927_060UL,
            "the level table past 120 differs");
        var lost = character.LoseExperience(ulong.MaxValue);
        Assert.True(character.Experience == ExperienceTable.ForLevel(180) && lost == ExperienceTable.Ceiling - ExperienceTable.ForLevel(180),
            "a loss went below the level's start or wrapped");
        Assert.True(KillReward.ExpForKill(ulong.MaxValue, 1, 1, 5) == ulong.MaxValue
            && PremiumRules.BoostExperience(ulong.MaxValue / 2, new PremiumTimers()) == ulong.MaxValue / 2, "kill experience wrapped");
        Assert.True(!character.TryChangeMoney(-1) && character.TryChangeMoney(10) && !character.TryChangeMoney(long.MaxValue), "money overflow/overdraft accepted");
        return Task.CompletedTask;
    }

    private static Task GridAsync()
    {
        var inventory = new Inventory();
        var large = new ItemInstance(new ItemDefinition(1, "Armor", ItemKind.Armor, GridWidth: 2, GridHeight: 3));
        Assert.True(!inventory.TryPlace(large, 11), "footprint wrapped across a row");
        Assert.True(inventory.TryPlace(large, 0), "valid footprint rejected");
        var small = new ItemInstance(new ItemDefinition(2, "Stone", ItemKind.Material));
        Assert.True(!inventory.TryPlace(small, 13), "item overlapped covered non-anchor cell");
        Assert.True(inventory.TryMove(0, 1), "self-overlapping move rejected");
        Assert.True(inventory.TryPlace(small, 0), "move did not release old footprint");
        var potion = new ItemInstance(new ItemDefinition(3, "Potion", ItemKind.Potion, MaxStack: 10), 10);
        Assert.True(inventory.TryPlace(potion, 8) && inventory.TrySplit(8, 9, 4), "valid split failed");
        Assert.True(inventory[8].InstanceId != inventory[9].InstanceId && inventory[8].Count + inventory[9].Count == 10, "split duplicated identity or quantity");
        Assert.True(inventory.TryMerge(9, 8) && inventory[8].Count == 10 && inventory[9] is null, "merge did not preserve total");
        var before = inventory.Version;
        var oversized = new ItemInstance(new ItemDefinition(4, "Oversized", ItemKind.Misc, GridWidth: 13));
        Assert.True(!inventory.TryExchange(0, oversized) && inventory[0].InstanceId == small.InstanceId && inventory.Version == before, "failed exchange removed item");
        Assert.True(!inventory.CanExchange(0, oversized) && !inventory.CanExchange(0, inventory[8]) && !inventory.TryExchange(0, inventory[8])
            && inventory.Version == before, "the exchange check accepted what the exchange refuses (an item too wide or already in the grid)");
        return Task.CompletedTask;
    }

    private static Task SwapAsync()
    {
        static ItemInstance Item(uint code, int width, int height) => new(new ItemDefinition(code, "Item" + code, ItemKind.Misc, GridWidth: width, GridHeight: height));

        // A 1x3 sword over a 2x3 armour: the sword takes the armour's place and the armour goes where the sword was.
        var inventory = new Inventory();
        var sword = Item(1, 1, 3);
        var armor = Item(2, 2, 3);
        Assert.True(inventory.TryPlace(sword, 0) && inventory.TryPlace(armor, 5), "fixture placement failed");
        Assert.True(inventory.TrySwap(0, 5) && inventory[5].InstanceId == sword.InstanceId && inventory[0].InstanceId == armor.InstanceId,
            "the sword did not take the armour's place, or the armour did not take the sword's");

        var before = inventory.Version;
        Assert.True(!inventory.TrySwap(5, 3) && inventory.Version == before, "a swap over no item was accepted");
        Assert.True(inventory.TryPlace(Item(3, 1, 1), 9) && inventory.TryPlace(Item(4, 1, 1), 21), "fixture placement failed");
        before = inventory.Version;
        Assert.True(!inventory.TrySwap(5, 9) && inventory.Version == before && inventory[5].InstanceId == sword.InstanceId,
            "a swap over two items was accepted");

        // The covered armour does not fit where a 1x1 stone was (a blocker beside it): it goes to the first free space down
        // the first column, under the stone's old cell.
        var crowded = new Inventory();
        var stone = Item(5, 1, 1);
        var other = Item(6, 2, 3);
        Assert.True(crowded.TryPlace(stone, 0) && crowded.TryPlace(Item(7, 1, 1), 1) && crowded.TryPlace(other, 5), "fixture placement failed");
        Assert.True(crowded.TrySwap(0, 5) && crowded[5].InstanceId == stone.InstanceId && crowded.Find(other.InstanceId) == crowded.Width,
            "the covered item did not go to the first free space");
        return Task.CompletedTask;
    }

    private static Task FillAsync()
    {
        var bag = new Inventory();
        var crystals = Enumerable.Range(0, 4).Select(_ => new ItemInstance(new ItemDefinition(1, "Crystal", ItemKind.Misc, GridWidth: 2, GridHeight: 2))).ToArray();
        Assert.True(crystals.All(bag.TryAdd), "a crystal did not fit");
        var anchors = crystals.Select(crystal => bag.Find(crystal.InstanceId)).ToArray();
        Assert.True(anchors.SequenceEqual([0, 2 * bag.Width, 4 * bag.Width, 2]), $"the crystals were not set down each column first: {string.Join(", ", anchors)}");
        return Task.CompletedTask;
    }

    private static Task SidesAsync()
    {
        var stone = new ItemDefinition(1, "Stone", ItemKind.Material) { CatalogKey = "MT101" };
        var sword = new ItemDefinition(2, "Sword", ItemKind.Sword, EquipmentSlot.Weapon) { CatalogKey = "WS101" };
        var axe = new ItemDefinition(3, "Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA101" };
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Sides", 10);
        var carried = new ItemInstance(stone);
        Assert.True(character.Inventory.TryAdd(carried) && character.Inventory.TryAdd(new ItemInstance(sword)), "fixture items did not fit");
        Assert.True(character.TryEquipFromInventory(character.Inventory.Find(character.OwnedItems().First(item => item.Definition == sword).InstanceId),
            EquipmentSlot.Weapon), "the sword was not equipped");

        Assert.True(character.TrySwitchBagPage(1) && character.BagPage == 1 && character.Inventory.Find(carried.InstanceId) < 0
            && character.HiddenBag.Find(carried.InstanceId) >= 0 && character.FindOwnedItem(carried.InstanceId) is not null,
            "page B did not hide page A's stone, or the stone stopped being carried");
        Assert.True(!character.TrySwitchBagPage(1), "showing the shown page changed something");
        Assert.True(character.TrySwitchWeaponSet(1) && character.GetEquipped(EquipmentSlot.Weapon) is null && character.StoredWeapon?.Definition == sword,
            "pair B did not store pair A's sword");
        Assert.True(character.Inventory.TryAdd(new ItemInstance(axe))
            && character.TryEquipFromInventory(character.Inventory.Find(character.OwnedItems().First(item => item.Definition == axe).InstanceId), EquipmentSlot.Weapon),
            "the axe was not equipped in pair B");
        var view = OwnerState.Capture(character);
        Assert.True(view.BagPage == 1 && view.WeaponSet == 1 && view.Items.All(item => item.InstanceId != carried.InstanceId),
            "the owner view shows the hidden side or not the active one");

        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Sides", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var record = CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero);
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Sides", 10);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([stone, sword, axe]));
        Assert.True(restored.BagPage == 1 && restored.WeaponSet == 1 && restored.HiddenBag.Find(carried.InstanceId) >= 0
            && restored.GetEquipped(EquipmentSlot.Weapon)?.Definition == axe && restored.StoredWeapon?.Definition == sword, "the sides were not saved and restored");
        Assert.True(restored.TrySwitchWeaponSet(0) && restored.GetEquipped(EquipmentSlot.Weapon)?.Definition == sword && restored.StoredWeapon?.Definition == axe
            && restored.TrySwitchBagPage(0) && restored.Inventory.Find(carried.InstanceId) >= 0, "switching back did not return pair A and page A");

        // The stored pair keeps the held pair's hand rule whatever order its items are restored in.
        var paired = classes.CreatePlayer(PlayerClass.Fighter, "Pair", 10);
        var shield = new ItemDefinition(4, "Shield", ItemKind.Shield, EquipmentSlot.OffHand) { CatalogKey = "DS101" };
        var greatAxe = new ItemDefinition(5, "Great Axe", ItemKind.Axe, EquipmentSlot.Weapon, TwoHanded: true) { CatalogKey = "WA102" };
        Assert.True(paired.RestoreStored(EquipmentSlot.OffHand, new ItemInstance(shield)) && !paired.RestoreStored(EquipmentSlot.Weapon, new ItemInstance(greatAxe))
            && !paired.RestoreStored(EquipmentSlot.Armor, new ItemInstance(sword)), "the stored pair took a two-handed weapon beside a shield, or a third slot");
        return Task.CompletedTask;
    }

    private static Task CarriedPageAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Pages", 10);
        var stone = new ItemInstance(new ItemDefinition(1, "Stone", ItemKind.Material) { CatalogKey = "MT101" });
        Assert.True(character.Inventory.TryPlace(stone, 5), "fixture stone did not fit");
        bool Switch(int page, Guid carried) => InventoryCommands.Apply(character,
            new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.SwitchBagPage, carried, page, 0, default));

        Assert.True(Switch(1, stone.InstanceId) && character.BagPage == 1 && character.Inventory.Find(stone.InstanceId) == 0
            && character.HiddenBag.Find(stone.InstanceId) < 0, "the stone on the cursor did not go to page B's first free cell");
        var block = new ItemDefinition(2, "Block", ItemKind.Misc, GridWidth: Inventory.DefaultWidth, GridHeight: Inventory.DefaultHeight);
        Assert.True(character.HiddenBag.TryPlace(new ItemInstance(block), 0), "fixture block did not fill page A");
        Assert.True(!Switch(0, stone.InstanceId) && character.BagPage == 1 && character.Inventory.Find(stone.InstanceId) == 0,
            "the switch to a full page took the stone or changed the page");
        Assert.True(!Switch(0, Guid.NewGuid()) && character.BagPage == 1, "a carried item the bag does not hold switched the page");
        Assert.True(Switch(0, Guid.Empty) && character.BagPage == 0 && character.HiddenBag.Find(stone.InstanceId) == 0,
            "a switch with nothing carried did not leave the stone on its page");
        return Task.CompletedTask;
    }

    private static Task QuestBoxAsync()
    {
        static ItemDefinition Kept(string key, string name, ItemKind kind) => new(ItemCodes.Parse(key), name, kind) { CatalogKey = key };
        var letters = Enumerable.Range(1, 16).Select(number => Kept($"QT1{number:D2}", $"Letter {number}", ItemKind.Quest)).ToArray();
        var honey = Kept("MA201", "Honey", ItemKind.Material);
        var oil = Kept("MA202", "Mystic Oil", ItemKind.Material);
        var stray = Kept("QT117", "Late Letter", ItemKind.Quest);
        var wing = Kept("QW101", "Metal Wing", ItemKind.Misc);
        var sword = new ItemDefinition(ItemCodes.Parse("WS101"), "Sword", ItemKind.Sword, EquipmentSlot.Weapon) { CatalogKey = "WS101" };
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Keeper", 10);

        Assert.True(character.TryReceive(new ItemInstance(letters[0])) && character.TryReceive(new ItemInstance(letters[1]))
            && character.TryReceive(new ItemInstance(letters[2])) && character.TryReceive(new ItemInstance(letters[3]))
            && character.QuestBox[0]?.Definition == letters[0] && character.QuestBox[QuestItemBox.Columns]?.Definition == letters[1]
            && character.QuestBox[2 * QuestItemBox.Columns]?.Definition == letters[2] && character.QuestBox[1]?.Definition == letters[3],
            "the box was not filled down each column, then the next");
        // The first sword goes on the empty weapon slot; the second and the stray letter go to the bag.
        Assert.True(character.TryReceive(new ItemInstance(sword)) && character.GetEquipped(EquipmentSlot.Weapon)?.Definition == sword
            && character.TryReceive(new ItemInstance(sword)) && character.TryReceive(new ItemInstance(stray))
            && character.Inventory.Items().Count() == 2, "an item the box does not keep went elsewhere than its slot or the bag");
        foreach (var letter in letters.Skip(4))
        {
            character.TryReceive(new ItemInstance(letter));
        }

        Assert.True(character.TryReceive(new ItemInstance(honey)) && character.TryReceive(new ItemInstance(oil)) && character.TryReceive(new ItemInstance(wing))
            && character.QuestBox.Occupied().Count() == QuestItemBox.Boxes && character.Inventory.Items().Any(item => item.Definition == wing),
            "a full box did not leave the next item to the bag");
        var leftOver = character.Inventory.Items().Single(item => item.Definition == wing).InstanceId;
        Assert.True(!character.TryStoreQuestItem(leftOver), "a full box took the bag's item");
        var revision = character.InventoryRevision;
        Assert.True(QuestInventory.Remove(character, letters[5].Code) == 1 && character.InventoryRevision > revision
            && QuestInventory.Count(character, letters[5].Code) == 0, "a quest removed nothing from the box, or left the revision");
        Assert.True(InventoryCommands.Apply(character, new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.StoreQuestItem,
            leftOver, 0, 0, default)) && character.QuestBox.Find(leftOver) >= 0 && character.Inventory.Find(leftOver) < 0,
            "the bag's box item did not go to the freed box");
        Assert.True(QuestInventory.RemoveInstance(character, character.QuestBox[0]!.InstanceId) && character.QuestBox[0] is null, "a box item was not removed by identity");

        var view = OwnerState.Capture(character).Items.Single(item => item.InstanceId == character.QuestBox[1]!.InstanceId);
        Assert.True(view.Slot == QuestItemBox.FirstOwnerSlot + 1 && view.QuestBox == 1 && view.BeltBox < 0 && !view.InBag && !view.Equipped,
            "the owner view lost the special item box");
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Keeper", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var record = CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero);
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Keeper", 10);
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([.. letters, honey, oil, stray, sword, wing]));
        Assert.True(Enumerable.Range(0, QuestItemBox.Boxes).All(box => restored.QuestBox[box]?.InstanceId == character.QuestBox[box]?.InstanceId)
            && restored.Inventory.Items().Count() == character.Inventory.Items().Count(), "the special item box was not saved and restored");
        return Task.CompletedTask;
    }

    private static Task EquipmentAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Equipment", 6);
        var axe = new ItemInstance(new ItemDefinition(1, "Axe", ItemKind.Axe, EquipmentSlot.Weapon, TwoHanded: true)
        {
            BaseStats = new ItemStatBlock(10, 20, 0, 3, 0, 0, 0, 0, 0, 0)
        });
        axe.Roll = axe.Roll with { SpecJob = (uint)JobBitCode.Archer };
        var shield = new ItemInstance(new ItemDefinition(2, "Shield", ItemKind.Shield, EquipmentSlot.OffHand));
        Assert.True(character.Inventory.TryPlace(axe, 0) && character.TryEquipFromInventory(0, EquipmentSlot.Weapon), "weapon equip failed");
        Assert.True(!character.CanEquip(EquipmentSlot.OffHand, shield), "two-handed weapon allowed a shield");
        Assert.True(!character.CanEquip(EquipmentSlot.Armor, axe), "weapon accepted in armor slot");
        Assert.True(character.EquipmentTotals.AttackRating == 3, "off-class specialization removed base stats");
        var attributes = character.Attributes;
        var expectedMin = 2 + 10 * (attributes.Strength + 130) / 130 + (attributes.Talent + attributes.Agility) / 40 + 1 + 30 / 16;
        var expectedMax = 4 + 20 * (attributes.Strength + 130) / 130 + (attributes.Talent + attributes.Agility) / 40 + 1;
        var attack = CombatProfileFactory.ForPlayerBasicAttack(character);
        Assert.True(attack.DamageMin == expectedMin && attack.DamageMax == expectedMax, "weapon formula double-counted bare-hand terms");
        Assert.True(character.TryUnequip(EquipmentSlot.Weapon) && character.Inventory.Find(axe.InstanceId) >= 0, "unequip lost identity");
        return Task.CompletedTask;
    }

    private static Task ReceivedWornAsync()
    {
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Wearer", 6);
        static ItemInstance Gear(uint code, ItemKind kind, EquipmentSlot slot, bool twoHanded = false) =>
            new(new ItemDefinition(code, kind.ToString(), kind, slot, TwoHanded: twoHanded));
        bool InBag(ItemInstance item) => character.Inventory.Find(item.InstanceId) >= 0;

        var armor = Gear(1, ItemKind.Armor, EquipmentSlot.Armor);
        var spare = Gear(2, ItemKind.Armor, EquipmentSlot.Armor);
        Assert.True(character.ReceiveGroundLoot(armor, 0) && character.GetEquipped(EquipmentSlot.Armor) == armor && !InBag(armor),
            "a picked up armour did not go on the empty armour slot");
        Assert.True(character.TryReceive(spare) && InBag(spare), "a second armour did not go to the bag");

        var rings = new[] { Gear(3, ItemKind.Ring, EquipmentSlot.RingLeft), Gear(4, ItemKind.Ring, EquipmentSlot.RingLeft), Gear(5, ItemKind.Ring, EquipmentSlot.RingLeft) };
        Assert.True(rings.All(character.TryReceive) && character.GetEquipped(EquipmentSlot.RingLeft) == rings[0]
            && character.GetEquipped(EquipmentSlot.RingRight) == rings[1] && InBag(rings[2]), "rings did not fill both ring slots, then the bag");

        var shield = Gear(6, ItemKind.Shield, EquipmentSlot.OffHand);
        var greatAxe = Gear(7, ItemKind.Axe, EquipmentSlot.Weapon, twoHanded: true);
        Assert.True(character.TryReceive(shield) && character.TryReceive(greatAxe) && character.GetEquipped(EquipmentSlot.OffHand) == shield
            && character.GetEquipped(EquipmentSlot.Weapon) is null && InBag(greatAxe), "a two-handed weapon was worn beside a shield");

        var boots = Gear(8, ItemKind.Boots, EquipmentSlot.Boots);
        boots.Roll = boots.Roll with { RequiredLevel = 99 };
        Assert.True(character.TryReceive(boots) && character.GetEquipped(EquipmentSlot.Boots) is null && InBag(boots),
            "boots the character cannot use were worn");

        // A full bag does not keep an item from its empty slot.
        var packed = classes.CreatePlayer(PlayerClass.Fighter, "Packed", 6);
        var block = new ItemDefinition(9, "Block", ItemKind.Misc, GridWidth: Inventory.DefaultWidth, GridHeight: Inventory.DefaultHeight);
        Assert.True(packed.Inventory.TryPlace(new ItemInstance(block), 0) && packed.HiddenBag.TryPlace(new ItemInstance(block), 0), "fixture blocks did not fill both pages");
        var amulet = Gear(10, ItemKind.Amulet, EquipmentSlot.Amulet);
        Assert.True(packed.TryReceive(amulet) && packed.GetEquipped(EquipmentSlot.Amulet) == amulet, "an amulet was refused with the bag full and its slot empty");
        Assert.True(!packed.TryReceive(Gear(11, ItemKind.Amulet, EquipmentSlot.Amulet)), "a second amulet found room in a full bag");

        // Picked up with the inventory open (into the bag, for the cursor), an armour lies in the bag beside its empty slot.
        var open = classes.CreatePlayer(PlayerClass.Fighter, "Open", 6);
        var robe = Gear(12, ItemKind.Armor, EquipmentSlot.Armor);
        Assert.True(open.ReceiveGroundLoot(robe, 0, intoBag: true) && open.GetEquipped(EquipmentSlot.Armor) is null && open.Inventory.Find(robe.InstanceId) >= 0,
            "an armour picked up for the cursor went on its slot");
        return Task.CompletedTask;
    }

    private static async Task PersistenceAsync()
    {
        var classes = new CharacterClassRegistry();
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Persist", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, record.Name);
        var definition = new ItemDefinition(1, "Potion", ItemKind.Potion, MaxStack: 10);
        var catalog = new ItemCatalog([definition]);
        character.AwardExperience(2620);
        character.TryChangeMoney(37);
        character.Inventory.TryPlace(new ItemInstance(definition, 4), 5);
        var captured = CharacterRecordFactory.Capture(record, character, 3, Vector3.Zero);
        var identity = character.Inventory[5].InstanceId;
        character.Inventory.ConsumeAt(5);
        var encoded = captured.Encode();
        var restored = new PlayerCharacter(classes.Get(PlayerClass.Fighter), record.Name, encoded.Level,
            new CharacterAttributes(encoded.Strength, encoded.Spirit, encoded.Talent, encoded.Agility, encoded.Health));
        CharacterStateCodec.Restore(restored, encoded, catalog);
        Assert.True(restored.Experience == 2620 && restored.Money == 37 && restored.StatPoints == 10, "saved progression lost");
        var veteran = encoded with { Exp = unchecked((long)ExperienceTable.Ceiling) };
        var reloaded = new PlayerCharacter(classes.Get(PlayerClass.Fighter), record.Name, ExperienceTable.MaximumLevel, restored.Attributes);
        CharacterStateCodec.Restore(reloaded, veteran, catalog);
        Assert.True(reloaded.Experience == ExperienceTable.Ceiling
            && CharacterRecordFactory.Capture(record, reloaded, 3, Vector3.Zero).Encode().Exp == veteran.Exp, "experience past 2^63 did not round-trip");
        Assert.True(restored.Inventory[5].Count == 4 && restored.Inventory[5].InstanceId == identity, "save snapshot aliases live inventory");
        var corrupt = encoded with { Inventory = new JsonText("{\"Version\":2,\"Items\":[]}") };
        var repository = new MemoryCharacters(corrupt);
        var registry = new SessionRegistry();
        var session = registry.Create(1, "owner");
        var service = new SessionAppService(registry, classes, repository, new ImmediateSaves(repository), new WorldCatalog());
        Assert.True(!(await service.EnterWorldAsync(session.SessionToken, record.Name)).Ok, "future version was treated as empty inventory");
        Assert.True(!await service.SaveCharacterAsync(session.SessionToken) && repository.Record.Inventory == corrupt.Inventory, "failed hydration overwrote saved state");
    }
}
