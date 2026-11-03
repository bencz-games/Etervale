using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Help;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Tests;

// The help window and the game guide without a device: the guide's data, when the level guide offers a page, its
// notice's clicks, and the window's pages.
internal static class HelpGuideTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the guide reads each level's page for the faction, the starter pages and the levels without a picture", BookAsync);
        yield return ("the level guide offers a page on reaching its level or entering fresh, and its notice opens a summary that closes", NoticeAsync);
        yield return ("the help window moves between help, the guide's list of reached subjects and a page, and switches the level guide", WindowAsync);
    }

    private const string Subjects = """
        [Level_1]
        Subj=Welcome
        [Level_10]
        Subj=Skills
        [Level_9]
        Subj=Specialized items
        [Level_80]
        Subj=Babel
        """;

    private const string Content = """
        [CreateCha_Temscron]
        Key0=image\\clanImage\\Help\\AutoHelp\\0.msg
        Guide1=image\\Sinimage\\help\\GHI-T01.tga
        a note in Korean
        [CreateCha_Moraion]
        Key1=image\\clanImage\\Help\\AutoHelp\\1.msg
        Guide2=image\\Sinimage\\help\\GHI-M01.tga
        [Level_10_Temscron]
        Key8=image\\clanImage\\Help\\AutoHelp\\8.msg
        Guide9=image\\Sinimage\\help\\GHI-T10.tga
        [Level_10_Moraion]
        Key9=image\\clanImage\\Help\\AutoHelp\\9.msg
        Guide10=image\\Sinimage\\help\\GHI-M10.tga
        [Level_9]
        Key25=image\\clanImage\\Help\\AutoHelp\\26.msg
        Guide26=image\\Sinimage\\help\\GHI-C07.tga
        [Level_80]
        Key23=image\\clanImage\\Help\\AutoHelp\\23.msg
        Guide25=image\\Sinimage\\help\\GHI-C80.tga
        [Level_1_W]
        Key29=image\\clanImage\\Help\\AutoHelp\\0_1.msg
        Guide25=image\\Sinimage\\help\\GHI-C02.tga
        [Level_1_D]
        Key30=image\\clanImage\\Help\\AutoHelp\\0_2.msg
        Guide26=image\\Sinimage\\help\\GHI-C02.tga
        """;

    private static Task BookAsync()
    {
        var book = LevelGuideBook.Parse(Subjects, Content);
        Assert.True(book.Subjects.Select(subject => subject.Level).SequenceEqual([1, 9, 10, 80]) && book.HasPage(10) && !book.HasPage(2),
            "the subjects are not the levels in order");
        Assert.True(book.Page(1, tempskron: true) == new LevelGuidePage("image/clanImage/Help/AutoHelp/0.msg", "image/Sinimage/help/GHI-T01.tga")
            && book.Page(1, tempskron: false)!.TextPath.EndsWith("1.msg") && book.Page(10, tempskron: false)!.TextPath.EndsWith("9.msg")
            && book.Page(80, tempskron: false)!.TextPath.EndsWith("23.msg"), "a page of the faction or the common page was not found");
        Assert.True(book.Page(9, tempskron: true) is { ImagePath: null } && book.Page(2, tempskron: true) is null, "level 9 showed level 7's picture");
        Assert.True(book.StarterPage(weapon: true)!.TextPath.EndsWith("0_1.msg") && book.StarterPage(weapon: false)!.TextPath.EndsWith("0_2.msg"),
            "the starter pages are wrong");

        var root = FindAssets();
        var subjects = Path.Combine(root ?? "", LevelGuideBook.SubjectsFile);
        if (root is null || !File.Exists(subjects))
        {
            Console.WriteLine("  (skipped the shipped guide: no assets next to the repository)");
            return Task.CompletedTask;
        }

        var shipped = LevelGuideBook.Parse(File.ReadAllText(subjects, Assets.Cp949.Encoding),
            File.ReadAllText(Path.Combine(root, LevelGuideBook.ContentFile), Assets.Cp949.Encoding));
        var missing = shipped.Subjects.SelectMany(subject => new[] { true, false }.Select(tempskron => (subject.Level, Page: shipped.Page(subject.Level, tempskron))))
            .Where(entry => entry.Page is null || !File.Exists(Path.Combine(root, entry.Page.TextPath)))
            .Select(entry => entry.Level).Distinct().ToArray();
        Assert.True(shipped.Subjects.Count >= 20 && missing.Length == 0, $"levels without a readable page: {string.Join(", ", missing)}");
        return Task.CompletedTask;
    }

    private static Task NoticeAsync()
    {
        Assert.True(HudLevelGuide.Reached(0, 1, 0) && !HudLevelGuide.Reached(0, 1, 5) && !HudLevelGuide.Reached(0, 10, 0)
            && HudLevelGuide.Reached(9, 10, 0) && !HudLevelGuide.Reached(10, 10, 0) && !HudLevelGuide.Reached(11, 10, 0),
            "the level guide offered the wrong levels");

        Assert.True(HudLevelGuide.Fresh(1, 0) && !HudLevelGuide.Fresh(2, 0), "the fresh character rule is wrong");
        var requested = new List<string>();
        var notice = new HudLevelGuide(new HudTextFiles(AppContext.BaseDirectory, Encoding.ASCII, ConsoleClientLog.Instance, "Help"), requested.Add);
        notice.Offer(new LevelGuidePage("PristonTale.Tests.dll", "a.tga"), atOnce: false);
        var icon = new Point(20, 20);
        Assert.True(requested.SequenceEqual(["a.tga"]) && notice.Captures(icon, blocked: false) && !notice.Captures(icon, blocked: true),
            "the notice did not ask for its picture, or took clicks while the pet's box holds its corner");
        Assert.True(notice.Click(icon, blocked: false, out var more) && more is null && notice.Summary, "a click on the notice did not open its summary");
        var close = new Point(HudPainter.GuideBoxRight(5, 28) - 16, 98);
        Assert.True(notice.Click(close, blocked: false, out _) && !notice.Showing, "Close did not close the summary");
        return Task.CompletedTask;
    }

    private static async Task WindowAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"priston-help-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, LevelGuideBook.Folder));
            await File.WriteAllTextAsync(Path.Combine(root, LevelGuideBook.SubjectsFile), Subjects);
            await File.WriteAllTextAsync(Path.Combine(root, LevelGuideBook.ContentFile), Content);
            var window = new HudHelpWindow(new HudTextFiles(root, Encoding.ASCII, ConsoleClientLog.Instance, "Help"), _ => { }) { Level = 10, Tempskron = false };
            for (var attempt = 0; attempt < 200 && window.Book.Subjects.Count == 0; attempt++)
            {
                await Task.Delay(10);
            }

            var switched = 0;
            window.OpenHelp();
            Assert.True(window.Click(HudHelpWindow.GuideLink.Center, () => switched++) && window.Page == HudHelpPage.GuideList,
                "the guide link did not open the guide");
            Assert.True(window.Click(HudHelpWindow.ListRow(0).Center, () => switched++) && window.Page == HudHelpPage.GuidePage
                && window.GuidePage!.TextPath.EndsWith("/9.msg"), "the latest subject reached (level 10, Morion) did not open its page");
            window.OpenGuide();
            Assert.True(window.Click(HudHelpWindow.ListRow(3).Center, () => switched++) && window.Page == HudHelpPage.GuideList
                && window.Click(HudHelpWindow.ListRow(2).Center, () => switched++) && window.GuidePage!.TextPath.EndsWith("/1.msg"),
                "the list held a level above the character's (80), or did not end at level 1");
            Assert.True(window.Click(HudHelpWindow.Switch.Center, () => switched++) && switched == 1, "the On/Off switch did not switch the level guide");
            Assert.True(window.Click(HudHelpWindow.OkButton.Center, () => switched++) && !window.IsOpen && !window.Click(HudHelpWindow.OkButton.Center, () => { }),
                "OK did not close the window, or a closed window took a click");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The repository's assets, when they hold the image folder this case reads.
    private static string? FindAssets() =>
        PristonTale.Assets.AssetFiles.FindAssetsRoot() is { } root && Directory.Exists(Path.Combine(root, "image")) ? root : null;
}
