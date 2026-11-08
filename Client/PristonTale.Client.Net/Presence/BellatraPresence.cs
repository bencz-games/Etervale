using PristonTale.Contracts.Social;

namespace PristonTale.Client.Net.Presence;

// The local player's Bellatra board as the server last sent it (Hidden outside the arena): the round (0 between rounds),
// its team (0 to 3), its score, the teams' scores, how its last round start or the end went, and at the last start that
// sent teams on, the teams that played it and those going on (a bit a team).
public sealed record BellatraBoardInfo(bool Shown, int Round, int Team, long Score, IReadOnlyList<long> TeamScores, BellatraOutcome Outcome,
    int StageTeams = 0, int GoingOn = 0)
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

public sealed record BellatraResultLineInfo(string Name, Contracts.Characters.PlayerClass Class, long Score, int Kills);

// A Bellatra result window, best score first: the player's game's final result, or a team's live one.
public sealed record BellatraResultInfo(bool Final, IReadOnlyList<BellatraResultLineInfo> Lines);

public sealed record BellatraClanResultInfo(Protocol.Packets.BellatraClanAction Action, BellatraClanStatus Status);
