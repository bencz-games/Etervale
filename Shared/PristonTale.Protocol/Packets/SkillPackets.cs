using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Skills;

namespace PristonTale.Protocol.Packets;

// Etervale skill messages. Every body but the event lists (EventWire) starts with {ushort Version, ushort Flags}; readers
// accept newer versions that append fields after the ones they know. Positions are play-data fixed point and angles are 4096 per turn.
// Flags (version 2) carry the SkillCastCommand: 0 a new cast, 1 the release of a held charge, 2 and 3 order the caster's
// summon to fight only its owner's target or to pick its own.
// {header, uint16 Version = 3, uint16 Flags, uint32 Sequence, SkillCode, uint64 TargetId, int32 X, Y, Z, int16 Angle,
// int16 0, uint32 ClientTimeMs}.
public readonly record struct SkillCastPacket(uint Sequence, uint SkillCode, ulong TargetId, int X, int Y, int Z, short Angle, uint ClientTimeMs,
    SkillCastCommand Command = SkillCastCommand.Cast) : IFramePacket
{
    public const ushort CurrentVersion = 3;
    public const int Size = 8 + 4 + 4 * 2 + 8 + 4 * 3 + 4 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillCast);
        writer.UInt16(CurrentVersion);
        writer.UInt16((ushort)Command);
        writer.UInt32(Sequence);
        writer.UInt32(SkillCode);
        writer.UInt64(TargetId);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int16(Angle);
        writer.Zero(2);
        writer.UInt32(ClientTimeMs);
        return writer.Finish();
    }

    public static SkillCastPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        var version = reader.UInt16();
        var command = reader.UInt16();
        if (version < CurrentVersion || command > (ushort)SkillCastCommand.SummonAggressive)
        {
            throw new InvalidDataException("Skill cast has an older layout or an unknown command.");
        }

        var sequence = reader.UInt32();
        var code = reader.UInt32();
        var target = reader.UInt64();
        var x = reader.Int32();
        var y = reader.Int32();
        var z = reader.Int32();
        var angle = reader.Int16();
        reader.Skip(2);
        return new SkillCastPacket(sequence, code, target, x, y, z, angle, reader.UInt32(), (SkillCastCommand)command);
    }
}

// Answer to the caster's own SkillCast, correlated by Sequence. CooldownMs is the full cooldown that started.
public readonly record struct SkillCastResultPacket(uint Sequence, uint SkillCode, SkillCastVerdict Status, byte Rank, uint CooldownMs, uint RemainingMs,
    int Mana, int Stamina, uint ServerTick)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + 4 + 4 * 2 + 4 + 4 * 5;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillCastResult);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt32(Sequence);
        writer.UInt32(SkillCode);
        writer.Byte((byte)Status);
        writer.Byte(Rank);
        writer.Zero(2);
        writer.UInt32(CooldownMs);
        writer.UInt32(RemainingMs);
        writer.Int32(Mana);
        writer.Int32(Stamina);
        writer.UInt32(ServerTick);
        return writer.Finish();
    }

    public static SkillCastResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Skill cast result");
        var sequence = reader.UInt32();
        var code = reader.UInt32();
        var status = reader.Byte();
        var rank = reader.Byte();
        reader.Skip(2);
        if (!Enum.IsDefined((SkillCastVerdict)status))
        {
            throw new InvalidDataException("Skill cast result has an unknown status.");
        }

        return new SkillCastResultPacket(sequence, code, (SkillCastVerdict)status, rank, reader.UInt32(), reader.UInt32(), reader.Int32(), reader.Int32(),
            reader.UInt32());
    }
}

