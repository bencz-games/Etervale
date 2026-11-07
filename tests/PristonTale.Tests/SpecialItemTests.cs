using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class SpecialItemTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("usable item catalog maps premium, travel, scroll and orb items by catalog key in any case", CatalogAsync);
        yield return ("premium timers refuse running kinds, packages, both vampiric kinds and a second helper pet", ConflictsAsync);
        yield return ("premium formulas return life, mana, costs, experience, drops and weight like the originals", FormulasAsync);
        yield return ("a drop-bonus item adds one main drop roll at its chance", DropBonusAsync);
        yield return ("premium timers persist with their real-clock expiry and expired ones are dropped on entry", PersistenceAsync);
        yield return ("an EXP potion is consumed, starts its timer at the commit and is stored, replays once and boosts kill experience", ExperiencePotionAsync);
        yield return ("a Teleport Core checks the level, moves the player to the field and is consumed only then", TeleportCoreAsync);
        yield return ("a Union Core binds to a registered clan's member, then takes another member of that clan to it and is spent", UnionCoreAsync);
        yield return ("stones reset stats and skills, hair tints refuse the same hair, skill masters train the tier", ProgressionItemsAsync);
        yield return ("scrolls and force orbs start item effects; Eternal Life guards and Fatal Edge excludes Avert", ScrollsAndOrbsAsync);
        yield return ("a firecracker publishes its effect to the field and cheers, and the mobile shop sells without an NPC", FirecrackerAndShopAsync);
        yield return ("item use, result, premium timer and item effect messages round-trip", WireAsync);
        yield return ("vampiric items heal per attack and helper pets and the Phoenix strike the owner's target", PetsAndLeechAsync);
        yield return ("presence carries premium timers, item effects and the pets of a player's descriptor", PresenceAsync);
    }

    private static Task CatalogAsync()
    {
        Assert.True(UsableItemCatalog.TryGet("bi113", out var exp) && exp.Grants[0] == new PremiumGrant(PremiumEffectKind.ExperienceUp, 30, UsableItemCatalog.Day),
            "EXP potion rule");
        Assert.True(UsableItemCatalog.TryGet("BI202", out var bronze) && bronze.Grants.Count == 2 && bronze.Conflict == PremiumConflict.ThirdEyesPackage
            && bronze.Grants[0].Value == 20 && bronze.Grants[1].Value == 10, "bronze package rule");
        Assert.True(UsableItemCatalog.TryGet("BI245", out var ultra) && ultra.Grants[0].Value == 50 && ultra.Grants[1].Value == 40
            && ultra.Grants[0].DurationSeconds == 3 * UsableItemCatalog.Hour, "ultra package rule");
        Assert.True(UsableItemCatalog.TryGet("BI152", out var terry) && terry.MinimumLevel == 10 && terry.Grants[0].Kind == PremiumEffectKind.HelpPetTerry
            && terry.Grants[0].DurationSeconds == 30 * UsableItemCatalog.Day, "helper pet rule");
        Assert.True(UsableItemCatalog.TryGet("BI105", out var life) && life.Action == UsableItemAction.ScrollEffect && life.ForbiddenInVillage, "scroll rule");
        Assert.True(UsableItemCatalog.TryGet("BI108", out var core) && core.Action == UsableItemAction.TeleportCore, "teleport core rule");
        Assert.True(UsableItemCatalog.TryGet("EC102", out var ether) && ether.Action == UsableItemAction.ReturnToTown && ether.Argument == 9, "ether core rule");
        Assert.True(UsableItemCatalog.TryGet("BI222", out var hair) && hair.Argument == 12, "hair tint M rule");
        Assert.True(!UsableItemCatalog.IsUsable("BI110") && !UsableItemCatalog.IsUsable("BI104") && !UsableItemCatalog.IsUsable("FO113"),
            "materials, the passive scroll and unrouted orbs are not usable");
        Assert.True(ForceOrbs.TryGet("FO112", out var enigma) && enigma.Damage == 140 && enigma.Percent == 20 && enigma.DurationSeconds == 3600
            && ForceOrbs.TryGet("FO127", out var murky) && murky.Magic && murky.MinimumLevel == 60 && murky.Icon.EndsWith("FOS_027.tga"), "force orb tables");
        Assert.True(TeleportCoreDestinations.All.Count == 37 && TeleportCoreDestinations.PageCount == 4
            && TeleportCoreDestinations.All[33].RequiredLevel == 105 && TeleportCoreDestinations.All[36].RequiredLevel == 115
            && new WorldCatalog().TryGetFieldByCode(TeleportCoreDestinations.All[36].FieldCode, out _),
            "teleport core table");
        foreach (var destination in TeleportCoreDestinations.All)
        {
            Assert.True(new WorldCatalog().TryGetFieldByCode(destination.FieldCode, out _), $"teleport destination {destination.Name} has no field");
        }

        return Task.CompletedTask;
    }

    private static async Task UnionCoreAsync()
    {
        var harness = await Harness.CreateAsync(3, "EC105");
        var character = harness.Session.Character!;
        async Task<ItemUseReply> UseCore()
        {
            var use = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("EC105")));
            await harness.AdvanceAsync(use);
            return await use;
        }

        Assert.True((await UseCore()).Outcome.Status == ItemUseStatus.ClanOnly, "a core worked without a clan");
        harness.Player.Clan = new Simulation.Social.Clans.ClanTag(7, "Lions", 0);
        Assert.True((await UseCore()).Outcome.Status == ItemUseStatus.RegisteredClanOnly, "a core worked in a clan still registering");
        harness.Player.Clan = new Simulation.Social.Clans.ClanTag(7, "Lions", 0, Registered: true);
        var bound = await UseCore();
        Assert.True(bound.Outcome is { Ok: true, Message.Id: GameTextId.UnionCoreBound } && harness.Item("EC105")?.Link == character.Name
            && harness.Repository.Record.Inventory.Value.Contains(character.Name, StringComparison.Ordinal), "the core was not bound to its user and stored");
        Assert.True((await UseCore()).Outcome.Status == ItemUseStatus.UnionCoreOwn && harness.Item("EC105") is not null, "the binder used its own core");
        var reloaded = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, character.Name);
        CharacterStateCodec.Restore(reloaded, harness.Repository.Record, harness.Holder.Current.Items);
        Assert.True(reloaded.OwnedItems().Single(item => item.Definition.CatalogKey == "EC105").Link == character.Name, "the bound core lost its member when reloaded");

        harness.Item("EC105")!.Link = "Ally";
        Assert.True((await UseCore()).Outcome.Status == ItemUseStatus.UnionCoreAway, "a core reached a member out of the world");
        var castle = await harness.JoinAsync("Guard", FieldCodes.Castle, 3);
        castle.Clan = harness.Player.Clan;
        var office = await harness.JoinAsync("Clerk", FieldCodes.OfficeRoom, 4);
        office.Clan = harness.Player.Clan;
        var catalog = new WorldCatalog();
        var deep = catalog.AllFields.First(field => field.LimitLevel > 1 && field.LimitLevel != FieldEntryLevels.Closed && field.State != FieldState.Room
            && !TravelUsesBlocked(field.FieldCode));
        var veteran = await harness.JoinAsync("Veteran", deep.FieldCode, 5);
        veteran.Clan = harness.Player.Clan;
        foreach (var (name, status) in new[] { ("Guard", ItemUseStatus.UnionCoreAway), ("Clerk", ItemUseStatus.UnionCoreAway), ("Veteran", ItemUseStatus.LevelTooLow) })
        {
            harness.Item("EC105")!.Link = name;
            Assert.True((await UseCore()).Outcome.Status == status && harness.Item("EC105") is not null, $"a core reaching {name} did not answer {status}");
        }

        harness.Item("EC105")!.Link = "Ally";
        var ally = await harness.JoinAsync("Ally", 4, 2);
        ally.Clan = new Simulation.Social.Clans.ClanTag(8, "Tigers", 0, Registered: true);
        Assert.True((await UseCore()).Outcome.Status == ItemUseStatus.UnionCoreWrongClan, "a core reached another clan's member");
        ally.Clan = harness.Player.Clan;
        var travelled = await UseCore();
        Assert.True(travelled.Outcome.Ok && harness.Item("EC105") is null && harness.Repository.Record.FieldId == 4,
            $"the core did not take its user to the member and get spent ({travelled.Outcome.Status}, {harness.Repository.Record.FieldId})");

        var entry = new Protocol.Packets.InventoryItemEntry(Guid.NewGuid(), 1, 0, 1, 0, -1, 1, 1, "Union Core", "Ally");
        var buffer = new byte[Protocol.Binary.FrameCodec.MaxFrameSize];
        var part = Protocol.Packets.InventoryPartPacket.Read(buffer.AsSpan(0, new Protocol.Packets.InventoryPartPacket(1, 0, 1, [entry]).Write(buffer)));
        Assert.True(part.Items is [{ Link: "Ally" }], "the inventory entry lost its link");
        var full = Enumerable.Repeat(entry with { Name = new string('n', Protocol.Packets.InventoryItemEntry.MaxNameBytes),
            Link = new string('l', Protocol.Packets.InventoryItemEntry.MaxLinkBytes) }, Protocol.Packets.InventoryPartPacket.MaxItemsPerPart).ToArray();
        Assert.True(new Protocol.Packets.InventoryPartPacket(1, 0, 1, full).Write(buffer) <= Protocol.Binary.FrameCodec.MaxFrameSize,
            "a full inventory part does not fit one frame");
    }

    private static bool TravelUsesBlocked(int fieldCode) => fieldCode is FieldCodes.Castle or FieldCodes.QuestArena or FieldCodes.Bellatra;

    private static Task ConflictsAsync()
    {
        const long now = 1_000_000;
        var timers = new PremiumTimers();
        timers.Start(PremiumEffectKind.ExperienceUp, 30, 3600, now);
        Assert.True(timers.CanStart(PremiumEffectKind.ExperienceUp, PremiumConflict.SameKind, now) == PremiumStartResult.AlreadyRunning, "same kind");
        Assert.True(timers.CanStart(PremiumEffectKind.ThirdEyes, PremiumConflict.ThirdEyesPackage, now) == PremiumStartResult.AlreadyRunning, "package");
        Assert.True(timers.CanStart(PremiumEffectKind.ManaReduce, PremiumConflict.SameKind, now) == PremiumStartResult.Started, "other kind refused");
        timers.Start(PremiumEffectKind.VampiricCuspid, 0, 3600, now);
        Assert.True(timers.CanStart(PremiumEffectKind.VampiricCuspidEx, PremiumConflict.SameKind, now) == PremiumStartResult.SameAttribute, "vampiric pair");
        timers.Start(PremiumEffectKind.HelpPetIo, 0, 3600, now);
        Assert.True(timers.CanStart(PremiumEffectKind.HelpPetMute, PremiumConflict.HelperPet, now) == PremiumStartResult.SameAttribute, "second helper pet");
        Assert.True(!timers.Expire(now + 3599) && timers.Expire(now + 3600) && timers.Timers.Count == 0, "expiry");
        Assert.True(timers.CanStart(PremiumEffectKind.ExperienceUp, PremiumConflict.SameKind, now + 3600) == PremiumStartResult.Started, "expired kind");
        Assert.True(timers.TryAddGravityScroll(5) && timers.GravityScrolls == 1, "gravity scroll");
        return Task.CompletedTask;
    }

    private static Task FormulasAsync()
    {
        Assert.True(PremiumRules.VampiricLife(100, 300, 1) == 8 && PremiumRules.VampiricExLife(100, 300, 1) == 10, "single target life");
        Assert.True(PremiumRules.VampiricLife(100, 300, 5) == 49 && PremiumRules.VampiricExLife(100, 300, 5) == 75, "area life");
        Assert.True(PremiumRules.ManaRecharge(65, 0, 1) == 6, "mana recharge");
        var caster = new CharacterClassRegistry().CreatePlayer(PlayerClass.Magician, "Mana", 80);
        var spell = caster.Class.Skills.First(static skill => skill.ManaByRank.Count > 0 && skill.ManaByRank[0] >= 10);
        var full = spell.ManaForRank(1);
        caster.Premium.Start(PremiumEffectKind.ManaReduce, 30, 3600, 0);
        PremiumEffects.Sync(caster);
        Assert.True(SkillFormulas.ManaCost(spell, 1, caster.Effects.Totals) == full - full * 30 / 100, "mana reduce");
        caster.Premium.Start(PremiumEffectKind.HelpPetTerry, 0, 3600, 0);
        PremiumEffects.Sync(caster);
        Assert.True(SkillFormulas.ManaCost(spell, 1, caster.Effects.Totals) == full - full * 50 / 100, "terry mana share");
        var timers = new PremiumTimers();
        timers.Start(PremiumEffectKind.StaminaReduce, 30, 3600, 0);
        Assert.True(PremiumRules.StaminaCost(41, timers) == 29, "stamina reduce");
        timers.Start(PremiumEffectKind.ExperienceUp, 50, 3600, 0);
        timers.Start(PremiumEffectKind.PhoenixPet, 0, 3600, 0);
        Assert.True(PremiumRules.BoostExperience(1000, timers) == 1700, "experience with phoenix");
        timers.Start(PremiumEffectKind.ThirdEyes, 20, 3600, 0);
        Assert.True(PremiumRules.ExtraDropChance(timers) == 70, "drop chance with phoenix");
        timers.Start(PremiumEffectKind.MightOfAwell, 300, 3600, 0);
        timers.TryAddGravityScroll(5);
        Assert.True(PremiumRules.WeightBonus(timers) == 350, "weight bonus");

        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Premium");
        var weight = character.Derived.MaxWeight;
        character.Premium.Start(PremiumEffectKind.MightOfAwell, 500, 3600, 0);
        character.Premium.Start(PremiumEffectKind.ManaReduce, 30, 3600, 0);
        Assert.True(character.Derived.MaxWeight == weight + 500, "weight bonus on the character");
        Assert.True(character.Effects.Totals.Percent(StatKind.SkillManaCost) == -30, "mana reduce modifier");
        character.Premium.Expire(3600);
        Assert.True(character.Derived.MaxWeight == weight && character.Effects.Totals.Percent(StatKind.SkillManaCost) == 0, "expired timers keep effects");
        return Task.CompletedTask;
    }

    private static Task DropBonusAsync()
    {
        var table = new DropTable([DropEntry.GoldSlot(100, 1, 1)], 1, false);
        Assert.True(DropRoller.Roll(new CombatRng(3), table).Count == 1, "plain roll");
        Assert.True(DropRoller.Roll(new CombatRng(3), table, extraRollChance: 100).Count == 2, "extra roll");
        Assert.True(DropRoller.Roll(new CombatRng(3), DropTable.Empty, extraRollChance: 100).Count == 0, "extra roll without drops");
        return Task.CompletedTask;
    }

    private static async Task PersistenceAsync()
    {
        var harness = await Harness.CreateAsync(20, "BI113");
        var timers = harness.Session.Character!.Premium;
        timers.Start(PremiumEffectKind.ThirdEyes, 50, 600, harness.NowSeconds);
        timers.Start(PremiumEffectKind.ExperienceUp, 30, 86400, harness.NowSeconds);
        var saved = SavedPremium.Capture(harness.Session.Character);
        var json = CharacterJson.Write(saved);
        var restored = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Restored");
        SavedPremium.Restore(restored, json, harness.NowSeconds + 601);
        Assert.True(restored.Premium.Timers.Count == 1 && restored.Premium.IsActive(PremiumEffectKind.ExperienceUp)
            && restored.Premium.Find(PremiumEffectKind.ExperienceUp)!.Value.ExpiresAtSeconds == harness.NowSeconds + 86400,
            "offline time did not count or the expired timer survived");
        var empty = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Empty");
        SavedPremium.Restore(empty, JsonText.EmptyObject, 0);
        Assert.True(empty.Premium.Timers.Count == 0, "blank premium column");
    }

    private static async Task ExperiencePotionAsync()
    {
        var harness = await Harness.CreateAsync(20, "BI113");
        var item = harness.Item("BI113")!;
        var command = new ItemUseCommand(Guid.NewGuid(), harness.Session.Character!.InventoryRevision, item.InstanceId);
        var pending = harness.Use(command);
        harness.World.Tick(0.05f, 0);
        Assert.True(!pending.IsCompleted && !harness.Session.Character.Premium.IsActive(PremiumEffectKind.ExperienceUp)
            && harness.Session.Character.Inventory[0] is not null, "the timer started before the use committed");
        await harness.AdvanceAsync(pending);
        var reply = await pending;
        Assert.True(reply.Outcome.Ok && harness.Session.Character.Inventory[0] is null, "use did not consume the potion");
        Assert.True(harness.Session.Character.Premium.ValueOf(PremiumEffectKind.ExperienceUp) == 30, "EXP timer missing");
        Assert.True(harness.Repository.Record.Premium.Value.Contains("ExperienceUp", StringComparison.Ordinal), "timer was not stored");
        var writes = harness.Repository.UpdateCount;
        var replay = harness.Use(command);
        await harness.AdvanceAsync(replay);
        Assert.True((await replay).Outcome.Ok && harness.Repository.UpdateCount == writes, "replay stored again");
        Assert.True(KillReward.ExpForKill(100, 10, 10) == 100
            && PremiumRules.BoostExperience(KillReward.ExpForKill(100, 10, 10), harness.Session.Character.Premium) == 130, "kill experience boost");

        harness.Session.Character.Inventory.TryAdd(new ItemInstance(harness.Definitions["BI113"]));
        var second = harness.Item("BI113")!;
        var again = harness.Use(new ItemUseCommand(Guid.NewGuid(), harness.Session.Character.InventoryRevision, second.InstanceId));
        await harness.AdvanceAsync(again);
        Assert.True((await again).Outcome.Status == ItemUseStatus.AlreadyRunning && harness.Item("BI113") is not null, "second potion was consumed");

        var owner = OwnerState.Capture(harness.Session.Character, null, harness.NowSeconds);
        Assert.True(owner.PremiumTimers.Count == 1 && owner.PremiumClockSeconds == harness.NowSeconds, "owner state lacks the timers");
    }

    private static async Task TeleportCoreAsync()
    {
        var harness = await Harness.CreateAsync(20, "BI108", "BI108");
        var core = harness.Item("BI108")!;
        var refused = harness.Use(new ItemUseCommand(Guid.NewGuid(), harness.Session.Character!.InventoryRevision, core.InstanceId, 10));
        await harness.AdvanceAsync(refused);
        Assert.True((await refused).Outcome.Status == ItemUseStatus.LevelTooLow && harness.Session.FieldId == 20, "level gate");
        var used = harness.Use(new ItemUseCommand(Guid.NewGuid(), harness.Session.Character.InventoryRevision, core.InstanceId, 3));
        await harness.AdvanceAsync(used);
        var reply = await used;
        Assert.True(reply.Outcome.Ok && reply.Arrival is { FieldCode: 0 } && harness.Session.FieldId == 0, "teleport did not move the player");
        Assert.True(harness.Session.Character.OwnedItems().Count(item => item.Definition.CatalogKey == "BI108") == 1, "core was not consumed once");
        Assert.True(harness.Repository.Record.FieldId == 0, "stored field is not the destination");
    }

    private static async Task ProgressionItemsAsync()
    {
        var harness = await Harness.CreateAsync(20, "BI101", "BI116", "BI117", "BI139", "BI102");
        var character = harness.Session.Character!;
        character.RestoreProgress(0, 0, 5, 0, 1, 0);
        character.TryAllocateAttribute(CharacterAttribute.Strength, 5);
        var total = character.Attributes.Strength + character.StatPoints;
        var blue = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI101")));
        await harness.AdvanceAsync(blue);
        Assert.True((await blue).Outcome.Ok && character.StatPoints == 5 && character.Attributes.Strength + character.StatPoints == total,
            "stat reset did not refund the points");

        var same = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI116")));
        await harness.AdvanceAsync(same);
        Assert.True((await same).Outcome.Status == ItemUseStatus.NothingToChange && harness.Item("BI116") is not null, "same hair was consumed");
        var tint = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI117")));
        await harness.AdvanceAsync(tint);
        Assert.True((await tint).Outcome.Ok && harness.Session.Loaded!.HairIndex == 1 && harness.Player.Appearance.HairIndex == 1
            && harness.Repository.Record.HairIndex == 1, "hair tint did not change the stored and shown hair");

        var skill = character.Class.Skills[0];
        character.Skills.Restore(skill.Code, 1);
        var master = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI139")));
        await harness.AdvanceAsync(master);
        Assert.True((await master).Outcome.Ok && character.Skills.UsesOf(skill.Code) == 10000, "skill master did not train the skill");

        var points = character.SkillPoints;
        var red = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI102")));
        await harness.AdvanceAsync(red);
        Assert.True((await red).Outcome.Ok && character.Skills.Ranks.Count == 0 && character.SkillPoints == points + 1, "skill reset did not refund");
    }

    private static async Task ScrollsAndOrbsAsync()
    {
        var harness = await Harness.CreateAsync(20, "BI105", "BI106", "BI107", "FO104");
        var character = harness.Session.Character!;
        var life = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI105")));
        await harness.AdvanceAsync(life);
        Assert.True((await life).Outcome.Ok && harness.Player.GuardMsLeft >= 29_000f, "Eternal Life did not guard");
        harness.Player.ApplyDamage(10);
        Assert.True(character.Life == character.MaxLife, "guarded player took damage");
        harness.Tick(2);
        Assert.True(character.Effects.TryGet(ItemScrolls.For(ScrollEffectKind.Invulnerability).StatusCode, out _), "Eternal Life status missing");
        Assert.True(harness.StatusCodes().Contains(ItemScrolls.For(ScrollEffectKind.Invulnerability).StatusCode), "Eternal Life status was not published");

        var edge = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI106")));
        await harness.AdvanceAsync(edge);
        harness.Tick(2);
        var avert = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI107")));
        await harness.AdvanceAsync(avert);
        Assert.True((await edge).Outcome.Ok && (await avert).Outcome.Status == ItemUseStatus.NotHere, "Fatal Edge did not exclude Avert");
        Assert.True(character.Effects.Totals.Flat(StatKind.CriticalChance) >= 15, "Fatal Edge critical missing");

        var orb = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("FO104")));
        await harness.AdvanceAsync(orb);
        harness.Tick(2);
        Assert.True((await orb).Outcome.Ok && character.Effects.Totals.Flat(StatKind.HitDamage) == 10, "force orb damage missing");
    }

    private static async Task FirecrackerAndShopAsync()
    {
        var harness = await Harness.CreateAsync(20, "BI123", "BI142", "PL101");
        var character = harness.Session.Character!;
        var fire = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI123")));
        await harness.AdvanceAsync(fire);
        Assert.True((await fire).Outcome.Ok, "firecracker refused");
        harness.Tick(2);
        Assert.True(harness.ItemEffects().Any(effect => effect.Effect == ItemEffectKind.Firecracker && effect.EntityId == harness.Player.EntityId),
            "firecracker effect was not published");
        Assert.True(harness.Player.MoveState == MotionState.Yahoo, $"the firecracker did not cheer: {harness.Player.MoveState}");

        var shop = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("BI142")));
        await harness.AdvanceAsync(shop);
        Assert.True((await shop).Outcome.Ok && harness.Session.MobileShopOpen, "mobile shop did not open");
        var npc = new NpcAppService(harness.Sessions, new ImmediateSaves(harness.Repository), new NoWarehouses(), harness.World, harness.Holder);
        var money = character.Money;
        var sell = npc.RequestAsync(harness.Session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), 0, NpcServiceKind.Shop, NpcServiceAction.Sell,
            character.InventoryRevision, 0, 0, harness.Id("PL101"), -1, 1, 0));
        await harness.AdvanceAsync(sell);
        Assert.True((await sell).Outcome.Ok && character.Money > money && harness.Item("PL101") is null, "mobile shop did not buy the potion");
        var close = npc.RequestAsync(harness.Session.SessionToken, new NpcServiceCommand(Guid.NewGuid(), 0, NpcServiceKind.Shop, NpcServiceAction.Close,
            0, 0, 0, Guid.Empty, 0, 0, 0));
        await harness.AdvanceAsync(close);
        Assert.True(!harness.Session.MobileShopOpen, "mobile shop stayed open");
    }

    private static Task WireAsync()
    {
        var buffer = new byte[8192];
        var command = new ItemUseCommandPacket(Guid.NewGuid(), 7, Guid.NewGuid(), 12, "Happy new year");
        var read = ItemUseCommandPacket.Read(buffer.AsSpan(0, command.Write(buffer)));
        Assert.True(read == command, "item use command");
        var result = new ItemUseResultPacket(Guid.NewGuid(), true, 9, (ushort)ItemUseStatus.LevelTooLow, 0x080B0800,
            GameText.Of(GameTextId.ItemsReceived, "Claw", "3"));
        Assert.True(ItemUseResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "item use result");
        var timers = new PremiumTimersPacket(3, [new PremiumTimerEntry(2, 30, 100, 86400), new PremiumTimerEntry(7, 0, 5, 3600)]);
        var timersRead = PremiumTimersPacket.Read(buffer.AsSpan(0, timers.Write(buffer)));
        Assert.True(timersRead.Revision == 3 && timersRead.Timers.SequenceEqual(timers.Timers), "premium timers");
        var effects = new ItemEffectsPacket([new ItemEffectEntry(5, 0x080B1700, 1, 256, 512, -768, 1234, 0), new ItemEffectEntry(6, 2, 5, 0, 0, 0, 0, 1UL << 40)]);
        var effectsRead = ItemEffectsPacket.Read(buffer.AsSpan(0, effects.Write(buffer)));
        Assert.True(effectsRead.Effects.SequenceEqual(effects.Effects), "item effects");
        return Task.CompletedTask;
    }

    private static Task PetsAndLeechAsync()
    {
        var scenario = SkillScenario.Create(PlayerClass.Fighter, 50, null, null, SkillScenario.Enemy());
        var monster = scenario.Spawn(SkillScenario.Enemy(), new Vector3(40f, 0f, 0f));
        var character = scenario.Character;
        character.Premium.Start(PremiumEffectKind.VampiricCuspid, 0, 3600, 0);
        character.Premium.Start(PremiumEffectKind.HelpPetNepsys, 0, 3600, 0);
        character.Premium.Start(PremiumEffectKind.PhoenixPet, 0, 3600, 0);
        character.Life = 10;
        var life = monster.Life;
        scenario.Field.Combat.ApplyPlayerDamage(scenario.Player, monster, 300, false, scenario.Field.NowMs);
        scenario.Run(1);
        var heal = PremiumRules.VampiricLife(character.Attributes.Strength, 300, 1);
        Assert.True(character.Life == 10 + heal, $"vampiric heal {character.Life - 10} instead of {heal}");
        scenario.Run(1);
        Assert.True(scenario.Field.ItemEvents.Snapshot().Count(effect => effect.Effect == ItemEffectKind.PetAttack) == 2, "pet strikes were not announced");
        var afterOwner = monster.Life;
        Assert.True(afterOwner == life - 300, "the owner's hit was not applied once");
        scenario.Run((int)(PremiumPets.HelperHitMs / 50) + 1);
        var nepsys = PremiumPets.DamageRange(PremiumEffectKind.HelpPetNepsys, 50);
        Assert.True(afterOwner - monster.Life >= nepsys.Min && afterOwner - monster.Life <= nepsys.Max, $"helper strike dealt {afterOwner - monster.Life}");
        Assert.True(monster.Effects.TryGet(PremiumPets.SlowStatusCode, out _), "Nepsys did not slow the monster");
        var afterHelper = monster.Life;
        scenario.Run((int)((PremiumPets.PhoenixHitMs - PremiumPets.HelperHitMs) / 50) + 2);
        var phoenix = PremiumPets.DamageRange(PremiumEffectKind.PhoenixPet, 50);
        Assert.True(afterHelper - monster.Life >= phoenix.Min && afterHelper - monster.Life <= phoenix.Max,
            $"Phoenix strike dealt {afterHelper - monster.Life}");
        Assert.True(character.Life == 10 + heal, "a pet strike healed through the vampiric item");
        Assert.True(PremiumPets.HelperChance(3) == 21 && PremiumPets.HelperChance(8) == 24 && PremiumPets.HelperChance(121) == 0, "helper chance table");
        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Owner");
        character.Premium.Start(PremiumEffectKind.ExperienceUp, 30, 3600, 1000);
        var owner = OwnerState.Capture(character, null, 1600);
        var appearance = new PlayerAppearanceView("Owner", PlayerClass.Fighter, 0, 0, 0, "", "") { HelperPet = 2, Phoenix = true, BigHead = 1 };
        var self = new EntityView(1, EntityKind.Player, true, new Vector3(256, 0, 512), 0f, MotionState.Stand, 10, 100, 100, appearance);
        var snapshot = new WorldSnapshot(4, [self], fieldId: 3, owner: owner)
        {
            ItemEffects = [new ItemEffectView(1, ItemCodes.Parse("BI123"), ItemEffectKind.Firecracker, new Vector3(256, 0, 512), 1234)]
        };
        var updates = new WireRoundTrip(1).Push(snapshot);
        var timers = updates.Select(update => update.PremiumTimers).Single(timers => timers is not null)!;
        Assert.True(timers.Timers.Single() is { Kind: PremiumEffectKind.ExperienceUp, Value: 30, RemainingSeconds: 3000, DurationSeconds: 3600 },
            "premium timers lost their remaining time");
        var effect = updates.SelectMany(update => update.ItemEffects).Single();
        Assert.True(effect.Effect == ItemEffectKind.Firecracker && effect.Variant == 1234 && effect.Position.X == 256, "item effect lost its data");
        var spawn = updates.SelectMany(update => update.Spawns).Single(candidate => candidate.EntityId == 1);
        Assert.True(spawn.Player is { HelperPet: 2, Phoenix: true, BigHead: 1 }, "player descriptor lost the pets");
        return Task.CompletedTask;
    }

    internal sealed class NoWarehouses : Persistence.Abstractions.Warehouses.IWarehouseRepository
    {
        public Task<Persistence.Abstractions.Warehouses.WarehouseRecord?> FindAsync(long accountId, CancellationToken ct = default) =>
            Task.FromResult<Persistence.Abstractions.Warehouses.WarehouseRecord?>(null);

        public Task<bool> SaveWithCharacterAsync(CharacterRecord character, Persistence.Abstractions.Warehouses.WarehouseRecord warehouse, long expectedRevision,
            CancellationToken ct = default) => Task.FromResult(false);
    }

    internal sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Harness
    {
        public required WorldServer World { get; init; }
        public required ItemUseAppService Service { get; init; }
        public required SessionRegistry Sessions { get; init; }
        public required PlayerSession Session { get; init; }
        public required WorldPlayer Player { get; init; }
        public required MemoryCharacters Repository { get; init; }
        public required Channel<WorldSnapshot> Outbound { get; init; }
        public required Dictionary<string, ItemDefinition> Definitions { get; init; }
        public required ManualClock Clock { get; init; }
        public required WorldDataHolder Holder { get; init; }
        private readonly List<WorldSnapshot> _snapshots = new();
        private uint _tick;

        public long NowSeconds => Clock.Now.ToUnixTimeSeconds();

        public static Task<Harness> CreateAsync(int fieldId, params string[] keys) => CreateAsync(fieldId, [], [], keys);

        // Monsters join the world catalog; given definitions replace the plain ones built for their keys and join the
        // item catalog even when not carried.
        public static async Task<Harness> CreateAsync(int fieldId, MonsterDefinition[] monsters, ItemDefinition[] given, params string[] keys)
        {
            var classes = new CharacterClassRegistry();
            var definitions = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys.Distinct())
            {
                definitions[key] = new ItemDefinition(ItemCodes.Parse(key), key, key.StartsWith("PL", StringComparison.Ordinal) ? ItemKind.Potion : ItemKind.Misc,
                    MaxStack: 1)
                { CatalogKey = key, Price = 100 };
            }

            foreach (var definition in given)
            {
                definitions[definition.CatalogKey] = definition;
            }

            var starter = CharacterRecordFactory.NewStarter(classes, 1, "Premium" + fieldId, 0, PlayerClass.Fighter, fieldId) with { Id = 1 };
            var character = classes.CreatePlayer(PlayerClass.Fighter, starter.Name);
            foreach (var key in keys)
            {
                character.Inventory.TryAdd(new ItemInstance(definitions[key]));
            }

            var repository = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, fieldId, Vector3.Zero));
            var data = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(new ItemCatalog(definitions.Values), monsters.Length > 0 ? new MonsterCatalog(monsters) : data.Monsters, data.Npcs,
                    data.FieldSpawns, data.Motions)
            };
            var clock = new ManualClock();
            var catalog = new WorldCatalog();
            var world = new WorldServer(holder, catalog, clock);
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "owner");
            var service = new SessionAppService(sessions, classes, repository, new ImmediateSaves(repository), catalog, world, holder, clock);
            Assert.True((await service.EnterWorldAsync(session.SessionToken, starter.Name)).Ok && session.TryBeginPlay(), "entry failed");
            var outbound = Channel.CreateUnbounded<WorldSnapshot>();
            var player = WorldPlayerFactory.Create(session);
            var harness = new Harness
            {
                World = world,
                Service = new ItemUseAppService(sessions, new ImmediateSaves(repository), world),
                Sessions = sessions,
                Session = session,
                Player = player,
                Repository = repository,
                Outbound = outbound,
                Definitions = definitions,
                Clock = clock,
                Holder = holder
            };
            await harness.AdvanceAsync(world.JoinAsync(fieldId, player, outbound.Writer, session));
            return harness;
        }

        public Guid Id(string key) => Item(key)?.InstanceId ?? throw new InvalidOperationException($"{key} is not carried.");

        // Another player in the same world.
        public async Task<WorldPlayer> JoinAsync(string name, int fieldId, long accountId)
        {
            var classes = new CharacterClassRegistry();
            var session = Sessions.Create(accountId, name);
            session.Character = classes.CreatePlayer(PlayerClass.Fighter, name);
            session.Loaded = CharacterRecordFactory.NewStarter(classes, accountId, name, 0, PlayerClass.Fighter, fieldId) with { Id = accountId };
            Assert.True(session.TryBeginEntry() && session.CompleteEntry(true) && session.TryBeginPlay(), "the other player did not begin play");
            var player = WorldPlayerFactory.Create(session);
            await AdvanceAsync(World.JoinAsync(fieldId, player, Channel.CreateUnbounded<WorldSnapshot>().Writer, session));
            return player;
        }

        public ItemInstance? Item(string key) => Session.Character!.OwnedItems().FirstOrDefault(item => item.Definition.CatalogKey == key);

        public Task<ItemUseReply> Use(ItemUseCommand command) => Service.UseAsync(Session.SessionToken, command);

        public void Tick(int count)
        {
            for (var index = 0; index < count; index++)
            {
                World.Tick(0.05f, ++_tick);
            }

            Drain();
        }

        public async Task AdvanceAsync(Task task)
        {
            await WorldPump.RunAsync(World, task, () => ++_tick);
            Drain();
        }

        public IEnumerable<uint> StatusCodes() => _snapshots.SelectMany(snapshot => snapshot.StatusEvents).Select(status => status.StatusCode);

        public IEnumerable<ItemEffectView> ItemEffects() => _snapshots.SelectMany(snapshot => snapshot.ItemEffects);

        private void Drain()
        {
            while (Outbound.Reader.TryRead(out var snapshot))
            {
                _snapshots.Add(snapshot);
            }
        }
    }
}
