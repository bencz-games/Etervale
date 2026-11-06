using System.Collections.Concurrent;
using System.Text;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Assets;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Rendering;

// Text files of the client's data in one encoding, read on the thread pool and kept: until a file is read its lines are
// null, and a missing or unreadable file reads as no lines. Tabs become spaces, lines lose their trailing spaces and the
// text its blank lines before and after. A translation beside the executable, Texts/<language>/<the file's path> in
// UTF-8, is read instead when the shown language has one (English too, for data written in another language); each
// language keeps its own copies.
public sealed class HudTextFiles(string assetRoot, Encoding encoding, IClientLog log, string tag, string translationRoot = null)
{
    private readonly string _translationRoot = translationRoot ?? DefaultTranslationRoot;
    private readonly ConcurrentDictionary<string, LanguageTexts> _languages = new(StringComparer.Ordinal);

    public static string DefaultTranslationRoot => Path.Combine(AppContext.BaseDirectory, "Texts");

    public bool IsLoading => _languages.Values.Any(static texts => !texts.Loading.IsEmpty);

    // The lines of a file in the shown language, or null while it loads.
    public string[] Lines(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return [];
        }

        var language = ClientText.Language;
        var texts = _languages.GetOrAdd(language, static _ => new LanguageTexts());
        if (texts.Lines.TryGetValue(relativePath, out var lines))
        {
            return lines;
        }

        if (texts.Loading.TryAdd(relativePath, 0))
        {
            _ = Task.Run(() => Load(texts, language, relativePath));
        }

        return null;
    }

    public static string[] Decode(byte[] bytes, Encoding encoding)
    {
        var text = encoding.GetString(bytes).Replace('\t', ' ');
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
        {
            first++;
        }

        var last = lines.Length;
        while (last > first && lines[last - 1].Trim().Length == 0)
        {
            last--;
        }

        return lines[first..last].Select(static line => line.TrimEnd()).ToArray();
    }

    private void Load(LanguageTexts texts, string language, string relativePath)
    {
        string[] lines = [];
        try
        {
            var translated = Path.Combine(_translationRoot, language, relativePath);
            var path = Path.Combine(assetRoot, relativePath);
            if (File.Exists(translated))
            {
                lines = Decode(File.ReadAllBytes(translated), Encoding.UTF8);
            }
            else if (AssetFiles.Exists(path))
            {
                lines = Decode(AssetFiles.ReadAllBytes(path), encoding);
            }
            else
            {
                log.Warn($"[{tag}] missing text {relativePath}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Error($"[{tag}] cannot read {relativePath}", exception);
        }

        texts.Lines[relativePath] = lines;
        texts.Loading.TryRemove(relativePath, out _);
    }

    private sealed class LanguageTexts
    {
        public ConcurrentDictionary<string, string[]> Lines { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentDictionary<string, byte> Loading { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
