using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Gameplay.Effects;

// A running original particle script: its source can move (particles already born stay) and it can be stopped (the
// live particles play out).
public interface IScriptEffect
{
    // False once the effect and its particles have ended (or the effect system dropped it).
    bool Alive { get; }

    void MoveTo(Vector3 position);

    void Stop();
}

// Starts original particle scripts by name: their converted effects ("particle.<name>" under Effect/Vfx).
public interface IScriptEffects
{
    IScriptEffect Spawn(string script, Vector3 position, Matrix orientation);
}

internal sealed class VfxScriptHandle(VfxHandle instance) : IScriptEffect
{
    public bool Alive => instance.Alive;

    public void MoveTo(Vector3 position) => instance.MoveTo(position);

    public void Stop() => instance.Stop();
}

public sealed class SystemScriptEffects(VfxSystem system, VfxLibrary library) : IScriptEffects
{
    public IScriptEffect Spawn(string script, Vector3 position, Matrix orientation)
    {
        if (!library.TryGet("particle." + script, out var effect)
            || system.Spawn(effect, new VfxSpawn(0, 0, position, 0f)) is not { Spawned: true } instance)
        {
            return null;
        }

        instance.SetOrientation(orientation);
        return new VfxScriptHandle(instance);
    }
}

// The scene's transient lights for effect layers; effect sounds are not played.
public sealed class TransientLightServices(PristonTransientLights lights) : IVfxServices
{
    public void AddLight(Vector3 position, Color color, int power, int decay) => lights.Add(position, color, power, decay);

    public void PlaySound(string path, Vector3 position, float volume)
    {
    }
}
