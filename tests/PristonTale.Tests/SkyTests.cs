using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class SkyTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("sky manifest preserves ordered layers, RGBA and Windows path normalization", ManifestAsync);
        yield return ("all configured outdoor fields resolve their original sky profiles", ProfilesAsync);
        yield return ("distance fog keeps the original start, caps atmospheric opacity and keeps the classic clip", FogCurveAsync);
    }

    private static Task ManifestAsync()
    {
        using var reader = new StringReader("""
            StartSky: 33
            label {
            Filename: Effect\\Sky\\day.ase FadeDelay: 1 RGBA: 255 128 64 220
            }
            empty {
            }
            """);
        var profiles = PristonSkyManifest.Parse(reader);
        Assert.True(profiles.Count == 2 && profiles[1].Length == 0, "empty profile shifted numeric identities");
        var layer = profiles[0][0];
        Assert.True(layer.ModelPath == "Effect/Sky/day.smd" && layer.DelayedFade, "compiled path or fade metadata lost");
        Assert.True(layer.Color == new Color(255, 128, 64, 220), "layer tint lost");
        return Task.CompletedTask;
    }

    private static Task ProfilesAsync()
    {
        foreach (var backdrop in new[] { FieldBackdrops.Forest, FieldBackdrops.Ruin1, FieldBackdrops.Ruin2,
            FieldBackdrops.Desert, FieldBackdrops.Fall, FieldBackdrops.Iron1, FieldBackdrops.Iron2, FieldBackdrops.Snow })
        {
            var profiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(backdrop.Day),
                PristonSkyProfiles.Resolve(backdrop.Glow), PristonSkyProfiles.Resolve(backdrop.Night));
            Assert.True(profiles.Select(12) == profiles.Day && profiles.Select(4) == profiles.Day && profiles.Select(22.5f) == profiles.Glow
                && profiles.Select(23) == profiles.Night && profiles.Select(0) == profiles.Night && profiles.Select(3.9f) == profiles.Night,
                "the sky's profile does not follow the game's day, glow and night");
        }

        Assert.True(PristonSkyProfiles.Opening.Select(12) == 22, "opening lost its 0x80 sky");
        return Task.CompletedTask;
    }

    private static Task FogCurveAsync()
    {
        var atmospheric = PristonDistanceFog.Atmospheric;
        const float start = PristonDistanceFog.OriginalStartDistance;
        Assert.True(atmospheric.Level(start) == 0f, "fog moved the original near start");
        Assert.Near(0.5f * atmospheric.MaxOpacity, atmospheric.Level(start + MathF.Log(2f) / atmospheric.Density));
        Assert.True(atmospheric.Level(start + 510f) < 0.15f, "atmospheric fog still hides geometry at the classic end distance");
        var far = atmospheric.Level(20000f);
        Assert.True(far <= atmospheric.MaxOpacity && far > atmospheric.MaxOpacity - 0.03f, "atmospheric fog misses its cap at the far plane");
        Assert.True(atmospheric.MaxOpacity is > 0.8f and <= 0.9f, "atmospheric cap left the 0.8..0.9 range");
        Assert.True(PristonDistanceFog.Disabled.Level(20000f) == 0f, "disabled fog still applies");
        Assert.True(PristonDistanceFog.For(DistanceFogMode.Classic) == atmospheric && new ClientSettings().DistanceFog == DistanceFogMode.Atmospheric,
            "Classic settings no longer fall back to the atmospheric fog, or the default changed");
        return Task.CompletedTask;
    }
}
