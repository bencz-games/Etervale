using PristonTale.Contracts.Social;

namespace PristonTale.Client.Net.Presence;

// The local player's Bellatra board as the server last sent it (Hidden outside the arena): the round (0 between rounds),
// its team (0 to 3), its score, the teams' scores and how its last round start or the end went.
public sealed record BellatraBoardInfo(bool Shown, int Round, int Team, long Score, IReadOnlyList<long> TeamScores, BellatraOutcome Outcome)
{
    public static readonly BellatraBoardInfo Hidden = new(false, 0, 0, 0, new long[BellatraLimits.Teams], BellatraOutcome.Playing);
}

public sealed record BellatraClanRankInfo(string Name, long Points);

// The secretary's window as the server last sent it (None: not available): the clan operating Bellatra (0 none), its
// chief, the tax on prizes, the player's clan's income, whether the player (its chief) redeems it, and yesterday's best
// clans.
public sealed record BellatraClanInfo(bool HasView, long OperatingClanId, string OperatingName, string Chief, int Tax, long Balance, bool CanRedeem,
    IReadOnlyList<BellatraClanRankInfo> Top)
{
    public static readonly BellatraClanInfo None = new(false, 0, "", "", 0, 0, false, []);
}

public sealed record BellatraClanResultInfo(Protocol.Packets.BellatraClanAction Action, BellatraClanStatus Status);
