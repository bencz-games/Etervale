using System.Collections.Immutable;
using System.Numerics;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.World.Bellatra;

// A room of the arena: its rectangle runs from Corner0 (west, north) to Corner1 (east, south), bounds excluded, on the
// floor at Corner0's height; teams enter at its Centre.
public sealed record BellatraRoom(Vector3 Corner0, Vector3 Corner1, Vector3 Centre)
{
    public bool Holds(Vector3 position) => position.X > Corner0.X && position.X < Corner1.X && position.Z > Corner1.Z && position.Z < Corner0.Z;

    // A monster's spawn point: anywhere inside the rectangle, at the room's height.
    public Vector3 RandomPoint(ISimRng rng) =>
        new(Corner0.X + rng.NextExclusive(1, (int)(Corner1.X - Corner0.X)), Corner0.Y, Corner0.Z - rng.NextExclusive(1, (int)(Corner0.Z - Corner1.Z)));
}

// Bellatra's arena in its field: the event area a player must be in to stay in the game, and its seven rooms. Rounds 1
// to 3 use the four first rooms (one a team), rounds 4 to 6 the two middle ones and the last two the top room.
public static class BellatraArena
{
    public const int Field = FieldCodes.Bellatra;
    public const int Village = FieldCodes.Navisko;
    public const int Teams = Contracts.Social.BellatraLimits.Teams;

    public static ImmutableArray<BellatraRoom> Rooms { get; } =
    [
        new(new(18967, 1154, 3055), new(19995, 1154, 2134), new(19485, 1154, 2615)),
        new(new(20838, 1154, 3024), new(21671, 1154, 2193), new(21255, 1154, 2615)),
        new(new(22478, 1154, 3049), new(23437, 1154, 2135), new(22948, 1154, 2615)),
        new(new(24204, 1154, 3094), new(25173, 1154, 2135), new(24674, 1154, 2615)),
        new(new(20509, 1621, 5284), new(21544, 1621, 4261), new(21012, 1621, 4773)),
        new(new(22575, 1621, 5253), new(23610, 1621, 4230), new(23077, 1621, 4742)),
        new(new(21538, 1638, 7057), new(22527, 1638, 6079), new(22018, 1638, 6569))
    ];

    // The rooms a round (1 to 8) fills with monsters, first and past the last.
    public static (int First, int End) RoomsOf(int round) => round switch
    {
        >= 1 and <= 3 => (0, 4),
        >= 4 and <= 6 => (4, 6),
        7 or 8 => (6, 7),
        _ => (0, 0)
    };

    // A team enters at its own room (team 0 to 3 = rooms 0 to 3). Before round 4 the best two teams go on, the best to
    // room 4 and the second to room 5; before round 7 the best team goes on to room 6.
    public static int AdvanceRoom(int rank, int round) => round == 7 ? 6 : rank == 0 ? 4 : 5;

    // How many teams go on into a round (all of them in the others).
    public static int GoOn(int round) => round switch
    {
        4 => 2,
        7 => 1,
        _ => Teams
    };

    // The room holding the position (none: -1).
    public static int RoomAt(Vector3 position)
    {
        for (var room = 0; room < Rooms.Length; room++)
        {
            if (Rooms[room].Holds(position))
            {
                return room;
            }
        }

        return -1;
    }

    public static bool InArea(Vector3 position) =>
        position.Y >= Rooms[0].Corner0.Y - 32 && position.X > 18516 && position.X < 25715 && position.Z > 1541 && position.Z < 7178;
}
