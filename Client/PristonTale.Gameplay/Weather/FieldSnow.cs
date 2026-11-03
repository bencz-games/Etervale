using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;
using PristonTale.Effects.Vfx;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Gameplay.Weather;

// The falling snow of the snow skies: the original's "Snow" particle script runs while the field shown snows. Its source
// follows the player, ahead of its facing and above it, as the original placed it every tick; flakes already born stay
// where they fall, and stopping lets them play out. A script the effect system cannot start yet (its library still
// compiling after entry) is tried again every RetrySeconds.
public sealed class FieldSnow(IScriptEffects scripts)
{
    public const string Script = "Snow";
    public const float AheadDistance = 40f;
    // 30000 in the authored fixed point.
    public const float SourceHeight = 30000f / GameUnits.FixedScale;
    // An effect lasts at most VfxDefinition.MaxDuration; a new one starts while the old one's last flakes still fall.
    public const float RenewLeadSeconds = 10f;
    public const float RenewSeconds = VfxDefinition.MaxDuration - RenewLeadSeconds;
    public const float RetrySeconds = 1f;

    private IScriptEffect _effect;
    private float _age;
    private float _retryIn;

    public bool Falling => _effect is not null;

    public void Update(bool snowing, Vector3 player, Vector3 forward, float seconds)
    {
        if (!snowing)
        {
            Stop();
            return;
        }

        var source = SourceFor(player, forward);
        _age += seconds;
        if (_effect is not null && _effect.Alive && _age < RenewSeconds)
        {
            _effect.MoveTo(source);
            return;
        }

        _effect?.Stop();
        _effect = null;
        _retryIn -= seconds;
        if (_retryIn > 0f)
        {
            return;
        }

        _effect = scripts.Spawn(Script, source, Matrix.Identity);
        _age = 0f;
        _retryIn = _effect is null ? RetrySeconds : 0f;
    }

    public void Stop()
    {
        _effect?.Stop();
        _effect = null;
        _age = 0f;
        _retryIn = 0f;
    }

    public static Vector3 SourceFor(Vector3 player, Vector3 forward)
    {
        var flat = new Vector3(forward.X, 0f, forward.Z);
        var ahead = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) * AheadDistance : Vector3.Zero;
        return player + ahead + Vector3.Up * SourceHeight;
    }
}
