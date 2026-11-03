using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Units;

namespace PristonTale.Protocol.Packets;

// World entry: EnterWorld picks a character and WorldEntry answers with its id, field and spawn point; JoinWorld then
// starts the presence stream and WorldJoined confirms it. The owner's state arrives on the presence stream.
public static class WorldEntryWire
{
    public const ushort CurrentVersion = 1;
    public const int MaxNameBytes = AccountWire.MaxNameBytes;
}

// C->S {header, uint16 Version = 1, uint16 0, text Name (64 B)}.
public readonly record struct EnterWorldPacket(string Name) : IFramePacket
{
    public const int MaxSize = 8 + 4 + 2 + WorldEntryWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.EnterWorld);
        WireHeader.Write(ref writer, WorldEntryWire.CurrentVersion);
        Utf8Text.Write(ref writer, Name, WorldEntryWire.MaxNameBytes);
        return writer.Finish();
    }

    public static EnterWorldPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 2);
        WireHeader.ReadVersion(ref reader, "World entry request");
        reader.Skip(2);
        return new EnterWorldPacket(Utf8Text.Read(ref reader, WorldEntryWire.MaxNameBytes, "World entry request"));
    }
}

// S->C {header, uint16 Version = 1, uint8 Status (0 entered, else the entry failure), uint8 0, uint64 EntityId, int32 Field,
// int32 X, Z (x256)}.
public readonly record struct WorldEntryPacket(byte Status, ulong EntityId, int Field, float X, float Z)
{
    public const int Size = 8 + 4 + 8 + 4 + 8;

    public bool Entered => Status == 0;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.WorldEntry);
        writer.UInt16(WorldEntryWire.CurrentVersion);
        writer.Byte(Status);
        writer.Byte(0);
        writer.UInt64(EntityId);
        writer.Int32(Field);
        writer.Int32(GameUnits.ToFixed(X));
        writer.Int32(GameUnits.ToFixed(Z));
        return writer.Finish();
    }

    public static WorldEntryPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "World entry");
        var status = reader.Byte();
        reader.Skip(1);
        var entity = reader.UInt64();
        var field = reader.Int32();
        return new WorldEntryPacket(status, entity, field, GameUnits.FromFixed(reader.Int32()), GameUnits.FromFixed(reader.Int32()));
    }
}

// C->S {header, uint16 Version = 1, uint16 0, uint64 EntityId}: EntityId must be the one WorldEntry handed out.
public readonly record struct JoinWorldPacket(ulong EntityId) : IFramePacket
{
    public const int Size = 8 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.JoinWorld);
        WireHeader.Write(ref writer, WorldEntryWire.CurrentVersion);
        writer.UInt64(EntityId);
        return writer.Finish();
    }

    public static JoinWorldPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "World join");
        reader.Skip(2);
        return new JoinWorldPacket(reader.UInt64());
    }
}

// S->C {header, uint16 Version = 1, uint8 GameMasterLevel, uint8 0, uint64 EntityId, int64 ServerTimeMs, int64 GameClockMs}:
// the player's game master level enables its client-side game master controls (the server checks every command and move
// itself); GameClockMs is the server's game clock, which the client's sky follows from then on.
public readonly record struct WorldJoinedPacket(ulong EntityId, long ServerTimeMs, byte GameMasterLevel = 0, long GameClockMs = 0)
{
    public const int Size = 8 + 4 + 8 + 8 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.WorldJoined);
        writer.UInt16(WorldEntryWire.CurrentVersion);
        writer.Byte(GameMasterLevel);
        writer.Byte(0);
        writer.UInt64(EntityId);
        writer.Int64(ServerTimeMs);
        writer.Int64(GameClockMs);
        return writer.Finish();
    }

    public static WorldJoinedPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "World joined");
        var level = reader.Byte();
        reader.Skip(1);
        var entity = reader.UInt64();
        var serverTime = reader.Int64();
        return new WorldJoinedPacket(entity, serverTime, level, reader.Int64());
    }
}

// Both directions {header, uint16 Version = 1, uint8 Saved, uint8 0}: the client asks to leave (Saved 0); the server
// answers with Saved 1 once the character is stored.
public readonly record struct LeaveWorldPacket(bool Saved) : IFramePacket
{
    public const int Size = 8 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.LeaveWorld);
        writer.UInt16(WorldEntryWire.CurrentVersion);
        writer.Byte(Saved ? (byte)1 : (byte)0);
        writer.Byte(0);
        return writer.Finish();
    }

    public static LeaveWorldPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Leave world");
        return new LeaveWorldPacket(reader.Byte() != 0);
    }
}
