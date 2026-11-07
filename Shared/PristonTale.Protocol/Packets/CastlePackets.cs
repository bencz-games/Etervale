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
// owner name, uint8 tax (in force on fees), uint8 siege (0 peace, 1 running, 3 over), uint8 flags (1 league mode, 2
// door down), varuint seconds the running siege had left when the state was made (0 otherwise), uint8 crystal towers
// standing, uint8 count, {varuint clan, text name, varuint damage} x count (the best clans, at most
// CastleLimits.TopClans), varuint total damage}.
public sealed record CastleStatePacket(ulong OwnerClanId, string OwnerName, byte Tax, byte Siege, bool League, bool DoorDown, uint SecondsLeft, byte Crystals,
    IReadOnlyList<CastleClanWire> Top, ulong TotalDamage) : IFramePacket
{
    private const byte LeagueFlag = 1;
    private const byte DoorDownFlag = 2;
    private const int ClanSize = VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + VarInt.MaxBytes64;
    public const int MaxSize = 8 + WireHeader.Size + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 3 + VarInt.MaxBytes64 + 2
        + CastleLimits.TopClans * ClanSize + VarInt.MaxBytes64;

    public static readonly CastleStatePacket None = new(0, "", 0, 0, false, false, 0, 0, [], 0);

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
        writer.Byte(Tax);
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
        var tax = reader.Byte();
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

        return new CastleStatePacket(owner, ownerName, tax, siege, (flags & LeagueFlag) != 0, (flags & DoorDownFlag) != 0, (uint)left, crystals, top, reader.VarUInt());
    }
}

public enum CastleOwnerAction : byte
{
    Open = 0,
    Save = 1,
    Withdraw = 2
}

// C->S the owner's window at the castle's butler: {header, uint8 Action, varuint Npc, uint8 Tax, uint8 Skill, uint8 x6
// Towers (0 none, 1 ice, 2 lightning, 3 fire), uint8 x3 Mercenaries, varuint Amount (a withdrawal's gold)}.
public sealed record CastleOwnerRequestPacket(CastleOwnerAction Action, ulong Npc, byte Tax, byte Skill, IReadOnlyList<byte> Towers,
    IReadOnlyList<byte> Mercenaries, ulong Amount) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 1 + VarInt.MaxBytes64 + 2 + CastleLimits.TowerSlots + CastleLimits.MercenaryKinds + VarInt.MaxBytes64;

    public int Write(Span<byte> destination)
    {
        if (Towers.Count != CastleLimits.TowerSlots || Mercenaries.Count != CastleLimits.MercenaryKinds)
        {
            throw new InvalidOperationException("A castle setup has six towers and three kinds of mercenaries.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.CastleOwnerRequest);
        CastleWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.VarUInt(Npc);
        writer.Byte(Tax);
        writer.Byte(Skill);
        foreach (var tower in Towers)
        {
            writer.Byte(tower);
        }

        foreach (var count in Mercenaries)
        {
            writer.Byte(count);
        }

        writer.VarUInt(Amount);
        return writer.Finish();
    }

    public static CastleOwnerRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        CastleWire.ReadHeader(ref reader);
        var action = reader.Byte();
        if (!Enum.IsDefined((CastleOwnerAction)action))
        {
            throw new InvalidDataException("Castle owner request has an unknown action.");
        }

        var npc = reader.VarUInt();
        var (tax, skill) = (reader.Byte(), reader.Byte());
        var towers = new byte[CastleLimits.TowerSlots];
        for (var slot = 0; slot < towers.Length; slot++)
        {
            towers[slot] = reader.Byte();
        }

        var mercenaries = new byte[CastleLimits.MercenaryKinds];
        for (var kind = 0; kind < mercenaries.Length; kind++)
        {
            mercenaries[kind] = reader.Byte();
        }

        return new CastleOwnerRequestPacket((CastleOwnerAction)action, npc, tax, skill, towers, mercenaries, reader.VarUInt());
    }
}

// S->C the owner's window, answering an Open and every change: {header, uint8 HasView (0: the player's clan does not hold
// the castle), then varuint Treasury, uint8 Tax (in force), uint8 NextTax, uint8 Skill (chosen), uint8 SkillInForce,
// uint8 x6 Towers, uint8 x3 Mercenaries, uint8 CanEdit (the chief)}.
public sealed record CastleOwnerStatePacket(bool HasView, ulong Treasury, byte Tax, byte NextTax, byte Skill, byte SkillInForce, IReadOnlyList<byte> Towers,
    IReadOnlyList<byte> Mercenaries, bool CanEdit) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 1 + VarInt.MaxBytes64 + 4 + CastleLimits.TowerSlots + CastleLimits.MercenaryKinds + 1;

    public static readonly CastleOwnerStatePacket None = new(false, 0, 0, 0, 0, 0, new byte[CastleLimits.TowerSlots],
        new byte[CastleLimits.MercenaryKinds], false);

    public int Write(Span<byte> destination)
    {
        if (Towers.Count != CastleLimits.TowerSlots || Mercenaries.Count != CastleLimits.MercenaryKinds)
        {
            throw new InvalidOperationException("A castle setup has six towers and three kinds of mercenaries.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.CastleOwnerState);
        CastleWire.Header(ref writer);
        writer.Byte(HasView ? (byte)1 : (byte)0);
        if (HasView)
        {
            writer.VarUInt(Treasury);
            writer.Byte(Tax);
            writer.Byte(NextTax);
            writer.Byte(Skill);
            writer.Byte(SkillInForce);
            foreach (var tower in Towers)
            {
                writer.Byte(tower);
            }

            foreach (var count in Mercenaries)
            {
                writer.Byte(count);
            }

            writer.Byte(CanEdit ? (byte)1 : (byte)0);
        }

        return writer.Finish();
    }

    public static CastleOwnerStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        CastleWire.ReadHeader(ref reader);
        if (reader.Byte() == 0)
        {
            return None;
        }

        var treasury = reader.VarUInt();
        var (tax, nextTax, skill, inForce) = (reader.Byte(), reader.Byte(), reader.Byte(), reader.Byte());
        var towers = new byte[CastleLimits.TowerSlots];
        for (var slot = 0; slot < towers.Length; slot++)
        {
            towers[slot] = reader.Byte();
        }

        var mercenaries = new byte[CastleLimits.MercenaryKinds];
        for (var kind = 0; kind < mercenaries.Length; kind++)
        {
            mercenaries[kind] = reader.Byte();
        }

        return new CastleOwnerStatePacket(true, treasury, tax, nextTax, skill, inForce, towers, mercenaries, reader.Byte() != 0);
    }
}

// S->C how a CastleOwnerRequest ended: {header, uint8 Action, uint8 Status (CastleOwnerStatus, 0 done)}.
public readonly record struct CastleOwnerResultPacket(CastleOwnerAction Action, byte Status) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CastleOwnerResult);
        CastleWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.Byte(Status);
        return writer.Finish();
    }

    public static CastleOwnerResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        CastleWire.ReadHeader(ref reader);
        return new CastleOwnerResultPacket((CastleOwnerAction)reader.Byte(), reader.Byte());
    }
}
