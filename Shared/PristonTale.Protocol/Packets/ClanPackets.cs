using PristonTale.Contracts.Characters;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// What a player asks of the clan service. Found and Entrust name the Clan Master (Id) they are asked at; Invite names the
// player (Id); Expel, Appoint, Discharge and Entrust name a member (Name); Found names the new clan. Mark is the chief's
// upload (ClanMarkPacket), answered as the others.
public enum ClanAction : byte
{
    Open = 0,
    Found = 1,
    Invite = 2,
    Accept = 3,
    Refuse = 4,
    Leave = 5,
    Expel = 6,
    Disband = 7,
    Appoint = 8,
    Discharge = 9,
    Entrust = 10,
    Mark = 11
}

public readonly record struct ClanMemberWire(string Name, PlayerClass Class, int Level, byte Rank, bool Online);

// Clan messages. Each body starts with {uint16 Version = 1, uint16 Flags = 0}; names are UTF-8 with a uint16 byte length.
public static class ClanWire
{
    public const ushort Version = 1;
    public const int MaxNameBytes = 64;

    // A clan name: 16 characters of at most three UTF-8 bytes (ClanRules).
    public const int MaxClanNameBytes = 48;

    // A clan mark: MarkSize x MarkSize pixels of 8-bit red, green, blue and alpha, rows from the top.
    public const int MarkSize = 32;
    public const int PixelBytes = 4;
    public const int MarkBytes = MarkSize * MarkSize * PixelBytes;
    public const int MaxIntroBytes = 256;
    public const int MaxMembers = 80;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Clan message");

    internal static string Name(ref PacketReader reader) => Utf8Text.Read(ref reader, MaxNameBytes, "Clan message");

    internal static string ClanName(ref PacketReader reader) => Utf8Text.Read(ref reader, MaxClanNameBytes, "Clan message");
}

// C->S {header, uint8 Action, varuint Id, text Name}.
public sealed record ClanRequestPacket(ClanAction Action, ulong Id, string Name) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 1 + 1 + 2;
    public const int MaxSize = MinimumSize + VarInt.MaxBytes64 + ClanWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanRequest);
        ClanWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.VarUInt(Id);
        Utf8Text.Write(ref writer, Name, ClanWire.MaxNameBytes);
        return writer.Finish();
    }

    public static ClanRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ClanWire.ReadHeader(ref reader);
        var action = (ClanAction)reader.Byte();
        if (!Enum.IsDefined(action))
        {
            throw new InvalidDataException("Clan request has an unknown action.");
        }

        return new ClanRequestPacket(action, reader.VarUInt(), ClanWire.Name(ref reader));
    }
}

