using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Client.Net.Presence;

public sealed record CastleClanInfo(long ClanId, string Name, long Damage);

// Bless Castle as the server last told it: the owner clan (0 for none), whether the server runs the league (players
// never fight each other then), and the siege's state, the seconds a running one had left and what stands, with the best
// clans' damage.
public sealed record CastleInfo(long OwnerClanId, string OwnerName, SiegeState Siege, bool League, long SecondsLeft, bool DoorDown, int Crystals,
    IReadOnlyList<CastleClanInfo> Top, long TotalDamage)
{
    public static readonly CastleInfo None = new(0, "", SiegeState.Peace, false, 0, false, 0, [], 0);
}
