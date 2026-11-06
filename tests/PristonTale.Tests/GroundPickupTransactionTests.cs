using PristonTale.Persistence.Abstractions.GameLogs;
using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class GroundPickupTransactionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a pickup commits at once, its capture goes to storage, and a retry replays without duplication", DurableAsync);
        yield return ("a thrown item (the asked part of a stack) or gold leaves the character and lies on the ground where the player stands", ThrowAsync);
        yield return ("a full page shown sends a pickup to the hand with the inventory open, else to the other page; the hand crosses pages and is saved", HandAsync);
    }

    private static async Task ThrowAsync()
    {
        var potion = new ItemDefinition(101, "Mini Life Potion", ItemKind.Potion, MaxStack: 100) { CatalogKey = "PL101" };
        var sword = new ItemDefinition(102, "Short Sword", ItemKind.Sword, EquipmentSlot.Weapon) { CatalogKey = "WS101" };
        var quest = new ItemDefinition(103, "Quest Note", ItemKind.Quest) { CatalogKey = "QT101" };
        Assert.True(!ItemDropRules.CanDrop(ItemKind.Misc, "BI142") && !ItemDropRules.CanDrop(ItemKind.Amulet, "DA135")
            && !ItemDropRules.CanDrop(ItemKind.Ring, "OA231") && !ItemDropRules.CanDrop(ItemKind.Misc, "GF101") && ItemDropRules.CanDrop(ItemKind.Potion, "PL101")
            && ItemDropRules.CanDrop(ItemKind.Amulet, "DA130"), "the drop rules differ from the original's lists");
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Thrower", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        character.RestoreProgress(0, 500);
        Assert.True(character.Inventory.TryAdd(new ItemInstance(potion, 5)) && character.Inventory.TryAdd(new ItemInstance(sword))
            && character.Inventory.TryAdd(new ItemInstance(quest)), "fixture items did not fit");
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(new ItemCatalog([potion, sword, quest]), empty.Monsters, empty.Npcs, empty.FieldSpawns, empty.Motions)
        };
        var catalog = new WorldCatalog([new FieldDef("fixture", "Fixture", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 }]);
        var log = new RecordingLog();
        var world = new WorldServer(holder, catalog, log: log);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, new ImmediateSaves(repository), catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "fixture entry failed");
        var outbox = new WorldOutbox();
        await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbox.Writer, session));

        InventoryCommand Throw(ItemDefinition definition, int count)
        {
            var anchor = Enumerable.Range(0, session.Character!.Inventory.Capacity).First(index => session.Character.Inventory[index]?.Definition == definition);
            return new InventoryCommand(Guid.NewGuid(), session.Character.InventoryRevision, InventoryAction.Drop,
                session.Character.Inventory[anchor]!.InstanceId, 0, count, default);
        }

        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(potion, 2)))).Ok, "two potions were not thrown");
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(sword, 1)))).Ok, "the sword was not thrown");
        var refused = await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(quest, 1)));
        Assert.True(!refused.Ok && refused.Message == GameText.Of(GameTextId.CannotDrop), $"a quest item was thrown or refused as {refused.Message}");
        InventoryCommand Gold(int amount) =>
            new(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.DropGold, Guid.Empty, 0, amount, default);
        Assert.True(!(await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Gold(501)))).Ok
            && (await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Gold(120)))).Ok
            && session.Character!.Money == 380 && repository.Record.Money == 380, "gold beyond what is carried was thrown, or 120 gold was not taken and saved");
        var carried = session.Character!.Inventory;
        Assert.True(Enumerable.Range(0, carried.Capacity).Sum(index => carried[index]?.Definition == potion ? carried[index]!.Count : 0) == 3
            && Enumerable.Range(0, carried.Capacity).All(index => carried[index]?.Definition != sword)
            && Enumerable.Range(0, carried.Capacity).Any(index => carried[index]?.Definition == quest), "the bag kept the wrong items");
        // A throw is stored before it lands: storage refusing it keeps the item in the bag.
        repository.BeforeUpdate = _ => Task.FromResult(false);
        var unstored = await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, Throw(potion, 1)));
        repository.BeforeUpdate = null;
        Assert.True(!unstored.Ok && Enumerable.Range(0, carried.Capacity).Sum(index => carried[index]?.Definition == potion ? carried[index]!.Count : 0) == 3,
            "a throw storage refused left the bag");

        GroundDropState[] ground = [];
        for (uint tick = 0; tick < 20 && ground.Length < 3; tick++)
        {
            world.Tick(0.05f, tick);
            while (outbox.Reader.TryRead(out var snapshot))
            {
                if (snapshot.GroundDrops.Length > 0)
                {
                    ground = snapshot.GroundDrops;
                }
            }
        }

        Assert.True(ground.Any(drop => drop.View.CatalogKey == "PL101" && drop.View.Amount == 2 && drop.Public)
            && ground.Any(drop => drop.View.CatalogKey == "WS101" && drop.View.Amount == 1)
            && ground.Any(drop => drop.View.Gold && drop.View.CatalogKey == ItemCodes.GoldKey && drop.View.Amount == 120 && drop.Public)
            && ground.All(drop => MathF.Max(MathF.Abs(drop.View.Position.X), MathF.Abs(drop.View.Position.Z)) == FieldGroundLoot.CellSpacing)
            && ground.Select(drop => drop.View.Position).Distinct().Count() == ground.Length,
            $"the ground holds {string.Join(", ", ground.Select(drop => $"{drop.View.CatalogKey} x{drop.View.Amount} at {drop.View.Position}"))}");

        // Each throw is logged from the character to the ground under its drop's id; picking up what a character threw is
        // logged from that character under the same id.
        var thrownSword = log.Rows.Single(row => row.ItemCode == "WS101");
        Assert.True(log.Rows.Count == 3 && log.Rows.All(row => row.Kind == ItemTransferKind.GroundThrow && row.FromCharacterId == starter.Id
                && row.ToCharacterId is null && row.FieldId == 3)
            && log.Rows.Any(row => row.ItemCode == "PL101" && row.Amount == 2) && log.Rows.Any(row => row.ItemCode is null && row.Amount == 120),
            "the throws were not logged");
        var swordDrop = ground.Single(drop => drop.View.CatalogKey == "WS101").View;
        Assert.True(swordDrop.Id.ToString() == thrownSword.TransferId, "the throw is not logged under its drop's id");
        Assert.True(await WorldPump.RunAsync(world, world.ReportMovementAsync(3, session.EntityId, swordDrop.Position, 0f, true, true, 0))
            == MovementVerdict.Accepted, "the walk to the sword was refused");
        var pickup = new InventoryCommand(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.Pickup, swordDrop.Id, 0, 1, default);
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, pickup))).Ok, "the sword was not picked up");
        Assert.True(log.Rows[^1] is { Kind: ItemTransferKind.GroundPickup, ItemCode: "WS101", Amount: 1 } picked && picked.TransferId == thrownSword.TransferId
            && picked.FromCharacterId == starter.Id && picked.ToCharacterId == starter.Id, "the pickup of a thrown item was not logged");

        // With the inventory open a pickup asks for the bag: the potions lie there as a stack of their own, for the cursor,
        // beside the bag's own; a destination the pickup does not know is refused.
        var potionDrop = ground.Single(drop => drop.View.CatalogKey == "PL101").View;
        for (uint tick = 100; tick < 140; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(await WorldPump.RunAsync(world, world.ReportMovementAsync(3, session.EntityId, potionDrop.Position, 0f, true, true, 0))
            == MovementVerdict.Accepted, "the walk to the potions was refused");
        InventoryCommand PickUp(int destination) =>
            new(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.Pickup, potionDrop.Id, destination, 1, default);
        Assert.True(!(await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, PickUp(7)))).Ok, "a pickup to an unknown destination was taken");
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, PickUp(InventoryCommand.PickupToBag)))).Ok, "the potions were not picked up");
        var bag = session.Character!.Inventory;
        Assert.True(Enumerable.Range(0, bag.Capacity).Count(index => bag[index]?.Definition == potion) == 2, "the potions picked up for the cursor joined the bag's stack");

        // An item in the hand is thrown as a bag item is.
        var inHand = new ItemInstance(sword);
        Assert.True(session.Character!.TryHold(inHand), "the fixture's hand was taken");
        var handThrow = new InventoryCommand(Guid.NewGuid(), session.Character.InventoryRevision, InventoryAction.Drop, inHand.InstanceId, 0, 1, default);
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, handThrow))).Ok && session.Character!.Held is null,
            "the item in the hand was not thrown");
    }

    private static Task HandAsync()
    {
        var potion = new ItemDefinition(101, "Mini Life Potion", ItemKind.Potion, MaxStack: 100) { CatalogKey = "PL101" };
        var stone = new ItemDefinition(102, "Stone", ItemKind.Misc) { CatalogKey = "MI101" };
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Hand");
        while (character.Inventory.TryAdd(new ItemInstance(stone)))
        {
        }

        // With the inventory closed, a full page shown sends the item to the other page.
        var closed = new ItemInstance(stone);
        Assert.True(character.ReceiveGroundLoot(closed, 0) && character.HiddenBag.Find(closed.InstanceId) >= 0 && character.Held is null,
            "a pickup with the page shown full did not go to the other page");

        // With it open, the item goes to the hand, which shows on the cursor; a second pickup with the hand full goes as any item.
        var picked = new ItemInstance(potion, 3);
        Assert.True(character.ReceiveGroundLoot(picked, 0, intoBag: true) && character.Held?.InstanceId == picked.InstanceId
            && OwnerState.Capture(character).Items.Single(item => item.Held) is { InstanceId: var shown, QuestBox: -1, InBag: false } && shown == picked.InstanceId,
            "a pickup with the inventory open and the page shown full did not go to the hand");
        var second = new ItemInstance(stone);
        Assert.True(character.ReceiveGroundLoot(second, 0, intoBag: true) && character.HiddenBag.Find(second.InstanceId) >= 0 && character.Held?.InstanceId == picked.InstanceId,
            "a pickup with the hand full did not go to the other page");

        // The hand stays across a page switch and is set down on the page shown.
        var last = character.Inventory.Capacity - 1;
        Assert.True(character.TrySwitchBagPage(1) && character.Held?.InstanceId == picked.InstanceId, "the hand did not stay across a page switch");
        var move = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.Move, picked.InstanceId, last, 0, default);
        Assert.True(InventoryCommands.Apply(character, move) && character.Held is null && character.Inventory[last]?.InstanceId == picked.InstanceId,
            "the held item was not set down on the page shown");

        // Set down over one item, it takes that item's place and the item goes to the hand; the hand is saved.
        Assert.True(character.TrySwitchBagPage(0) && character.TryHold(new ItemInstance(potion, 2)), "the fixture's hand was taken");
        var covered = character.Inventory[0]!;
        var swap = new InventoryCommand(Guid.NewGuid(), character.InventoryRevision, InventoryAction.Swap, character.Held!.InstanceId, 0, 0, default);
        Assert.True(InventoryCommands.Apply(character, swap) && character.Held?.InstanceId == covered.InstanceId && character.Inventory[0]?.Definition == potion,
            "the held item did not exchange places with the item under it");
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Hand", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Hand");
        CharacterStateCodec.Restore(restored, CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero), new ItemCatalog([potion, stone]));
        Assert.True(restored.Held?.InstanceId == covered.InstanceId && restored.OwnedItems().Count() == character.OwnedItems().Count(), "the hand was not saved");
        return Task.CompletedTask;
    }

    private static async Task DurableAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Loot", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var monster = new MonsterDefinition(1, "Loot fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "")
        {
            ExpReward = 105,
            Drops = new DropTable([DropEntry.GoldSlot(100, 8, 8)], 1, false),
            // Monsters walk through players as in the original, so the fixture chases the player to stay in reach.
            Ai = new MonsterAi(5, MonsterNature.Evil, 200, 1, 0, MonsterRange.Zero, MonsterActiveHour.Any)
        };
        var spawn = new FieldSpawnData([new SpawnPoint(24, 0, true)], [new SpawnTableEntry(1, 100)], [], [])
        {
            MaxConcurrentMonsters = 1,
            MaxPerSpawnPoint = 1
        };
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, new MonsterCatalog([monster]), empty.Npcs,
                new Dictionary<int, FieldSpawnData> { [3] = spawn }, empty.Motions)
        };
        var catalog = new WorldCatalog([new FieldDef("fixture", "Fixture", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 }]);
        var log = new RecordingLog();
        var world = new WorldServer(holder, catalog, log: log);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, new ImmediateSaves(repository), catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "fixture entry failed");
        var outbox = new WorldOutbox();
        await WorldPump.RunAsync(world, world.JoinAsync(3, WorldPlayerFactory.Create(session), outbox.Writer, session));
        WorldSnapshot snapshot = default;
        for (uint tick = 0; tick < 100; tick++)
        {
            world.Tick(0.05f, tick);
            if (outbox.Reader.TryRead(out snapshot) && snapshot.Entities.Any(entity => entity.Kind == EntityKind.Monster))
            {
                break;
            }
        }

        var target = snapshot.Entities.Single(entity => entity.Kind == EntityKind.Monster);
        await WorldPump.RunAsync(world, world.SetAttackTargetAsync(3, session.EntityId, target.EntityId, true));
        for (uint tick = 0; tick < 800; tick++)
        {
            world.Tick(0.05f, tick);
            if (outbox.Reader.TryRead(out snapshot) && snapshot.GroundDrops.Length > 0)
            {
                break;
            }
        }

        var drop = snapshot.GroundDrops.Single().View;
        Assert.True(session.Character!.Experience == 525, "production host omitted configured XP bonus");
        // A drop falls on a cell around the corpse; the client walks to it, once the killing swing ends, before picking it up.
        for (uint tick = 800; tick < 840; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(await WorldPump.RunAsync(world, world.ReportMovementAsync(3, session.EntityId, drop.Position, 0f, true, true, 0)) == MovementVerdict.Accepted,
            "the walk to the drop was refused");
        long money = session.Character.Money;
        var command = new InventoryCommand(Guid.NewGuid(), session.Character.InventoryRevision, InventoryAction.Pickup, drop.Id, 0, 1, default);
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok, "the pickup failed");
        Assert.True(session.Character.Money == money + 8 && repository.Record.Money == money + 8, "the gold was not taken, or its capture not stored");
        Assert.True(log.Rows.Count == 0, "a monster's drop was logged as a transfer");
        int writes = repository.UpdateCount;
        Assert.True((await WorldPump.RunAsync(world, service.ApplyInventoryAsync(session.SessionToken, command))).Ok && repository.UpdateCount == writes,
            "retry duplicated ground reward or storage");
        Assert.True(session.Character.Money == money + 8, "retry granted gold twice");
    }
}
