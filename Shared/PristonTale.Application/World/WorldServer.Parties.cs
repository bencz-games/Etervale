using System.Collections.Concurrent;
using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Parties;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// Parties live in memory only, as the original keeps them: leaving the world leaves the party. Every change runs on the
// tick's command phase and answers the lines its players receive (WorldNotice). What the members see is rebuilt after
// each change and every PartyRefreshTicks, before the fields step.
public sealed partial class WorldServer
{
    private const uint PartyRefreshTicks = 10;
    private readonly HashSet<Party> _parties = new();
    private readonly PartyMemberView[] _partyScratch = new PartyMemberView[PartyLimits.MaxMembers];

    // Who hears a party line.
    public PartyRoster Parties { get; } = new();

    public Task<WorldNotice[]> InviteToPartyAsync(PlayerSession session, ulong targetId) =>
        EnqueueAsync(session.EntityId, () => PartyAction(session, player =>
        {
            if (_connections.TryGetValue(targetId, out var target))
            {
                Invite(player, target.Player);
            }
        }));

    // The original's "//party name" command.
    public Task<WorldNotice[]> InviteToPartyByNameAsync(PlayerSession session, string name) =>
        EnqueueAsync(session.EntityId, () => PartyAction(session, player =>
        {
            if (PlayerNamed(name) is { } target)
            {
                Invite(player, target);
            }
            else
            {
                Reply(player, PartyMessages.NotFound(name));
            }
        }));

    public Task<WorldNotice[]> AnswerPartyAsync(PlayerSession session, ulong fromId, bool accept) =>
        EnqueueAsync(session.EntityId, () => PartyAction(session, player => Answer(player, fromId, accept)));

    public Task<WorldNotice[]> CommandPartyAsync(PlayerSession session, PartyCommand command, ulong memberId) =>
        EnqueueAsync(session.EntityId, () => PartyAction(session, player => Command(player, command, memberId)));

    private WorldNotice[] PartyAction(PlayerSession session, Action<WorldPlayer> action) => NoticeAction(session, connection => action(connection.Player));

