using PristonTale.Simulation.Characters;
using PristonTale.Gameplay.UI;
using System.Text.Json;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class CharacterHeadTests
{
    private sealed record SourceHead(string Class, int Tier, int Hair, string Path);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("job heads preserve tier naming exceptions and the selected hair variant", HeadsAsync);
        yield return ("authentication controls share responsive rendering and pointer coordinates", LoginLayoutAsync);
    }

    private static Task HeadsAsync()
    {
        using var source = typeof(CharacterHeadTests).Assembly.GetManifestResourceStream("PristonTale.Tests.Fixtures.job-head-source.json")
            ?? throw new InvalidOperationException("Original head fixture is missing.");
        var rows = JsonSerializer.Deserialize<SourceHead[]>(source)!;
        Assert.True(rows.Length == 416, "original head fixture is incomplete");
        foreach (var row in rows)
        {
            var actual = CharacterHead.Resolve(Enum.Parse<PlayerClass>(row.Class), row.Tier, row.Hair);
            Assert.True(string.Equals(actual, row.Path, StringComparison.OrdinalIgnoreCase), $"head differs from the original head table: {row.Path}");
        }

        Assert.True(CharacterHead.Resolve(PlayerClass.Mechanician, 2, 2) == "char/tmABCD/tmh_a03b.inx", "job head selected a comic-head filename");
        Assert.True(CharacterHead.Resolve(PlayerClass.Pikeman, 3, 12) == "char/tmABCD/tmhC-13c.inx", "fourth-tier pikeman naming exception was lost");
        Assert.True(CharacterHead.Resolve(PlayerClass.Atalanta, 1, 9) == "char/tmABCD/Mfh-B10a.inx", "hair variant above three was truncated");
        Assert.True(CharacterHead.Resolve(PlayerClass.Assassin, 2, 2) == "char/tmABCD/tfh-e03b.inx", "Assassin advanced face path changed");
        Assert.True(CharacterHead.Resolve(PlayerClass.Shaman, 3, 2) == "char/tmABCD/Mmh-E03c.inx", "Shaman advanced face path changed");
        Assert.True(CharacterHead.Resolve(PlayerClass.MartialArtist, 0, 2) == "char/tmABCD/tfh-f03.inx", "Martial Artist creation face path changed");
        Assert.True(CharacterHead.Resolve(PlayerClass.Mechanician, 4, 0) == "char/tmABCD/Tmh-A01d.inx"
            && CharacterHead.Resolve(PlayerClass.Priestess, 4, 5) == "char/tmABCD/Mfh-C006d.inx"
            && CharacterHead.Resolve(PlayerClass.Priestess, 4, 9) == "char/tmABCD/Mfh-C10d.inx"
            && CharacterHead.Resolve(PlayerClass.Assassin, 4, 2) == "char/tmABCD/tfh-e03d.inx"
            && CharacterHead.Resolve(PlayerClass.MartialArtist, 4, 2) == "char/tmABCD/tfh-f03d.inx", "fifth-tier heads changed");
        Assert.True(CharacterHead.Resolve(PlayerClass.MartialArtist, 2, 2) == "char/tmABCD/tfh-f03b.inx"
            && CharacterHead.Apply(new CharacterClassRegistry().Get(PlayerClass.MartialArtist).Definition.DefaultVisuals, PlayerClass.MartialArtist, 1, 1, 7)
                .Head.InxExtensionPath == "char/tmABCD/tfh-f02a.inx", "a Martial Artist job head names a file no client ships");
        var defaults = new CharacterClassRegistry().Get(PlayerClass.Mechanician).Definition.DefaultVisuals;
        var resolved = CharacterHead.Apply(defaults, PlayerClass.Mechanician, 2, 2, 7);
        Assert.True(CharacterFace.ResolveHeadPath(resolved, 2) == "char/tmABCD/tmh_a08b.inx", "face selection overwrote the hair override");
        Assert.True(resolved.Body == defaults.Body, "head selection changed the body");
        var created = CharacterHead.Apply(defaults, PlayerClass.Mechanician, 0, 2, 0);
        Assert.True(created.Head.InxExtensionPath == "char/tmABCD/tmh-a03.inx", "existing creation face changed");
        var assassinDefaults = new CharacterClassRegistry().Get(PlayerClass.Assassin).Definition.DefaultVisuals;
        var unsupportedCrown = CharacterHead.Apply(assassinDefaults, PlayerClass.Assassin, 2, 1, 8);
        Assert.True(unsupportedCrown.Head.InxExtensionPath == "char/tmABCD/tfh-e02b.inx", "missing Assassin crown did not fall back to the selected face");
        foreach (var invalid in new[] { -1, 5 })
        {
            var rejected = false;
            try
            {
                CharacterHead.Resolve(PlayerClass.Mechanician, invalid, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            Assert.True(rejected, "event/comic head was accepted as an advancement tier");
        }

        var unavailableVariantRejected = false;
        try
        {
            _ = CharacterHead.Resolve(PlayerClass.Shaman, 1, 3);
        }
        catch (ArgumentOutOfRangeException)
        {
            unavailableVariantRejected = true;
        }

        Assert.True(unavailableVariantRejected, "missing Shaman advanced-tier crown produced a nonexistent filename");
        var martialArtistCrownRejected = false;
        try
        {
            _ = CharacterHead.Resolve(PlayerClass.MartialArtist, 4, 3);
        }
        catch (ArgumentOutOfRangeException)
        {
            martialArtistCrownRejected = true;
        }

        Assert.True(martialArtistCrownRejected, "missing Martial Artist crown produced a nonexistent filename");
        Assert.True(CharacterHead.SoccerHead(PlayerClass.MartialArtist) == "char/tmABCD/Tfh-f18t.inx", "Martial Artist soccer head path changed");

        return Task.CompletedTask;
    }

    private static Task LoginLayoutAsync()
    {
        foreach (var (width, height) in new[] { (640, 480), (800, 600), (1280, 720), (1920, 1080), (3440, 1440) })
        {
            var layout = LoginLayout.Create(width, height);
            Assert.True(layout.Panel.Left >= 0 && layout.Panel.Top >= 0
                && layout.Panel.Right * layout.Scale <= width && layout.Panel.Bottom * layout.Scale <= height, "login artwork is clipped");
            var previousBottom = 0;
            foreach (var box in new[] { layout.Account, layout.Password, layout.Login })
            {
                Assert.True(layout.Panel.Contains(box) && box.Top > previousBottom, "login hit regions overlap");
                var center = box.Center;
                var drawable = new Microsoft.Xna.Framework.Point((int)(center.X * layout.Scale), (int)(center.Y * layout.Scale));
                Assert.True(box.Contains(layout.Pointer(drawable)), "scaled input missed its displayed button");
                previousBottom = box.Bottom;
            }
        }

        return Task.CompletedTask;
    }
}
