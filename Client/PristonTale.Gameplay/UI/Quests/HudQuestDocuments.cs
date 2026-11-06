using System.Text;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Quests;

// The quest texts of the client's data: dialog documents (one .sin file per dialog under the quest folder) and the quest
// window's descriptions (.msg files under the quest board folder). Both are Windows-1252 text with CR LF lines. Files
// load on the thread pool; until a file is read its lines are null, and a missing file reads as no lines.
public sealed partial class HudQuestDocuments(string assetRoot, IClientLog log)
{
    public const string DialogFolder = "image/Sinimage/Quest";
    public const string BoardFolder = "image/party/party_DATA";

    private static readonly Encoding Latin = CreateEncoding();
    private readonly HudTextFiles _files = new(assetRoot, Latin, log, "Quest");
    private readonly Dictionary<(string Language, string Path, int Width, string Subject), string[]> _wrapped = new();

    // The mark a dialog puts where the quest's subject is named.
    public const string SubjectMark = "$$%s";

    public static string WithSubject(string line, string subject) => line.Replace(SubjectMark, subject, StringComparison.Ordinal);

    public static string DialogPath(string document) => $"{DialogFolder}/{document}.sin";

    public static string BoardPath(string description) => $"{BoardFolder}/{description.Replace('\\', '/')}";

    // The raw lines of a file, or null while it loads.
    public string[] Lines(string relativePath) => _files.Lines(relativePath);

    // The lines of a file wrapped to a width in design pixels, its "$$%s" naming the subject, or null while it loads.
    public string[] Wrapped(string relativePath, SpriteFont font, int width, string subject = "")
    {
        if (_wrapped.TryGetValue((Localization.ClientText.Language, relativePath, width, subject), out var wrapped))
        {
            return wrapped;
        }

        var lines = Lines(relativePath);
        if (lines is null)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var paragraph in Paragraphs(lines).Select(line => WithSubject(line, subject)))
        {
            if (paragraph.Length == 0)
            {
                result.Add("");
                continue;
            }

            // A paragraph that fits keeps its own spacing (the columns of a stat table).
            if (font.MeasureString(paragraph).X * HudPainter.TextScale <= width)
            {
                result.Add(paragraph);
                continue;
            }

            foreach (var line in HudTextLayout.Wrap(font, paragraph, width, HudPainter.TextScale))
            {
                BreakLongLine(font, line, width, result);
            }
        }

        wrapped = result.ToArray();
        _wrapped[(Localization.ClientText.Language, relativePath, width, subject)] = wrapped;
        return wrapped;
    }

    public bool IsLoading => _files.IsLoading;

    // The files break their lines for the classic client's narrower font; consecutive lines are joined again so the text
    // wraps to the HUD's own width. A blank line ends a paragraph (several count as one); a line opening with a list mark
    // starts one; a line laid out in columns (a run of spaces inside it) or a rule of one repeated mark stands alone.
    internal static List<string> Paragraphs(IReadOnlyList<string> lines)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush(paragraphs, current);
                if (paragraphs.Count > 0 && paragraphs[^1].Length > 0)
                {
                    paragraphs.Add("");
                }

                continue;
            }

            if (line.Contains("  ", StringComparison.Ordinal) || IsRule(line))
            {
                Flush(paragraphs, current);
                paragraphs.Add(IsRule(line) ? line : Columns().Replace(line, "   "));
                continue;
            }

            if (line[0] is '-' or '*' or '<' or '[' or '(' || (char.IsDigit(line[0]) && line.Length > 1 && line[1] is '.' or ')'))
            {
                Flush(paragraphs, current);
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);
        }

        Flush(paragraphs, current);
        if (paragraphs.Count > 0 && paragraphs[^1].Length == 0)
        {
            paragraphs.RemoveAt(paragraphs.Count - 1);
        }

        return paragraphs;
    }

    private static bool IsRule(string line) => line.Length >= 5 && !char.IsLetterOrDigit(line[0]) && line.All(character => character == line[0]);

    [System.Text.RegularExpressions.GeneratedRegex(" {3,}")]
    private static partial System.Text.RegularExpressions.Regex Columns();

    // A word wider than the line (a rule of marks) is cut to the width.
    private static void BreakLongLine(SpriteFont font, string line, int width, List<string> result)
    {
        var start = 0;
        while (start < line.Length)
        {
            var length = line.Length - start;
            while (length > 1 && font.MeasureString(line.Substring(start, length)).X * HudPainter.TextScale > width)
            {
                length--;
            }

            result.Add(line.Substring(start, length));
            start += length;
            if (IsRule(line))
            {
                return;
            }
        }
    }

    private static void Flush(List<string> paragraphs, StringBuilder current)
    {
        if (current.Length > 0)
        {
            paragraphs.Add(current.ToString());
            current.Clear();
        }
    }

    internal static string[] Decode(byte[] bytes) => HudTextFiles.Decode(bytes, Latin);

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
