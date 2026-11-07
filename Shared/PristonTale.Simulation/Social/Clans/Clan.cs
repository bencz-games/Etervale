using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Social.Clans;

// A member's place: the chief leads, the one vice chief also recruits, the others are members.
public enum ClanRank : byte
{
    Member = 0,
    Vice = 1,
    Chief = 2
}

// A clan is registering until it first counts ClanRules.RegisteredMembers members, then registered.
public enum ClanState : byte
{
    Registering = 0,
    Registered = 1
}

public sealed record ClanMember(long CharacterId, long AccountId, string Name, PlayerClass Class, int Level, ClanRank Rank, long JoinedAtSeconds);

// One clan as the server keeps it. DeadlineSeconds (Unix seconds, 0 for none) is when a clan below
// ClanRules.RegisteredMembers members is dissolved: a registering clan's first window, or the countdown a registered clan
// starts when it falls below. MarkVersion 0 is the default mark; an uploaded mark raises it.
public sealed class Clan
{
    private readonly List<ClanMember> _members = [];

    public Clan(long id, string name, ClanState state, long createdAtSeconds, long deadlineSeconds, string intro = "", int markVersion = 0)
    {
        Id = id;
        Name = name;
        State = state;
        CreatedAtSeconds = createdAtSeconds;
        DeadlineSeconds = deadlineSeconds;
        Intro = intro;
        MarkVersion = markVersion;
    }

    public long Id { get; }
    public string Name { get; }
    public ClanState State { get; set; }
    public long CreatedAtSeconds { get; }
    public long DeadlineSeconds { get; set; }
    public string Intro { get; set; }
    public int MarkVersion { get; set; }

    public IReadOnlyList<ClanMember> Members => _members;

    public int Count => _members.Count;

    public ClanMember Chief => _members.First(static member => member.Rank == ClanRank.Chief);

    public ClanMember Vice => _members.FirstOrDefault(static member => member.Rank == ClanRank.Vice);

    public int Capacity => ClanRules.Capacity(Chief.Level);

    public ClanMember Find(long characterId) => _members.FirstOrDefault(member => member.CharacterId == characterId);

    public ClanMember Find(string name) => _members.FirstOrDefault(member => member.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Add(ClanMember member) => _members.Add(member);

    public bool Remove(long characterId) => _members.RemoveAll(member => member.CharacterId == characterId) > 0;

    public void Replace(ClanMember member)
    {
        var at = _members.FindIndex(existing => existing.CharacterId == member.CharacterId);
        if (at >= 0)
        {
            _members[at] = member;
        }
    }
}

// What a player shows of its clan over its head: the clan, its name and its mark's version (0 the default mark); the
// world also reads whether the clan is registered (clan pets need one).
public sealed record ClanTag(long ClanId, string Name, int MarkVersion, bool Registered = false);
