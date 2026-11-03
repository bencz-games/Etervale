using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

// The sRGB transfer curve (IEC 61966-2-1) between display-encoded and linear values, both in [0, 1].
public static class PristonColorSpace
{
    public static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    public static Vector3 SrgbToLinear(Vector3 colour) => new(SrgbToLinear(colour.X), SrgbToLinear(colour.Y), SrgbToLinear(colour.Z));

    public static float LinearToSrgb(float value) =>
        value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
}