// A skill's start, impact, cancel or release. Positions are fixed point (x256); the wire holds them at 1/8 of a unit.
public readonly record struct SkillEventEntry(
    ulong Caster,
    ulong Target,
    uint SkillCode,
    SkillEventPhase Phase,
    byte Rank,
    SkillEventFlags Flags,
    float DurationMs,
    float HitAtMs,
    int X,
    int Y,
    int Z,
    short Angle,
    ushort TargetCount,
    int Radius) : IEventEntry<SkillEventEntry>
{
    // {uint8 Phase, Fields, varuint Flags, uint32 SkillCode, uint8 Rank, entity Caster}, then what Fields names, in its bit
    // order (EventWire).
    public const int MinSize = 9;
    public const int MaxSize = 2 + VarInt.MaxBytes16 + 4 + 1 + EventWire.MaxEntityBytes * 2 + EventWire.MaxTimeBytes * 2 + EventWire.PointBytes
        + VarInt.MaxBytes16 * 2 + VarInt.MaxBytes32;

    static int IEventEntry<SkillEventEntry>.MinSize => MinSize;

    static int IEventEntry<SkillEventEntry>.MaxSize => MaxSize;

    [Flags]
    private enum Fields : byte
    {
        Target = 1,
        Timing = 2,
        Point = 4,
        Angle = 8,
        TargetCount = 16,
        Radius = 32,
        All = 63
    }

    public void Write(ref PacketWriter writer, IEventHandleWriter handles)
    {
        var fields = (Target != 0 ? Fields.Target : 0)
            | (DurationMs != 0f || HitAtMs != 0f ? Fields.Timing : 0)
            | (X != 0 || Y != 0 || Z != 0 ? Fields.Point : 0)
            | (Angle != 0 ? Fields.Angle : 0)
            | (TargetCount != 0 ? Fields.TargetCount : 0)
            | (Radius != 0 ? Fields.Radius : 0);
        writer.Byte((byte)Phase);
        writer.Byte((byte)fields);
        writer.VarUInt((ushort)Flags);
        writer.UInt32(SkillCode);
        writer.Byte(Rank);
        EventWire.WriteEntity(ref writer, Caster, handles);
        if ((fields & Fields.Target) != 0)
        {
            EventWire.WriteEntity(ref writer, Target, handles);
        }

        if ((fields & Fields.Timing) != 0)
        {
            EventWire.WriteTime(ref writer, DurationMs);
            EventWire.WriteTime(ref writer, HitAtMs);
        }

        if ((fields & Fields.Point) != 0)
        {
            EventWire.WritePoint(ref writer, X, Y, Z);
        }

        if ((fields & Fields.Angle) != 0)
        {
            writer.VarInt(Angle);
        }

        if ((fields & Fields.TargetCount) != 0)
        {
            writer.VarUInt(TargetCount);
        }

        if ((fields & Fields.Radius) != 0)
        {
            writer.VarInt(Radius);
        }
    }

    public static SkillEventEntry Read(ref PacketReader reader, IEventHandleReader handles)
    {
        var phase = reader.Byte();
        if (phase is < (byte)SkillEventPhase.Start or > (byte)SkillEventPhase.Release)
        {
            throw new InvalidDataException("Skill event has an unknown phase.");
        }

        var fields = (Fields)reader.Byte();
        var flags = reader.VarUInt32();
        if (flags > ushort.MaxValue || (fields & ~Fields.All) != 0)
        {
            throw new InvalidDataException("Skill event flags exceed 16 bits or name unknown fields.");
        }

        var code = reader.UInt32();
        var rank = reader.Byte();
        var caster = EventWire.ReadEntity(ref reader, handles);
        var target = (fields & Fields.Target) != 0 ? EventWire.ReadEntity(ref reader, handles) : 0;
        var (duration, hitAt) = (fields & Fields.Timing) != 0 ? (EventWire.ReadTime(ref reader), EventWire.ReadTime(ref reader)) : (0f, 0f);
        var (x, y, z) = (fields & Fields.Point) != 0 ? EventWire.ReadPoint(ref reader) : (0, 0, 0);
        var angle = (fields & Fields.Angle) != 0 ? reader.VarInt() : 0;
        var targets = (fields & Fields.TargetCount) != 0 ? reader.VarUInt32() : 0;
        if (angle is < short.MinValue or > short.MaxValue || targets > ushort.MaxValue)
        {
            throw new InvalidDataException("Skill event value is out of range.");
        }

        var radius = (fields & Fields.Radius) != 0 ? reader.VarInt() : 0;
        return new SkillEventEntry(caster, target, code, (SkillEventPhase)phase, rank, (SkillEventFlags)flags, duration, hitAt, x, y, z, (short)angle,
            (ushort)targets, radius);
    }
}

