using System.Text;
using System.Text.Json;
using PristonTale.Assets;
using PristonTale.Effects.Vfx;
using PristonTale.VfxImport;

// ptvfx import <assetRoot>: converts the original effect group scripts (Effect/NewEffect/*.lua) and particle scripts
// (Effect/Particle/Script/*.part) to effect definitions under <assetRoot>/Effect/Vfx/Scripts and .../Particles, installs
// the hand-authored definitions kept with this tool (Authored/**) at the same relative paths under Effect/Vfx, and
// prints every construct that converted with a caveat.
if (args.Length != 2 || args[0] != "import")
{
    Console.Error.WriteLine("usage: ptvfx import <assetRoot>");
    return 2;
}

var root = args[1];
var result = VfxImporter.Import(root);
foreach (var line in result.Report)
{
    Console.WriteLine(line);
}

Console.WriteLine($"{result.Scripts} effect group scripts and {result.Particles} particle scripts converted, {result.Authored} authored files installed, "
    + $"{result.Failed} failed.");
return result.Failed == 0 ? 0 : 1;

namespace PristonTale.VfxImport
{
    internal sealed record ImportResult(int Scripts, int Particles, int Authored, int Failed, IReadOnlyList<string> Report);

    internal static class VfxImporter
    {
        public const string ScriptsFolder = "Scripts";
        public const string ParticlesFolder = "Particles";

        public static ImportResult Import(string assetRoot)
        {
            var assets = new AssetPaths(assetRoot, "Effect/NewEffect", "Effect/Particle");
            var output = VfxCatalog.FolderOf(assetRoot);
            var report = new List<string>();
            var failed = 0;
            var scripts = 0;
            foreach (var file in Files(AssetFiles.Resolve(AssetFiles.Combine(assetRoot, EffectScriptConverter.Folder)), "*.lua"))
            {
                failed += Write(file, output, ScriptsFolder, report, text => EffectScriptConverter.Convert(Path.GetFileNameWithoutExtension(file), text, assets, report))
                    ? 0 : 1;
                scripts++;
            }

            var particles = 0;
            foreach (var file in Files(AssetFiles.Resolve(AssetFiles.Combine(assetRoot, "Effect/Particle/Script")), "*.part"))
            {
                failed += Write(file, output, ParticlesFolder, report,
                    text => ParticleScriptConverter.Convert(Path.GetFileNameWithoutExtension(file), text, assets, report)) ? 0 : 1;
                particles++;
            }

            var authored = 0;
            var source = Path.Combine(AppContext.BaseDirectory, AuthoredFolder);
            foreach (var file in AuthoredFiles(source))
            {
                failed += Install(source, file, output, report) ? 0 : 1;
                authored++;
            }

            return new ImportResult(scripts, particles, authored, failed, report);
        }

        public const string AuthoredFolder = "Authored";

        public static IEnumerable<string> AuthoredFiles(string source)
        {
            if (!Directory.Exists(source))
            {
                return [];
            }

            return Directory.EnumerateFiles(source, VfxCatalog.Pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal);
        }

        // Authored files are validated before they are copied so a broken one never reaches the game data.
        private static bool Install(string source, string file, string output, List<string> report)
        {
            var relative = Path.GetRelativePath(source, file);
            try
            {
                var text = File.ReadAllText(file);
                foreach (var definition in VfxDefinition.ParseFile(text))
                {
                    definition.Validate();
                }

                var target = Path.Combine(output, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
            {
                report.Add($"{relative}: not installed: {exception.Message}");
                return false;
            }
        }

        private static IEnumerable<string> Files(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            // The original reads both ".lua" and ".Lua"; the directory listing is case-insensitive on the game's platform.
            return Directory.EnumerateFiles(directory)
                .Where(path => Path.GetExtension(path).Equals(Path.GetExtension(pattern), StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal);
        }

        private static bool Write(string file, string output, string folder, List<string> report, Func<string, VfxDefinition> convert)
        {
            try
            {
                // Calls and keywords are ASCII; comments may be in a original code page.
                var definition = convert(File.ReadAllText(file, Encoding.Latin1));
                definition.Validate();
                var directory = Path.Combine(output, folder);
                Directory.CreateDirectory(directory);
                var target = Path.Combine(directory, Path.GetFileNameWithoutExtension(file) + VfxCatalog.Extension);
                File.WriteAllText(target, JsonSerializer.Serialize(new[] { definition }, VfxDefinition.Json) + Environment.NewLine);
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
            {
                report.Add($"{Path.GetFileName(file)}: not converted: {exception.Message}");
                return false;
            }
        }
    }
}
