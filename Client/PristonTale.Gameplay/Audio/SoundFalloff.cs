using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.Audio;

// The original's falloff for a positional sound: every 1100 squared units of distance take one step off a full 400.
public static class SoundFalloff
{
    public const int FullSteps = 400;
    private const float SquaredUnitsPerStep = 1100f;

    // The steps left at this squared distance (past any inner radius the caller takes off first).
    public static int Steps(float distanceSquared) => FullSteps - Math.Clamp((int)(distanceSquared / SquaredUnitsPerStep), 0, FullSteps);

    public static float Volume(Vector3 source, Vector3 listener) => Steps(Vector3.DistanceSquared(source, listener)) / (float)FullSteps;
}
