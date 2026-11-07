using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Clans;

// A clan row: state 0 registering, 1 registered; times in Unix seconds, deadline 0 for none.
public sealed record ClanRecord(long Id, string Name, short State, long CreatedAt, long DeadlineAt, string Intro, int MarkVersion);

// A member row with the character's name, class and level read alongside (they are not stored twice).
public sealed record ClanMemberRecord(long ClanId, long CharacterId, short Rank, long JoinedAt, string Name = "", int JobCode = 0, int Level = 0,
    long AccountId = 0);

// A member's new rank.
public readonly record struct ClanRankChange(long CharacterId, short Rank);

// The clans on the server. Every change that costs the character gold stores the character (its UpdateSql) in the same
// transaction: either both are written or neither. False (or 0) when nothing was written: the name is taken, the
// character's capture is older than the stored one, or a row moved under the request.
public interface IClanRepository
{
    Task<(IReadOnlyList<ClanRecord> Clans, IReadOnlyList<ClanMemberRecord> Members)> LoadAsync(CancellationToken ct = default);

    // The new clan's id, its chief's row and the paying character; 0 when the name is taken.
    Task<long> FoundAsync(ClanRecord clan, ClanMemberRecord chief, CharacterRecord payment, CancellationToken ct = default);

    // A member joins (the clan's state and deadline as they are after it).
    Task<bool> AddMemberAsync(ClanMemberRecord member, short state, long deadlineAt, CancellationToken ct = default);

    // A member leaves or is expelled (the clan's state and deadline as they are after it).
    Task<bool> RemoveMemberAsync(long clanId, long characterId, short state, long deadlineAt, CancellationToken ct = default);

    // Ranks change together (a vice appointed or discharged, the chief handing the clan over with the payment).
    Task<bool> SetRanksAsync(long clanId, IReadOnlyList<ClanRankChange> changes, CharacterRecord? payment = null, CancellationToken ct = default);

    // The clan and its member rows go; the members cannot join another until cooldownUntil.
    Task<bool> DisbandAsync(long clanId, IReadOnlyList<long> members, long cooldownUntil, CancellationToken ct = default);

    // When the character may join a clan again (0 when it may now).
    Task<long> CooldownAsync(long characterId, CancellationToken ct = default);

    Task<bool> IsMemberAsync(long characterId, CancellationToken ct = default);

    // The clan's mark version once a new mark is kept.
    Task<bool> SetMarkVersionAsync(long clanId, int version, CancellationToken ct = default);

    // The clan name of each of the account's characters that is in one, by character id.
    Task<IReadOnlyDictionary<long, string>> ClanNamesAsync(long accountId, CancellationToken ct = default);
}
