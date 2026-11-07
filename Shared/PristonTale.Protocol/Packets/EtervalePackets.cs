using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Texts;

namespace PristonTale.Protocol.Packets;

// Fixed little-endian structs for Etervale extension opcodes. They follow the original {size, code} header and the
// 8192-byte frame limit; lists that can exceed one frame are split into parts.
public sealed record OwnerStatusPacket(
    int Level,
    ulong Experience,
    ulong LevelStart,
    ulong NextLevel,
    long Money,
    int Life,
    int MaxLife,
    int Mana,
    int MaxMana,
    int Stamina,
    int MaxStamina,
    int StatPoints,
    int SkillPoints,
    int Strength,
    int Spirit,
    int Talent,
    int Agility,
    int Health,
    int DamageMin,
    int DamageMax,
    int Defence,
    int Absorption,
    int AttackRating,
    int MoveSpeed,
    float AttackRange,
    long InventoryRevision,
    long ProgressionRevision,
    long SkillsRevision,
    int EliteSkillPoints = 0,
    int Tier = 0,
    byte BagPage = 0,
    byte WeaponSet = 0)
{
    public const int Size = 8 + 4 + 8 * 4 + 4 * 19 + 4 + 8 * 3 + 4 * 2 + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.OwnerStatus);
        writer.Int32(Level);
        writer.UInt64(Experience);
        writer.UInt64(LevelStart);
        writer.UInt64(NextLevel);
        writer.Int64(Money);
        writer.Int32(Life);
        writer.Int32(MaxLife);
        writer.Int32(Mana);
        writer.Int32(MaxMana);
        writer.Int32(Stamina);
        writer.Int32(MaxStamina);
        writer.Int32(StatPoints);
        writer.Int32(SkillPoints);
        writer.Int32(Strength);
        writer.Int32(Spirit);
        writer.Int32(Talent);
        writer.Int32(Agility);
        writer.Int32(Health);
        writer.Int32(DamageMin);
        writer.Int32(DamageMax);
        writer.Int32(Defence);
        writer.Int32(Absorption);
        writer.Int32(AttackRating);
        writer.Int32(MoveSpeed);
        writer.Single(AttackRange);
        writer.Int64(InventoryRevision);
        writer.Int64(ProgressionRevision);
        writer.Int64(SkillsRevision);
        writer.Int32(EliteSkillPoints);
        writer.Int32(Tier);
        writer.Byte(BagPage);
        writer.Byte(WeaponSet);
        return writer.Finish();
    }

    public static OwnerStatusPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new OwnerStatusPacket(r.Int32(), r.UInt64(), r.UInt64(), r.UInt64(), r.Int64(), r.Int32(), r.Int32(), r.Int32(), r.Int32(),
            r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32(),
            r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Single(), r.Int64(), r.Int64(), r.Int64(), r.Int32(), r.Int32(), r.Byte(), r.Byte());
    }
}

public readonly record struct InventoryItemEntry(Guid InstanceId, uint Code, int Kind, int Count, int Anchor, int Slot, short Width, short Height, string Name)
{
    public const int MaxNameBytes = 64;
    public const int MaxSize = 16 + 4 * 5 + 2 * 2 + 2 + MaxNameBytes;
}

