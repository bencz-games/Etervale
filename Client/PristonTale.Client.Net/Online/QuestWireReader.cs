using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Client.Net.Online;

// Quest messages from the wire into the shared quest types the HUD reads.
internal static class QuestWireReader
{
    public static QuestRequestPacket ToPacket(QuestCommand command) =>
        new(command.OperationId, command.NpcEntityId, (byte)command.Action, (ushort)command.Code, command.State, command.Choice);

    public static QuestOutcome ToOutcome(QuestResultPacket result) =>
        new(result.Ok, (QuestStatus)result.Status, result.Message, result.Revision, ToPrompt(result.Prompt), ToNotices(result.Notices))
        {
            ClassChangeOffered = result.ClassChangeOffered,
            SkillResetOffered = result.SkillResetOffered
        };

    public static QuestPrompt? ToPrompt(QuestPromptEntry? entry) => entry is null
        ? null
        : new QuestPrompt((QuestCode)entry.Code, entry.State, entry.Document, (QuestFollowup)entry.Followup)
        {
            Title = entry.Title,
            ConfirmLine1 = entry.ConfirmLine1,
            ConfirmLine2 = entry.ConfirmLine2,
            ConfirmDetail = entry.ConfirmDetail,
            Subject = entry.Subject,
            Choices = entry.Choices,
            ChoiceDocuments = entry.ChoiceDocuments,
            Choice = entry.Choice
        };

    public static QuestNotice[] ToNotices(IReadOnlyList<QuestNoticeEntry> entries) =>
        entries.Select(static entry => new QuestNotice((QuestNoticeKind)entry.Kind, entry.Text)).ToArray();

    public static QuestView ToView(QuestJournalPacket packet) =>
        new(packet.Revision, (QuestCode)packet.Code, packet.State, packet.TargetName, packet.Kills, packet.KillsNeeded, (QuestTimerKind)packet.Timer,
            packet.TimerMs, Entries(packet.Progress), Entries(packet.Finished))
        {
            SkillResetOffered = packet.SkillResetOffered,
            WeaponStage = packet.WeaponStage,
            WeaponKillsLeft = packet.WeaponKillsLeft
        };

    private static QuestBoardEntry[] Entries(IReadOnlyList<QuestBoardLine> lines) =>
        lines.Select(static line => new QuestBoardEntry(line.Title, line.Description)).ToArray();
}
