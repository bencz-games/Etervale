using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.UI.Clans;

// A picture the chief picks for the clan's mark from the ClanMarks folder beside the game: a PNG, JPG or BMP of at most
// MaxFileBytes, whose centred square is fitted to ClanWire.MarkSize (Pixels, what ClanMarkPacket sends) and to
// ClanMarks.Size (Small, as name boxes and windows draw a mark), each pixel the alpha-weighted average of the source pixels
// it covers. Pixels are straight RGBA, rows from the top. Listing and loading read the disk: they run off the game thread.
public sealed record ClanMarkPicture(byte[] Pixels, byte[] Small)
{
    public const long MaxFileBytes = 8 * 1024 * 1024;
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp"];

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "ClanMarks");

    // The pictures in the folder by file name; none when it is missing or unreadable.
    public static IReadOnlyList<string> List(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            return
            [
                .. Directory.EnumerateFiles(folder).Where(static path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .Select(static path => Path.GetFileName(path)).Order(StringComparer.OrdinalIgnoreCase)
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The picture fitted to the mark; null when it is too large or cannot be read or decoded.
    public static ClanMarkPicture Load(string folder, string name)
    {
        try
        {
            var path = Path.Combine(folder, name);
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return null;
            }

            var image = PristonImageDecoder.Decode(File.ReadAllBytes(path), Path.GetExtension(name));
            return image.IsValid
                ? new ClanMarkPicture(Fit(image.Width, image.Height, image.Pixels, ClanWire.MarkSize), Fit(image.Width, image.Height, image.Pixels, ClanMarks.Size))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    public static byte[] Fit(int width, int height, Color[] pixels, int size)
    {
        var side = Math.Min(width, height);
        var left = (width - side) / 2;
        var top = (height - side) / 2;
        var fitted = new byte[size * size * ClanWire.PixelBytes];
        for (var y = 0; y < size; y++)
        {
            var y0 = top + y * side / size;
            var y1 = Math.Max(y0 + 1, top + (y + 1) * side / size);
            for (var x = 0; x < size; x++)
            {
                var x0 = left + x * side / size;
                var x1 = Math.Max(x0 + 1, left + (x + 1) * side / size);
                long red = 0, green = 0, blue = 0, alpha = 0, count = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var pixel = pixels[sy * width + sx];
                        red += pixel.R * pixel.A;
                        green += pixel.G * pixel.A;
                        blue += pixel.B * pixel.A;
                        alpha += pixel.A;
                        count++;
                    }
                }

                var at = (y * size + x) * ClanWire.PixelBytes;
                if (alpha > 0)
                {
                    fitted[at] = (byte)(red / alpha);
                    fitted[at + 1] = (byte)(green / alpha);
                    fitted[at + 2] = (byte)(blue / alpha);
                    fitted[at + 3] = (byte)(alpha / count);
                }
            }
        }

        return fitted;
    }
}
