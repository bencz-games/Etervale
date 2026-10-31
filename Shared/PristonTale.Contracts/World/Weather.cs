namespace PristonTale.Contracts.World;

// The sky over a field. Rain and Storm rain; a storm also flashes and thunders.
public enum WeatherKind : byte
{
    Clear,
    Overcast,
    Rain,
    Storm
}

// Intensity: the rain's density, 1..255 while it rains and 0 otherwise. The wind blows toward WindHeading (256 steps of
// the compass, 0 toward +Z, 64 toward +X) with the strength Wind (0..255).
public readonly record struct WeatherState(WeatherKind Kind, byte Intensity, byte WindHeading, byte Wind)
{
    public static readonly WeatherState Clear = default;

    public bool Raining => Kind is WeatherKind.Rain or WeatherKind.Storm;
}
