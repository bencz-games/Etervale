using Microsoft.Xna.Framework;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Rendering.Weather;

namespace PristonTale.Gameplay.Weather;

// The weather the client shows: the server's (GameScene.ServerWeather) eased in, clear while the setting is off. The
// clouds gather and part over CloudSeconds, the rain swells and stops over RainSeconds and the wind turns over
// WindSeconds. In a storm lightning strikes every StrikeMinSeconds to StrikeMaxSeconds: two or three flashes in a third
// of a second, then the thunder after a delay of its distance, louder the nearer (Thunder hears its volume).
public sealed class WeatherPresentation(Random random)
{
    public const float CloudSeconds = 6f;
    public const float RainSeconds = 4f;
    public const float WindSeconds = 3f;
    // Ground speed of the strongest wind, in world units per second.
    public const float MaxWind = 360f;
    public const float StrikeMinSeconds = 5f;
    public const float StrikeMaxSeconds = 16f;
    public const float ThunderMinDelay = 0.3f;
    public const float ThunderMaxDelay = 3f;
    // The flashes of a strike start this far apart and each dies away at this rate.
    private const float PulseSpacing = 0.11f;
    private const float PulseDecay = 16f;

    private readonly float[] _pulses = new float[3];
    private int _pulseCount;
    private float _sinceStrike = float.PositiveInfinity;
    private float _untilStrike = -1f;
    private float _untilThunder = -1f;
    private float _thunderVolume;

    public WeatherPresentation() : this(new Random())
    {
    }

    public float Cloud { get; private set; }
    public float Rain { get; private set; }
    public float Flash { get; private set; }
    public Vector2 Wind { get; private set; }

    public PristonWeather View => new(Cloud, Rain, Flash, Wind);

    public event Action<float> Thunder;

    // Snaps to the weather at once (entering the world or a field) instead of easing in.
    public void Snap(WeatherState weather, bool enabled)
    {
        var target = enabled ? weather : WeatherState.Clear;
        (Cloud, Rain, Wind) = (Clouded(target), Rained(target), Blown(target));
    }

    public void Update(WeatherState weather, bool enabled, float seconds)
    {
        var target = enabled ? weather : WeatherState.Clear;
        Cloud = Ease(Cloud, Clouded(target), seconds, CloudSeconds);
        Rain = Ease(Rain, Rained(target), seconds, RainSeconds);
        Wind = Vector2.Lerp(Wind, Blown(target), 1f - MathF.Exp(-seconds * 3f / WindSeconds));
        UpdateLightning(target.Kind == WeatherKind.Storm, seconds);
    }

    private void UpdateLightning(bool storm, float seconds)
    {
        if (!storm)
        {
            _untilStrike = -1f;
        }
        else if (_untilStrike < 0f)
        {
            _untilStrike = Between(StrikeMinSeconds, StrikeMaxSeconds);
        }
        else if ((_untilStrike -= seconds) <= 0f)
        {
            Strike();
        }

        _sinceStrike += seconds;
        var flash = 0f;
        for (var pulse = 0; pulse < _pulseCount; pulse++)
        {
            var age = _sinceStrike - pulse * PulseSpacing;
            if (age >= 0f)
            {
                flash = MathF.Max(flash, _pulses[pulse] * MathF.Exp(-age * PulseDecay));
            }
        }

        Flash = flash;
        if (_untilThunder >= 0f && (_untilThunder -= seconds) < 0f)
        {
            Thunder?.Invoke(_thunderVolume);
        }
    }

    private void Strike()
    {
        _untilStrike = Between(StrikeMinSeconds, StrikeMaxSeconds);
        _sinceStrike = 0f;
        // A near strike flashes hardest and thunders soonest and loudest; the later flashes are fainter.
        var nearness = random.NextSingle();
        _pulseCount = 2 + random.Next(2);
        _pulses[0] = MathHelper.Lerp(0.45f, 1f, nearness);
        for (var pulse = 1; pulse < _pulseCount; pulse++)
        {
            _pulses[pulse] = _pulses[0] * Between(0.4f, 0.9f);
        }

        _untilThunder = MathHelper.Lerp(ThunderMaxDelay, ThunderMinDelay, nearness);
        _thunderVolume = MathHelper.Lerp(0.45f, 1f, nearness);
    }

    private float Between(float low, float high) => low + random.NextSingle() * (high - low);

    private static float Clouded(WeatherState weather) => weather.Kind == WeatherKind.Clear ? 0f : 1f;

    private static float Rained(WeatherState weather) => weather.Raining ? weather.Intensity / 255f : 0f;

    // 0 toward +Z, 64 toward +X.
    private static Vector2 Blown(WeatherState weather)
    {
        var angle = weather.WindHeading / 256f * MathF.Tau;
        return new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * (weather.Wind / 255f * MaxWind);
    }

    // Moves toward the target at a pace that crosses the whole range in the given seconds.
    private static float Ease(float value, float target, float seconds, float span)
    {
        var step = seconds / span;
        return value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
    }
}
