using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// Keyed values of the effect formats: authored values of one to four numbers, the time between keys and the ramp that
// follows a set event.
internal static class VfxKeys
{
    // Missing components are 0.
    public static Vector4 ToVector4(float[] values) =>
        new(values[0], values.Length > 1 ? values[1] : 0f, values.Length > 2 ? values[2] : 0f, values.Length > 3 ? values[3] : 0f);

    // The time from one key to the next; 1 when they share a time, so a step never divides by zero.
    public static float Span(float from, float to)
    {
        var delta = to - from;
        return delta == 0f ? 1f : delta;
    }

    // For each of count time-ordered events, the first later event that ramps the same value (sameValue), -1 when none.
    public static int[] NextRamps(int count, Func<int, bool> ramps, Func<int, int, bool> sameValue)
    {
        var next = new int[count];
        for (var start = 0; start < count; start++)
        {
            next[start] = -1;
            for (var end = start + 1; end < count; end++)
            {
                if (ramps(end) && sameValue(start, end))
                {
                    next[start] = end;
                    break;
                }
            }
        }

        return next;
    }
}
