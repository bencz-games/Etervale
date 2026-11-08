using System.Globalization;

namespace PristonTale.Simulation.Items;

// The 32-bit item code of a catalog key: a key is a family of two letters and a letter or digit ("WA1", "DA3") followed by
// a two-digit ordinal, and its code is family << 8 | ordinal. Every family has a code, so items of a new family need no
// registration; codes are stable because they only depend on the key.
public static class ItemCodes
{
    private const int FamilySymbols = 36;

    // The key, code and name gold shows as: its ground model, icon and coin sound.
    public const string GoldKey = "GG101";
    public const string GoldName = "Gold";
    public static readonly uint GoldCode = Parse(GoldKey);

    // The Mystic mana potion, which Bless Castle's fountain and Bellatra's end give.
    public static readonly uint MysticManaPotion = Parse("PM104");

    public static bool TryParse(string key, out uint code)
    {
        code = 0;
        if (key is null || key.Length != 5 || !char.IsAsciiDigit(key[3]) || !char.IsAsciiDigit(key[4])
            || !TryFamily(key.AsSpan(0, 3), out var family))
        {
            return false;
        }

        code = family << 8 | (uint)((key[3] - '0') * 10 + (key[4] - '0'));
        return true;
    }

    public static uint Parse(string key) =>
        TryParse(key, out var code) ? code : throw new FormatException($"'{key}' is not an item key.");

    // The key of a code; empty for 0, an empty slot, or a code no key spells.
    public static string KeyOf(uint code) => TryFormat(code, out var key) ? key : "";

    public static bool TryFormat(uint code, out string key)
    {
        key = "";
        var ordinal = code & 0xFF;
        var family = (code >> 8) - 1;
        if (ordinal > 99 || family >= 26 * 26 * FamilySymbols)
        {
            return false;
        }

        var third = family % FamilySymbols;
        var second = family / FamilySymbols % 26;
        var first = family / FamilySymbols / 26;
        key = string.Create(CultureInfo.InvariantCulture,
            $"{(char)('A' + first)}{(char)('A' + second)}{Symbol(third)}{ordinal:D2}");
        return true;
    }

    // The family part of a code: two codes share it when their keys share the first three characters.
    public static uint Family(uint code) => code >> 8;

    public static uint Family(string prefix) =>
        prefix.Length == 3 && TryFamily(prefix, out var family) ? family : throw new FormatException($"'{prefix}' is not an item family.");

    public static int Ordinal(uint code) => (int)(code & 0xFF);

    // The family of a catalog key, its first three characters upper-cased ("WA1"); empty for a shorter key.
    public static string FamilyOf(string key) => key is { Length: >= 3 } ? key[..3].ToUpperInvariant() : "";

    // Whether a key belongs to one of these families, without allocating.
    public static bool InFamily(string key, ReadOnlySpan<string> families)
    {
        if (key is not { Length: >= 3 })
        {
            return false;
        }

        foreach (var family in families)
        {
            if (key.AsSpan(0, 3).Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFamily(ReadOnlySpan<char> prefix, out uint family)
    {
        family = 0;
        var first = char.ToUpperInvariant(prefix[0]);
        var second = char.ToUpperInvariant(prefix[1]);
        var third = char.ToUpperInvariant(prefix[2]);
        if (!char.IsAsciiLetterUpper(first) || !char.IsAsciiLetterUpper(second) || !char.IsAsciiLetterOrDigit(third))
        {
            return false;
        }

        var symbol = char.IsAsciiDigit(third) ? third - '0' : third - 'A' + 10;
        family = (uint)(((first - 'A') * 26 + (second - 'A')) * FamilySymbols + symbol) + 1;
        return true;
    }

    private static char Symbol(uint value) => value < 10 ? (char)('0' + value) : (char)('A' + value - 10);
}
