using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using static PristonTale.Tests.QuestFixture;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

// The quest state machines on the shared rules: offers, steps, hunts, items, rewards and the rank-ups that alone move
// the class tier.
internal static class QuestTests
{
    private static readonly QuestNpc HomeMaster = new(ClassChangeGroup: 4, HomeMaster: true);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("level quests follow level and log, count their hunt, pay points and keep one quest at a time", LevelQuestsAsync);
        yield return ("the level 85 tear goes back on death and returns with the restarted hunt", KhaliaTearAsync);
        yield return ("quest grants respect the carried-weight limit and leave the quest unchanged when refused", WeightAsync);
        yield return ("beginner quests run in order with deliveries, hunts, tribe finishers and class rewards", ElementaryAsync);
        yield return ("the first Tempskron rank-up needs the home master, level and skills and gathers one trophy of each", TrophiesAsync);
        yield return ("the first Morion rank-up follows the name branch, brews at the aging master and promotes after delivery", PotionAsync);
        yield return ("the second rank-up buys a quest weapon with vamps, grows it by stage and expires it with its ring", QuestWeaponAsync);
        yield return ("the third rank-up hunts against the clock, needs the amulet holder and ends with the duel", MasterTrialAsync);
        yield return ("Fury of Phantom hunts by field, fights three battles and grants the chosen reward", FuryAsync);
        yield return ("the level 80 and 90 bonus chains pay back points, add points per level and curse the hunter", BonusChainsAsync);
        yield return ("the wing exchange takes the held wing, crystals and gold for the next wing", WingAsync);
        yield return ("quest drops come only from allowed sources and while a running quest wants them", DropsAsync);
        yield return ("class tier changes only through rank-up quests and masters of a lower group stop at the first", TierAuthorityAsync);
    }

    private static Task LevelQuestsAsync()
    {
        var npc = new QuestNpc(GiverCode: 1001);
        var young = Character(PlayerClass.Fighter, "Young", 29);
        Assert.True(Talk(young, npc).Prompt is null, "a level 30 quest was offered below level 30");

        var hunter = Character(PlayerClass.Fighter, "Hunter", 30);
        var offer = Talk(hunter, npc).Prompt;
        Assert.True(offer is { Code: QuestCode.Level30, State: 0, Followup: QuestFollowup.Confirm, Document: "LevelQuest30start" }, "level 30 offer differs");
        Expect(hunter, npc, QuestAction.Accept, QuestCode.Level30, 0, "accept failed");
        var active = hunter.Quests.Active;
        Assert.True(active is { Code: QuestCode.Level30, State: 1, Target: QuestMonsters.Bargon, KillsNeeded: 20, Kills: 0 }, "the hunt did not start");
        Assert.True(Talk(hunter, npc).Prompt is { State: 1, Followup: QuestFollowup.Close }, "the running hunt's dialog differs");

        Assert.True(Talk(hunter, new QuestNpc(GiverCode: 1014)).Notices.Any(n => n.Text == QuestTexts.QuestInProgress),
            "another giver did not report the running quest");
        Assert.True(Run(hunter, new QuestNpc(GiverCode: 1006), QuestAction.Accept, QuestCode.Level90, 0).Status == QuestStatus.Stale,
            "a second quest started beside the running one");

        Kill(hunter, QuestMonsters.Muffin, 5);
        var counted = Kill(hunter, QuestMonsters.Bargon, 19);
        Assert.True(hunter.Quests.Active is { State: 1, Kills: 19 } && counted.Notices.Count(n => n.Kind == QuestNoticeKind.Chat) == 19,
            "the hunt counted the wrong monster or lost its count lines");
        var finished = Kill(hunter, QuestMonsters.Bargon);
        Assert.True(hunter.Quests.Active is { State: 2, Kills: 20 } && finished.Notices.Any(n => n.Text == QuestTexts.TwentyBargons),
            "the finished hunt did not move to the reward step");
        Kill(hunter, QuestMonsters.Bargon, 3);
        Assert.True(hunter.Quests.Active.Kills == 20, "kills counted after the hunt was done");

        var reward = Talk(hunter, npc).Prompt;
        Assert.True(reward is { State: 2, Followup: QuestFollowup.Proceed, Document: "LevelQuest30end" }, "reward dialog differs");
        var points = hunter.StatPoints;
        var done = Expect(hunter, npc, QuestAction.Proceed, QuestCode.Level30, 2, "the reward step failed");
        Assert.True(hunter.StatPoints == points + 5 && hunter.Quests.Active.IsIdle && (hunter.Quests.LevelLog & LevelQuestLog.Level30) != 0
            && hunter.Quests.HasFinished(QuestCode.Level30) && done.Notices.Any(n => n.Kind == QuestNoticeKind.Fanfare), "level 30 reward differs");
        Assert.True(Talk(hunter, npc).Prompt is null, "a finished level quest was offered again");
        Assert.True(Run(hunter, npc, QuestAction.Proceed, QuestCode.Level30, 2).Status == QuestStatus.Stale && hunter.StatPoints == points + 5,
            "a replayed reward step paid twice");

        var morion = Character(PlayerClass.Priestess, "Morion", 55);
        morion.Quests.LevelLog = LevelQuestLog.Level30;
        Assert.True(Talk(morion, new QuestNpc(GiverCode: 1003)).Prompt is null
            && Talk(morion, new QuestNpc(GiverCode: 1004)).Prompt is { Code: QuestCode.Level55Morion }, "level 55 branches do not follow the tribe");
        return Task.CompletedTask;
    }

    private static Task KhaliaTearAsync()
    {
        var npc = new QuestNpc(GiverCode: 1005);
        var character = Character(PlayerClass.Fighter, "Tear", 85);
        character.Quests.LevelLog = LevelQuestLog.Level30 | LevelQuestLog.Level55 | LevelQuestLog.Level70 | LevelQuestLog.Level80;
        Assert.True(Talk(character, npc).Prompt is { Code: QuestCode.Level85, State: 0 }, "level 85 offer differs");
        Expect(character, npc, QuestAction.Accept, QuestCode.Level85, 0, "level 85 accept failed");
        Assert.True(character.Quests.Active is { State: 2, Target: QuestMonsters.Grotesque, KillsNeeded: 10 }
            && QuestInventory.Has(character, QuestItems.KhaliaTear), "the tear was not handed out");

        var death = Context(character);
        Assert.True(QuestTick.OnDeath(death) && character.Quests.Active.State == 1, "a death did not reset the level 85 quest");
        Assert.True(QuestDrops.UpkeepDue(character, 0), "the upkeep was not asked to take the tear");
        Drop(character, QuestItems.Vamp);
        Assert.True(!QuestInventory.Has(character, QuestItems.KhaliaTear), "the upkeep left the tear after the death");

        Assert.True(Talk(character, npc).Prompt is { State: 1, Followup: QuestFollowup.Proceed }, "the restart dialog differs");
        Expect(character, npc, QuestAction.Proceed, QuestCode.Level85, 1, "the restart failed");
        Assert.True(character.Quests.Active is { State: 2, Kills: 0 } && QuestInventory.Has(character, QuestItems.KhaliaTear), "the hunt did not restart");
        Kill(character, QuestMonsters.Grotesque, 10);
        Expect(character, npc, QuestAction.Proceed, QuestCode.Level85, 3, "level 85 completion failed");
        Assert.True((character.Quests.LevelLog & LevelQuestLog.Level85) != 0 && QuestRewards.ReducedDeathPenalty(character), "level 85 reward missing");
        return Task.CompletedTask;
    }

    private static Task WeightAsync()
    {
        var npc = new QuestNpc(GiverCode: 1005);
        var loaded = Character(PlayerClass.Fighter, "Loaded", 85);
        loaded.Quests.LevelLog = LevelQuestLog.Level30 | LevelQuestLog.Level55 | LevelQuestLog.Level70 | LevelQuestLog.Level80;
        Assert.True(Items.TryGet(Code("WA197"), out var anvil) && loaded.Inventory.TryAdd(new ItemInstance(anvil)), "fixture anvil did not fit");
        var refused = Run(loaded, npc, QuestAction.Accept, QuestCode.Level85, 0);
        Assert.True(refused.Status == QuestStatus.OverWeight && refused.Message == ItemRuleMessages.OverWeight && loaded.Quests.Active.IsIdle
            && !QuestInventory.Has(loaded, QuestItems.KhaliaTear), "an overweight character received a quest item");
        Assert.True(QuestInventory.Check(loaded, [new ItemInstance(anvil)]) == QuestGrantCheck.OverWeight, "the weight check differs");
        return Task.CompletedTask;
    }

    private static Task ElementaryAsync()
    {
        var instructor = new QuestNpc(GiverCode: 1014);
        var keeper = new QuestNpc(GiverCode: 1015);
        var wrongKeeper = new QuestNpc(GiverCode: 1016);
        var fighter = Character(PlayerClass.Fighter, "Novice", 5);
        Assert.True(Talk(fighter, instructor).Prompt is { Code: QuestCode.ElementaryA, State: 0 }, "the first beginner quest was not offered");
        Assert.True(Run(fighter, instructor, QuestAction.Accept, QuestCode.ElementaryB, 0).Status == QuestStatus.Stale, "beginner quests ran out of order");
        Expect(fighter, instructor, QuestAction.Accept, QuestCode.ElementaryA, 0, "beginner A accept failed");
        Assert.True(QuestInventory.Has(fighter, QuestItems.Cake), "the cake was not handed out");
        Assert.True(Talk(fighter, wrongKeeper).Prompt is null && QuestInventory.Has(fighter, QuestItems.Cake), "the other tribe's keeper took the cake");
        Assert.True(Talk(fighter, keeper).Prompt is { State: 2 } && !QuestInventory.Has(fighter, QuestItems.Cake), "the keeper did not take the cake");
        Expect(fighter, instructor, QuestAction.Proceed, QuestCode.ElementaryA, 2, "beginner A completion failed");
        var weapon = fighter.OwnedItems().Single(item => item.Definition.Code == Code("WA102"));
        Assert.True(weapon.JobSpecMask == QuestClasses.JobMask(fighter) && (fighter.Quests.ElementaryLog & ElementaryQuestLog.A) != 0,
            "the beginner weapon was not specialised or logged");

        Assert.True(Talk(fighter, instructor).Prompt is { Code: QuestCode.ElementaryB, Document: "haQuestterrified_Bstart_T" }, "beginner B differs");
        var started = Expect(fighter, instructor, QuestAction.Accept, QuestCode.ElementaryB, 0, "beginner B accept failed");
        Assert.True(fighter.Quests.Active is { State: 2, Target: QuestMonsters.Hopy, KillsNeeded: 5, Timer: QuestTimerKind.Display }
            && started.Notices.Any(n => n.Kind == QuestNoticeKind.Fanfare), "the beginner hunt did not show the timer icon");
        Kill(fighter, QuestMonsters.Rabie, 5);
        Assert.True(fighter.Quests.Active.Kills == 0, "the other tribe's prey counted");
        Kill(fighter, QuestMonsters.Hopy, 5);
        Expect(fighter, instructor, QuestAction.Proceed, QuestCode.ElementaryB, 3, "beginner B completion failed");
        Assert.True(fighter.OwnedItems().Any(item => item.Definition.Code == Code("OA202")) && ElementaryQuests.Completed(fighter.Quests.ElementaryLog) == 2,
            "beginner B reward differs");

        Expect(fighter, instructor, QuestAction.Accept, QuestCode.ElementaryC, 0, "beginner C accept failed");
        var smith = new QuestNpc(GiverCode: 1018);
        Assert.True(Talk(fighter, smith).Prompt is { Code: QuestCode.ElementaryC, State: 2 }, "the smith did not take the candy step");
        Expect(fighter, smith, QuestAction.Proceed, QuestCode.ElementaryC, 2, "beginner C completion failed");
        Assert.True(!QuestInventory.Has(fighter, QuestItems.Candy) && QuestInventory.Count(fighter, Code("PL101")) == 10, "beginner C reward differs");

        var grown = Character(PlayerClass.Fighter, "Grown", 40);
        grown.Quests.Active = new ActiveQuest { Code = QuestCode.ElementaryC, State = 1 };
        Give(grown, QuestItems.Candy);
        Assert.True(Talk(grown, instructor).Prompt is { Document: "haQuestLevel_Over" } && grown.Quests.Active.IsIdle && !QuestInventory.Has(grown, QuestItems.Candy),
            "past level 39 the instructor kept the beginner quest");
        return Task.CompletedTask;
    }

    private static Task TrophiesAsync()
    {
        var fighter = Character(PlayerClass.Fighter, "Trophy", 20);
        var talk = Talk(fighter, HomeMaster);
        Assert.True(talk.ClassChangeOffered && talk.Prompt is null, "the home master did not offer the class change");
        Assert.True(!Talk(fighter, HomeMaster with { HomeMaster = false }).ClassChangeOffered, "a master away from home offered the class change");
        var unskilled = Character(PlayerClass.Fighter, "Unskilled", 20);
        unskilled.Skills.Clear();
        Assert.True(!Talk(unskilled, HomeMaster).ClassChangeOffered, "the class change ignored the unlearned skills");
        Assert.True(!Talk(Character(PlayerClass.Fighter, "Low", 19), HomeMaster).ClassChangeOffered, "the class change ignored the level");

        var change = Expect(fighter, HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0, "class change failed");
        Assert.True(change.Prompt is { Code: QuestCode.RankUpTrophies, State: 0, Followup: QuestFollowup.Confirm, Document: "ChangeJob1" },
            "class change dialog differs");
        Expect(fighter, HomeMaster, QuestAction.Accept, QuestCode.RankUpTrophies, 0, "rank-up accept failed");
        Assert.True(fighter.Quests.Active is { Code: QuestCode.RankUpTrophies, State: 1 } && fighter.Tier == 0, "accepting the rank-up moved the tier");
        Assert.True(Run(fighter, HomeMaster, QuestAction.Accept, QuestCode.RankUpTrophies, 1).Status == QuestStatus.Stale && fighter.Tier == 0,
            "the rank-up completed without the trophies");

        Drop(fighter, QuestItems.SilverWolverineTrophy);
        Drop(fighter, QuestItems.SilverWolverineTrophy);
        Drop(fighter, QuestItems.BronzeWolverineTrophy, source: QuestDropSource.BonusTable);
        Drop(fighter, QuestItems.GoldenWolverineTrophy);
        Assert.True(QuestInventory.Count(fighter, QuestItems.SilverWolverineTrophy) == 1, "a second trophy of one kind reached the pack");
        var ready = Talk(fighter, HomeMaster).Prompt;
        Assert.True(ready is { Code: QuestCode.RankUpTrophies, State: 1, Followup: QuestFollowup.Confirm } && ready.ConfirmDetail.Length > 0,
            "the rank-up dialog does not name the new job");
        var promoted = Expect(fighter, HomeMaster, QuestAction.Accept, QuestCode.RankUpTrophies, 1, "the rank-up failed");
        Assert.True(fighter.Tier == 1 && fighter.Quests.Active.IsIdle && fighter.Quests.HasFinished(QuestCode.RankUpTrophies)
            && !QuestInventory.Has(fighter, QuestItems.GoldenWolverineTrophy) && fighter.Quests.SkillResetOffered
            && promoted.Notices.Any(n => n.Kind == QuestNoticeKind.Fanfare), "the rank-up result differs");

        var points = fighter.SkillPoints;
        var learned = fighter.Skills.Ranks.Values.Sum();
        Expect(fighter, QuestNpc.None, QuestAction.ResetSkills, QuestCode.None, 0, "the skill redistribution failed");
        Assert.True(fighter.Skills.Ranks.Values.Sum() == 0 && fighter.SkillPoints >= points + learned && !fighter.Quests.SkillResetOffered,
            "the redistribution did not refund the ranks");
        Assert.True(Run(fighter, QuestNpc.None, QuestAction.ResetSkills, QuestCode.None, 0).Status == QuestStatus.Rejected, "a second redistribution ran");
        fighter.Quests.SkillResetOffered = true;
        Expect(fighter, QuestNpc.None, QuestAction.DeclineSkillReset, QuestCode.None, 0, "No did not end the redistribution offer");
        Assert.True(!fighter.Quests.SkillResetOffered
            && Run(fighter, QuestNpc.None, QuestAction.ResetSkills, QuestCode.None, 0).Status == QuestStatus.Rejected,
            "a declined offer still stood (it opened again at the next login)");
        return Task.CompletedTask;
    }

    private static Task PotionAsync()
    {
        Assert.True(RankUpByPotion.Branch("Mira") == QuestCode.RankUpHoney && RankUpByPotion.Branch("Mirb") == QuestCode.RankUpOil,
            "the name branch differs");
        var priestess = Character(PlayerClass.Priestess, "Mira", 20);
        Assert.True(!QuestClasses.IsTempskron(priestess), "the priestess is not Morion");
        var change = Expect(priestess, HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0, "class change failed");
        Assert.True(change.Prompt is { Code: QuestCode.RankUpHoney, Document: "ChangeJob2M_M0" }, "the honey branch was not offered");
        Assert.True(Run(priestess, HomeMaster, QuestAction.Accept, QuestCode.RankUpOil, 0).Status == QuestStatus.Stale, "the other branch was accepted");
        Expect(priestess, HomeMaster, QuestAction.Accept, QuestCode.RankUpHoney, 0, "branch accept failed");

        Drop(priestess, QuestItems.Honey);
        Assert.True(!QuestInventory.Has(priestess, QuestItems.Honey), "honey came before the bottle");
        var villager = new QuestNpc(ScriptCode: (int)QuestCode.RankUpHoney, ScriptParam: 1);
        Assert.True(Talk(priestess, villager).Prompt is { State: 2, Document: "ChangeJob2M_M1" } && QuestInventory.Has(priestess, QuestItems.GlassBottle)
            && priestess.Quests.Active.State == 2, "the villager did not hand out the bottle");
        Assert.True(Talk(priestess, new QuestNpc(ScriptCode: (int)QuestCode.RankUpOil, ScriptParam: 1)).Prompt is null, "the other villager answered");
        for (var drop = 0; drop < 12; drop++)
        {
            Drop(priestess, QuestItems.Honey, source: QuestDropSource.BonusTable);
        }

        Assert.True(QuestInventory.Count(priestess, QuestItems.Honey) == QuestItems.HoneyForJelly, "more honey than the brew needs reached the pack");
        var aging = new QuestNpc(ItemAging: true);
        var brew = Talk(priestess, aging).Prompt;
        Assert.True(brew is { Code: QuestCode.RankUpHoney, State: 2, Followup: QuestFollowup.Confirm, ConfirmDetail: "Royal Jelly" }, "the brew offer differs");
        Expect(priestess, aging, QuestAction.Accept, QuestCode.RankUpHoney, 2, "the brew failed");
        Assert.True(QuestInventory.Has(priestess, QuestItems.RoyalJelly) && !QuestInventory.Has(priestess, QuestItems.Honey)
            && !QuestInventory.Has(priestess, QuestItems.GlassBottle), "the brew did not use its ingredients");

        Assert.True(Talk(priestess, villager).Prompt is { State: 3, Document: "ChangeJob2M_M3" } && priestess.Quests.HasFinished(QuestCode.RankUpHoney)
            && !QuestInventory.Has(priestess, QuestItems.RoyalJelly) && priestess.Tier == 0, "the delivery differs");
        var talk = Talk(priestess, HomeMaster);
        Assert.True(talk.Prompt is { Code: QuestCode.RankUpHoney, State: 3, Document: "ChangeJob2M_Success" } && !talk.ClassChangeOffered,
            "the master did not offer the promotion");
        Expect(priestess, HomeMaster, QuestAction.Accept, QuestCode.RankUpHoney, 3, "the promotion failed");
        Assert.True(priestess.Tier == 1 && !Talk(priestess, HomeMaster).ClassChangeOffered, "the Morion promotion differs");
        return Task.CompletedTask;
    }

    private static Task QuestWeaponAsync()
    {
        var fighter = Character(PlayerClass.Fighter, "Forge", 40, tier: 1);
        Assert.True(Expect(fighter, HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0, "class change failed").Prompt is
            { Code: QuestCode.RankUpQuestWeapon, Document: "ChangeJob3_0" }, "the second rank-up was not offered");
        Expect(fighter, HomeMaster, QuestAction.Accept, QuestCode.RankUpQuestWeapon, 0, "second rank-up accept failed");
        for (var drop = 0; drop < 5; drop++)
        {
            Drop(fighter, QuestItems.Vamp);
        }

        Assert.True(QuestInventory.Count(fighter, QuestItems.Vamp) == RankUpByQuestWeapon.VampsNeeded, "more vamps than needed reached the pack");
        Assert.True(Talk(fighter, HomeMaster).Prompt is { State: 2, Followup: QuestFollowup.Proceed } && fighter.Quests.Active.State == 2,
            "the master did not take the vamps");
        Expect(fighter, HomeMaster, QuestAction.Proceed, QuestCode.RankUpQuestWeapon, 2, "the forge failed", now: 1000);
        var active = fighter.Quests.Active;
        var weapon = fighter.FindOwnedItem(active.WeaponId);
        Assert.True(weapon is { Definition.Name: "Quest Axe" } && !QuestInventory.Has(fighter, QuestItems.Vamp)
            && active is { State: 3, WeaponStage: 1, WeaponKillsLeft: 30, WeaponCreatedUnixMs: 1000 }, "the quest weapon differs");
        Assert.True(RankUpByQuestWeapon.IsBound(fighter, weapon), "the quest weapon is not bound");

        // The forged weapon goes on the empty weapon slot, as any item received; taken off, it counts no kill.
        Assert.True(fighter.GetEquipped(EquipmentSlot.Weapon) == weapon && fighter.TryUnequip(EquipmentSlot.Weapon), "the forged weapon was not worn");
        Kill(fighter, QuestMonsters.Bargon, 30);
        Assert.True(fighter.Quests.Active.WeaponKillsLeft == 30, "kills counted with the weapon unequipped");
        Assert.True(fighter.TryEquipFromInventory(fighter.Inventory.Find(weapon.InstanceId), EquipmentSlot.Weapon), "the quest weapon cannot be wielded");
        // The bag kept a copy of the weapon taken off: follow the one worn now.
        weapon = fighter.GetEquipped(EquipmentSlot.Weapon);
        Kill(fighter, QuestMonsters.HeadCutter, 5);
        var context = Context(fighter);
        var grows = false;
        for (var kill = 0; kill < 30; kill++)
        {
            grows = QuestTick.OnKill(context, QuestMonsters.Bargon);
        }

        Assert.True(grows && fighter.Quests.Active.WeaponKillsLeft == 0 && QuestDrops.UpkeepDue(fighter, 0), "the first stage did not finish");
        var damage = weapon.Roll.DamageMax;
        Drop(fighter, QuestItems.Vamp);
        Assert.True(fighter.Quests.Active is { WeaponStage: 2, WeaponKillsLeft: 40, WeaponLifeBonus: 4 } && weapon.Roll.DamageMax > damage,
            "the weapon did not grow");
        Kill(fighter, QuestMonsters.SkeletonWarrior, 40);
        Drop(fighter, QuestItems.Vamp);
        Kill(fighter, QuestMonsters.HeadCutter, 50);
        Drop(fighter, QuestItems.Vamp);
        Assert.True(fighter.Quests.Active is { State: 4, WeaponStage: RankUpByQuestWeapon.FinalStage, WeaponLifeBonus: 12 }, "the weapon did not finish growing");
        Assert.True(RankUpByQuestWeapon.HuntLine(QuestWeaponHunt.Melee, 1, 18) == "Bargon:12/30"
            && RankUpByQuestWeapon.HuntLine(QuestWeaponHunt.Melee, 2, 40) == "Skeleton Warrior:0/40"
            && RankUpByQuestWeapon.HuntLine(QuestWeaponHunt.Field, 3, 1) == "Ancient Prison 1F's Monster:99/100"
            && RankUpByQuestWeapon.HuntLine(QuestWeaponHunt.Melee, RankUpByQuestWeapon.FinalStage, 0) == "",
            "the quest weapon's box does not name its hunt with the kills made");
        Assert.True(fighter.Effects.TryGet(QuestEffects.QuestWeaponCode, out _), "the wielded quest weapon gives no bonus");

        Assert.True(Talk(fighter, HomeMaster).Prompt is { State: 4, Document: "ChangeJob3_4" }, "the second rank-up dialog differs");
        Assert.True(Run(fighter, HomeMaster with { ClassChangeGroup = 1 }, QuestAction.Accept, QuestCode.RankUpQuestWeapon, 4).Status == QuestStatus.Stale,
            "a first-group master promoted to the third tier");
        Expect(fighter, HomeMaster, QuestAction.Accept, QuestCode.RankUpQuestWeapon, 4, "second rank-up failed");
        var ring = fighter.FindOwnedItem(fighter.Quests.Active.RingId);
        Assert.True(fighter.Tier == 2 && fighter.Quests.Active.State == 5 && ring is { Definition.Name: "Emperor Ring" } && RankUpByQuestWeapon.IsBound(fighter, ring),
            "the second rank-up result differs");

        Drop(fighter, QuestItems.Vamp, now: 1000 + RankUpByQuestWeapon.WeaponLifetimeMs);
        Assert.True(fighter.FindOwnedItem(weapon.InstanceId) is not null, "the quest weapon expired early");
        Assert.True(QuestDrops.UpkeepDue(fighter, 1001 + RankUpByQuestWeapon.WeaponLifetimeMs), "the expiry was not noticed");
        Drop(fighter, QuestItems.Vamp, now: 1001 + RankUpByQuestWeapon.WeaponLifetimeMs);
        Assert.True(fighter.FindOwnedItem(weapon.InstanceId) is null && fighter.FindOwnedItem(ring.InstanceId) is null && fighter.Quests.Active.State == 6,
            "the quest weapon and ring did not expire");
        Assert.True(Talk(fighter, HomeMaster).Prompt is { State: 6, Document: "ChangeJob3_5" }, "the closing dialog differs");
        Expect(fighter, HomeMaster, QuestAction.Proceed, QuestCode.RankUpQuestWeapon, 6, "closing the second rank-up failed");
        fighter.RefreshQuestEffects();
        Assert.True(fighter.Quests.HasFinished(QuestCode.RankUpQuestWeapon) && fighter.Effects.TryGet(QuestEffects.SecondRankUpLifeCode, out _),
            "the lasting life bonus is missing");
        return Task.CompletedTask;
    }

    private static Task MasterTrialAsync()
    {
        var fighter = Character(PlayerClass.Fighter, "Trial", 60, tier: 2);
        Assert.True(Expect(fighter, HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0, "class change failed").Prompt is
            { Code: QuestCode.RankUpMasterTrial, Document: "ChangeJob4_1Start" }, "the third rank-up was not offered");
        Expect(fighter, HomeMaster, QuestAction.Accept, QuestCode.RankUpMasterTrial, 0, "third rank-up accept failed");
        var first = fighter.Quests.Active;
        Assert.True(first is { State: 1, KillsNeeded: 50, Timer: QuestTimerKind.Expiring, TimerMs: RankUpByMasterTrial.HuntTimeMs }
            && first.Target is QuestMonsters.Muffin or QuestMonsters.SkeletonKnight, "the first timed hunt differs");
        Kill(fighter, first.Target, 50);
        Assert.True(fighter.Quests.Active.HuntComplete, "the first hunt did not complete");
        Assert.True(Talk(fighter, HomeMaster).Prompt is { State: 2 } && fighter.Quests.Active is { State: 2, Timer: QuestTimerKind.None },
            "the master did not close the first hunt");
        Expect(fighter, HomeMaster, QuestAction.Proceed, QuestCode.RankUpMasterTrial, 2, "the second hunt did not start");
        Assert.True(fighter.Quests.Active.Target is QuestMonsters.AveliskL or QuestMonsters.BeEvil, "the second hunt prey differs");

        var clock = Context(fighter);
        QuestTick.Step(clock, RankUpByMasterTrial.HuntTimeMs - 1);
        Assert.True(fighter.Quests.Active is { State: 3, TimerMs: 1 }, "the timer did not count down");
        QuestTick.Step(clock, 1);
        Assert.True(fighter.Quests.Active is { State: 2, Kills: 0, Timer: QuestTimerKind.None }
            && clock.Notices.Any(n => n.Text == QuestTexts.MissionTimedOut), "a lost hunt did not go back a step");
        Expect(fighter, HomeMaster, QuestAction.Proceed, QuestCode.RankUpMasterTrial, 2, "the second hunt did not restart");
        Kill(fighter, fighter.Quests.Active.Target, 50);
        Talk(fighter, HomeMaster);
        Expect(fighter, HomeMaster, QuestAction.Proceed, QuestCode.RankUpMasterTrial, 4, "the amulet step did not start");
        var holder = fighter.Quests.Active.Target;
        Assert.True(fighter.Quests.Active.State == 5 && holder == QuestMonsters.SolidSnail, "the amulet holder of the level differs");
        Drop(fighter, QuestItems.RoyalAmulet, monsterCode: QuestMonsters.Mummy, source: QuestDropSource.EventItem);
        Assert.True(!QuestInventory.Has(fighter, QuestItems.RoyalAmulet), "another monster's amulet counted");
        Drop(fighter, QuestItems.RoyalAmulet, monsterCode: holder, source: QuestDropSource.EventItem);
        Assert.True(Talk(fighter, HomeMaster).Prompt is { State: 6 }, "the master did not take the amulet step");
        var duel = Expect(fighter, HomeMaster, QuestAction.Proceed, QuestCode.RankUpMasterTrial, 6, "the duel did not open");
        Assert.True(duel.Duel is { Quest: QuestCode.RankUpMasterTrial, SoundCode: CharacterSoundCodes.SkillMaster }
            && duel.Travel is { Kind: QuestTravelKind.Arena, FieldCode: QuestArena.FieldCode } && !QuestInventory.Has(fighter, QuestItems.RoyalAmulet),
            "the duel request differs");

        var lost = Context(fighter);
        Assert.True(RankUpByMasterTrial.OnDuelLost(lost) && fighter.Quests.Active.State == 4, "a lost duel did not go back to the amulet step");
        fighter.Quests.Active = fighter.Quests.Active with { State = 7 };
        var won = Context(fighter);
        Assert.True(RankUpByMasterTrial.OnDuelWon(won) && won.Prompt is { State: 8, Followup: QuestFollowup.Confirm }, "the victory dialog differs");
        var promoted = Expect(fighter, QuestNpc.None, QuestAction.Accept, QuestCode.RankUpMasterTrial, 8, "the final rank-up failed",
            field: QuestArena.FieldCode);
        Assert.True(fighter.Tier == 3 && promoted.Travel is { Kind: QuestTravelKind.Home, FieldCode: FieldCodes.Ricarten }
            && !Talk(fighter, HomeMaster).ClassChangeOffered, "the final tier differs");

        var morion = Character(PlayerClass.Magician, "Mage", 60, tier: 2);
        Assert.True(QuestClasses.DuelMasterSound(morion) == CharacterSoundCodes.MagicMaster, "Morion classes do not duel the magic master");
        return Task.CompletedTask;
    }

    private static Task FuryAsync()
    {
        var npc = new QuestNpc(GiverCode: 1013);
        var hero = Character(PlayerClass.Fighter, "Hero", 100);
        Assert.True(Talk(Character(PlayerClass.Fighter, "Low", 99), npc).Prompt is null, "Fury was offered below level 100");
        Assert.True(Talk(hero, npc).Prompt is { Code: QuestCode.FuryOfPhantom, State: 0, Followup: QuestFollowup.Confirm }, "the Fury offer differs");
        var accepted = Expect(hero, npc, QuestAction.Accept, QuestCode.FuryOfPhantom, 0, "Fury accept failed");
        Assert.True(accepted.Prompt is { State: 1, Followup: QuestFollowup.Proceed }, "Fury did not explain the first hunt");
        Expect(hero, npc, QuestAction.Proceed, QuestCode.FuryOfPhantom, 1, "the first hunt did not open");
        Kill(hero, QuestMonsters.Bargon, 60, field: 3);
        Assert.True(hero.Quests.Active.Kills == 0, "kills outside the Heart of Perum counted");
        Kill(hero, QuestMonsters.Bargon, 50, field: FuryOfPhantomQuest.HeartOfPerumField);
        Assert.True(hero.Quests.Active is { State: 3, Timer: QuestTimerKind.None }, "the first hunt did not finish");

        foreach (var (battle, level) in new[] { (3, 118), (8, 119), (10, 120) })
        {
            Assert.True(Talk(hero, npc).Prompt is { Followup: QuestFollowup.Confirm } prompt && prompt.State == battle, $"battle {battle} dialog differs");
            var enter = Expect(hero, npc, QuestAction.Accept, QuestCode.FuryOfPhantom, battle, $"battle {battle} did not open");
            var fighting = battle == 3 ? 4 : battle + 1;
            Assert.True(hero.Quests.Active.State == fighting && enter.Duel is { SoundCode: CharacterSoundCodes.Fury } duel && duel.Level == level
                && enter.Travel?.FieldCode == QuestArena.FieldCode, $"battle {battle} duel differs");
            var won = Context(hero, field: QuestArena.FieldCode);
            Assert.True(FuryOfPhantomQuest.OnDuelWon(won) && won.Prompt is { Document: "100LVQuest_Victory" }, "the Fury victory dialog differs");
            var back = Expect(hero, QuestNpc.None, QuestAction.Proceed, QuestCode.FuryOfPhantom, fighting, "the victory step failed",
                field: QuestArena.FieldCode);
            Assert.True(back.Travel is { Kind: QuestTravelKind.Home, FieldCode: FieldCodes.Pillai } && !hero.Quests.Active.ArenaWon,
                "a won battle did not send the player to Pillai");
            if (battle == 3)
            {
                Expect(hero, npc, QuestAction.Proceed, QuestCode.FuryOfPhantom, 5, "the second hunt did not open");
                Kill(hero, QuestMonsters.Bargon, 100, field: FuryOfPhantomQuest.GalluviaValleyField);
            }
        }

        var list = Talk(hero, npc).Prompt;
        Assert.True(list is { State: 12, Followup: QuestFollowup.ChooseFromList } && list.Choices.Count == 8 && list.ChoiceDocuments.Count == 8,
            "the reward list differs");
        var chosen = Expect(hero, npc, QuestAction.ChooseReward, QuestCode.FuryOfPhantom, 12, "choosing a reward failed", choice: 3);
        Assert.True(chosen.Prompt is { State: 13, Followup: QuestFollowup.ConfirmChoice, Choice: 3 } && hero.Quests.Active.Variant == 3,
            "the chosen reward dialog differs");
        Assert.True(Run(hero, npc, QuestAction.Accept, QuestCode.FuryOfPhantom, 13, choice: 9).Status == QuestStatus.Stale, "an unknown reward was granted");
        Expect(hero, npc, QuestAction.Accept, QuestCode.FuryOfPhantom, 13, "claiming the reward failed", choice: 1);
        Assert.True(hero.OwnedItems().Any(item => item.Definition.Name == "Minotaur Axe") && hero.Quests.Active.IsIdle
            && (hero.Quests.ElementaryLog & ElementaryQuestLog.FuryOfPhantom) != 0 && Talk(hero, npc).Prompt is null, "the Fury reward differs");
        return Task.CompletedTask;
    }

    private static Task BonusChainsAsync()
    {
        var first = new QuestNpc(GiverCode: 1010);
        var second = new QuestNpc(GiverCode: 1011);
        var hero = Character(PlayerClass.Fighter, "Bonus", 84);
        Assert.True(Talk(hero, first).Prompt is { Code: QuestCode.Level80Bonus, State: 0 }, "the level 80 chain was not offered");
        Expect(hero, first, QuestAction.Accept, QuestCode.Level80Bonus, 0, "level 80 chain accept failed");
        Assert.True(Talk(hero, second).Prompt is { State: 1 }, "the second NPC did not explain the vamps");
        Expect(hero, second, QuestAction.Proceed, QuestCode.Level80Bonus, 1, "the vamp step did not open");
        Drop(hero, QuestItems.BonusVampOne, source: QuestDropSource.BonusTable);
        Drop(hero, QuestItems.BonusVampTwo, source: QuestDropSource.BonusTable);
        Assert.True(Talk(hero, second).Prompt is { State: 2, Followup: QuestFollowup.Close }, "the vamps were taken incomplete");
        Drop(hero, QuestItems.BonusVampThree, source: QuestDropSource.BonusTable);
        Assert.True(Talk(hero, second).Prompt is { State: 3 }, "the vamps were not taken");
        Expect(hero, second, QuestAction.Proceed, QuestCode.Level80Bonus, 3, "the recommendation failed");
        Assert.True(QuestInventory.Has(hero, QuestItems.Recommendation) && !QuestInventory.Has(hero, QuestItems.BonusVampOne), "the recommendation differs");
        var points = hero.StatPoints;
        Expect(hero, first, QuestAction.Proceed, QuestCode.Level80Bonus, 4, "the level 80 chain did not finish");
        Assert.True(hero.StatPoints == points + 10 && (hero.Quests.LevelLog & LevelQuestLog.Level80Bonus) != 0, "the level 80 back pay differs");
        points = hero.StatPoints;
        hero.LevelUp();
        Assert.True(hero.StatPoints == points + 7, "a level-up after the chain did not pay seven points");

        var cursed = Character(PlayerClass.Fighter, "Cursed", 90);
        cursed.Quests.LevelLog = LevelQuestLog.Level80Bonus;
        var giver = new QuestNpc(GiverCode: 1012);
        var curser = new QuestNpc(GiverCode: 1013);
        Assert.True(Talk(cursed, giver).Prompt is { Code: QuestCode.Level90Bonus, State: 0 }, "the level 90 chain was not offered");
        Expect(cursed, giver, QuestAction.Accept, QuestCode.Level90Bonus, 0, "level 90 chain accept failed");
        Talk(cursed, curser);
        Assert.True(cursed.Quests.Active.Variant is >= 1 and <= 3, "the cursed prey was not drawn");
        Expect(cursed, curser, QuestAction.Proceed, QuestCode.Level90Bonus, 1, "the cursed hunt did not start");
        cursed.RefreshQuestEffects();
        Assert.True(cursed.Effects.TryGet(QuestEffects.CurseCode, out _) && cursed.Quests.Active.KillsNeeded == 50, "the curse is missing");
        Assert.True(QuestTick.OnDeath(Context(cursed)) == false && cursed.Quests.Active is { State: 1, Variant: 0 }, "a death did not reset the curse hunt");
        Talk(cursed, curser);
        Expect(cursed, curser, QuestAction.Proceed, QuestCode.Level90Bonus, 1, "the cursed hunt did not restart");
        Kill(cursed, cursed.Quests.Active.Target, 50);
        Assert.True(cursed.Quests.Active.State == 3, "the cursed hunt did not finish");
        points = cursed.StatPoints;
        Expect(cursed, giver, QuestAction.Proceed, QuestCode.Level90Bonus, 3, "the level 90 chain did not finish");
        cursed.RefreshQuestEffects();
        Assert.True(cursed.StatPoints == points + 3 && !cursed.Effects.TryGet(QuestEffects.CurseCode, out _), "the level 90 chain reward differs");
        points = cursed.StatPoints;
        cursed.LevelUp();
        Assert.True(cursed.StatPoints == points + 10, "a level-up after both chains did not pay ten points");
        return Task.CompletedTask;
    }

    private static Task WingAsync()
    {
        var npc = new QuestNpc(WingExchange: true);
        var flyer = Character(PlayerClass.Fighter, "Flyer", 50, money: 4000);
        Assert.True(Talk(flyer, npc).Prompt is { Followup: QuestFollowup.Close, Document: "M_Wing_a" }, "the wing NPC did not explain the first wing");
        Give(flyer, Code("GP101"));
        Give(flyer, Code("GP102"));
        var offer = Talk(flyer, npc).Prompt;
        Assert.True(offer is { Followup: QuestFollowup.Confirm, Document: "M_Wing_b", ConfirmDetail: "Metal Wing", State: 0 }, "the exchange offer differs");
        Expect(flyer, npc, QuestAction.Accept, QuestCode.None, 0, "the exchange failed");
        Assert.True(flyer.Money == 1000 && QuestInventory.Has(flyer, Code("QW101")) && !QuestInventory.Has(flyer, Code("GP101")), "the metal wing differs");
        Assert.True(Run(flyer, npc, QuestAction.Accept, QuestCode.None, 0).Status == QuestStatus.Stale, "the first recipe ran twice");
        Give(flyer, Code("GP102"));
        Give(flyer, Code("GP103"));
        Assert.True(Run(flyer, npc, QuestAction.Accept, QuestCode.None, 1).Status == QuestStatus.NotEnoughGold, "a wing was exchanged without gold");
        Assert.True(Run(flyer, QuestNpc.None, QuestAction.Accept, QuestCode.None, 1).Status == QuestStatus.Stale, "a wing was exchanged away from the NPC");
        return Task.CompletedTask;
    }

    private static Task DropsAsync()
    {
        var novice = Character(PlayerClass.Fighter, "Drops", 20, tier: 0);
        var veteran = Character(PlayerClass.Fighter, "Veteran", 40, tier: 1);
        Assert.True(QuestDrops.PassesSource(novice, QuestItems.SilverWolverineTrophy, QuestDropSource.MainTable)
            && !QuestDrops.PassesSource(veteran, QuestItems.SilverWolverineTrophy, QuestDropSource.MainTable)
            && QuestDrops.PassesSource(veteran, QuestItems.Vamp, QuestDropSource.MainTable)
            && QuestDrops.PassesSource(veteran, QuestItems.SilverWolverineTrophy, QuestDropSource.BonusTable), "drop sources differ");
        Assert.True(QuestItems.IsQuestDrop(QuestItems.Vamp) && QuestItems.IsQuestDrop(QuestItems.Honey) && !QuestItems.IsQuestDrop(Code("PL101")),
            "quest drop categories differ");

        var always = new CountingRng(99);
        Assert.True(QuestDrops.EventItemDrops(always, Code("QT114"), 0) && !QuestDrops.EventItemDrops(always, QuestItems.RoyalAmulet, 2)
            && QuestDrops.EventItemDrops(new CountingRng(20), QuestItems.RoyalAmulet, 2)
            && !QuestDrops.EventItemDrops(new CountingRng(20), QuestItems.RoyalAmulet, 3), "the royal amulet chance differs");

        Drop(novice, QuestItems.SilverWolverineTrophy);
        Drop(novice, QuestItems.LostHammer);
        Assert.True(!QuestInventory.Has(novice, QuestItems.SilverWolverineTrophy) && !QuestInventory.Has(novice, QuestItems.LostHammer),
            "quest items reached a pack without their quest");

        Give(veteran, QuestItems.GoldenWolverineTrophy);
        Assert.True(QuestDrops.UpkeepDue(veteran, 0), "a leftover trophy was not noticed");
        Drop(veteran, Code("QT114"), source: QuestDropSource.EventItem);
        Assert.True(!QuestInventory.Has(veteran, QuestItems.GoldenWolverineTrophy) && QuestInventory.Has(veteran, Code("QT114")),
            "leftovers stayed or a free event item was refused");
        return Task.CompletedTask;
    }

    private static Task TierAuthorityAsync()
    {
        var fighter = Character(PlayerClass.Fighter, "Tier", 20);
        var progression = new Simulation.Progression.ProgressionCommand(Guid.NewGuid(), fighter.ProgressionRevision,
            Simulation.Progression.ProgressionAction.AllocateAttribute, CharacterAttribute.Strength, 1);
        fighter.ApplyProgression(progression);
        Assert.True(fighter.Tier == 0, "progression moved the tier");
        var lowGroup = new QuestNpc(ClassChangeGroup: 1, HomeMaster: true);
        Assert.True(Talk(fighter, lowGroup).ClassChangeOffered, "a first-group master did not offer the first rank-up");
        var later = Character(PlayerClass.Fighter, "Later", 40, tier: 1);
        Assert.True(!Talk(later, lowGroup).ClassChangeOffered && Talk(later, HomeMaster).ClassChangeOffered,
            "a first-group master offered a later rank-up");
        var done = Character(PlayerClass.Fighter, "Done", 100, tier: 3);
        Assert.True(!Talk(done, HomeMaster).ClassChangeOffered
            && Run(done, HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0).Status == QuestStatus.Rejected, "a final-tier class changed again");
        Assert.True(ClassProfiles.Of(PlayerClass.Fighter).TitleAt(1).Length > 0, "job titles are missing");

        // The quests split the classes by class number: the assassin takes the Morion side but gathers trophies.
        var assassin = Character(PlayerClass.Assassin, "Shade", 30);
        Assert.True(Talk(assassin, new QuestNpc(GiverCode: 1001)).Prompt is null && Talk(assassin, new QuestNpc(GiverCode: 1002)).Prompt is
            { Code: QuestCode.Level30 } && QuestClasses.HomeField(assassin) == FieldCodes.Pillai, "the assassin's quest side differs");
        Assert.True(Run(Character(PlayerClass.Assassin, "Shade", 20), HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0).Prompt is
            { Code: QuestCode.RankUpTrophies }, "the assassin's first rank-up differs");
        Assert.True(Run(Character(PlayerClass.Shaman, "Mystic", 20), HomeMaster, QuestAction.ChangeClass, QuestCode.None, 0).Prompt?.Code is
            QuestCode.RankUpHoney or QuestCode.RankUpOil, "the shaman's first rank-up differs");
        return Task.CompletedTask;
    }

    private sealed class CountingRng(int value) : Simulation.World.Ai.ISimRng
    {
        public int NextInclusive(int minInclusive, int maxInclusive) => Math.Clamp(value, minInclusive, maxInclusive);

        public int NextExclusive(int minInclusive, int maxExclusive) => Math.Clamp(value, minInclusive, maxExclusive - 1);

        public float NextUnit() => 0f;
    }
}
