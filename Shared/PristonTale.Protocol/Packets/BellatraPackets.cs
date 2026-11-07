using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class BellatraWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Bellatra message");
}

public enum BellatraClanAction : byte
{
    Open = 0,
    Redeem = 1
}

// C->S Bellatra's secretary: {header, uint8 Action, varuint Npc, varuint Amount (a redemption's gold)}.
public readonly record struct BellatraClanRequestPacket(BellatraClanAction Action, ulong Npc, ulong Amount) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 1 + 2 * VarInt.MaxBytes64;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.BellatraClanRequest);
        BellatraWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.VarUInt(Npc);
        writer.VarUInt(Amount);
        return writer.Finish();
    }

    public static BellatraClanRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        BellatraWire.ReadHeader(ref reader);
        var action = reader.Byte();
        if (!Enum.IsDefined((BellatraClanAction)action))
        {
            throw new InvalidDataException("Bellatra request has an unknown action.");
        }

        return new BellatraClanRequestPacket((BellatraClanAction)action, reader.VarUInt(), reader.VarUInt());
    }
}

public readonly record struct BellatraClanRankWire(string Name, ulong Points);

// S->C the secretary's window, answering an Open and every redemption: {header, uint8 HasView (0: not available), then
// varuint OperatingClanId (0 none), text OperatingName, text Chief, uint8 Tax, varuint Balance (the player's clan's
// income), uint8 CanRedeem (its chief), uint8 Count (at most BellatraLimits.TopClans), {text Name, varuint Points} x
// Count (yesterday's best clans)}.
public sealed record BellatraClanStatePacket(bool HasView, ulong OperatingClanId, string OperatingName, string Chief, byte Tax, ulong Balance, bool CanRedeem,
    IReadOnlyList<BellatraClanRankWire> Top) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 1 + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 2 + ClanWire.MaxNameBytes + 1
        + VarInt.MaxBytes64 + 2 + BellatraLimits.TopClans * (2 + ClanWire.MaxClanNameBytes + VarInt.MaxBytes64);

    public static readonly BellatraClanStatePacket None = new(false, 0, "", "", 0, 0, false, []);

    public int Write(Span<byte> destination)
    {
        if (Top.Count > BellatraLimits.TopClans)
        {
            throw new InvalidOperationException("Bellatra's ranking holds at most ten clans.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.BellatraClanState);
        BellatraWire.Header(ref writer);
        writer.Byte(HasView ? (byte)1 : (byte)0);
        if (HasView)
        {
            writer.VarUInt(OperatingClanId);
            Utf8Text.Write(ref writer, OperatingName, ClanWire.MaxClanNameBytes);
            Utf8Text.Write(ref writer, Chief, ClanWire.MaxNameBytes);
            writer.Byte(Tax);
            writer.VarUInt(Balance);
            writer.Byte(CanRedeem ? (byte)1 : (byte)0);
            writer.Byte((byte)Top.Count);
            foreach (var clan in Top)
            {
                Utf8Text.Write(ref writer, clan.Name, ClanWire.MaxClanNameBytes);
                writer.VarUInt(clan.Points);
            }
        }

        return writer.Finish();
    }

    public static BellatraClanStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        BellatraWire.ReadHeader(ref reader);
        if (reader.Byte() == 0)
        {
            return None;
        }

        var operating = reader.VarUInt();
        var name = ClanWire.ClanName(ref reader);
        var chief = ClanWire.Name(ref reader);
        var (tax, balance, canRedeem) = (reader.Byte(), reader.VarUInt(), reader.Byte() != 0);
        var count = reader.Byte();
        if (count > BellatraLimits.TopClans)
        {
            throw new InvalidDataException("Bellatra's ranking holds too many clans.");
        }

        var top = new BellatraClanRankWire[count];
        for (var index = 0; index < count; index++)
        {
            top[index] = new BellatraClanRankWire(ClanWire.ClanName(ref reader), reader.VarUInt());
        }

        return new BellatraClanStatePacket(true, operating, name, chief, tax, balance, canRedeem, top);
    }
}

// S->C how a BellatraClanRequest ended: {header, uint8 Action, uint8 Status (0 done)}.
public readonly record struct BellatraClanResultPacket(BellatraClanAction Action, byte Status) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 2;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.BellatraClanResult);
        BellatraWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.Byte(Status);
        return writer.Finish();
    }

    public static BellatraClanResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        BellatraWire.ReadHeader(ref reader);
        return new BellatraClanResultPacket((BellatraClanAction)reader.Byte(), reader.Byte());
    }
}

// S->C a Bellatra entrant's board while it is in the arena: {header, uint8 Shown (0 hides it), then uint8 Round (0
// between rounds), uint8 Team (0 to 3), uint8 Outcome (0 playing, 1 the team goes on, 2 the team is out, 3 the game is
// over), varuint Score, varuint x BellatraLimits.Teams TeamScores}.
public sealed record BellatraBoardPacket(bool Shown, byte Round, byte Team, byte Outcome, ulong Score, IReadOnlyList<ulong> TeamScores) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 4 + (1 + BellatraLimits.Teams) * VarInt.MaxBytes64;

    public static readonly BellatraBoardPacket Hidden = new(false, 0, 0, 0, 0, new ulong[BellatraLimits.Teams]);

    public int Write(Span<byte> destination)
    {
        if (TeamScores.Count != BellatraLimits.Teams)
        {
            throw new InvalidOperationException("Bellatra's board has four teams.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.BellatraBoard);
        BellatraWire.Header(ref writer);
        writer.Byte(Shown ? (byte)1 : (byte)0);
        if (Shown)
        {
            writer.Byte(Round);
            writer.Byte(Team);
            writer.Byte(Outcome);
            writer.VarUInt(Score);
            foreach (var score in TeamScores)
            {
                writer.VarUInt(score);
            }
        }

        return writer.Finish();
    }

    public static BellatraBoardPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        BellatraWire.ReadHeader(ref reader);
        if (reader.Byte() == 0)
        {
            return Hidden;
        }

        var (round, team, outcome, score) = (reader.Byte(), reader.Byte(), reader.Byte(), reader.VarUInt());
        var teams = new ulong[BellatraLimits.Teams];
        for (var index = 0; index < teams.Length; index++)
        {
            teams[index] = reader.VarUInt();
        }

        return new BellatraBoardPacket(true, round, team, outcome, score, teams);
    }
}
