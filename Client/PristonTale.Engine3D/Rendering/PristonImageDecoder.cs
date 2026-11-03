using Microsoft.Xna.Framework;
using StbImageSharp;

namespace PristonTale.Engine3D.Rendering;

public sealed record DecodedPristonImage(int Width, int Height, Color[] Pixels, bool HasSourceAlpha)
{
    public static readonly DecodedPristonImage Empty = new(0, 0, [], false);
    public bool IsValid => Width > 0 && Height > 0 && (long)Width * Height == Pixels.Length;
}

public static class PristonImageDecoder
{
    private const long MaximumPixels = 64_000_000;

    // UI bitmaps pass bmpCoverage: the fourth byte of a 32-bit BMP is then its coverage, unless every byte is 0 (reserved
    // data) or 255. World BMPs stay opaque.
    public static DecodedPristonImage Decode(byte[] bytes, string extension, bool bmpCoverage = false)
    {
        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return LoadTga(bytes, out _);
        }

        if (bytes.Length >= 54 && (IsBitmapHeader(bytes) || bytes[0] == 'A' && bytes[1] == '8' || bytes[0] == 'C' && bytes[1] == '8'))
        {
            return LoadBmp(bytes, bmpCoverage);
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var info = ImageInfo.FromStream(stream);
        if (info is null || info.Value.Width <= 0 || info.Value.Height <= 0 || (long)info.Value.Width * info.Value.Height > MaximumPixels)
        {
            return DecodedPristonImage.Empty;
        }

        stream.Position = 0;
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var pixels = new Color[image.Width * image.Height];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            pixels[index] = new Color(image.Data[offset], image.Data[offset + 1], image.Data[offset + 2], image.Data[offset + 3]);
        }

