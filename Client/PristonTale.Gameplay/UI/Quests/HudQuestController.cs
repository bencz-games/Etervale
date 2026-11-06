using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.Texts;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Quests;

// The client side of the quests: one request in flight, the quest dialog the server opened, the yes/no box that follows
// it, the skill redistribution offer and the journal the server publishes. Notices wait in a queue for the HUD.
public sealed class HudQuestController(Func<QuestCommand, Task<QuestOutcome>> request, IClientLog log)
{
    private readonly Queue<QuestNotice> _notices = new();
    private readonly HudRequest<QuestOutcome> _request = new(log, "Quest request failed");
    private QuestAction _pendingAction;
    private ulong _pendingNpc;
    private bool _resetDeclined;

    public QuestView View { get; private set; }
    public QuestPrompt Dialog { get; private set; }

    // The NPC the dialog answers to; 0 for a dialog the world opened (a won duel).
    public ulong DialogNpc { get; private set; }

    // The reward a reward dialog shows (1-based).
    public int Browse { get; private set; }
    public bool Confirming { get; private set; }
    public bool SkillResetBox { get; private set; }

    // The skill master whose class-change button works for this player (0 for none).
    public ulong ClassChangeNpc { get; private set; }
    public bool Pending => _request.Pending;
    public int Completions { get; private set; }
    public string Message { get; private set; } = "";

    // Increases whenever a new dialog opens; the HUD closes what the dialog replaces.
    public int DialogsOpened { get; private set; }

    public void Update()
    {
        if (!_request.TryTake(out var outcome))
        {
            return;
        }

        Apply(outcome, _pendingAction, _pendingNpc);
    }

    // The quest part of a presence update: the journal, the quest notices and a dialog the world opened.
    public void Observe(PresenceUpdate update)
    {
        if (update.Quest is { } view)
        {
            View = view;
            if (!view.SkillResetOffered)
            {
                _resetDeclined = false;
                SkillResetBox = false;
            }
            else if (!_resetDeclined)
            {
                SkillResetBox = true;
            }
        }

        foreach (var notice in update.QuestNotices)
        {
            _notices.Enqueue(notice);
        }

        if (update.QuestPrompt is { } prompt)
        {
            Open(prompt, 0);
        }
    }

    public bool TryTakeNotice(out QuestNotice notice) => _notices.TryDequeue(out notice);

    // An NPC that takes part in quests was clicked.
    public void Talk(ulong npc)
    {
        if (!Pending)
        {
            ClassChangeNpc = 0;
            Send(new QuestCommand(Guid.NewGuid(), npc, QuestAction.Talk, QuestCode.None, 0, 0));
        }
    }

    public void ChangeClass(ulong npc)
    {
        if (!Pending && npc != 0 && npc == ClassChangeNpc)
        {
            Send(new QuestCommand(Guid.NewGuid(), npc, QuestAction.ChangeClass, QuestCode.None, 0, 0));
        }
    }

    // The dialog's OK button.
    public void Ok()
    {
        if (Dialog is not { } dialog || Pending)
        {
            return;
        }

        switch (dialog.Followup)
        {
            case QuestFollowup.Proceed:
                Send(new QuestCommand(Guid.NewGuid(), DialogNpc, QuestAction.Proceed, dialog.Code, dialog.State, 0));
                CloseDialog();
                break;
            case QuestFollowup.Confirm or QuestFollowup.ConfirmChoice:
                Confirming = true;
                break;
            default:
                CloseDialog();
                break;
        }
    }

    public void Choose(int index)
    {
        if (Dialog is { Followup: QuestFollowup.ChooseFromList } dialog && !Pending && index >= 0 && index < dialog.Choices.Count)
        {
            Send(new QuestCommand(Guid.NewGuid(), DialogNpc, QuestAction.ChooseReward, dialog.Code, dialog.State, index + 1));
        }
    }

    public void BrowseBy(int delta)
    {
        if (Dialog is { Followup: QuestFollowup.ConfirmChoice } dialog && dialog.Choices.Count > 0 && !Confirming)
        {
            var count = dialog.Choices.Count;
            Browse = ((Browse - 1 + delta) % count + count) % count + 1;
        }
    }

    // The document the dialog shows: the browsed reward's for a reward dialog.
    public string DialogDocument => Dialog is not { } dialog ? ""
        : dialog.Followup == QuestFollowup.ConfirmChoice && Browse >= 1 && Browse <= dialog.ChoiceDocuments.Count ? dialog.ChoiceDocuments[Browse - 1]
        : dialog.Document;

    public string ConfirmDetail => Dialog is not { } dialog ? ""
        : dialog.Followup == QuestFollowup.ConfirmChoice && Browse >= 1 && Browse <= dialog.Choices.Count ? dialog.Choices[Browse - 1]
        : ClientText.Format(dialog.ConfirmDetail);

    public void Answer(bool yes)
    {
        if (!Confirming || Dialog is not { } dialog)
        {
            return;
        }

        Confirming = false;
        if (yes && !Pending)
        {
            var choice = dialog.Followup == QuestFollowup.ConfirmChoice ? Browse : dialog.Choice;
            Send(new QuestCommand(Guid.NewGuid(), DialogNpc, QuestAction.Accept, dialog.Code, dialog.State, choice));
        }

        CloseDialog();
    }

    public void AnswerSkillReset(bool yes)
    {
        if (!SkillResetBox)
        {
            return;
        }

        SkillResetBox = false;
        _resetDeclined = true;
        if (!Pending)
        {
            Send(new QuestCommand(Guid.NewGuid(), 0, yes ? QuestAction.ResetSkills : QuestAction.DeclineSkillReset, QuestCode.None, 0, 0));
        }
    }

    public void CloseDialog()
    {
        Dialog = null;
        Confirming = false;
        Browse = 0;
    }

    // Leaving the NPC also leaves its class-change offer.
    public void LeaveNpc()
    {
        ClassChangeNpc = 0;
    }

    private void Send(QuestCommand command)
    {
        if (request is null)
        {
            return;
        }

        _pendingAction = command.Action;
        _pendingNpc = command.NpcEntityId;
        _request.Start(() => request(command),
            cancelled => QuestOutcome.Failed(QuestStatus.Rejected, cancelled ? GameText.None : GameText.Of(GameTextId.ConnectionError)));
    }

    private void Apply(QuestOutcome outcome, QuestAction action, ulong npc)
    {
        foreach (var notice in outcome.Notices)
        {
            _notices.Enqueue(notice);
        }

        if (action == QuestAction.Talk && outcome.ClassChangeOffered)
        {
            ClassChangeNpc = npc;
        }

        if (outcome.Prompt is { } prompt)
        {
            Open(prompt, npc);
        }

        // A talk that found nothing to say, or met a busy save, stays silent like a plain NPC click.
        var silent = action == QuestAction.Talk && outcome.Status is QuestStatus.Busy or QuestStatus.OutOfReach;
        if (!outcome.Ok && outcome.Prompt is null && !silent && !outcome.Message.IsNone)
        {
            Message = ClientText.Format(outcome.Message);
            Completions++;
        }
    }

    private void Open(QuestPrompt prompt, ulong npc)
    {
        Dialog = prompt;
        DialogNpc = npc;
        Confirming = false;
        Browse = prompt.Followup == QuestFollowup.ConfirmChoice ? Math.Clamp(prompt.Choice, 1, Math.Max(1, prompt.Choices.Count)) : 0;
        DialogsOpened++;
    }
}
