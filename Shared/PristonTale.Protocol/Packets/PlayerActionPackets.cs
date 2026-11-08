using System.Numerics;
using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

namespace PristonTale.Protocol.Packets;

// C->S {header, uint16 Version = 1, uint8 Motion (PresenceMotion stand, walk or run), uint8 0, int32 Field (-1 keeps the
// current field), int32 X, Y, Z (x256), uint16 Facing (65536 per turn), uint16 0, uint64 ActionTarget (0 none)}: the
// client's own position, sent every 200 ms while moving, on state changes and every second while idle. A field other
// than the current one asks to cross into it.
public readonly record struct MovementReportPacket(MotionState Motion, int Field, Vector3 Position, float Facing, ulong ActionTarget) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 + 12 + 4 + 8;
    private const int FacingSteps = 65536;

    public bool Moving => Motion is MotionState.Walk or MotionState.Run;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.MovementReport);
        writer.UInt16(CurrentVersion);
        writer.Byte((byte)Motion);
        writer.Byte(0);
        writer.Int32(Field);
        writer.Int32(GameUnits.ToFixed(Position.X));
        writer.Int32(GameUnits.ToFixed(Position.Y));
        writer.Int32(GameUnits.ToFixed(Position.Z));
        writer.UInt16((ushort)WireAngle.ToSteps(Facing, FacingSteps));
        writer.UInt16(0);
        writer.UInt64(ActionTarget);
        return writer.Finish();
    }

    public static MovementReportPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Movement report");

        var motion = (MotionState)reader.Byte();
        reader.Skip(1);
        if (motion > MotionState.Run)
        {
            throw new InvalidDataException("Movement report has an unknown motion.");
        }

        var field = reader.Int32();
        var position = new Vector3(GameUnits.FromFixed(reader.Int32()), GameUnits.FromFixed(reader.Int32()), GameUnits.FromFixed(reader.Int32()));
        var facing = WireAngle.FromSteps(reader.UInt16(), FacingSteps);
        reader.Skip(2);
        return new MovementReportPacket(motion, field, position, facing, reader.UInt64());
    }
}

// C->S {header, uint16 Version = 1, uint8 Flags (1 held attack), uint8 0, uint64 Target, uint64 Sequence}: the attack
// target the client clicked (0 stops attacking); an NPC target opens the NPC. The server resolves every swing itself.
public readonly record struct AttackRequestPacket(ulong Target, bool Sustained, ulong Sequence) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 8 + 8;
    private const byte SustainedFlag = 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.AttackRequest);
        writer.UInt16(CurrentVersion);
        writer.Byte(Sustained ? SustainedFlag : (byte)0);
        writer.Byte(0);
        writer.UInt64(Target);
        writer.UInt64(Sequence);
        return writer.Finish();
    }

    public static AttackRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Attack request");

        var flags = reader.Byte();
        reader.Skip(1);
        if (flags > SustainedFlag)
        {
            throw new InvalidDataException("Attack request has unknown flags.");
        }

        return new AttackRequestPacket(reader.UInt64(), flags == SustainedFlag, reader.UInt64());
    }
}

// S->C {header, uint16 Version = 1, uint8 Flags (1 forced: a correction, teleport or restart), uint8 0, int32 Field,
// int32 X, Y, Z (x256)}: the authoritative field and position of the owner; the client puts the owner on its floor nearest
// the height Y.
public readonly record struct FieldWarpPacket(int Field, float X, float Y, float Z, bool Forced)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 + 12;
    private const byte ForcedFlag = 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.FieldWarp);
        writer.UInt16(CurrentVersion);
        writer.Byte(Forced ? ForcedFlag : (byte)0);
        writer.Byte(0);
        writer.Int32(Field);
        writer.Int32(GameUnits.ToFixed(X));
        writer.Int32(GameUnits.ToFixed(Y));
        writer.Int32(GameUnits.ToFixed(Z));
        return writer.Finish();
    }

    public static FieldWarpPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Field warp");

        var flags = reader.Byte();
        reader.Skip(1);
        var field = reader.Int32();
        var x = GameUnits.FromFixed(reader.Int32());
        var y = GameUnits.FromFixed(reader.Int32());
        return new FieldWarpPacket(field, x, y, GameUnits.FromFixed(reader.Int32()), (flags & ForcedFlag) != 0);
    }
}

// C->S {header, uint16 Version = 1, uint16 0}: Space pressed while the yahoo window a chat word opened is still running.
// The server checks the window, the motion and the stamina itself; the cheer shows on the presence timeline.
public readonly record struct YahooKeyPacket : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.YahooKey);
        WireHeader.Write(ref writer, CurrentVersion);
        return writer.Finish();
    }

    public static void Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Yahoo key");
    }
}

// C->S {header, uint16 Version = 1, uint16 0}: Space with nothing ahead in an action field. The server swings at nothing
// when the player stands free there; the swing shows on the presence timeline.
public readonly record struct SwingRequestPacket : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SwingRequest);
        WireHeader.Write(ref writer, CurrentVersion);
        return writer.Finish();
    }

    public static void Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Swing request");
    }
}
