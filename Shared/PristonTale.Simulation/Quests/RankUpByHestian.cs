using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Quests;

// Fourth rank-up, into the fifth tier. The home-town master sets three hunts of forty against the clock, each of one of
// three monsters drawn when it starts, and gives a Moon Stone for each one reported. Elder Hosean at the Railway of Chaos
// takes a player holding the three stones to the arena, where Hestian must be killed within seven minutes; he then turns
// the stones into the Gem of Hestian, which the master takes for the rank-up. A hunt that runs out of time starts over;
// a fight lost (death, time) sends the player home to try again from Hosean.
public static class RankUpByHestian
{
    public const QuestCode Code = QuestCode.RankUpHestian;

    // The masters whose class-change group reaches 4 offer it.
    public const int MasterGroup = 4;

    // Elder Hosean's quest class.
    public const int ElderGiverCode = 2100;

    public const int HuntKills = 40;
    public const int MoonStones = 3;
    public const int HestianLevel = 100;

    // 6300 seconds of play time per hunt and 420 for the fight; both run in every field and through deaths.
    public const long HuntTimeMs = 6_300_000;
    public const long FightTimeMs = 420_000;

    private const int Offered = 0;
    private const int FirstHunt = 1;
    private const int FirstHuntDone = 2;
    private const int SecondHunt = 3;
    private const int SecondHuntDone = 4;
    private const int ThirdHunt = 5;
    private const int ThirdHuntDone = 6;
    private const int SeekElder = 7;
    private const int Fighting = 8;
    private const int Won = 9;
    private const int GemHeld = 10;

    private static readonly int[][] Prey =
    [
        [QuestMonsters.IronGuard, QuestMonsters.ChainGolem, QuestMonsters.Spidertrooper],
        [QuestMonsters.DeadZone, QuestMonsters.HyperMachine, QuestMonsters.GoblinShaman],
        [QuestMonsters.Grotesque, QuestMonsters.DarkSpecter, QuestMonsters.Ratoo]
    ];

    public static int MinimumLevel => RankUpRules.RequiredLevel(PlayerCharacter.UncappedTier);

