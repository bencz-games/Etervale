using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

// GPU captures of the chat window in its states and of speech bubbles over characters:
// --capture-chat <assets> <content> <output> [background.png].
internal sealed class ChatCaptureProbe : Game
{
    private sealed record Scenario(string Name, int Width, int Height, bool Open = true, bool Focused = true, HudChatTab Tab = HudChatTab.All,
        int NoticeMode = 0, Keys Panel = Keys.None, bool Bubbles = false);

    private static readonly Scenario[] Scenarios =
    [
        new("chat-open-800", 800, 600),
        new("chat-closed-800", 800, 600, Open: false, Focused: false),
        new("chat-unfocused-800", 800, 600, Focused: false),
        new("chat-whisper-tab-800", 800, 600, Tab: HudChatTab.Whisper),
        new("chat-trade-tab-800", 800, 600, Tab: HudChatTab.Trade),
        new("chat-assist-split-800", 800, 600, NoticeMode: 1),
        new("chat-assist-page-800", 800, 600, NoticeMode: 2),
        new("chat-side-inventory-800", 800, 600, Panel: Keys.I),
        new("chat-1280", 1280, 720),
        new("chat-1600x1200-scale2", 1600, 1200),
        new("bubbles-800", 800, 600, Open: false, Focused: false, Bubbles: true),
        new("bubbles-chat-1280", 1280, 720, Focused: false, Bubbles: true)
    ];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly string? _backgroundPath;
    private SpriteBatch _batch = null!;
    private GameplayHud _hud = null!;
    private Texture2D? _background;
    private RenderTarget2D? _target;
    private OwnerState _owner = null!;
    private int _scenario;
    private int _frames;
    private bool _prepared;
    private readonly PresentationSettings _presentation = new();

    public ChatCaptureProbe(string assetRoot, string contentRoot, string output, string? background)
    {
        _assetRoot = assetRoot;
        _output = output;
        _backgroundPath = background;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _batch = new SpriteBatch(GraphicsDevice);
        var font = Content.Load<SpriteFont>("Fonts/UI");
        var context = new ScreenContext(GraphicsDevice, Content, _batch, font, _assetRoot, null!, ConsoleClientLog.Instance, _ => { }, _presentation);
        _hud = new GameplayHud(context, _ => Task.FromResult(new InventoryOutcome(false, 0, GameText.Of(GameTextId.RequestRefused))));
        _hud.CharacterName = "Panzer";
        _hud.CharacterClass = PlayerClass.Mechanician;
        _owner = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer"));
        _hud.World = new HudWorldView("village-2", false, default, Vector3.Zero, Vector3.Forward, 13, false, MapHidden: true);
        _hud.Combat.SetCatalog(new WorldRenderCatalog());
        _hud.Combat.Observe(new PresenceUpdate(1,
        [
            new(1, EntityKind.Player, true, default, 0, MotionState.Stand, 64, 64, new PlayerAppearanceInfo("Panzer", PlayerClass.Mechanician, 0, 0, 0, "", "", 1), 0),
            new(2, EntityKind.Player, false, default, 0, MotionState.Stand, 80, 80, new PlayerAppearanceInfo("Rosalind", PlayerClass.Atalanta, 0, 0, 0, "", "", 1), 0),
            new(3, EntityKind.Npc, false, default, 0, MotionState.Stand, 0, 0, null, 1, Name: "Lena"),
            new(4, EntityKind.Monster, false, default, 0, MotionState.Stand, 30, 30, null, 1, Name: "Hopy")
        ], [], [], []));
        _hud.SelfId = 1;
        _hud.Chat.SelfId = 1;
        _hud.Chat.PlayerName = "Panzer";
        var points = new Dictionary<ulong, HudOverheadPoint>();
        _hud.Overhead = id => points.TryGetValue(id, out var point) ? point : null;
        _points = points;
        Fill(_hud.Chat);
        if (_backgroundPath is not null)
        {
            var image = PristonImageDecoder.Decode(File.ReadAllBytes(_backgroundPath), ".png");
            _background = new Texture2D(GraphicsDevice, image.Width, image.Height);
            _background.SetData(image.Pixels);
        }
    }

    private Dictionary<ulong, HudOverheadPoint> _points = null!;

