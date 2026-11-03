using System.Text;
using PristonTale.Assets;

namespace PristonTale.DataImport;

internal sealed record CopyResult(int Copied, int Kept, int Skipped, int Rejected, IReadOnlyList<string> Report);

// Copies folders or files of another client's game data into the game data. A file already in the game data is kept as
// it is; a file of a kind the game does not read is skipped; a model, stage or image the game could not read (an SMD
// signature the game's reader does not know, a damaged header) is refused and reported.
internal static class DataCopier
{
    private static readonly HashSet<string> Copied = new(StringComparer.OrdinalIgnoreCase)
    {
        ".smd", ".smb", ".bmp", ".tga", ".png", ".jpg", ".wav"
    };

    public static CopyResult Copy(string sourceRoot, string assetRoot, IEnumerable<string> paths, byte mask)
    {
        var report = new List<string>();
        int copied = 0, kept = 0, skipped = 0, rejected = 0;
        foreach (var path in paths)
        {
            var source = Path.Combine(sourceRoot, path);
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                report.Add($"missing: {source}");
                rejected++;
                continue;
            }

            IEnumerable<string> files = File.Exists(source)
                ? [source]
                : Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(sourceRoot, file);
                if (!Copied.Contains(Path.GetExtension(file)))
                {
                    report.Add($"skipped {relative}");
                    skipped++;
                    continue;
                }

                var target = Path.Combine(assetRoot, relative);
                if (File.Exists(target))
                {
                    kept++;
                    continue;
                }

                var bytes = File.ReadAllBytes(file);
                if (mask != 0)
                {
                    for (var index = 0; index < bytes.Length; index++)
                    {
                        bytes[index] ^= mask;
                    }
                }

                var problem = Problem(Path.GetExtension(file), bytes);
                if (problem is not null)
                {
                    report.Add($"rejected {relative}: {problem}");
                    rejected++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                report.Add($"copied {relative}");
                copied++;
            }
        }

        return new CopyResult(copied, kept, skipped, rejected, report);
    }

    // Why the game could not read the file, or null when it can.
    private static string? Problem(string extension, byte[] bytes)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".smd":
            case ".smb":
                {
                    if (bytes.Length < SmdLayout.SignatureSize)
                    {
                        return "shorter than its signature";
                    }

                    var signature = Encoding.ASCII.GetString(bytes, 0, SmdLayout.SignatureSize).TrimEnd('\0');
                    return signature is SmdLayout.StageSignature or SmdLayout.StageSignature73 or SmdLayout.ModelSignature
                        ? null
                        : $"signature \"{Printable(signature)}\"";
                }

            case ".bmp":
                return bytes.Length >= 54 && (Starts(bytes, 'B', 'M') || Starts(bytes, 'A', '8') || Starts(bytes, 'C', '8'))
                    ? null
                    : "not a bitmap";
            case ".tga":
                // A masked header ("G8") hides the image type; a plain one must be true colour, stored or run-length.
                return bytes.Length >= 18 && (Starts(bytes, 'G', '8') || bytes[1] == 0 && bytes[2] is 2 or 10)
                    ? null
                    : "not a true-colour TGA";
            case ".png":
                return bytes.Length >= 8 && bytes[0] == 0x89 && Starts(bytes.AsSpan(1), 'P', 'N') ? null : "not a PNG";
            case ".jpg":
                return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? null : "not a JPEG";
            case ".wav":
                return bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" ? null : "not a RIFF wave";
            default:
                return "not game data";
        }
    }

    private static bool Starts(ReadOnlySpan<byte> bytes, char first, char second) => bytes[0] == first && bytes[1] == second;

    private static string Printable(string text) => new(text.Select(static c => c is >= ' ' and < '\x7f' ? c : '?').ToArray());
}
