using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using PristonTale.Display;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Assets;
using PristonTale.Protocol.Transport;

namespace PristonTale;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private GameClient _client;

    private KeyboardState _lastKeyboardState;
    private readonly FrameTiming _timing = new();
    private readonly PresentationSettings _presentation = new();
    private readonly ClientSettingsStore _settingsStore;
    private PresentationController _presentationController;
    private Task _shutdown;

    public Game1(string settingsPath = null)
    {
        _settingsStore = new ClientSettingsStore(settingsPath ?? ClientSettingsStore.DefaultPath, ConsoleClientLog.Instance);
        _presentation.Requested = _settingsStore.Load();
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _presentation.Requested.WindowWidth,
            PreferredBackBufferHeight = _presentation.Requested.WindowHeight,
            GraphicsProfile = GraphicsProfile.HiDef,
            HardwareModeSwitch = true,
            SynchronizeWithVerticalRetrace = _presentation.VerticalSync,
            // The scene renders into the renderer's multisampled HDR target; the back buffer receives the display
            // transform and the UI only.
            PreferMultiSampling = false
        };
        // A variable presentation step avoids repeating client work before Draw; authority remains at 20 Hz.
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / FrameTiming.TargetHz);
        Window.AllowUserResizing = true;
        Window.Title = "Etervale";
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        _presentationController = new PresentationController(_presentation, _settingsStore, _graphics, Window);
        _client = new GameClient(GraphicsDevice, Content, ResolveAssetRoot(), ResolveServerAddress(_presentation.Requested.ServerAddress), _presentation);
        _client.LoadContent();
        Window.TextInput += (_, args) => _client.TypeCharacter(args.Character);
        ConsoleClientLog.Instance.Info($"[Presentation] GPU={GraphicsDevice.Adapter.DeviceName}; variable timestep; VSync={_presentation.VerticalSync}; maxFps={_presentation.Requested.MaxFps} (0=unlimited).");
    }

    // The assets beside the game or in an ancestor folder (AssetFiles.FindAssetsRoot); the game's own folder otherwise, so
    // a missing asset is reported where the files are expected.
    private static string ResolveAssetRoot() => AssetFiles.FindAssetsRoot() ?? Path.Combine(AppContext.BaseDirectory, "assets");

    // The settings' server; an address that does not parse is logged and the client uses the server on this machine.
    private static string ResolveServerAddress(string configured)
    {
        try
        {
            var address = TlsTransport.ParseAddress(configured);
            ConsoleClientLog.Instance.Info($"[Network] Server {address.Host}:{address.Port}.");
            return address.AbsoluteUri;
        }
        catch (ArgumentException exception)
        {
            ConsoleClientLog.Instance.Error($"[Settings] serverAddress: {exception.Message} Using {TlsTransport.LocalAddress}.");
            return TlsTransport.LocalAddress;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        if (_shutdown is not null)
        {
            if (_shutdown.IsCompleted)
            {
                Exit();
            }

            UpdateDiagnostics();
            base.Update(gameTime);
            _timing.RecordUpdate(Stopwatch.GetTimestamp() - started, GC.GetAllocatedBytesForCurrentThread() - allocated);
            return;
        }

        var keyboard = Keyboard.GetState();
        var escapePressed = keyboard.IsKeyDown(Keys.Escape) && !_lastKeyboardState.IsKeyDown(Keys.Escape);
        if (_presentation.AwaitingConfirmation)
        {
            _presentation.RevertRequested |= escapePressed;
            _presentation.ConfirmRequested |= keyboard.IsKeyDown(Keys.Enter) && !_lastKeyboardState.IsKeyDown(Keys.Enter);
            escapePressed = false;
        }
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || escapePressed && !_client.HandleEscape() || _client.ExitRequested)
        {
            Exit();
        }

        if (keyboard.IsKeyDown(Keys.F9) && !_lastKeyboardState.IsKeyDown(Keys.F9))
        {
            _client.DiagnosticsShown = !_client.DiagnosticsShown;
        }

        _client.Update(gameTime, IsActive);
        UpdateDiagnostics();
        _lastKeyboardState = keyboard;
        base.Update(gameTime);
        _timing.RecordUpdate(Stopwatch.GetTimestamp() - started, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    // The frame timing and the screen's status for the diagnostics F9 shows (the original's debug display); the window
    // title stays the game's name. Sampled only while shown.
    private void UpdateDiagnostics()
    {
        if (!_client.DiagnosticsShown || !_timing.TrySample(Stopwatch.GetTimestamp(), out var sample))
        {
            return;
        }

        var limit = _presentation.Requested.FrameLimit(IsActive);
        var ceiling = limit == 0 ? "unlimited" : limit.ToString();
        _client.Diagnostics = $"FPS {sample.FramesPerSecond:F0} (cap {ceiling}, VSync {(_presentation.VerticalSync ? "on" : "off")})"
            + $"\nframe p95/p99 {sample.FrameP95Ms:F1}/{sample.FrameP99Ms:F1} ms, work {sample.WorkP95Ms:F1} ms, present {sample.PresentP95Ms:F1} ms,"
            + $" pacing {sample.PacingP95Ms:F1} ms, {sample.AllocatedBytesPerFrame / 1024:F1} KB/frame"
            + $"\n{GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height} | {_client.StatusText.Replace(" | ", "\n")}";
    }

    protected override void Draw(GameTime gameTime)
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        _client.Draw();
        base.Draw(gameTime);
        _timing.RecordDraw(started, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (_client is not null)
        {
            if (_shutdown is null)
            {
                _presentationController.CaptureWindowPlacement();
            }

            _shutdown ??= Task.WhenAll(_client.BeginShutdown(), _presentationController.CompleteAsync());
            args.Cancel = !_shutdown.IsCompleted;
        }

        base.OnExiting(sender, args);
    }

    protected override void EndDraw()
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        base.EndDraw();
        _timing.RecordPresentation(Stopwatch.GetTimestamp() - started, GC.GetAllocatedBytesForCurrentThread() - allocated);
        if (_shutdown is null)
        {
            _presentationController.CaptureWindowSize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            _presentationController.UpdateBoundary();
        }

        _timing.RecordPacing(_presentationController.Pace(IsActive));
    }

    protected override void UnloadContent()
    {
        _client?.Dispose();
        base.UnloadContent();
    }
}
