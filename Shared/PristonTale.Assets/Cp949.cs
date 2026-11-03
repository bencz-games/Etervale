using System.Text;

namespace PristonTale.Assets;

// Code page 949 (Unified Hangul Code), the encoding of the authored Korean text files and of the bytes the talk-sound
// checksum is computed over. .NET ships it only through the code-pages provider, registered on first use.
public static class Cp949
{
    private static readonly Lazy<Encoding> Instance = new(static () =>
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(949);
    });

    public static Encoding Encoding => Instance.Value;

    public static string GetString(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);

    // The lines of a file, with CR LF and lone CR read as line breaks.
    public static IReadOnlyList<string> ReadLines(string path) => Lines(File.ReadAllBytes(path));

    public static string[] Lines(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    // A fixed-length field of a binary record: the bytes up to the first NUL.
    public static string GetNullTerminated(ReadOnlySpan<byte> field)
    {
        var length = field.IndexOf((byte)0);
        return Encoding.GetString(length < 0 ? field : field[..length]);
    }
}
