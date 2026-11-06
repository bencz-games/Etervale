using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Texts;

namespace PristonTale.Protocol.Packets;

// NPC messages are Etervale extensions. Each body starts with {uint16 Version, uint16 Flags}; readers accept newer
// versions that append fields. Text is UTF-8 with a uint16 byte length, so localized names keep their characters.
public static class NpcWire
{
    public const ushort Version = 3;
    public const int MaxNameBytes = 64;
    public const int MaxItemNameBytes = 40;
    public const int MaxMessageBytes = 128;
    public const int MaxLineBytes = 256;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "NPC message", Version);

    internal static void Text(ref PacketWriter writer, string? value, int maxBytes) => Utf8Text.Write(ref writer, value, maxBytes);

    internal static string Text(ref PacketReader reader, int maxBytes) => Utf8Text.Read(ref reader, maxBytes, "NPC message");

    internal static int TextSize(string? value, int maxBytes) => Utf8Text.Size(value, maxBytes);
}

public readonly record struct NpcServiceEntry(byte Kind, bool Available, int Value, ushort PricePercent = 100)
{
    public const int Size = 8;
}

// Completes an NPC click: {header, uint64 NpcId, int32 Ok, text Name, game text Message, int32 ServiceCount,
// services {uint8 Kind, uint8 Available, uint16 0, int32 Value}, int32 LineCount, text lines}.
public sealed record NpcDialogPacket(ulong NpcId, bool Ok, string Name, GameText Message, IReadOnlyList<NpcServiceEntry> Services,
    IReadOnlyList<string> Lines)
{
    public const int MaxServices = 32;
    public const int MaxLines = 20;
    private const int MinimumSize = 8 + 4 + 8 + 4 + 2 + GameTextWire.MinimumSize + 4 + 4;

    public int Write(Span<byte> destination)
    {
        if (Services.Count > MaxServices || Lines.Count > MaxLines)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many NPC services or lines.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.NpcResult);
        NpcWire.Header(ref writer);
        writer.UInt64(NpcId);
        writer.Int32(Ok ? 1 : 0);
        NpcWire.Text(ref writer, Name, NpcWire.MaxNameBytes);
        GameTextWire.Write(ref writer, Message);
        writer.Int32(Services.Count);
        foreach (var service in Services)
        {
            writer.Byte(service.Kind);
            writer.Byte(service.Available ? (byte)1 : (byte)0);
            writer.UInt16(service.PricePercent);
            writer.Int32(service.Value);
        }

        writer.Int32(Lines.Count);
        foreach (var line in Lines)
        {
            NpcWire.Text(ref writer, line, NpcWire.MaxLineBytes);
        }

        return writer.Finish();
    }

    public static NpcDialogPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        NpcWire.ReadHeader(ref reader);
        var entityId = reader.UInt64();
        var ok = reader.Int32() != 0;
        var name = NpcWire.Text(ref reader, NpcWire.MaxNameBytes);
        var message = GameTextWire.Read(ref reader);
        var serviceCount = reader.Int32();
        if (serviceCount < 0 || serviceCount > MaxServices)
        {
            throw new InvalidDataException("NPC dialog has an invalid service count.");
        }

        var services = new NpcServiceEntry[serviceCount];
        for (var index = 0; index < serviceCount; index++)
        {
            var kind = reader.Byte();
            var available = reader.Byte() != 0;
            var percent = reader.UInt16();
            services[index] = new NpcServiceEntry(kind, available, reader.Int32(), percent);
        }

        var lineCount = reader.Int32();
        if (lineCount < 0 || lineCount > MaxLines)
        {
            throw new InvalidDataException("NPC dialog has an invalid line count.");
        }

        var lines = new string[lineCount];
        for (var index = 0; index < lineCount; index++)
        {
            lines[index] = NpcWire.Text(ref reader, NpcWire.MaxLineBytes);
        }

        return new NpcDialogPacket(entityId, ok, name, message, services, lines);
    }
}

