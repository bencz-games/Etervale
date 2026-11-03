using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// The random draws of the effect layers, all from the instance's sequence (VfxInstance.NextUnit). A vector draws its
// components in x, y, z, w order.
internal static class VfxRandom
{
    public static float Range(float min, float max, ref uint random) => min + (max - min) * VfxInstance.NextUnit(ref random);

    // A [min, max] pair.
    public static float Range(float[] range, ref uint random) => Range(range[0], range[1], ref random);

    public static Vector3 Box(Vector3 min, Vector3 max, ref uint random) =>
        new(Range(min.X, max.X, ref random), Range(min.Y, max.Y, ref random), Range(min.Z, max.Z, ref random));

    public static Vector4 Box(Vector4 min, Vector4 max, ref uint random) =>
        new(Range(min.X, max.X, ref random), Range(min.Y, max.Y, ref random), Range(min.Z, max.Z, ref random), Range(min.W, max.W, ref random));

    // The direction on the unit sphere at height z (-1 to 1) and this angle around the vertical, uniform over the sphere
    // when both are drawn uniformly.
    public static Vector3 SphereDirection(float z, float angle)
    {
        var planar = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(MathF.Cos(angle) * planar, z, MathF.Sin(angle) * planar);
    }

    // The direction of a vector; zero for a zero vector.
    public static Vector3 SafeNormalize(Vector3 value) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : Vector3.Zero;
}
