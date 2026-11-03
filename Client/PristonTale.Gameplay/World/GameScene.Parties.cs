using Microsoft.Xna.Framework;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Parties;

namespace PristonTale.Gameplay.World;

// Party requests through the world connection, and the other player a click approached: once reached, its portrait shows
// the trade, party and friend buttons while it stays the target within TradeLimits.Reach, as the original keeps them.
public sealed partial class GameScene : IPartyClient
{
    private enum ApproachKind
    {
        Npc,
        Seller,
        Player
    }

    // The player whose portrait shows the buttons; 0 for none.
    public ulong ReachedPlayerId { get; private set; }

    public void DismissPlayerButtons() => ReachedPlayerId = 0;

    public Task InviteToPartyAsync(ulong targetId) => SendSocial(_playChannel?.InviteToPartyAsync(targetId, _presenceCts.Token), "party invite");

    public Task AnswerPartyAsync(ulong fromId, bool accept) => SendSocial(_playChannel?.AnswerPartyAsync(fromId, accept, _presenceCts.Token), "party answer");

    public Task CommandPartyAsync(PartyCommand command, ulong memberId) =>
        SendSocial(_playChannel?.CommandPartyAsync(command, memberId, _presenceCts.Token), "party command");

    private Task SendSocial(Task send, string what)
    {
        if (send is not null)
        {
            SendUpstream(send, what);
        }

        return Task.CompletedTask;
    }

    private void UpdateReachedPlayer()
    {
        if (ReachedPlayerId != 0 && (SelectedTargetId != ReachedPlayerId || _remote is null || !_remote.TryGetTarget(ReachedPlayerId, out var position, out _)
            || !TradeLimits.InReach(position.ToNumerics(), PlayerPosition.ToNumerics())))
        {
            ReachedPlayerId = 0;
        }
    }
}
