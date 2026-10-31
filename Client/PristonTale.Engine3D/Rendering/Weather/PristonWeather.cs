using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Weather;

// The weather the renderer draws, as the presentation blends it: cloud cover and rain (0..1), the lightning flash (0..1)
// and the wind on the ground plane in world units per second.
public readonly record struct PristonWeather(float Cloud, float Rain, float Flash, Vector2 Wind)
{
    public static readonly PristonWeather Clear = default;
}
