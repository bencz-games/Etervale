namespace PristonTale.Simulation.Characters;

// The grace in which no damage lands on a player. Its viewers see it as a status (StatusCode) and the body blinks while
// it runs. Entering the world and coming out of a warp gate grant ArrivalMs, the end of a castle siege SiegeEndMs to the
// players in the castle's field; restarts, revivals and invulnerability items grant their own.
public static class PlayerGrace
{
    public const uint StatusCode = 0xF0000600;
    public const float ArrivalMs = 10_000f;
    public const float SiegeEndMs = 6_000f;
}
