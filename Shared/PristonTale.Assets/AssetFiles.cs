using System.Collections.Concurrent;

namespace PristonTale.Assets;

// Asset references use Windows separators and arbitrary letter case; every read of an asset or source file resolves
// through here so the game runs on case-sensitive file systems. A path that does not resolve comes back as requested,
// for diagnostics, and is not cached.
public static class AssetFiles
{
    // Resolution runs concurrently on the asset hot path.
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    // Joins an asset-relative reference to a root with the platform separator, without touching the disk.
    public static string Combine(string root, string relative) =>
        Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));

    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var resolved = Present(normalized) ? normalized : WalkCaseInsensitive(normalized);
        return Present(resolved) ? Cache.GetOrAdd(path, resolved) : resolved;
    }

    // The file a reference names under a root, or null when there is none.
    public static string? ResolveFile(string root, string relative)
    {
        var resolved = Resolve(Combine(root, relative));
        return File.Exists(resolved) ? resolved : null;
    }

    public static bool Exists(string path) => Present(Resolve(path));

    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(Resolve(path));

    public static Stream OpenRead(string path) => File.OpenRead(Resolve(path));

    // The assets directory in the working directory or the executable's directory, or in one of their ancestors.
    public static string? FindAssetsRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "assets");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool Present(string path) => File.Exists(path) || Directory.Exists(path);

    // Rebuilds the real casing component by component. A case-sensitive file system may hold several entries that differ
    // only in case (npcXms beside npcxms): each is tried, the exact spelling first and then in ordinal order, until one
    // leads to the whole path.
    private static string WalkCaseInsensitive(string normalized)
    {
        var separator = Path.DirectorySeparatorChar;
        var parts = normalized.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return normalized;
        }

        var current = ".";
        var startIndex = 0;
        if (Path.IsPathRooted(normalized))
        {
            current = Path.GetPathRoot(normalized) ?? string.Empty;
            startIndex = current.Split(separator, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        return Walk(current, parts, startIndex) ?? normalized;
    }

    private static string? Walk(string current, string[] parts, int index)
    {
        if (index == parts.Length)
        {
            return current;
        }

        var direct = Path.Combine(current, parts[index]);
        if (Present(direct) && Walk(direct, parts, index + 1) is { } exact)
        {
            return exact;
        }

        if (!Directory.Exists(current))
        {
            return null;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(current).Order(StringComparer.Ordinal))
        {
            if (entry != direct && string.Equals(Path.GetFileName(entry), parts[index], StringComparison.OrdinalIgnoreCase)
                && Walk(entry, parts, index + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
