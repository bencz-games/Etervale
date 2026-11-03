using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Quests;

// Third rank-up: two hunts against the clock (each of one of two monsters picked for the player's level), the royal
// amulet from a named monster of the level, then a duel in the arena with the class's master. Beating the master to a
// sixteenth of its life earns the final tier; dying in the duel sends the player back to the amulet step. A hunt that
// runs out of time is lost and starts over.
public static class RankUpByMasterTrial
{
    public const QuestCode Code = QuestCode.RankUpMasterTrial;
    public static readonly int MinimumLevel = RankUpRules.RequiredLevel(2);
    public const int DuelMinimumLevel = 49;

    // 4900 seconds of play time per hunt; the timer runs in every field and through deaths.
    public const long HuntTimeMs = 4_900_000;

    private const int Offered = 0;
    private const int FirstHunt = 1;
    private const int FirstHuntDone = 2;
    private const int SecondHunt = 3;
    private const int SecondHuntDone = 4;
    private const int AmuletHunt = 5;
    private const int AmuletFound = 6;
    private const int Dueling = 7;
    private const int DuelWon = 8;

    private static readonly int[] LevelSteps = [60, 65, 70, 75, 80, 85, 200];

    private static readonly int[][] FirstPrey =
    [
        [QuestMonsters.Muffin, QuestMonsters.SkeletonKnight],
        [QuestMonsters.HeadCutter, QuestMonsters.SolidSnail],
        [QuestMonsters.ThornCrawler, QuestMonsters.Mummy],
        [QuestMonsters.DoomGuard, QuestMonsters.Figon],
        [QuestMonsters.StoneGiant, QuestMonsters.StoneGolem],
        [QuestMonsters.IronGuard, QuestMonsters.Avelin]
    ];

    private static readonly int[][] SecondPrey =
    [
        [QuestMonsters.AveliskL, QuestMonsters.BeEvil],
        [QuestMonsters.IllusionKnight, QuestMonsters.Nightmare],
        [QuestMonsters.Witch, QuestMonsters.HeavyGoblin],
        [QuestMonsters.Dawlin, QuestMonsters.Stygian],
        [QuestMonsters.Incubus, QuestMonsters.ChainGolem],
        [QuestMonsters.DarkSpecter, QuestMonsters.Metron]
    ];

    private static readonly int[] AmuletHolders =
    [
        QuestMonsters.SolidSnail, QuestMonsters.IllusionKnight, QuestMonsters.AveliskLord, QuestMonsters.Dusk, QuestMonsters.ChainGolem,
        QuestMonsters.Sadness
    ];

    // Bracket of the player's level: 60, 65, 70, 75, 80 and 85 and above.
    public static int Bracket(int level)
    {
        var bracket = 0;
        for (var index = 0; index < LevelSteps.Length - 1; index++)
        {
            if (level >= LevelSteps[index] && level < LevelSteps[index + 1])
            {
                bracket = index;
            }
        }

        return bracket;
    }

    // Fifty kills below level 80, thirty from 80 on.
    public static int HuntCount(int level) => Bracket(level) < 4 ? 50 : 30;

