using System.Collections.Concurrent;
using System.Data.Common;
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

// The clans of the server, kept in memory. Changes run one at a time under the gate and apply only once stored (a
// request storage refuses changes nothing); the gate is held across storage, never by world entry, departure or the
// window's view. Clans and the character index change under the state lock, held briefly and never across a wait, so
// those readers take it instead. A fee goes through the world's durable path with the clan row in the same transaction.
// An online member's level is its character's live level. Online members get their clan's view after each change, the
// clan's line in the chat, and their tag over the head.
public sealed class ClanAppService : IClanDirectory
{
    private readonly IClanRepository _repository;
    private readonly WorldServer _world;
    private readonly ChatDirectory _chat;
    private readonly ISessionRegistry _sessions;
    private readonly TimeProvider _clock;
    private readonly DurableOperations _durable;
    private readonly IClanMarkStore _marks;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _state = new();
    private readonly Dictionary<long, Clan> _clans = new();
    private readonly Dictionary<long, Clan> _byCharacter = new();
    private readonly ConcurrentDictionary<ulong, Online> _online = new();
    private readonly ConcurrentDictionary<long, ulong> _entityOf = new();
    private readonly ConcurrentDictionary<ulong, Pending> _invitations = new();

    public ClanAppService(IClanRepository repository, WorldServer world, ChatDirectory chat, ISessionRegistry sessions, ICharacterSaves saves,
        TimeProvider? clock = null, IClanMarkStore? marks = null)
    {
        _marks = marks ?? NoClanMarkStore.Instance;
        _repository = repository;
        _world = world;
        _chat = chat;
        _sessions = sessions;
        _clock = clock ?? TimeProvider.System;
        _durable = new DurableOperations(saves);
    }

    public bool Available { get; private set; }

    private long Now => _clock.GetUtcNow().ToUnixTimeSeconds();

    public long ClanOf(ulong entityId) => _online.TryGetValue(entityId, out var online) ? online.ClanId : 0;

    public string? NameOf(long clanId)
    {
        lock (_state)
        {
            return _clans.TryGetValue(clanId, out var clan) ? clan.Name : null;
        }
    }

    public string? ChiefNameOf(long clanId)
    {
        lock (_state)
        {
            return _clans.TryGetValue(clanId, out var clan) ? clan.Chief.Name : null;
        }
    }

