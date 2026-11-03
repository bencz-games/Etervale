namespace PristonTale.Engine3D.Rendering;

// A texture without alpha of its own takes its opacity map's channel mean as alpha. A map that is the texture itself, or
// of another size, is not applied.
public static class PristonOpacityMask
{
    public static bool Applies(string texturePath, string maskPath, int width, int height, int maskWidth, int maskHeight) =>
        maskPath.Length > 0 && !string.Equals(texturePath, maskPath, StringComparison.OrdinalIgnoreCase) && maskWidth == width && maskHeight == height;

    public static byte Alpha(byte red, byte green, byte blue) => (byte)((red + green + blue) / 3);
}
