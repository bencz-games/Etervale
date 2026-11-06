using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Quests;

// Persisted with the applied-operation history and carried on the wire: append values, never renumber.
public enum QuestAction : byte
{
    // The player clicked the NPC: the quests that NPC takes part in answer with a dialog, and may move on.
    Talk = 1,

    // The player closed a quest dialog with its OK button.
    Proceed = 2,

    // The player answered yes in the confirmation box that followed a dialog.
    Accept = 3,

    // The class-change button of a skill master.
    ChangeClass = 4,

    // A reward picked from a list (Choice is 1-based).
    ChooseReward = 5,

    // The free skill redistribution a rank-up offers.
    ResetSkills = 6,

    // The redistribution offer answered No: the offer ends.
    DeclineSkillReset = 7
}

public enum QuestStatus : byte
{
    Ok = 0,
    Rejected = 1,
    Stale = 2,
    NoSpace = 3,
    InProgress = 4,
    OutOfReach = 5,
    Busy = 6,
    StorageFailed = 7,
    Conflict = 8,
    NotAvailable = 9,
    NotEnoughGold = 10,
    OverWeight = 11
}

// What the dialog's OK button does.
public enum QuestFollowup : byte
{
    // Closes the dialog.
    Close = 0,

    // Sends Proceed with the dialog's code and state.
    Proceed = 1,

    // Opens the yes/no box with the confirmation lines; yes sends Accept.
    Confirm = 2,

    // The dialog lists choices; picking one sends ChooseReward. OK closes it.
    ChooseFromList = 3,

    // The dialog shows the chosen reward; arrows browse the choices and OK asks to confirm it (Accept with the choice).
    ConfirmChoice = 4
}

public enum QuestNoticeKind : byte
{
    // A quest line in the chat window.
    Chat = 1,

    // The centred event message.
    Event = 2,

    // A one-line message box.
    Message = 3,

    // Plays the quest fanfare without text.
    Fanfare = 4
}

public sealed record QuestCommand(Guid OperationId, ulong NpcEntityId, QuestAction Action, QuestCode Code, int State, int Choice);

public sealed record QuestNotice(QuestNoticeKind Kind, GameText Text);

// A quest dialog: Document names the dialog text in the client's quest folder; Code and State identify the step the
// follow-up answers.
public sealed record QuestPrompt(QuestCode Code, int State, string Document, QuestFollowup Followup)
{
    public GameText Title { get; init; }
    public GameText ConfirmLine1 { get; init; }
    public GameText ConfirmLine2 { get; init; }

    // Shown under the confirmation lines: a job title, a destination or an item name.
    public GameText ConfirmDetail { get; init; }

    // What the document's "$$%s" names: the monster a hunt draws.
    public GameText Subject { get; init; }
    public IReadOnlyList<string> Choices { get; init; } = [];
    public IReadOnlyList<string> ChoiceDocuments { get; init; } = [];
    public int Choice { get; init; }
}

public enum QuestTravelKind : byte
{
    // Into the duel arena.
    Arena = 1,

    // Back to the home town after a duel or a rank-up.
    Home = 2
}

public sealed record QuestTravel(QuestTravelKind Kind, int FieldCode, int X, int Z);

// The duel a quest opens in the arena: the opponent is picked by its sound family and, for Fury, its level.
public sealed record QuestArenaDuel(QuestCode Quest, int SoundCode, int Level);

public sealed record QuestOutcome(bool Ok, QuestStatus Status, GameText Message, long Revision, QuestPrompt Prompt, IReadOnlyList<QuestNotice> Notices)
{
    public bool ClassChangeOffered { get; init; }
    public bool SkillResetOffered { get; init; }
    public QuestTravel Travel { get; init; }
    public QuestArenaDuel Duel { get; init; }

    public static QuestOutcome Failed(QuestStatus status, GameText message, long revision = 0) => new(false, status, message, revision, null, []);
}
