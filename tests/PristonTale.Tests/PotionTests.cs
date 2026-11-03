using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Gameplay.Combat;
using PristonTale.Engine3D.Formats;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class PotionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("potions restore at the EAT event once, clamp resources and cannot revive", TimingAsync);
        yield return ("the potion belt fills its boxes up to the potion space, exchanges kinds, drinks, returns to the bag and is saved", BeltAsync);
        yield return ("a potion is consumed and restores only at its commit, once, and a replay consumes nothing more", DurabilityAsync);
        yield return ("a received potion fills the boxes of the same potion, then the empty ones, and the bag takes the rest", ReceivedOnBeltAsync);
    }

    private static Task TimingAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Potion");
        character.Life = 1;
        character.Mana = 0;
        character.Stamina = 0;
        var potion = new PotionDefinition(1000, 1000, 1000, 1000, 1000, 1000);
        var random = new CombatRng(1);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        player.QueuePotion(potion);
        player.BeginSwing(2, Vector3.One, 100, 50);
        Assert.True(!player.BeginPendingPotion(600, 500), "potion interrupted an active swing");
        player.TickAction(100, out _);
        Assert.True(player.BeginPendingPotion(600, 500), "queued potion did not start after attack");
        Assert.True(player.ReportMovement(new Vector3(50, 0, 0), 0f, true, true, 0, 1000) != MovementVerdict.Accepted && player.Position == Vector3.Zero,
            "movement interrupted EAT");
        Assert.True(player.TickPotion(499, random) == PotionResource.None && character.Life == 1, "restored before event");
        Assert.True(player.TickPotion(1, random) == (PotionResource)7, "missing resource effects at event");
        Assert.True(character.Life == character.MaxLife && character.Mana == character.MaxMana
            && character.Stamina == character.MaxStamina, "restoration did not clamp to maxima");
        character.Life = 1;
        Assert.True(player.TickPotion(100, random) == PotionResource.None && character.Life == 1 && !player.HasPendingPotion,
            "potion restored twice or stayed busy");

        character.Life = 1;
        player.QueuePotion(potion);
        player.BeginPendingPotion(600, 500);
        player.TickPotion(100, random);
        player.Teleport(new Vector3(10, 0, 10));
        Assert.True(player.HasPendingPotion && player.BeginPendingPotion(600, 500) && player.TickPotion(600, random) != PotionResource.None
            && character.Life == character.MaxLife, "a warp before the potion restored lost the paid potion");
        player.QueuePotion(potion);
        player.BeginPendingPotion(600, 500);
        player.TickPotion(550, random);
        player.Teleport(new Vector3(20, 0, 20));
        Assert.True(!player.HasPendingPotion, "a potion that already restored was kept for another drink after a warp");

        player.QueuePotion(potion);
        player.BeginPendingPotion(600, 500);
        player.ApplyDamage(10000);
        Assert.True(player.TickPotion(1000, random) == PotionResource.None && player.IsDead && character.Life == 0
            && !player.HasPendingPotion, "potion revived a dead player");

        var motion = new ActorMotionController();
        Assert.True(motion.ApplyAuthoritative(MotionCode.Eat).Kind == MotionCommandKind.Restart, "EAT was not a one-shot");
        Assert.True(motion.ApplyLocomotion(MotionCode.Run).Kind == MotionCommandKind.None, "locomotion overrode EAT");
        motion.ApplyAuthoritative(MotionCode.Stand);
        Assert.True(!motion.LocomotionFrozen, "EAT completion did not release input");
        return Task.CompletedTask;
    }

    private static async Task DurabilityAsync()
    {
        var classes = new CharacterClassRegistry();
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "DurablePotion", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
        character.Life = 1;
        var definition = new ItemDefinition(1, "Health potion", ItemKind.Potion, MaxStack: 100)
        {
            CatalogKey = "PL101",
            Potion = new PotionDefinition(10, 10, 0, 0, 0, 0)
        };
        var item = new ItemInstance(definition, 3);
        character.Inventory.TryPlace(item, 0);
        var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
        var data = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(new ItemCatalog([definition]), data.Monsters, data.Npcs, data.FieldSpawns, data.Motions)
        };
        var catalog = new WorldCatalog();
        var world = new WorldServer(holder, catalog);
        var sessions = new SessionRegistry();
        var session = sessions.Create(1, "owner");
        var service = new SessionAppService(sessions, classes, repository, new ImmediateSaves(repository), catalog, world, holder);
        Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
        var outbound = Channel.CreateUnbounded<WorldSnapshot>();
        var player = WorldPlayerFactory.Create(session);
        await WorldPump.RunAsync(world, world.JoinAsync(3, player, outbound.Writer, session));
        var command = new InventoryCommand(Guid.NewGuid(), session.Character!.InventoryRevision, InventoryAction.ConsumePotion,
            item.InstanceId, 0, 1, default);
        var accepted = service.ApplyInventoryAsync(session.SessionToken, command);
        world.Tick(0.05f, 0);
        Assert.True(!accepted.IsCompleted && session.Character.Inventory[0].Count == 3 && session.Character.Life == 1 && !player.HasPendingPotion,
            "the prepared potion was consumed or healed through a shared item reference before its commit");
        await WorldPump.RunAsync(world, accepted);
        Assert.True((await accepted).Ok && session.Character.Inventory[0].Count == 2, "commit did not consume exactly one");
        for (uint tick = 0; tick < 30; tick++)
        {
            world.Tick(0.05f, tick);
        }

        Assert.True(session.Character.Life == 11 && !player.HasPendingPotion, "committed potion did not restore exactly once");
        var begin = 0;
        var applied = 0;
        while (outbound.Reader.TryRead(out var snapshot))
        {
            foreach (var effect in snapshot.Events)
            {
                begin += effect.Kind == CombatEventKind.PotionBegin ? 1 : 0;
                applied += effect.Kind == CombatEventKind.PotionApplied && effect.Resources == 1 ? 1 : 0;
            }
        }

        Assert.True(begin == 1 && applied == 1, "potion events missing or duplicated");
        var writes = repository.UpdateCount;
        var replay = service.ApplyInventoryAsync(session.SessionToken, command);
        await WorldPump.RunAsync(world, replay);
        Assert.True((await replay).Ok && repository.UpdateCount == writes && session.Character.Life == 11
            && session.Character.Inventory[0].Count == 2 && !player.HasPendingPotion, "retry consumed or healed twice");
    }

    private static Task BeltAsync()
    {
        static ItemDefinition Potion(uint code, string key) =>
            new(code, key, ItemKind.Potion, MaxStack: 100) { CatalogKey = key, Potion = new PotionDefinition(10, 10, 0, 0, 0, 0) };
        var life = Potion(1, "PL101");
        var mana = Potion(2, "PM101");
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Belt");
        Assert.True(character.Inventory.TryAdd(new ItemInstance(life, 10)) && character.Inventory.TryAdd(new ItemInstance(mana, 4)), "fixture potions did not fit");
        Guid Bag(ItemDefinition definition) => character.OwnedItems().First(item => item.Definition == definition && character.Inventory.Find(item.InstanceId) >= 0).InstanceId;
        int BagCount(ItemDefinition definition) => character.OwnedItems().Where(item => item.Definition == definition && character.Inventory.Find(item.InstanceId) >= 0).Sum(item => item.Count);
        var space = character.Derived.PotionSpace;

        Assert.True(character.TryPutOnBelt(Bag(life), 0, 0) && character.Belt[0].Count == space && BagCount(life) == 10 - space,
            "the box did not take the potion space from the bag");
        Assert.True(!character.TryPutOnBelt(Bag(life), 0, 0), "a full box took more");
        Assert.True(character.TryPutOnBelt(Bag(mana), 0, 1) && character.Belt[0].Definition == mana && character.Belt[0].Count == 1 && BagCount(life) == 10,
            "another potion did not send the box's stack back to the bag");
        Assert.True(character.TryPutOnBelt(Bag(life), 2, 0) && character.TryPutOnBelt(character.Belt[2].InstanceId, 0, 0)
            && character.Belt[0].Definition == life && character.Belt[2].Definition == mana, "two boxes did not change places");
        var before = character.Belt[0].Count;
        Assert.True(character.TryConsumeFromBelt(character.Belt[0].InstanceId) && character.Belt[0].Count == before - 1, "drinking did not take one potion");
        Assert.True(character.TryTakeFromBelt(character.Belt[2].InstanceId, -1) && character.Belt[2] is null && BagCount(mana) == 4,
            "the box's potions did not go back to the bag");
        var bagMana = character.Inventory.Find(Bag(mana));
        Assert.True(character.TryPutOnBelt(Bag(mana), 2, 1) && !character.TryTakeFromBelt(character.Belt[2].InstanceId, character.Inventory.Find(Bag(life)))
            && character.TryTakeFromBelt(character.Belt[2].InstanceId, bagMana) && character.Belt[2] is null && BagCount(mana) == 4,
            "a box's potions joined another potion's stack, or not their own");
        Assert.True(character.TryPutOnBelt(Bag(mana), 2, 1) && !character.TryTakeFromBelt(character.Belt[2].InstanceId, character.Inventory.Capacity)
            && character.TryTakeFromBelt(character.Belt[2].InstanceId, -1), "a bag anchor past the bag's capacity was not refused");

        var view = OwnerState.Capture(character).Items.Single(item => item.BeltBox == 0);
        Assert.True(view.Slot == PotionBelt.FirstOwnerSlot && !view.InBag && !view.Equipped && view.Count == before - 1, "the owner view lost the belt box");
        var starter = CharacterRecordFactory.NewStarter(classes, 1, "Belt", 0, PlayerClass.Fighter, 3) with { Id = 1 };
        var record = CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero);
        var restored = classes.CreatePlayer(PlayerClass.Fighter, "Belt");
        CharacterStateCodec.Restore(restored, record, new ItemCatalog([life, mana]));
        Assert.True(restored.Belt[0]?.InstanceId == character.Belt[0].InstanceId && restored.Belt[0].Count == before - 1 && restored.Belt[2] is null,
            "the belt was not saved and restored");
        return Task.CompletedTask;
    }

    private static Task ReceivedOnBeltAsync()
    {
        static ItemDefinition Potion(uint code, string key) =>
            new(code, key, ItemKind.Potion, MaxStack: 100) { CatalogKey = key, Potion = new PotionDefinition(10, 10, 0, 0, 0, 0) };
        var life = Potion(1, "PL101");
        var mana = Potion(2, "PM101");
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Pickup");
        int BagCount(ItemDefinition definition) => character.OwnedItems().Where(item => item.Definition == definition && character.Inventory.Find(item.InstanceId) >= 0).Sum(item => item.Count);
        var space = character.Derived.PotionSpace;
        Assert.True(space >= 2, "the fixture needs room for two potions in a box");

        // Box 1 holds one mana potion, box 2 one life potion, box 3 is empty.
        Assert.True(character.TryReceive(new ItemInstance(mana, 1)) && character.Belt[0]?.Definition == mana && BagCount(mana) == 0,
            "a received potion did not go to the first empty box");
        Assert.True(character.Inventory.TryAdd(new ItemInstance(life, 1)) && character.TryPutOnBelt(character.OwnedItems().First(item => item.Definition == life).InstanceId, 1, 1),
            "fixture life potion did not reach box 2");

        // The life potion box fills first, the empty box next, the mana box is passed over and the bag takes the rest.
        Assert.True(character.ReceiveGroundLoot(new ItemInstance(life, space - 1 + space + 3), 0), "the potion pickup was refused");
        Assert.True(character.Belt[1].Count == space && character.Belt[2]?.Definition == life && character.Belt[2].Count == space
            && character.Belt[0].Definition == mana && character.Belt[0].Count == 1 && BagCount(life) == 3,
            "the pickup did not fill the same potion's box, then the empty box, leaving the rest in the bag");
        Assert.True(character.TryReceive(new ItemInstance(mana, space)) && character.Belt[0].Count == space && BagCount(mana) == 1,
            "a mana potion did not top up its own box before the bag");
        Assert.True(character.TryReceive(new ItemInstance(life, 2)) && BagCount(life) == 5, "with its boxes full a potion did not join its bag stack");

        // With the bag full, a pickup the belt cannot hold whole is refused and nothing moves; one it can hold is taken.
        var full = classes.CreatePlayer(PlayerClass.Fighter, "Full");
        var block = new ItemDefinition(3, "Block", ItemKind.Misc, GridWidth: Inventory.DefaultWidth, GridHeight: Inventory.DefaultHeight);
        Assert.True(full.Inventory.TryPlace(new ItemInstance(block), 0) && full.HiddenBag.TryPlace(new ItemInstance(block), 0), "fixture blocks did not fill both pages");
        var tooMany = new ItemInstance(life, PotionBelt.Boxes * space + 1);
        var revision = full.InventoryRevision;
        Assert.True(!full.TryReceive(tooMany) && tooMany.Count == PotionBelt.Boxes * space + 1 && full.Belt.Occupied().Count() == 0
            && full.InventoryRevision == revision, "a refused potion pickup changed the belt or the stack");
        Assert.True(full.TryReceive(new ItemInstance(life, space)) && full.Belt[0].Count == space, "a potion the belt can hold was refused with the bag full");
        return Task.CompletedTask;
    }
}
