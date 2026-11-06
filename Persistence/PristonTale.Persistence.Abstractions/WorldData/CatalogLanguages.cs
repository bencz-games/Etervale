namespace PristonTale.Persistence.Abstractions.WorldData;

// Languages of the catalog's player-facing texts (the languages table, BCP 47 codes). Every name and dialog line has an
// English text; a text missing in another language falls back to it.
public static class CatalogLanguages
{
    public const string English = "en";
    public const string BrazilianPortuguese = "pt-BR";
    public static IReadOnlyList<string> Supported { get; } = [English, BrazilianPortuguese];

    public static string Normalize(string? language)
    {
        foreach (var supported in Supported)
        {
            if (string.Equals(language, supported, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return English;
    }
}
