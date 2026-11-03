namespace PristonTale.Contracts.World;

// The server's world tick: every field steps and every receiver's presence is encoded once per tick, so a sample the
// client receives was taken at most one interval after what it reports began.
public static class ServerTick
{
    public const int Hz = 20;
    public const int IntervalMs = 1000 / Hz;
}
