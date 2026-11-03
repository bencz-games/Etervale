using System.Numerics;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Physics;

namespace PristonTale.Simulation.Parties;

// The original's party rules. Two players group when their levels (a party's average level stands for its members) are
// less than LevelGap apart. A kill's experience is taken at the party's average level and shared by the party's size:
// the killer always gets its share, every other member its own while within ShareRange of the killer. Gold under
// SharedGoldBelow picked up by a member is split evenly among the members within ShareRange of the picker, the picker
// keeping the remainder. A request to join waits InviteMs for its answer.
public static class PartyRules
{
    public const int LevelGap = 15;
    public const long SharedGoldBelow = 10_000;
    // The original's request box closes after 1400 frames of 70 per second.
    public const long InviteMs = 20_000;

    // Percent of the kill's experience for the killer and for each other member, by the party's size.
    private static readonly (int Killer, int Other)[] ExpShares = [(100, 0), (65, 65), (50, 50), (45, 45), (40, 40), (37, 37)];

    public static (int Killer, int Other) ExpShare(int members) => ExpShares[Math.Clamp(members, 1, PartyLimits.MaxMembers) - 1];

    public static ulong Share(ulong experience, int percent) => (ulong)((UInt128)experience * (uint)percent / 100);

    // Integer average, as the original keeps it.
    public static int AverageLevel(ReadOnlySpan<int> levels)
    {
        if (levels.IsEmpty)
        {
            return 0;
        }

        long sum = 0;
        foreach (var level in levels)
        {
            sum += level;
        }

        return (int)(sum / levels.Length);
    }

    public static bool LevelsMatch(int inviterLevel, int targetLevel) => Math.Abs(inviterLevel - targetLevel) < LevelGap;

    public static bool InShareRange(Vector3 from, Vector3 member) => Planar.Within(member, from, PartyLimits.ShareRange);

    public static bool SharesGold(long amount) => amount > 0 && amount < SharedGoldBelow;

    // The picker's part and every other sharer's, for count players sharing amount.
    public static (long Picker, long Each) SplitGold(long amount, int count)
    {
        var each = amount / Math.Max(1, count);
        return (each + amount % Math.Max(1, count), each);
    }
}
