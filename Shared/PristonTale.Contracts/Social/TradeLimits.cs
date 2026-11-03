namespace PristonTale.Contracts.Social;

// The original trade's sizes, shared by its rules, its messages and the HUD: the items one side offers on its 9x4 grid
// of the trade window, the distance (3D) within which two players trade, how long a request waits and how long a side
// cannot lock after the other changed its offer.
public static class TradeLimits
{
    public const int MaxItems = 20;
    public const int Columns = 9;
    public const int Rows = 4;
    public const float Reach = 256;

    public static bool InReach(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => System.Numerics.Vector3.DistanceSquared(a, b) < Reach * Reach;
    public const long RequestMs = 10_000;
    public const long LockDelayMs = 2_000;
}