    // Every kind of line the window shows, oldest first.
    private static void Fill(HudChat chat)
    {
        var log = chat.Window.Log;
        log.Add("Notice> Welcome to Etervale. Double experience this weekend!", ChatKind.Notice);
        log.Add("Rosalind: anyone going to the Bamboo Forest? I need two more for the hunt near the river", ChatKind.Player);
        log.Add("Panzer: count me in", ChatKind.Player);
        log.Add("Notice> The servers will be going down soon due to maintenance", ChatKind.Notice);
        log.Add("Merchant Kay: Selling a +4 Dragon Axe, whisper me", ChatKind.Trade);
        log.Add("From> Rosalind: meet me at the fountain", ChatKind.Whisper);
        log.Add("To> Rosalind: on my way", ChatKind.Whisper);
        log.Add("Gnome: party pull in 5", ChatKind.Party);
        log.Add("Ironwood> clan war at nine", ChatKind.Clan);
        log.Add("Rosalind is currently online", ChatKind.Server);
        log.Add("Bought.", ChatKind.Server);
        log.Add("Kill 10 Hopy near the windmill.", ChatKind.Assist);
        log.Add("Report back to the Bargon of Ricarten.", ChatKind.Assist);
        log.Add("Rosalind: see you there", ChatKind.Player);
    }

    protected override void Update(GameTime gameTime)
    {
        _frames++;
        if (_frames > 3600 || _hud.AssetsFailed)
        {
            throw new InvalidOperationException("Chat assets did not become ready within the capture timeout.");
        }

        if (_scenario >= Scenarios.Length)
        {
            Exit();
            return;
        }

        var scenario = Scenarios[_scenario];
        if (!_prepared)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(GraphicsDevice, scenario.Width, scenario.Height);
            _hud.ClosePanel();
            _hud.ClosePanel();
            _hud.Update(_owner, default, default);
            _hud.Update(_owner, scenario.Panel == Keys.None ? default : new KeyboardState(scenario.Panel), default);
            var window = _hud.Chat.Window;
            window.Input.Set("/:Rosalind bring potions please");
            if (scenario.Open)
            {
                window.OpenInput();
                if (!scenario.Focused)
                {
                    window.Unfocus();
                }
            }

            window.Log.SelectTab(scenario.Tab);
            while (window.NoticeMode != scenario.NoticeMode)
            {
                window.Click(HudLayout.Create(scenario.Width, scenario.Height), HudChatWindow.NoticeButton(HudLayout.Create(scenario.Width, scenario.Height)).Center,
                    side: false);
            }

            _points.Clear();
            if (scenario.Bubbles)
            {
                var scale = HudLayout.Create(scenario.Width, scenario.Height).Scale;
                _points[1] = new HudOverheadPoint(new Vector2(scenario.Width * 0.5f, scenario.Height * 0.55f), 200);
                _points[2] = new HudOverheadPoint(new Vector2(scenario.Width * 0.72f, scenario.Height * 0.45f), 300);
                _points[3] = new HudOverheadPoint(new Vector2(scenario.Width * 0.25f, scenario.Height * 0.32f), 400);
                _points[4] = new HudOverheadPoint(new Vector2(scenario.Width * 0.6f, scenario.Height * 0.22f), 600);
                var now = 0d;
                _hud.Chat.Bubbles.Say(1, "Panzer: count me in", now, int.MaxValue);
                _hud.Chat.Bubbles.Say(2, "Rosalind: anyone going to the Bamboo Forest? I need two more for the hunt near the river", now, int.MaxValue);
                _hud.Chat.Bubbles.Say(3, "Lena: Welcome, traveller.", now, int.MaxValue);
                _hud.Chat.Bubbles.Say(4, "Hopy:  Leave this forest ", now, int.MaxValue);
                _ = scale;
            }

            _hud.Update(_owner, default, default);
            _prepared = true;
        }
        else
        {
            _hud.Update(_owner, default, default);
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!_prepared || !_hud.IsReady || !_hud.IconsIdle || _scenario >= Scenarios.Length)
        {
            GraphicsDevice.SetRenderTarget(null);
            return;
        }

        var scenario = Scenarios[_scenario];
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(45, 53, 47));
        if (_background is not null)
        {
            _batch.Begin();
            _batch.Draw(_background, new Rectangle(0, 0, scenario.Width, scenario.Height), Color.White);
            _batch.End();
        }

        _hud.Draw();
        GraphicsDevice.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(_output, scenario.Name + ".png"));
        _target!.SaveAsPng(stream, scenario.Width, scenario.Height);
        Console.WriteLine($"Chat {scenario.Name}: captured; shown messages {_hud.Chat.Window.ShownMessages}; bubbles {_hud.Chat.Bubbles.Count}");
        _scenario++;
        _prepared = false;
    }

    protected override void UnloadContent()
    {
        _hud.Dispose();
        _target?.Dispose();
        _background?.Dispose();
        _batch.Dispose();
    }
}
