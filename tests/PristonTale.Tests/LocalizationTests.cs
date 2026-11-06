using System.Text.RegularExpressions;
using System.Xml.Linq;
using PristonTale.Contracts.Sessions;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Items.Mixing;
using PristonTale.Simulation.Items.Usable;

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
        yield return ("every phrase key the client builds from a value exists", BuiltKeysAsync);
        yield return ("the client shows the chosen language and English for an unknown one", LanguageAsync);
        yield return ("a line of the server is worded by its phrase with its arguments in the shown language", GameTextAsync);
        yield return ("every skill name and description, class title and quest window title has its Brazilian Portuguese text, and each names game data", DataTextsAsync);
        yield return ("data texts show the data in English and its translation in Brazilian Portuguese", DataLanguageAsync);
    }

    internal static string RepositoryRoot()
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

    // Keys the client builds from a prefix and a value it shows.
    private static IEnumerable<string> BuiltKeys() =>
        Enum.GetNames<WindowMode>().Select(static name => "Settings.WindowMode." + name)
            .Concat(Enum.GetNames<GraphicsQuality>().Select(static name => "Settings.Quality." + name))
            .Concat(Enum.GetNames<CloudQuality>().Select(static name => "Settings.Clouds." + name))
            .Concat(ClientSettings.SupportedLanguages.Select(static language => "Language." + language))
            .Concat(Enum.GetValues<StatKind>().Where(static stat => stat is not StatKind.None and not StatKind.Count).Select(static stat => "Stat." + stat))
            .Concat(Enum.GetNames<Element>().Select(static name => "Element." + name))
            .Concat(Enum.GetNames<MonsterBrood>().Select(static name => "Brood." + name))
            .Concat(Enum.GetNames<ResourceKind>().Select(static name => "Resource." + name))
            .Concat(Enum.GetNames<ItemKind>().Select(static name => "ItemKind." + name))
            .Concat(Enum.GetNames<LoginFailure>().Select(static name => "Login.Failure." + name))
            .Concat(Enum.GetNames<EnterWorldFailure>().Select(static name => "CharSelect.EnterFailure." + name))
            .Concat(Enum.GetNames<CreateCharacterFailure>().Select(static name => "CharSelect.CreateFailure." + name))
            .Concat(Enum.GetValues<GameTextId>().Where(static id => id is not (GameTextId.None or GameTextId.ClassTitle)).Select(ClientText.GameKey))
            .Concat(Enum.GetValues<MixStat>().Where(static stat => stat != MixStat.None).Select(static stat => "Mix.Bonus." + stat));

    private static Task BuiltKeysAsync()
    {
        var english = Load("ClientText.resx");
        var unknown = BuiltKeys().Where(key => !english.ContainsKey(key)).ToArray();
        Assert.True(unknown.Length == 0, "built keys without a phrase: " + string.Join(", ", unknown));
        return Task.CompletedTask;
    }

    private static Task DataTextsAsync()
    {
        var portuguese = XDocument.Load(Path.Combine(Texts, "DataText.pt-BR.resx")).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);
        var expected = ClassSkillCatalog.All.SelectMany(static skill => new[] { DataText.SkillKey(skill.Code, "Name"), DataText.SkillKey(skill.Code, "Description") })
            .Concat(Enum.GetValues<PlayerClass>().SelectMany(static playerClass =>
                Enumerable.Range(0, ClassProfiles.Of(playerClass).Titles.Count).Select(tier => DataText.ClassKey(playerClass, tier))))
            .Concat(QuestBoard.Titles.Select(DataText.QuestKey))
            .Concat(CastleItems.All.Select(static castle => DataText.CastleKey(castle.CatalogKey)))
            .ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(key => !portuguese.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text)).ToArray();
        var unknown = portuguese.Keys.Where(key => !expected.Contains(key)).ToArray();
        Assert.True(missing.Length == 0, "data texts without a translation: " + string.Join(", ", missing));
        Assert.True(unknown.Length == 0, "translations of no game data: " + string.Join(", ", unknown));
        return Task.CompletedTask;
    }

    private static Task DataLanguageAsync()
    {
        Assert.True(ClassSkillCatalog.TryGet(0x02010001, out var mastery), "the Fighter's first skill is missing");
        try
        {
            ClientText.Use(ClientSettings.BrazilianPortugueseLanguage);
            Assert.True(ClientText.Format(QuestTexts.ClassTitle(PlayerClass.Knight, 1)) == "Paladino", "a class title of the server is not worded from the data");
            Assert.True(ClientText.MixBonus(new MixBonus(MixStat.AttackRating, 20f, true)) == "Taxa de ataque +20%", "a mix bonus is not worded in Brazilian Portuguese");
            Assert.True(CastleItems.TryGet("BC102", out var edge) && DataText.CastleEffect(edge) == "Chance de crítico +5%"
                && DataText.StatusEffect(ItemStatuses.PremiumKeepCode(PremiumEffectKind.LifeBooster)) == $"Aumenta a vida em {PremiumRules.BoosterPercent}%",
                "the effect lines are not worded in Brazilian Portuguese");
            Assert.True(DataText.SkillName(mastery) == "Maestria Corpo a Corpo" && DataText.ClassTitle(PlayerClass.Fighter, 0) == "Lutador"
                && DataText.ClassTitle(PlayerClass.Fighter, 9) == "Senhor da Guerra" && DataText.QuestTitle("The Cave(completed)") == "A caverna(Concluída)",
                "the data texts are not shown in Brazilian Portuguese");
            ClientText.Use(ClientSettings.EnglishLanguage);
            Assert.True(ClientText.MixBonus(new MixBonus(MixStat.Absorb, 1f, false)) == "Absorption Rating +1.0"
                && ClientText.MixBonus(new MixBonus(MixStat.Critical, 4f, false)) == "Critical +4%" && ClientText.MixBonus(new MixBonus(MixStat.Mana, 30f, false)) == "MP +30",
                "a mix bonus is not worded from its stat and value");
            Assert.True(DataText.SkillName(mastery) == mastery.Name && DataText.SkillDescription(mastery.Code, mastery.Description) == mastery.Description
                && DataText.ClassTitle(PlayerClass.Fighter, 0) == "Fighter", "the data texts are not the data in English");
        }
        finally
        {
            ClientText.Use(ClientSettings.EnglishLanguage);
        }

        return Task.CompletedTask;
    }

    private static Task GameTextAsync()
    {
        var received = GameText.Of(GameTextId.ItemsReceived, "Claw", "3");
        try
        {
            ClientText.Use(ClientSettings.BrazilianPortugueseLanguage);
            Assert.True(ClientText.Format(received) == "Você recebeu Claw x3" && ClientText.Format(GameText.Of(GameTextId.ItemUsed)) == "Item usado"
                && ClientText.Format(GameText.None).Length == 0, "a game text is not worded in Brazilian Portuguese");
            ClientText.Use(ClientSettings.EnglishLanguage);
            Assert.True(ClientText.Format(received) == "You received Claw x3", "a game text is not worded in English");
        }
        finally
        {
            ClientText.Use(ClientSettings.EnglishLanguage);
        }

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
