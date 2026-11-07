using PristonTale.Application.Bellatra;
using PristonTale.Protocol.Packets;

using PristonTale.Contracts.Social;

namespace PristonTale.Server.Net;

internal sealed partial class ClientSession
{
    // Bellatra's secretary: every request is answered with its result and the window as it is then.
    private async Task BellatraClanAsync(BellatraClanRequestPacket request)
    {
        if (_player is null || _session is not { } session || _token is not { } token)
        {
            return;
        }

        var clans = _services.BellatraClans;
        var status = request.Action == BellatraClanAction.Redeem
            ? await clans.RedeemAsync(token, request.Npc, (long)Math.Min(request.Amount, long.MaxValue))
            : BellatraClanStatus.Ok;
        var view = await clans.ViewAsync(session.EntityId);
        var answered = request.Action == BellatraClanAction.Open && view is null ? BellatraClanStatus.Unavailable : status;
        SendLarge(new BellatraClanResultPacket(request.Action, (byte)answered).Write, BellatraClanResultPacket.Size);
        var packet = view is null ? BellatraClanStatePacket.None
            : new BellatraClanStatePacket(true, (ulong)view.OperatingClanId, view.OperatingName, view.OperatingChief, (byte)Math.Clamp(view.Tax, 0, 100),
                (ulong)Math.Max(0, view.Balance), view.CanRedeem, [.. view.Top.Select(static clan => new BellatraClanRankWire(clan.Name, (ulong)Math.Max(0, clan.Points)))]);
        SendLarge(packet.Write, BellatraClanStatePacket.MaxSize);
    }
}
