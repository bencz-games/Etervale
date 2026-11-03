using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;

namespace PristonTale.Display;

internal sealed class PresentationController
{
    // A changed display is kept only when confirmed within this many seconds.
    private const int ConfirmSeconds = 15;

    private readonly PresentationSettings _shared;
    private readonly ClientSettingsStore _store;
    private readonly DisplayController _display;
    private readonly GraphicsDeviceManager _graphics;
    private readonly GameWindow _window;
    private readonly FramePacer _pacer = new();
    private ClientSettings _applied;
    private ClientSettings _accepted;
    private long _confirmationDeadline;
    // The last position of the window while windowed and not maximized: the one it is restored to.
    private Point? _normalPosition;

    public PresentationController(PresentationSettings shared, ClientSettingsStore store, GraphicsDeviceManager graphics, GameWindow window)
    {
        _shared = shared;
        _store = store;
        _graphics = graphics;
        _window = window;
        _display = new DisplayController(graphics, window, ConsoleClientLog.Instance);
        DisplayController.DescribeOptions(shared);
        // The saved display was confirmed when it was chosen, so it applies without asking again; a failing first apply
        // falls back to the defaults (Apply).
        _accepted = shared.Requested;
        Apply(shared.Requested);
        _accepted = _applied;
    }

    public void UpdateBoundary()
    {
        var requested = _shared.Requested;
        if (requested != _applied && !_shared.AwaitingConfirmation)
        {
            requested = requested.Validate(ConsoleClientLog.Instance);
            _shared.Requested = requested;
            var preview = !requested.HasSameDisplay(_applied);
            Apply(requested);
            if (preview && _applied == requested)
            {
                _shared.AwaitingConfirmation = true;
                _confirmationDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * ConfirmSeconds;
            }
            else
            {
                Accept();
            }
        }

        if (_shared.AwaitingConfirmation)
        {
            if (_confirmationDeadline == 0)
            {
                _confirmationDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * ConfirmSeconds;
            }

            _shared.ConfirmationSeconds = Math.Max(0,
                (int)Math.Ceiling((double)(_confirmationDeadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency));
            if (_shared.RevertRequested || _shared.ConfirmationSeconds == 0)
            {
                Apply(_accepted);
                _shared.Requested = _accepted;
                _shared.AwaitingConfirmation = false;
            }
            else if (_shared.ConfirmRequested)
            {
                Accept();
            }
        }

        _shared.ConfirmRequested = false;
        _shared.RevertRequested = false;
        if (_store.LastError.Length > 0)
        {
            _shared.Message = _store.LastError;
        }
    }

    // Every frame: a manual resize becomes the windowed size; a maximized window keeps the size it is restored to.
    public void CaptureWindowSize(int width, int height)
    {
        if (_applied is null || _applied.WindowMode != WindowMode.Windowed || _window.IsMaximized)
        {
            return;
        }

        _normalPosition = _window.Position;
        if (_shared.AwaitingConfirmation || width < HudLayout.DesignWidth || height < HudLayout.DesignHeight || _shared.Requested != _applied)
        {
            return;
        }

        if (width == _applied.WindowWidth && height == _applied.WindowHeight)
        {
            return;
        }

        _applied = _applied with { WindowWidth = width, WindowHeight = height };
        _shared.Requested = _applied;
        Accept();
    }

    // On exit: where the windowed window was left and whether it was maximized, for the next start (DisplayController
    // restores them).
    public void CaptureWindowPlacement()
    {
        if (_applied is null || _shared.AwaitingConfirmation || _applied.WindowMode != WindowMode.Windowed || _shared.Requested != _applied)
        {
            return;
        }

        var maximized = _window.IsMaximized;
        var position = maximized ? _normalPosition : _window.Position;
        var x = position?.X ?? _applied.WindowX;
        var y = position?.Y ?? _applied.WindowY;
        if (x == _applied.WindowX && y == _applied.WindowY && maximized == _applied.WindowMaximized)
        {
            return;
        }

        _applied = _applied with { WindowX = x, WindowY = y, WindowMaximized = maximized };
        Accept();
    }

    public long Pace(bool active)
    {
        var started = Stopwatch.GetTimestamp();
        var deadline = _pacer.Schedule(started, Stopwatch.Frequency, _applied.FrameLimit(active));
        var now = started;
        while (now < deadline)
        {
            var remainingMs = (deadline - now) * 1000d / Stopwatch.Frequency;
            Thread.Sleep(Math.Clamp((int)Math.Ceiling(remainingMs), 1, 4));
            now = Stopwatch.GetTimestamp();
        }

        return now - started;
    }

    public Task CompleteAsync() => _store.CompleteAsync();

    private void Apply(ClientSettings settings)
    {
        _shared.Requested = settings;
        try
        {
            if (_applied is null || !settings.HasSameDisplay(_applied))
            {
                _shared.EffectiveDisplay = _display.Apply(settings);
            }
            else if (settings.VerticalSync != _applied.VerticalSync)
            {
                _graphics.SynchronizeWithVerticalRetrace = settings.VerticalSync;
                _graphics.ApplyChanges();
            }

            _shared.ApplyRendering(settings);
            _applied = settings;
            _shared.Message = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NoSuitableGraphicsDeviceException)
        {
            ConsoleClientLog.Instance.Error("[Display] Requested configuration failed; restoring the accepted settings.", exception);
            var fallback = _applied is null ? new ClientSettings() : _accepted;
            // PreparingDeviceSettings reads Requested, so rollback must publish it before ApplyChanges.
            _shared.Requested = fallback;
            _shared.EffectiveDisplay = _display.Apply(fallback);
            _shared.ApplyRendering(fallback);
            _shared.Message = exception.Message;
            _applied = fallback;
        }
    }

    private void Accept()
    {
        _accepted = _applied;
        _shared.Requested = _applied;
        _shared.AwaitingConfirmation = false;
        _store.QueueSave(_accepted);
    }
}
