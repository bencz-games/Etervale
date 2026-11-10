namespace PristonTale.Gameplay.Audio;

// The original plays a sound faster or slower by naming its rate in tens of hertz; its sounds are recorded at 22050 Hz.
// The sound bank takes the change in octaves.
public static class SoundRates
{
    public const float Recorded = 2205f;

    public static float Octaves(float rate) => MathF.Log2(rate / Recorded);
}
