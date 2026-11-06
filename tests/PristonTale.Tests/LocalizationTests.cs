using System.Text.RegularExpressions;
using System.Xml.Linq;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Tests;

// The client's phrases: every language file holds every phrase with the same placeholders, every key the client code
// asks for exists, and the shown language follows the setting with English for anything else.
internal static partial class LocalizationTests
{
    // The repository: the first folder above the test binaries that holds the solution.
    private static readonly string Root = RepositoryRoot();
    private static readonly string Texts = Path.Combine(Root, "Client", "PristonTale.Gameplay", "Localization");

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every client phrase is in every language with the same placeholders", CompleteAsync);
        yield return ("every phrase key the client code asks for exists", KeysAsync);
        yield return ("the client shows the chosen language and English for an unknown one", LanguageAsync);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PristonTale.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository holding PristonTale.sln is not above the test binaries.");
    }

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(Texts, file)).Root!.Elements("data").ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);

    private static Task CompleteAsync()
    {
        var english = Load("ClientText.resx");
        var portuguese = Load("ClientText.pt-BR.resx");
        var missing = english.Keys.Except(portuguese.Keys).Concat(portuguese.Keys.Except(english.Keys)).ToArray();
        Assert.True(missing.Length == 0, "phrases in one language only: " + string.Join(", ", missing));
        var placeholders = english.Where(pair => !Placeholders(pair.Value).SequenceEqual(Placeholders(portuguese[pair.Key]))).Select(pair => pair.Key).ToArray();
        Assert.True(placeholders.Length == 0, "phrases whose placeholders differ: " + string.Join(", ", placeholders));
        return Task.CompletedTask;
    }

    private static IEnumerable<string> Placeholders(string text) => Placeholder().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal);

    private static Task KeysAsync()
    {
        var english = Load("ClientText.resx");
        var asked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "Client"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match call in TextCall().Matches(File.ReadAllText(file)))
            {
                foreach (Match key in Key().Matches(call.Groups[1].Value))
                {
                    asked.Add(key.Groups[1].Value);
                }
            }
        }

        var unknown = asked.Where(key => !english.ContainsKey(key)).ToArray();
        Assert.True(asked.Count > 0 && unknown.Length == 0, "keys without a phrase: " + string.Join(", ", unknown));
        return Task.CompletedTask;
    }

    private static Task LanguageAsync()
    {
        try
        {
            ClientText.Use(ClientSettings.BrazilianPortugueseLanguage);
            Assert.True(ClientText.Get("Skill.NotEnoughMana") == "Mana insuficiente." && ClientText.Format("Status.Opening", "x") == "Abertura: x",
                "Brazilian Portuguese is not shown");
            ClientText.Use("fr");
            Assert.True(ClientText.Language == ClientSettings.EnglishLanguage && ClientText.Get("Skill.NotEnoughMana") == "Not enough mana.",
                "an unknown language did not fall back to English");
        }
        finally
        {
            ClientText.Use(ClientSettings.EnglishLanguage);
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\{\d+[^}]*\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"ClientText\.(?:Get|Format)\(([^;]*)")]
    private static partial Regex TextCall();

    [GeneratedRegex(@"""([A-Z][A-Za-z]*(?:\.[A-Z][A-Za-z0-9]*)+)""")]
    private static partial Regex Key();
}
