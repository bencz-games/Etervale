using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Texts;

namespace PristonTale.Protocol.Packets;

// Usable-item messages are Etervale extensions. Every body starts with {uint16 Version, uint16 Flags}; readers reject
// version 0 and accept newer versions that append fields, except the lists, whose length is checked exactly.
public static class ItemUseWire
{
    public const ushort Version = 1;
    public const int MaxTextBytes = 64;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Item use message");
}

// C->S: use one owned item. {header, Guid OperationId, int64 ExpectedRevision, Guid ItemId, int32 Argument, text Text}.
// Argument names a destination of a travel item; Text is the line a firecracker shows.
public sealed record ItemUseCommandPacket(Guid OperationId, long ExpectedRevision, Guid ItemId, int Argument, string Text) : IFramePacket
{
    public const int MinimumSize = 8 + 4 + 16 + 8 + 16 + 4 + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemUseCommand);
        ItemUseWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Int64(ExpectedRevision);
        writer.Guid(ItemId);
        writer.Int32(Argument);
        NpcWire.Text(ref writer, Text, ItemUseWire.MaxTextBytes);
        return writer.Finish();
    }

    public static ItemUseCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ItemUseWire.ReadHeader(ref reader);
        return new ItemUseCommandPacket(reader.Guid(), reader.Int64(), reader.Guid(), reader.Int32(), NpcWire.Text(ref reader, ItemUseWire.MaxTextBytes));
    }
}

// S->C: the answer to one item use. {header, Guid OperationId, int32 Ok, int64 Revision, uint16 Status, uint16 0,
// uint32 ItemCode, game text Message}.
public sealed record ItemUseResultPacket(Guid OperationId, bool Ok, long Revision, ushort Status, uint ItemCode, GameText Message)
{
    public const int MinimumSize = 8 + 4 + 16 + 4 + 8 + 4 + 4 + GameTextWire.MinimumSize;
    public const int MaximumSize = MinimumSize - GameTextWire.MinimumSize + GameTextWire.MaximumSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemUseResult);
        ItemUseWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Int32(Ok ? 1 : 0);
        writer.Int64(Revision);
        WireHeader.Write(ref writer, Status);
        writer.UInt32(ItemCode);
        GameTextWire.Write(ref writer, Message);
        return writer.Finish();
    }

    public static ItemUseResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ItemUseWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var ok = reader.Int32() != 0;
        var revision = reader.Int64();
        var status = reader.UInt16();
        reader.Skip(2);
        var code = reader.UInt32();
        return new ItemUseResultPacket(operation, ok, revision, status, code, GameTextWire.Read(ref reader));
    }
}

public readonly record struct PremiumTimerEntry(byte Kind, int Value, uint RemainingSeconds, uint DurationSeconds) : IWireEntry<PremiumTimerEntry>
{
    public const int Size = 16;

    static int IWireEntry<PremiumTimerEntry>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.Byte(Kind);
        writer.Zero(3);
        writer.Int32(Value);
        writer.UInt32(RemainingSeconds);
        writer.UInt32(DurationSeconds);
    }

    public static PremiumTimerEntry Read(ref PacketReader reader)
    {
        var kind = reader.Byte();
        reader.Skip(3);
        return new PremiumTimerEntry(kind, reader.Int32(), reader.UInt32(), reader.UInt32());
    }
}

// S->C, owner only: every running timed item effect. {header, int64 Revision, int32 Count, entries {uint8 Kind,
// uint8 0, uint16 0, int32 Value, uint32 RemainingSeconds, uint32 DurationSeconds}}.
public sealed record PremiumTimersPacket(long Revision, IReadOnlyList<PremiumTimerEntry> Timers)
{
    public const int HeaderSize = 8 + 4 + 8 + 4;
    public const int MaxTimers = 32;

    public int Write(Span<byte> destination)
    {
        if (Timers.Count > MaxTimers)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many timed item effects.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PremiumTimers);
        ItemUseWire.Header(ref writer);
        writer.Int64(Revision);
        WireList.Write(ref writer, Timers);

        return writer.Finish();
    }

    public static PremiumTimersPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        ItemUseWire.ReadHeader(ref reader);
        var revision = reader.Int64();
        return new PremiumTimersPacket(revision, WireList.Read<PremiumTimerEntry>(ref reader, MaxTimers, "Timed item effect list"));
    }
}

public readonly record struct ItemEffectEntry(ulong Entity, uint ItemCode, byte Effect, int X, int Y, int Z, int Variant, ulong Target)
    : IWireEntry<ItemEffectEntry>
{
    public const int Size = 40;

    static int IWireEntry<ItemEffectEntry>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.UInt64(Entity);
        writer.UInt32(ItemCode);
        writer.Byte(Effect);
        writer.Zero(3);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int32(Variant);
        writer.UInt64(Target);
    }

    public static ItemEffectEntry Read(ref PacketReader reader)
    {
        var entity = reader.UInt64();
        var code = reader.UInt32();
        var effect = reader.Byte();
        reader.Skip(3);
        return new ItemEffectEntry(entity, code, effect, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.UInt64());
    }
}

// S->C, presence timeline: items whose use plays a visible effect around a player (firecrackers, mastery). {header,
// int32 Count, entries {uint64 Entity, uint32 ItemCode, uint8 Effect, uint8 0, uint16 0, int32 X, Y, Z (x256),
// int32 Variant, uint64 Target}}.
public sealed record ItemEffectsPacket(IReadOnlyList<ItemEffectEntry> Effects)
{
    public const int HeaderSize = 8 + 4 + 4;
    public const int MaxEffects = 64;

    public int Write(Span<byte> destination)
    {
        if (Effects.Count > MaxEffects)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many item effects.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemEffects);
        ItemUseWire.Header(ref writer);
        WireList.Write(ref writer, Effects);

        return writer.Finish();
    }

    public static ItemEffectsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        ItemUseWire.ReadHeader(ref reader);
        return new ItemEffectsPacket(WireList.Read<ItemEffectEntry>(ref reader, MaxEffects, "Item effect list"));
    }
}

// C->S {header, uint16 Version = 1, uint16 0, uint32 StatusCode}: the owner confirmed the right click on a crystal pet's
// icon in the running-effects bar; the server dismisses the pet that status code names.
public readonly record struct DismissCrystalPetsPacket(uint StatusCode) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + WireHeader.Size + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DismissCrystalPets);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.UInt32(StatusCode);
        return writer.Finish();
    }

    public static DismissCrystalPetsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Crystal pet dismissal");
        return new DismissCrystalPetsPacket(reader.UInt32());
    }
}