// C→S request for one NPC service, correlated by OperationId. Item, anchor, count, amount and covered fields are read
// only by the actions that use them; the client never sends items, prices or totals. A work on an item appends its
// materials: {uint8 Count, Guid[Count]} (absent when none).
public sealed record NpcServiceRequestPacket(
    Guid OperationId,
    ulong NpcId,
    byte Service,
    byte Action,
    long ExpectedRevision,
    long ExpectedStorageRevision,
    uint ItemCode,
    Guid ItemId,
    int Anchor,
    int Count,
    long Amount,
    Guid[]? Materials = null,
    Guid Covered = default) : IFramePacket
{
    public const int Size = 8 + 4 + 16 + 8 + 4 + 8 + 8 + 4 + 16 + 4 + 4 + 8 + 16;
    public const int MaxMaterials = 16;
    public const int MaxSize = Size + 1 + 16 * MaxMaterials;

    public bool Equals(NpcServiceRequestPacket? other) =>
        other is not null && OperationId == other.OperationId && NpcId == other.NpcId && Service == other.Service && Action == other.Action
        && ExpectedRevision == other.ExpectedRevision && ExpectedStorageRevision == other.ExpectedStorageRevision && ItemCode == other.ItemCode
        && ItemId == other.ItemId && Anchor == other.Anchor && Count == other.Count && Amount == other.Amount && Covered == other.Covered
        && (Materials ?? []).AsSpan().SequenceEqual(other.Materials ?? []);

    public override int GetHashCode() => HashCode.Combine(OperationId, NpcId, Service, Action, ItemId, Amount);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.NpcServiceRequest);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Byte(Service);
        writer.Byte(Action);
        writer.UInt16(0);
        writer.Int64(ExpectedRevision);
        writer.Int64(ExpectedStorageRevision);
        writer.UInt32(ItemCode);
        writer.Guid(ItemId);
        writer.Int32(Anchor);
        writer.Int32(Count);
        writer.Int64(Amount);
        writer.Guid(Covered);
        if (Materials is { Length: > 0 } materials)
        {
            if (materials.Length > MaxMaterials)
            {
                throw new ArgumentException("Too many materials.");
            }

            writer.Byte((byte)materials.Length);
            foreach (var material in materials)
            {
                writer.Guid(material);
            }
        }

        return writer.Finish();
    }

    public static NpcServiceRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var service = r.Byte();
        var action = r.Byte();
        r.Skip(2);
        var request = new NpcServiceRequestPacket(operation, entityId, service, action, r.Int64(), r.Int64(), r.UInt32(), r.Guid(), r.Int32(), r.Int32(),
            r.Int64(), Covered: r.Guid());
        if (r.Remaining == 0)
        {
            return request;
        }

        var count = r.Byte();
        if (count is 0 or > MaxMaterials || r.Remaining != count * 16)
        {
            throw new InvalidDataException("Invalid NPC service materials.");
        }

        var materials = new Guid[count];
        for (var index = 0; index < count; index++)
        {
            materials[index] = r.Guid();
        }

        return request with { Materials = materials };
    }
}

// S→C completion of a service request, sent after the state messages the request produced.
public sealed record NpcServiceResultPacket(Guid OperationId, ulong NpcId, byte Service, byte Action, ushort Status, bool Ok,
    long Revision, long StorageRevision, GameText Message)
{
    private const int MinimumSize = 8 + 4 + 16 + 8 + 4 + 4 + 8 + 8 + GameTextWire.MinimumSize;
    public const int MaxSize = MinimumSize - GameTextWire.MinimumSize + GameTextWire.MaximumSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.NpcServiceResult);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Byte(Service);
        writer.Byte(Action);
        writer.UInt16(Status);
        writer.Int32(Ok ? 1 : 0);
        writer.Int64(Revision);
        writer.Int64(StorageRevision);
        GameTextWire.Write(ref writer, Message);
        return writer.Finish();
    }

    public static NpcServiceResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, MinimumSize);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var service = r.Byte();
        var action = r.Byte();
        var status = r.UInt16();
        var ok = r.Int32() != 0;
        var revision = r.Int64();
        var storage = r.Int64();
        return new NpcServiceResultPacket(operation, entityId, service, action, status, ok, revision, storage, GameTextWire.Read(ref r));
    }
}

// S→C what a work on an item came to, sent before the result: {header, Guid OperationId, uint64 NpcId, uint8 Service,
// uint8 Result, uint16 0, Guid ItemId (empty when the item was lost), uint32 Code}.
public sealed record ItemWorkPacket(Guid OperationId, ulong NpcId, byte Service, byte Result, Guid ItemId, uint Code)
{
    public const int Size = 8 + 4 + 16 + 8 + 4 + 16 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ItemWork);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Byte(Service);
        writer.Byte(Result);
        writer.UInt16(0);
        writer.Guid(ItemId);
        writer.UInt32(Code);
        return writer.Finish();
    }

    public static ItemWorkPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, Size);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var service = r.Byte();
        var result = r.Byte();
        r.Skip(2);
        return new ItemWorkPacket(operation, entityId, service, result, r.Guid(), r.UInt32());
    }
}

