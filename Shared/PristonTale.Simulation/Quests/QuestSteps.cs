using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Quests;

// Shared step helpers: dialogs, the one-quest-at-a-time rule and completion bookkeeping.
public static class QuestSteps
{
    public static QuestPrompt Close(QuestCode code, int state, string document) => new(code, state, document, QuestFollowup.Close)
    {
        Title = QuestTexts.QuestTitle
    };

    public static QuestPrompt Proceed(QuestCode code, int state, string document) => new(code, state, document, QuestFollowup.Proceed)
    {
        Title = QuestTexts.QuestTitle
    };

    public static QuestPrompt Confirm(QuestCode code, int state, string document, (GameText First, GameText Second) lines, GameText detail = default) =>
        new(code, state, document, QuestFollowup.Confirm)
        {
            Title = QuestTexts.QuestTitle,
            ConfirmLine1 = lines.First,
            ConfirmLine2 = lines.Second,
            ConfirmDetail = detail
        };

    // Another quest is running: the step cannot start or move on.
    public static bool Blocked(QuestContext context, QuestCode code)
    {
        var active = context.Journal.Active;
        return !active.IsIdle && active.Code != code;
    }

    // The dialog a follow-up answers must still describe the journal: the running quest at that state, or an offer
    // (state 0) while no quest runs.
    public static bool Matches(QuestContext context, QuestCode code, int state)
    {
        var active = context.Journal.Active;
        return active.Code == code ? active.State == state : active.IsIdle && state == 0;
    }

    public static bool Stale(QuestContext context) => context.Fail(QuestStatus.Stale, GameText.Of(GameTextId.QuestMovedOn));

    // Ends the running quest: it joins the finished history and the journal goes idle.
    public static void Finish(QuestContext context, QuestCode code)
    {
        context.Journal.Active = ActiveQuest.Idle;
        context.Journal.Finish(code);
        context.Notice(QuestNoticeKind.Fanfare, GameText.None);
    }

    // A kill of the running hunt's prey: counted with its chat line, and the hunt announced finished at its count.
    public static void CreditHunt(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        if (active.Target == 0 || monsterCode != active.Target || active.KillsNeeded == 0 || active.HuntComplete)
        {
            return;
        }

        var kills = active.Kills + 1;
        var name = QuestMonsters.NameOf(active.Target);
        context.Notice(QuestNoticeKind.Chat, QuestTexts.Count(name, kills));
        if (kills >= active.KillsNeeded)
        {
            context.Notice(QuestNoticeKind.Event, QuestTexts.HuntFinished(active.KillsNeeded, name));
        }

        context.Journal.Active = active with { Kills = kills };
    }

    // The hunt timer counts play time until the master hears of the hunt: when it runs out first, the hunt step is lost,
    // its kills made or not. It goes back one step with the kill count cleared; the needed count is kept so the master
    // restarts the hunt.
    public static void StepHuntTimer(QuestContext context, long elapsedMs)
    {
        var active = context.Journal.Active;
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

        context.Journal.Active = active with
        {
            State = active.KillsNeeded > 0 ? active.State - 1 : active.State,
            Kills = 0,
            Target = 0,
            Timer = QuestTimerKind.None,
            TimerMs = 0
        };
        context.Notice(QuestNoticeKind.Message, QuestTexts.MissionTimedOut);
    }

    // A hunt the quest timer icon follows; the fanfare plays as the icon appears.
    public static ActiveQuest StartHunt(QuestContext context, ActiveQuest quest, int target, int count, QuestTimerKind timer, long timerMs = 0)
    {
        context.Notice(QuestNoticeKind.Fanfare, GameText.None);
        context.Notice(QuestNoticeKind.Event, QuestTexts.Hunt(count, QuestMonsters.NameOf(target)));
        return quest.Hunting(target, count) with { Timer = timer, TimerMs = timerMs };
    }
}