    private WorldPlayer? PlayerNamed(string name)
    {
        foreach (var connection in _connections.Values)
        {
            if (string.Equals(connection.Player.Character.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return connection.Player;
            }
        }

        return null;
    }

    // A party's average level stands for each of its members. A player alone, or asked by a party's leader, is asked
    // directly; a leader asked by a player alone answers for its party; a member's request goes to its leader.
    private void Invite(WorldPlayer inviter, WorldPlayer target)
    {
        if (ReferenceEquals(inviter, target))
        {
            return;
        }

        if (!PartyRules.LevelsMatch(inviter.Party?.AverageLevel ?? inviter.Level, target.Party?.AverageLevel ?? target.Level))
        {
            Reply(inviter, PartyMessages.LevelGap);
            return;
        }

        var expires = UnixNowMs + PartyRules.InviteMs;
        if (target.Party is null || inviter.Party is null && target.Party.LeaderId == target.EntityId)
        {
            if (inviter.Party is { } own && own.LeaderId != inviter.EntityId && _connections.TryGetValue(own.LeaderId, out var leader))
            {
                Reply(inviter, PartyMessages.Recommended(leader.Player.Character.Name, target.Character.Name));
                leader.Player.PartyRequest = new PartyRequest(inviter.EntityId, inviter.Character.Name, target.EntityId, target.Character.Name, expires);
                return;
            }

            Reply(inviter, PartyMessages.Requested(target.Character.Name));
            target.PartyRequest = new PartyRequest(inviter.EntityId, inviter.Character.Name, inviter.EntityId, "", expires);
            return;
        }

        var name = target.Character.Name;
        Reply(inviter, inviter.SharesPartyWith(target) ? PartyMessages.AlreadyMember(name) : PartyMessages.InOtherParty(name));
    }

    // A leader who accepts a member's recommendation asks the recommended player in the party's name.
    private void Answer(WorldPlayer player, ulong fromId, bool accept)
    {
        if (player.PartyRequest is not { } request || request.FromId != fromId)
        {
            return;
        }

        player.PartyRequest = null;
        if (!accept || request.ExpiresMs < UnixNowMs)
        {
            return;
        }

        if (request.Recommendation)
        {
            if (player.Party is { } led && led.LeaderId == player.EntityId && _connections.TryGetValue(request.JoinerId, out var recommended))
            {
                Invite(player, recommended.Player);
            }

            return;
        }

        if (!_connections.TryGetValue(request.FromId, out var from))
        {
            return;
        }

        // The request was made before the answer; a party it would join must still be led by the one who asked or answers,
        // and the levels must still match.
        var inviter = from.Player;
        if (inviter.Party is { } theirs && theirs.LeaderId != inviter.EntityId || player.Party is { } answering && answering.LeaderId != player.EntityId)
        {
            return;
        }

        if (!PartyRules.LevelsMatch(inviter.Party?.AverageLevel ?? inviter.Level, player.Party?.AverageLevel ?? player.Level))
        {
            Reply(player, PartyMessages.LevelGap);
            return;
        }

        if (inviter.Party is { } theirParty)
        {
            Join(theirParty, player);
        }
        else if (player.Party is { } mine)
        {
            Join(mine, inviter);
        }
        else
        {
            var party = new Party(inviter.EntityId, player.EntityId);
            _parties.Add(party);
            inviter.Party = party;
            player.Party = party;
            PartyChanged(party);
        }
    }

    private void Join(Party party, WorldPlayer player)
    {
        if (player.Party is not null)
        {
            return;
        }

        if (party.IsFull)
        {
            Reply(player, PartyMessages.CannotEnter);
            return;
        }

        party.Add(player.EntityId);
        player.Party = party;
        PartyChanged(party);
        Tell(party, PartyMessages.Entered(player.Character.Name));
    }

    private void Command(WorldPlayer player, PartyCommand command, ulong memberId)
    {
        if (player.Party is not { } party)
        {
            return;
        }

        var leads = party.LeaderId == player.EntityId;
        switch (command)
        {
            case PartyCommand.Delegate when leads && memberId != player.EntityId && party.Contains(memberId)
                && _connections.TryGetValue(memberId, out var heir):
                HandOver(party, heir.Player);
                Reply(player, PartyMessages.Delegated(heir.Player.Character.Name));
                break;
            case PartyCommand.Disband when leads:
                Release(party);
                Reply(player, PartyMessages.Disbanded);
                break;
            case PartyCommand.Kick when leads && memberId != player.EntityId && party.Contains(memberId) && _connections.TryGetValue(memberId, out var kicked):
                LeaveParty(kicked.Player);
                var expelled = PartyMessages.Expelled(kicked.Player.Character.Name);
                if (player.Party is { } remaining)
                {
                    Tell(remaining, expelled);
                }
                else
                {
                    Reply(player, expelled);
                }

                break;
            case PartyCommand.Leave:
                LeaveParty(player);
                Reply(player, PartyMessages.YouLeft);
                break;
        }
    }

    // A leader who leaves hands the party to the next member first; a party of two is released.
    private void LeaveParty(WorldPlayer player)
    {
        if (player.Party is not { } party)
        {
            return;
        }

        if (party.LeaderId == player.EntityId)
        {
            foreach (var id in party.Members)
            {
                if (id != player.EntityId && _connections.TryGetValue(id, out var heir))
                {
                    HandOver(party, heir.Player);
                    break;
                }
            }
        }

        Tell(party, PartyMessages.Left(player.Character.Name));
        if (party.Count <= 2)
        {
            Release(party);
            return;
        }

        party.Remove(player.EntityId);
        player.Party = null;
        Parties.Remove(player.EntityId);
        PartyChanged(party);
    }

    private void HandOver(Party party, WorldPlayer heir)
    {
        party.Lead(heir.EntityId);
        PartyChanged(party);
        Tell(party, PartyMessages.LeaderNow(heir.Character.Name));
    }

    private void Release(Party party)
    {
        Tell(party, PartyMessages.Released);
        foreach (var id in party.Members)
        {
            if (_connections.TryGetValue(id, out var member) && ReferenceEquals(member.Player.Party, party))
            {
                member.Player.Party = null;
            }

            Parties.Remove(id);
        }

        _parties.Remove(party);
    }

    private void PartyChanged(Party party)
    {
        Parties.Set(party);
        RefreshParty(party);
    }

    private void Tell(Party party, GameText text)
    {
        foreach (var id in party.Members)
        {
            Notify(id, ChatKind.System, text);
        }
    }

    // Tick only, before the fields step: what the members see, and the average level their kills and requests use.
    private void RefreshParties(uint serverTick)
    {
        if (serverTick % PartyRefreshTicks != 0)
        {
            return;
        }

        foreach (var party in _parties)
        {
            RefreshParty(party);
        }
    }

    private void RefreshParty(Party party)
    {
        Span<int> levels = stackalloc int[PartyLimits.MaxMembers];
        var count = 0;
        foreach (var id in party.Members)
        {
            if (!_connections.TryGetValue(id, out var connection))
            {
                continue;
            }

            var player = connection.Player;
            _partyScratch[count] = new PartyMemberView(id, player.Character.Name, player.Level, player.Life, player.MaxLife, player.Position.X,
                player.Position.Z);
            levels[count++] = player.Level;
        }

        party.AverageLevel = PartyRules.AverageLevel(levels[..count]);
        if (party.View is null || !party.View.SameAs(_partyScratch.AsSpan(0, count)))
        {
            party.View = new PartyView(_partyScratch.AsSpan(0, count).ToArray());
        }
    }
}

// Who hears a party line: the chat reads it from any thread, the tick keeps it.
public sealed class PartyRoster : IPartyRoster
{
    private readonly ConcurrentDictionary<ulong, ulong[]> _members = new();

    public IReadOnlyCollection<ulong> MembersOf(ulong entityId) => _members.TryGetValue(entityId, out var members) ? members : [];

    internal void Set(Party party)
    {
        var members = party.Members.ToArray();
        foreach (var id in members)
        {
            _members[id] = members;
        }
    }

    internal void Remove(ulong entityId) => _members.TryRemove(entityId, out _);
}
