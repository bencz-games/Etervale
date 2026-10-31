namespace PristonTale.Engine3D.Rendering;

// How often an actor of a crowd samples its skeleton again, the costly part of animating it: every frame within
// NearDistance of the camera's focus (on screen or not, so the shadows near the player keep up), every second frame to
// FarDistance, every fourth beyond, and every eighth while off screen. Each actor's turn is staggered by its slot so the
// samples spread over frames; its world, and so its place on screen, still follows every frame, and a new motion is
// sampled at once. The target frame's portrait shows its actor close up, so that actor samples every frame wherever it
// stands.
public static class CrowdAnimationLod
{
    public const float NearDistance = 400f;
    public const float FarDistance = 900f;
    public const int FarInterval = 4;
    public const int OffScreenInterval = 8;

    public static int Interval(float distance, bool onScreen, bool portrait = false) =>
        portrait || distance <= NearDistance ? 1 : !onScreen ? OffScreenInterval : distance <= FarDistance ? 2 : FarInterval;

    public static bool Due(int interval, uint frame, int slot) => interval <= 1 || (frame + (uint)slot) % (uint)interval == 0;
}
