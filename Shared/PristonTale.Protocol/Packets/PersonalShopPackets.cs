using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Texts;

namespace PristonTale.Protocol.Packets;

// Personal shop messages. Each body starts with {uint16 Version = 1, uint16 Flags = 0}; readers reject version 0. Text is
// UTF-8 with a uint16 byte length, names and messages within the NPC messages' limits. Requests carry an operation id the
// answers repeat: a view or a purchase is answered by PersonalShopListing and PersonalShopDetails (when the shop is still
// open) and then PersonalShopResult.
public static class PersonalShopWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Personal shop message");

    internal static string Text(ref PacketReader reader, int maxBytes) => Utf8Text.Read(ref reader, maxBytes, "Personal shop message");
}

// An item the seller lays out: {Guid ItemId, int64 UnitPrice, int32 Cell}.
public readonly record struct PersonalShopEntryWire(Guid ItemId, long UnitPrice, int Cell) : IWireEntry<PersonalShopEntryWire>
{
    public const int Size = 16 + 8 + 4;

    static int IWireEntry<PersonalShopEntryWire>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(ItemId);
        writer.Int64(UnitPrice);
        writer.Int32(Cell);
    }

    public static PersonalShopEntryWire Read(ref PacketReader reader) => new(reader.Guid(), reader.Int64(), reader.Int32());
}

// C->S: open the shop. {header, Guid OperationId, text Sign, int32 Count, entries}.
public sealed record PersonalShopOpenPacket(Guid OperationId, string Sign, IReadOnlyList<PersonalShopEntryWire> Entries) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 2 + 4;
    public const int MaxSize = MinimumSize + PersonalShopLimits.MaxSignBytes + PersonalShopLimits.MaxEntries * PersonalShopEntryWire.Size;

    public int Write(Span<byte> destination)
    {
        if (Entries.Count > PersonalShopLimits.MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many personal shop entries.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopOpen);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        Utf8Text.Write(ref writer, Sign, PersonalShopLimits.MaxSignBytes);
        WireList.Write(ref writer, Entries);
        return writer.Finish();
    }

    public static PersonalShopOpenPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        PersonalShopWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var sign = PersonalShopWire.Text(ref reader, PersonalShopLimits.MaxSignBytes);
        return new PersonalShopOpenPacket(operation, sign, WireList.Read<PersonalShopEntryWire>(ref reader, PersonalShopLimits.MaxEntries, "Personal shop"));
    }
}

// C->S: close the shop. {header, Guid OperationId}.
public sealed record PersonalShopClosePacket(Guid OperationId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopClose);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        return writer.Finish();
    }

    public static PersonalShopClosePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        PersonalShopWire.ReadHeader(ref reader);
        return new PersonalShopClosePacket(reader.Guid());
    }
}

// C->S: show a seller's shop. {header, Guid OperationId, uint64 SellerId}.
public sealed record PersonalShopViewPacket(Guid OperationId, ulong SellerId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopView);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(SellerId);
        return writer.Finish();
    }

    public static PersonalShopViewPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        PersonalShopWire.ReadHeader(ref reader);
        return new PersonalShopViewPacket(reader.Guid(), reader.UInt64());
    }
}

// C->S: buy Count units of a listed item at the unit price shown. {header, Guid OperationId, uint64 SellerId, Guid ItemId,
// int64 UnitPrice, int32 Count, int32 0, int64 ExpectedRevision (the buyer's inventory)}.
public sealed record PersonalShopBuyPacket(Guid OperationId, ulong SellerId, Guid ItemId, long UnitPrice, int Count, long ExpectedRevision) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16 + 8 + 16 + 8 + 4 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopBuy);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(SellerId);
        writer.Guid(ItemId);
        writer.Int64(UnitPrice);
        writer.Int32(Count);
        writer.Zero(4);
        writer.Int64(ExpectedRevision);
        return writer.Finish();
    }

    public static PersonalShopBuyPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        PersonalShopWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var seller = reader.UInt64();
        var item = reader.Guid();
        var price = reader.Int64();
        var count = reader.Int32();
        reader.Skip(4);
        return new PersonalShopBuyPacket(operation, seller, item, price, count, reader.Int64());
    }
}

