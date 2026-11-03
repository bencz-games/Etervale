using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;

namespace PristonTale.Display;

// Window mode, size and vertical sync through MonoGame's Native platform (Vulkan). The platform reports the display
// modes of the primary monitor only, without refresh rates, and switches exclusive fullscreen to the closest mode at the
// primary monitor's rate: the client therefore offers one monitor and the desktop frequency (RefreshRate 0). Display
// selection and per-mode refresh rates need runtime support the MonoGame fork does not have yet.
internal sealed class DisplayController
{
    private const int PrimaryMonitor = 0;

    private readonly GraphicsDeviceManager _graphics;
    private readonly GameWindow _window;
    private readonly IClientLog _log;
    private bool _placed;

    public DisplayController(GraphicsDeviceManager graphics, GameWindow window, IClientLog log)
    {
        _graphics = graphics;
        _window = window;
        _log = log;
    }

    public string Apply(ClientSettings requested)
    {
        var width = requested.WindowWidth;
        var height = requested.WindowHeight;
        if (requested.Monitor > PrimaryMonitor)
        {
            _log.Warn($"[Display] Monitor {requested.Monitor} is not selectable on this platform; using the primary monitor.");
        }

        if (requested.RefreshRate != 0)
        {
            _log.Warn($"[Display] A {requested.RefreshRate} Hz mode is not selectable on this platform; using the desktop frequency.");
        }

        // Leave fullscreen before choosing a new exclusive mode.
        if (_graphics.IsFullScreen)
        {
            _graphics.IsFullScreen = false;
            _graphics.ApplyChanges();
        }

        var desktop = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        if (requested.WindowMode == WindowMode.Fullscreen)
        {
            var mode = SelectMode(requested, desktop);
            width = mode.Width;
            height = mode.Height;
        }
        else if (requested.WindowMode == WindowMode.Borderless)
        {
            width = desktop.Width;
            height = desktop.Height;
        }

        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.HardwareModeSwitch = requested.WindowMode == WindowMode.Fullscreen;
        _graphics.IsFullScreen = requested.WindowMode != WindowMode.Windowed;
        _graphics.SynchronizeWithVerticalRetrace = requested.VerticalSync;
        // Multisampling belongs to the HDR scene target (PristonRenderer); the back buffer only receives the display
        // transform and the UI, so it is never multisampled.
        _graphics.PreferMultiSampling = false;
        _graphics.ApplyChanges();
        if (requested.WindowMode == WindowMode.Windowed)
        {
            Place(requested, new Rectangle(0, 0, desktop.Width, desktop.Height));
        }

        var parameters = _graphics.GraphicsDevice.PresentationParameters;
        var effective = $"Monitor {PrimaryMonitor}: {parameters.BackBufferWidth}x{parameters.BackBufferHeight}, desktop Hz, " +
            $"scene MSAA {requested.MultiSampleCount}x requested";
        _log.Info($"[Display] requested={requested.WindowMode} {requested.WindowWidth}x{requested.WindowHeight}@{requested.RefreshRate}; effective={effective}");
        return effective;
    }

    // The platform creates the window centred at MonoGame's default size and a new size grows it from its top-left, so
    // a large window would spill off the screen. The first windowed apply restores the saved position while its title
    // bar is on the screen, or centres the window, then maximizes it if it was; later applies centre a window that no
    // longer fits.
    private void Place(ClientSettings requested, Rectangle screen)
    {
        if (_window.IsMaximized)
        {
            return;
        }

        var size = new Point(_window.ClientBounds.Width, _window.ClientBounds.Height);
        var saved = requested.WindowX is int x && requested.WindowY is int y ? new Point(x, y) : (Point?)null;
        if (!_placed && saved is { } position && WindowPlacement.TitleOnScreen(position, size, screen))
        {
            _window.Position = position;
        }
        else if (!_placed || !WindowPlacement.OnScreen(_window.Position, size, screen))
        {
            _window.Position = WindowPlacement.Centered(size, screen);
        }

        if (!_placed && requested.WindowMaximized)
        {
            _window.Maximize();
        }

        _placed = true;
    }

    public static void DescribeOptions(PresentationSettings shared)
    {
        shared.MonitorCount = 1;
        var options = new List<DisplayModeOption>();
        foreach (var mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
        {
            var option = new DisplayModeOption(PrimaryMonitor, mode.Width, mode.Height, 0);
            if (mode.Width >= HudLayout.DesignWidth && mode.Height >= HudLayout.DesignHeight && !options.Contains(option))
            {
                options.Add(option);
            }
        }

        shared.DisplayModes = options;
        var desktop = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        shared.Desktop = new DisplayModeOption(PrimaryMonitor, desktop.Width, desktop.Height, 0);
    }

    private DisplayMode SelectMode(ClientSettings requested, DisplayMode desktop)
    {
        foreach (var mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
        {
            if (mode.Width == requested.WindowWidth && mode.Height == requested.WindowHeight)
            {
                return mode;
            }
        }

        _log.Warn($"[Display] Requested fullscreen mode unavailable; selecting {desktop.Width}x{desktop.Height}.");
        return desktop;
    }
}
