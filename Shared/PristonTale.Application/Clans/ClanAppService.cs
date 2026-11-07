using System.Collections.Concurrent;
using PristonTale.Application.Chat;
using PristonTale.Application.Saves;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Application.Clans;

public sealed record ClanMemberView(string Name, PlayerClass Class, int Level, ClanRank Rank, bool Online);

// A member's view of its clan; Deadline 0 when no dissolution is pending.
public sealed record ClanView(long ClanId, string Name, ClanState State, long DeadlineSeconds, ClanRank Rank, int Capacity, string Intro, int MarkVersion,
    IReadOnlyList<ClanMemberView> Members);

// What an invited player is asked: the clan, its chief and the recruiter who asked.
public sealed record ClanInvitation(ulong RecruiterId, string ClanName, string ChiefName);

// How the clan service reaches an online player: its clan as it is now (null without one) and an invitation.
public interface IClanLink
{
    void Deliver(ClanView? view);

    void Invite(ClanInvitation invitation);
}

// The clans of the server, kept in memory and stored before every change applies (a request storage refuses changes
// nothing). One change runs at a time; the chat reads ClanOf without waiting. A fee goes through the world's durable path
// with the clan row in the same transaction. Online members get their clan's view after each change, the clan's line in
// the chat, and their tag over the head.
public sealed class ClanAppService : IClanRoster
{
    private readonly IClanRepository _repository;
    private readonly WorldServer _world;
    private readonly ChatDirectory _chat;
    private readonly ISessionRegistry _sessions;
    private readonly TimeProvider _clock;
    private readonly DurableOperations _durable;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<long, Clan> _clans = new();
    private readonly Dictionary<long, Clan> _byCharacter = new();
    private readonly ConcurrentDictionary<ulong, Online> _online = new();
    private readonly ConcurrentDictionary<long, ulong> _entityOf = new();
    private readonly Dictionary<ulong, Pending> _invitations = new();

    public ClanAppService(IClanRepository repository, WorldServer world, ChatDirectory chat, ISessionRegistry sessions, ICharacterSaves saves,
        TimeProvider? clock = null)
    {
        _repository = repository;
        _world = world;
        _chat = chat;
        _sessions = sessions;
        _clock = clock ?? TimeProvider.System;
        _durable = new DurableOperations(saves);
    }

    public bool Available { get; private set; }

    private long Now => _clock.GetUtcNow().ToUnixTimeSeconds();

    public uint ClanOf(ulong entityId) => _online.TryGetValue(entityId, out var online) && online.ClanId != 0 ? (uint)online.ClanId : 0;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var (clans, members) = await _repository.LoadAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            _clans.Clear();
            _byCharacter.Clear();
            foreach (var record in clans)
            {
                _clans[record.Id] = new Clan(record.Id, record.Name, (ClanState)record.State, record.CreatedAt, record.DeadlineAt, record.Intro, record.MarkVersion);
            }

            foreach (var record in members)
            {
                if (_clans.TryGetValue(record.ClanId, out var clan))
                {
                    clan.Add(new ClanMember(record.CharacterId, record.AccountId, record.Name, (PlayerClass)record.JobCode, record.Level, (ClanRank)record.Rank,
                        record.JoinedAt));
                    _byCharacter[record.CharacterId] = clan;
                }
            }

