using Microsoft.Xna.Framework;
using PristonTale.Application.World;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Binary;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class EquipmentTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original catalog metadata enables armor and weapons without allowing two-handed shield combinations", CatalogMetadataAsync);
        yield return ("equipment resources preserve original indices, armor aliases and village bones", ResourcesAsync);
        yield return ("the assassin holds a dagger in each hand and the shaman's phantom hovers at its back", NewClassWeaponsAsync);
        yield return ("an item key's family names its kind without allocating", KindOfAsync);
        yield return ("motion selection respects item lists, class and map masks", MotionAsync);
        yield return ("equipment changes refresh public appearance over TCP", PresenceAsync);
        yield return ("remote equipment preparation preserves old actors and discards superseded swaps", ReplacementAsync);
    }

    private static Task KindOfAsync()
    {
        Assert.True(ItemEquipmentMetadata.KindOf("WS101") == ItemKind.Bow && ItemEquipmentMetadata.KindOf("ws201") == ItemKind.Sword
            && ItemEquipmentMetadata.KindOf("WT105") == ItemKind.ThrowingWeapon && ItemEquipmentMetadata.KindOf("OR201") == ItemKind.Ring,
            "a family named the wrong kind");
        Assert.True(ItemEquipmentMetadata.IsShield("DS105") && !ItemEquipmentMetadata.IsShield("OM101") && !ItemEquipmentMetadata.IsShield(null)
            && ItemEquipmentMetadata.KindOf("XX1") is null && ItemEquipmentMetadata.KindOf("WS") is null, "a shield or an unknown key misread");
        ItemEquipmentMetadata.KindOf("WM101");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var staff = ItemEquipmentMetadata.KindOf("WM101");
        Assert.True(staff == ItemKind.Staff && GC.GetAllocatedBytesForCurrentThread() == before, "the kind lookup allocated");
        return Task.CompletedTask;
    }

    private static Task CatalogMetadataAsync()
    {
        var armor = ItemEquipmentMetadata.Apply(new ItemDefinition(1, "Doom Armor", ItemKind.Armor) { CatalogKey = "DA119", RequiredLevel = 80 });
        var bow = ItemEquipmentMetadata.Apply(new ItemDefinition(2, "Short Bow", ItemKind.Sword) { CatalogKey = "WS101" });
        var axe = ItemEquipmentMetadata.Apply(new ItemDefinition(3, "Stone Axe", ItemKind.Axe) { CatalogKey = "WA101" });
        var shield = ItemEquipmentMetadata.Apply(new ItemDefinition(4, "Shield", ItemKind.Shield) { CatalogKey = "DS108" });
        var classes = new CharacterClassRegistry();
        var character = new PlayerCharacter(classes.Get(PlayerClass.Mechanician), "Catalog", 80, new CharacterAttributes(800, 800, 800, 800, 800));
        Assert.True(armor.EquipSlot == EquipmentSlot.Armor && bow.Kind == ItemKind.Bow && bow.TwoHanded && !axe.TwoHanded,
            "original equipment metadata was not restored");
        character.Equip(EquipmentSlot.Armor, new ItemInstance(armor));
        character.Equip(EquipmentSlot.Weapon, new ItemInstance(bow));
        Assert.True(!character.CanEquip(EquipmentSlot.OffHand, new ItemInstance(shield)), "two-handed weapon accepted a shield");
        character.Equip(EquipmentSlot.Weapon, new ItemInstance(axe));
        Assert.True(character.CanEquip(EquipmentSlot.OffHand, new ItemInstance(shield)), "one-handed weapon rejected a shield");
        Assert.True(!classes.CreatePlayer(PlayerClass.Mechanician, "Low level").CanEquip(EquipmentSlot.Armor, new ItemInstance(armor)),
            "catalog enrichment bypassed the armor level requirement");
        return Task.CompletedTask;
    }

    private static Task ResourcesAsync()
    {
        Assert.True(EquipmentVisuals.MotionIndex("WA101") == 0 && EquipmentVisuals.MotionIndex("WC101") == 15, "the motion indices shifted");
        Assert.True(EquipmentVisuals.MeshPath("WC101").EndsWith("itWC101.smd", StringComparison.Ordinal), "drop/equipped mesh identity changed");
        var definition = new CharacterClassRegistry().Get(PlayerClass.Mechanician).Definition;
        Assert.True(definition.MotionJobMask == 2 && new CharacterClassRegistry().Get(PlayerClass.Archer).Definition.MotionJobMask == 4, "motion and item job masks were confused");
        var armored = EquipmentVisuals.Apply(definition.DefaultVisuals, "DA108", "WC101", "DS101", false, definition.MotionJobMask);
        Assert.True(armored.Body.InxExtensionPath.EndsWith("A008.inx", StringComparison.Ordinal), "armor selected the wrong class body");
        string Body(string armor) => EquipmentVisuals.Apply(definition.DefaultVisuals, armor, "", "", false, definition.MotionJobMask).Body.InxExtensionPath;
        Assert.True(Body("DA126").EndsWith("A025.inx", StringComparison.Ordinal) && Body("DA129").EndsWith("A028.inx", StringComparison.Ordinal),
            "an armour from ordinal 26 does not wear its body sixteen slots further");
        Assert.True(Body("DA301").EndsWith("A029.inx", StringComparison.Ordinal) && Body("DA302").EndsWith("A030.inx", StringComparison.Ordinal)
            && Body("DA304").EndsWith("A032.inx", StringComparison.Ordinal) && Body("DA131").EndsWith("Ca001.inx", StringComparison.Ordinal)
            && Body("DA155").EndsWith("Ca012.inx", StringComparison.Ordinal), "the later armours or the costume bodies differ");
        Assert.True(EquipmentVisuals.MotionIndex("WA126") == EquipmentVisuals.MotionIndex("WA105")
            && EquipmentVisuals.MeshPath("WA126").EndsWith("itWA126.smd", StringComparison.Ordinal) && EquipmentVisuals.HeldInLeftHand("DS125"),
            "an item newer than the motion files has no model, motion or hand");
        Assert.True(EquipmentVisuals.WeaponBone("WA101", true) == "Bip in01", "city weapon not sheathed");
        Assert.True(EquipmentVisuals.WeaponBone("WC101", true) == "Bip weapon01", "claw should remain in hand");
        Assert.True(EquipmentVisuals.WeaponBone("WS104", true) == "Bip in-cro", "crossbow back attachment differs from BackObjBip[1]");
        Assert.True(EquipmentVisuals.WeaponBone("WS101", false) == "Bip01 L Hand", "bow uses the wrong hand");
        Assert.True(PlayerEquipmentTiming.TryGet(PlayerClass.Mechanician, "WC101", out var timing) && timing.Event0 == 3360, "claw impact retained bare-hand timing");
        return Task.CompletedTask;
    }

    private static Task NewClassWeaponsAsync()
    {
        Assert.True(EquipmentVisuals.MotionIndex("WN101") == 261 && EquipmentVisuals.MotionIndex("WN153") == 292
            && EquipmentVisuals.MotionIndex("WD101") == 293 && EquipmentVisuals.MotionIndex("WD153") == 324
            && EquipmentVisuals.MotionIndex("WD137") == 293, "the phantoms and daggers lost their motion indices");
        Assert.True(EquipmentVisuals.MeshPath("WN151").EndsWith("itWN120.smd", StringComparison.Ordinal)
            && EquipmentVisuals.MeshPath("WD136").EndsWith("itWD135.smd", StringComparison.Ordinal), "a phantom or dagger draws another model");

        var registry = new CharacterClassRegistry();
        CharacterVisualSet Armed(PlayerClass playerClass, string weapon)
        {
            var definition = registry.Get(playerClass).Definition;
            return EquipmentVisuals.Apply(definition.DefaultVisuals, "", weapon, "", false, definition.MotionJobMask);
        }

        var assassin = Armed(PlayerClass.Assassin, "WD101");
        Assert.True(EquipmentVisuals.WeaponBone(assassin, false) == "Bip weapon01" && EquipmentVisuals.TwinBone(assassin, false) == "Bip weapon05"
            && EquipmentVisuals.WeaponBone(assassin, true) == "Bip in07" && EquipmentVisuals.TwinBone(assassin, true) == "Bip in08"
            && EquipmentVisuals.WeaponHover(assassin) is null, "the assassin does not hold and hang two daggers");
        var fighter = Armed(PlayerClass.Fighter, "WD101");
        Assert.True(EquipmentVisuals.TwinBone(fighter, false) is null && EquipmentVisuals.WeaponBone(fighter, true) == "Bip in01",
            "a dagger doubled outside the assassin");
        var shaman = Armed(PlayerClass.Shaman, "WN101");
        Assert.True(EquipmentVisuals.WeaponBone(shaman, false) == "Bip in01" && EquipmentVisuals.WeaponBone(shaman, true) == "Bip in01"
            && EquipmentVisuals.TwinBone(shaman, false) is null && EquipmentVisuals.WeaponHover(shaman) is { Lift: > 3.9f and < 3.91f },
            "the shaman's phantom does not hover at its back");
        Assert.True(PlayerEquipmentTiming.TryGet(PlayerClass.Assassin, "WD101", out var stab) && stab.Frames == 40
            && PlayerEquipmentTiming.TryGet(PlayerClass.Shaman, "WN101", out var phantom) && phantom.Frames == 40,
            "the server has no attack timing for the new classes' weapons");
        return Task.CompletedTask;
    }

    private static Task MotionAsync()
    {
        var empty = new PristonMotionInfo(0, 0, 0, [], 0, 0, 0, 0, 0, 0);
        var slots = Enumerable.Repeat(empty, 10).ToList();
        var city = new PristonMotionInfo((uint)MotionCode.Stand, 1, 11, [], 1, 1, 1, 1, 0, 0) { ItemIndices = [255] };
        var armed = city with { StartFrame = 20, EndFrame = 40, MapPosition = 2, ItemIndices = [15] };
        var wrongJob = armed with { StartFrame = 50, JobCodeBit = 2 };
        slots.AddRange([city, wrongJob, armed]);
        var group = new PristonModelGroup(0, []);
        var info = new PristonModelInfo("", "", "", "", group, group, group, slots, (uint)slots.Count, "", "", "", [], 0);
        Assert.True(PristonMotionSelection.Select(info, MotionCode.Stand, new(15, 1, 2)) == armed, "job/item mask ignored");
        Assert.True(PristonMotionSelection.Select(info, MotionCode.Stand, new(15, 1, 1)) == city, "village must select the unarmed sentinel");
        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Gear", 1);
        var appearance = new PlayerAppearanceView("Gear", PlayerClass.Mechanician, 0, 0, 0, "", "");
        var player = new WorldPlayer(1, System.Numerics.Vector3.Zero, character, false, appearance);
        var cursor = new SubscriberPresence(1);
        cursor.Diff([player.ToView()]);
        character.Equip(EquipmentSlot.Weapon, new ItemInstance(new ItemDefinition(1, "Claw", ItemKind.Claw, EquipmentSlot.Weapon) { CatalogKey = "WC101" }));
        var diff = cursor.Diff([player.ToView()]);
        Assert.True(diff.Spawns.Count == 1 && diff.Spawns[0].IsSelf, "equipment was omitted from appearance refresh");
        var wire = new WireRoundTrip(1);
        var projected = wire.PushMerged(new WorldSnapshot(1, [player.ToView()], fieldId: 3)).Spawns.Single().Player!;
        Assert.True(projected.WeaponKey == "WC101" && projected.Name == "Gear", "equipment metadata changed over TCP");
        character.Equip(EquipmentSlot.Weapon, null!);
        var refreshed = wire.PushMerged(new WorldSnapshot(2, [player.ToView()], fieldId: 3));
        Assert.True(refreshed.Spawns.Single().Player!.WeaponKey == "", "unequip did not refresh the entity description");
        Assert.True(wire.PushMerged(new WorldSnapshot(3, [player.ToView()], fieldId: 3)).Spawns.Count == 0, "unchanged description was resent");
        character.Equip(EquipmentSlot.Weapon, new ItemInstance(new ItemDefinition(1, "Claw", ItemKind.Claw, EquipmentSlot.Weapon) { CatalogKey = "WC101" }));
        Assert.True(cursor.Diff([player.ToView()]).Spawns.Count == 0, "unchanged equipment resent a descriptor");
        var field = new FieldInstance(3);
        var timing = field.PlayerSwingTiming(player);
        Assert.Near(MotionTiming.DurationMs(21, PristonTale.Simulation.Characters.AnimationRate.AttackRate(character.Derived.AttackSpeed)), timing.HitAtMs);
        character.Equip(EquipmentSlot.Weapon, null!);
        Assert.True(cursor.Diff([player.ToView()]).Spawns[0].Appearance.WeaponKey == "", "unequip retained the weapon");
        return Task.CompletedTask;
    }

    private static Task ReplacementAsync()
    {
        var scene = new FakeScene();
        var composer = new FakeComposer(scene);
        var roster = new RemoteEntityManager(scene, composer, new ModelActorComposer(scene, new PristonActorLoader()), new CharacterClassRegistry(), "");
        var appearance = new PlayerAppearanceInfo("Gear", PlayerClass.Mechanician, 0, 0, 0, "", "", 1);
        var spawn = new EntitySpawnInfo(7, EntityKind.Player, false, System.Numerics.Vector3.Zero, 0, MotionState.Stand, 100, 100, appearance, 0);
        void Apply(EntitySpawnInfo descriptor) => roster.Apply(new PresenceUpdate(1, [descriptor], [], [], []));
        Apply(spawn);
        roster.Update(0);
        Assert.True(scene.Live.Count == 2, "initial avatar did not publish");
        var wait = new TaskCompletionSource();
        composer.Preparation = wait.Task;
        Apply(spawn with { Player = appearance with { WeaponKey = "WC101" } });
        roster.Update(0);
        Assert.True(scene.Live.Count == 2 && composer.Published == 1, "pending replacement removed the old avatar");
        Apply(spawn);
        wait.SetResult();
        roster.Update(0);
        Assert.True(composer.Published == 1, "superseded gear appeared after returning to the old loadout");
        composer.Preparation = Task.CompletedTask;
        Apply(spawn with { Player = appearance with { ArmorKey = "DA108" } });
        roster.Update(0);
        Assert.True(scene.Live.Count == 2 && composer.Published == 2, "replacement leaked actor handles");
        composer.FailPublication = true;
        Apply(spawn with { Player = appearance with { ArmorKey = "DA109" } });
        roster.Update(0);
        Assert.True(scene.Live.Count == 2 && composer.Published == 2, "failed replacement removed the previous avatar");
        roster.Dispose();
        Assert.True(scene.Live.Count == 0, "removed avatar retained its handles");
        return Task.CompletedTask;
    }

    private sealed class FakeComposer(FakeScene scene) : IPlayerActorComposer
    {
        public Task Preparation { get; set; } = Task.CompletedTask;
        public int Published { get; private set; }
        public bool FailPublication { get; set; }
        public Task PrepareAsync(string root, CharacterVisualSet visuals, int face) => Preparation;
        public PlayerActorHandles Compose(string root, CharacterVisualSet visuals, int face, Matrix world, MotionCode motion)
        {
            if (FailPublication)
            {
                throw new InvalidDataException("Injected equipment publication failure.");
            }

            Published++;
            return new PlayerActorHandles(scene.Create(), scene.Create());
        }
    }

    private sealed class FakeScene : IActorScene
    {
        private int _next;
        public HashSet<ActorHandle> Live { get; } = [];
        public ActorHandle Create()
        {
            var handle = new ActorHandle(_next++, 1);
            Live.Add(handle);
            return handle;
        }
        public ActorHandle AddActor(PristonActorInstance instance) => Create();
        public void SetActorWorld(ActorHandle handle, Matrix world) { }
        public void SetActorMotion(ActorHandle handle, MotionCode motion) { }
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex) { }
        public void RestartActorMotion(ActorHandle handle, MotionCode motion) { }
        public void SetActorPlaybackRate(ActorHandle handle, float rate) { }
        public void RemoveActor(ActorHandle handle) => Live.Remove(handle);
    }
}
