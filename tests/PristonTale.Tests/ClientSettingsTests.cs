using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class ClientSettingsTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("settings validate limits without restricting valid monitor modes", ValidationAsync);
        yield return ("older settings inherit the current shadow refresh default", OlderDefaultsAsync);
        yield return ("older boolean distance fog settings load as fog modes", BooleanDistanceFogAsync);
        yield return ("settings persistence drains the latest snapshot on shutdown", PersistenceAsync);
        yield return ("invalid and future settings documents survive attempted saves", PreserveInvalidAsync);
        yield return ("settings write failures are observable without faulting gameplay", WriteFailureAsync);
        yield return ("frame pacing has no catch-up bursts and separates foreground limits", PacingAsync);
        yield return ("a texture named with another extension is found in the subfolder its name points to", TextureExtensionAsync);
        yield return ("opening textures use only a configured Ricarten directory fallback after the source folder", TextureScopedFallbackAsync);
        yield return ("opening NPC E resolves only mm.bmp through the original TN-006 model directory", OpeningNpcTextureFallbackAsync);
    }

    private static Task ValidationAsync()
    {
        var settings = new ClientSettings
        {
            WindowWidth = -1,
            WindowHeight = 0,
            WindowMode = (WindowMode)999,
            UiScale = float.NaN,
            MaxFps = -1,
            BackgroundMaxFps = 1,
            MultiSampleCount = 3,
            ShadowMapSize = 9000,
            LampShadowTileSize = 0,
            ShadowUpdateRate = 75,
            RefreshRate = -9,
            Monitor = -2
        };
        Assert.True(settings.Validate(NullClientLog.Instance) == new ClientSettings(), "invalid settings did not restore safe defaults");
        Assert.True(new ClientSettings { ServerAddress = " " }.Validate(NullClientLog.Instance).ServerAddress == new ClientSettings().ServerAddress,
            "a blank server address was kept");
        Assert.True(new ClientSettings { MultiSampleCount = 8 }.Validate(NullClientLog.Instance).MultiSampleCount == new ClientSettings().MultiSampleCount,
            "eight MSAA samples, which the renderer cannot run, were kept");
        Assert.True(new ClientSettings { Language = "fr" }.Validate(NullClientLog.Instance).Language == ClientSettings.EnglishLanguage,
            "unsupported language was kept");
        Assert.True(new ClientSettings { Language = ClientSettings.BrazilianPortugueseLanguage }.Validate(NullClientLog.Instance).Language == ClientSettings.BrazilianPortugueseLanguage,
            "Brazilian Portuguese was rejected");
        settings = new ClientSettings { WindowWidth = 3440, WindowHeight = 1440, RefreshRate = 165, MaxFps = 237, Monitor = 2 };
        Assert.True(settings.Validate(NullClientLog.Instance) == settings, "valid custom mode or frame limit was lost");
        Assert.True(settings.HasSameDisplay(settings with { MaxFps = 60, UiScale = 1.2f, MusicEnabled = false, SoundEnabled = false }), "non-device settings triggered display reset");
        Assert.True(new ClientSettings().CameraSceneryCollision, "camera collision should default on");
        Assert.True((settings with { CameraSceneryCollision = false }).Validate(NullClientLog.Instance).CameraSceneryCollision == false,
            "camera collision option was not persisted by validation");
        Assert.True(settings.HasSameDisplay(settings with { CameraSceneryCollision = false }),
            "camera-only option triggered a display reset");
        Assert.True(!settings.HasSameDisplay(settings with { RefreshRate = 144 }), "refresh change missed display reset");
        return Task.CompletedTask;
    }

    private static Task TextureExtensionAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-textures-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "sub"));
            var shipped = Path.Combine(directory, "sub", "tex.tga");
            File.WriteAllBytes(shipped, []);
            File.WriteAllBytes(Path.Combine(directory, "tex.jpg"), []);
            var found = PristonTextureLoader.ResolvePath(directory, "sub\\tex.bmp");
            Assert.True(Path.GetFullPath(found) == Path.GetFullPath(shipped), $"the retry looked outside the named subfolder: {found}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TextureScopedFallbackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "priston-opening-textures-" + Guid.NewGuid().ToString("N"));
        var opening = Path.Combine(root, "StartImage", "Opening", "Map");
        var ricarten = Path.Combine(root, "Field", "Ricarten");
        var castle = Path.Combine(root, "Field", "castle");
        try
        {
            Directory.CreateDirectory(opening);
            Directory.CreateDirectory(ricarten);
            Directory.CreateDirectory(castle);
            var ricartenTexture = Path.Combine(ricarten, "riy-w017.bmp");
            var castleTexture = Path.Combine(castle, "riy-w017.bmp");
            File.WriteAllBytes(ricartenTexture, [1]);
            File.WriteAllBytes(castleTexture, [2]);

            Assert.True(PristonTextureLoader.ResolvePath(opening, "riy-w017.bmp") == "",
                "unconfigured opening unexpectedly searched other fields");
            var found = PristonTextureLoader.ResolvePathWithFallback(opening, "riy-w017.bmp", ricarten);
            Assert.True(found == ricartenTexture, $"opening did not choose the explicit Ricarten fallback: {found}");
            Assert.True(found != castleTexture, "opening chose the similarly named castle texture");

            var openingTexture = Path.Combine(opening, "riy-w017.bmp");
            File.WriteAllBytes(openingTexture, [3]);
            Assert.True(PristonTextureLoader.ResolvePathWithFallback(opening, "riy-w017.bmp", ricarten) == openingTexture,
                "fallback overrode an authentic local opening texture");

            var alternate = Path.Combine(ricarten, "eye_roof.tga");
            File.WriteAllBytes(alternate, [4]);
            Assert.True(PristonTextureLoader.ResolvePathWithFallback(opening, "eye_roof.BMP", ricarten) == alternate,
                "fallback lost existing case/extension resolution");
            Assert.True(PristonTextureLoader.ResolvePathWithFallback(opening, "unknown.bmp", ricarten) == "",
                "missing assets should remain missing, not silently substitute a different texture");
            Assert.True(PristonTextureLoader.ResolvePathWithFallback(opening, "mm.bmp", null) == "",
                "an unrelated opening NPC must not inherit the map fallback");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task OpeningNpcTextureFallbackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "priston-opening-npc-" + Guid.NewGuid().ToString("N"));
        var opening = Path.Combine(root, "StartImage", "Opening", "npc");
        var tn006 = Path.Combine(root, "char", "npc", "TN-006");
        var sod = Path.Combine(root, "Field", "Sod");
        try
        {
            Directory.CreateDirectory(opening);
            Directory.CreateDirectory(tn006);
            Directory.CreateDirectory(sod);
            var original = Path.Combine(tn006, "mm.bmp");
            File.WriteAllBytes(original, [1]);
            File.WriteAllBytes(Path.Combine(tn006, "TN-006-b.BMP"), [2]);
            File.WriteAllBytes(Path.Combine(sod, "mm.bmp"), [3]);

            using var loader = new PristonTextureLoader(null, NullClientLog.Instance);
            loader.SetFallbackTextureDirectory(opening, "mm.bmp", tn006);
            Assert.True(loader.ResolveTexturePath(opening, "MM.BMP") == original,
                "opening NPC E did not resolve its original TN-006 texture by case-insensitive name");
            Assert.True(loader.ResolveTexturePath(opening, "TN-006-b.BMP") == "",
                "another opening NPC material incorrectly inherited the mm.bmp fallback");
            Assert.True(loader.ResolveTexturePath(Path.Combine(root, "StartImage", "Opening", "Map"), "mm.bmp") == "",
                "the NPC texture fallback leaked into the opening stage");

            var local = Path.Combine(opening, "mm.bmp");
            File.WriteAllBytes(local, [4]);
            Assert.True(loader.ResolveTexturePath(opening, "mm.bmp") == local,
                "an opening-local original texture was overridden by the TN-006 fallback");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static async Task OlderDefaultsAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "priston-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"version\":1,\"maxFps\":144,\"serverAddress\":\"play.example.com:10037\"}");
            var store = new ClientSettingsStore(path, NullClientLog.Instance);
            var loaded = store.Load();
            await store.CompleteAsync();
            Assert.True(loaded.MaxFps == 144, "existing setting was not loaded");
            Assert.True(loaded.ServerAddress == "play.example.com:10037", "the server address was not loaded");
            Assert.True(loaded.ShadowUpdateRate == 60, "missing shadow refresh setting did not inherit the current default");
            Assert.True(loaded.MusicEnabled && loaded.SoundEnabled, "missing audio settings did not inherit enabled defaults");
            Assert.True(store.LastError.Length == 0, "compatible older settings produced a load error");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task BooleanDistanceFogAsync()
    {
        foreach (var (json, expected) in new[]
        {
            ("true", DistanceFogMode.Atmospheric),
            ("false", DistanceFogMode.Off),
            ("\"Classic\"", DistanceFogMode.Classic)
        })
        {
            var path = Path.Combine(Path.GetTempPath(), "priston-settings-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await File.WriteAllTextAsync(path, "{\"version\":1,\"distanceFog\":" + json + "}");
                var store = new ClientSettingsStore(path, NullClientLog.Instance);
                var loaded = store.Load();
                await store.CompleteAsync();
                Assert.True(loaded.DistanceFog == expected && store.LastError.Length == 0, $"distanceFog {json} did not load as {expected}");
            }
            finally
            {
                File.Delete(path);
            }
        }

        var serialized = System.Text.Json.JsonSerializer.Serialize(new ClientSettings { DistanceFog = DistanceFogMode.Classic });
        Assert.True(serialized.Contains("\"DistanceFog\":\"Classic\"", StringComparison.Ordinal), "fog mode is not written by name");
    }

    private static async Task PersistenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "client-settings.json");
        try
        {
            var store = new ClientSettingsStore(path, NullClientLog.Instance);
            Assert.True(store.Load() == new ClientSettings(), "missing file did not create defaults");
            var latest = new ClientSettings();
            for (var index = 30; index <= 500; index++)
            {
                latest = latest with { MaxFps = index, WindowMode = WindowMode.Borderless };
                Assert.True(store.QueueSave(latest), "live persistence queue rejected settings");
            }

            latest = latest with { MusicEnabled = false, SoundEnabled = false };
            Assert.True(store.QueueSave(latest), "audio preferences were rejected by the persistence queue");

            await store.CompleteAsync();
            Assert.True(store.LastError.Length == 0 && !store.QueueSave(new ClientSettings()), "shutdown did not finish or close persistence");
            var reopened = new ClientSettingsStore(path, NullClientLog.Instance);
            Assert.True(reopened.Load() == latest, "latest settings were lost on close/reopen");
            await reopened.CompleteAsync();
            Assert.True(Directory.GetFiles(directory).Length == 1, "temporary files leaked");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task PreserveInvalidAsync()
    {
        foreach (var contents in new[] { "{not json", "null", "{\"version\":999}", "{\"maxFPS_typo\":60}" })
        {
            var path = Path.Combine(Path.GetTempPath(), "priston-settings-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await File.WriteAllTextAsync(path, contents);
                var store = new ClientSettingsStore(path, NullClientLog.Instance);
                Assert.True(store.Load() == new ClientSettings(), "unreadable settings did not fall back");
                Assert.True(store.LastError.Length > 0 && !store.QueueSave(new ClientSettings { MaxFps = 60 }), "invalid file was writable or failure was hidden");
                await store.CompleteAsync();
                Assert.True(await File.ReadAllTextAsync(path) == contents, "original document was destroyed");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static async Task WriteFailureAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "priston-settings-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "directory blocked by file");
        try
        {
            var store = new ClientSettingsStore(Path.Combine(path, "settings.json"), NullClientLog.Instance);
            store.QueueSave(new ClientSettings());
            await store.CompleteAsync();
            Assert.True(store.LastError.Length > 0, "write failure was silently discarded");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task PacingAsync()
    {
        var pacer = new FramePacer();
        const long frequency = 1_000_000;
        var deadline = pacer.Schedule(frequency, frequency, 100);
        Assert.True(deadline == 1_010_000, "initial frame deadline is wrong");
        Assert.True(pacer.Schedule(deadline + 2_000, frequency, 100) == deadline + 10_000, "work time was added to each frame interval");
        Assert.True(pacer.Schedule(2_000_000, frequency, 100) == 2_010_000, "a hitch created a catch-up burst");
        Assert.True(pacer.Schedule(3_000_000, frequency, 0) == 3_000_000, "unlimited pacing delayed the frame");
        Assert.True(pacer.Schedule(4_000_000, frequency, 50) == 4_020_000, "changed limit retained the previous deadline");
        var settings = new ClientSettings { MaxFps = 60, BackgroundMaxFps = 120 };
        Assert.True(settings.FrameLimit(false) == 60 && settings.FrameLimit(true) == 60, "background increased the foreground ceiling");
        settings = settings with { MaxFps = 0, BackgroundMaxFps = 30 };
        Assert.True(settings.FrameLimit(false) == 30 && settings.FrameLimit(true) == 0, "focus did not select the configured limit");
        return Task.CompletedTask;
    }
}
