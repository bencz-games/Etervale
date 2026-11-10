using PristonTale.Effects.Vfx;
using PristonTale.VfxImport;

namespace PristonTale.Tests;

// The effect definitions the importer installs (tools/PristonTale.VfxImport/Authored), found upward from the runner.
internal static class AuthoredEffects
{
    public static string? Folder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "PristonTale.VfxImport", VfxImporter.AuthoredFolder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // Every effect name defined under a subfolder of the authored folder.
    public static HashSet<string> Names(string subfolder)
    {
        var folder = Folder();
        Assert.True(folder is not null, "the importer's Authored folder was not found");
        return Directory.EnumerateFiles(Path.Combine(folder!, subfolder), VfxCatalog.Pattern, SearchOption.AllDirectories)
            .SelectMany(file => VfxDefinition.ParseFile(File.ReadAllText(file)))
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}
