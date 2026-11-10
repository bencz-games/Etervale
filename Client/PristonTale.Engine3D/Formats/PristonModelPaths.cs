using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

// Where a model, its motions and its model info are read from, whatever name the data gives them. A name in a format the
// engine reads (.smd, .smb, .glb, .gltf, .inx) is that file; any other (a source the original tools took, such as .ase,
// or a descriptor .ini, .inf) tries the compiled form first, then glTF, then the text: a model with no compiled file
// beside it is read from its glTF, and model info with no .inx from its descriptor (PristonModelText).
public static class PristonModelPaths
{
    private static readonly string[] MeshFormats = [".smd", ".glb", ".gltf"];
    private static readonly string[] MotionFormats = [".smb", ".glb", ".gltf"];
    private static readonly string[] InfoFormats = [".inx", ".ini", ".inf"];

    // path: a file on disk (any extension); the model info to read for it, the .inx when none exists.
    public static string Info(string path) => Resolve(path, InfoFormats, explicitFormat: false);

    // root: the assets root; reference: the model as the data names it.
    public static string Mesh(string root, string reference) => Resolve(AssetFiles.Combine(root, reference), MeshFormats, explicitFormat: true);

    public static string Motion(string root, string reference) => Resolve(AssetFiles.Combine(root, reference), MotionFormats, explicitFormat: true);

    public static bool IsGltf(string path) =>
        Path.GetExtension(path) is { } extension
        && (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase));

    public static bool Exists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(AssetFiles.Resolve(path));

    // The first format present; a name already in one of them is kept when explicitFormat (missing or not).
    private static string Resolve(string path, string[] formats, bool explicitFormat)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("Empty model path.");
        }

        var extension = Path.GetExtension(path);
        if (explicitFormat && formats.Any(format => format.Equals(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return AssetFiles.Resolve(path);
        }

        foreach (var format in formats)
        {
            var candidate = AssetFiles.Resolve(PristonInxReader.WithExtension(path, format));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return AssetFiles.Resolve(PristonInxReader.WithExtension(path, formats[0]));
    }
}
