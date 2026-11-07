using PristonTale.Application.Castle;
using PristonTale.Application.Sessions;
using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

// The castle side of a connection: while the player is in the world it is told Bless Castle (CastleState) at entry and
// on every change. One state's packet is made once and shared by every connection.
internal sealed partial class ClientSession
{
    private static CastlePacketOf s_castlePacket = new(CastleBroadcast.None, CastleStatePacket.None);

    private void JoinCastle(PlayerSession session) => _services.Castle.Attach(session.EntityId, new CastleLink(this));

    private void SendCastle(CastleBroadcast castle)
    {
        var made = Volatile.Read(ref s_castlePacket);
        if (!ReferenceEquals(made.Castle, castle))
        {
            made = new CastlePacketOf(castle, new CastleStatePacket((ulong)castle.OwnerClanId, castle.OwnerName, (byte)castle.Siege, castle.League,
                castle.DoorDown, (uint)Math.Clamp(castle.SecondsLeft, 0, uint.MaxValue), (byte)Math.Clamp(castle.Crystals, 0, byte.MaxValue),
                [.. castle.Top.Select(static clan => new CastleClanWire((ulong)clan.ClanId, clan.Name, (ulong)Math.Max(0, clan.Damage)))],
                (ulong)Math.Max(0, castle.TotalDamage)));
            Volatile.Write(ref s_castlePacket, made);
        }

        SendLarge(made.Packet.Write, CastleStatePacket.MaxSize);
    }

    private sealed record CastlePacketOf(CastleBroadcast Castle, CastleStatePacket Packet);

    private sealed class CastleLink(ClientSession owner) : ICastleLink
    {
        public void Deliver(CastleBroadcast castle) => owner.SendCastle(castle);
    }
}