// S->C: what a request came to. {header, Guid OperationId, uint8 Action, uint8 0, uint16 Status, int32 Count, int64 Revision,
// int64 UnitPrice, game text Message}. Count and UnitPrice name what the shop offers now after a changed count or price.
public sealed record PersonalShopResultPacket(Guid OperationId, byte Action, ushort Status, int Count, long Revision, long UnitPrice, GameText Message)
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 4 + 4 + 8 + 8 + GameTextWire.MinimumSize;
    public const int MaxSize = MinimumSize - GameTextWire.MinimumSize + GameTextWire.MaximumSize;

    public bool Ok => Status == 0;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopResult);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Action);
        writer.Zero(1);
        writer.UInt16(Status);
        writer.Int32(Count);
        writer.Int64(Revision);
        writer.Int64(UnitPrice);
        GameTextWire.Write(ref writer, Message);
        return writer.Finish();
    }

    public static PersonalShopResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        PersonalShopWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var action = reader.Byte();
        reader.Skip(1);
        var status = reader.UInt16();
        var count = reader.Int32();
        var revision = reader.Int64();
        var price = reader.Int64();
        return new PersonalShopResultPacket(operation, action, status, count, revision, price, GameTextWire.Read(ref reader));
    }
}

// An offered item at its store grid cell (the entry's anchor) and the price of one unit.
public readonly record struct PersonalShopOfferWire(StoreItemEntry Item, long UnitPrice);

// S->C: a seller's shop as the viewer sees it. {header, Guid OperationId, uint64 SellerId, int32 Count, offers
// {StoreItemEntry, int64 UnitPrice}}.
public sealed record PersonalShopListingPacket(Guid OperationId, ulong SellerId, IReadOnlyList<PersonalShopOfferWire> Offers)
{
    private const int MinimumSize = 8 + WireHeader.Size + 16 + 8 + 4;
    public const int MaxSize = MinimumSize + PersonalShopLimits.MaxEntries * (StoreItemEntry.MaxSize + 8);

    public int Write(Span<byte> destination)
    {
        if (Offers.Count > PersonalShopLimits.MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many personal shop offers.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopListing);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(SellerId);
        writer.Int32(Offers.Count);
        foreach (var offer in Offers)
        {
            offer.Item.Write(ref writer);
            writer.Int64(offer.UnitPrice);
        }

        return writer.Finish();
    }

    public static PersonalShopListingPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        PersonalShopWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var seller = reader.UInt64();
        var count = reader.Int32();
        if (count < 0 || count > PersonalShopLimits.MaxEntries)
        {
            throw new InvalidDataException("Personal shop listing has an invalid count.");
        }

        var offers = new PersonalShopOfferWire[count];
        for (var index = 0; index < count; index++)
        {
            offers[index] = new PersonalShopOfferWire(StoreItemEntry.Read(ref reader), reader.Int64());
        }

        return new PersonalShopListingPacket(operation, seller, offers);
    }
}

// S->C: the values of the offered copies, for their tooltips. {header, Guid OperationId, uint64 SellerId, int32 Count,
// ItemDetailEntry entries}.
public sealed record PersonalShopDetailsPacket(Guid OperationId, ulong SellerId, IReadOnlyList<ItemDetailEntry> Items)
{
    private const int HeaderSize = 8 + WireHeader.Size + 16 + 8 + 4;
    public const int MaxSize = HeaderSize + PersonalShopLimits.MaxEntries * ItemDetailEntry.Size;

    public int Write(Span<byte> destination)
    {
        if (Items.Count > PersonalShopLimits.MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many personal shop details.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PersonalShopDetails);
        PersonalShopWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.UInt64(SellerId);
        WireList.Write(ref writer, Items);
        return writer.Finish();
    }

    public static PersonalShopDetailsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        PersonalShopWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var seller = reader.UInt64();
        return new PersonalShopDetailsPacket(operation, seller, WireList.Read<ItemDetailEntry>(ref reader, PersonalShopLimits.MaxEntries, "Personal shop details"));
    }
}
