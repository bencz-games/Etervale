using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Gameplay.Weather;

// The falling snow of the snow skies: the original's "Snow" particle script runs while the field shown snows. Its source
// follows the player, ahead of its facing and above it, as the original placed it every tick; flakes already born stay
// where they fall, and stopping lets them play out.
public sealed class FieldSnow(IScriptEffects scripts)
{
    public const string Script = "Snow";
    public const float AheadDistance = 40f;
    // 30000 in the original's fixed point, 256 to the unit.
    public const float SourceHeight = 30000f / 256f;
    // An effect lasts at most VfxDefinition.MaxDuration; a new one starts while the old one's last flakes still fall.
    public const float RenewSeconds = VfxDefinition.MaxDuration - 10f;

    private IScriptEffect _effect;
    private float _age;
    private bool _unavailable;

    public bool Falling => _effect is not null;

    public void Update(bool snowing, Vector3 player, Vector3 forward, float seconds)
    {
        if (!snowing || _unavailable)
        {
            Stop();
            return;
        }

        var source = SourceFor(player, forward);
        _age += seconds;
        if (_effect is null || !_effect.Alive || _age >= RenewSeconds)
        {
            _effect?.Stop();
            _effect = scripts.Spawn(Script, source, Matrix.Identity);
            _unavailable = _effect is null;
            _age = 0f;
            return;
        }

        _effect.MoveTo(source);
    }

    public void Stop()
    {
        _effect?.Stop();
        _effect = null;
        _age = 0f;
    }

    public static Vector3 SourceFor(Vector3 player, Vector3 forward)
    {
        var flat = new Vector3(forward.X, 0f, forward.Z);
        var ahead = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) * AheadDistance : Vector3.Zero;
        return player + ahead + Vector3.Up * SourceHeight;
    }
}
