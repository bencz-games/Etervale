using System.Numerics;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Ai;
using static PristonTale.Tests.QuestFixture;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

// The level 106, 110 and 114 elite chains on the shared rules: offers by level and order, objectives, timer, items owed
// to the upkeep, the item used in its area, the rewards and the saved log.
internal static class EliteQuestTests
{
    private static readonly QuestNpc ForceMaster = new(GiverCode: 1006);
    private static readonly QuestNpc Ray = new(GiverCode: 1013);
    private static readonly QuestNpc Derik = new(GiverCode: 1012);
    private static readonly QuestNpc WanderingSoul = new(GiverCode: 2300);
    private static readonly QuestNpc Celina = new(GiverCode: 2301);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("an elite chain offers its next quest from the chain's level, once no quest runs, and its hunt counts in its field only", OfferAndHuntAsync);
        yield return ("an elite quest reports for its experience, joins the log and opens the chain's next quest", ReportAsync);
        yield return ("Chaos Cara's hunt runs out and drops the quest; the ring it earns waits for the upkeep", TimerAndOwedItemAsync);
        yield return ("drops and crystals roll 51 in 1000, each crystal once; the report takes them for the Dragon Crystal", CrystalsAsync);
        yield return ("an elite quest's item counts only inside its area, alone or with its kill, and pays elite and stat points", UseItemAsync);
        yield return ("the elite log and a quest's flags are saved, and the quest window lists the running and completed quests", StorageAsync);
    }

    // A character past every other quest those NPCs give, so only the elite chains answer.
    private static PlayerCharacter Elite(int level, string name = "Elite")
    {
        var character = Character(PlayerClass.Fighter, name, level, tier: 3);
        character.Quests.LevelLog = (LevelQuestLog)0xFF;
        character.Quests.ElementaryLog = (ElementaryQuestLog)0xFF;
        return character;
    }

    private static void Complete(PlayerCharacter character, params QuestCode[] codes)
    {
        foreach (var code in codes)
        {
            character.Quests.EliteLog |= EliteQuests.Bit(code);
        }
    }

    private static QuestContext At(PlayerCharacter character, QuestNpc npc, int field, Vector3 position, QuestAction action, QuestCode code, int state)
    {
        var context = new QuestContext(character, npc, new CatalogQuestItemSource(Items), new SeededSimRng(1), 0, field) { Position = position };
        QuestEngine.Run(context, Command(action, code, state));
        return context;
    }

    private static Task OfferAndHuntAsync()
    {
        Assert.True(Talk(Elite(105), ForceMaster).Prompt is null, "an elite quest was offered below its chain's level");
        var hunter = Elite(106);
        var offer = Talk(hunter, ForceMaster).Prompt;
        Assert.True(offer is { Code: QuestCode.Elite106A, State: 0, Followup: QuestFollowup.Confirm, Document: "106LVQuest_1_Start" }
            && offer.ConfirmDetail == QuestTexts.QuestName("Lovers of history1") && offer.ConfirmLine1 == GameText.Of(GameTextId.ConfirmAcceptQuest),
            "the 106 chain's first offer differs");
        Assert.True(Talk(hunter, Derik).Prompt is null, "the 110 chain was offered at level 106");
        Expect(hunter, ForceMaster, QuestAction.Accept, QuestCode.Elite106A, 0, "accept failed");
        Assert.True(hunter.Quests.Active is { Code: QuestCode.Elite106A, State: EliteQuests.Running, KillsNeeded: 20, Kills: 0 }, "the hunt did not start");
        Assert.True(Talk(hunter, ForceMaster).Prompt is { Document: "106LVQuest_1_Path", Followup: QuestFollowup.Close }, "the running quest's path differs");

        var other = Elite(110, "Other");
        Expect(other, ForceMaster, QuestAction.Accept, QuestCode.Elite106A, 0, "accept failed");
        Assert.True(Talk(other, Derik).Prompt is null && Run(other, Derik, QuestAction.Accept, QuestCode.Elite110A, 0).Status == QuestStatus.Stale,
            "a second elite quest started beside the running one");

        Kill(hunter, 98, 5, field: 3);
        Assert.True(hunter.Quests.Active.Kills == 0, "Cold Eyes counted outside Ice 1");
        Kill(hunter, 98, 19, field: 31);
        var last = Kill(hunter, 98, 1, field: 31);
        Assert.True(hunter.Quests.Active is { State: EliteQuests.Met, Kills: 20 }
            && last.Notices.Any(n => n.Kind == QuestNoticeKind.Event && n.Text == QuestTexts.EliteProgress("Lovers of history1", 20, 20)),
            "the hunt did not meet its objective with its notice");
        Kill(hunter, 98, 3, field: 31);
        Assert.True(hunter.Quests.Active.Kills == 20, "kills counted past the objective");
        return Task.CompletedTask;
    }

    private static Task ReportAsync()
    {
        var hunter = Elite(106);
        hunter.Quests.Active = new ActiveQuest { Code = QuestCode.Elite106A, State = EliteQuests.Met, Kills = 20, KillsNeeded = 20 };
        Assert.True(Talk(hunter, ForceMaster).Prompt is { Code: QuestCode.Elite106A, State: EliteQuests.Met, Followup: QuestFollowup.Proceed,
            Document: "106LVQuest_1_End" }, "the end dialog differs");
        var report = Expect(hunter, ForceMaster, QuestAction.Proceed, QuestCode.Elite106A, EliteQuests.Met, "the report failed");
        var cost = ExperienceTable.ForLevel(107) - ExperienceTable.ForLevel(106);
        Assert.True(report.Experience == cost / 100 && EliteQuests.Completed(hunter.Quests, QuestCode.Elite106A) && hunter.Quests.Active.IsIdle
            && hunter.Quests.HasFinished(QuestCode.Elite106A), "the report did not pay 1% of level 106 or join the log");
        Assert.True(Run(hunter, ForceMaster, QuestAction.Proceed, QuestCode.Elite106A, EliteQuests.Met).Status == QuestStatus.Stale,
            "a replayed report paid twice");
        Assert.True(Talk(hunter, ForceMaster).Prompt is { Code: QuestCode.Elite106B, State: 0 }, "the chain's next quest was not offered");
        Assert.True(EliteQuests.Experience(EliteQuests.Find(QuestCode.Elite114A)!) == (ExperienceTable.ForLevel(115) - ExperienceTable.ForLevel(114)) * 2 / 100,
            "the 114 chain's experience is not 2% of its level");
        return Task.CompletedTask;
    }

    private static Task TimerAndOwedItemAsync()
    {
        var hunter = Elite(106);
        Complete(hunter, QuestCode.Elite106A);
        Expect(hunter, ForceMaster, QuestAction.Accept, QuestCode.Elite106B, 0, "accept failed");
        Assert.True(hunter.Quests.Active is { Timer: QuestTimerKind.Expiring, TimerMs: 1_800_000 }, "Chaos Cara's hunt has no time limit");
        var context = Context(hunter);
        QuestTick.Step(context, 1_799_000);
        Assert.True(hunter.Quests.Active is { Code: QuestCode.Elite106B, TimerMs: 1000 }, "the time limit ran wrong");
        QuestTick.Step(context, 1000);
        Assert.True(hunter.Quests.Active.IsIdle && context.Notices.Any(n => n.Text == QuestTexts.EliteExpired)
            && !EliteQuests.Completed(hunter.Quests, QuestCode.Elite106B), "the expired quest was not dropped with its notice");

        Expect(hunter, ForceMaster, QuestAction.Accept, QuestCode.Elite106B, 0, "the dropped quest was not offered again");
        Assert.True(QuestTick.OnKill(Context(hunter, field: 8), 65), "Chaos Cara's kill did not earn its ring");
        Assert.True(hunter.Quests.Active is { State: EliteQuests.Met, Timer: QuestTimerKind.None } && EliteQuests.OwesItems(hunter.Quests.Active)
            && QuestDrops.UpkeepDue(hunter, 0) && !QuestInventory.Has(hunter, EliteQuests.CursedRing), "the ring was not owed to the upkeep");
        QuestDrops.Upkeep(Context(hunter), []);
        Assert.True(QuestInventory.Has(hunter, EliteQuests.CursedRing) && !EliteQuests.OwesItems(hunter.Quests.Active), "the upkeep did not hand out the ring");

        // The ring goes to Ray, who takes it at the report.
        Expect(hunter, ForceMaster, QuestAction.Proceed, QuestCode.Elite106B, EliteQuests.Met, "the report failed");
        Expect(hunter, ForceMaster, QuestAction.Accept, QuestCode.Elite106C, 0, "the delivery was not accepted");
        Assert.True(hunter.Quests.Active.State == EliteQuests.Met && Talk(hunter, ForceMaster).Prompt is { Followup: QuestFollowup.Close }
            && Talk(hunter, Ray).Prompt is { Document: "106LVQuest_3_End" }, "a delivery is not reported at its reporter");
        Expect(hunter, Ray, QuestAction.Proceed, QuestCode.Elite106C, EliteQuests.Met, "the delivery failed");
        Assert.True(!QuestInventory.Has(hunter, EliteQuests.CursedRing), "the report did not take the ring");
        return Task.CompletedTask;
    }

    private static Task CrystalsAsync()
    {
        var digger = Elite(106);
        Complete(digger, QuestCode.Elite106A, QuestCode.Elite106B, QuestCode.Elite106C);
        Expect(digger, Ray, QuestAction.Accept, QuestCode.Elite106D, 0, "accept failed");
        var context = Context(digger, seed: 3);
        var kills = 0;
        while (digger.Quests.Active.State == EliteQuests.Running && kills < 5000)
        {
            QuestTick.OnKill(context, 99);
            kills++;
        }

        Assert.True(digger.Quests.Active is { State: EliteQuests.Met, Kills: 1 } && kills > 1 && EliteQuests.OwesItems(digger.Quests.Active),
            $"the Mine Golem's crystal did not drop by chance ({kills} kills)");

        var hunter = Elite(114);
        Complete(hunter, QuestCode.Elite114A, QuestCode.Elite114B);
        Expect(hunter, Celina, QuestAction.Accept, QuestCode.Elite114C, 0, "accept failed");
        var rolls = Context(hunter, seed: 5);
        var tries = 0;
        while (hunter.Quests.Active.State == EliteQuests.Running && tries < 20_000)
        {
            QuestTick.OnKill(rolls, new[] { 112, 113, 115, 116 }[tries % 4]);
            tries++;
        }

        Assert.True(hunter.Quests.Active.State == EliteQuests.Met && (hunter.Quests.Active.Flags & 0xF) == 0xF, "the four crystals were not gathered");
        Assert.True(rolls.Notices.Count(n => n.Kind == QuestNoticeKind.Chat) == 3, "each crystal did not post its count once");
        QuestDrops.Upkeep(Context(hunter), []);
        uint[] crystals = [EliteQuests.TSoldierCrystal, EliteQuests.TEliteCrystal, EliteQuests.AceroCrystal, EliteQuests.ChalybsCrystal];
        Assert.True(crystals.All(code => QuestInventory.Count(hunter, code) == 1), "the upkeep did not hand out each crystal once");
        Expect(hunter, Celina, QuestAction.Proceed, QuestCode.Elite114C, EliteQuests.Met, "the report failed");
        Assert.True(crystals.All(code => !QuestInventory.Has(hunter, code)) && QuestInventory.Has(hunter, EliteQuests.DragonCrystal),
            "the report did not trade the crystals for the Dragon Crystal");
        return Task.CompletedTask;
    }

    private static Task UseItemAsync()
    {
        var soul = Elite(114);
        Complete(soul, QuestCode.Elite114A, QuestCode.Elite114B, QuestCode.Elite114C);
        Expect(soul, Celina, QuestAction.Accept, QuestCode.Elite114D, 0, "accept failed");
        Assert.True(QuestInventory.Has(soul, EliteQuests.DemonSoul) && EliteQuests.ItemToUse(soul.Quests.Active) == EliteQuests.DemonSoul,
            "the demon soul was not handed out to be used");
        var centre = new Vector3(12700, 0, -57000);
        Assert.True(At(soul, QuestNpc.None, 48, new Vector3(0, 0, 0), QuestAction.UseItem, QuestCode.Elite114D, EliteQuests.Running).Status
            == QuestStatus.Rejected && At(soul, QuestNpc.None, 46, centre, QuestAction.UseItem, QuestCode.Elite114D, EliteQuests.Running).Status
            == QuestStatus.Rejected && soul.Quests.Active.State == EliteQuests.Running, "the soul counted outside the weapon's centre");
        Assert.True(QuestEngine.Run(new QuestContext(soul, QuestNpc.None, new CatalogQuestItemSource(Items), new SeededSimRng(1), 0, 48) { Position = centre },
            Command(QuestAction.UseItem, QuestCode.Elite114D, EliteQuests.Running)) && soul.Quests.Active.State == EliteQuests.Met,
            "the soul used at the weapon's centre did not meet the objective");
        var stats = soul.StatPoints;
        Expect(soul, WanderingSoul, QuestAction.Proceed, QuestCode.Elite114D, EliteQuests.Met, "the report failed");
        Assert.True(soul.StatPoints == stats + 10 && !QuestInventory.Has(soul, EliteQuests.DemonSoul), "the demon soul did not pay ten stat points");

        var bomber = Elite(110);
        Complete(bomber, QuestCode.Elite110A, QuestCode.Elite110B, QuestCode.Elite110C, QuestCode.Elite110D, QuestCode.Elite110E, QuestCode.Elite110F,
            QuestCode.Elite110G);
        Expect(bomber, Derik, QuestAction.Accept, QuestCode.Elite110H, 0, "accept failed");
        Kill(bomber, 105);
        Assert.True(bomber.Quests.Active.State == EliteQuests.Running && (bomber.Quests.Active.Flags & EliteQuests.Killed) != 0,
            "Draxos's kill alone met the objective");
        var room = new Vector3(-14400, 0, -59300);
        Assert.True(At(bomber, QuestNpc.None, 46, room, QuestAction.UseItem, QuestCode.Elite110H, EliteQuests.Running).Status == QuestStatus.Ok
            && bomber.Quests.Active.State == EliteQuests.Met, "the bomb in the mechanical room after Draxos did not meet the objective");
        var elite = bomber.EliteSkillPoints;
        var report = Expect(bomber, Derik, QuestAction.Proceed, QuestCode.Elite110H, EliteQuests.Met, "the report failed");
        Assert.True(bomber.EliteSkillPoints == elite + 3 && report.Notices.Any(n => n.Text == QuestTexts.ElitePoints(3)),
            "the 110 chain did not pay three elite points");
        return Task.CompletedTask;
    }

    private static Task StorageAsync()
    {
        var character = Elite(114);
        Complete(character, QuestCode.Elite106A, QuestCode.Elite110D);
        character.Quests.Active = new ActiveQuest { Code = QuestCode.Elite114C, State = EliteQuests.Running, Flags = 0b101 | EliteQuests.ObjectiveItemOwed };
        var restored = QuestStateCodec.Read(CharacterJson.Write(QuestStateCodec.Capture(character.Quests)));
        Assert.True(restored.EliteLog == character.Quests.EliteLog && restored.Active.Flags == character.Quests.Active.Flags
            && restored.Active.Code == QuestCode.Elite114C, "the elite log or the quest's flags were not saved");

        var lines = QuestBoard.Progress(character);
        var finished = QuestBoard.Finished(character);
        Assert.True(lines.Single() == new QuestBoardEntry("Soul of Devil", "HaQuest\\114LVQuest_3.msg")
            && finished.Select(line => line.Title).SequenceEqual(["Invaders chopped up(Complete)", "Lovers of history1(Complete)"]),
            "the quest window does not list the elite quests");
        Assert.True(QuestBoard.Documents.Count(document => document.Contains("LVQuest_", StringComparison.Ordinal)) >= 21, "the board lacks the elite documents");
        return Task.CompletedTask;
    }
}
