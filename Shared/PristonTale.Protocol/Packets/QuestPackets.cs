using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Texts;

namespace PristonTale.Protocol.Packets;

// Quest messages are Etervale extensions. Bodies start with {uint16 Version, uint16 Flags}; text is UTF-8 with a uint16
// byte length (NpcWire). Readers reject version 0 and accept newer versions that append fields.
public static class QuestWire
{
    public const int MaxTextBytes = 64;
    public const int MaxChoices = 16;
    public const int MaxNotices = 32;
    public const int MaxProgress = 16;
    public const int MaxFinished = 32;

    internal static void WritePrompt(ref PacketWriter writer, QuestPromptEntry prompt)
    {
        if (prompt.Choices.Count > MaxChoices || prompt.ChoiceDocuments.Count != prompt.Choices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(prompt), "Too many or unpaired quest choices.");
        }

        writer.UInt16(prompt.Code);
        writer.Byte(prompt.Followup);
        writer.Byte(0);
        writer.Int32(prompt.State);
        writer.Int32(prompt.Choice);
        NpcWire.Text(ref writer, prompt.Document, MaxTextBytes);
        GameTextWire.Write(ref writer, prompt.Title);
        GameTextWire.Write(ref writer, prompt.ConfirmLine1);
        GameTextWire.Write(ref writer, prompt.ConfirmLine2);
        GameTextWire.Write(ref writer, prompt.ConfirmDetail);
        GameTextWire.Write(ref writer, prompt.Subject);
        writer.Int32(prompt.Choices.Count);
        for (var index = 0; index < prompt.Choices.Count; index++)
        {
            NpcWire.Text(ref writer, prompt.Choices[index], MaxTextBytes);
            NpcWire.Text(ref writer, prompt.ChoiceDocuments[index], MaxTextBytes);
        }
    }

    internal static QuestPromptEntry ReadPrompt(ref PacketReader reader)
    {
        var code = reader.UInt16();
        var followup = reader.Byte();
        reader.Skip(1);
        var state = reader.Int32();
        var choice = reader.Int32();
        var document = NpcWire.Text(ref reader, MaxTextBytes);
        var title = GameTextWire.Read(ref reader);
        var first = GameTextWire.Read(ref reader);
        var second = GameTextWire.Read(ref reader);
        var detail = GameTextWire.Read(ref reader);
        var subject = GameTextWire.Read(ref reader);
        var count = reader.Int32();
        if (count < 0 || count > MaxChoices)
        {
            throw new InvalidDataException("Quest dialog has an invalid choice count.");
        }

        var choices = new string[count];
        var documents = new string[count];
        for (var index = 0; index < count; index++)
        {
            choices[index] = NpcWire.Text(ref reader, MaxTextBytes);
            documents[index] = NpcWire.Text(ref reader, MaxTextBytes);
        }

        return new QuestPromptEntry(code, state, followup, choice, document, title, first, second, detail, subject, choices, documents);
    }

    internal static int PromptSize(QuestPromptEntry prompt)
    {
        var size = 12 + NpcWire.TextSize(prompt.Document, MaxTextBytes) + GameTextWire.Size(prompt.Title)
            + GameTextWire.Size(prompt.ConfirmLine1) + GameTextWire.Size(prompt.ConfirmLine2)
            + GameTextWire.Size(prompt.ConfirmDetail) + GameTextWire.Size(prompt.Subject) + 4;
        for (var index = 0; index < prompt.Choices.Count; index++)
        {
            size += NpcWire.TextSize(prompt.Choices[index], MaxTextBytes) + NpcWire.TextSize(prompt.ChoiceDocuments[index], MaxTextBytes);
        }

        return size;
    }

    internal static void WriteNotices(ref PacketWriter writer, IReadOnlyList<QuestNoticeEntry> notices)
    {
        if (notices.Count > MaxNotices)
        {
            throw new ArgumentOutOfRangeException(nameof(notices), "Too many quest notices.");
        }

        writer.Int32(notices.Count);
        foreach (var notice in notices)
        {
            writer.Byte(notice.Kind);
            writer.Byte(0);
            GameTextWire.Write(ref writer, notice.Text);
        }
    }

    internal static QuestNoticeEntry[] ReadNotices(ref PacketReader reader)
    {
        var count = reader.Int32();
        if (count < 0 || count > MaxNotices)
        {
            throw new InvalidDataException("Quest message has an invalid notice count.");
        }

        var notices = new QuestNoticeEntry[count];
        for (var index = 0; index < count; index++)
        {
            var kind = reader.Byte();
            reader.Skip(1);
            notices[index] = new QuestNoticeEntry(kind, GameTextWire.Read(ref reader));
        }

        return notices;
    }

    internal static int NoticesSize(IReadOnlyList<QuestNoticeEntry> notices)
    {
        var size = 4;
        foreach (var notice in notices)
        {
            size += 2 + GameTextWire.Size(notice.Text);
        }

        return size;
    }
}

