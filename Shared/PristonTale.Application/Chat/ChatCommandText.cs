namespace PristonTale.Application.Chat;

// Parsing of slash commands as the classic server reads them: a command matches when the line starts with its exact,
// case-sensitive text; words are separated by spaces, tabs and colons.
public static class ChatCommandText
{
    // The length of the command when the line starts with it, else 0.
    public static int Match(string command, string line) =>
        command.Length > 0 && line.StartsWith(command, StringComparison.Ordinal) ? command.Length : 0;

    // Skips separators, then reads up to the next separator; index ends on that separator.
    public static string ReadWord(string line, ref int index)
    {
        while (index < line.Length && IsSeparator(line[index]))
        {
            index++;
        }

        var start = index;
        while (index < line.Length && !IsSeparator(line[index]) && line[index] is not '\r' and not '\n')
        {
            index++;
        }

        return line[start..index];
    }

    // A word, or a double-quoted phrase that may contain spaces (monster and item names).
    public static string ReadPhrase(string line, ref int index)
    {
        var probe = index;
        var word = ReadWord(line, ref probe);
        if (!word.StartsWith('"'))
        {
            index = probe;
            return word;
        }

        var open = line.IndexOf('"', index);
        var close = line.IndexOf('"', open + 1);
        if (close < 0)
        {
            index = line.Length;
            return line[(open + 1)..];
        }

        index = close + 1;
        return line[(open + 1)..close];
    }

    // The rest of the line after the command, without the spaces that follow it.
    public static string Rest(string line, int index)
    {
        while (index < line.Length && line[index] == ' ')
        {
            index++;
        }

        return index < line.Length ? line[index..] : "";
    }

    // An entity id typed in a command: leading digits as an unsigned 64-bit value, 0 when there are none or they overflow.
    public static ulong ToEntityId(string text) =>
        ulong.TryParse(new string(text.TrimStart().TakeWhile(char.IsAsciiDigit).ToArray()), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var entityId) ? entityId : 0;

    // C atoi: optional leading blanks and sign, then digits; anything else ends the number (0 when none).
    public static int ToNumber(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        var negative = false;
        if (index < text.Length && text[index] is '-' or '+')
        {
            negative = text[index] == '-';
            index++;
        }

        long value = 0;
        while (index < text.Length && text[index] is >= '0' and <= '9' && value < int.MaxValue)
        {
            value = value * 10 + (text[index] - '0');
            index++;
        }

        value = Math.Min(value, int.MaxValue);
        return (int)(negative ? -value : value);
    }

    public static bool StartsWithDigit(string text) => text.Length > 0 && text[0] is >= '0' and <= '9';

    // Control characters cannot be drawn and end the classic text field; they become spaces.
    public static string Clean(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                span[index] = char.IsControl(source[index]) ? ' ' : source[index];
            }
        });
    }

    private static bool IsSeparator(char value) => value is ' ' or '\t' or ':';
}
