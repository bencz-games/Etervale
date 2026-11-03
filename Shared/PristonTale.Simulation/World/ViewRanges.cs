namespace PristonTale.Simulation.World;

// The ranges around a player, as squared world units: the connect range, inside which a player sees an entity and
// keeps monsters and spawn points awake, and the near range, where presence updates faster and spoken lines carry.
public static class ViewRanges
{
    public const int ConnectSquared = 0x120000;
    public const int NearSquared = 0x48000;

    public static readonly float Connect = MathF.Sqrt(ConnectSquared);
    public static readonly float Near = MathF.Sqrt(NearSquared);
}