public readonly record struct ShopCatalogEntry(uint Code, byte Tab, byte Kind, ushort Ordinal, long Price, short Width, short Height, int MaxStack,
    string Name);

// The NPC's stock with server prices: {header, Guid OperationId, uint64 NpcId, int32 Count, entries {uint32 Code,
// uint8 Tab, uint8 Kind, uint16 Ordinal, int64 Price, int16 Width, int16 Height, int32 MaxStack, text Name}}.
public sealed record ShopCatalogPacket(Guid OperationId, ulong NpcId, IReadOnlyList<ShopCatalogEntry> Items)
{
    public const int MaxItems = 96;
    private const int HeaderSize = 8 + 4 + 16 + 8 + 4;

    public int Write(Span<byte> destination)
    {
        if (Items.Count > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many shop entries.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ShopCatalog);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int32(Items.Count);
        foreach (var item in Items)
        {
            writer.UInt32(item.Code);
            writer.Byte(item.Tab);
            writer.Byte(item.Kind);
            writer.UInt16(item.Ordinal);
            writer.Int64(item.Price);
            writer.Int16(item.Width);
            writer.Int16(item.Height);
            writer.Int32(item.MaxStack);
            NpcWire.Text(ref writer, item.Name, NpcWire.MaxItemNameBytes);
        }

        return writer.Finish();
    }

    public static ShopCatalogPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, HeaderSize);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var count = r.Int32();
        if (count < 0 || count > MaxItems)
        {
            throw new InvalidDataException("Shop catalog has an invalid count.");
        }

        var items = new ShopCatalogEntry[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = new ShopCatalogEntry(r.UInt32(), r.Byte(), r.Byte(), r.UInt16(), r.Int64(), r.Int16(), r.Int16(), r.Int32(),
                NpcWire.Text(ref r, NpcWire.MaxItemNameBytes));
        }

        return new ShopCatalogPacket(operation, entityId, items);
    }
}

public readonly record struct ShopQuoteEntry(Guid ItemId, long SellPrice, long RepairCost, int Durability, int MaxDurability)
    : IWireEntry<ShopQuoteEntry>
{
    public const int Size = 16 + 8 + 8 + 4 + 4;

    static int IWireEntry<ShopQuoteEntry>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(ItemId);
        writer.Int64(SellPrice);
        writer.Int64(RepairCost);
        writer.Int32(Durability);
        writer.Int32(MaxDurability);
    }

    public static ShopQuoteEntry Read(ref PacketReader reader) =>
        new(reader.Guid(), reader.Int64(), reader.Int64(), reader.Int32(), reader.Int32());
}

// What the shop pays for each owned item and what repairing it costs: {header, Guid OperationId, uint64 NpcId,
// int64 InventoryRevision, int32 Count, entries}.
public sealed record ShopQuotesPacket(Guid OperationId, ulong NpcId, long InventoryRevision, IReadOnlyList<ShopQuoteEntry> Items)
{
    public const int MaxItems = 128;
    public const int HeaderSize = 8 + 4 + 16 + 8 + 8 + 4;

    public static int SizeFor(int count) => HeaderSize + count * ShopQuoteEntry.Size;

    public int Write(Span<byte> destination)
    {
        if (Items.Count > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many shop quotes.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ShopQuotes);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int64(InventoryRevision);
        WireList.Write(ref writer, Items);

        return writer.Finish();
    }

    public static ShopQuotesPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, HeaderSize);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var revision = r.Int64();
        return new ShopQuotesPacket(operation, entityId, revision, WireList.Read<ShopQuoteEntry>(ref r, MaxItems, "Shop quote list"));
    }
}