        return new DecodedPristonImage(image.Width, image.Height, pixels, true);
    }

    private static DecodedPristonImage LoadBmp(byte[] source, bool coverage)
    {
        var bytes = (byte[])source.Clone();
        var a8Header = bytes[0] == 'A' && bytes[1] == '8';
        if (a8Header)
        {
            for (var i = 0; i < 14; i++)
            {
                bytes[i] = unchecked((byte)(bytes[i] - i * i));
            }
        }

        var pixelOffset = BitConverter.ToInt32(bytes, 10);
        var dibSize = BitConverter.ToInt32(bytes, 14);
        if (dibSize < 40 || bytes.Length < 14 + dibSize)
        {
            return DecodedPristonImage.Empty;
        }

        // Some original 8-bit alpha files leave bfOffBits zero; their pixels follow the DIB header and palette.
        if (a8Header && pixelOffset == 0)
        {
            var inferredBitCount = BitConverter.ToUInt16(bytes, 28);
            var colorsUsed = BitConverter.ToInt32(bytes, 46);
            var paletteBytes = inferredBitCount == 8 ? 4L * (colorsUsed != 0 ? colorsUsed : 256) : 0L;
            var inferred = 14L + dibSize + paletteBytes;
            if (inferred > bytes.Length)
            {
                return DecodedPristonImage.Empty;
            }

            pixelOffset = (int)inferred;
        }

        var width = BitConverter.ToInt32(bytes, 18);
        var heightRaw = BitConverter.ToInt32(bytes, 22);
        var planes = BitConverter.ToUInt16(bytes, 26);
        var bitCount = BitConverter.ToUInt16(bytes, 28);
        var compression = BitConverter.ToInt32(bytes, 30);
        var paletteCount = BitConverter.ToInt32(bytes, 46);
        if (width <= 0 || heightRaw == 0 || planes != 1 || compression != 0)
        {
            return DecodedPristonImage.Empty;
        }

        var topDown = heightRaw < 0;
        if (heightRaw == int.MinValue)
        {
            return DecodedPristonImage.Empty;
        }

        var height = Math.Abs(heightRaw);
        if ((long)width * height > MaximumPixels)
        {
            return DecodedPristonImage.Empty;
        }

        if (pixelOffset <= 0 || pixelOffset >= bytes.Length)
        {
            return DecodedPristonImage.Empty;
        }

        var palette = Array.Empty<Color>();
        if (bitCount <= 8)
        {
            var colors = paletteCount > 0 ? paletteCount : 1 << bitCount;
            palette = new Color[colors];
            var paletteOffset = 14 + dibSize;
            if (paletteOffset + colors * 4 > bytes.Length)
            {
                return DecodedPristonImage.Empty;
            }

            for (var i = 0; i < colors; i++)
            {
                var p = paletteOffset + i * 4;
                palette[i] = new Color(bytes[p + 2], bytes[p + 1], bytes[p], (byte)255);
            }
        }

        var rowStride = ((width * bitCount + 31) / 32) * 4;
        if (pixelOffset + (long)rowStride * height > bytes.Length)
        {
            return DecodedPristonImage.Empty;
        }

        var pixels = new Color[width * height];
        var readCoverage = coverage && bitCount == 32;
        bool anyCoverage = false, anyTransparency = false;
        for (var y = 0; y < height; y++)
        {
            var sourceY = topDown ? y : height - 1 - y;
            var row = pixelOffset + sourceY * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixel = ReadBmpPixel(bytes, row, x, bitCount, palette);
                if (readCoverage)
                {
                    pixel.A = bytes[row + x * 4 + 3];
                    anyCoverage |= pixel.A != 0;
                    anyTransparency |= pixel.A != 255;
                }

                pixels[y * width + x] = pixel;
            }
        }

        if (readCoverage && !anyCoverage)
        {
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index].A = 255;
            }
        }

        return new DecodedPristonImage(width, height, pixels, readCoverage && anyCoverage && anyTransparency);
    }

    private static Color ReadBmpPixel(byte[] bytes, int row, int x, int bitCount, Color[] palette)
    {
        switch (bitCount)
        {
            case 32:
                {
                    var offset = row + x * 4;
                    return new Color(bytes[offset + 2], bytes[offset + 1], bytes[offset], (byte)255);
                }

            case 24:
                {
                    var offset = row + x * 3;
                    return new Color(bytes[offset + 2], bytes[offset + 1], bytes[offset], (byte)255);
                }

            case 8:
                return palette[bytes[row + x]];
            case 4:
                {
                    var packed = bytes[row + x / 2];
                    var index = (x & 1) == 0 ? packed >> 4 : packed & 0x0F;
                    return palette[index];
                }

            case 1:
                {
                    var packed = bytes[row + x / 8];
                    var index = (packed >> (7 - x % 8)) & 1;
                    return palette[index];
                }

            default:
                return Color.White;
        }
    }

    private static bool IsBitmapHeader(byte[] bytes) => bytes[0] == 'B' && bytes[1] == 'M';

    private static DecodedPristonImage LoadTga(byte[] bytes, out bool hasSourceAlpha)
    {
        hasSourceAlpha = false;
        if (bytes.Length < 18)
        {
            return DecodedPristonImage.Empty;
        }

        var encrypted = bytes[0] == 'G' && bytes[1] == '8';
        if (bytes[0] == 'G' && bytes[1] == '8')
        {
            bytes = (byte[])bytes.Clone();
            for (var i = 0; i < 18; i++)
            {
                bytes[i] = unchecked((byte)(bytes[i] - i * i));
            }
        }

        var idLength = bytes[0];
        var colorMapType = bytes[1];
        var imageType = bytes[2];
        if (!encrypted && (colorMapType != 0 || imageType is not (TgaTrueColor or TgaRunLength)))
        {
            return DecodedPristonImage.Empty;
        }

        var width = BitConverter.ToUInt16(bytes, 12);
        var height = BitConverter.ToUInt16(bytes, 14);
        var bits = bytes[16];
        var descriptor = bytes[17];
        if (width == 0 || height == 0 || bits is not (16 or 24 or 32))
        {
            return DecodedPristonImage.Empty;
        }

        hasSourceAlpha = bits == 32;
        var src = encrypted ? 18 : 18 + idLength;
        var pixelSize = bits / 8;
        if ((long)width * height > MaximumPixels)
        {
            return DecodedPristonImage.Empty;
        }

        var count = width * height;

        // The pixels in file order: stored as they are, or (run-length) as packets whose header's high bit repeats one
        // pixel (low 7 bits + 1) times and otherwise introduces that many stored pixels.
        var stream = new Color[count];
        if (imageType == TgaRunLength)
        {
            var filled = 0;
            while (filled < count)
            {
                if (src >= bytes.Length)
                {
                    return DecodedPristonImage.Empty;
                }

                var header = bytes[src++];
                var run = Math.Min((header & 0x7F) + 1, count - filled);
                var repeated = (header & 0x80) != 0;
                if (src + (long)(repeated ? 1 : run) * pixelSize > bytes.Length)
                {
                    return DecodedPristonImage.Empty;
                }

                if (repeated)
                {
                    var pixel = ReadTgaPixel(bytes, ref src, bits);
                    Array.Fill(stream, pixel, filled, run);
                    filled += run;
                    continue;
                }

                for (var index = 0; index < run; index++)
                {
                    stream[filled++] = ReadTgaPixel(bytes, ref src, bits);
                }
            }
        }
        else
        {
            if (src + (long)count * pixelSize > bytes.Length)
            {
                return DecodedPristonImage.Empty;
            }

            for (var index = 0; index < count; index++)
            {
                stream[index] = ReadTgaPixel(bytes, ref src, bits);
            }
        }

        var colors = new Color[count];
        var topOrigin = (descriptor & 0x20) != 0;
        for (var y = 0; y < height; y++)
        {
            var destY = topOrigin ? y : height - 1 - y;
            Array.Copy(stream, y * width, colors, destY * width, width);
        }

        return new DecodedPristonImage(width, height, colors, hasSourceAlpha);
    }

    private const byte TgaTrueColor = 2;
    private const byte TgaRunLength = 10;

    private static Color ReadTgaPixel(byte[] bytes, ref int src, int bits)
    {
        if (bits == 16)
        {
            var value = BitConverter.ToUInt16(bytes, src);
            src += 2;
            return new Color(Expand5((value >> 10) & 0x1F), Expand5((value >> 5) & 0x1F), Expand5(value & 0x1F), (byte)255);
        }

        var b = bytes[src++];
        var g = bytes[src++];
        var r = bytes[src++];
        var a = bits == 32 ? bytes[src++] : (byte)255;
        return new Color(r, g, b, a);
    }

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

}