            Available = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // A player entered the world: its clan's member row takes its name, class and level, and it gets its view and tag.
    public async Task AttachAsync(PlayerSession session, string name, PlayerClass playerClass, int level, IClanLink link)
    {
        if (session.Loaded is not { } loaded)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var clan = _byCharacter.GetValueOrDefault(loaded.Id);
            if (clan?.Find(loaded.Id) is { } member)
            {
                clan.Replace(member with { Name = name, Class = playerClass, Level = level, AccountId = session.AccountId });
            }

            _online[session.EntityId] = new Online(loaded.Id, session.AccountId, name, playerClass, level, link) { ClanId = clan?.Id ?? 0 };
            _entityOf[loaded.Id] = session.EntityId;
            await _world.SetClanTagAsync(session.EntityId, Tag(clan));
            link.Deliver(View(clan, loaded.Id));
            if (clan is not null)
            {
                Refresh(clan);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Detach(ulong entityId)
    {
        if (_online.TryRemove(entityId, out var online))
        {
            _entityOf.TryRemove(online.CharacterId, out _);
            _ = RefreshLaterAsync(online.CharacterId);
        }
    }

    // The requester's view again (the Clan Master's window opening).
    public ClanView? ViewOf(ulong entityId) =>
        _online.TryGetValue(entityId, out var online) && _byCharacter.TryGetValue(online.CharacterId, out var clan) ? View(clan, online.CharacterId) : null;

    // Founding at the Clan Master: not in a clan nor in a cooldown, no other chief on the account, a free valid name,
    // then the world takes the fee (level and reach checked there) and the clan row is stored with the character.
    public async Task<ClanStatus> FoundAsync(string sessionToken, ulong npcEntityId, string name)
    {
        name = (name ?? "").Trim();
        await _gate.WaitAsync();
        try
        {
            if (!_sessions.TryGet(sessionToken, out var session) || !_online.TryGetValue(session.EntityId, out var online))
            {
                return ClanStatus.Unavailable;
            }

            var refusal = !Available ? ClanStatus.Unavailable
                : _byCharacter.ContainsKey(online.CharacterId) ? ClanStatus.AlreadyInClan
                : !ClanRules.IsValidName(name) ? ClanStatus.NameInvalid
                : _clans.Values.Any(clan => clan.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? ClanStatus.NameTaken
                : _clans.Values.Any(clan => clan.Chief.AccountId == online.AccountId) ? ClanStatus.ChiefOnAccount
                : await CooldownAsync(online.CharacterId);
            if (refusal != ClanStatus.Ok)
            {
                return Tell(session.EntityId, refusal);
            }

            var now = Now;
            var status = ClanStatus.Ok;
            long id = 0;
            using var lease = await _durable.AdmitAsync(_sessions, sessionToken, CancellationToken.None);
            var outcome = await lease.RunAsync(
                s => _world.PrepareClanPaymentAsync(s, npcEntityId, ClanRules.FoundingCost, ClanRules.FoundingLevel),
                _world.CommitClanPaymentAsync,
                static prepared => prepared.Status,
                static _ => ClanStatus.Unavailable,
                async (s, prepared) =>
                {
                    id = await _repository.FoundAsync(new ClanRecord(0, name, (short)ClanState.Registering, now, now + ClanRules.GraceSeconds, "", 0),
                        new ClanMemberRecord(0, online.CharacterId, (short)ClanRank.Chief, now), s.Stamp(prepared.Snapshot!), CancellationToken.None);
                    status = id != 0 ? ClanStatus.Ok : ClanStatus.Unavailable;
                    return id != 0 ? null : DurableFailure.StorageFailed;
                });
            if (outcome != ClanStatus.Ok || status != ClanStatus.Ok)
            {
                return Tell(session.EntityId, outcome != ClanStatus.Ok ? outcome : status);
            }

            var founded = new Clan(id, name, ClanState.Registering, now, now + ClanRules.GraceSeconds);
            founded.Add(new ClanMember(online.CharacterId, online.AccountId, online.Name, online.Class, session.Character?.Level ?? online.Level, ClanRank.Chief,
                now));
            _clans[id] = founded;
            _byCharacter[online.CharacterId] = founded;
            await Retag(founded, online.CharacterId);
            Announce(founded, GameText.Of(GameTextId.ClanFounded, name));
            Refresh(founded);
            return ClanStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The chief or vice asks an online player without a clan; the answer comes through AnswerAsync.
    public async Task<ClanStatus> InviteAsync(ulong recruiterId, ulong targetId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!TryMember(recruiterId, out var online, out var clan, out var member))
            {
                return Tell(recruiterId, ClanStatus.NotInClan);
            }

            _online.TryGetValue(targetId, out var target);
            var refusal = !ClanRules.CanRecruit(member.Rank) ? ClanStatus.NotRecruiter
                : target is null || targetId == recruiterId ? ClanStatus.TargetNotFound
                : _byCharacter.ContainsKey(target.CharacterId) ? ClanStatus.TargetInClan
                : ClanRules.CheckRoom(clan);
            if (refusal == ClanStatus.Ok)
            {
                refusal = await CooldownAsync(target!.CharacterId);
            }

            if (refusal != ClanStatus.Ok)
            {
                return Tell(recruiterId, refusal);
            }

            _invitations[targetId] = new Pending(clan.Id, recruiterId, Now + ClanRules.InvitationSeconds);
            target!.Link.Invite(new ClanInvitation(recruiterId, clan.Name, clan.Chief.Name));
            Say(recruiterId, GameText.Of(GameTextId.ClanInviteSent, target.Name));
            return ClanStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The invited player's answer: a yes joins it (checked again), a no tells the recruiter.
    public async Task<ClanStatus> AnswerAsync(ulong entityId, bool accept)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_invitations.Remove(entityId, out var pending) || pending.ExpiresAt < Now || !_clans.TryGetValue(pending.ClanId, out var clan)
                || !_online.TryGetValue(entityId, out var online))
            {
                return Tell(entityId, ClanStatus.NoInvitation);
            }

            if (!accept)
            {
                Say(pending.RecruiterId, GameText.Of(GameTextId.ClanInviteRefused, online.Name));
                return ClanStatus.InviteRefused;
            }

            var refusal = _byCharacter.ContainsKey(online.CharacterId) ? ClanStatus.AlreadyInClan : ClanRules.CheckRoom(clan);
            if (refusal != ClanStatus.Ok)
            {
                return Tell(entityId, refusal);
            }

            var now = Now;
            var joined = new ClanMember(online.CharacterId, online.AccountId, online.Name, online.Class, online.Level, ClanRank.Member, now);
            var state = clan.State;
            var deadline = clan.DeadlineSeconds;
            clan.Add(joined);
            ClanRules.Recount(clan, now);
            if (!await StoreAsync(() => _repository.AddMemberAsync(new ClanMemberRecord(clan.Id, online.CharacterId, (short)ClanRank.Member, now),
                    (short)clan.State, clan.DeadlineSeconds)))
            {
                clan.Remove(online.CharacterId);
                (clan.State, clan.DeadlineSeconds) = (state, deadline);
                return Tell(entityId, ClanStatus.Unavailable);
            }

            _byCharacter[online.CharacterId] = clan;
            await Retag(clan, online.CharacterId);
            Announce(clan, GameText.Of(GameTextId.ClanJoined, online.Name));
            if (state == ClanState.Registering && clan.State == ClanState.Registered)
            {
                Announce(clan, GameText.Of(GameTextId.ClanRegistered, clan.Name));
            }

            Refresh(clan);
            return ClanStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ClanStatus> LeaveAsync(ulong entityId) => RunMemberAsync(entityId, async (online, clan, member) =>
        member.Rank == ClanRank.Chief ? ClanStatus.ChiefCannotLeave
        : await RemoveAsync(clan, member, GameText.Of(GameTextId.ClanLeft, member.Name), null));

    // The chief expels a member by name, online or not.
    public Task<ClanStatus> ExpelAsync(ulong entityId, string name) => RunMemberAsync(entityId, async (online, clan, member) =>
    {
        if (member.Rank != ClanRank.Chief)
        {
            return ClanStatus.NotChief;
        }

        return clan.Find((name ?? "").Trim()) is { Rank: not ClanRank.Chief } expelled
            ? await RemoveAsync(clan, expelled, GameText.Of(GameTextId.ClanExpelled, expelled.Name), GameText.Of(GameTextId.ClanYouWereExpelled, clan.Name))
            : ClanStatus.NotMember;
    });

    public Task<ClanStatus> DisbandAsync(ulong entityId) => RunMemberAsync(entityId, async (online, clan, member) =>
        member.Rank != ClanRank.Chief ? ClanStatus.NotChief : await DissolveAsync(clan) ? ClanStatus.Ok : ClanStatus.Unavailable);

    // The chief appoints a member vice chief (only one) or discharges the vice.
    public Task<ClanStatus> SetViceAsync(ulong entityId, string name, bool appoint) => RunMemberAsync(entityId, async (online, clan, member) =>
    {
        if (member.Rank != ClanRank.Chief)
        {
            return ClanStatus.NotChief;
        }

        var target = clan.Find((name ?? "").Trim());
        var refusal = target is null || target.Rank == ClanRank.Chief ? ClanStatus.NotMember
            : appoint && clan.Vice is not null ? ClanStatus.ViceExists
            : !appoint && target.Rank != ClanRank.Vice ? ClanStatus.NotVice
            : ClanStatus.Ok;
        if (refusal != ClanStatus.Ok)
        {
            return refusal;
        }

        var rank = appoint ? ClanRank.Vice : ClanRank.Member;
        if (!await StoreAsync(() => _repository.SetRanksAsync(clan.Id, [new ClanRankChange(target!.CharacterId, (short)rank)])))
        {
            return ClanStatus.Unavailable;
        }

        clan.Replace(target! with { Rank = rank });
        Announce(clan, GameText.Of(appoint ? GameTextId.ClanViceAppointed : GameTextId.ClanViceDischarged, target!.Name));
        Refresh(clan);
        return ClanStatus.Ok;
    });

    // The chief hands the clan to a member at the Clan Master for ClanRules.EntrustCost; the old chief becomes a member.
    public async Task<ClanStatus> EntrustAsync(string sessionToken, ulong npcEntityId, string name)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_sessions.TryGet(sessionToken, out var session) || !TryMember(session.EntityId, out _, out var clan, out var chief))
            {
                return ClanStatus.NotInClan;
            }

            var heir = clan.Find((name ?? "").Trim());
            var refusal = chief.Rank != ClanRank.Chief ? ClanStatus.NotChief
                : ClanRules.CheckEntrust(clan, chief, heir, session.Character?.Money ?? 0);
            if (refusal != ClanStatus.Ok)
            {
                return Tell(session.EntityId, refusal);
            }

            using var lease = await _durable.AdmitAsync(_sessions, sessionToken, CancellationToken.None);
            var outcome = await lease.RunAsync(
                s => _world.PrepareClanPaymentAsync(s, npcEntityId, ClanRules.EntrustCost, 0),
                _world.CommitClanPaymentAsync,
                static prepared => prepared.Status,
                static _ => ClanStatus.Unavailable,
                async (s, prepared) => await _repository.SetRanksAsync(clan.Id,
                    [new ClanRankChange(heir!.CharacterId, (short)ClanRank.Chief), new ClanRankChange(chief.CharacterId, (short)ClanRank.Member)],
                    s.Stamp(prepared.Snapshot!), CancellationToken.None) ? null : DurableFailure.StorageFailed);
            if (outcome != ClanStatus.Ok)
            {
                return Tell(session.EntityId, outcome);
            }

            clan.Replace(heir! with { Rank = ClanRank.Chief });
            clan.Replace(chief with { Rank = ClanRank.Member });
            Announce(clan, GameText.Of(GameTextId.ClanChiefChanged, chief.Name, heir!.Name));
            Refresh(clan);
            return ClanStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Dissolves the clans whose deadline passed (a registering clan short of six, or a registered one that stayed below).
    public async Task ExpireAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var now = Now;
            foreach (var clan in _clans.Values.Where(clan => ClanRules.Expired(clan, now)).ToArray())
            {
                await DissolveAsync(clan);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ClanStatus> RunMemberAsync(ulong entityId, Func<Online, Clan, ClanMember, Task<ClanStatus>> action)
    {
        await _gate.WaitAsync();
        try
        {
            var status = TryMember(entityId, out var online, out var clan, out var member) ? await action(online, clan, member) : ClanStatus.NotInClan;
            return status == ClanStatus.Ok ? status : Tell(entityId, status);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ClanStatus> RemoveAsync(Clan clan, ClanMember member, GameText line, GameText? toMember)
    {
        var state = clan.State;
        var deadline = clan.DeadlineSeconds;
        clan.Remove(member.CharacterId);
        ClanRules.Recount(clan, Now);
        if (!await StoreAsync(() => _repository.RemoveMemberAsync(clan.Id, member.CharacterId, (short)clan.State, clan.DeadlineSeconds)))
        {
            clan.Add(member);
            (clan.State, clan.DeadlineSeconds) = (state, deadline);
            return ClanStatus.Unavailable;
        }

        _byCharacter.Remove(member.CharacterId);
        await Retag(null, member.CharacterId);
        Announce(clan, line);
        if (_entityOf.TryGetValue(member.CharacterId, out var entityId))
        {
            Say(entityId, toMember ?? line);
        }

        if (deadline == 0 && clan.DeadlineSeconds != 0)
        {
            Announce(clan, GameText.Of(GameTextId.ClanBelowSix, (ClanRules.GraceSeconds / 86400).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Refresh(clan);
        return ClanStatus.Ok;
    }

    private async Task<bool> DissolveAsync(Clan clan)
    {
        var members = clan.Members.Select(static member => member.CharacterId).ToArray();
        if (!await StoreAsync(() => _repository.DisbandAsync(clan.Id, members, Now + ClanRules.JoinCooldownSeconds)))
        {
            return false;
        }

        Announce(clan, GameText.Of(GameTextId.ClanDisbanded, clan.Name));
        _clans.Remove(clan.Id);
        foreach (var characterId in members)
        {
            _byCharacter.Remove(characterId);
            await Retag(null, characterId);
        }

        return true;
    }

    private async Task<ClanStatus> CooldownAsync(long characterId)
    {
        try
        {
            return await _repository.CooldownAsync(characterId) > Now ? ClanStatus.JoinCooldown : ClanStatus.Ok;
        }
        catch (System.Data.Common.DbException)
        {
            return ClanStatus.Unavailable;
        }
    }

    private static async Task<bool> StoreAsync(Func<Task<bool>> write)
    {
        try
        {
            return await write();
        }
        catch (System.Data.Common.DbException)
        {
            return false;
        }
    }

    private bool TryMember(ulong entityId, out Online online, out Clan clan, out ClanMember member)
    {
        clan = null!;
        member = null!;
        if (!_online.TryGetValue(entityId, out online!) || !_byCharacter.TryGetValue(online.CharacterId, out var found) || found.Find(online.CharacterId) is not { } self)
        {
            return false;
        }

        (clan, member) = (found, self);
        return true;
    }

    // A member's online state, view and tag after its clan changed (a clan of null: it has none now).
    private async Task Retag(Clan? clan, long characterId)
    {
        if (!_entityOf.TryGetValue(characterId, out var entityId) || !_online.TryGetValue(entityId, out var online))
        {
            return;
        }

        online.ClanId = clan?.Id ?? 0;
        await _world.SetClanTagAsync(entityId, Tag(clan));
        if (clan is null)
        {
            online.Link.Deliver(null);
        }
    }

    // Every online member gets the clan's view as it is now.
    private void Refresh(Clan clan)
    {
        foreach (var member in clan.Members)
        {
            if (_entityOf.TryGetValue(member.CharacterId, out var entityId) && _online.TryGetValue(entityId, out var online))
            {
                online.Link.Deliver(View(clan, member.CharacterId));
            }
        }
    }

    // The clan of a member who left the world shows it offline.
    private async Task RefreshLaterAsync(long characterId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_byCharacter.TryGetValue(characterId, out var clan))
            {
                Refresh(clan);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private ClanView? View(Clan? clan, long characterId) =>
        clan?.Find(characterId) is not { } self ? null
            : new ClanView(clan.Id, clan.Name, clan.State, clan.DeadlineSeconds, self.Rank, clan.Capacity, clan.Intro, clan.MarkVersion,
                clan.Members.OrderByDescending(static member => member.Rank).ThenBy(static member => member.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(member => new ClanMemberView(member.Name, member.Class, member.Level, member.Rank, _entityOf.ContainsKey(member.CharacterId)))
                    .ToArray());

    private static ClanTag? Tag(Clan? clan) => clan is null ? null : new ClanTag(clan.Id, clan.Name, clan.MarkVersion);

    private void Announce(Clan clan, GameText line)
    {
        foreach (var member in clan.Members)
        {
            if (_entityOf.TryGetValue(member.CharacterId, out var entityId))
            {
                Say(entityId, line);
            }
        }
    }

    private void Say(ulong entityId, GameText line)
    {
        if (_chat.TryGet(entityId, out var member))
        {
            member.Link.Deliver(ChatLine.Reply(line));
        }
    }

    private ClanStatus Tell(ulong entityId, ClanStatus status)
    {
        Say(entityId, ClanTexts.Of(status));
        return status;
    }

    // An online player as it entered the world (its level is the one it joins a clan with).
    private sealed class Online(long characterId, long accountId, string name, PlayerClass playerClass, int level, IClanLink link)
    {
        public long CharacterId { get; } = characterId;
        public long AccountId { get; } = accountId;
        public string Name { get; } = name;
        public PlayerClass Class { get; } = playerClass;
        public int Level { get; } = level;
        public IClanLink Link { get; } = link;

        // Read by the chat without the gate.
        public long ClanId { get => Interlocked.Read(ref _clanId); set => Interlocked.Exchange(ref _clanId, value); }

        private long _clanId;
    }

    private readonly record struct Pending(long ClanId, ulong RecruiterId, long ExpiresAt);
}
