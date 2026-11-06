using System.Globalization;
using System.Resources;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Texts;
using System.Collections.Concurrent;

namespace PristonTale.Gameplay.Localization;

// The client's phrases: ClientText.resx holds the English ones, ClientText.<language>.resx their translations, keyed
// by name. The shown language follows client-settings.json (Use); a phrase a translation lacks falls back to English.
// Formats take their arguments in invariant culture.
public static class ClientText
{
    private static readonly ResourceManager Resources = new(typeof(ClientText));
    private static readonly ConcurrentDictionary<GameTextId, string> GameKeys = new();
    private static CultureInfo _culture = CultureInfo.GetCultureInfo(ClientSettings.EnglishLanguage);

    public static string Language { get; private set; } = ClientSettings.EnglishLanguage;

    public static CultureInfo Culture => _culture;

    public static void Use(string language)
    {
        var normalized = ClientSettings.NormalizeLanguage(language);
        if (normalized != Language)
        {
            Language = normalized;
            _culture = CultureInfo.GetCultureInfo(normalized);
        }
    }

    public static string Get(string key) => Resources.GetString(key, _culture) ?? key;

    public static string Format(string key, object arg0) => string.Format(CultureInfo.InvariantCulture, Get(key), arg0);

    public static string Format(string key, object arg0, object arg1) => string.Format(CultureInfo.InvariantCulture, Get(key), arg0, arg1);

    public static string Format(string key, object arg0, object arg1, object arg2) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), arg0, arg1, arg2);

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);

    // A line of the server or the simulation, worded by the phrase GameKey names.
    public static string Format(GameText text) => text.IsNone ? ""
        : text.Args.IsEmpty ? Get(GameKey(text.Id))
        : string.Format(CultureInfo.InvariantCulture, Get(GameKey(text.Id)), [.. text.Args]);

    public static string Get(GameTextId id) => Get(GameKey(id));

    // Built once per id: the HUD words some game texts every frame.
    public static string GameKey(GameTextId id) => GameKeys.GetOrAdd(id, static key => "Game." + key);
}
