using PristonTale.Assets;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.HelpPet;

// The helper pet's texts, read from the client's data (code page 949), or in another language than English from its
// translation beside the executable (Texts/<language>/<FilePath>, UTF-8) when there is one: a section opens with a "*Topic" line, holds one
// quoted line per message and closes with "end". A message runs from its first quote to the next one, or to the end of
// the line when the closing quote is missing or is an apostrophe (the file has both); a line of two quoted parts keeps
// the first. Lines outside a section are ignored.
public sealed class HelpPetMessages
{
    public const string FilePath = "image/Sinimage/PetData/PetMessage.txt";

    public static readonly HelpPetMessages Empty = new(new Dictionary<string, string[]>());

    private readonly Dictionary<string, string[]> _topics;

    private HelpPetMessages(Dictionary<string, string[]> topics) => _topics = topics;

    // The messages of a topic (without its leading '*'); none when the file lacks it.
    public string[] Lines(string topic) => _topics.TryGetValue(topic, out var lines) ? lines : [];

    // The pet's name and its dragon line ("Fire Dragon ASTERYAN"), by kind 1 to 4.
    public string Name(int kind) => Entry("PetName", kind);

    public string Lineage(int kind) => Entry("PetKind", kind);

    public static HelpPetMessages Parse(string text)
    {
        var topics = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string topic = null;
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (topic is null)
            {
                if (line.StartsWith('*'))
                {
                    topic = line[1..].Trim();
                    lines.Clear();
                }

                continue;
            }

            if (line == "end")
            {
                topics[topic] = [.. lines];
                topic = null;
                continue;
            }

            if (line.StartsWith('"'))
            {
                var close = line.IndexOf('"', 1);
                var message = close > 0 ? line[1..close] : line[1..].TrimEnd('\'');
                lines.Add(message.Trim());
            }
        }

        return new HelpPetMessages(topics);
    }

    // Reads the file in a language on the thread pool; a missing or unreadable file gives no messages.
    public static Task<HelpPetMessages> LoadAsync(string assetRoot, IClientLog log, string language = ClientSettings.EnglishLanguage,
        string translationRoot = null) => Task.Run(() =>
    {
        var path = Path.Combine(assetRoot, FilePath);
        var translated = language == ClientSettings.EnglishLanguage ? null : Path.Combine(translationRoot ?? HudTextFiles.DefaultTranslationRoot, language, FilePath);
        try
        {
            if (translated is not null && File.Exists(translated))
            {
                return Parse(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(translated)));
            }

            if (AssetFiles.Exists(path))
            {
                return Parse(Cp949.Encoding.GetString(AssetFiles.ReadAllBytes(path)));
            }

            log.Warn($"[HelpPet] missing {FilePath}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Error($"[HelpPet] cannot read {FilePath}", exception);
        }

        return Empty;
    });

    private string Entry(string topic, int kind)
    {
        var lines = Lines(topic);
        return kind >= 1 && kind <= lines.Length ? string.Join(' ', lines[kind - 1].Split(' ', StringSplitOptions.RemoveEmptyEntries)) : "";
    }
}