    public long IdOf(string name)
    {
        lock (_state)
        {
            return _clans.Values.FirstOrDefault(clan => clan.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
        }
    }

    public ClanRank? RankOf(ulong entityId)
    {
        lock (_state)
        {
            return _online.TryGetValue(entityId, out var online) && _byCharacter.TryGetValue(online.CharacterId, out var clan)
                ? clan.Find(online.CharacterId)?.Rank
                : null;
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var (clans, members) = await _repository.LoadAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            lock (_state)
            {
                _clans.Clear();
                _byCharacter.Clear();
                foreach (var record in clans)
                {
                    _clans[record.Id] = new Clan(record.Id, record.Name, (ClanState)record.State, record.CreatedAt, record.DeadlineAt, record.Intro,
                        record.MarkVersion);
                }

                foreach (var record in members)
                {
                    if (_clans.TryGetValue(record.ClanId, out var clan))
                    {
                        clan.Add(new ClanMember(record.CharacterId, record.AccountId, record.Name, (PlayerClass)record.JobCode, record.Level,
                            (ClanRank)record.Rank, record.JoinedAt));
                        _byCharacter[record.CharacterId] = clan;
                    }
                }
            }

            Available = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // A player entered the world: it gets its clan's view and tag, and its clan sees it online.
    public async Task AttachAsync(PlayerSession session, string name, PlayerClass playerClass, int level, IClanLink link)
    {
        if (session.Loaded is not { } loaded)
        {
            return;
        }

        var online = new Online(loaded.Id, session.AccountId, name, playerClass, level, session, link);
        Clan? clan;
        lock (_state)
        {
            clan = _byCharacter.GetValueOrDefault(loaded.Id);
            online.ClanId = clan?.Id ?? 0;
            _online[session.EntityId] = online;
            _entityOf[loaded.Id] = session.EntityId;
        }

        await ShowTagAsync(session.EntityId, clan);
        Clan? current;
        lock (_state)
        {
            current = _byCharacter.GetValueOrDefault(loaded.Id);
            if (current is null)
            {
                link.Deliver(null);
            }
            else
            {
                Refresh(current);
            }
        }

        // A change that ran while the tag was being shown already retagged the player; show the clan it left.
        if (current != clan)
        {
            await ShowTagAsync(session.EntityId, current);
        }
    }

    public void Detach(ulong entityId)
    {
        if (!_online.TryRemove(entityId, out var online))
        {
            return;
        }

        _entityOf.TryRemove(new KeyValuePair<long, ulong>(online.CharacterId, entityId));
        _invitations.TryRemove(entityId, out _);
        lock (_state)
        {
            if (_byCharacter.TryGetValue(online.CharacterId, out var clan))
            {
                Refresh(clan);
            }
        }
    }

    // The requester's view again (the Clan Master's window opening).
    public ClanView? ViewOf(ulong entityId)
    {
        lock (_state)
        {
            return _online.TryGetValue(entityId, out var online) && _byCharacter.TryGetValue(online.CharacterId, out var clan) ? View(clan, online.CharacterId) : null;
        }
    }

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
                : IdOf(name) != 0 ? ClanStatus.NameTaken
                : ChiefOnAccount(online.AccountId) ? ClanStatus.ChiefOnAccount
                : await CooldownAsync(online.CharacterId);
            if (refusal != ClanStatus.Ok)
            {
                return Tell(session.EntityId, refusal);
            }

            var now = Now;
            long id = 0;
            var outcome = await PayAsync(sessionToken, npcEntityId, ClanRules.FoundingCost, ClanRules.FoundingLevel, async (s, prepared) =>
            {
                id = await _repository.FoundAsync(new ClanRecord(0, name, (short)ClanState.Registering, now, now + ClanRules.GraceSeconds, "", 0),
                    new ClanMemberRecord(0, online.CharacterId, (short)ClanRank.Chief, now), s.Stamp(prepared.Snapshot!), CancellationToken.None);
                return id != 0;
            });
            if (outcome != ClanStatus.Ok)
            {
                return Tell(session.EntityId, outcome);
            }

            var founded = new Clan(id, name, ClanState.Registering, now, now + ClanRules.GraceSeconds);
            founded.Add(new ClanMember(online.CharacterId, online.AccountId, online.Name, online.Class, online.Level, ClanRank.Chief, now));
            lock (_state)
            {
                _clans[id] = founded;
                _byCharacter[online.CharacterId] = founded;
            }

            await Retag(founded, online.CharacterId);
            Announce(founded, GameText.Of(GameTextId.ClanFounded, name, ClanTexts.Number(ClanRules.RegisteredMembers), ClanTexts.Number(ClanRules.GraceDays)));
            Publish(founded);
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

            _invitations[targetId] = new Pending(clan.Id, recruiterId, online.CharacterId, Now + ClanRules.InvitationSeconds);
            target!.Link.Invite(new ClanInvitation(recruiterId, clan.Name, clan.Chief.Name));
            Say(recruiterId, GameText.Of(GameTextId.ClanInviteSent, target.Name));
            return ClanStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The invited player's answer: a yes joins it (checked again, the recruiter still recruiting), a no tells the recruiter.
    public async Task<ClanStatus> AnswerAsync(ulong entityId, bool accept)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_invitations.TryRemove(entityId, out var pending) || pending.ExpiresAt < Now || !_clans.TryGetValue(pending.ClanId, out var clan)
                || !_online.TryGetValue(entityId, out var online))
            {
                return Tell(entityId, ClanStatus.NoInvitation);
            }

            if (!accept)
            {
                Say(pending.RecruiterId, GameText.Of(GameTextId.ClanInviteRefused, online.Name));
                return ClanStatus.InviteRefused;
            }

            Sync(clan);
            var refusal = clan.Find(pending.RecruiterCharacterId) is not { } recruiter || !ClanRules.CanRecruit(recruiter.Rank) ? ClanStatus.NoInvitation
                : _byCharacter.ContainsKey(online.CharacterId) ? ClanStatus.AlreadyInClan
                : ClanRules.CheckRoom(clan);
            if (refusal != ClanStatus.Ok)
            {
                return Tell(entityId, refusal);
            }

            var now = Now;
            var wasState = clan.State;
            var (state, deadline) = ClanRules.Counted(clan, clan.Count + 1, now);
            if (!await StoreAsync(() => _repository.AddMemberAsync(new ClanMemberRecord(clan.Id, online.CharacterId, (short)ClanRank.Member, now),
                    (short)state, deadline)))
            {
                return Tell(entityId, ClanStatus.Unavailable);
            }

            lock (_state)
            {
                clan.Add(new ClanMember(online.CharacterId, online.AccountId, online.Name, online.Class, online.Level, ClanRank.Member, now));
                (clan.State, clan.DeadlineSeconds) = (state, deadline);
                _byCharacter[online.CharacterId] = clan;
            }

            Announce(clan, GameText.Of(GameTextId.ClanJoined, online.Name));
            if (wasState == ClanState.Registering && state == ClanState.Registered)
            {
                // Every member's tag now carries the registration.
                await Task.WhenAll(clan.Members.Select(member => Retag(clan, member.CharacterId)));
                Announce(clan, GameText.Of(GameTextId.ClanRegistered, clan.Name));
            }
            else
            {
                await Retag(clan, online.CharacterId);
            }

            Publish(clan);
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

        lock (_state)
        {
            clan.Replace(target! with { Rank = rank });
        }

        Announce(clan, GameText.Of(appoint ? GameTextId.ClanViceAppointed : GameTextId.ClanViceDischarged, target!.Name));
        Publish(clan);
        return ClanStatus.Ok;
    });

