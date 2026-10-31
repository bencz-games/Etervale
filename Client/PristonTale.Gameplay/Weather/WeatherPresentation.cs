using Microsoft.Xna.Framework;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Rendering.Weather;

namespace PristonTale.Gameplay.Weather;

// The weather the client shows: the server's (GameScene.ServerWeather) eased in, clear while the setting is off. The
// clouds gather and part over CloudSeconds, the rain swells and stops over RainSeconds and the wind turns over
// WindSeconds. A storm's lightning comes as a real storm's does, never on a beat: the storm's activity wanders between
// quiet and fierce spells (ActivitySpellSeconds), the time to the next strike is drawn from an exponential law around
// MeanStrikeSeconds over the activity, and a strike often brings another soon after (FollowUpChance). Most strikes are
// far off: a faint flash and a late, low rumble, or none at all for the farthest; a few are near, bright and loud at
// once. Each flickers in one to four flashes at uneven intervals. Thunder hears each strike's volume. Under a light rain
// the clouds now and then break over the sun for a while (SunBreak), the light and the rainbow a sunshower brings.
public sealed class WeatherPresentation(Random random)
{
    public const float CloudSeconds = 6f;
    public const float RainSeconds = 4f;
    public const float WindSeconds = 3f;
    // Ground speed of the strongest wind, in world units per second.
    public const float MaxWind = 360f;
    // The mean time between strikes at an activity of 1, the activity's range, and how long a spell of it lasts.
    public const float MeanStrikeSeconds = 9f;
    public const float LeastActivity = 0.25f;
    public const float MostActivity = 1.8f;
    public const float ActivitySpellSeconds = 40f;
    // The share of strikes followed by another within FollowUpSeconds, and the least wait between two strikes.
    public const float FollowUpChance = 0.3f;
    public const float FollowUpSeconds = 2.5f;
    public const float LeastStrikeSeconds = 0.35f;
    // Thunder comes after this delay at the nearest and the farthest strike; strikes quieter than LeastThunder are too
    // far to hear.
    public const float ThunderMinDelay = 0.2f;
    public const float ThunderMaxDelay = 6f;
    public const float LeastThunder = 0.18f;
    // A sunshower: the densest rain whose clouds break, how often and how long a spell lasts, and how long a break takes
    // to open or close.
    public const byte LightRainMost = 110;
    public const float BreakChance = 0.4f;
    public const float BreakMinSeconds = 20f;
    public const float BreakMaxSeconds = 60f;
    public const float BreakSeconds = 6f;
    private const float PulseDecay = 9f;
    private const int MostPulses = 4;

    private readonly float[] _pulses = new float[MostPulses];
    private readonly float[] _pulseTimes = new float[MostPulses];
    private readonly List<(float Delay, float Volume)> _thunders = [];
    private int _pulseCount;
    private float _sinceStrike = float.PositiveInfinity;
    private float _untilStrike = -1f;
    private float _activity = 1f;
    private float _activityTarget = 1f;
    private float _untilSpell;
    private float _breakTarget;
    private float _untilBreak;

    public WeatherPresentation() : this(new Random())
    {
    }

    public float Cloud { get; private set; }
    public float Rain { get; private set; }
    public float Flash { get; private set; }
    public Vector2 Wind { get; private set; }

    // The last strike's compass heading (radians), where its flash lights the clouds.
    public float FlashAzimuth { get; private set; }

    // A break in the clouds passing the sun (0..1), only in a light rain.
    public float SunBreak { get; private set; }

    public PristonWeather View => new(Cloud, Rain, Flash, Wind, FlashAzimuth, SunBreak);

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
        UpdateSunBreaks(target, seconds);
    }

    // A light rain's clouds break over the sun in BreakChance of the spells (BreakMinSeconds to BreakMaxSeconds each),
    // the break opening and closing over BreakSeconds; no other weather breaks.
    private void UpdateSunBreaks(WeatherState weather, float seconds)
    {
        var light = weather.Raining && weather.Intensity <= LightRainMost;
        if ((_untilBreak -= seconds) <= 0f)
        {
            _untilBreak = Between(BreakMinSeconds, BreakMaxSeconds);
            _breakTarget = random.NextSingle() < BreakChance ? Between(0.5f, 1f) : 0f;
        }

        SunBreak = Ease(SunBreak, light ? _breakTarget : 0f, seconds, BreakSeconds);
    }

    private void UpdateLightning(bool storm, float seconds)
    {
        if (!storm)
        {
            _untilStrike = -1f;
        }
        else
        {
            WanderActivity(seconds);
            if (_untilStrike < 0f)
            {
                _untilStrike = NextStrike();
            }
            else if ((_untilStrike -= seconds) <= 0f)
            {
                Strike();
            }
        }

        _sinceStrike += seconds;
        var flash = 0f;
        for (var pulse = 0; pulse < _pulseCount; pulse++)
        {
            var age = _sinceStrike - _pulseTimes[pulse];
            if (age >= 0f)
            {
                flash = MathF.Max(flash, _pulses[pulse] * MathF.Exp(-age * PulseDecay));
            }
        }

        Flash = flash;
        for (var index = _thunders.Count - 1; index >= 0; index--)
        {
            var (delay, volume) = _thunders[index];
            if ((delay -= seconds) < 0f)
            {
                _thunders.RemoveAt(index);
                Thunder?.Invoke(volume);
            }
            else
            {
                _thunders[index] = (delay, volume);
            }
        }
    }

    // The storm's activity eases toward a level drawn for each spell, so quiet and fierce spells come and go.
    private void WanderActivity(float seconds)
    {
        if ((_untilSpell -= seconds) <= 0f)
        {
            _untilSpell = ActivitySpellSeconds * Between(0.5f, 1.5f);
            _activityTarget = Between(LeastActivity, MostActivity);
        }

        _activity = MathHelper.Lerp(_activity, _activityTarget, 1f - MathF.Exp(-seconds / (ActivitySpellSeconds * 0.25f)));
    }

    // The wait to the next strike: soon after this one now and then, else exponential around the activity's mean.
    private float NextStrike()
    {
        if (random.NextSingle() < FollowUpChance)
        {
            return Between(LeastStrikeSeconds, FollowUpSeconds);
        }

        var mean = MeanStrikeSeconds / MathF.Max(_activity, LeastActivity);
        return MathF.Max(LeastStrikeSeconds, -mean * MathF.Log(1f - random.NextSingle() * 0.999f));
    }

    private void Strike()
    {
        _untilStrike = NextStrike();
        _sinceStrike = 0f;
        FlashAzimuth = Between(0f, MathF.Tau);
        // Most strikes are far: nearness is drawn with its square. The near ones flash hardest and thunder soonest and
        // loudest; the later flashes of one strike are fainter and come at uneven intervals.
        var nearness = random.NextSingle();
        nearness *= nearness;
        _pulseCount = 1 + random.Next(MostPulses);
        _pulses[0] = MathHelper.Lerp(0.25f, 1f, nearness);
        _pulseTimes[0] = 0f;
        for (var pulse = 1; pulse < _pulseCount; pulse++)
        {
            _pulses[pulse] = _pulses[0] * Between(0.3f, 0.95f);
            _pulseTimes[pulse] = _pulseTimes[pulse - 1] + Between(0.04f, 0.2f);
        }

        var volume = MathHelper.Lerp(0.1f, 1f, nearness);
        if (volume >= LeastThunder)
        {
            _thunders.Add((MathHelper.Lerp(ThunderMaxDelay, ThunderMinDelay, nearness) * Between(0.85f, 1.15f), volume));
        }
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
