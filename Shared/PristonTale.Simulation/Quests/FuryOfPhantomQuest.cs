using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

// Fury of Phantom, from level 100: fifty monsters in the Heart of Perum, a duel with Fury, a hundred monsters in the
// Galluvia valley and two more duels against stronger forms of Fury; the reward is one of eleven weapons, any class's.
// Each won duel sends the player back to Pillai.
public static class FuryOfPhantomQuest
{
    public const QuestCode Code = QuestCode.FuryOfPhantom;
    public const int MinimumLevel = 100;
    public const int HeartOfPerumField = 28;
    public const int GalluviaValleyField = 31;
    private const int FirstHunt = 50;
    private const int SecondHunt = 100;

    private static readonly uint[] RewardWeapons =
    [
        ItemCodes.Parse("WA120"), ItemCodes.Parse("WC120"), ItemCodes.Parse("WH121"), ItemCodes.Parse("WP121"),
        ItemCodes.Parse("WS122"), ItemCodes.Parse("WS223"), ItemCodes.Parse("WT121"), ItemCodes.Parse("WM121"),
        ItemCodes.Parse("WD121"), ItemCodes.Parse("WN121"), ItemCodes.Parse("WV121")
    ];

    private static readonly string[] RewardDocuments =
    [
        "haQuestFuryItem1", "haQuestFuryItem2", "haQuestFuryItem3", "haQuestFuryItem4", "haQuestFuryItem5", "haQuestFuryItem6", "haQuestFuryItem7",
        "haQuestFuryItem8", "haQuestFuryItem9", "haQuestFuryItem10", "haQuestFuryItem11"
    ];

    // The duel of each battle state: the first in state 4, the second in 9 and the third in 11, each against a stronger Fury.
    public static int DuelLevel(int state) => state switch
    {
        4 => 118,
        9 => 119,
        11 => 120,
        _ => 0
    };

    public static void Talk(QuestContext context)
    {
        var journal = context.Journal;
        if ((journal.ElementaryLog & ElementaryQuestLog.FuryOfPhantom) != 0 || context.Character.Level < MinimumLevel)
        {
            return;
        }

        if (QuestSteps.Blocked(context, Code))
        {
            context.Block();
            return;
        }

        var active = journal.Active;
        context.Show(Prompt(active.Code == Code ? active : ActiveQuest.Idle));
    }

    public static QuestPrompt Prompt(ActiveQuest active)
    {
        var state = active.State;
        return state switch
        {
            0 => QuestSteps.Confirm(Code, 0, "100LVQuest_1Start", QuestTexts.GetQuest, QuestTexts.FuryTitle),
            1 => QuestSteps.Proceed(Code, 1, "100LVQuest_2Start"),
            2 => QuestSteps.Close(Code, 2, "100LVQuest_2Pass"),
            3 => Battle(active, "100LVQuest_3Start"),
            4 => Battle(active, "100LVQuest_3Pass"),
            5 => QuestSteps.Proceed(Code, 5, "100LVQuest_4Start"),
            6 or 7 => QuestSteps.Close(Code, state, "100LVQuest_4Pass"),
            8 => Battle(active, "100LVQuest_5Start"),
            9 => Battle(active, "100LVQuest_5Pass"),
            10 => Battle(active, "100LVQuest_6Start"),
            11 => Battle(active, "100LVQuest_6Pass"),
            12 => new QuestPrompt(Code, 12, "100LVQuest_7End", QuestFollowup.ChooseFromList)
            {
                Title = QuestTexts.FuryTitle,
                ChoiceItems = RewardWeapons,
                ChoiceDocuments = RewardDocuments
            },
            _ => Reward(active.Variant)
        };
    }

    private static QuestPrompt Battle(ActiveQuest active, string document) => active.ArenaWon
        ? QuestSteps.Proceed(Code, active.State, "100LVQuest_Victory")
        : QuestSteps.Confirm(Code, active.State, document, QuestTexts.BattleZone);

    private static QuestPrompt Reward(int choice) => new(Code, 13, RewardDocuments[Math.Clamp(choice, 1, RewardDocuments.Length) - 1], QuestFollowup.ConfirmChoice)
    {
        Title = QuestTexts.FuryTitle,
        ConfirmLine1 = QuestTexts.ReceiveItem.Item1,
        ConfirmLine2 = QuestTexts.ReceiveItem.Item2,
        ChoiceItems = RewardWeapons,
        ChoiceDocuments = RewardDocuments,
        Choice = Math.Clamp(choice, 1, RewardDocuments.Length)
    };