// S->C the player's clan, or none: {header, uint8 HasClan; with a clan varuint ClanId, text Name, uint8 State, int64
// Deadline (Unix seconds, 0 none), uint8 Rank, uint8 Capacity, text Intro, varuint MarkVersion, uint16 Count, then per
// member {text Name, uint8 Class, varuint Level, uint8 Rank, uint8 Online}}.
public sealed record ClanStatePacket(ulong ClanId, string Name, byte State, long Deadline, byte Rank, int Capacity, string Intro, int MarkVersion,
    IReadOnlyList<ClanMemberWire> Members) : IFramePacket
{
    private const int MemberSize = 2 + ClanWire.MaxNameBytes + 1 + VarInt.MaxBytes64 + 2;
    public const int MaxSize = 8 + WireHeader.Size + 1 + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 1 + 8 + 2 + 2 + ClanWire.MaxIntroBytes
        + VarInt.MaxBytes64 + 2 + ClanWire.MaxMembers * MemberSize;

    public static readonly ClanStatePacket None = new(0, "", 0, 0, 0, 0, "", 0, []);

    public bool HasClan => ClanId != 0;

    public int Write(Span<byte> destination)
    {
        if (Members.Count > ClanWire.MaxMembers)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many clan members.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanState);
        ClanWire.Header(ref writer);
        writer.Byte(HasClan ? (byte)1 : (byte)0);
        if (HasClan)
        {
            writer.VarUInt(ClanId);
            Utf8Text.Write(ref writer, Name, ClanWire.MaxClanNameBytes);
            writer.Byte(State);
            writer.Int64(Deadline);
            writer.Byte(Rank);
            writer.Byte((byte)Math.Clamp(Capacity, 0, byte.MaxValue));
            Utf8Text.Write(ref writer, Intro, ClanWire.MaxIntroBytes);
            writer.VarUInt((ulong)Math.Max(0, MarkVersion));
            writer.UInt16((ushort)Members.Count);
            foreach (var member in Members)
            {
                Utf8Text.Write(ref writer, member.Name, ClanWire.MaxNameBytes);
                writer.Byte((byte)member.Class);
                writer.VarUInt((ulong)Math.Max(0, member.Level));
                writer.Byte(member.Rank);
                writer.Byte(member.Online ? (byte)1 : (byte)0);
            }
        }

        return writer.Finish();
    }

    public static ClanStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        ClanWire.ReadHeader(ref reader);
        if (reader.Byte() == 0)
        {
            return None;
        }

        var clanId = reader.VarUInt();
        var name = ClanWire.ClanName(ref reader);
        var state = reader.Byte();
        var deadline = reader.Int64();
        var rank = reader.Byte();
        var capacity = reader.Byte();
        var intro = Utf8Text.Read(ref reader, ClanWire.MaxIntroBytes, "Clan message");
        var mark = reader.VarUInt31();
        var count = reader.UInt16();
        if (count > ClanWire.MaxMembers)
        {
            throw new InvalidDataException("Clan state has too many members.");
        }

        var members = new ClanMemberWire[count];
        for (var index = 0; index < count; index++)
        {
            members[index] = new ClanMemberWire(ClanWire.Name(ref reader), (PlayerClass)reader.Byte(), reader.VarUInt31(), reader.Byte(), reader.Byte() != 0);
        }

        return new ClanStatePacket(clanId, name, state, deadline, rank, capacity, intro, mark, members);
    }
}

// S->C a clan's invitation: {header, varuint RecruiterId, text ClanName, text ChiefName}.
public sealed record ClanInvitePacket(ulong RecruiterId, string ClanName, string ChiefName) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 2 + ClanWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanInvite);
        ClanWire.Header(ref writer);
        writer.VarUInt(RecruiterId);
        Utf8Text.Write(ref writer, ClanName, ClanWire.MaxClanNameBytes);
        Utf8Text.Write(ref writer, ChiefName, ClanWire.MaxNameBytes);
        return writer.Finish();
    }

    public static ClanInvitePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1 + 4);
        ClanWire.ReadHeader(ref reader);
        return new ClanInvitePacket(reader.VarUInt(), ClanWire.ClanName(ref reader), ClanWire.Name(ref reader));
    }
}

// S->C how a request ended: {header, uint8 Action, uint8 Status (ClanStatus)}; a refusal's line comes in the chat.
public sealed record ClanResultPacket(ClanAction Action, byte Status) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanResult);
        ClanWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.Byte(Status);
        return writer.Finish();
    }

    public static ClanResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        ClanWire.ReadHeader(ref reader);
        return new ClanResultPacket((ClanAction)reader.Byte(), reader.Byte());
    }
}

// C->S the chief's new clan mark: {header, uint16 Size (ClanWire.MarkSize), Size x Size RGBA pixels}. The client fits the
// picture to the size before it sends it.
public sealed record ClanMarkPacket(byte[] Pixels) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 2 + ClanWire.MarkBytes;

    public int Write(Span<byte> destination)
    {
        if (Pixels.Length != ClanWire.MarkBytes)
        {
            throw new ArgumentException("A clan mark is ClanWire.MarkSize square RGBA pixels.", nameof(Pixels));
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ClanMark);
        ClanWire.Header(ref writer);
        writer.UInt16(ClanWire.MarkSize);
        writer.Bytes(Pixels);
        return writer.Finish();
    }

    public static ClanMarkPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        ClanWire.ReadHeader(ref reader);
        if (reader.UInt16() != ClanWire.MarkSize || reader.Remaining != ClanWire.MarkBytes)
        {
            throw new InvalidDataException("Clan mark has the wrong size.");
        }

        return new ClanMarkPacket(reader.Bytes(ClanWire.MarkBytes).ToArray());
    }
}
