using System.Text.Json;

namespace PristonTale.Effects.Vfx;

// The effect definitions of the game data: every *.vfx.json under <assetRoot>/Effect/Vfx, read and validated off the
// game thread. Files load in ordinal order of their relative path; a later definition of a name replaces an earlier
// one. A file that cannot be read, parsed or validated is reported and skipped as a whole.
public sealed class VfxCatalog
{
    public const string Extension = ".vfx.json";
    public const string Pattern = "*" + Extension;

    public static string FolderOf(string assetRoot) => Path.Combine(assetRoot, "Effect", "Vfx");

    private VfxCatalog(IReadOnlyList<VfxDefinition> definitions, int files, IReadOnlyList<string> errors)
    {
        Definitions = definitions;
        Files = files;
        Errors = errors;
    }

    public IReadOnlyList<VfxDefinition> Definitions { get; }
    public int Files { get; }
    public IReadOnlyList<string> Errors { get; }

    public static VfxCatalog Load(string assetRoot)
    {
        var directory = FolderOf(assetRoot);
        var errors = new List<string>();
        var definitions = new List<VfxDefinition>();
        if (!Directory.Exists(directory))
        {
            return new VfxCatalog(definitions, 0, errors);
        }

        var paths = Directory.EnumerateFiles(directory, Pattern, SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(directory, path).Replace('\\', '/')))
            .OrderBy(static file => file.Relative, StringComparer.Ordinal)
            .ToArray();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, relative) in paths)
        {
            VfxDefinition[] parsed;
            try
            {
                parsed = VfxDefinition.ParseFile(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                errors.Add($"{relative}: {exception.Message}");
                continue;
            }

            foreach (var definition in parsed)
            {
                if (seen.TryGetValue(definition.Name, out var earlier) && earlier != relative)
                {
                    errors.Add($"{relative}: {definition.Name} replaces the definition in {earlier}");
                }

                seen[definition.Name] = relative;
                definitions.Add(definition);
            }
        }

        return new VfxCatalog(definitions, paths.Length, errors);
    }

    // The numbered frame of a texture, "<stem><frame>.<ext>": the number goes before the last dot.
    public static string FrameName(string texture, int frame)
    {
        var dot = texture.LastIndexOf('.');
        return dot > 0 ? $"{texture[..dot]}{frame}{texture[dot..]}" : $"{texture}{frame}";
    }

    // A texture path, or its numbered frames 1 .. frames when frames > 1.
    public static IEnumerable<string> Frames(string texture, int frames)
    {
        if (frames <= 1)
        {
            yield return texture;
            yield break;
        }

        for (var frame = 1; frame <= frames; frame++)
        {
            yield return FrameName(texture, frame);
        }
    }

    // Asset paths the definitions read: textures (with numbered frames) and models with their skeletons.
    public static IEnumerable<string> Textures(VfxDefinition definition)
    {
        foreach (var emitter in definition.Emitters)
        {
            yield return emitter.Texture;
        }

        foreach (var trail in definition.Trails)
        {
            yield return trail.Texture;
        }

        foreach (var sequence in definition.Sequences)
        {
            yield return sequence.Texture;
        }

        foreach (var part in definition.Parts)
        {
            if (part.Shape != VfxPartShape.Model && part.Animation.Length == 0 && part.Texture.Length > 0)
            {
                foreach (var frame in Frames(part.Texture, part.Frames))
                {
                    yield return frame;
                }
            }
        }

        foreach (var component in definition.Components)
        {
            if (component.Kind == VfxComponentKind.Model || component.Frames <= 0)
            {
                yield return component.Texture;
                continue;
            }

            for (var frame = 1; frame <= component.Frames; frame++)
            {
                yield return FrameName(component.Texture, frame);
            }
        }
    }
}
