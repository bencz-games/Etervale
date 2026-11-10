using PristonTale.Assets;

namespace PristonTale.Tests;

// The test-only models in tests/assets (not distributed, ignored by git; docs/testing.md), found from the working
// directory or the test executable upward. Null when the folder or the file is absent, so a test skips.
internal static class TestAssets
{
    public static string? Find(string relative)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var root = Path.Combine(directory.FullName, "tests", "assets");
                if (Directory.Exists(root))
                {
                    return AssetFiles.ResolveFile(root, relative);
                }
            }
        }

        return null;
    }
}
