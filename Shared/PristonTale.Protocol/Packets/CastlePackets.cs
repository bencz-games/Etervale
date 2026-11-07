using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class CastleWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Castle message");
}

// A clan's damage to the castle in the running siege.
public readonly record struct CastleClanWire(ulong ClanId, string Name, ulong Damage);

// S->C Bless Castle as it stands, sent at world entry and on every change: {header, varuint owner clan (0 none), text
// owner name, uint8 siege (0 peace, 1 running, 3 over), uint8 flags (1 league mode, 2 door down), varuint seconds the
// running siege had left when the state was made (0 otherwise), uint8 crystal towers standing, uint8 count, {varuint
// clan, text name, varuint damage} x count (the best clans, at most CastleLimits.TopClans), varuint total damage}.
public sealed record CastleStatePacket(ulong OwnerClanId, string OwnerName, byte Siege, bool League, bool DoorDown, uint SecondsLeft, byte Crystals,
    IReadOnlyList<CastleClanWire> Top, ulong TotalDamage) : IFramePacket
{
    private const byte LeagueFlag = 1;
    private const byte DoorDownFlag = 2;
    private const int ClanSize = VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + VarInt.MaxBytes64;
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 2 + VarInt.MaxBytes64 + 2
        + CastleLimits.TopClans * ClanSize + VarInt.MaxBytes64;

    public static readonly CastleStatePacket None = new(0, "", 0, false, false, 0, 0, [], 0);

    public int Write(Span<byte> destination)
    {
        if (Top.Count > CastleLimits.TopClans)
        {
            throw new InvalidOperationException("Too many clans on the castle's board.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.CastleState);
        CastleWire.Header(ref writer);
        writer.VarUInt(OwnerClanId);
        Utf8Text.Write(ref writer, OwnerName, ClanWire.MaxClanNameBytes);
        writer.Byte(Siege);
        writer.Byte((byte)((League ? LeagueFlag : 0) | (DoorDown ? DoorDownFlag : 0)));
        writer.VarUInt(SecondsLeft);
        writer.Byte(Crystals);
        writer.Byte((byte)Top.Count);
        foreach (var clan in Top)
        {
            writer.VarUInt(clan.ClanId);
            Utf8Text.Write(ref writer, clan.Name, ClanWire.MaxClanNameBytes);
            writer.VarUInt(clan.Damage);
        }

        writer.VarUInt(TotalDamage);
        return writer.Finish();
    }

    public static CastleStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size);
        CastleWire.ReadHeader(ref reader);
        var owner = reader.VarUInt();
        var ownerName = ClanWire.ClanName(ref reader);
        var siege = reader.Byte();
        var flags = reader.Byte();
        var left = reader.VarUInt();
        if (left > uint.MaxValue)
        {
            throw new InvalidDataException("Castle message has a siege too long.");
        }

        var crystals = reader.Byte();
        var count = reader.Byte();
        if (count > CastleLimits.TopClans)
        {
            throw new InvalidDataException("Castle message has too many clans.");
        }

        var top = new CastleClanWire[count];
        for (var index = 0; index < count; index++)
        {
            top[index] = new CastleClanWire(reader.VarUInt(), ClanWire.ClanName(ref reader), reader.VarUInt());
        }

        return new CastleStatePacket(owner, ownerName, siege, (flags & LeagueFlag) != 0, (flags & DoorDownFlag) != 0, (uint)left, crystals, top, reader.VarUInt());
    }
}
