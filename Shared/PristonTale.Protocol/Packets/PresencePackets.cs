using System.Buffers.Binary;
using System.Numerics;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Little-endian base-128 integers: 7 bits per byte, low group first, high bit set on every byte but the last.
public static class VarInt
{
    public const int MaxBytes16 = 3;
    public const int MaxBytes32 = 5;
    public const int MaxBytes64 = 10;

    public static int Size(ulong value)
    {
        var size = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            size++;
        }

        return size;
    }

    public static int Write(Span<byte> destination, ulong value)
    {
        var index = 0;
        while (value >= 0x80)
        {
            destination[index++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[index++] = (byte)value;
        return index;
    }

    public static ulong Read(ReadOnlySpan<byte> source, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= source.Length)
            {
                throw new InvalidDataException("Variable-length integer runs past the packet.");
            }

            var part = source[offset++];
            // The tenth byte holds only bit 63, and a last byte of zero after the first would be an overlong encoding.
            if (shift == 63 && part > 1 || part == 0 && shift > 0)
            {
                throw new InvalidDataException("Variable-length integer is out of range or not minimal.");
            }

            value |= (ulong)(part & 0x7F) << shift;
            if ((part & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("Variable-length integer is longer than 64 bits.");
    }

    public static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    // A zigzag value wider than 32 bits is refused, as every 32-bit varint is.
    public static int UnZigZag(ulong value) =>
        value <= uint.MaxValue ? (int)(uint)(value >> 1) ^ -(int)(uint)(value & 1) : throw new InvalidDataException("Variable-length value exceeds 32 bits.");
}

// A position as the presence wire carries it: world units times PositionScale, rounded and held to what 24 bits carry.
public readonly record struct PresencePoint(int X, int Y, int Z)
{
    public const float PositionScale = 8f;
    public const int Limit = (1 << 23) - 1;

    public static PresencePoint From(Vector3 position) => new(Scale(position.X), Scale(position.Y), Scale(position.Z));

    private static int Scale(float value) => (int)Math.Clamp(MathF.Round(value * PositionScale), -Limit, Limit);

    public Vector3 ToWorld() => new Vector3(X, Y, Z) / PositionScale;

    public static PresencePoint operator -(PresencePoint left, PresencePoint right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static PresencePoint operator +(PresencePoint left, PresencePoint right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
}

// The values an entry may carry beyond position, facing and motion; a spawn carries them all. The level is the
// description's: a level change changes the revision.
[Flags]
public enum PresenceFields : byte
{
    None = 0,
    Life = 1,
    MaxLife = 2,
    Target = 4,
    AttackSequence = 8,
    Revision = 16,
    Template = 32,
    FieldCode = 64,
    All = 127
}

// One entity's entry in a presence message, as the wire carries it. Handle is the receiver's own number for the entity,
// given by its spawn (which names the entity id) and free again once the entity is removed. Position is absolute
// (Absolute) or the change from the last position this receiver was sent for the handle.
public readonly record struct PresenceEntry(
    uint Handle,
    MotionState Motion,
    PresencePoint Position,
    byte Facing,
    bool Absolute = true,
    ulong EntityId = 0,
    PresenceFields Fields = PresenceFields.None,
    int Life = 0,
    int MaxLife = 0,
    ulong Target = 0,
    ulong AttackSequence = 0,
    byte Revision = 0,
    uint Template = 0,
    int FieldCode = -1)
{
    public bool Spawn => EntityId != 0;

    public float FacingRadians => WireAngle.FromSteps(Facing, PresenceWire.FacingSteps);

    public static byte ToFacing(float radians) => (byte)WireAngle.ToSteps(radians, PresenceWire.FacingSteps);
}

// The presence of nearby entities (S->C). {header, uint16 Version = 2, uint16 TickIntervalMs, uint32 Tick, uint16 Count,
// uint16 0}: the message also sets the presence clock to Tick. Then Count entries: {varuint Handle, uint8 Head (bits 0-3
// Motion, 4 Spawn, 5 Absolute, 6 Fields)}, when Spawn {varuint EntityId}, the position (Absolute: int24 X, Y, Z;
// otherwise varint (zigzag) dX, dY, dZ, each within 32 bits), uint8 Facing (256 per turn), and when Fields {uint8
// PresenceFields, then each field it names in its bit order: varuint Life, MaxLife, Target, AttackSequence, uint8
// Revision, varuint Template, varint (zigzag) FieldCode}. A spawn is absolute and carries every field. Positions span
// the world's +-1,000,000 units at 1/8 of a unit.
public static class PresenceWire
{
    public const int Int24Bytes = 3;
    public const ushort Version = 2;
    public const int HeaderSize = 8 + 4 + 4 + 4;
    public const int FacingSteps = 256;
    // A monster's and an NPC's template codes are numbered apart; an NPC's key carries this bit.
    public const uint NpcTemplate = 0x8000_0000;
    private const int MotionMask = 0x0F;
    private const byte SpawnBit = 0x10;
    private const byte AbsoluteBit = 0x20;
    private const byte FieldsBit = 0x40;
    private const byte ReservedBit = 0x80;
    private const int HeadBytes = 1;
    private const int FacingBytes = 1;
    private const int AbsoluteBytes = 3 * 3;
    // Three one-byte changes: a position that stood still.
    private const int MinChangeBytes = 3;
    private const int MaxChangeBytes = 3 * VarInt.MaxBytes32;
    private const int MaxFieldBytes = 1 + VarInt.MaxBytes32 * 2 + VarInt.MaxBytes64 * 2 + 1 + VarInt.MaxBytes32 * 2;

    // The smallest and largest entries, for budgets and buffer sizing.
    public const int MinEntrySize = 1 + HeadBytes + MinChangeBytes + FacingBytes;
    public const int MaxMotionEntrySize = VarInt.MaxBytes32 + HeadBytes + MaxChangeBytes + FacingBytes;
    public const int MaxEntrySize = MaxMotionEntrySize + VarInt.MaxBytes64 + MaxFieldBytes;

    // The key under which clients share a monster's or NPC's description by template.
    public static uint TemplateKey(EntityKind kind, uint code) => kind == EntityKind.Npc ? code | NpcTemplate : code;

    // Whether a change of position is cheaper sent whole.
    public static bool PrefersAbsolute(PresencePoint change) => ChangeSize(change) >= AbsoluteBytes;

    // The size of an entry with no fields.
    public static int MotionSize(uint handle, PresencePoint position, bool absolute) =>
        VarInt.Size(handle) + HeadBytes + (absolute ? AbsoluteBytes : ChangeSize(position)) + FacingBytes;

    public static int Write(Span<byte> destination, in PresenceEntry entry)
    {
        var offset = VarInt.Write(destination, entry.Handle);
        destination[offset++] = (byte)((byte)entry.Motion | (entry.Spawn ? SpawnBit : 0) | (entry.Absolute ? AbsoluteBit : 0)
            | (entry.Fields != PresenceFields.None ? FieldsBit : 0));
        if (entry.Spawn)
        {
            offset += VarInt.Write(destination[offset..], entry.EntityId);
        }

        if (entry.Absolute)
        {
            WriteInt24(destination, ref offset, entry.Position.X);
            WriteInt24(destination, ref offset, entry.Position.Y);
            WriteInt24(destination, ref offset, entry.Position.Z);
        }
        else
        {
            offset += VarInt.Write(destination[offset..], VarInt.ZigZag(entry.Position.X));
            offset += VarInt.Write(destination[offset..], VarInt.ZigZag(entry.Position.Y));
            offset += VarInt.Write(destination[offset..], VarInt.ZigZag(entry.Position.Z));
        }

        destination[offset++] = entry.Facing;
        if (entry.Fields == PresenceFields.None)
        {
            return offset;
        }

        var fields = entry.Fields;
        destination[offset++] = (byte)fields;
        offset += Optional(destination[offset..], fields, PresenceFields.Life, (ulong)Math.Max(0, entry.Life));
        offset += Optional(destination[offset..], fields, PresenceFields.MaxLife, (ulong)Math.Max(0, entry.MaxLife));
        offset += Optional(destination[offset..], fields, PresenceFields.Target, entry.Target);
        offset += Optional(destination[offset..], fields, PresenceFields.AttackSequence, entry.AttackSequence);
        if ((fields & PresenceFields.Revision) != 0)
        {
            destination[offset++] = entry.Revision;
        }

        offset += Optional(destination[offset..], fields, PresenceFields.Template, entry.Template);
        offset += Optional(destination[offset..], fields, PresenceFields.FieldCode, VarInt.ZigZag(entry.FieldCode));
        return offset;
    }

    public static PresenceEntry Read(ReadOnlySpan<byte> source, ref int offset)
    {
        var handle = VarInt.Read(source, ref offset) is var code && code <= uint.MaxValue ? (uint)code : throw new InvalidDataException("Presence handle exceeds 32 bits.");
        if (offset >= source.Length)
        {
            throw new InvalidDataException("Presence entry ends before its head.");
        }

        var head = source[offset++];
        var motion = (MotionState)(head & MotionMask);
        if (motion > MotionState.Yahoo || (head & ReservedBit) != 0)
        {
            throw new InvalidDataException("Presence entry has an unknown motion or flag.");
        }

        var spawn = (head & SpawnBit) != 0;
        var absolute = (head & AbsoluteBit) != 0;
        var entityId = spawn ? VarInt.Read(source, ref offset) : 0;
        if (spawn && (entityId == 0 || !absolute || (head & FieldsBit) == 0))
        {
            throw new InvalidDataException("Presence spawn names no entity, or lacks its position or fields.");
        }

        PresencePoint position;
        if (absolute)
        {
            if (source.Length - offset < 9)
            {
                throw new InvalidDataException("Presence entry ends inside its position.");
            }

            position = new PresencePoint(ReadInt24(source, ref offset), ReadInt24(source, ref offset), ReadInt24(source, ref offset));
        }
        else
        {
            position = new PresencePoint(ReadChange(source, ref offset), ReadChange(source, ref offset), ReadChange(source, ref offset));
        }

        if (offset >= source.Length)
        {
            throw new InvalidDataException("Presence entry ends before its facing.");
        }

        var facing = source[offset++];
        if ((head & FieldsBit) == 0)
        {
            return new PresenceEntry(handle, motion, position, facing, absolute, entityId);
        }

        if (offset >= source.Length)
        {
            throw new InvalidDataException("Presence entry ends before its fields.");
        }

        var fields = (PresenceFields)source[offset++];
        if ((fields & ~PresenceFields.All) != 0 || spawn && fields != PresenceFields.All)
        {
            throw new InvalidDataException("Presence entry has unknown fields, or a spawn lacks some.");
        }

        var life = (fields & PresenceFields.Life) != 0 ? Narrow(VarInt.Read(source, ref offset)) : 0;
        var maxLife = (fields & PresenceFields.MaxLife) != 0 ? Narrow(VarInt.Read(source, ref offset)) : 0;
        var target = (fields & PresenceFields.Target) != 0 ? VarInt.Read(source, ref offset) : 0;
        var sequence = (fields & PresenceFields.AttackSequence) != 0 ? VarInt.Read(source, ref offset) : 0;
        byte revision = 0;
        if ((fields & PresenceFields.Revision) != 0)
        {
            revision = offset < source.Length ? source[offset++] : throw new InvalidDataException("Presence entry ends before its revision.");
        }

        var template = (fields & PresenceFields.Template) == 0 ? 0u
            : VarInt.Read(source, ref offset) is var value && value <= uint.MaxValue ? (uint)value : throw new InvalidDataException("Presence template exceeds 32 bits.");
        var field = (fields & PresenceFields.FieldCode) != 0 ? VarInt.UnZigZag(VarInt.Read(source, ref offset)) : -1;
        return new PresenceEntry(handle, motion, position, facing, absolute, entityId, fields, life, maxLife, target, sequence, revision, template, field);
    }

    private static int ReadChange(ReadOnlySpan<byte> source, ref int offset) =>
        VarInt.UnZigZag(VarInt.Read(source, ref offset));

    private static int ChangeSize(PresencePoint change) =>
        VarInt.Size(VarInt.ZigZag(change.X)) + VarInt.Size(VarInt.ZigZag(change.Y)) + VarInt.Size(VarInt.ZigZag(change.Z));

    private static int Optional(Span<byte> destination, PresenceFields fields, PresenceFields field, ulong value) =>
        (fields & field) != 0 ? VarInt.Write(destination, value) : 0;

    // The point is already within PresencePoint.Limit.
    internal static void WriteInt24(Span<byte> destination, ref int offset, int value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        offset += Int24Bytes;
    }

    internal static int ReadInt24(ReadOnlySpan<byte> source, ref int offset)
    {
        var value = source[offset] | source[offset + 1] << 8 | (sbyte)source[offset + 2] << 16;
        offset += Int24Bytes;
        return value;
    }

    private static int Narrow(ulong value) => value > int.MaxValue ? throw new InvalidDataException("Presence value exceeds 31 bits.") : (int)value;
}

// Builds one presence message from entries; Finish writes the header with the tick it carries.
public ref struct PresenceEntriesWriter
{
    private readonly Span<byte> _buffer;
    private readonly uint _tick;
    private readonly ushort _tickIntervalMs;
    private int _length;
    private int _count;

    public PresenceEntriesWriter(Span<byte> buffer, uint tick, ushort tickIntervalMs)
    {
        _buffer = buffer;
        _tick = tick;
        _tickIntervalMs = tickIntervalMs;
        _length = PresenceWire.HeaderSize;
        _count = 0;
    }

    public readonly int Count => _count;

    public readonly int Free => _buffer.Length - _length;

    public Span<byte> Reserve(int size) => _buffer.Slice(_length, size);

    public void Commit(int size)
    {
        if (_count == ushort.MaxValue)
        {
            throw new InvalidOperationException("A presence message holds at most 65535 entries.");
        }

        _length += size;
        _count++;
    }

    public ReadOnlySpan<byte> Finish()
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer, _length);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[4..], EtervaleOpcodes.PresenceEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[8..], PresenceWire.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[10..], _tickIntervalMs);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[12..], _tick);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[16..], (ushort)_count);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[18..], 0);
        return _buffer[.._length];
    }

    public void Reset()
    {
        _length = PresenceWire.HeaderSize;
        _count = 0;
    }
}

