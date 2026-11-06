using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public enum LoginIntent
{
    None,
    SubmitLogin
}

public sealed class LoginPanel
{
    private enum FocusTarget
    {
        Account,
        Password,
        Login
    }

    private readonly HudCanvas _canvas;
    private MouseState _previousMouse;
    private FocusTarget _focus;

    public LoginPanel(SpriteBatch batch, SpriteFont font, HudAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        if (!atlas.IsReady)
        {
            throw new InvalidOperationException("Login requires interface resources initialized before the first screen.");
        }

        _canvas = new HudCanvas(batch, font, atlas);
    }

    public TextInputField Account { get; } = new() { MaxLength = 20, Focused = true };
    public TextInputField Password { get; } = new() { MaxLength = 20, Masked = true };
    public string Status { get; set; } = string.Empty;
    public bool InputEnabled { get; set; } = true;

    // typed: the characters the window typed since the last frame (TypedText), for the focused field.
    public LoginIntent Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, ReadOnlySpan<char> typed, double dtSeconds, Viewport viewport)
    {
        return Update(keyboard, previousKeyboard, Mouse.GetState(), typed, dtSeconds, viewport);
    }

    internal LoginIntent Update(in KeyboardState keyboard, in KeyboardState previousKeyboard, MouseState mouse, ReadOnlySpan<char> typed, double dtSeconds,
        Viewport viewport)
    {
        var intent = LoginIntent.None;
        if (InputEnabled)
        {
            var layout = LoginLayout.Create(viewport.Width, viewport.Height);
            var cursor = layout.Pointer(mouse.Position);
            var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (Pressed(keyboard, previousKeyboard, Keys.Tab))
            {
                var direction = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? -1 : 1;
                SetFocus((FocusTarget)(((int)_focus + direction + 3) % 3));
            }

            if (clicked)
            {
                if (layout.Account.Contains(cursor))
                {
                    SetFocus(FocusTarget.Account);
                }
                else if (layout.Password.Contains(cursor))
                {
                    SetFocus(FocusTarget.Password);
                }
                else if (layout.Login.Contains(cursor))
                {
                    SetFocus(FocusTarget.Login);
                    intent = LoginIntent.SubmitLogin;
                }
            }

            Account.Update(typed, dtSeconds);
            Password.Update(typed, dtSeconds);
            if (Pressed(keyboard, previousKeyboard, Keys.Enter)
                || _focus >= FocusTarget.Login && Pressed(keyboard, previousKeyboard, Keys.Space))
            {
                intent = LoginIntent.SubmitLogin;
            }
        }

        _previousMouse = mouse;
        return intent;
    }

    private void SetFocus(FocusTarget focus)
    {
        _focus = focus;
        Account.Focused = focus == FocusTarget.Account;
        Password.Focused = focus == FocusTarget.Password;
    }

    public void Draw(Viewport viewport)
    {
        var layout = LoginLayout.Create(viewport.Width, viewport.Height);
        var tint = InputEnabled ? Color.White : new Color(155, 155, 155);
        _canvas.Begin(layout.Scale);
        _canvas.Image("LoginPanel", layout.Panel);
        _canvas.Text("ETERVALE", layout.Title, pixels: 30f, centered: true);
        _canvas.Text(ClientText.Get("Login.Subtitle"), layout.Subtitle, HudCanvas.Muted, 17f, centered: true);
        DrawField(ClientText.Get("Login.Account"), Account, layout.Account, tint);
        DrawField(ClientText.Get("Login.Password"), Password, layout.Password, tint);
        DrawButton(ClientText.Get("Login.LogIn"), layout.Login, _focus == FocusTarget.Login, layout, tint);
        _canvas.Text(Status, layout.Status, HudCanvas.Gold, 16f, centered: true);
        _canvas.Text(ClientText.Get("Login.Hint"), layout.Hint, HudCanvas.Muted, 13f, centered: true);
        _canvas.End();
    }

    private void DrawField(string label, TextInputField field, Rectangle box, Color tint)
    {
        var focused = InputEnabled && field.Focused;
        _canvas.Text(label, new Rectangle(box.X, box.Y - 28, box.Width, 24), HudCanvas.Muted, 17f);
        _canvas.Frame(box, "Inset", 8, focused ? HudCanvas.Gold : tint);
        var text = field.DisplayText;
        if (focused && field.CaretVisible)
        {
            text += "_";
        }

        _canvas.Text(text, new Rectangle(box.X + 14, box.Y + 6, box.Width - 28, box.Height - 12), tint, 20f);
    }

    private void DrawButton(string label, Rectangle box, bool focused, LoginLayout layout, Color tint)
    {
        var hovered = box.Contains(layout.Pointer(_previousMouse.Position));
        var highlighted = InputEnabled && (focused || hovered);
        var pressed = InputEnabled && hovered && _previousMouse.LeftButton == ButtonState.Pressed;
        var color = highlighted ? new Color(255, 230, 180) : tint;
        if (pressed)
        {
            color *= 0.7f;
        }

        _canvas.Frame(box, "Button", 12, color);
        _canvas.Text(label, box, highlighted ? Color.White : HudCanvas.Gold, 20f, centered: true);
    }

    private static bool Pressed(in KeyboardState current, in KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }
}
