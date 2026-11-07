using System.Collections.Immutable;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.Castle;

// A clan on the running siege's board, named.
public readonly record struct CastleBoardClan(long ClanId, string Name, long Damage);

// What every player in the world is told of the castle: its owner (the crown over its members), the tax in force on fees
// (0 without an owner; the prices a client works out itself carry it), whether the server runs the league (players never
// fight each other then), and the siege's state, the seconds a running one had left when this was made and what stands,
// with the best clans' damage (CastleSiegeView).
public sealed record CastleBroadcast(long OwnerClanId, string OwnerName, int Tax, SiegeState Siege, bool League, long SecondsLeft, bool DoorDown, int Crystals,
    ImmutableArray<CastleBoardClan> Top, long TotalDamage)
{
    public static CastleBroadcast None { get; } = new(0, "", 0, SiegeState.Peace, false, 0, false, 0, [], 0);
}

// A player's connection, told the castle at world entry and on every change.
public interface ICastleLink
{
    void Deliver(CastleBroadcast castle);
}
