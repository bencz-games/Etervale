using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

// Word wrap: words join a line while it measures within the width; a single word wider than the width keeps a line of
// its own. Runs of spaces collapse.
public static class HudTextLayout
{
    // The text, or the longest prefix that fits the width with an ellipsis (a prefix's width grows with its length, so it
    // is bisected).
    public static string Fit(string value, float width, Func<string, float> measure)
    {
        if (measure(value) <= width)
        {
            return value;
        }

        var fitted = string.Empty;
        for (int low = 1, high = value.Length - 1; low <= high;)
        {
            var length = (low + high) / 2;
            var candidate = value[..length] + "...";
            if (measure(candidate) <= width)
            {
                fitted = candidate;
                low = length + 1;
            }
            else
            {
                high = length - 1;
            }
        }

        return fitted;
    }

    public static string[] Wrap(SpriteFont font, string text, float width, float scale) =>
        Wrap(text, width, candidate => font.MeasureString(candidate).X * scale).ToArray();

    public static List<string> Wrap(string text, float width, Func<string, float> measure)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(candidate) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }
}
