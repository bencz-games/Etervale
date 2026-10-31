using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.Audio;

namespace PristonTale.Gameplay.Screens.Framework;

public sealed class GameClient : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ContentManager _content;
    private readonly string _assetRoot;
    private readonly string _serverAddress;

    private SpriteBatch _spriteBatch;
    private IGameBackend _backend;
    private ScreenContext _context;
    private GameScreen _screen;
    private GameScreen _pendingScreen;
    private Task _shutdown;
    private SpriteFont _font;
    private HudAtlas _uiAtlas;
    private HudCursor _cursor;
    private PristonSkyLibrary _sky;
    private PristonTale.Gameplay.Effects.EffectLibrary _effects;
    private PristonAudioLibrary _audio;
    private readonly PresentationSettings _presentation;
    private DisplayConfirmation _displayConfirmation;

    public GameClient(GraphicsDevice graphicsDevice, ContentManager content, string assetRoot, string serverAddress, PresentationSettings presentation = null)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _assetRoot = assetRoot;
        _serverAddress = serverAddress;
        _presentation = presentation ?? new PresentationSettings();
    }

    public string StatusText
    {
        get
        {
            if (_shutdown is not null)
            {
                return "Saving and disconnecting…";
            }

            return _screen?.StatusText ?? "Loading…";
        }
    }

    public void LoadContent()
    {
        _spriteBatch = new SpriteBatch(_graphicsDevice);
        var font = _content.Load<SpriteFont>("Fonts/UI");
        _font = font;
        _uiAtlas = HudAtlas.LoadBootstrap(_graphicsDevice, _assetRoot, ConsoleClientLog.Instance);
        _cursor = HudCursor.Load(_graphicsDevice, _assetRoot, ConsoleClientLog.Instance);
        _cursor.Show(HudCursorKind.Default);
        _sky = PristonSkyLibrary.LoadBootstrap(_graphicsDevice, _assetRoot, ConsoleClientLog.Instance);
        _effects = PristonTale.Gameplay.Effects.EffectLibrary.LoadBootstrap(_graphicsDevice, _content, _assetRoot, ConsoleClientLog.Instance);
        _audio = PristonAudioLibrary.LoadBootstrap(_assetRoot, ConsoleClientLog.Instance);
        _audio.ApplySettings(_presentation.Requested);
        _backend = new GameServerConnection(_serverAddress);
        _context = new ScreenContext(
            _graphicsDevice,
            _content,
            _spriteBatch,
            font,
            _assetRoot,
            _backend,
            ConsoleClientLog.Instance,
            RequestScreen,
            _presentation,
            _uiAtlas,
            _sky,
            _effects,
            _audio,
            RenewBackend);
        _displayConfirmation = new DisplayConfirmation(_context);
        // First screen: the opening / authentication scene (the login form overlays it next).
        SetScreen(new OpeningScreen(_context));
    }

    public bool HandleEscape() => _screen?.HandleEscape() == true;

    // The diagnostics overlay (F9, as the original's debug display): the frame timing and the screen's status, drawn at
    // the top left over everything.
    public bool DiagnosticsShown { get; set; }
    public string Diagnostics { get; set; } = string.Empty;

    // A character from the window's text input (the chat line reads it).
    public void TypeCharacter(char value) => _context?.TypedText.Push(value);

    // The server asked the client to save and quit.
    public bool ExitRequested => _context?.ExitRequested == true;

    public void Update(GameTime gameTime, bool isWindowActive = true)
    {
        _audio?.ApplySettings(_presentation.Requested);
        if (_context is not null)
        {
            _context.IsWindowActive = isWindowActive;
        }

        _displayConfirmation?.Update();
        _screen?.Update(gameTime);
        _cursor?.Show(_screen?.Cursor ?? HudCursorKind.Default);
        if (_pendingScreen != null)
        {
            var next = _pendingScreen;
            _pendingScreen = null;
            SetScreen(next);
        }
    }

    public void Draw()
    {
        if (_shutdown is null)
        {
            _screen?.Draw();
            _displayConfirmation?.Draw();
            DrawDiagnostics();
            return;
        }

        _graphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        _spriteBatch.DrawString(_font, StatusText, new Vector2(24f, 24f), Color.White);
        _spriteBatch.End();
    }

    private void DrawDiagnostics()
    {
        if (!DiagnosticsShown || Diagnostics.Length == 0)
        {
            return;
        }

        _spriteBatch.Begin();
        _spriteBatch.DrawString(_font, Diagnostics, new Vector2(9f, 9f), Color.Black);
        _spriteBatch.DrawString(_font, Diagnostics, new Vector2(8f, 8f), Color.White);
        _spriteBatch.End();
    }

    private void RequestScreen(GameScreen next) => _pendingScreen = next;

    private void SetScreen(GameScreen next)
    {
        _screen?.Dispose();
        _screen = next;
        _screen.Enter();
    }

    private void RenewBackend()
    {
        var previous = _backend;
        _backend = new GameServerConnection(_serverAddress);
        _context.ReplaceBackend(_backend);
        _ = Task.Run(() => DisposeBackendAsync(previous));
    }

    private static async Task DisposeBackendAsync(IGameBackend backend)
    {
        try
        {
            await backend.DisposeAsync();
        }
        catch (Exception ex)
        {
            ConsoleClientLog.Instance.Error($"[Client] backend shutdown failed: {ex}");
        }
    }

    public Task BeginShutdown()
    {
        if (_shutdown is not null)
        {
            return _shutdown;
        }

        // GPU resources are released on the game thread. Network/database cleanup runs asynchronously, after the screen's
        // closing work (leaving the world, so the server stores the character) has used the connection.
        var screen = _screen;
        screen?.Dispose();
        var closing = screen?.Closing ?? Task.CompletedTask;
        _screen = null;
        _pendingScreen = null;
        var backend = _backend;
        _shutdown = backend is null ? closing : Task.Run(async () =>
        {
            try
            {
                await closing;
            }
            finally
            {
                await DisposeBackendAsync(backend);
            }
        });
        return _shutdown;
    }

    public void Dispose()
    {
        _ = BeginShutdown();
        _uiAtlas?.Dispose();
        _cursor?.Dispose();
        _sky?.Dispose();
        _effects?.Dispose();
        _audio?.Dispose();
        _spriteBatch?.Dispose();
    }
}