    // The chief's new mark: kept by the mark store as the clan's next mark version, which is then stored with the clan and
    // shown over its online members' heads.
    public Task<ClanStatus> UploadMarkAsync(ulong entityId, ReadOnlyMemory<byte> pixels) => RunMemberAsync(entityId, async (online, clan, member) =>
    {
        if (member.Rank != ClanRank.Chief)
        {
            return ClanStatus.NotChief;
        }

        var version = clan.MarkVersion + 1;
        if (!await StoreAsync(() => _marks.SaveAsync(clan.Id, version, pixels)))
        {
            return ClanStatus.MarkUnavailable;
        }

        if (!await StoreAsync(() => _repository.SetMarkVersionAsync(clan.Id, version)))
        {
            return ClanStatus.Unavailable;
        }

        lock (_state)
        {
            clan.MarkVersion = version;
        }

        await Task.WhenAll(clan.Members.Select(clanMember => Retag(clan, clanMember.CharacterId)));
        Announce(clan, GameText.Of(GameTextId.ClanMarkChanged, clan.Name));
        Publish(clan);
        return ClanStatus.Ok;
    });

    // The chief hands the clan to a member at the Clan Master for ClanRules.EntrustCost; the old chief becomes a member.
    // The heir's account must hold no other clan's chief.
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
            var refusal = chief.Rank != ClanRank.Chief ? ClanStatus.NotChief : ClanRules.CheckEntrust(clan, chief, heir, session.Character?.Money ?? 0);
            if (refusal == ClanStatus.Ok && ChiefOnAccount(heir!.AccountId, clan))
            {
                refusal = ClanStatus.ChiefOnAccount;
            }

            if (refusal != ClanStatus.Ok)
            {
                return Tell(session.EntityId, refusal);
            }

