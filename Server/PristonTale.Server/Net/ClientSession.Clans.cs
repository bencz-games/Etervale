using Microsoft.Extensions.Logging;
using PristonTale.Application.Clans;
using PristonTale.Application.Sessions;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Server.Net;

// The clan side of a connection: requests go to the clan service, answered by a ClanResult; the service sends the clan's
// state and invitations through the link registered while the player is in the world.
internal sealed partial class ClientSession
{
    private ClanLink? _clanLink;

    private async Task JoinClansAsync(PlayerSession session)
    {
        if (session.Character is not { } character)
        {
            return;
        }

        var link = new ClanLink(this);
        _clanLink = link;
        await _services.Clans.AttachAsync(session, character.Name, character.Definition.Class, character.Level, link);
    }

    private void LeaveClans(PlayerSession session)
    {
        if (_clanLink is not null)
        {
            _services.Clans.Detach(session.EntityId);
            _clanLink = null;
        }
    }

    private async Task ClanRequestAsync(ClanRequestPacket request)
    {
        if (_player is null || _session is not { } session || _token is not { } token)
        {
            return;
        }

        var clans = _services.Clans;
        await AnswerClanAsync(request.Action, async () => request.Action switch
        {
            ClanAction.Open => Open(session),
            ClanAction.Found => await clans.FoundAsync(token, request.Id, request.Name),
            ClanAction.Invite => await clans.InviteAsync(session.EntityId, request.Id),
            ClanAction.Accept => await clans.AnswerAsync(session.EntityId, true),
            ClanAction.Refuse => await clans.AnswerAsync(session.EntityId, false),
            ClanAction.Leave => await clans.LeaveAsync(session.EntityId),
            ClanAction.Expel => await clans.ExpelAsync(session.EntityId, request.Name),
            ClanAction.Disband => await clans.DisbandAsync(session.EntityId),
            ClanAction.Appoint => await clans.SetViceAsync(session.EntityId, request.Name, true),
            ClanAction.Discharge => await clans.SetViceAsync(session.EntityId, request.Name, false),
            ClanAction.Entrust => await clans.EntrustAsync(token, request.Id, request.Name),
            _ => ClanStatus.Unavailable
        });
    }

    private async Task ClanMarkAsync(ClanMarkPacket mark)
    {
        if (_player is null || _session is not { } session)
        {
            return;
        }

        await AnswerClanAsync(ClanAction.Mark, () => _services.Clans.UploadMarkAsync(session.EntityId, mark.Pixels));
    }

    // Every request is answered: the window waits for its result.
    private async Task AnswerClanAsync(ClanAction action, Func<Task<ClanStatus>> run)
    {
        ClanStatus status;
        try
        {
            status = await run();
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            _services.Logger.LogWarning("Clan request {Action} from {Remote} failed: {Message}", action, _connection.RemoteEndPoint, ex.Message);
            status = ClanStatus.Unavailable;
        }

        SendLarge(new ClanResultPacket(action, (byte)status).Write, ClanResultPacket.Size);
    }

    // Opening the Clan Master's window sends the player's clan as it is now.
    private ClanStatus Open(PlayerSession session)
    {
        SendClan(_services.Clans.ViewOf(session.EntityId));
        return _services.Clans.Available ? ClanStatus.Ok : ClanStatus.Unavailable;
    }

    private void SendClan(ClanView? view)
    {
        var packet = view is null ? ClanStatePacket.None
            : new ClanStatePacket((ulong)view.ClanId, view.Name, (byte)view.State, view.DeadlineSeconds, (byte)view.Rank, view.Capacity, view.Intro,
                view.MarkVersion, view.Members.Select(static member => new ClanMemberWire(member.Name, member.Class, member.Level, (byte)member.Rank,
                    member.Online)).ToArray());
        SendLarge(packet.Write, ClanStatePacket.MaxSize);
    }

    private sealed class ClanLink(ClientSession owner) : IClanLink
    {
        public void Deliver(ClanView? view) => owner.SendClan(view);

        public void Invite(ClanInvitation invitation) =>
            owner.SendLarge(new ClanInvitePacket(invitation.RecruiterId, invitation.ClanName, invitation.ChiefName).Write, ClanInvitePacket.MaxSize);
    }
}
