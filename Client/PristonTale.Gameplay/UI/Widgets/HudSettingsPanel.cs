using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Display/audio options reached from the system menu "Settings" entry, drawn in the original help box.
// The interface scale is not listed: the in-game HUD always uses the largest integer scale that fits.
internal sealed class HudSettingsPanel
{
    public const int BoxX = 50;
    public const int BoxY = 45;
    public const int SizeW = 9;
    public const int SizeH = 14;
    private const int RowHeight = 17;

    private static readonly int[] FrameLimits = [0, 30, 60, 90, 120, 144, 165, 240, 360];
    private static readonly int[] ShadowSizes = [1024, 2048, 4096, 8192];
    private static readonly GraphicsQuality[] Presets = [GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High, GraphicsQuality.Ultra];
    private static readonly int[] ShadowUpdateRates = [30, 60, 120, 0];
    private readonly PresentationSettings _settings;
    private readonly string[] _labels = new string[19];
    private ClientSettings _labelSettings;

    public HudSettingsPanel(PresentationSettings settings) => _settings = settings;

    public static Rectangle Box => HudPainter.HelpBoxBounds(BoxX, BoxY, SizeW, SizeH);
    public static Rectangle Title => new(BoxX + (SizeW * 32 + 64) / 2 - 76, BoxY + 14, 152, 28);
    public static Rectangle OkButton => new(BoxX + (SizeW * 32 + 64) / 2 - 24 + 9, BoxY + SizeH * 32 - 2 + 4, 32, 16);

    public static Rectangle Row(int row) => new(BoxX + 24, BoxY + 64 + row * RowHeight, SizeW * 32 + 64 - 48, RowHeight - 1);

    public void Click(Point pointer, bool backwards)
    {
        if (_settings.AwaitingConfirmation)
        {
            return;
        }

        var current = _settings.Requested;
        for (var row = 0; row < _labels.Length; row++)
        {
            if (!Row(row).Contains(pointer))
            {
                continue;
            }

            var direction = backwards ? -1 : 1;
            _settings.Requested = row switch
            {
                0 => current with { WindowMode = (WindowMode)(((int)current.WindowMode + direction + 3) % 3) },
                1 => current with { Monitor = ((current.Monitor + 1 + direction + _settings.MonitorCount + 1) % (_settings.MonitorCount + 1)) - 1 },
                2 => NextResolution(current, direction),
                3 => current with { RefreshRate = NextFrequency(current, direction) },
                4 => current with { VerticalSync = !current.VerticalSync },
                5 => current with { MusicEnabled = !current.MusicEnabled },
                6 => current with { SoundEnabled = !current.SoundEnabled },
                7 => current with { MaxFps = Cycle(FrameLimits, current.MaxFps, direction) },
                8 => current with { BackgroundMaxFps = Cycle(FrameLimits, current.BackgroundMaxFps, direction) },
                9 => current with { Quality = GraphicsQuality.Custom, AntiAliasing = (AntiAliasingMode)(((int)current.AntiAliasing + direction + 5) % 5) },
                10 => current with { Quality = GraphicsQuality.Custom, ShadowMapSize = Cycle(ShadowSizes, current.ShadowMapSize, direction) },
                11 => current.WithQuality(NextPreset(current.Quality, direction)),
                12 => current with { Quality = GraphicsQuality.Custom, ShadowUpdateRate = Cycle(ShadowUpdateRates, current.ShadowUpdateRate, direction) },
                13 => current with { Quality = GraphicsQuality.Custom, ShadowsEnabled = !current.ShadowsEnabled },
                14 => current with { ClassicTargetHealthBar = !current.ClassicTargetHealthBar },
                15 => current with { ClassicNamePlates = !current.ClassicNamePlates },
                16 => current with { WeatherEnabled = !current.WeatherEnabled },
                17 => current with { RealisticSky = !current.RealisticSky },
                18 => current with { Quality = GraphicsQuality.Custom, CloudQuality = (CloudQuality)(((int)current.CloudQuality + direction + 4) % 4) },
                _ => current
            };
            return;
        }
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        RefreshLabels();
        painter.HelpBox(BoxX, BoxY, SizeW, SizeH);
        var title = Title;
        painter.Sprite(HudArt.HelpTitle, title.X, title.Y);
        painter.TextCentered("Settings", title.Left, title.Right, title.Y + 7, new Color(255, 255, 180));
        for (var index = 0; index < _labels.Length; index++)
        {
            var row = Row(index);
            if (row.Contains(pointer))
            {
                painter.Fill(row, Color.FromNonPremultiplied(255, 200, 0, 64));
            }

            painter.Text(painter.Fit(_labels[index], row.Width - 8), row.X + 6, row.Y + 1);
        }

        var notes = Row(_labels.Length);
        painter.Text(painter.Fit(_settings.EffectiveDisplay, notes.Width - 8), notes.X + 6, notes.Y + 6, new Color(200, 200, 200));
        var hint = _settings.Message.Length > 0 ? "Settings need attention; see the client log." : "Click: next   Shift+click: previous";
        painter.Text(hint, notes.X + 6, notes.Y + 6 + RowHeight, _settings.Message.Length > 0 ? Color.Orange : new Color(200, 200, 200));
        var ok = OkButton;
        painter.Sprite(HudArt.HelpButton, ok.X - 9, ok.Y - 4);
        painter.Sprite(ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, ok.X, ok.Y);
    }

