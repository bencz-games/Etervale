using System.Diagnostics;
using PristonTale.Assets;

namespace PristonTale.Tests;

internal static class AssetFileTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("an asset reference resolves whatever its letter case, and through folders that differ only in case on a case-sensitive file "
            + "system, the exact spelling first", CaseAsync);
    }

    private static Task CaseAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "priston-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (!CaseSensitive(root))
            {
                Console.WriteLine("  (skipped: no case-sensitive directory here)");
                return Task.CompletedTask;
            }

            Write(root, "char", "npcXms", "zizzyadorii.SMD");
            Write(root, "char", "npcxms", "kadomatsu.smd");
            Write(root, "char", "npcxms", "both.smd");
            Write(root, "char", "npcXms", "both.smd");

            Assert.True(Resolved(root, @"char\npcxms\zizzyadorii.smd") == Path.Combine(root, "char", "npcXms", "zizzyadorii.SMD"),
                "a file in npcXms was not found through npcxms");
            Assert.True(Resolved(root, @"Char\NPCXMS\Kadomatsu.SMD") == Path.Combine(root, "char", "npcxms", "kadomatsu.smd"),
                "a file in npcxms was not found through another case");
            Assert.True(Resolved(root, @"char\npcXms\both.smd") == Path.Combine(root, "char", "npcXms", "both.smd")
                && Resolved(root, @"char\npcxms\both.smd") == Path.Combine(root, "char", "npcxms", "both.smd"),
                "a file in both folders did not resolve to the exact spelling");
            Assert.True(!AssetFiles.Exists(Path.Combine(root, "char", "npcxms", "missing.smd")), "a missing file resolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static string Resolved(string root, string reference) => AssetFiles.ResolveFile(root, reference) ?? "";

    private static void Write(string root, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
    }

    // Linux and macOS folders usually are; a Windows folder is when its case-sensitive attribute can be set.
    private static bool CaseSensitive(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var fsutil = Process.Start(new ProcessStartInfo("fsutil.exe", ["file", "setCaseSensitiveInfo", root, "enable"])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                fsutil?.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        Directory.CreateDirectory(Path.Combine(root, "probe"));
        var sensitive = !Directory.Exists(Path.Combine(root, "PROBE"));
        Directory.Delete(Path.Combine(root, "probe"));
        return sensitive;
    }
}