            var outcome = await PayAsync(sessionToken, npcEntityId, ClanRules.EntrustCost, 0, (s, prepared) => _repository.SetRanksAsync(clan.Id,
                [new ClanRankChange(heir!.CharacterId, (short)ClanRank.Chief), new ClanRankChange(chief.CharacterId, (short)ClanRank.Member)],
                s.Stamp(prepared.Snapshot!), CancellationToken.None));
            if (outcome != ClanStatus.Ok)
            {
                return Tell(session.EntityId, outcome);
            }

            lock (_state)
            {
                clan.Replace(heir! with { Rank = ClanRank.Chief });
                clan.Replace(chief with { Rank = ClanRank.Member });
            }

            Announce(clan, GameText.Of(GameTextId.ClanChiefChanged, chief.Name, heir!.Name));
            Publish(clan);
            return ClanStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Dissolves the clans whose deadline passed (a registering clan short of six, or a registered one that stayed below)
    // and forgets the invitations nobody answered in time.
    public async Task ExpireAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var now = Now;
            foreach (var (entityId, pending) in _invitations)
            {
                if (pending.ExpiresAt < now)
                {
                    _invitations.TryRemove(entityId, out _);
                }
            }

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

    // The Clan Master's fee through the world's durable path, stored by store with the paying character; a refusal, a
    // busy world or a storage failure answers why.
    private async Task<ClanStatus> PayAsync(string sessionToken, ulong npcEntityId, long fee, int minimumLevel,
        Func<PlayerSession, PreparedNpcPayment, Task<bool>> store)
    {
        try
        {
            using var lease = await _durable.AdmitAsync(_sessions, sessionToken, CancellationToken.None);
            return await lease.RunAsync(
                s => _world.PrepareNpcPaymentAsync(s, npcEntityId, Simulation.World.Npcs.NpcServiceKind.Clan, fee, minimumLevel),
                _world.CommitNpcPaymentAsync,
                static prepared => prepared.Status switch
                {
                    NpcPaymentStatus.Ok => ClanStatus.Ok,
                    NpcPaymentStatus.OutOfReach => ClanStatus.OutOfReach,
                    NpcPaymentStatus.LevelTooLow => ClanStatus.LevelTooLow,
                    NpcPaymentStatus.NotEnoughGold => ClanStatus.NotEnoughGold,
                    _ => ClanStatus.Busy
                },
                static _ => ClanStatus.Unavailable,
                async (s, prepared) => await StoreAsync(() => store(s, prepared)) ? null : DurableFailure.StorageFailed);
        }
        catch (WorldBusyException)
        {
            return ClanStatus.Unavailable;
        }
    }

    private async Task<ClanStatus> RemoveAsync(Clan clan, ClanMember member, GameText line, GameText? toMember)
    {
        var deadlineBefore = clan.DeadlineSeconds;
        var (state, deadline) = ClanRules.Counted(clan, clan.Count - 1, Now);
        if (!await StoreAsync(() => _repository.RemoveMemberAsync(clan.Id, member.CharacterId, (short)state, deadline)))
        {
            return ClanStatus.Unavailable;
        }

        lock (_state)
        {
            clan.Remove(member.CharacterId);
            (clan.State, clan.DeadlineSeconds) = (state, deadline);
            _byCharacter.Remove(member.CharacterId);
        }

        await Retag(null, member.CharacterId);
        Announce(clan, line);
        if (_entityOf.TryGetValue(member.CharacterId, out var entityId))
        {
            Say(entityId, toMember ?? line);
        }

        if (deadlineBefore == 0 && deadline != 0)
        {
            Announce(clan, GameText.Of(GameTextId.ClanBelowSix, ClanTexts.Number(ClanRules.GraceDays), ClanTexts.Number(ClanRules.RegisteredMembers)));
        }

        Publish(clan);
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
        lock (_state)
        {
            _clans.Remove(clan.Id);
            foreach (var characterId in members)
            {
                _byCharacter.Remove(characterId);
            }
        }

        await Task.WhenAll(members.Select(characterId => Retag(null, characterId)));
        return true;
    }

