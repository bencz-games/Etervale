using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Weather;

// The weather the renderer draws, as the presentation blends it: cloud cover and rain (0..1), the lightning flash (0..1)
// and the wind on the ground plane in world units per second; the strike's compass heading (radians) lights the clouds
// on its side; SunBreak (0..1) a break in the clouds passing the sun.
public readonly record struct PristonWeather(float Cloud, float Rain, float Flash, Vector2 Wind, float FlashAzimuth = 0f, float SunBreak = 0f)
{
    public static readonly PristonWeather Clear = default;
}
