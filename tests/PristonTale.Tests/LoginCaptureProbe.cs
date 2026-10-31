using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Tests;

internal sealed class LoginCaptureProbe : Game
{
    private sealed record Scenario(string Name, int Width, int Height, bool Busy = false, string Status = "", bool LoginFocus = false);

    private static readonly Scenario[] Scenarios =
    [
        new("login-1280", 1280, 720),
        new("login-800", 800, 600),
        new("login-ultrawide", 3440, 1440),
        new("login-connecting", 1280, 720, Busy: true, Status: "Connecting…"),
        new("login-error", 800, 600, Status: "Could not reach server. Please try again."),
        new("login-keyboard-focus", 1280, 720, LoginFocus: true)
    ];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly string _backgroundPath;
    private SpriteBatch _batch = null!;
    private SpriteFont _font = null!;
    private HudAtlas _atlas = null!;
    private Texture2D _background = null!;
    private int _index;

    public LoginCaptureProbe(string assetRoot, string contentRoot, string output, string background)
    {
        _assetRoot = assetRoot;
        _output = output;
        _backgroundPath = background;
        _ = new GraphicsDeviceManager(this) { SynchronizeWithVerticalRetrace = false };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _batch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/UI");
        _atlas = HudAtlas.LoadBootstrap(GraphicsDevice, _assetRoot, ConsoleClientLog.Instance);
        var decoded = PristonImageDecoder.Decode(File.ReadAllBytes(_backgroundPath), ".png");
        _background = new Texture2D(GraphicsDevice, decoded.Width, decoded.Height);
        _background.SetData(decoded.Pixels);
        var context = new ScreenContext(GraphicsDevice, Content, _batch, _font, _assetRoot, null!, ConsoleClientLog.Instance,
            _ => { }, uiAtlas: _atlas);
        using (var hud = new GameplayHud(context, _ => throw new InvalidOperationException("Capture only")))
        {
            // Screen transitions can Draw immediately after Enter, before the first HUD Update or sprite upload.
            hud.Draw();
            var viewport = GraphicsDevice.Viewport;
            GraphicsDevice.Viewport = new Viewport(0, 0, 1, 1);
            hud.Draw();
            GraphicsDevice.Viewport = viewport;
            hud.Draw();
        }

        Assert.True(!_atlas.Texture(_atlas.Region("Panel").Texture).IsDisposed, "screen disposed the shared atlas");
        ValidateInput();
    }

    private LoginPanel CreatePanel(Scenario scenario)
    {
        var panel = new LoginPanel(_batch, _font, _atlas);
        panel.Account.Update("panzer", 0);
        panel.Password.Focused = true;
        panel.Password.Update("secret", 0);

        panel.Password.Focused = false;
        if (scenario.LoginFocus)
        {
            for (var index = 0; index < 2; index++)
            {
                panel.Update(new KeyboardState(Keys.Tab), default, default, [], 0, new Viewport(0, 0, scenario.Width, scenario.Height));
            }
        }

        panel.InputEnabled = !scenario.Busy;
        panel.Status = scenario.Status;
        return panel;
    }

    private void ValidateInput()
    {
        var viewport = new Viewport(0, 0, 800, 600);
        var panel = CreatePanel(Scenarios[0]);
        panel.InputEnabled = false;
        var original = panel.Account.Text;
        var intent = panel.Update(new KeyboardState(Keys.A, Keys.Enter), default, default, [], 0, viewport);
        Assert.True(intent == LoginIntent.None && panel.Account.Text == original, "disabled login accepted input");
        panel.InputEnabled = true;
        panel.Update(new KeyboardState(Keys.LeftShift, Keys.Tab), default, default, [], 0, viewport);
        intent = panel.Update(new KeyboardState(Keys.Enter), default, default, [], 0, viewport);
        Assert.True(intent == LoginIntent.SubmitLogin, "keyboard focus could not activate login");
        var layout = LoginLayout.Create(viewport.Width, viewport.Height);
        var center = layout.Login.Center;
        var mouse = new MouseState((int)(center.X * layout.Scale), (int)(center.Y * layout.Scale), 0,
            ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        intent = panel.Update(default, default, mouse, [], 0, viewport);
        Assert.True(intent == LoginIntent.SubmitLogin, "scaled login hit region differs from rendering");
        Assert.True(panel.Update(default, default, mouse, [], 0, viewport) == LoginIntent.None, "held button submitted twice");
        Assert.True(panel.Password.DisplayText == "******", "password was exposed in presentation");
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_index == Scenarios.Length)
        {
            Exit();
            return;
        }

        var scenario = Scenarios[_index++];
        using var target = new RenderTarget2D(GraphicsDevice, scenario.Width, scenario.Height);
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(Color.Black);
        _batch.Begin();
        _batch.Draw(_background, new Rectangle(0, 0, scenario.Width, scenario.Height), Color.White);
        _batch.End();
        CreatePanel(scenario).Draw(GraphicsDevice.Viewport);
        GraphicsDevice.SetRenderTarget(null);
        using var file = File.Create(Path.Combine(_output, scenario.Name + ".png"));
        target.SaveAsPng(file, target.Width, target.Height);
        Console.WriteLine($"Login {scenario.Name}: captured; atlas={_atlas.TextureBytes} bytes; GPU={GraphicsDevice.Adapter.DeviceName}");
    }

    protected override void UnloadContent()
    {
        _background?.Dispose();
        _atlas?.Dispose();
        _batch?.Dispose();
        base.UnloadContent();
    }
}
