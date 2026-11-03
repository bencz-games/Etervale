using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.World.Motion;

public static class PlayerMotionFallback
{
    public const int SwingFrames = 40;

    public const int HitFrames = 22;

    public static MotionClip GetClip(PlayerClass playerClass, MotionCode state)
    {
        var (attackEvent, damageFrames, eatEvent) = ClassProfiles.Of(playerClass).Motion;

        // Clip timing uses spans and event offsets; frame windows are normalized to a zero origin.
        return state switch
        {
            MotionCode.Attack => new MotionClip(0, SwingFrames, attackEvent, 0, 0, 0, false),
            MotionCode.Damage => new MotionClip(0, damageFrames, 0, 0, 0, 0, false),
            MotionCode.Eat => new MotionClip(0, 20, eatEvent, 0, 0, 0, false),
            MotionCode.Yahoo => new MotionClip(0, 50, 0, 0, 0, 0, false),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }
}
