using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

// Builds a full mip chain on the CPU. Colour is averaged in linear light and weighted by alpha so transparent
// texels do not bleed their key colour into visible edges. Cutout textures keep the base level's alpha-test
// coverage on every level (Castaño 2010), otherwise box-filtered alpha erodes foliage below the cutoff at range.
internal static class PristonMipChain
{
    private const int LinearToSrgbSteps = 16384;
    private const int CoverageSearchIterations = 12;
    private const float MaxCoverageScale = 8f;
    private static readonly float[] SrgbToLinearTable = BuildSrgbToLinear();
    private static readonly byte[] LinearToSrgbTable = BuildLinearToSrgb();

    public static Color[][] Build(Color[] pixels, int width, int height, byte alphaCutoff)
    {
        var levels = new List<Color[]> { pixels };
        var baseCoverage = alphaCutoff == 0 ? -1f : Coverage(pixels, alphaCutoff);
        var preserveCoverage = baseCoverage is > 0f and < 1f;
        var premultiplied = ToLinear(pixels, premultiply: true);
        var straight = ToLinear(pixels, premultiply: false);
        while (width > 1 || height > 1)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);
            premultiplied = Downsample(premultiplied, width, height, nextWidth, nextHeight);
            straight = Downsample(straight, width, height, nextWidth, nextHeight);
            var alphaScale = preserveCoverage ? FindCoverageScale(premultiplied, baseCoverage, alphaCutoff) : 1f;
            levels.Add(ToColors(premultiplied, straight, alphaScale));
            width = nextWidth;
            height = nextHeight;
        }

        return levels.ToArray();
    }

    public static int LevelCount(int width, int height)
    {
        var count = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            count++;
        }

        return count;
    }

    internal static float SrgbToLinear(byte value) => SrgbToLinearTable[value];

    internal static byte LinearToSrgb(float value)
    {
        var index = (int)(Math.Clamp(value, 0f, 1f) * (LinearToSrgbSteps - 1) + 0.5f);
        return LinearToSrgbTable[index];
    }

    private static Vector4[] ToLinear(Color[] pixels, bool premultiply)
    {
        var result = new Vector4[pixels.Length];
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            var alpha = pixel.A / 255f;
            var weight = premultiply ? alpha : 1f;
            result[index] = new Vector4(
                SrgbToLinearTable[pixel.R] * weight,
                SrgbToLinearTable[pixel.G] * weight,
                SrgbToLinearTable[pixel.B] * weight,
                alpha);
        }

        return result;
    }

    private static Vector4[] Downsample(Vector4[] source, int width, int height, int nextWidth, int nextHeight)
    {
        var result = new Vector4[nextWidth * nextHeight];
        for (var y = 0; y < nextHeight; y++)
        {
            var y0 = Math.Min(y * 2, height - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            for (var x = 0; x < nextWidth; x++)
            {
                var x0 = Math.Min(x * 2, width - 1);
                var x1 = Math.Min(x0 + 1, width - 1);
                var sum = source[y0 * width + x0] + source[y0 * width + x1] + source[y1 * width + x0] + source[y1 * width + x1];
                result[y * nextWidth + x] = sum * 0.25f;
            }
        }

        return result;
    }

    // A fully transparent footprint has no weighted colour; it keeps the unweighted average so bilinear filtering
    // next to cutout edges still sees the authored colour.
    private static Color[] ToColors(Vector4[] premultiplied, Vector4[] straight, float alphaScale)
    {
        var result = new Color[premultiplied.Length];
        for (var index = 0; index < premultiplied.Length; index++)
        {
            var texel = premultiplied[index];
            var alpha = texel.W;
            var colour = alpha > 1e-6f
                ? new Vector3(texel.X, texel.Y, texel.Z) / alpha
                : new Vector3(straight[index].X, straight[index].Y, straight[index].Z);
            var scaledAlpha = Math.Clamp(alpha * alphaScale, 0f, 1f);
            result[index] = new Color(
                LinearToSrgb(colour.X),
                LinearToSrgb(colour.Y),
                LinearToSrgb(colour.Z),
                (byte)(scaledAlpha * 255f + 0.5f));
        }

        return result;
    }

    private static float Coverage(Color[] pixels, byte cutoff)
    {
        var covered = 0;
        for (var index = 0; index < pixels.Length; index++)
        {
            if (pixels[index].A >= cutoff)
            {
                covered++;
            }
        }

        return covered / (float)pixels.Length;
    }

    private static float Coverage(Vector4[] texels, float threshold, float scale)
    {
        var covered = 0;
        for (var index = 0; index < texels.Length; index++)
        {
            if (texels[index].W * scale >= threshold)
            {
                covered++;
            }
        }

        return covered / (float)texels.Length;
    }

    private static float FindCoverageScale(Vector4[] texels, float targetCoverage, byte cutoff)
    {
        var threshold = (cutoff - 0.5f) / 255f;
        var low = 0f;
        var high = MaxCoverageScale;
        for (var iteration = 0; iteration < CoverageSearchIterations; iteration++)
        {
            var middle = (low + high) * 0.5f;
            if (Coverage(texels, threshold, middle) < targetCoverage)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return high;
    }

    private static float[] BuildSrgbToLinear()
    {
        var table = new float[256];
        for (var index = 0; index < table.Length; index++)
        {
            table[index] = PristonColorSpace.SrgbToLinear(index / 255f);
        }

        return table;
    }

    private static byte[] BuildLinearToSrgb()
    {
        var table = new byte[LinearToSrgbSteps];
        for (var index = 0; index < table.Length; index++)
        {
            var encoded = PristonColorSpace.LinearToSrgb(index / (float)(LinearToSrgbSteps - 1));
            table[index] = (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
        }

        return table;
    }
}
