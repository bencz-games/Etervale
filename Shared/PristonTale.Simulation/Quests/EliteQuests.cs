using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Quests;

// What an elite quest asks: talk to its reporter, kill its monsters, kill them for a drop, gather one crystal from each,
// use its item inside an area, or both of the last two.
public enum EliteObjective : byte
{
    Deliver,
    Kill,
    Drop,
    Crystals,
    Use,
    UseAndKill
}

// The area an elite quest's item is used in (world units).
public readonly record struct EliteArea(int Field, int MinX, int MaxX, int MinZ, int MaxZ)
{
    public static readonly EliteArea None = new(-1, 0, 0, 0, 0);

    public bool Contains(int field, float x, float z) => Field >= 0 && field == Field && x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

// One quest of the three elite chains. Giver and Reporter are NPC quest giver codes; Monsters are monster quest codes
// (Field -1 counts them anywhere); Seconds limits the objective; ExpPercent is the report's experience as a percent of the
// cost of the chain's level. Title and Board name the quest window line; Document the dialogs (<Document>_Start|Path|End).
public sealed record EliteQuest(
    QuestCode Code,
    int Chain,
    int Giver,
    int Reporter,
    EliteObjective Objective,
    int[] Monsters,
    int Count,
    int Field,
    int Seconds,
    uint AcceptItem,
    uint ObjectiveItem,
    uint[] Crystals,
    EliteArea Area,
    int ExpPercent,
    int ElitePoints,
    int StatPoints,
    uint RewardItem,
    uint[] ReportItems,
    string Title,
    string Document)
{
    public int Level => EliteQuests.ChainLevels[Chain];
    public string Board => $"HaQuest\\{Document}.msg";
}

// The level 106, 110 and 114 chains: 21 quests, each chain in order, that end in elite skill points (2, 3 and 3) and, at
// the 114 chain's demon soul, ten stat points. A quest is accepted from its giver once the chain reaches it and no quest
// runs, and reported to its reporter once its objective is met: the report takes the quest's items back and pays its
// experience, points and reward. Kills count for the credited player; a drop or crystal rolls 51 in 1000 per kill; Chaos
// Cara's hunt runs out after 1800 seconds of play time and drops the quest. Items a kill earns wait for the quest upkeep.
public static class EliteQuests
{
    public static readonly int[] ChainLevels = [106, 110, 114];

    // A drop or a crystal comes with this many kills in 1000.
    public const int DropPerMille = 51;

    // ActiveQuest.Flags: bits 0-3 the crystals gathered (by monster), the item used, the monster killed; the items a kill
    // earned that the upkeep still has to hand out (the objective item, then one bit per crystal).
    public const int ItemUsed = 1 << 4;
    public const int Killed = 1 << 5;
    public const int ObjectiveItemOwed = 1 << 8;
    private const int CrystalsGathered = 0xF;
    private const int CrystalOwedShift = 9;

    // The state of a running elite quest.
    public const int Running = 1;
    public const int Met = 2;

    private const int ForceMaster = 1006;
    private const int Derik = 1012;
    private const int Ray = 1013;
    private const int Salon = 2200;
    private const int Newter = 2201;
    private const int WanderingSoul = 2300;
    private const int Celina = 2301;

    private const int Ice1 = 31;
    private const int AncientWeapon = 48;
    private static readonly EliteArea MechanicalRoom = new(46, -14671, -14105, -59541, -59059);
    private static readonly EliteArea WeaponCentre = new(AncientWeapon, 12314, 13215, -57529, -56699);

    // Monster quest codes.
    private const int ColdEye = 98, ChaosCara = 65, MineGolem = 99, Valento = 101;
    private const int LabEngineer = 102, ITechnician = 103, ISoldier = 94, IBomber = 96, IElite = 95, Tulla = 104, Draxos = 105;
    private const int Nihil = 114, TSoldier = 112, TElite = 113, Acero = 115, Chalybs = 116, Greedy = 117;

    public static readonly uint CursedRing = ItemCodes.Parse("QT127");
    public static readonly uint MagicCrystal = ItemCodes.Parse("QT128");
    public static readonly uint EngagementRing = ItemCodes.Parse("QT129");
    public static readonly uint QuestionableDocument = ItemCodes.Parse("QT130");
    public static readonly uint SalonReport = ItemCodes.Parse("QT131");
    public static readonly uint UnstableCrystal = ItemCodes.Parse("QT132");
    public static readonly uint DestructionBomb = ItemCodes.Parse("QT133");
    public static readonly uint ConcentratedSoul = ItemCodes.Parse("QT136");
    public static readonly uint TSoldierCrystal = ItemCodes.Parse("QT137");
    public static readonly uint TEliteCrystal = ItemCodes.Parse("QT138");
    public static readonly uint AceroCrystal = ItemCodes.Parse("QT139");
    public static readonly uint ChalybsCrystal = ItemCodes.Parse("QT140");
    public static readonly uint DemonSoul = ItemCodes.Parse("QT141");
    public static readonly uint MagicJail = ItemCodes.Parse("QT142");
    public static readonly uint SecretDocument = ItemCodes.Parse("QT143");
    public static readonly uint DragonCrystal = ItemCodes.Parse("GP141");

    public static readonly IReadOnlyList<EliteQuest> All =
    [
        Quest(QuestCode.Elite106A, 0, ForceMaster, ForceMaster, EliteObjective.Kill, [ColdEye], 20, Ice1, expPercent: 1, title: "Lovers of history1",
            document: "106LVQuest_1"),
        Quest(QuestCode.Elite106B, 0, ForceMaster, ForceMaster, EliteObjective.Kill, [ChaosCara], 1, seconds: 1800, objectiveItem: CursedRing,
            expPercent: 1, title: "Lovers of history2", document: "106LVQuest_2"),
        Quest(QuestCode.Elite106C, 0, ForceMaster, Ray, EliteObjective.Deliver, reportItems: [CursedRing], title: "Ring purification1",
            document: "106LVQuest_3"),
        Quest(QuestCode.Elite106D, 0, Ray, Ray, EliteObjective.Drop, [MineGolem], 1, objectiveItem: MagicCrystal, expPercent: 1,
            reportItems: [MagicCrystal], title: "Ring purification2", document: "106LVQuest_4"),
        Quest(QuestCode.Elite106E, 0, Ray, ForceMaster, EliteObjective.Deliver, acceptItem: EngagementRing, reportItems: [EngagementRing],
            title: "Eternal Love1", document: "106LVQuest_5"),
        Quest(QuestCode.Elite106F, 0, ForceMaster, ForceMaster, EliteObjective.Kill, [Valento], 1, acceptItem: EngagementRing, elitePoints: 2,
            reportItems: [EngagementRing], title: "Eternal Love2", document: "106LVQuest_6"),

        Quest(QuestCode.Elite110A, 1, Derik, Derik, EliteObjective.Drop, [LabEngineer], 1, objectiveItem: QuestionableDocument, expPercent: 1,
            reportItems: [QuestionableDocument], title: "War of the Ancient", document: "110LVQuest_1"),
        Quest(QuestCode.Elite110B, 1, Derik, Salon, EliteObjective.Deliver, acceptItem: QuestionableDocument, reportItems: [QuestionableDocument],
            title: "Questionable document", document: "110LVQuest_2"),
        Quest(QuestCode.Elite110C, 1, Salon, Derik, EliteObjective.Deliver, acceptItem: SalonReport, reportItems: [SalonReport],
            title: "Report delivery", document: "110LVQuest_3"),
        Quest(QuestCode.Elite110D, 1, Derik, Derik, EliteObjective.Kill, [ITechnician, ISoldier, IBomber, IElite], 60, expPercent: 1,
            title: "Invaders chopped up", document: "110LVQuest_4"),
        Quest(QuestCode.Elite110E, 1, Derik, Newter, EliteObjective.Deliver, title: "Clever Newter", document: "110LVQuest_5"),
        Quest(QuestCode.Elite110F, 1, Newter, Newter, EliteObjective.Kill, [Tulla], 1, objectiveItem: UnstableCrystal, expPercent: 1,
            reportItems: [UnstableCrystal], title: "Manufacturing bomb", document: "110LVQuest_6"),
        Quest(QuestCode.Elite110G, 1, Newter, Derik, EliteObjective.Deliver, acceptItem: DestructionBomb, reportItems: [DestructionBomb],
            title: "Delivery bomb", document: "110LVQuest_7"),
        Quest(QuestCode.Elite110H, 1, Derik, Derik, EliteObjective.UseAndKill, [Draxos], 1, acceptItem: DestructionBomb, area: MechanicalRoom,
            elitePoints: 3, reportItems: [DestructionBomb], title: "Destroyed destruction weapon", document: "110LVQuest_8"),

        Quest(QuestCode.Elite114A, 2, WanderingSoul, WanderingSoul, EliteObjective.Kill, [Nihil], 50, AncientWeapon, expPercent: 2,
            title: "Mission from the ancestor", document: "114LVQuest_1"),
        Quest(QuestCode.Elite114B, 2, WanderingSoul, Celina, EliteObjective.Deliver, acceptItem: ConcentratedSoul, reportItems: [ConcentratedSoul],
            title: "Patron's request", document: "114LVQuest_2"),
        Quest(QuestCode.Elite114C, 2, Celina, Celina, EliteObjective.Crystals, [TSoldier, TElite, Acero, Chalybs], 4,
            crystals: [TSoldierCrystal, TEliteCrystal, AceroCrystal, ChalybsCrystal], rewardItem: DragonCrystal,
            reportItems: [TSoldierCrystal, TEliteCrystal, AceroCrystal, ChalybsCrystal], title: "Soul of Devil", document: "114LVQuest_3"),
        Quest(QuestCode.Elite114D, 2, Celina, WanderingSoul, EliteObjective.Use, acceptItem: DemonSoul, area: WeaponCentre, statPoints: 10,
            reportItems: [DemonSoul], title: "Shattering Ancient weapon", document: "114LVQuest_4"),
        Quest(QuestCode.Elite114E, 2, WanderingSoul, Celina, EliteObjective.Deliver, title: "Devious Necromancer", document: "114LVQuest_5"),
        Quest(QuestCode.Elite114F, 2, Celina, Celina, EliteObjective.Kill, [Greedy], 1, acceptItem: MagicJail, expPercent: 2,
            reportItems: [MagicJail], title: "Capturing the devil", document: "114LVQuest_6"),
        Quest(QuestCode.Elite114G, 2, Celina, Derik, EliteObjective.Deliver, acceptItem: SecretDocument, elitePoints: 3, reportItems: [SecretDocument],
            title: "Miranda's location", document: "114LVQuest_7")
    ];

    private static EliteQuest Quest(QuestCode code, int chain, int giver, int reporter, EliteObjective objective, int[] monsters = null, int count = 0,
        int field = -1, int seconds = 0, uint acceptItem = 0, uint objectiveItem = 0, uint[] crystals = null, EliteArea? area = null, int expPercent = 0,
        int elitePoints = 0, int statPoints = 0, uint rewardItem = 0, uint[] reportItems = null, string title = "", string document = "") =>
        new(code, chain, giver, reporter, objective, monsters ?? [], count, field, seconds, acceptItem, objectiveItem, crystals ?? [], area ?? EliteArea.None,
            expPercent, elitePoints, statPoints, rewardItem, reportItems ?? [], title, document);

    public static bool IsElite(QuestCode code) => code is >= QuestCode.Elite106A and <= QuestCode.Elite114G;

    public static EliteQuest Find(QuestCode code) => IsElite(code) ? All[code - QuestCode.Elite106A] : null;

    public static EliteQuestLog Bit(QuestCode code) => IsElite(code) ? (EliteQuestLog)(1u << (code - QuestCode.Elite106A)) : EliteQuestLog.None;

    public static bool Completed(QuestJournal journal, QuestCode code) => IsElite(code) && (journal.EliteLog & Bit(code)) != 0;

    // The chain's next quest the log has not completed; null once the chain is done.
    public static EliteQuest Next(QuestJournal journal, int chain)
    {
        foreach (var quest in All)
        {
            if (quest.Chain == chain && !Completed(journal, quest.Code))
            {
                return quest;
            }
        }

        return null;
    }

    // The quest's experience: its percent of what the chain's level takes, from entering it to entering the next.
    public static ulong Experience(EliteQuest quest) =>
        (ExperienceTable.ForLevel(quest.Level + 1) - ExperienceTable.ForLevel(quest.Level)) * (ulong)quest.ExpPercent / 100;

    // The item a running quest is waiting to be used, or 0.
    public static uint ItemToUse(ActiveQuest active) =>
        Find(active.Code) is { Objective: EliteObjective.Use or EliteObjective.UseAndKill } quest && active.State == Running
            && (active.Flags & ItemUsed) == 0 ? quest.AcceptItem : 0;

    // A giver offers its chain's next quest; the running quest talks at its giver (the path) and its reporter (the path,
    // or the end once its objective is met). Only when no other quest of the NPC answered.
    public static void Talk(QuestContext context, int giverCode)
    {
        var journal = context.Journal;
        if (Find(journal.Active.Code) is { } running)
        {
            if (journal.Active.State == Met && running.Reporter == giverCode)
            {
                context.Show(QuestSteps.Proceed(running.Code, Met, running.Document + "_End"));
            }
            else if (running.Giver == giverCode || running.Reporter == giverCode)
            {
                context.Show(QuestSteps.Close(running.Code, journal.Active.State, running.Document + "_Path"));
            }

            return;
        }

        for (var chain = 0; chain < ChainLevels.Length; chain++)
        {
            if (Next(journal, chain) is not { } next || next.Giver != giverCode || context.Character.Level < next.Level)
            {
                continue;
            }

            if (!journal.Active.IsIdle)
            {
                context.Block();
                return;
            }

            context.Show(QuestSteps.Confirm(next.Code, 0, next.Document + "_Start", QuestTexts.AcceptQuest, QuestTexts.QuestName(next.Title)));
            return;
        }
    }

    public static bool Accept(QuestContext context, QuestCode code, int state)
    {
        var journal = context.Journal;
        if (state != 0 || Find(code) is not { } quest || !journal.Active.IsIdle || Next(journal, quest.Chain) != quest
            || context.Character.Level < quest.Level)
        {
            return QuestSteps.Stale(context);
        }

        if (quest.AcceptItem != 0 && !QuestInventory.TryGrant(context, [new QuestGrant(quest.AcceptItem)]))
        {
            return QuestInventory.Refused(context);
        }

        var active = new ActiveQuest
        {
            Code = code,
            State = quest.Objective == EliteObjective.Deliver ? Met : Running,
            KillsNeeded = quest.Objective is EliteObjective.Kill or EliteObjective.Drop ? quest.Count : 0
        };
        if (quest.Seconds > 0)
        {
            active = active with { Timer = QuestTimerKind.Expiring, TimerMs = quest.Seconds * 1000L };
        }

        journal.Active = active;
        context.Notice(QuestNoticeKind.Fanfare, GameText.None);
        return true;
    }

    // The report at the reporter: the quest's items go back, its experience, points and reward are paid and it joins the log.
    public static bool Report(QuestContext context, QuestCode code, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (Find(code) is not { } quest || active.Code != code || active.State != Met || state != Met)
        {
            return QuestSteps.Stale(context);
        }

        var character = context.Character;
        foreach (var item in quest.ReportItems)
        {
            QuestInventory.Remove(character, item);
        }

        if (quest.RewardItem != 0 && !QuestInventory.TryGrant(context, [new QuestGrant(quest.RewardItem)]))
        {
            return QuestInventory.Refused(context);
        }

        context.Experience += Experience(quest);
        if (quest.ElitePoints != 0 || quest.StatPoints != 0)
        {
            character.ApplyQuestProgress(character.Tier, quest.StatPoints, 0, quest.ElitePoints);
        }

        if (quest.ElitePoints != 0)
        {
            context.Notice(QuestNoticeKind.Message, QuestTexts.ElitePoints(quest.ElitePoints));
        }

        if (quest.StatPoints != 0)
        {
            context.Notice(QuestNoticeKind.Message, QuestTexts.StatPoints(quest.StatPoints));
        }

        journal.EliteLog |= Bit(code);
        QuestSteps.Finish(context, code);
        return true;
    }

    // The quest's item used inside its area, which the player stands in.
    public static bool UseItem(QuestContext context, QuestCode code, int state)
    {
        var active = context.Journal.Active;
        if (Find(code) is not { } quest || active.Code != code || active.State != state || ItemToUse(active) == 0)
        {
            return QuestSteps.Stale(context);
        }

        if (!QuestInventory.Has(context.Character, quest.AcceptItem) || !quest.Area.Contains(context.FieldCode, context.Position.X, context.Position.Z))
        {
            return context.Fail(QuestStatus.Rejected, QuestTexts.NotHere);
        }

        Progressed(context, quest, active with { Flags = active.Flags | ItemUsed });
        return true;
    }

    // Runs on the world tick for the credited player. Returns true when the kill earned an item the upkeep hands out.
    public static bool OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        if (Find(active.Code) is not { } quest || active.State != Running || quest.Field >= 0 && quest.Field != context.FieldCode)
        {
            return false;
        }

        var index = Array.IndexOf(quest.Monsters, monsterCode);
        if (index < 0)
        {
            return false;
        }

        switch (quest.Objective)
        {
            case EliteObjective.Kill:
                active = active with { Kills = active.Kills + 1 };
                break;
            case EliteObjective.Drop:
                if (context.Rng.NextExclusive(0, 1000) >= DropPerMille)
                {
                    return false;
                }

                active = active with { Kills = active.Kills + 1 };
                break;
            case EliteObjective.Crystals:
                if ((active.Flags & (1 << index)) != 0 || context.Rng.NextExclusive(0, 1000) >= DropPerMille)
                {
                    return false;
                }

                active = active with { Flags = active.Flags | 1 << index | 1 << (CrystalOwedShift + index) };
                break;
            case EliteObjective.UseAndKill:
                if ((active.Flags & Killed) != 0)
                {
                    return false;
                }

                active = active with { Flags = active.Flags | Killed };
                break;
            default:
                return false;
        }

        Progressed(context, quest, active);
        return OwesItems(context.Journal.Active);
    }

    // The objective moved: the count is told, and a met objective waits for the report and earns its item.
    private static void Progressed(QuestContext context, EliteQuest quest, ActiveQuest active)
    {
        var met = quest.Objective switch
        {
            EliteObjective.Kill or EliteObjective.Drop => active.Kills >= quest.Count,
            EliteObjective.Crystals => int.PopCount(active.Flags & CrystalsGathered) >= quest.Count,
            EliteObjective.Use => (active.Flags & ItemUsed) != 0,
            EliteObjective.UseAndKill => (active.Flags & (ItemUsed | Killed)) == (ItemUsed | Killed),
            _ => true
        };
        var done = quest.Objective == EliteObjective.Crystals ? int.PopCount(active.Flags & CrystalsGathered) : active.Kills;
        var progress = quest.Count > 0 ? QuestTexts.EliteProgress(quest.Title, done, quest.Count) : QuestTexts.EliteDone(quest.Title);
        if (met)
        {
            active = active with
            {
                State = Met,
                Timer = QuestTimerKind.None,
                TimerMs = 0,
                Flags = quest.ObjectiveItem != 0 ? active.Flags | ObjectiveItemOwed : active.Flags
            };
            context.Notice(QuestNoticeKind.Event, progress);
        }
        else if (quest.Count > 0)
        {
            context.Notice(QuestNoticeKind.Chat, progress);
        }

        context.Journal.Active = active;
    }

    // Chaos Cara's hunt runs out: the quest drops and can be accepted again.
    public static void StepTimer(QuestContext context, long elapsedMs)
    {
        var active = context.Journal.Active;
        if (!IsElite(active.Code) || active.Timer != QuestTimerKind.Expiring || elapsedMs <= 0)
        {
            return;
        }

        if (active.TimerMs > elapsedMs)
        {
            context.Journal.Active = active with { TimerMs = active.TimerMs - elapsedMs };
            return;
        }

        context.Journal.Active = ActiveQuest.Idle;
        context.Notice(QuestNoticeKind.Message, QuestTexts.EliteExpired);
    }

    public static bool OwesItems(ActiveQuest active) => IsElite(active.Code) && (active.Flags & (ObjectiveItemOwed | 0xF << CrystalOwedShift)) != 0;

    // The upkeep hands out what kills earned; an item that does not fit stays owed for the next upkeep.
    public static void Upkeep(QuestContext context)
    {
        var active = context.Journal.Active;
        if (!OwesItems(active) || Find(active.Code) is not { } quest)
        {
            return;
        }

        var flags = active.Flags;
        if ((flags & ObjectiveItemOwed) != 0 && QuestInventory.TryGrant(context, [new QuestGrant(quest.ObjectiveItem)]))
        {
            flags &= ~ObjectiveItemOwed;
        }

        for (var index = 0; index < quest.Crystals.Length; index++)
        {
            var owed = 1 << (CrystalOwedShift + index);
            if ((flags & owed) != 0 && QuestInventory.TryGrant(context, [new QuestGrant(quest.Crystals[index])]))
            {
                flags &= ~owed;
            }
        }

        if (flags != active.Flags)
        {
            context.Journal.Active = active with { Flags = flags };
        }
    }

    // The quest window lines: the running quest, and every completed one.
    public static QuestBoardEntry Line(EliteQuest quest, bool completed) =>
        new(completed ? quest.Title + "(Complete)" : quest.Title, quest.Board);
}