// A quest dialog: the document to show, what OK does (Followup), the confirmation box lines and the reward choices.
public sealed record QuestPromptEntry(
    ushort Code,
    int State,
    byte Followup,
    int Choice,
    string Document,
    GameText Title,
    GameText ConfirmLine1,
    GameText ConfirmLine2,
    GameText ConfirmDetail,
    GameText Subject,
    IReadOnlyList<string> Choices,
    IReadOnlyList<string> ChoiceDocuments);

public readonly record struct QuestNoticeEntry(byte Kind, GameText Text);

public readonly record struct QuestBoardLine(string Title, string Description);

// C->S, 48 bytes: {header, Guid OperationId, uint64 NpcId, uint8 Action, uint8 0, uint16 Code, int32 State, int32 Choice}.
public sealed record QuestRequestPacket(Guid OperationId, ulong NpcId, byte Action, ushort Code, int State, int Choice) : IFramePacket
{
    public const int Size = 48;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.QuestRequest);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Byte(Action);
        writer.Byte(0);
        writer.UInt16(Code);
        writer.Int32(State);
        writer.Int32(Choice);
        return writer.Finish();
    }

    public static QuestRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        NpcWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var npc = reader.UInt64();
        var action = reader.Byte();
        reader.Skip(1);
        var code = reader.UInt16();
        return new QuestRequestPacket(operation, npc, action, code, reader.Int32(), reader.Int32());
    }
}

// S->C answer to a request: {header, Guid OperationId, uint8 Status, uint8 Flags (1 ok, 2 class change offered, 4 skill
// redistribution offered, 8 dialog follows), uint16 0, int64 Revision, game text Message, [dialog], notices}.
public sealed record QuestResultPacket(Guid OperationId, byte Status, bool Ok, bool ClassChangeOffered, bool SkillResetOffered, long Revision,
    GameText Message, QuestPromptEntry? Prompt, IReadOnlyList<QuestNoticeEntry> Notices)
{
    private const int MinimumSize = 8 + 4 + 16 + 4 + 8 + GameTextWire.MinimumSize + 4;

    public int Capacity => MinimumSize - GameTextWire.MinimumSize + GameTextWire.Size(Message) + (Prompt is null ? 0 : QuestWire.PromptSize(Prompt))
        + QuestWire.NoticesSize(Notices);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.QuestResult);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Byte((byte)((Ok ? 1 : 0) | (ClassChangeOffered ? 2 : 0) | (SkillResetOffered ? 4 : 0) | (Prompt is null ? 0 : 8)));
        writer.UInt16(0);
        writer.Int64(Revision);
        GameTextWire.Write(ref writer, Message);
        if (Prompt is not null)
        {
            QuestWire.WritePrompt(ref writer, Prompt);
        }

        QuestWire.WriteNotices(ref writer, Notices);
        return writer.Finish();
    }

    public static QuestResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        NpcWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var status = reader.Byte();
        var flags = reader.Byte();
        reader.Skip(2);
        var revision = reader.Int64();
        var message = GameTextWire.Read(ref reader);
        var prompt = (flags & 8) != 0 ? QuestWire.ReadPrompt(ref reader) : null;
        var notices = QuestWire.ReadNotices(ref reader);
        return new QuestResultPacket(operation, status, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, revision, message, prompt, notices);
    }
}