// Skill starts and impacts on the presence timeline of the last clock (EventWire, version 4).
public static class SkillEventList
{
    public const ushort CurrentVersion = 4;

    public static int Write(Span<byte> destination, ReadOnlySpan<SkillEventEntry> events, IEventHandleWriter handles, out int written) =>
        EventWire.Write(destination, EtervaleOpcodes.SkillEvents, CurrentVersion, events, handles, out written);

    public static SkillEventEntry[] Read(ReadOnlySpan<byte> packet, IEventHandleReader handles) =>
        EventWire.Read<SkillEventEntry>(packet, CurrentVersion, "Skill event list", handles);
}

public static class StatusEffectFlags
{
    public const ushort Harmful = 1;
    // No timer: the effect lasts while its source holds it (an equipped item, an aura).
    public const ushort Permanent = 2;
}

// One change of a modifier effect on an entity. The layout is source-agnostic: SourceKind tells items, player skills,
// monster skills, consumables and auras apart, and Control carries the control states (stun, root, silence, slow,
// hidden) the effect imposes. Value is the periodic amount for Tick entries and the main magnitude otherwise.
public readonly record struct StatusEffectEntry(
    ulong Entity,
    ulong Source,
    uint StatusCode,
    StatusEffectOp Op,
    byte Stacks,
    byte SourceKind,
    byte Control,
    ushort Flags,
    int DurationMs,
    int RemainingMs,
    int Value) : IEventEntry<StatusEffectEntry>
{
    // {uint8 Op, Fields, uint32 StatusCode, entity Entity}, then what Fields names, in its bit order (EventWire).
    public const int MinSize = 7;
    public const int MaxSize = 2 + 4 + EventWire.MaxEntityBytes * 2 + 3 + VarInt.MaxBytes16 + VarInt.MaxBytes32 * 3;

    static int IEventEntry<StatusEffectEntry>.MinSize => MinSize;

    static int IEventEntry<StatusEffectEntry>.MaxSize => MaxSize;

    [Flags]
    private enum Fields : byte
    {
        Source = 1,
        Stacks = 2,
        SourceKind = 4,
        Control = 8,
        Flags = 16,
        Duration = 32,
        Remaining = 64,
        Value = 128
    }

    public void Write(ref PacketWriter writer, IEventHandleWriter handles)
    {
        var fields = (Source != 0 ? Fields.Source : 0)
            | (Stacks != 0 ? Fields.Stacks : 0)
            | (SourceKind != 0 ? Fields.SourceKind : 0)
            | (Control != 0 ? Fields.Control : 0)
            | (Flags != 0 ? Fields.Flags : 0)
            | (DurationMs != 0 ? Fields.Duration : 0)
            | (RemainingMs != 0 ? Fields.Remaining : 0)
            | (Value != 0 ? Fields.Value : 0);
        writer.Byte((byte)Op);
        writer.Byte((byte)fields);
        writer.UInt32(StatusCode);
        EventWire.WriteEntity(ref writer, Entity, handles);
        if ((fields & Fields.Source) != 0)
        {
            EventWire.WriteEntity(ref writer, Source, handles);
        }

        if ((fields & Fields.Stacks) != 0)
        {
            writer.Byte(Stacks);
        }

        if ((fields & Fields.SourceKind) != 0)
        {
            writer.Byte(SourceKind);
        }

        if ((fields & Fields.Control) != 0)
        {
            writer.Byte(Control);
        }

        if ((fields & Fields.Flags) != 0)
        {
            writer.VarUInt(Flags);
        }

        if ((fields & Fields.Duration) != 0)
        {
            writer.VarUInt((uint)DurationMs);
        }

        if ((fields & Fields.Remaining) != 0)
        {
            writer.VarUInt((uint)RemainingMs);
        }

        if ((fields & Fields.Value) != 0)
        {
            writer.VarInt(Value);
        }
    }

    public static StatusEffectEntry Read(ref PacketReader reader, IEventHandleReader handles)
    {
        var op = reader.Byte();
        if (op is < (byte)StatusEffectOp.Add or > (byte)StatusEffectOp.Tick)
        {
            throw new InvalidDataException("Status effect has an unknown operation.");
        }

        var fields = (Fields)reader.Byte();
        var code = reader.UInt32();
        var entity = EventWire.ReadEntity(ref reader, handles);
        var source = (fields & Fields.Source) != 0 ? EventWire.ReadEntity(ref reader, handles) : 0;
        var stacks = (fields & Fields.Stacks) != 0 ? reader.Byte() : (byte)0;
        var sourceKind = (fields & Fields.SourceKind) != 0 ? reader.Byte() : (byte)0;
        var control = (fields & Fields.Control) != 0 ? reader.Byte() : (byte)0;
        var flags = (fields & Fields.Flags) != 0 ? reader.VarUInt32() : 0;
        var duration = (fields & Fields.Duration) != 0 ? reader.VarUInt31() : 0;
        var remaining = (fields & Fields.Remaining) != 0 ? reader.VarUInt31() : 0;
        var value = (fields & Fields.Value) != 0 ? reader.VarInt() : 0;
        if (flags > ushort.MaxValue || remaining > duration)
        {
            throw new InvalidDataException("Status effect has invalid flags or timers.");
        }

        return new StatusEffectEntry(entity, source, code, (StatusEffectOp)op, stacks, sourceKind, control, (ushort)flags, duration, remaining, value);
    }
}

