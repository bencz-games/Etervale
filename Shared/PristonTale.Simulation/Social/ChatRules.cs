namespace PristonTale.Simulation.Social;

// Limits and timings of the chat that client and server share. Limits count characters; the wire carries UTF-8.
public static class ChatRules
{
    // The input line accepts this many characters, and the composed "Name: text" line is cut to it before sending.
    public const int InputLimit = 78;

    // A relayed line longer than this is cut to RelayKeep characters followed by "...".
    public const int RelayLimit = 70;
    public const int RelayKeep = 68;

    // A line is cut to this many characters.
    public const int MessageCapacity = 255;

    // Spoken lines reach players inside the near range (about 543 units).
    public const float AreaRangeSquared = World.ViewRanges.NearSquared;

    // Overhead bubbles: other characters' lines stay five seconds, the local player's own line seven.
    public const int OtherBubbleMs = 5000;
    public const int SelfBubbleMs = 7000;

    // Flood rules (ChatFloodGuard): more than ten lines less than two seconds apart freeze the chat for seven seconds, and
    // the same line is not sent twice within five seconds. Whispers are exempt from the first rule, commands from the second.
    public const int FloodGapMs = 2000;
    public const int FloodLines = 10;
    public const int FloodPenaltyMs = 7000;
    public const int RepeatWindowMs = 5000;

    public static string Compose(string name, string text) => name + ": " + text;

    public static string Shorten(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Length > RelayLimit ? string.Concat(line.AsSpan(0, RelayKeep), "...") : line;
    }

    // A '~' (every server) or '`' (the data server) before a slash command is the classic routing mark; one server
    // answers every command here, so the mark is dropped and the command goes as typed after it.
    public static string WithoutRoute(string text) =>
        text.Length > 1 && text[1] == '/' && text[0] is '~' or '`' ? text[1..] : text;

    // Whispers and every slash command travel unchanged; other lines are spoken.
    public static bool IsCommand(string text) => text.Length > 0 && (text[0] == '/' || text[0] == '`');

    public static bool IsWhisper(string text) => text.Length > 1 && text[0] == '/' && (text[1] == ':' || text[1] == ';');

    public static string ClipToCapacity(string text) => text.Length > MessageCapacity ? text[..MessageCapacity] : text;

    // A notice line every player sees, clipped to a message.
    public static string Notice(string text) => ClipToCapacity("Notice> " + text);
}