// S->C whenever the journal changes: {header, int64 Revision, uint16 Code, uint8 Timer, uint8 SkillResetOffered,
// int32 State, text TargetName, int32 Kills, KillsNeeded, int64 TimerMs, int32 WeaponStage, WeaponKillsLeft,
// int32 ProgressCount, lines, int32 FinishedCount, lines}; a line is {text Title, text Description}.
public sealed record QuestJournalPacket(long Revision, ushort Code, byte Timer, bool SkillResetOffered, int State, string TargetName, int Kills,
    int KillsNeeded, long TimerMs, int WeaponStage, int WeaponKillsLeft, IReadOnlyList<QuestBoardLine> Progress, IReadOnlyList<QuestBoardLine> Finished)
{
    private const int MinimumSize = 8 + 4 + 8 + 4 + 4 + 2 + 4 + 4 + 8 + 4 + 4 + 4 + 4;

    public int Capacity
    {
        get
        {
            var size = MinimumSize + NpcWire.TextSize(TargetName, QuestWire.MaxTextBytes);
            foreach (var line in Progress.Concat(Finished))
            {
                size += NpcWire.TextSize(line.Title, QuestWire.MaxTextBytes) + NpcWire.TextSize(line.Description, QuestWire.MaxTextBytes);
            }

            return size;
        }
    }

    public int Write(Span<byte> destination)
    {
        if (Progress.Count > QuestWire.MaxProgress || Finished.Count > QuestWire.MaxFinished)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many quest window lines.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.QuestJournal);
        NpcWire.Header(ref writer);
        writer.Int64(Revision);
        writer.UInt16(Code);
        writer.Byte(Timer);
        writer.Byte(SkillResetOffered ? (byte)1 : (byte)0);
        writer.Int32(State);
        NpcWire.Text(ref writer, TargetName, QuestWire.MaxTextBytes);
        writer.Int32(Kills);
        writer.Int32(KillsNeeded);
        writer.Int64(TimerMs);
        writer.Int32(WeaponStage);
        writer.Int32(WeaponKillsLeft);
        WriteLines(ref writer, Progress);
        WriteLines(ref writer, Finished);
        return writer.Finish();
    }

    private static void WriteLines(ref PacketWriter writer, IReadOnlyList<QuestBoardLine> lines)
    {
        writer.Int32(lines.Count);
        foreach (var line in lines)
        {
            NpcWire.Text(ref writer, line.Title, QuestWire.MaxTextBytes);
            NpcWire.Text(ref writer, line.Description, QuestWire.MaxTextBytes);
        }
    }

    private static QuestBoardLine[] ReadLines(ref PacketReader reader, int max)
    {
        var count = reader.Int32();
        if (count < 0 || count > max)
        {
            throw new InvalidDataException("Quest window has an invalid line count.");
        }

        var lines = new QuestBoardLine[count];
        for (var index = 0; index < count; index++)
        {
            var title = NpcWire.Text(ref reader, QuestWire.MaxTextBytes);
            lines[index] = new QuestBoardLine(title, NpcWire.Text(ref reader, QuestWire.MaxTextBytes));
        }

        return lines;
    }

    public static QuestJournalPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        NpcWire.ReadHeader(ref reader);
        var revision = reader.Int64();
        var code = reader.UInt16();
        var timer = reader.Byte();
        var reset = reader.Byte() != 0;
        var state = reader.Int32();
        var target = NpcWire.Text(ref reader, QuestWire.MaxTextBytes);
        var kills = reader.Int32();
        var needed = reader.Int32();
        var timerMs = reader.Int64();
        var stage = reader.Int32();
        var left = reader.Int32();
        var progress = ReadLines(ref reader, QuestWire.MaxProgress);
        var finished = ReadLines(ref reader, QuestWire.MaxFinished);
        return new QuestJournalPacket(revision, code, timer, reset, state, target, kills, needed, timerMs, stage, left, progress, finished);
    }
}

// S->C quest notices of the world tick and a dialog it opened: {header, uint8 Flags (1 dialog follows), uint8 0,
// uint16 0, notices, [dialog]}.
public sealed record QuestNoticesPacket(IReadOnlyList<QuestNoticeEntry> Notices, QuestPromptEntry? Prompt)
{
    private const int MinimumSize = 8 + 4 + 4 + 4;

    public int Capacity => MinimumSize + QuestWire.NoticesSize(Notices) + (Prompt is null ? 0 : QuestWire.PromptSize(Prompt));

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.QuestNotices);
        NpcWire.Header(ref writer);
        writer.Byte(Prompt is null ? (byte)0 : (byte)1);
        writer.Byte(0);
        writer.UInt16(0);
        QuestWire.WriteNotices(ref writer, Notices);
        if (Prompt is not null)
        {
            QuestWire.WritePrompt(ref writer, Prompt);
        }

        return writer.Finish();
    }

    public static QuestNoticesPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        NpcWire.ReadHeader(ref reader);
        var flags = reader.Byte();
        reader.Skip(3);
        var notices = QuestWire.ReadNotices(ref reader);
        var prompt = (flags & 1) != 0 ? QuestWire.ReadPrompt(ref reader) : null;
        return new QuestNoticesPacket(notices, prompt);
    }
}