    private bool ChiefOnAccount(long accountId, Clan? except = null) =>
        _clans.Values.Any(clan => clan != except && clan.Chief.AccountId == accountId);

    private async Task<ClanStatus> CooldownAsync(long characterId)
    {
        try
        {
            return await _repository.CooldownAsync(characterId) > Now ? ClanStatus.JoinCooldown : ClanStatus.Ok;
        }
        catch (DbException)
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
        catch (DbException)
        {
            return false;
        }
    }

    // The member's clan with its online members' rows brought to their live levels (the chief's sets the capacity).
    private bool TryMember(ulong entityId, out Online online, out Clan clan, out ClanMember member)
    {
        clan = null!;
        member = null!;
        if (!_online.TryGetValue(entityId, out online!) || !_byCharacter.TryGetValue(online.CharacterId, out var found))
        {
            return false;
        }

        Sync(found);
        if (found.Find(online.CharacterId) is not { } self)
        {
            return false;
        }

        (clan, member) = (found, self);
        return true;
    }

    private void Sync(Clan clan)
    {
        lock (_state)
        {
            for (var index = 0; index < clan.Count; index++)
            {
                var member = clan.Members[index];
                if (LevelOf(member) is var level && level != member.Level)
                {
                    clan.Replace(member with { Level = level });
                }
            }
        }
    }

    private int LevelOf(ClanMember member) =>
        _entityOf.TryGetValue(member.CharacterId, out var entityId) && _online.TryGetValue(entityId, out var online) ? online.Level : member.Level;

    // A member's online state, view and tag after its clan changed (a clan of null: it has none now).
    private async Task Retag(Clan? clan, long characterId)
    {
        if (!_entityOf.TryGetValue(characterId, out var entityId) || !_online.TryGetValue(entityId, out var online))
        {
            return;
        }

        online.ClanId = clan?.Id ?? 0;
        await ShowTagAsync(entityId, clan);
        if (clan is null)
        {
            online.Link.Deliver(null);
        }
    }

    private void Publish(Clan clan)
    {
        lock (_state)
        {
            Refresh(clan);
        }
    }

    // Every online member gets the clan's view as it is now; called under the state lock.
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

    // Called under the state lock.
    private ClanView? View(Clan clan, long characterId) =>
        clan.Find(characterId) is not { } self ? null
            : new ClanView(clan.Id, clan.Name, clan.State, clan.DeadlineSeconds, self.Rank, ClanRules.Capacity(LevelOf(clan.Chief)), clan.Intro, clan.MarkVersion,
                clan.Members.OrderByDescending(static member => member.Rank).ThenBy(static member => member.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(member => new ClanMemberView(member.Name, member.Class, LevelOf(member), member.Rank, _entityOf.ContainsKey(member.CharacterId)))
                    .ToArray());

    // The tag over the head is looks only: a busy world leaves the old one until the next change or world entry.
    private async Task ShowTagAsync(ulong entityId, Clan? clan)
    {
        try
        {
            await _world.SetClanTagAsync(entityId, clan is null ? null : new ClanTag(clan.Id, clan.Name, clan.MarkVersion, clan.State == ClanState.Registered));
        }
        catch (WorldBusyException)
        {
        }
    }

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

    // An online player; its level is its character's live level (the level it entered with while none is loaded).
    private sealed class Online(long characterId, long accountId, string name, PlayerClass playerClass, int level, PlayerSession session, IClanLink link)
    {
        public long CharacterId { get; } = characterId;
        public long AccountId { get; } = accountId;
        public string Name { get; } = name;
        public PlayerClass Class { get; } = playerClass;
        public int Level => session.Character?.Level ?? level;
        public IClanLink Link { get; } = link;

        // Read by the chat without the gate.
        public long ClanId { get => Interlocked.Read(ref _clanId); set => Interlocked.Exchange(ref _clanId, value); }

        private long _clanId;
    }

    private readonly record struct Pending(long ClanId, ulong RecruiterId, long RecruiterCharacterId, long ExpiresAt);
}
