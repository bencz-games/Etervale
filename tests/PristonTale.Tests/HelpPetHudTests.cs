using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.HelpPet;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Tests;

// The beginner's helper pet on the client without a device: its texts, its help boxes (the greeting, the level guides,
// their queue) and its speech over itself.
internal static class HelpPetHudTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the pet's texts read with the file's quirks, and the shipped file holds every topic the HUD speaks of", MessagesAsync);
        yield return ("a fresh character is greeted and each level up to 10 queues its guides, which OK or fifteen seconds close in turn", BoxesAsync);
        yield return ("the pet reads its texts again in the new language when the shown one changes", LanguageAsync);
        yield return ("the pet speaks of what is pointed at, clicked or pressed below level 10 while shown, idles in town and cheers in battle", SpeechAsync);
    }

    private const string Text = """
        *PetKind
        "Fire Dragon   ASTERYAN"
        "Water Dragon  KRONAPSYS"
        end
        *PetName
        "Terry"
        "Napsys"
        end
        stray line
        *Welcom_ALL
        "I am a descendant of( %s )."
        "Call me( %s )."
        end
        *Welcom_Tems
        "This is Ricarten."
        end
        *Welcom_Mora
        "This is Pillai."
        end
        *System
        "Hover"
        "Click"
        "Right"
        "Key"
        end
        *Normal_Message
        "Idle one'
        "Idle two
        end
        *Battle_Message
        "Yap~ It's my ATK" or "HP~"
        end
        *Level_Message2-1
        "Two A"
        end
        *Level_Message2-2
        "Two B"
        end
        *Level_Message3
        "Three"
        end
        *Level_Message10-1
        "Ten A"
        end
        *Level_Message10-2
        "Ten B"
        end
        *Special_Message8_Mecha
        "Mechanician"
        end
        *Special_Message9
        "Farewell"
        end
        """;

    private static readonly HelpPetMessages Book = HelpPetMessages.Parse(Text);
    private static readonly Point Ok = new(530, 98);

    private static Task MessagesAsync()
    {
        Assert.True(Book.Lineage(1) == "Fire Dragon ASTERYAN" && Book.Name(2) == "Napsys" && Book.Name(3) == "", "the names are wrong");
        Assert.True(Book.Lines("Normal_Message").SequenceEqual(["Idle one", "Idle two"]) && Book.Lines("Battle_Message").SequenceEqual(["Yap~ It's my ATK"])
            && Book.Lines("Level_Message2-1").SequenceEqual(["Two A"]) && Book.Lines("System").Length == 4 && Book.Lines("Missing").Length == 0,
            "a quirk of the file was misread");

        var root = Path.Combine(FindRoot(), "assets");
        var path = Path.Combine(root, HelpPetMessages.FilePath);
        if (!File.Exists(path))
        {
            Console.WriteLine("  (skipped the shipped file: no assets next to the repository)");
            return Task.CompletedTask;
        }

        var shipped = HelpPetMessages.LoadAsync(root, Engine3D.Diagnostics.ConsoleClientLog.Instance).Result;
        string[] topics =
        [
            "Welcom_ALL", "Welcom_Tems", "Welcom_Mora", "Normal_Message", "Battle_Message", "CarStatus", "InvenTory", "Skill", "Party", "Quest",
            "System", "RunAndWalk", "Camera_Hand", "Mini_Map", "LeftSkill", "RightSkill", "ChatBox", "Bar_Life", "Bar_Mana", "Bar_Stamina", "Bar_Exp",
            "DayandNight", "Special_Message9", .. new[] { 2, 6, 9, 10 }.SelectMany(level => new[] { $"Level_Message{level}-1", $"Level_Message{level}-2" }),
            .. new[] { 3, 4, 5, 7, 8 }.Select(level => $"Level_Message{level}"),
            .. Enum.GetValues<PlayerClass>().Select(playerClass => $"Special_Message8_{(playerClass == PlayerClass.Mechanician ? "Mecha" : playerClass)}")
        ];
        var missing = topics.Where(topic => shipped.Lines(topic).Length == 0).ToArray();
        Assert.True(missing.Length == 0 && shipped.Lines("Potion_ShortKey").Length == 6 && shipped.Name(4) == "Mut", $"missing topics: {string.Join(", ", missing)}");

        // The Brazilian Portuguese texts hold every topic with as many lines, and the greeting's names.
        var translations = Path.Combine(FindRoot(), "Client", "PristonTale", "Texts");
        var portuguese = HelpPetMessages.LoadAsync(root, Engine3D.Diagnostics.ConsoleClientLog.Instance, Engine3D.Rendering.ClientSettings.BrazilianPortugueseLanguage,
            translations).Result;
        var uneven = topics.Append("Potion_ShortKey").Append("PetName").Append("PetKind")
            .Where(topic => portuguese.Lines(topic).Length != shipped.Lines(topic).Length).ToArray();
        Assert.True(uneven.Length == 0 && portuguese.Lines("Welcom_ALL").Take(2).All(static line => line.Contains("%s", StringComparison.Ordinal))
            && portuguese.Lineage(1) == "Dragão de Fogo ASTERYAN", $"translated topics that differ: {string.Join(", ", uneven)}");
        return Task.CompletedTask;
    }

    private static Task BoxesAsync()
    {
        var pet = NewPet();
        pet.Update(State(kind: 1, level: 1, fresh: true, playerClass: PlayerClass.Shaman), 0);
        Assert.True(pet.Shown is { Kind: 1 } shown && shown.Lines.SequenceEqual(["I am a descendant of( Fire Dragon ASTERYAN ).", "Call me( Terry )."])
            && pet.Queued == 1, "the greeting did not name the pet");
        Assert.True(pet.Click(Ok, 100) && pet.Shown?.Lines.Single() == "This is Pillai.", "OK did not show the Pillai welcome of a Morion class");
        pet.Update(State(kind: 1, level: 1), 200);
        pet.Update(State(kind: 1, level: 1), 200 + HudHelpPet.BoxMs);
        Assert.True(pet.Shown is null, "a box outlived fifteen seconds");

        pet.Update(State(kind: 1, level: 2), 20_000);
        Assert.True(pet.Shown?.Lines.Single() == "Two A" && pet.Queued == 1, "level 2 did not queue both its guides");
        pet.Click(Ok, 20_100);
        pet.Update(State(kind: 1, level: 3), 20_200);
        Assert.True(pet.Shown?.Lines.Single() == "Two B" && pet.Queued == 1, "the level 3 guide did not wait its turn");
        pet.Click(Ok, 20_300);
        pet.Click(Ok, 20_400);

        pet.Update(State(kind: 0, level: 9), 30_000);
        pet.Update(State(kind: 0, level: 10), 30_000 + HudHelpPet.LeaveGraceMs);
        Assert.True(pet.Shown is { Kind: 1 } parting && parting.Lines.Single() == "Ten A", "the level 10 guide was lost to the pet leaving first");

        var hidden = NewPet();
        hidden.Update(State(kind: 2, level: 1), 0);
        hidden.Update(State(kind: 0, level: 1), 1_000);
        hidden.Update(State(kind: 0, level: 2), 1_000 + HudHelpPet.LeaveGraceMs + 1);
        Assert.True(hidden.Shown is null && hidden.Queued == 0, "a hidden pet guided");
        hidden.Update(State(kind: 2, level: 3, fresh: true), 50_000);
        Assert.True(hidden.Shown is { Kind: 2 } back && back.Lines.Single() == "Three" && hidden.Queued == 0,
            "the pet shown again did not guide, or greeted a second time");
        return Task.CompletedTask;
    }

    private static Task SpeechAsync()
    {
        var pet = NewPet();
        pet.Update(State(kind: 2, level: 3), 0);
        pet.Point("System", 0);
        Assert.True(pet.Bubble == "Napsys : Hover", "pointing did not speak");
        pet.Say("System", 1, 100);
        pet.Point("System", 200);
        Assert.True(pet.Bubble == "Napsys : Click", "the pointer staying replaced the click's line");
        pet.Update(State(kind: 2, level: 3), 200 + HudHelpPet.BubbleMs - 1);
        Assert.True(pet.Bubble == "Napsys : Click", "the pointer staying did not keep the line up");
        pet.Say("Battle_Message", 0, 4_000);
        pet.Point("System", 4_100);
        Assert.True(pet.Bubble == "Napsys : Yap~ It's my ATK", "the pointer staying replaced another topic's line");
        pet.Point(null, 4_200);
        pet.Say("System", 3, 4_300);
        pet.Update(State(kind: 2, level: 3), 4_300 + HudHelpPet.BubbleMs);
        Assert.True(pet.Bubble is null, "a line outlived four seconds");
        pet.Say("System", 9, 9_000);
        Assert.True(pet.Bubble is null, "a missing line was said");

        var idle = new HashSet<string>();
        for (var round = 1; round <= 2; round++)
        {
            pet.Update(State(kind: 2, level: 3, inTown: true), round * HudHelpPet.IdleEveryMs);
            idle.Add(pet.Bubble ?? "");
        }

        Assert.True(idle.SetEquals(["Napsys : Idle one", "Napsys : Idle two"]), $"the idle lines repeated: {string.Join(", ", idle)}");

        var fighter = NewPet();
        fighter.Update(State(kind: 1, level: 5, percent: 12), 0);
        fighter.Update(State(kind: 1, level: 5, percent: 12, attacking: true), 10);
        Assert.True(fighter.Bubble is null, "a step passed before the fight cheered");
        fighter.Update(State(kind: 1, level: 5, percent: 36, attacking: true), 20);
        Assert.True(fighter.Bubble == "Terry : Yap~ It's my ATK", "the next experience step did not cheer");
        fighter.Update(State(kind: 1, level: 5, percent: 36, attacking: true), HudHelpPet.BubbleMs + 20);
        fighter.Update(State(kind: 1, level: 5, percent: 36, attacking: true), HudHelpPet.BubbleMs + 30);
        Assert.True(fighter.Bubble is null, "steps passed at once cheered more than once");

        var mechanician = NewPet();
        mechanician.Update(State(kind: 1, level: 8, percent: 90, attacking: true, playerClass: PlayerClass.Mechanician), 0);
        mechanician.Update(State(kind: 1, level: 8, percent: 95, attacking: true, playerClass: PlayerClass.Mechanician), 10);
        mechanician.Update(State(kind: 1, level: 9, percent: 90, attacking: true, playerClass: PlayerClass.Mechanician), 20);
        Assert.True(mechanician.Shown?.Lines.Single() == "Mechanician" && mechanician.Queued == 1, "the class guide or the farewell is wrong");

        var veteran = NewPet();
        veteran.Update(State(kind: 3, level: 10), 0);
        veteran.Say("System", 1, 0);
        var shy = NewPet();
        shy.Update(State(kind: 0, level: 2), 0);
        shy.Say("System", 1, 0);
        Assert.True(veteran.Bubble is null && shy.Bubble is null, "a pet spoke at level 10 or while hidden");
        return Task.CompletedTask;
    }

    private static Task LanguageAsync()
    {
        var asked = new List<string>();
        var translated = HelpPetMessages.Parse(Text.Replace("I am a descendant of", "Sou descendente do", StringComparison.Ordinal));
        var pet = new HudHelpPet(Task.FromResult(Book), new Random(5), Engine3D.Diagnostics.ConsoleClientLog.Instance, language =>
        {
            asked.Add(language);
            return Task.FromResult(translated);
        });
        try
        {
            pet.Update(State(kind: 0, level: 1), 0);
            Assert.True(asked.Count == 0, "the pet read its texts again without a change of language");
            ClientText.Use(Engine3D.Rendering.ClientSettings.BrazilianPortugueseLanguage);
            pet.Update(State(kind: 1, level: 1, fresh: true), 100);
            Assert.True(asked.SequenceEqual([Engine3D.Rendering.ClientSettings.BrazilianPortugueseLanguage])
                && pet.Shown?.Lines[0] == "Sou descendente do( Fire Dragon ASTERYAN ).", "the pet did not speak in the new language");
        }
        finally
        {
            ClientText.Use(Engine3D.Rendering.ClientSettings.EnglishLanguage);
        }

        return Task.CompletedTask;
    }

    private static HudHelpPet NewPet() => new(Task.FromResult(Book), new Random(5), Engine3D.Diagnostics.ConsoleClientLog.Instance);

    private static HelpPetState State(int kind, int level, int percent = 0, bool fresh = false, PlayerClass playerClass = PlayerClass.Fighter,
        bool inTown = false, bool attacking = false) =>
        new(kind, level, percent, fresh, playerClass, inTown, attacking);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "assets", "image")))
            {
                return directory.FullName;
            }
        }

        return AppContext.BaseDirectory;
    }
}
