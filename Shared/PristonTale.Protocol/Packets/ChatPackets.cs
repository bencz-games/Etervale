using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Social;

namespace PristonTale.Protocol.Packets;

// Chat text is UTF-8; the composed lines stay within ChatRules' character limits, which fit these fields.
public static class ChatWire
{
    public const ushort CurrentVersion = 1;
    public const int MaxTextBytes = 1024;
    public const int HeaderSize = 8 + 4;
}

// C->S {header, uint16 Version = 1, uint8 Party (0 nearby or a command, 1 the party channel), uint8 0, text Text}: a line
// typed in the chat window.
public readonly record struct ChatSayPacket(bool Party, string Text) : IFramePacket
{
    public const int MaxSize = ChatWire.HeaderSize + 2 + ChatWire.MaxTextBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ChatSay);
        writer.UInt16(ChatWire.CurrentVersion);
        writer.Byte(Party ? (byte)1 : (byte)0);
        writer.Byte(0);
        Utf8Text.Write(ref writer, Text, ChatWire.MaxTextBytes);
        return writer.Finish();
    }

    public static ChatSayPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, ChatWire.HeaderSize + 2);
        WireHeader.ReadVersion(ref reader, "Chat line");
        var party = reader.Byte();
        reader.Skip(1);
        if (party > 1)
        {
            throw new InvalidDataException("Chat line names an unknown channel.");
        }

        return new ChatSayPacket(party == 1, Utf8Text.Read(ref reader, ChatWire.MaxTextBytes, "Chat line"));
    }
}

// S->C {header, uint16 Version = 1, uint8 Kind (ChatKind), uint8 0, uint64 Speaker (0 for server lines), text Text}:
// one chat line for this receiver.
public readonly record struct ChatLinePacket(ChatKind Kind, ulong Speaker, string Text)
{
    public const int MaxSize = ChatWire.HeaderSize + 8 + 2 + ChatWire.MaxTextBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ChatLine);
        writer.UInt16(ChatWire.CurrentVersion);
        writer.Byte((byte)Kind);
        writer.Byte(0);
        writer.UInt64(Speaker);
        Utf8Text.Write(ref writer, Text, ChatWire.MaxTextBytes);
        return writer.Finish();
    }

    public static ChatLinePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, ChatWire.HeaderSize + 8 + 2);
        WireHeader.ReadVersion(ref reader, "Chat line");
        var kind = (ChatKind)reader.Byte();
        reader.Skip(1);
        var speaker = reader.UInt64();
        return new ChatLinePacket(kind, speaker, Utf8Text.Read(ref reader, ChatWire.MaxTextBytes, "Chat line"));
    }
}

// S->C {header, uint16 Version = 1, uint16 0}: the end of a game master's shutdown countdown; the client quits.
public static class CloseRequestPacket
{
    public const int Size = 8 + 4;

    public static int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CloseRequest);
        WireHeader.Write(ref writer, ChatWire.CurrentVersion);
        return writer.Finish();
    }

    public static void Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Close request");
    }
}