// {size, code, Revision, Part, Parts, Count, entries {Guid, uint32 Code, int32 Kind, Count, Anchor, Slot, int16 Width,
// Height, text Name}}. The client replaces its inventory when all parts of a revision arrive.
public sealed record InventoryPartPacket(long Revision, int Part, int Parts, IReadOnlyList<InventoryItemEntry> Items)
{
    public const int HeaderSize = 8 + 8 + 4 * 3;
    public const int MaxItemsPerPart = 64;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Inventory);
        writer.Int64(Revision);
        writer.Int32(Part);
        writer.Int32(Parts);
        writer.Int32(Items.Count);
        foreach (var item in Items)
        {
            writer.Guid(item.InstanceId);
            writer.UInt32(item.Code);
            writer.Int32(item.Kind);
            writer.Int32(item.Count);
            writer.Int32(item.Anchor);
            writer.Int32(item.Slot);
            writer.Int16(item.Width);
            writer.Int16(item.Height);
            Utf8Text.Write(ref writer, item.Name, InventoryItemEntry.MaxNameBytes);
        }

        return writer.Finish();
    }

    public static InventoryPartPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var revision = reader.Int64();
        var part = reader.Int32();
        var parts = reader.Int32();
        var count = reader.Int32();
        if (parts < 1 || part < 0 || part >= parts || count < 0 || count > MaxItemsPerPart)
        {
            throw new InvalidDataException("Inventory part has invalid counts.");
        }

        var items = new InventoryItemEntry[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = new InventoryItemEntry(reader.Guid(), reader.UInt32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(),
                reader.Int16(), reader.Int16(), Utf8Text.Read(ref reader, InventoryItemEntry.MaxNameBytes, "Inventory item"));
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Inventory part has trailing bytes.");
        }

        return new InventoryPartPacket(revision, part, parts, items);
    }
}

public sealed record SkillsPacket(long Revision, IReadOnlyList<(uint Code, int Rank, int Uses)> Skills)
{
    public const int HeaderSize = 8 + 8 + 4;
    public const int MaxSkills = 512;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Skills);
        writer.Int64(Revision);
        writer.Int32(Skills.Count);
        foreach (var (code, rank, uses) in Skills)
        {
            writer.UInt32(code);
            writer.Int32(rank);
            writer.Int32(uses);
        }

        return writer.Finish();
    }

    public static SkillsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var revision = reader.Int64();
        var count = reader.Int32();
        if (count < 0 || count > MaxSkills || reader.Remaining != count * 12)
        {
            throw new InvalidDataException("Skill list has an invalid count.");
        }

        var skills = new (uint, int, int)[count];
        for (var index = 0; index < count; index++)
        {
            skills[index] = (reader.UInt32(), reader.Int32(), reader.Int32());
        }

        return new SkillsPacket(revision, skills);
    }
}

public sealed record InventoryCommandPacket(Guid OperationId, long ExpectedRevision, int Action, Guid ItemId, int Destination, int Count, int Slot) : IFramePacket
{
    public const int Size = 8 + 16 + 8 + 4 + 16 + 4 * 3;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.InventoryCommand);
        writer.Guid(OperationId);
        writer.Int64(ExpectedRevision);
        writer.Int32(Action);
        writer.Guid(ItemId);
        writer.Int32(Destination);
        writer.Int32(Count);
        writer.Int32(Slot);
        return writer.Finish();
    }

    public static InventoryCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new InventoryCommandPacket(r.Guid(), r.Int64(), r.Int32(), r.Guid(), r.Int32(), r.Int32(), r.Int32());
    }
}

public sealed record ProgressionCommandPacket(Guid OperationId, long ExpectedRevision, int Action, int Attribute, int Amount, uint SkillCode, ulong NpcId) : IFramePacket
{
    public const int Size = 8 + 16 + 8 + 4 * 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ProgressionCommand);
        writer.Guid(OperationId);
        writer.Int64(ExpectedRevision);
        writer.Int32(Action);
        writer.Int32(Attribute);
        writer.Int32(Amount);
        writer.UInt32(SkillCode);
        writer.UInt64(NpcId);
        return writer.Finish();
    }

    public static ProgressionCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        return new ProgressionCommandPacket(r.Guid(), r.Int64(), r.Int32(), r.Int32(), r.Int32(), r.UInt32(), r.UInt64());
    }
}

