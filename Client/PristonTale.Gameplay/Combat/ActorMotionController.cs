using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.Combat;

public enum MotionCommandKind : byte
{
    None = 0,

    Set = 1,

    Restart = 2,
}

public readonly record struct MotionCommand(MotionCommandKind Kind, MotionCode Motion)
{
    public static readonly MotionCommand None = new(MotionCommandKind.None, MotionCode.Stand);
}

public sealed class ActorMotionController
{
    public MotionCode Current { get; private set; } = MotionCode.Stand;

    public bool IsDead => Current == MotionCode.Dead;

    // Swings and casts begun so far: each one that begins ends the one before it.
    public long ActionStarts { get; private set; }

    // A flinch, death, drink, landing or cheer holds the player until its clip ends; swings and casts hold it through their
    // own lock.
    public bool LocomotionFrozen => Current is MotionCode.Damage or MotionCode.Dead or MotionCode.Eat
        or MotionCode.FallStand or MotionCode.FallDamage or MotionCode.Yahoo;

    public MotionCommand ApplyAuthoritative(MotionCode motion)
    {
        if (motion == Current)
        {
            return MotionCommand.None;
        }

        Begin(motion);
        return new MotionCommand(IsOneShotBusy(motion) ? MotionCommandKind.Restart : MotionCommandKind.Set, motion);
    }

    // A fall keeps its motion while the player walks; the landing replaces it.
    public MotionCommand ApplyLocomotion(MotionCode motion)
    {
        if (LocomotionFrozen || motion == Current || Current == MotionCode.FallDown)
        {
            return MotionCommand.None;
        }

        Current = motion;
        return new MotionCommand(MotionCommandKind.Set, motion);
    }

    public MotionCommand Retrigger(MotionCode oneShot)
    {
        if (IsDead)
        {
            return MotionCommand.None;
        }

        Begin(oneShot);
        return new MotionCommand(MotionCommandKind.Restart, oneShot);
    }

    private void Begin(MotionCode motion)
    {
        Current = motion;
        if (motion is MotionCode.Attack or MotionCode.Skill)
        {
            ActionStarts++;
        }
    }

    private static bool IsOneShotBusy(MotionCode motion) => motion is
        MotionCode.Attack or
        MotionCode.Damage or
        MotionCode.Skill or
        MotionCode.Eat or
        MotionCode.Yahoo or
        MotionCode.Restart;
}
