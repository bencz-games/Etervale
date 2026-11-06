using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Contracts.Skills;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class MonsterCrystalTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("crystal tables draw their monsters by weight, uniformly or from the event table, and pets scale with their owner", TablesAsync);
        yield return ("a crystal calls an owned pet beside its user, consumes itself and plays the return sparkle", SummonAsync);
        yield return ("billing crystals wait for the shared delay; towns, clan crystals, mercenaries and unknown souls refuse", RefusalsAsync);
        yield return ("a crystal pet hunts the nearest wild monster, credits its owner and dies when it expires", PetHuntsAsync);
        yield return ("a crystal pet follows its owner, shows in the owner's status bar and dies when dismissed, with the owner's death or left behind",
            FollowsAndDismissesAsync);
        yield return ("every pet has its own icon, a mixed crystal's pet shows its monster's crystal, and pets do not stand in each other", PetsApartAsync);
        yield return ("pets called before a step stand apart, a dismissal before the step leaves no status, a stunned pet stays put, and pets end in a town",
            PetEndsAsync);
    }

    private static Task TablesAsync()
    {
        Assert.True(MonsterCrystals.TryGet("gp109", out var mystic) && mystic.Pick(0, false) == "HOPY 2" && mystic.Pick(99, false) == "Hopy King 2"
            && mystic.Pick(39, false) == "Hobgoblin 2" && mystic.Pick(40, false) == "Decoy 2", "mystic weights");
        Assert.True(mystic.Pick(0, true) == "Zzangpy 2", "mystic event table");
        Assert.True(MonsterCrystals.TryGet("GP120", out var extreme) && extreme.Pick(34, false) == "Omicron 2" && extreme.Pick(35, false) == "Spidertrooper 2"
            && extreme.Pick(99, false) == "Omega 3" && extreme.SharedDelay, "extreme weights");
        Assert.True(MonsterCrystals.TryGet("GP117", out var skeleton) && skeleton.Pick(1, false) == "Skeleton Ranger 2" && skeleton.Pick(7, true) == "Skeleton Knight 2",
            "billing crystals draw uniformly in and out of the event");
        Assert.True(MonsterCrystals.TryGet("GP101", out var hopy) && hopy.Pick(5, false) == "HOPY 2" && !hopy.SharedDelay, "fixed crystal");
        Assert.True(MonsterCrystals.TryGet("GP205", out var cyan) && cyan.Kind == CrystalKind.SoulStone && cyan.Pick(6, false) == "Soul of Icegoblin",
            "soul stone table");
        var identities = PristonTale.WorldImport.IdentityRegistry.Monsters();
        var unknown = MonsterCrystals.All.Where(static crystal => crystal.Kind != CrystalKind.SoulStone)
            .SelectMany(static crystal => crystal.Monsters.Concat(crystal.EventMonsters)).Select(static monster => monster.Key)
            .Where(key => !identities.HasKey(key)).Distinct().ToArray();
        Assert.True(unknown.Length == 0 && MonsterCrystals.TryGet("GP104", out var bargon) && bargon.Pick(0, false) == "Bargon 2",
            $"crystals call monsters the identity registry does not know: {string.Join(", ", unknown)}");
        Assert.True(MonsterCrystals.PetLifetimeMs(50) == 340_000f && MonsterCrystals.PetLifetimeMs(100) == 240_000f && MonsterCrystals.PetBonusLife(99) == 297,
            "pet lifetime and life bonus");
        Assert.True(MonsterCrystals.EventActive(1, 12) && !MonsterCrystals.EventActive(1, 11) && MonsterCrystals.EventActive(2, 3)
            && !MonsterCrystals.EventActive(0, 15), "crystal event hours");
        Assert.True(!UsableItemCatalog.IsUsable("GP301") && !UsableItemCatalog.IsUsable("GP123") && UsableItemCatalog.IsUsable("GP122"),
            "crystals without a defined behaviour stay unusable");
        return Task.CompletedTask;
    }

    private static async Task SummonAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, [Hopy()], [], "GP101", "GP101", "GP101");
        var character = harness.Session.Character!;
        var use = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP101")));
        await harness.AdvanceAsync(use);
        Assert.True((await use).Outcome.Ok && character.OwnedItems().Count(item => item.Definition.CatalogKey == "GP101") == 2, "crystal was not consumed once");
        harness.Tick(1);
        Assert.True(harness.World.TryGetOpenField(20, out var field), "field missing");
        var pet = field.MonstersForInspection.Single(monster => monster.OwnerId == harness.Player.EntityId);
        Assert.True(pet.Def.Brood == MonsterBrood.User && pet.Def.Faction == MonsterFaction.Npc && pet.Def.Combat.MaxLife == 150 + 3, "pet is not owned");
        Assert.True(Vector3.Distance(pet.Position, harness.Player.Position) <= 60f, "pet was not called beside its owner");
        Assert.True(harness.ItemEffects().Any(effect => effect.Effect == ItemEffectKind.Return && effect.EntityId == harness.Player.EntityId),
            "return sparkle missing");
        var again = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP101")));
        await harness.AdvanceAsync(again);
        harness.Tick(1);
        Assert.True((await again).Outcome.Ok && field.MonstersForInspection.Count(monster => monster.OwnerId == harness.Player.EntityId) == 2,
            "ordinary crystals have no delay");

        // A player keeps at most ten pets: the eleventh crystal is refused and kept.
        for (var called = 2; called < MonsterCrystals.PetSlots; called++)
        {
            Assert.True(field.CallCrystalMonster(harness.Player, "GP101", "HOPY 2") is not null, "a pet under the limit was not called");
        }

        Assert.True(!field.HasCrystalPetSlot(harness.Player) && field.CallCrystalMonster(harness.Player, "GP101", "HOPY 2") is null, "an eleventh pet was called");
        var over = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP101")));
        await harness.AdvanceAsync(over);
        Assert.True((await over).Outcome.Status == ItemUseStatus.SummonLimit && harness.Item("GP101") is not null, "the eleventh crystal was not refused and kept");
        Assert.True((await over).Outcome.Message == GameText.Of(GameTextId.SummonLimit, "10"), "the eleventh crystal's refusal does not name the limit");
    }

    private static async Task RefusalsAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, [Hopy(), .. Skeletons()], [], "GP117", "GP117", "GP110", "GP114", "GP201");
        var character = harness.Session.Character!;
        var first = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP117")));
        await harness.AdvanceAsync(first);
        Assert.True((await first).Outcome.Ok, "skeleton crystal refused");
        var second = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP117")));
        await harness.AdvanceAsync(second);
        Assert.True((await second).Outcome.Status == ItemUseStatus.Delay && harness.Item("GP117") is not null, "shared delay did not refuse");
        harness.Clock.Now = harness.Clock.Now.AddSeconds(3);
        var third = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP117")));
        await harness.AdvanceAsync(third);
        Assert.True((await third).Outcome.Ok, "the delay outlived three seconds");

        var clan = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP110")));
        await harness.AdvanceAsync(clan);
        Assert.True((await clan).Outcome.Status == ItemUseStatus.ClanOnly && harness.Item("GP110") is not null, "clan crystal without a clan");
        var mercenary = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP114")));
        await harness.AdvanceAsync(mercenary);
        Assert.True((await mercenary).Outcome.Status == ItemUseStatus.CrystalRefused && harness.Item("GP114") is not null, "mercenary without a siege");
        harness.Clock.Now = harness.Clock.Now.AddSeconds(3);
        var soul = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("GP201")));
        await harness.AdvanceAsync(soul);
        Assert.True((await soul).Outcome.Status == ItemUseStatus.NotUsable && harness.Item("GP201") is not null, "soul stone without its monsters");

        var town = await SpecialItemTests.Harness.CreateAsync(3, [Hopy()], [], "GP101");
        var inTown = town.Use(new ItemUseCommand(Guid.NewGuid(), town.Session.Character!.InventoryRevision, town.Id("GP101")));
        await town.AdvanceAsync(inTown);
        Assert.True((await inTown).Outcome.Status == ItemUseStatus.NotHere && town.Item("GP101") is not null, "crystal worked in town");
    }

    private static Task PetHuntsAsync()
    {
        var wild = SkillScenario.Enemy("Wild", life: 2, sight: 0, level: 1) with { ExpReward = 100 };
        var scenario = SkillScenario.Create(PlayerClass.Fighter, 20, null, null, wild, Hopy());
        Assert.True(scenario.Field.TryFindMonsterKey("HOPY 2", out var hopy), "catalog lacks the crystal monster");
        var spawned = scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2");
        Assert.True(spawned is not null, "pet was not called");
        var prey = scenario.Spawn(wild, spawned!.Value + new Vector3(60f, 0f, 0f));
        var experience = scenario.Character.Experience;
        scenario.Run(200);
        Assert.True(!prey.IsAlive && scenario.Character.Experience > experience, "the pet did not hunt for its owner");
        var pet = scenario.Field.MonstersForInspection.Single(monster => monster.OwnerId == scenario.Player.EntityId);
        pet.ExpiresAtMs = 0;
        scenario.Run(2);
        Assert.True(!pet.IsAlive, "an expired pet kept fighting");
        return Task.CompletedTask;
    }

    private static Task FollowsAndDismissesAsync()
    {
        var scenario = SkillScenario.Create(PlayerClass.Fighter, 20, null, null, Hopy());
        Assert.True(MonsterCrystals.TryGet("GP101", out var crystal), "Hopy crystal missing");
        var code = MonsterCrystals.PetStatusCode(crystal);
        MonsterInstance Call()
        {
            scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2");
            scenario.Run(2);
            return scenario.Field.MonstersForInspection.Last(monster => monster.OwnerId == scenario.Player.EntityId && monster.IsAlive);
        }

        // The owner keeps a status for the pet with its lifetime; its icon names the crystal.
        var pet = Call();
        Assert.True(code == 0xF0000565 && scenario.Character.Effects.TryGet(code, out _)
            && scenario.Statuses.Any(value => value.StatusCode == code && value.Op == StatusEffectOp.Add), "the owner did not get the crystal's status");
        Assert.True(ItemStatuses.Name(code) == "Hopy Crystal" && ItemStatuses.Icon(code) == "image/Sinimage/Items/Event/itGP101.bmp"
            && Gameplay.Localization.DataText.StatusEffect(code) == "Summoned Pet that supports you in Battle" && !MonsterCrystals.TryGetByPetStatus(MonsterCrystals.PetStatusBase + 201, out _),
            "the crystal status shows the wrong name, icon or effect, or a soul stone has one");

        // The icon's right click asks first; Yes sends the dismissal, which names the crystal's status on the wire.
        var box = new HudCrystalDismiss();
        Assert.True(!box.Offer(0xF0000001) && !box.Confirming && box.Offer(code) && box.Confirming, "the dismissal box opened for another status, or not for a pet");
        Assert.True(box.Click(HudMessageBox.Accept.Center, out var dismissed) && dismissed == code && !box.Confirming, "Yes did not name the pet's crystal");
        Assert.True(box.Offer(code) && box.Click(HudMessageBox.Reject.Center, out var kept) && kept == 0 && !box.Confirming, "No dismissed the pet");
        Assert.True(box.Accept() == 0 && box.Offer(code) && box.Accept() == code && !box.Confirming, "Enter did not answer Yes, or answered a closed question");
        var packet = new byte[DismissCrystalPetsPacket.Size];
        Assert.True(new DismissCrystalPetsPacket(code).Write(packet) == DismissCrystalPetsPacket.Size && DismissCrystalPetsPacket.Read(packet).StatusCode == code
            && BitConverter.ToUInt32(packet, 4) == EtervaleOpcodes.DismissCrystalPets, "the dismissal did not round-trip");
        packet[8] = 0;
        Assert.Throws<InvalidDataException>(() => DismissCrystalPetsPacket.Read(packet), "a dismissal of version 0 was read");

        // The pet follows its owner as a skill summon does, and is recalled beside a far owner.
        scenario.Player.Teleport(scenario.Player.Position + new Vector3(0, 0, 260));
        Assert.True(scenario.Run(20 * 10, () => Vector3.Distance(pet.Position, scenario.Player.Position) < 120f) > 0, "the pet did not follow its owner");
        scenario.Player.Teleport(scenario.Player.Position + new Vector3(0, 0, 1200));
        scenario.Run(2);
        Assert.True(MathF.Abs(Vector3.Distance(pet.Position, scenario.Player.Position) - 48f) < 2f, "the pet was not recalled beside a far owner");

        // A second pet gets its own status in the next slot; a dismissal ends only the pet of its status, whose status ends
        // with the next step.
        var second = Call();
        var secondCode = MonsterCrystals.PetStatusCode(crystal, 1);
        Assert.True(secondCode == 0xF0001565 && scenario.Character.Effects.TryGet(secondCode, out _) && MonsterCrystals.TryGetByPetStatus(secondCode, out var shown)
            && shown == crystal && MonsterCrystals.PetSlot(secondCode) == 1 && ItemStatuses.Name(secondCode) == "Hopy Crystal", "the second pet has no status of its own");
        Assert.True(Vector3.Distance(pet.Position, second.Position) >= 32f, "the second pet was called into the first");
        Assert.True(scenario.Field.DismissCrystalPets(scenario.Player, code) && !pet.IsAlive && second.IsAlive, "the dismissal did not end only its pet");
        scenario.Run(2);
        Assert.True(!scenario.Character.Effects.TryGet(code, out _) && scenario.Character.Effects.TryGet(secondCode, out _)
            && scenario.Statuses.Any(value => value.StatusCode == code && value.Op == StatusEffectOp.Remove), "the dismissed pet's status stayed, or took the other's");
        Assert.True(!scenario.Field.DismissCrystalPets(scenario.Player, code), "a dismissal without its pet was taken");

        // A pet that falls takes its status with it.
        second.ApplyDamage(second.Life);
        scenario.Run(2);
        Assert.True(!scenario.Character.Effects.TryGet(secondCode, out _), "a dead pet's status stayed");

        // The pets die with the status when the owner dies, and stay behind when the owner leaves the field.
        var third = Call();
        scenario.Player.ApplyDamage(scenario.Character.Life);
        scenario.Run(2);
        Assert.True(!third.IsAlive, "the pet outlived its owner's death");
        scenario.Player.Revive(scenario.Character.MaxLife, 0f);
        var fourth = Call();
        scenario.Field.ArriveWithoutCrystalPets(scenario.Player);
        scenario.Run(1);
        Assert.True(!scenario.Character.Effects.TryGet(code, out _), "a status that came from another field stayed");
        scenario.Field.Remove(scenario.Player.EntityId);
        scenario.Run(2);
        Assert.True(!fourth.IsAlive, "the pet outlived its owner leaving the field");
        return Task.CompletedTask;
    }

    private static Task PetsApartAsync()
    {
        var found = MonsterCrystals.TryGet("GP109", out var mystic);
        found &= MonsterCrystals.TryGet("GP102", out var hobgoblin);
        found &= MonsterCrystals.TryGet("GP117", out var skeleton);
        Assert.True(found, "crystals missing");
        Assert.True(MonsterCrystals.ShownCrystal(mystic, "Hobgoblin 2") == hobgoblin && MonsterCrystals.ShownCrystal(mystic, "wild goblin") == hobgoblin
            && MonsterCrystals.ShownCrystal(skeleton, "Skeleton Archer 2") == skeleton, "a pet shows the wrong crystal");

        var scenario = SkillScenario.Create(PlayerClass.Fighter, 20, null, null, Hopy(), Hobgoblin());
        scenario.Field.CallCrystalMonster(scenario.Player, "GP109", "Hobgoblin 2");
        scenario.Field.CallCrystalMonster(scenario.Player, "GP109", "Hobgoblin 2");
        scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2");
        scenario.Run(2);
        var codes = scenario.Character.Effects.Effects.Select(effect => effect.Code).Where(code => MonsterCrystals.TryGetByPetStatus(code, out _)).ToArray();
        Assert.True(codes.Length == 3 && codes.Distinct().Count() == 3 && codes.Count(code => ItemStatuses.Icon(code) == "image/Sinimage/Items/Event/itGP102.bmp") == 2,
            "three pets did not get three icons, or the Mystic's hobgoblins did not show their crystal");

        // Pets on one spot (as a recall onto a crowd could leave them) walk apart while they stand idle.
        var pets = scenario.Field.MonstersForInspection.Where(monster => monster.OwnerId == scenario.Player.EntityId).ToArray();
        foreach (var pet in pets)
        {
            pet.Position = pets[0].Position;
        }

        scenario.Run(20 * 3);
        for (var first = 0; first < pets.Length; first++)
        {
            for (var other = first + 1; other < pets.Length; other++)
            {
                Assert.True(Vector3.Distance(pets[first].Position, pets[other].Position) >= 31f, "pets on one spot stayed in each other");
            }
        }

        return Task.CompletedTask;
    }

    private static Task PetEndsAsync()
    {
        var scenario = SkillScenario.Create(PlayerClass.Fighter, 20, null, null, Hopy());
        Assert.True(MonsterCrystals.TryGet("GP101", out var crystal), "Hopy crystal missing");
        var code = MonsterCrystals.PetStatusCode(crystal);

        // Pets called before a step each see the one called before them.
        scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2");
        scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2");
        var called = scenario.Field.MonstersForInspection.Where(monster => monster.OwnerId == scenario.Player.EntityId).ToArray();
        Assert.True(called.Length == 2 && Vector3.Distance(called[0].Position, called[1].Position) >= 32f, "two pets called before a step landed on one spot");

        // A dismissal before the step that starts the pet's status ends both: no status is left without its pet.
        Assert.True(scenario.Field.DismissCrystalPets(scenario.Player, code) && called.Count(pet => !pet.IsAlive) == 1, "a pet dismissed before its status started stayed");
        scenario.Run(2);
        Assert.True(!scenario.Character.Effects.TryGet(code, out _), "a dismissed pet's status started after its dismissal");

        // A stunned pet standing in another pet does not walk out of it.
        var pet = called.Single(monster => monster.IsAlive);
        scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2");
        scenario.Field.QueueItemEffect(pet.EntityId, new EffectSpec(0x01020003, EffectSourceKind.PlayerSkill, true, [], control: ControlFlags.Stun), 60000);
        scenario.Run(1);
        var other = scenario.Field.MonstersForInspection.Last(monster => monster.OwnerId == scenario.Player.EntityId && monster.IsAlive);
        pet.Position = other.Position;
        var stunnedAt = pet.Position;
        scenario.Run(10);
        Assert.True(pet.Effects.Has(ControlFlags.Stun) && Vector3.Distance(pet.Position, stunnedAt) < 1f, "a stunned pet walked");

        // The pets die, and their statuses end, when their owner enters a town.
        scenario.Player.CrossGate(inVillage: true);
        scenario.Run(2);
        Assert.True(!pet.IsAlive && !other.IsAlive && !scenario.Character.Effects.Effects.Any(effect => MonsterCrystals.TryGetByPetStatus(effect.Code, out _)),
            "a pet outlived its owner entering a town, or its status stayed");
        return Task.CompletedTask;
    }

    private static MonsterDefinition Hobgoblin() => SkillScenario.SummonModel("Hobgoblin", 51) with
    {
        Key = "Hobgoblin 2",
        Combat = new MonsterCombatStats(4, 400, new MonsterRange(5, 6), 6, 50, 50, 5, 0, 150, ResistanceBlock.Zero)
    };

    private static MonsterDefinition Hopy() => SkillScenario.SummonModel("HOPY", 50) with
    {
        Key = "HOPY 2",
        Combat = new MonsterCombatStats(4, 400, new MonsterRange(5, 6), 6, 50, 50, 5, 0, 150, ResistanceBlock.Zero)
    };

    private static MonsterDefinition[] Skeletons() =>
    [
        SkillScenario.SummonModel("Skeleton Archer", 51) with { Key = "Skeleton Archer 2" },
        SkillScenario.SummonModel("Skeleton Ranger", 52) with { Key = "Skeleton Ranger 2" },
        SkillScenario.SummonModel("Skeleton Warrior", 53) with { Key = "Skeleton Warrior 2" },
        SkillScenario.SummonModel("Skeleton Knight", 54) with { Key = "Skeleton Knight 2" }
    ];
}
