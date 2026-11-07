using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Client.Net.Presence;

public sealed record CastleClanInfo(long ClanId, string Name, long Damage);

// The owner's window as the server last sent it (None: the player's clan does not hold the castle): the treasury, the tax
// in force and the setup for the next day and siege, the clan skill in force, and whether the player (the chief) edits.
public sealed record CastleOwnerInfo(bool HasView, long Treasury, int Tax, CastleSetup Setup, CastleClanSkill SkillInForce, bool CanEdit)
{
    public static readonly CastleOwnerInfo None = new(false, 0, 0, CastleSetup.Empty, CastleClanSkill.None, false);
}

public sealed record CastleOwnerResultInfo(Protocol.Packets.CastleOwnerAction Action, CastleOwnerStatus Status);

// Bless Castle as the server last told it: the owner clan (0 for none), the tax in force on fees, whether the server
// runs the league (players never fight each other then), and the siege's state, the seconds a running one had left and
// what stands, with the best clans' damage.
public sealed record CastleInfo(long OwnerClanId, string OwnerName, int Tax, SiegeState Siege, bool League, long SecondsLeft, bool DoorDown, int Crystals,
    IReadOnlyList<CastleClanInfo> Top, long TotalDamage)
{
    public static readonly CastleInfo None = new(0, "", 0, SiegeState.Peace, false, 0, false, 0, [], 0);
}