// Shared reply for inventory and progression commands, correlated by OperationId: {header, Guid OperationId, int32 Ok,
// int64 Revision, game text Message}.
public sealed record OperationResultPacket(uint Code, Guid OperationId, bool Ok, long Revision, GameText Message)
{
    public const int MinSize = 8 + 16 + 4 + 8 + GameTextWire.MinimumSize;
    public const int MaxSize = MinSize - GameTextWire.MinimumSize + GameTextWire.MaximumSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, Code);
        writer.Guid(OperationId);
        writer.Int32(Ok ? 1 : 0);
        writer.Int64(Revision);
        GameTextWire.Write(ref writer, Message);
        return writer.Finish();
    }

    public static OperationResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, MinSize);
        return new OperationResultPacket(r.Code, r.Guid(), r.Int32() != 0, r.Int64(), GameTextWire.Read(ref r));
    }
}

public static class CombatEventFlags
{
    public const byte Critical = 1;
    public const byte Killed = 2;
    // A monster's swing or hit that belongs to its skill.
    public const byte Skill = 4;
}

// A resolved combat event. Positions are fixed point (x256); the wire holds them at 1/8 of a unit.
public readonly record struct CombatEventEntry(
    ulong Attacker,
    ulong Target,
    CombatEventKind Kind,
    byte Flags,
    float DurationMs,
    float HitAtMs,
    int Damage,
    int Resources,
    int SoundCode,
    int X,
    int Y,
    int Z,
    short Angle,
    short Clip = -1) : IEventEntry<CombatEventEntry>
{
    // {uint8 Kind, Flags, Fields, entity Attacker}, then what Fields names, in its bit order (EventWire).
    public const int MinSize = 4;
    public const int MaxSize = 3 + EventWire.MaxEntityBytes * 2 + EventWire.MaxTimeBytes * 2 + VarInt.MaxBytes32 * 3 + EventWire.PointBytes
        + VarInt.MaxBytes16 * 2;

    static int IEventEntry<CombatEventEntry>.MinSize => MinSize;

    static int IEventEntry<CombatEventEntry>.MaxSize => MaxSize;

    [Flags]
    private enum Fields : byte
    {
        Target = 1,
        Timing = 2,
        Damage = 4,
        Resources = 8,
        Sound = 16,
        Point = 32,
        Angle = 64,
        Clip = 128
    }

    public void Write(ref PacketWriter writer, IEventHandleWriter handles)
    {
        var fields = (Target != 0 ? Fields.Target : 0)
            | (DurationMs != 0f || HitAtMs != 0f ? Fields.Timing : 0)
            | (Damage != 0 ? Fields.Damage : 0)
            | (Resources != 0 ? Fields.Resources : 0)
            | (SoundCode != 0 ? Fields.Sound : 0)
            | (X != 0 || Y != 0 || Z != 0 ? Fields.Point : 0)
            | (Angle != 0 ? Fields.Angle : 0)
            | (Clip != -1 ? Fields.Clip : 0);
        writer.Byte((byte)Kind);
        writer.Byte(Flags);
        writer.Byte((byte)fields);
        EventWire.WriteEntity(ref writer, Attacker, handles);
        if ((fields & Fields.Target) != 0)
        {
            EventWire.WriteEntity(ref writer, Target, handles);
        }

        if ((fields & Fields.Timing) != 0)
        {
            EventWire.WriteTime(ref writer, DurationMs);
            EventWire.WriteTime(ref writer, HitAtMs);
        }

        if ((fields & Fields.Damage) != 0)
        {
            writer.VarInt(Damage);
        }

        if ((fields & Fields.Resources) != 0)
        {
            writer.VarInt(Resources);
        }

        if ((fields & Fields.Sound) != 0)
        {
            writer.VarInt(SoundCode);
        }

        if ((fields & Fields.Point) != 0)
        {
            EventWire.WritePoint(ref writer, X, Y, Z);
        }

        if ((fields & Fields.Angle) != 0)
        {
            writer.VarInt(Angle);
        }

        if ((fields & Fields.Clip) != 0)
        {
            writer.VarInt(Clip);
        }
    }

    public static CombatEventEntry Read(ref PacketReader reader, IEventHandleReader handles)
    {
        var kind = (CombatEventKind)reader.Byte();
        var flags = reader.Byte();
        var fields = (Fields)reader.Byte();
        var attacker = EventWire.ReadEntity(ref reader, handles);
        var target = (fields & Fields.Target) != 0 ? EventWire.ReadEntity(ref reader, handles) : 0;
        var (duration, hitAt) = (fields & Fields.Timing) != 0 ? (EventWire.ReadTime(ref reader), EventWire.ReadTime(ref reader)) : (0f, 0f);
        var damage = (fields & Fields.Damage) != 0 ? reader.VarInt() : 0;
        var resources = (fields & Fields.Resources) != 0 ? reader.VarInt() : 0;
        var sound = (fields & Fields.Sound) != 0 ? reader.VarInt() : 0;
        var (x, y, z) = (fields & Fields.Point) != 0 ? EventWire.ReadPoint(ref reader) : (0, 0, 0);
        var angle = (fields & Fields.Angle) != 0 ? Short(reader.VarInt()) : (short)0;
        var clip = (fields & Fields.Clip) != 0 ? Short(reader.VarInt()) : (short)-1;
        return new CombatEventEntry(attacker, target, kind, flags, duration, hitAt, damage, resources, sound, x, y, z, angle, clip);
    }

    private static short Short(int value) =>
        value is >= short.MinValue and <= short.MaxValue ? (short)value : throw new InvalidDataException("Combat event value is out of range.");
}