    public static bool Accept(QuestContext context, int state, int choice)
    {
        var journal = context.Journal;
        var active = journal.Active;
        if (!QuestSteps.Matches(context, Code, state))
        {
            return QuestSteps.Stale(context);
        }

        switch (state)
        {
            case 0:
                if ((journal.ElementaryLog & ElementaryQuestLog.FuryOfPhantom) != 0 || context.Character.Level < MinimumLevel)
                {
                    return QuestSteps.Stale(context);
                }

                journal.Active = QuestSteps.StartHunt(context, new ActiveQuest { Code = Code, State = 1 }, QuestMonsters.HeartOfPerum, FirstHunt,
                    QuestTimerKind.Display);
                context.Show(Prompt(journal.Active));
                return true;
            case 3 or 4 or 8 or 9 or 10 or 11 when !active.ArenaWon:
                var battle = state switch
                {
                    3 => 4,
                    8 => 9,
                    10 => 11,
                    _ => state
                };
                journal.Active = active with { State = battle };
                EnterArena(context, battle);
                return true;
            case 13:
                return Claim(context, active, choice);
        }

        return QuestSteps.Stale(context);
    }

    private static void EnterArena(QuestContext context, int battleState)
    {
        context.Duel = new QuestArenaDuel(Code, CharacterSoundCodes.Fury, DuelLevel(battleState));
        context.Travel = new QuestTravel(QuestTravelKind.Arena, QuestArena.FieldCode, QuestArena.EntryX, QuestArena.EntryZ);
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
            case 1:
                journal.Active = active with { State = 2 };
                return true;
            case 3 or 4 or 8 or 9 or 10 or 11 when active.ArenaWon:
                // Each won duel moves the quest on and sends the player back to Pillai.
                journal.Active = active with { State = state + 1, ArenaWon = false };
                context.Travel = new QuestTravel(QuestTravelKind.Home, FieldCodes.Pillai, 0, 0);
                return true;
            case 5:
                journal.Active = QuestSteps.StartHunt(context, active with { State = 7 }, QuestMonsters.GalluviaValley, SecondHunt, QuestTimerKind.Display);
                return true;
        }

        return QuestSteps.Stale(context);
    }

    public static bool Choose(QuestContext context, int state, int choice)
    {
        var active = context.Journal.Active;
        if (active.Code != Code || active.State != state || state is not (12 or 13) || choice < 1 || choice > RewardWeapons.Length)
        {
            return QuestSteps.Stale(context);
        }

        context.Journal.Active = active with { State = 13, Variant = choice };
        context.Show(Reward(choice));
        return true;
    }

    private static bool Claim(QuestContext context, ActiveQuest active, int choice)
    {
        if (choice < 1 || choice > RewardWeapons.Length)
        {
            return QuestSteps.Stale(context);
        }

        if (!QuestInventory.TryGrant(context, [new QuestGrant(RewardWeapons[choice - 1], ClassSpecific: true, QuestVariant: true)]))
        {
            context.Journal.Active = active with { State = 12, Variant = 0 };
            return QuestInventory.Refused(context);
        }

        context.Journal.ElementaryLog |= ElementaryQuestLog.FuryOfPhantom;
        context.Notice(QuestNoticeKind.Message, QuestTexts.RewardReceived);
        QuestSteps.Finish(context, Code);
        return true;
    }

    // The hunts count every monster killed in their field.
    public static void OnKill(QuestContext context)
    {
        var active = context.Journal.Active;
        var field = active.State switch
        {
            2 => HeartOfPerumField,
            7 => GalluviaValleyField,
            _ => 0
        };
        if (active.Code != Code || field == 0 || context.FieldCode != field || active.KillsNeeded == 0)
        {
            return;
        }

        var kills = active.Kills + 1;
        var name = QuestMonsters.NameOf(active.Target);
        context.Notice(QuestNoticeKind.Chat, QuestTexts.Count(name, kills));
        if (kills < active.KillsNeeded)
        {
            context.Journal.Active = active with { Kills = kills };
            return;
        }

        context.Notice(QuestNoticeKind.Event, QuestTexts.HuntFinished(active.KillsNeeded, name));
        context.Journal.Active = active with { Kills = kills, State = active.State + 1, Timer = QuestTimerKind.None };
    }

    // The arena reports the outcome of a duel; a win waits for the victory dialog to move on.
    public static bool OnDuelWon(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code != Code || DuelLevel(active.State) == 0)
        {
            return false;
        }

        context.Journal.Active = active with { ArenaWon = true };
        context.Notice(QuestNoticeKind.Message, QuestTexts.FuryWon);
        context.Show(Prompt(context.Journal.Active));
        return true;
    }

    public static void OnDuelLost(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code == Code && DuelLevel(active.State) != 0 && !active.ArenaWon)
        {
            context.Notice(QuestNoticeKind.Message, QuestTexts.FuryLost);
        }
    }
}
