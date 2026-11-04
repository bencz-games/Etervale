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
        settings = new ClientSettings { WindowWidth = 3440, WindowHeight = 1440, RefreshRate = 165, MaxFps = 237, Monitor = 2 };
        Assert.True(settings.Validate(NullClientLog.Instance) == settings, "valid custom mode or frame limit was lost");
        Assert.True(settings.HasSameDisplay(settings with { MaxFps = 60, UiScale = 1.2f, MusicEnabled = false, SoundEnabled = false }), "non-device settings triggered display reset");
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