    public static bool TalkAtMaster(QuestContext context)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != Code || context.Character.Level < MinimumLevel)
        {
            return false;
        }

        switch (active.State)
        {
            case Offered when active.KillsNeeded > 0:
                // A lost first hunt: the master explains it again and starts it over.
                context.Show(QuestSteps.Proceed(Code, Offered, "ChangeJob4_1Start"));
                break;
            case FirstHunt when active.HuntComplete:
            case FirstHuntDone:
                journal.Active = active.StopHunt() with { State = FirstHuntDone };
                context.Show(QuestSteps.Proceed(Code, FirstHuntDone, "ChangeJob4_2Start"));
                break;
            case FirstHunt:
                context.Show(QuestSteps.Close(Code, FirstHunt, "ChangeJob4_1Path"));
                break;
            case SecondHunt when active.HuntComplete:
            case SecondHuntDone:
            case Dueling:
                journal.Active = active.StopHunt() with { State = active.State == Dueling ? Dueling : SecondHuntDone };
                context.Show(QuestSteps.Proceed(Code, journal.Active.State, "ChangeJob4_3Start"));
                break;
            case SecondHunt:
                context.Show(QuestSteps.Close(Code, SecondHunt, "ChangeJob4_2Path"));
                break;
            case AmuletHunt when QuestInventory.Has(context.Character, QuestItems.RoyalAmulet):
            case AmuletFound:
                journal.Active = active with { State = AmuletFound };
                context.Show(QuestSteps.Proceed(Code, AmuletFound, "ChangeJob4_4Start"));
                break;
            case AmuletHunt:
                context.Show(QuestSteps.Close(Code, AmuletHunt, "ChangeJob4_3Path"));
                break;
            case DuelWon:
                context.Show(RankUpRules.RankUpPrompt(context, Code, DuelWon, "ChangeJob4_4End"));
                break;
            default:
                return false;
        }

        return true;
    }

    public static bool Accept(QuestContext context, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (state == Offered)
        {
            if (!active.IsIdle || RankUpRules.Offer(context) != Code)
            {
                return QuestSteps.Stale(context);
            }

            journal.Active = StartHunt(context, new ActiveQuest { Code = Code, State = FirstHunt }, FirstPrey);
            return true;
        }

        // The rank-up is confirmed from the victory dialog in the arena or later at the master.
        if (state != DuelWon || active.Code != Code || active.State != DuelWon || context.Character.Tier != 2)
        {
            return QuestSteps.Stale(context);
        }

        QuestSteps.Finish(context, Code);
        RankUpRules.RankUp(context);
        context.Travel = new QuestTravel(QuestTravelKind.Home, QuestClasses.HomeField(context.Character), 0, 0);
        return true;
    }

    public static bool Proceed(QuestContext context, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (active.Code != Code || active.State != state)
        {
            return QuestSteps.Stale(context);
        }

        switch (state)
        {
            case Offered when active.KillsNeeded > 0:
                journal.Active = StartHunt(context, active with { State = FirstHunt }, FirstPrey);
                return true;
            case FirstHuntDone:
                journal.Active = StartHunt(context, active with { State = SecondHunt }, SecondPrey);
                return true;
            case SecondHuntDone:
            case Dueling:
                var holder = AmuletHolders[Bracket(context.Character.Level)];
                journal.Active = active.StopHunt() with { State = AmuletHunt, Target = holder };
                context.Notice(QuestNoticeKind.Event, QuestTexts.GetItemFrom(QuestMonsters.NameOf(holder)));
                return true;
            case AmuletFound:
                if (context.Character.Level < DuelMinimumLevel || QuestInventory.Remove(context.Character, QuestItems.RoyalAmulet, 1) == 0)
                {
                    return QuestSteps.Stale(context);
                }

                journal.Active = active with { State = Dueling, ArenaWon = false };
                context.Duel = new QuestArenaDuel(Code, QuestClasses.DuelMasterSound(context.Character), 0);
                context.Travel = new QuestTravel(QuestTravelKind.Arena, QuestArena.FieldCode, QuestArena.EntryX, QuestArena.EntryZ);
                return true;
        }

        return QuestSteps.Stale(context);
    }

    private static ActiveQuest StartHunt(QuestContext context, ActiveQuest quest, int[][] table)
    {
        var level = context.Character.Level;
        var pair = table[Bracket(level)];
        var prey = pair[context.Rng.NextInclusive(0, 1)];
        return QuestSteps.StartHunt(context, quest, prey, HuntCount(level), QuestTimerKind.Expiring, HuntTimeMs);
    }

    public static void OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        if (active.Code == Code && active.State <= SecondHuntDone)
        {
            QuestSteps.CreditHunt(context, monsterCode);
        }
    }

    public static void StepTimer(QuestContext context, long elapsedMs)
    {
        if (context.Journal.Active.Code == Code)
        {
            QuestSteps.StepHuntTimer(context, elapsedMs);
        }
    }

    // The royal amulet drops from the named monster of the step, one at a time.
    public static bool Accepts(QuestContext context, uint code, int killedMonsterCode)
    {
        var active = context.Journal.Active;
        return code == QuestItems.RoyalAmulet && active.Code == Code && active.State == AmuletHunt && killedMonsterCode == active.Target
            && !QuestInventory.Has(context.Character, QuestItems.RoyalAmulet);
    }

    public static bool OnDuelWon(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code != Code || active.State != Dueling)
        {
            return false;
        }

        context.Journal.Active = active with { State = DuelWon, ArenaWon = true };
        context.Show(RankUpRules.RankUpPrompt(context, Code, DuelWon, "ChangeJob4_4End"));
        return true;
    }

    public static bool OnDuelLost(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code != Code || active.State != Dueling)
        {
            return false;
        }

        context.Journal.Active = active with { State = SecondHuntDone };
        return true;
    }
}