// Resolved combat events (EventWire, version 1); the original instead lets each client roll its own hits.
public static class CombatEventList
{
    public const ushort CurrentVersion = 1;

    public static int Write(Span<byte> destination, ReadOnlySpan<CombatEventEntry> events, IEventHandleWriter handles, out int written) =>
        EventWire.Write(destination, EtervaleOpcodes.CombatEvents, CurrentVersion, events, handles, out written);

    public static CombatEventEntry[] Read(ReadOnlySpan<byte> packet, IEventHandleReader handles) =>
        EventWire.Read<CombatEventEntry>(packet, CurrentVersion, "Combat event list", handles);
}

// Objects leaving the receiver's interest area ({size, code, int32 Count, uint64 EntityId...}); the original client instead
// times them out after 4-30 seconds.
public static class RemoveObjectList
{
    public const int HeaderSize = 12;
    public const int MaxEntityIds = (FrameCodec.MaxFrameSize - HeaderSize) / 8;

    public static int SizeFor(int count) => HeaderSize + count * 8;

    public static int Write(Span<byte> destination, ReadOnlySpan<ulong> entityIds)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RemoveObjects);
        writer.Int32(entityIds.Length);
        foreach (var entityId in entityIds)
        {
            writer.UInt64(entityId);
        }

        return writer.Finish();
    }

    public static ulong[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        var count = reader.Int32();
        if (count < 0 || count > MaxEntityIds || reader.Remaining != count * 8)
        {
            throw new InvalidDataException("Object removal list has an invalid count.");
        }

        var entityIds = new ulong[count];
        for (var index = 0; index < count; index++)
        {
            entityIds[index] = reader.UInt64();
        }

        return entityIds;
    }
}

// Server simulation clock for the presence entries that follow it on the connection: presence entries and
// combat events until the next clock. Layout: {size, code, ushort Version, ushort Flags, uint ServerTick,
// uint TickIntervalMs}. Readers accept newer versions that append fields.
public readonly record struct PresenceClockPacket(uint ServerTick, uint TickIntervalMs)
{
    public const ushort CurrentVersion = 1;
    public const int Size = 20;

    public double ServerTimeMs => (double)ServerTick * TickIntervalMs;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PresenceClock);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt32(ServerTick);
        writer.UInt32(TickIntervalMs);
        return writer.Finish();
    }

    public static PresenceClockPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Presence clock");
        var tick = reader.UInt32();
        var interval = reader.UInt32();
        if (interval == 0)
        {
            throw new InvalidDataException("Presence clock has a zero tick interval.");
        }

        return new PresenceClockPacket(tick, interval);
    }
}
