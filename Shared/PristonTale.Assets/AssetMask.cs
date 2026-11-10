using System.Text;

namespace PristonTale.Assets;

// Some shipped models, skeletons, model infos, descriptors and textures keep every byte XOR Key. Each format recognises
// its own masked opening; unmasking is the same XOR.
public static class AssetMask
{
    public const byte Key = 0x82;
    private const int TgaHeaderSize = 18;

    public static void Unmask(Span<byte> data)
    {
        for (var index = 0; index < data.Length; index++)
        {
            data[index] ^= Key;
        }
    }

    // A masked SMD or SMB reads its "SMD ..." signature only once unmasked.
    public static bool IsMaskedSmd(ReadOnlySpan<byte> data)
    {
        return data.Length >= SmdLayout.SignatureSize && StartsMasked(data, "SMD "u8);
    }

    // A masked image reads its own opening only once unmasked: "BM", the JPEG or PNG marker, or (a TGA, which has no
    // signature) no colour map and a true-colour or run-length image type.
    public static bool IsMaskedImage(ReadOnlySpan<byte> data, string extension)
    {
        if (data.Length < TgaHeaderSize)
        {
            return false;
        }

        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return !IsTgaOpening(data[1], data[2]) && IsTgaOpening((byte)(data[1] ^ Key), (byte)(data[2] ^ Key));
        }

        return StartsMasked(data, "BM"u8) || StartsMasked(data, [0xFF, 0xD8]) || StartsMasked(data, [0x89, (byte)'P', (byte)'N', (byte)'G']);
    }

    // A zero-padded record (the model info) is masked when its padding reads Key instead of zero.
    public static bool IsMaskedPadding(ReadOnlySpan<byte> data)
    {
        return data.Count(Key) > data.Count((byte)0);
    }

    // The file's bytes, unmasked when the file is a masked SMD or SMB.
    public static byte[] ReadSmd(string path)
    {
        var data = AssetFiles.ReadAllBytes(path);
        if (IsMaskedSmd(data))
        {
            Unmask(data);
        }

        return data;
    }

    // The 24-byte signature as text, read through the mask when there is one.
    public static string SmdSignature(ReadOnlySpan<byte> data)
    {
        Span<byte> signature = stackalloc byte[SmdLayout.SignatureSize];
        var length = Math.Min(data.Length, signature.Length);
        data[..length].CopyTo(signature);
        if (IsMaskedSmd(data))
        {
            Unmask(signature);
        }

        var end = signature[..length].IndexOf((byte)0);
        return Encoding.ASCII.GetString(signature[..(end < 0 ? length : end)]);
    }

    private static bool StartsMasked(ReadOnlySpan<byte> data, ReadOnlySpan<byte> opening)
    {
        return !Starts(data, opening, 0) && Starts(data, opening, Key);
    }

    private static bool Starts(ReadOnlySpan<byte> data, ReadOnlySpan<byte> opening, byte key)
    {
        if (data.Length < opening.Length)
        {
            return false;
        }

        for (var index = 0; index < opening.Length; index++)
        {
            if ((data[index] ^ key) != opening[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTgaOpening(byte colorMapType, byte imageType)
    {
        const byte TrueColor = 2;
        const byte RunLength = 10;
        return colorMapType == 0 && imageType is TrueColor or RunLength;
    }
}
