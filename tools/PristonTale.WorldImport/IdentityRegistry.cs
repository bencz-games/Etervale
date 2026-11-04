using System.Text.Encodings.Web;
using System.Text.Json;

namespace PristonTale.WorldImport;

// The stable id and unique English key of every monster and NPC (Identities/monsters.json, Identities/npcs.json). A
// monster's source is its authored identity, which spawn and boss tables reference byte for byte; an NPC's source is
// its definition file's name (case-insensitive), which field placements name. The catalog and the game use only the id
// and the key. Ids are never reused or renumbered: a source missing from the registry takes the next free id and its
// English label as key (numbered when taken), and the import report lists the entries to add to the file.
public sealed class IdentityRegistry
{
    public sealed record Entry(int Id, string Key, string Source);

    // The registry files' JSON: camel-case members, and text unescaped so a reported entry reads as the file spells it.
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Dictionary<string, Entry> _bySource;
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _ids = [];
    private readonly string _unnamed;
    private int _lastId;

    public IdentityRegistry(string fileName, string unnamed, StringComparer sources, IEnumerable<Entry> entries)
    {
        FileName = fileName;
        _bySource = new Dictionary<string, Entry>(sources);
        _unnamed = unnamed;
        foreach (var entry in entries)
        {
            if (entry.Id <= 0 || entry.Key.Length == 0 || !_ids.Add(entry.Id) || !_keys.Add(entry.Key) || !_bySource.TryAdd(entry.Source, entry))
            {
                throw new InvalidDataException($"{fileName}: entry {entry.Id} '{entry.Key}' has no id or key, or repeats an id, a key or an identity.");
            }

            _lastId = Math.Max(_lastId, entry.Id);
        }
    }

    public string FileName { get; }

    // The highest id registered or given out; an unregistered identity takes the next.
    public int LastId => _lastId;

    // Identities this import met that the registry file lacks, in the order met.
    public List<Entry> Added { get; } = [];

    public static IdentityRegistry Monsters() => Load("monsters.json", "Monster", StringComparer.Ordinal);

    public static IdentityRegistry Npcs() => Load("npcs.json", "NPC", StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string source, out Entry entry) => _bySource.TryGetValue(source, out entry!);

    public bool HasKey(string key) => _keys.Contains(key);

    public Entry Resolve(string source, string label)
    {
        if (_bySource.TryGetValue(source, out var known))
        {
            return known;
        }

        var id = ++_lastId;
        var baseKey = KeyFrom(label);
        if (baseKey.Length == 0)
        {
            baseKey = $"{_unnamed} {id}";
        }

        var key = baseKey;
        for (var number = 2; !_keys.Add(key); number++)
        {
            key = $"{baseKey} {number}";
        }

        var entry = new Entry(id, key, source);
        _bySource[source] = entry;
        Added.Add(entry);
        return entry;
    }

    // A registry entry as the line to add to its file.
    public static string Line<T>(T entry) => JsonSerializer.Serialize(entry, Json);

    // An English label as a key: trimmed, each word capitalised when the label is all lower case; empty when the label
    // is not plain ASCII text.
    private static string KeyFrom(string label)
    {
        var text = string.Join(' ', label.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0 || text.Any(static c => c > 0x7E || char.IsControl(c)))
        {
            return "";
        }

        return text == text.ToLowerInvariant() ? string.Join(' ', text.Split(' ').Select(static word => char.ToUpperInvariant(word[0]) + word[1..])) : text;
    }

    private static IdentityRegistry Load(string fileName, string unnamed, StringComparer sources) =>
        new(fileName, unnamed, sources, ReadEntries<Entry>(fileName));

    // The entries of a registry file embedded from Identities/.
    internal static List<T> ReadEntries<T>(string fileName)
    {
        using var stream = typeof(IdentityRegistry).Assembly.GetManifestResourceStream($"PristonTale.WorldImport.Identities.{fileName}")
            ?? throw new InvalidOperationException($"Identities/{fileName} is not embedded in the importer.");
        return JsonSerializer.Deserialize<List<T>>(stream, Json) ?? [];
    }
}
