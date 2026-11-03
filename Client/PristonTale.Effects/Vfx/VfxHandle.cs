using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// A spawned effect as its holder sees it. Instances are pooled: once this effect ends and its instance starts another,
// the handle reaches nothing, so a late Stop or MoveTo cannot touch the effect that reused it.
public readonly struct VfxHandle
{
    private readonly VfxInstance? _instance;
    private readonly int _generation;

    internal VfxHandle(VfxInstance instance)
    {
        _instance = instance;
        _generation = instance.Generation;
    }

    private VfxInstance? Live => _instance is { } instance && instance.Generation == _generation ? instance : null;

    // The instance while it still runs this effect (tests inspect its layer states).
    internal VfxInstance? Instance => Live;

    // False for the handle of an effect that was not spawned (unknown name, instance limit).
    public bool Spawned => _instance is not null;

    public bool Alive => Live?.Alive == true;

    // Ends emission; live particles finish their lives.
    public void Stop() => Live?.Stop();

    // Ends the effect at once and hides what it drew.
    public void StopNow() => Live?.StopNow();

    public void Travel(Vector3 from, Vector3 to, float seconds, float arc = 0f) => Live?.Travel(from, to, seconds, arc);

    public void MoveTo(Vector3 point) => Live?.MoveTo(point);

    public void SetOrientation(Matrix orientation) => Live?.SetOrientation(orientation);
}