    public static QuestPrompt StartDialog(int state) => QuestSteps.Confirm(Code, state, "ChangeJob5_1Start", QuestTexts.ProcessQuest);

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
                // A first hunt that ran out: the master asks again.
                context.Show(StartDialog(Offered));
                break;
            case FirstHunt or SecondHunt or ThirdHunt when active.HuntComplete:
                // The reported hunt earns its Moon Stone; a full pack keeps the hunt reported until there is room.
                if (!QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.MoonStone)]))
                {
                    context.Notice(QuestNoticeKind.Message, QuestTexts.NotEnoughSpace);
                    break;
                }

                journal.Active = active.StopHunt() with { State = active.State + 1 };
                context.Show(QuestSteps.Proceed(Code, active.State + 1, DoneDialog(active.State + 1)));
                break;
            case FirstHuntDone or SecondHuntDone or ThirdHuntDone:
                context.Show(QuestSteps.Proceed(Code, active.State, DoneDialog(active.State)));
                break;
            case FirstHunt:
                context.Show(QuestSteps.Close(Code, FirstHunt, "ChangeJob5_1Pass"));
                break;
            case SecondHunt:
                context.Show(QuestSteps.Close(Code, SecondHunt, "ChangeJob5_2Pass"));
                break;
            case ThirdHunt:
                context.Show(QuestSteps.Close(Code, ThirdHunt, "ChangeJob5_3Pass"));
                break;
            case GemHeld when QuestInventory.Count(context.Character, QuestItems.GemOfHestian) == 1:
                context.Show(QuestSteps.Confirm(Code, GemHeld, "ChangeJob5_5End", QuestTexts.RankUpFifth,
                    ClassProfiles.Of(QuestClasses.ClassOf(context.Character)).TitleAt(context.Character.Tier + 1)));
                break;
            default:
                return false;
        }

        return true;
    }

    private static string DoneDialog(int state) => state switch
    {
        FirstHuntDone => "ChangeJob5_2Start",
        SecondHuntDone => "ChangeJob5_3Start",
        _ => "ChangeJob5_3End"
    };

    // Elder Hosean answers from the third stone on: the arena while the fight is to be won, the gem after it.
    public static void TalkAtElder(QuestContext context)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (context.Npc.GiverCode != ElderGiverCode || active.Code != Code || active.State is < SeekElder or > Won
            || QuestInventory.Count(context.Character, QuestItems.MoonStone) != MoonStones)
        {
            return;
        }

        if (active.State == Won)
        {
            context.Show(QuestSteps.Proceed(Code, Won, "ChangeJob5_5Start"));
            return;
        }

        // A fight abandoned (the player left the arena or the game) starts over from the elder.
        journal.Active = active.StopHunt() with { State = SeekElder };
        context.Show(QuestSteps.Proceed(Code, SeekElder, "ChangeJob5_4Start"));
    }

    public static bool Accept(QuestContext context, int state)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (state == Offered)
        {
            var restart = active.Code == Code && active.State == Offered && active.KillsNeeded > 0;
            if (!restart && (!active.IsIdle || RankUpRules.Offer(context) != Code))
            {
                return QuestSteps.Stale(context);
            }

            journal.Active = StartHunt(context, restart ? active with { State = FirstHunt } : new ActiveQuest { Code = Code, State = FirstHunt });
            return true;
        }

        var character = context.Character;
        if (state != GemHeld || active.Code != Code || active.State != GemHeld || character.Tier != PlayerCharacter.UncappedTier
            || character.Level < MinimumLevel || QuestInventory.Remove(character, QuestItems.GemOfHestian, 1) != 1)
        {
            return QuestSteps.Stale(context);
        }

        QuestSteps.Finish(context, Code);
        RankUpRules.RankUp(context);
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
            case FirstHuntDone or SecondHuntDone:
                journal.Active = StartHunt(context, active with { State = state + 1 });
                return true;
            case ThirdHuntDone:
                journal.Active = active.StopHunt() with { State = SeekElder };
                return true;
            case SeekElder when context.Npc.GiverCode == ElderGiverCode && QuestInventory.Count(context.Character, QuestItems.MoonStone) == MoonStones:
                journal.Active = QuestSteps.StartHunt(context, active with { State = Fighting }, QuestMonsters.Hestian, 1, QuestTimerKind.Expiring,
                    FightTimeMs);
                context.Duel = new QuestArenaDuel(Code, CharacterSoundCodes.Hestian, HestianLevel);
                context.Travel = new QuestTravel(QuestTravelKind.Arena, QuestArena.FieldCode, QuestArena.EntryX, QuestArena.EntryZ);
                context.Show(QuestSteps.Close(Code, Fighting, "ChangeJob5_4Cheer"));
                return true;
            case Won when context.Npc.GiverCode == ElderGiverCode:
                // The stones make room for the gem before it is placed.
                if (QuestInventory.Remove(context.Character, QuestItems.MoonStone, MoonStones) != MoonStones)
                {
                    return QuestSteps.Stale(context);
                }

                if (!QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.GemOfHestian)]))
                {
                    return QuestInventory.Refused(context);
                }

                journal.Active = active with { State = GemHeld };
                return true;
        }

        return QuestSteps.Stale(context);
    }

    private static ActiveQuest StartHunt(QuestContext context, ActiveQuest quest)
    {
        var choices = Prey[(quest.State - FirstHunt) / 2];
        var prey = choices[context.Rng.NextInclusive(0, choices.Length - 1)];
        return QuestSteps.StartHunt(context, quest, prey, HuntKills, QuestTimerKind.Expiring, HuntTimeMs);
    }

    public static void OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        if (active.Code == Code && active.State is FirstHunt or SecondHunt or ThirdHunt)
        {
            QuestSteps.CreditHunt(context, monsterCode);
        }
    }

    // A hunt that runs out goes back one step with its kills cleared, to be set again by the master; a fight that runs out
    // is lost.
    public static void StepTimer(QuestContext context, long elapsedMs)
    {
        var active = context.Journal.Active;
        if (active.Code != Code)
        {
            return;
        }

        if (active.State != Fighting)
        {
            QuestSteps.StepHuntTimer(context, elapsedMs);
            return;
        }

        if (active.Timer != QuestTimerKind.Expiring || elapsedMs <= 0)
        {
            return;
        }

        var left = active.TimerMs - elapsedMs;
        if (left > 0)
        {
            context.Journal.Active = active with { TimerMs = left };
            return;
        }

        LoseFight(context);
        context.Notice(QuestNoticeKind.Message, QuestTexts.MissionTimedOut);
        context.Retreat = true;
    }

    public static bool OnDuelWon(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code != Code || active.State != Fighting)
        {
            return false;
        }

        context.Journal.Active = active.StopHunt() with { State = Won };
        context.Notice(QuestNoticeKind.Message, QuestTexts.HestianWon);
        context.Retreat = true;
        return true;
    }

    public static void OnDuelLost(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code == Code && active.State == Fighting)
        {
            LoseFight(context);
        }
    }

    private static void LoseFight(QuestContext context)
    {
        context.Journal.Active = context.Journal.Active.StopHunt() with { State = SeekElder };
        context.Notice(QuestNoticeKind.Message, QuestTexts.HestianLost);
    }
}
