using PristonTale.Contracts.Motion;

namespace PristonTale.Simulation.World.Monsters;

// The monster AI reads its motion through these.
public static class MotionStateExtensions
{
    public static bool IsMoving(this MotionState state)
        => state is MotionState.Walk or MotionState.Run;
}
