using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Engine3D.Rendering.Post;

// The colour grade of the final pass (Shaders/Passes/PostFinal.fx Grade): a 32^3 LUT from sRGB-encoded display colour
// to sRGB-encoded display colour, stored as 32 slices of 32x32 (blue selects the slice) in a 1024x32 texture. The
// built-in look is a gentle filmic grade; a .cube file (Adobe/Resolve format, any size, display-referred sRGB input)
// replaces it.
internal static class PristonColorGrading
{
    public const int Size = 32;
    // The built-in look: mid-tone contrast, a little more colour, cool shadows and warm highlights.
    private const float Contrast = 0.12f;
    private const float Saturation = 1.06f;
    private const float SplitTone = 0.012f;

    // Reads and samples the grade off the render thread; the pixels are uploaded with Upload.
    public static Color[] BuildPixels(string cubePath, IClientLog log)
    {
        Func<Vector3, Vector3> grade = BuiltInLook;
        if (!string.IsNullOrWhiteSpace(cubePath))
        {
            try
            {
                var cube = CubeLut.Parse(File.ReadAllLines(cubePath));
                grade = cube.Sample;
                log.Info($"[Rendering] colour grade LUT {cube.Size}^3 from '{cubePath}'.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                log.Warn($"[Rendering] colour grade '{cubePath}' unusable ({exception.Message}); using the built-in grade.");
            }
        }

        var pixels = new Color[Size * Size * Size];
        for (var b = 0; b < Size; b++)
        {
            for (var g = 0; g < Size; g++)
            {
                for (var r = 0; r < Size; r++)
                {
                    var input = new Vector3(r, g, b) / (Size - 1);
                    var output = Vector3.Clamp(grade(input), Vector3.Zero, Vector3.One);
                    pixels[g * Size * Size + b * Size + r] = new Color(output);
                }
            }
        }

        return pixels;
    }

    public static Texture2D Upload(GraphicsDevice device, Color[] pixels)
    {
        var texture = new Texture2D(device, Size * Size, Size, false, SurfaceFormat.Color) { Name = "Colour grade LUT" };
        texture.SetData(pixels);
        return texture;
    }

    // Applied to sRGB-encoded display colour.
    internal static Vector3 BuiltInLook(Vector3 encoded)
    {
        var linear = PristonColorSpace.SrgbToLinear(encoded);
        var luminance = Lighting.PristonPhotometry.Luminance(linear);
        linear = Vector3.Max(Vector3.Zero, new Vector3(luminance) + (linear - new Vector3(luminance)) * Saturation);
        var shadows = 1f - MathHelper.Clamp(luminance * 4f, 0f, 1f);
        var highlights = MathHelper.Clamp((luminance - 0.35f) * 2f, 0f, 1f);
        linear += new Vector3(-0.5f, 0f, 1f) * (SplitTone * shadows * 0.25f) + new Vector3(1f, 0.4f, -0.6f) * (SplitTone * highlights);
        var result = new Vector3(Encode(linear.X), Encode(linear.Y), Encode(linear.Z));
        return new Vector3(SCurve(result.X), SCurve(result.Y), SCurve(result.Z));
    }

    private static float SCurve(float x)
    {
        var c = MathHelper.Clamp(x, 0f, 1f);
        var smooth = c * c * (3f - 2f * c);
        return MathHelper.Lerp(c, smooth, Contrast);
    }

    private static float Encode(float value) => PristonColorSpace.LinearToSrgb(MathHelper.Clamp(value, 0f, 1f));

    internal sealed class CubeLut
    {
        private readonly Vector3[] _values;
        private readonly Vector3 _domainMin;
        private readonly Vector3 _domainMax;

        private CubeLut(int size, Vector3[] values, Vector3 domainMin, Vector3 domainMax)
        {
            Size = size;
            _values = values;
            _domainMin = domainMin;
            _domainMax = domainMax;
        }

        public int Size { get; }

        public static CubeLut Parse(IEnumerable<string> lines)
        {
            var size = 0;
            var domainMin = Vector3.Zero;
            var domainMax = Vector3.One;
            var values = new List<Vector3>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("TITLE", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == "LUT_3D_SIZE")
                {
                    size = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    continue;
                }

                if (parts[0] == "DOMAIN_MIN" || parts[0] == "DOMAIN_MAX")
                {
                    var domain = ParseVector(parts, 1);
                    if (parts[0] == "DOMAIN_MIN")
                    {
                        domainMin = domain;
                    }
                    else
                    {
                        domainMax = domain;
                    }

                    continue;
                }

                if (char.IsLetter(parts[0][0]))
                {
                    continue;
                }

                values.Add(ParseVector(parts, 0));
            }

            if (size is < 2 or > 256 || values.Count != size * size * size)
            {
                throw new FormatException($"a 3D LUT of size {size} needs {size * size * size} entries, found {values.Count}");
            }

            return new CubeLut(size, values.ToArray(), domainMin, domainMax);
        }

        private static Vector3 ParseVector(string[] parts, int start)
        {
            if (parts.Length < start + 3)
            {
                throw new FormatException("a LUT line needs three numbers");
            }

            return new Vector3(
                float.Parse(parts[start], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(parts[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(parts[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        // Trilinear lookup; red varies fastest in the file.
        public Vector3 Sample(Vector3 input)
        {
            var range = Vector3.Max(_domainMax - _domainMin, new Vector3(1e-6f));
            var p = Vector3.Clamp((input - _domainMin) / range, Vector3.Zero, Vector3.One) * (Size - 1);
            var r0 = (int)MathF.Floor(p.X);
            var g0 = (int)MathF.Floor(p.Y);
            var b0 = (int)MathF.Floor(p.Z);
            var r1 = Math.Min(r0 + 1, Size - 1);
            var g1 = Math.Min(g0 + 1, Size - 1);
            var b1 = Math.Min(b0 + 1, Size - 1);
            var f = p - new Vector3(r0, g0, b0);
            var c00 = Vector3.Lerp(At(r0, g0, b0), At(r1, g0, b0), f.X);
            var c10 = Vector3.Lerp(At(r0, g1, b0), At(r1, g1, b0), f.X);
            var c01 = Vector3.Lerp(At(r0, g0, b1), At(r1, g0, b1), f.X);
            var c11 = Vector3.Lerp(At(r0, g1, b1), At(r1, g1, b1), f.X);
            return Vector3.Lerp(Vector3.Lerp(c00, c10, f.Y), Vector3.Lerp(c01, c11, f.Y), f.Z);
        }

        private Vector3 At(int r, int g, int b) => _values[(b * Size + g) * Size + r];
    }
}
