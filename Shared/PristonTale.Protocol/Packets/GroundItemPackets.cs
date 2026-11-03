using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// A drop on the ground as the receiver sees it. Id is the drop's id, which the pick-up command names; gold drops carry
// the gold code with Amount gold.
public readonly record struct GroundItemEntry(Guid Id, uint ItemCode, bool Gold, int Amount, int X, int Y, int Z, string Name)
{
    public const int MaxNameBytes = 64;
    public const int MaxSize = 16 + 4 + 4 + 4 + 12 + 2 + MaxNameBytes;
    private const byte GoldFlag = 1;

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(Id);
        writer.UInt32(ItemCode);
        writer.Byte(Gold ? GoldFlag : (byte)0);
        writer.Zero(3);
        writer.Int32(Amount);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        Utf8Text.Write(ref writer, Name, MaxNameBytes);
    }

    public static GroundItemEntry Read(ref PacketReader reader)
    {
        var id = reader.Guid();
        var code = reader.UInt32();
        var flags = reader.Byte();
        reader.Skip(3);
        return new GroundItemEntry(id, code, (flags & GoldFlag) != 0, reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(),
            Utf8Text.Read(ref reader, MaxNameBytes, "Ground item"));
    }
}

// S->C {header, uint16 Version = 1, uint16 Flags = 0, int32 Count, entries {Guid Id, uint32 ItemCode, uint8 Flags (1 gold),
// uint8 0, uint16 0, int32 Amount, X, Y, Z (x256), text Name}}: drops entering the receiver's view.
public static class GroundItemList
{
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 8 + 4 + 4;
    public const int MaxItems = 64;

    public static int Write(Span<byte> destination, ReadOnlySpan<GroundItemEntry> items)
    {
        if (items.Length is < 1 or > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(items), "A ground item list carries between 1 and 64 drops.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.GroundItems);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Int32(items.Length);
        foreach (var item in items)
        {
            item.Write(ref writer);
        }

        return writer.Finish();
    }

    public static GroundItemEntry[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        WireHeader.Read(ref reader, "Ground item list");
        var count = reader.Int32();
        if (count is < 1 or > MaxItems)
        {
            throw new InvalidDataException("Ground item list has an invalid count.");
        }

        var items = new GroundItemEntry[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = GroundItemEntry.Read(ref reader);
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Ground item list has trailing bytes.");
        }

        return items;
    }
}

// S->C {header, uint16 Version = 1, uint16 Flags = 0, int32 Count, Guid Id...}: drops leaving the receiver's view
// (picked up, expired or out of range).
public static class GroundItemRemovals
{
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 8 + 4 + 4;
    public const int MaxItems = (FrameCodec.MaxFrameSize - HeaderSize) / 16;

    public static int Write(Span<byte> destination, ReadOnlySpan<Guid> ids)
    {
        if (ids.Length is < 1 or > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(ids), "A ground item removal names at least one drop and fits one frame.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.GroundItemsRemoved);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Int32(ids.Length);
        foreach (var id in ids)
        {
            writer.Guid(id);
        }

        return writer.Finish();
    }

    public static Guid[] Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        WireHeader.Read(ref reader, "Ground item removal");
        var count = reader.Int32();
        if (count is < 1 or > MaxItems || reader.Remaining != count * 16)
        {
            throw new InvalidDataException("Ground item removal has an invalid count.");
        }

        var ids = new Guid[count];
        for (var index = 0; index < count; index++)
        {
            ids[index] = reader.Guid();
        }

        return ids;
    }
}

internal static class GroundItemWire
{}
