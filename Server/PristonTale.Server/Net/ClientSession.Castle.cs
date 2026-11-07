using Microsoft.Extensions.Logging;
using PristonTale.Application.Castle;
using PristonTale.Application.Sessions;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Castle;

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
            made = new CastlePacketOf(castle, new CastleStatePacket((ulong)castle.OwnerClanId, castle.OwnerName, (byte)castle.Tax, (byte)castle.Siege, castle.League,
                castle.DoorDown, (uint)Math.Clamp(castle.SecondsLeft, 0, uint.MaxValue), (byte)Math.Clamp(castle.Crystals, 0, byte.MaxValue),
                [.. castle.Top.Select(static clan => new CastleClanWire((ulong)clan.ClanId, clan.Name, (ulong)Math.Max(0, clan.Damage)))],
                (ulong)Math.Max(0, castle.TotalDamage)));
            Volatile.Write(ref s_castlePacket, made);
        }

        SendLarge(made.Packet.Write, CastleStatePacket.MaxSize);
    }

    // The owner's window at the castle's butler: every request is answered with its result and the window as it is then.
    private async Task CastleOwnerAsync(CastleOwnerRequestPacket request)
    {
        if (_player is null || _session is not { } session || _token is not { } token)
        {
            return;
        }

        var castle = _services.Castle;
        CastleOwnerStatus status;
        try
        {
            status = request.Action switch
            {
                CastleOwnerAction.Save => await castle.SaveSetupAsync(token, request.Npc, new CastleSetup(request.Tax, (CastleClanSkill)request.Skill,
                    [.. request.Towers.Select(static tower => (CastleTower)tower)], [.. request.Mercenaries.Select(static count => (int)count)])),
                CastleOwnerAction.Withdraw => await castle.WithdrawAsync(token, request.Npc, (long)Math.Min(request.Amount, long.MaxValue)),
                _ => castle.OwnerViewOf(session.EntityId) is null ? CastleOwnerStatus.NotOwner : CastleOwnerStatus.Ok
            };
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            _services.Logger.LogWarning("Castle owner request {Action} from {Remote} failed: {Message}", request.Action, _connection.RemoteEndPoint, ex.Message);
            status = CastleOwnerStatus.Unavailable;
        }

        SendLarge(new CastleOwnerResultPacket(request.Action, (byte)status).Write, CastleOwnerResultPacket.Size);
        var view = castle.OwnerViewOf(session.EntityId);
        var packet = view is null ? CastleOwnerStatePacket.None
            : new CastleOwnerStatePacket(true, (ulong)Math.Max(0, view.Treasury), (byte)view.Tax, (byte)view.Setup.Tax, (byte)view.Setup.Skill,
                (byte)view.SkillInForce, [.. view.Setup.Towers.Select(static tower => (byte)tower)],
                [.. view.Setup.Mercenaries.Select(static count => (byte)Math.Clamp(count, 0, byte.MaxValue))], view.CanEdit);
        SendLarge(packet.Write, CastleOwnerStatePacket.MaxSize);
    }

    private sealed record CastlePacketOf(CastleBroadcast Castle, CastleStatePacket Packet);

    private sealed class CastleLink(ClientSession owner) : ICastleLink
    {
        public void Deliver(CastleBroadcast castle) => owner.SendCastle(castle);
    }
}
