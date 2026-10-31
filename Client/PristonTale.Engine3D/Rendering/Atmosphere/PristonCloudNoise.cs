namespace PristonTale.Engine3D.Rendering.Atmosphere;

// The clouds' tiling noise volume (Atmosphere.fx): Size^3 RGBA8 texels, R the Perlin-Worley shape (billowing cells
// with gradient noise between them) and G, B, A Worley fBm at rising frequencies for the eroding detail. Every channel
// wraps at the volume's edges and is stretched to the full range, so the coverage threshold carves clear sky.
public static class PristonCloudNoise
{
    public const int Size = 64;

    public static byte[] Generate(int size = Size)
    {
        var texels = new byte[size * size * size * 4];
        Parallel.For(0, size, z =>
        {
            Span<float> rgba = stackalloc float[4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    At((x + 0.5f) / size, (y + 0.5f) / size, (z + 0.5f) / size, rgba);
                    var texel = ((z * size + y) * size + x) * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        texels[texel + channel] = (byte)MathF.Round(Math.Clamp(rgba[channel], 0f, 1f) * 255f);
                    }
                }
            }
        });

        for (var channel = 0; channel < 4; channel++)
        {
            int least = 255, most = 0;
            for (var index = channel; index < texels.Length; index += 4)
            {
                least = Math.Min(least, texels[index]);
                most = Math.Max(most, texels[index]);
            }

            var span = Math.Max(most - least, 1);
            for (var index = channel; index < texels.Length; index += 4)
            {
                texels[index] = (byte)((texels[index] - least) * 255 / span);
            }
        }

        return texels;
    }

    // The four channels at a point of the unit cube (wrapping).
    public static void At(float x, float y, float z, Span<float> rgba)
    {
        Span<float> worley = stackalloc float[5];
        for (var octave = 0; octave < worley.Length; octave++)
        {
            worley[octave] = Worley(x, y, z, 4 << octave);
        }

        float perlin = 0f, amplitude = 1f, total = 0f;
        for (var octave = 0; octave < 4; octave++)
        {
            perlin += Perlin(x, y, z, 4 << octave) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
        }

        perlin = Math.Clamp(0.5f + 0.7f * perlin / total, 0f, 1f);
        var shape = Fbm(worley, 0);
        rgba[0] = Math.Clamp(Remap(perlin, shape - 1f, 1f, 0f, 1f), 0f, 1f);
        rgba[1] = shape;
        rgba[2] = Fbm(worley, 1);
        rgba[3] = Fbm(worley, 2);
    }

    private static float Fbm(ReadOnlySpan<float> worley, int first) => worley[first] * 0.625f + worley[first + 1] * 0.25f + worley[first + 2] * 0.125f;

    private static float Remap(float value, float low, float high, float newLow, float newHigh) =>
        newLow + (value - low) * (newHigh - newLow) / MathF.Max(high - low, 1e-6f);

    private static uint Hash(int x, int y, int z, uint seed)
    {
        var h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu ^ seed * 0x165667B1u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }

    private static float Unit(uint h) => (h & 0xFFFFFF) / 16777216f;

    private static int Wrap(int i, int period) => ((i % period) + period) % period;

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    // Gradient noise on a lattice of period cells per unit, about -1..1.
    private static float Perlin(float x, float y, float z, int period)
    {
        x *= period;
        y *= period;
        z *= period;
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float fx = x - xi, fy = y - yi, fz = z - zi;
        Span<float> corners = stackalloc float[8];
        for (var corner = 0; corner < 8; corner++)
        {
            int dx = corner & 1, dy = (corner >> 1) & 1, dz = corner >> 2;
            var h = Hash(Wrap(xi + dx, period), Wrap(yi + dy, period), Wrap(zi + dz, period), 7u) % 12;
            // The twelve edge directions of a cube.
            var (gx, gy, gz) = h switch
            {
                0 => (1, 1, 0), 1 => (-1, 1, 0), 2 => (1, -1, 0), 3 => (-1, -1, 0),
                4 => (1, 0, 1), 5 => (-1, 0, 1), 6 => (1, 0, -1), 7 => (-1, 0, -1),
                8 => (0, 1, 1), 9 => (0, -1, 1), 10 => (0, 1, -1), _ => (0, -1, -1)
            };
            corners[corner] = gx * (fx - dx) + gy * (fy - dy) + gz * (fz - dz);
        }

        float u = Fade(fx), v = Fade(fy), w = Fade(fz);
        var near = float.Lerp(float.Lerp(corners[0], corners[1], u), float.Lerp(corners[2], corners[3], u), v);
        var far = float.Lerp(float.Lerp(corners[4], corners[5], u), float.Lerp(corners[6], corners[7], u), v);
        return float.Lerp(near, far, w);
    }

    // One minus the distance to the nearest of one random point per cell (cells per unit, wrapping): bright cell
    // cores and dark borders.
    private static float Worley(float x, float y, float z, int cells)
    {
        x *= cells;
        y *= cells;
        z *= cells;
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        var nearest = float.MaxValue;
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                    var h = Hash(Wrap(cx, cells), Wrap(cy, cells), Wrap(cz, cells), 13u);
                    var px = cx + Unit(h) - x;
                    var py = cy + Unit(Hash((int)h, 1, 2, 3u)) - y;
                    var pz = cz + Unit(Hash((int)h, 4, 5, 6u)) - z;
                    nearest = MathF.Min(nearest, px * px + py * py + pz * pz);
                }
            }
        }

        return Math.Clamp(1f - MathF.Sqrt(nearest), 0f, 1f);
    }
}
