using System.Collections.Immutable;
using System.Numerics;

namespace PristonTale.Simulation.World.Monsters;

// Monsters whose death at a player's hand calls others. Anywhere, a parent of Kids calls its children within Spread of
// where it fell, free to roam. In Bless Castle during a siege, a fallen crystal tower calls CrystalGuardCount guards of
// one kind of CrystalGuards (at random) and the fallen door DoorSoldierCount door soldiers at each of GuardPosts within
// PostReach of where it fell, each within Spread of its post, as defenders of the siege.
public static class KillSummons
{
    public sealed record Kid(string Parent, string Child, int Count);

    public static ImmutableArray<Kid> Kids { get; } = [new("Wanted Morif", "Mini morif", 3)];

    public static ImmutableArray<string> CrystalGuards { get; } = ["Course ricarten militia", "Course ricarten guard", "Course bless royal guard"];

    public const int CrystalGuardCount = 10;
    public const string DoorSoldier = "Castledoor soldier";
    public const int DoorSoldierCount = 6;
    public const float PostReach = 300f;
    public const float Spread = 64f;

    // By the door, by the two towers near it, mid-castle and by the Tower of Valhalla (x, z).
    public static ImmutableArray<Vector2> GuardPosts { get; } =
    [
        new(34560, -25785), new(34200, -26080), new(34900, -26080), new(33745, -28575), new(35345, -28575), new(34135, -30575), new(34975, -30575)
    ];

    public static bool NearPost(Vector2 post, Vector3 fell) => Vector2.DistanceSquared(post, new Vector2(fell.X, fell.Z)) < PostReach * PostReach;
}
