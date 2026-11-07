namespace PristonTale.Simulation.Items;

// The smelter's and craftsman's materials by catalog family and colour number (two digits): ores PR1nn (8 colours),
// crystals PR2nn and the A (PR3nn) and B (PR4nn) runes (14 colours each). A crystal smelts into its colour's A or B rune.
public static class CraftMaterials
{
    public const string Ore = "PR1";
    public const string Crystal = "PR2";
    public const string RuneA = "PR3";
    public const string RuneB = "PR4";
    public const int Colours = 14;

    // The colour number of a key of the family; 0 for anything else.
    public static int Colour(string family, string key) =>
        key is { Length: 5 } && key.StartsWith(family, StringComparison.OrdinalIgnoreCase) && int.TryParse(key.AsSpan(3), out var number)
        && number is >= 1 and <= Colours ? number : 0;

    public static string Key(string family, int colour) => $"{family}{colour:00}";
}