// An item on the store grid (the warehouse, a personal shop): {Guid, uint32 Code, uint8 Kind, uint8 0, uint16 0, int32 Count,
// int32 Anchor, int16 Width, int16 Height, text Name}.
public readonly record struct StoreItemEntry(Guid InstanceId, uint Code, byte Kind, int Count, int Anchor, short Width, short Height,
    string Name)
{
    public const int MaxSize = 16 + 4 + 4 + 4 + 4 + 2 + 2 + 2 + NpcWire.MaxItemNameBytes;

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(InstanceId);
        writer.UInt32(Code);
        writer.Byte(Kind);
        writer.Byte(0);
        writer.UInt16(0);
        writer.Int32(Count);
        writer.Int32(Anchor);
        writer.Int16(Width);
        writer.Int16(Height);
        NpcWire.Text(ref writer, Name, NpcWire.MaxItemNameBytes);
    }

    public static StoreItemEntry Read(ref PacketReader reader)
    {
        var id = reader.Guid();
        var code = reader.UInt32();
        var kind = reader.Byte();
        reader.Skip(3);
        return new StoreItemEntry(id, code, kind, reader.Int32(), reader.Int32(), reader.Int16(), reader.Int16(), NpcWire.Text(ref reader, NpcWire.MaxItemNameBytes));
    }
}

// The account warehouse after an open or a change: {header, Guid OperationId, uint64 NpcId, int64 Revision, int64 Money,
// int32 Weight, int32 MaxWeight, int16 Columns, int16 Rows, int32 Count, StoreItemEntry items}.
public sealed record WarehousePacket(Guid OperationId, ulong NpcId, long Revision, long Money, int Weight, int MaxWeight, short Columns,
    short Rows, IReadOnlyList<StoreItemEntry> Items)
{
    public const int MaxItems = 100;
    private const int HeaderSize = 8 + 4 + 16 + 8 + 8 + 8 + 4 + 4 + 2 + 2 + 4;

    public int Write(Span<byte> destination)
    {
        if (Items.Count > MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many warehouse items.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.Warehouse);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int64(Revision);
        writer.Int64(Money);
        writer.Int32(Weight);
        writer.Int32(MaxWeight);
        writer.Int16(Columns);
        writer.Int16(Rows);
        writer.Int32(Items.Count);
        foreach (var item in Items)
        {
            item.Write(ref writer);
        }

        return writer.Finish();
    }

    public static WarehousePacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, HeaderSize);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var revision = r.Int64();
        var money = r.Int64();
        var weight = r.Int32();
        var maxWeight = r.Int32();
        var columns = r.Int16();
        var rows = r.Int16();
        var count = r.Int32();
        if (count < 0 || count > MaxItems || columns < 1 || rows < 1)
        {
            throw new InvalidDataException("Warehouse message has an invalid size.");
        }

        var items = new StoreItemEntry[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = StoreItemEntry.Read(ref r);
        }

        return new WarehousePacket(operation, entityId, revision, money, weight, maxWeight, columns, rows, items);
    }
}

public readonly record struct TeleportEntry(int Index, int FieldCode, long Price, int MinimumLevel, string Name);

// Destinations of a teleporter: {header, Guid OperationId, uint64 NpcId, int32 Count, entries {int32 Index,
// int32 FieldCode, int64 Price, int32 MinimumLevel, text Name}}.
public sealed record TeleportListPacket(Guid OperationId, ulong NpcId, IReadOnlyList<TeleportEntry> Destinations)
{
    public const int MaxDestinations = 16;
    private const int HeaderSize = 8 + 4 + 16 + 8 + 4;

    public int Write(Span<byte> destination)
    {
        if (Destinations.Count > MaxDestinations)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many teleport destinations.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.TeleportList);
        NpcWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(NpcId);
        writer.Int32(Destinations.Count);
        foreach (var entry in Destinations)
        {
            writer.Int32(entry.Index);
            writer.Int32(entry.FieldCode);
            writer.Int64(entry.Price);
            writer.Int32(entry.MinimumLevel);
            NpcWire.Text(ref writer, entry.Name, NpcWire.MaxNameBytes);
        }

        return writer.Finish();
    }

    public static TeleportListPacket Read(ReadOnlySpan<byte> packet)
    {
        var r = new PacketReader(packet, HeaderSize);
        NpcWire.ReadHeader(ref r);
        var operation = r.Guid();
        var entityId = r.UInt64();
        var count = r.Int32();
        if (count < 0 || count > MaxDestinations)
        {
            throw new InvalidDataException("Teleport list has an invalid count.");
        }

        var destinations = new TeleportEntry[count];
        for (var index = 0; index < count; index++)
        {
            destinations[index] = new TeleportEntry(r.Int32(), r.Int32(), r.Int64(), r.Int32(), NpcWire.Text(ref r, NpcWire.MaxNameBytes));
        }

        return new TeleportListPacket(operation, entityId, destinations);
    }
}
