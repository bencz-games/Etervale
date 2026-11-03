using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Gameplay.Effects;

// Follows one actor's clip from frame to frame, so its owner learns which event frames passed since the last frame. A new
// clip, or the same clip back at an earlier tick, starts again from the clip's first frame.
public struct ClipEventCursor
{
    private float _start;
    private float _previous;

    public PristonMotionInfo Motion { get; private set; }
    public float Ticks { get; private set; }

    // Moves to this tick of the clip; true when the clip (re)started.
    public bool Advance(PristonMotionInfo motion, float ticks)
    {
        var restarted = !ReferenceEquals(motion, Motion) || ticks < Ticks;
        _start = (float)motion.StartFrame * GameUnits.FrameTicks;
        _previous = restarted ? _start : Ticks;
        Motion = motion;
        Ticks = ticks;
        return restarted;
    }

    // Whether the clip's event frame of this index (a set one) was passed by the last Advance.
    public readonly bool Passed(int index)
    {
        if (Motion is null || index >= Motion.EventFrames.Count || Motion.EventFrames[index] == 0)
        {
            return false;
        }

        var at = _start + Motion.EventFrames[index];
        return at > _previous && at <= Ticks;
    }
}
