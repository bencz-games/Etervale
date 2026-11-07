using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Client.Net.Presence;

public sealed record ClanMemberInfo(string Name, PlayerClass Class, int Level, ClanRank Rank, bool Online);

// The local player's clan as the server last sent it; None without one.
public sealed record ClanInfo(long ClanId, string Name, ClanState State, long DeadlineSeconds, ClanRank Rank, int Capacity, string Intro, int MarkVersion,
    IReadOnlyList<ClanMemberInfo> Members)
{
    public static readonly ClanInfo None = new(0, "", ClanState.Registering, 0, ClanRank.Member, 0, "", 0, []);

    public bool HasClan => ClanId != 0;

    public bool CanRecruit => HasClan && ClanRules.CanRecruit(Rank);
}

public sealed record ClanInvitationInfo(ulong RecruiterId, string ClanName, string ChiefName);

// How a clan request ended (ClanStatus; a refusal's line comes in the chat).
public sealed record ClanResultInfo(byte Action, ClanStatus Status);