    private ClientSettings NextResolution(ClientSettings current, int direction)
    {
        var sizes = Resolutions(current.WindowMode, _settings.DisplayModes, _settings.Desktop);
        if (sizes.Count == 0)
        {
            return current;
        }

        var index = sizes.IndexOf(new Point(current.WindowWidth, current.WindowHeight));
        var next = index < 0 && direction < 0 ? sizes[^1] : sizes[(index + direction + sizes.Count) % sizes.Count];
        return current with { WindowWidth = next.X, WindowHeight = next.Y };
    }

    // The sizes a mode offers, smallest first, all from the modes the monitor reports: fullscreen any of them; a window
    // those that fit on the desktop below its title bar; borderless none, since it always covers the desktop.
    internal static List<Point> Resolutions(WindowMode mode, IReadOnlyList<DisplayModeOption> modes, DisplayModeOption desktop)
    {
        var sizes = new List<Point>();
        if (mode == WindowMode.Borderless)
        {
            return sizes;
        }

        foreach (var option in modes)
        {
            var size = new Point(option.Width, option.Height);
            var fits = mode == WindowMode.Fullscreen || option.Width <= desktop.Width && option.Height <= desktop.Height - WindowPlacement.TitleBar;
            if (fits && !sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }

        sizes.Sort(static (left, right) => ((long)left.X * left.Y).CompareTo((long)right.X * right.Y));
        return sizes;
    }

    private int NextFrequency(ClientSettings current, int direction)
    {
        if (current.WindowMode != WindowMode.Fullscreen)
        {
            return 0;
        }

        var frequencies = new List<int> { 0 };
        foreach (var mode in _settings.DisplayModes)
        {
            if ((current.Monitor == -1 || mode.Monitor == current.Monitor)
                && mode.Width == current.WindowWidth && mode.Height == current.WindowHeight && !frequencies.Contains(mode.RefreshRate))
            {
                frequencies.Add(mode.RefreshRate);
            }
        }

        frequencies.Sort();
        return Cycle(frequencies, current.RefreshRate, direction);
    }

    private static GraphicsQuality NextPreset(GraphicsQuality quality, int direction)
    {
        var index = Array.IndexOf(Presets, quality);
        if (index < 0)
        {
            return direction > 0 ? Presets[0] : Presets[^1];
        }

        return Presets[(index + direction + Presets.Length) % Presets.Length];
    }

    private static int Cycle(IReadOnlyList<int> values, int value, int direction)
    {
        var index = 0;
        for (var candidate = 0; candidate < values.Count; candidate++)
        {
            if (values[candidate] == value)
            {
                index = candidate;
                break;
            }
        }

        return values[(index + direction + values.Count) % values.Count];
    }

    private void RefreshLabels()
    {
        var settings = _settings.Requested;
        if (_labelSettings == settings)
        {
            return;
        }

        _labelSettings = settings;
        _labels[0] = $"Window: {settings.WindowMode}";
        _labels[1] = settings.Monitor < 0 ? "Monitor: current" : $"Monitor: {settings.Monitor}";
        _labels[2] = settings.WindowMode == WindowMode.Borderless ? "Resolution: desktop" : $"Resolution: {settings.WindowWidth} x {settings.WindowHeight}";
        _labels[3] = settings.WindowMode != WindowMode.Fullscreen ? "Frequency: desktop" : $"Frequency: {settings.RefreshRate} Hz (0 = automatic)";
        _labels[4] = settings.VerticalSync ? "VSync: on" : "VSync: off";
        _labels[5] = settings.MusicEnabled ? "BGM: on" : "BGM: off";
        _labels[6] = settings.SoundEnabled ? "Audio: on" : "Audio: off";
        _labels[7] = settings.MaxFps == 0 ? "Maximum FPS: unlimited" : $"Maximum FPS: {settings.MaxFps}";
        _labels[8] = settings.BackgroundMaxFps == 0 ? "Background FPS: foreground limit" : $"Background FPS: {settings.BackgroundMaxFps}";
        _labels[9] = settings.AntiAliasing == AntiAliasingMode.Msaa
            ? $"Anti-aliasing: MSAA {Math.Max(settings.MultiSampleCount, 2)}x"
            : $"Anti-aliasing: {settings.AntiAliasing.ToString().ToUpperInvariant()}";
        _labels[10] = $"Sun shadow: {settings.ShadowMapSize}";
        _labels[11] = $"Graphics quality: {settings.Quality}";
        _labels[12] = settings.ShadowUpdateRate == 0 ? "Shadow animation: every frame" : $"Shadow animation: {settings.ShadowUpdateRate} Hz";
        _labels[13] = settings.ShadowsEnabled ? "Shadows: on" : "Shadows: off";
        _labels[14] = settings.ClassicTargetHealthBar ? "Target HP: classic (after first hit)" : "Target HP: always, with percentage";
        _labels[15] = settings.ClassicNamePlates ? "Name plates: classic" : "Name plates: compact";
        _labels[16] = settings.WeatherEnabled ? "Weather: on" : "Weather: off";
        _labels[17] = settings.RealisticSky ? "Sky: realistic" : "Sky: classic";
        _labels[18] = $"Clouds: {settings.CloudQuality}";
    }
}
