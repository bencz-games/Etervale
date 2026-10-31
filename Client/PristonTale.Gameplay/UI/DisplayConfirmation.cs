using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI;

internal sealed class DisplayConfirmation
{
    private readonly ScreenContext _context;
    private readonly HudCanvas _canvas;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private int _seconds = -1;
    private string _countdown = string.Empty;

    public DisplayConfirmation(ScreenContext context)
    {
        _context = context;
        _canvas = new HudCanvas(context.SpriteBatch, context.Font, context.UiAtlas);
    }

    public void Update()
    {
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        var settings = _context.Presentation;
        if (settings.AwaitingConfirmation && _context.IsWindowActive)
        {
            var layout = Layout();
            var panel = Panel(layout);
            var pointer = layout.ToLogical(mouse.Position);
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            settings.ConfirmRequested |= keyboard.IsKeyDown(Keys.Enter) && !_previousKeyboard.IsKeyDown(Keys.Enter)
                || clicked && Confirm(panel).Contains(pointer);
            settings.RevertRequested |= keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape)
                || clicked && Revert(panel).Contains(pointer);
        }

        if (_seconds != settings.ConfirmationSeconds)
        {
            _seconds = settings.ConfirmationSeconds;
            _countdown = $"Reverting in {_seconds} seconds";
        }

        _previousMouse = mouse;
        _previousKeyboard = keyboard;
    }

    public void Draw()
    {
        if (!_context.Presentation.AwaitingConfirmation)
        {
            return;
        }

        var layout = Layout();
        var panel = Panel(layout);
        _canvas.Begin(layout.Scale);
        _canvas.Frame(panel);
        _canvas.Text("Keep these display settings?", new Rectangle(panel.X + 20, panel.Y + 14, panel.Width - 40, 32), centered: true);
        _canvas.Text(_context.Presentation.EffectiveDisplay, new Rectangle(panel.X + 20, panel.Y + 50, panel.Width - 40, 28), pixels: 16f, centered: true);
        _canvas.Text(_countdown, new Rectangle(panel.X + 20, panel.Y + 82, panel.Width - 40, 25), centered: true);
        _canvas.Frame(Confirm(panel), "Button", 6);
        _canvas.Frame(Revert(panel), "Button", 6);
        _canvas.Text("Keep (Enter)", Confirm(panel), centered: true);
        _canvas.Text("Revert (Esc)", Revert(panel), centered: true);
        _canvas.End();
    }

    private HudLayout Layout()
    {
        var viewport = _context.GraphicsDevice.Viewport;
        return HudLayout.Create(viewport.Width, viewport.Height);
    }

    // Uses the in-game integer scale; the frame comes from the shared login atlas.
    private static Rectangle Panel(HudLayout layout)
    {
        var width = Math.Min(620, layout.Width);
        return new Rectangle((layout.Width - width) / 2, (layout.Height - 180) / 2, width, 180);
    }

    private static Rectangle Confirm(Rectangle panel) => new(panel.X + 24, panel.Bottom - 58, 272, 36);
    private static Rectangle Revert(Rectangle panel) => new(panel.X + 324, panel.Bottom - 58, 272, 36);
}