// Status changes on the presence timeline of the last clock (EventWire, version 3).
public static class StatusEffectList
{
    public const ushort CurrentVersion = 3;

    public static int Write(Span<byte> destination, ReadOnlySpan<StatusEffectEntry> entries, IEventHandleWriter handles, out int written) =>
        EventWire.Write(destination, EtervaleOpcodes.StatusEffects, CurrentVersion, entries, handles, out written);

    public static StatusEffectEntry[] Read(ReadOnlySpan<byte> packet, IEventHandleReader handles) =>
        EventWire.Read<StatusEffectEntry>(packet, CurrentVersion, "Status effect list", handles);
}

public readonly record struct SkillCooldownEntry(uint SkillCode, uint RemainingMs, uint DurationMs) : IWireEntry<SkillCooldownEntry>
{
    public const int Size = 12;

    static int IWireEntry<SkillCooldownEntry>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.UInt32(SkillCode);
        writer.UInt32(RemainingMs);
        writer.UInt32(DurationMs);
    }

    public static SkillCooldownEntry Read(ref PacketReader reader)
    {
        var entry = new SkillCooldownEntry(reader.UInt32(), reader.UInt32(), reader.UInt32());
        return entry.RemainingMs <= entry.DurationMs ? entry : throw new InvalidDataException("Skill cooldown remains longer than its duration.");
    }
}

// The owner's running cooldowns at ServerTick: {size, code, Version, Flags, ServerTick, Count, entries}. A skill that is
// missing from the list is ready. Sent when the set changes, so a reconnecting or warping client resynchronizes.
public sealed record SkillCooldownsPacket(uint ServerTick, IReadOnlyList<SkillCooldownEntry> Cooldowns)
{
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 8 + 4 + 4 + 4;
    public const int MaxEntries = 256;

    public int Write(Span<byte> destination)
    {
        if (Cooldowns.Count > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.SkillCooldowns);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt32(ServerTick);
        WireList.Write(ref writer, Cooldowns);

        return writer.Finish();
    }

    public static SkillCooldownsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        WireHeader.Read(ref reader, "Skill cooldowns");
        var tick = reader.UInt32();
        return new SkillCooldownsPacket(tick, WireList.Read<SkillCooldownEntry>(ref reader, MaxEntries, "Skill cooldown list"));
    }
}

internal static class SkillWire
{}