public static class PresenceEntriesReader
{
    public delegate void EntryHandler(in PresenceEntry entry);

    // The clock the message sets.
    public static PresenceClockPacket Clock(ReadOnlySpan<byte> packet)
    {
        var reader = Header(packet);
        var interval = reader.UInt16();
        return interval != 0 ? new PresenceClockPacket(reader.UInt32(), interval) : throw new InvalidDataException("Presence message has no tick interval.");
    }

    // How many entries the message holds.
    public static int Count(ReadOnlySpan<byte> packet)
    {
        var reader = Header(packet);
        reader.Skip(2 + 4);
        return reader.UInt16();
    }

    // Hands every entry to the handler; the entries must fill the packet exactly.
    public static void Read(ReadOnlySpan<byte> packet, EntryHandler handler)
    {
        var count = Count(packet);
        var offset = PresenceWire.HeaderSize;
        for (var index = 0; index < count; index++)
        {
            handler(PresenceWire.Read(packet, ref offset));
        }

        if (offset != packet.Length)
        {
            throw new InvalidDataException("Presence message has bytes after its entries.");
        }
    }

    private static PacketReader Header(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, PresenceWire.HeaderSize);
        return reader.UInt16() == PresenceWire.Version ? reader : throw new InvalidDataException("Presence message has another version.");
    }
}
