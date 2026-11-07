using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Items;

namespace PristonTale.Server.Net;

// Quest messages between the application's quest types and the wire.
internal static class QuestWireMapping
{
    public static QuestCommand ToCommand(QuestRequestPacket request) =>
        new(request.OperationId, request.NpcId, (QuestAction)request.Action, (QuestCode)request.Code, request.State, request.Choice);

    public static QuestResultPacket ToResult(Guid operationId, QuestOutcome outcome, ItemCatalog items) =>
        new(operationId, (byte)outcome.Status, outcome.Ok, outcome.ClassChangeOffered, outcome.SkillResetOffered, outcome.Revision, outcome.Message,
            ToPrompt(outcome.Prompt, items), ToNotices(outcome.Notices));

    // Item choices take their names from the player's language's catalog.
    public static QuestPromptEntry? ToPrompt(QuestPrompt? prompt, ItemCatalog items) => prompt is null
        ? null
        : new QuestPromptEntry((ushort)prompt.Code, prompt.State, (byte)prompt.Followup, prompt.Choice, prompt.Document, prompt.Title,
            prompt.ConfirmLine1, prompt.ConfirmLine2, prompt.ConfirmDetail, prompt.Subject, Choices(prompt, items),
            prompt.ChoiceDocuments.Take(QuestWire.MaxChoices).ToArray());

    private static string[] Choices(QuestPrompt prompt, ItemCatalog items) => prompt.ChoiceItems.Count == 0
        ? prompt.Choices.Take(QuestWire.MaxChoices).ToArray()
        : prompt.ChoiceItems.Take(QuestWire.MaxChoices).Select(code => items.TryGet(code, out var item) ? item.Name : ItemCodes.KeyOf(code)).ToArray();

    public static QuestNoticeEntry[] ToNotices(IReadOnlyList<QuestNotice> notices) =>
        notices.Take(QuestWire.MaxNotices).Select(static notice => new QuestNoticeEntry((byte)notice.Kind, notice.Text)).ToArray();

    public static QuestJournalPacket ToJournal(QuestView view) =>
        new(view.Revision, (ushort)view.Code, (byte)view.Timer, view.SkillResetOffered, view.State, view.TargetName, view.Kills, view.KillsNeeded,
            view.TimerMs, view.WeaponStage, view.WeaponKillsLeft, Lines(view.Progress, QuestWire.MaxProgress), Lines(view.Finished, QuestWire.MaxFinished));

    private static QuestBoardLine[] Lines(IReadOnlyList<QuestBoardEntry> entries, int max) =>
        entries.Take(max).Select(static entry => new QuestBoardLine(entry.Title, entry.Description)).ToArray();

    // Sends a snapshot's quest update: the journal first, then the notices and any dialog the world opened.
    public static void Send(QuestUpdate? update, ItemCatalog items, Action<Func<Span<byte>, int>, int> send)
    {
        if (update is null || update.IsEmpty)
        {
            return;
        }

        if (update.View is { } view)
        {
            var journal = ToJournal(view);
            send(journal.Write, journal.Capacity);
        }

        for (var offset = 0; offset < update.Notices.Count || (offset == 0 && update.Prompt is not null); offset += QuestWire.MaxNotices)
        {
            var batch = update.Notices.Skip(offset).Take(QuestWire.MaxNotices).ToArray();
            var last = offset + QuestWire.MaxNotices >= update.Notices.Count;
            var notices = new QuestNoticesPacket(ToNotices(batch), last ? ToPrompt(update.Prompt, items) : null);
            send(notices.Write, notices.Capacity);
        }
    }
}
