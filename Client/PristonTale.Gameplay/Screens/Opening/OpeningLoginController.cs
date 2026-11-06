using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class OpeningLoginController : IDisposable
{
    private enum Phase { Login, Authenticating }

    private readonly ScreenContext _context;
    private readonly LoginAudio _audio;
    private readonly LoginPanel _login;
    private Phase _phase = Phase.Login;
    private Task<LoginOutcome> _loginTask;
    private CancellationTokenSource _loginCts;

    public OpeningLoginController(ScreenContext context, LoginAudio audio)
    {
        _context = context;
        _audio = audio;
        _login = new LoginPanel(context.SpriteBatch, context.Font, context.UiAtlas);
    }

    public enum LoginResult { None, Succeeded }

    public LoginResult Update(in KeyboardState keyboard, in KeyboardState lastKeyboard, float dt)
    {
        var typed = _context.TypedText.Drain();
        if (_phase == Phase.Login && _login.Update(keyboard, lastKeyboard, typed, dt, _context.GraphicsDevice.Viewport) == LoginIntent.SubmitLogin)
        {
            BeginLogin();
        }

        return _phase == Phase.Authenticating ? PollLogin() : LoginResult.None;
    }

    private void BeginLogin()
    {
        if (string.IsNullOrWhiteSpace(_login.Account.Text) || string.IsNullOrEmpty(_login.Password.Text))
        {
            _login.Status = "Enter your account and password.";
            return;
        }

        _audio.Play(LoginCue.Login);
        _login.Status = "Connecting…";
        _login.InputEnabled = false;
        _phase = Phase.Authenticating;
        _loginCts = new CancellationTokenSource();
        if (_context.Backend.RequiresNewConnection)
        {
            _context.RenewBackend();
        }

        _loginTask = _context.Backend.LoginAsync(_login.Account.Text, _login.Password.Text,
            _context.Presentation.Requested.Language, _loginCts.Token);
    }

    private LoginResult PollLogin()
    {
        if (_loginTask is not { IsCompleted: true })
        {
            return LoginResult.None;
        }

        var succeeded = _loginTask.IsCompletedSuccessfully && _loginTask.Result.Ok;
        if (!succeeded)
        {
            _login.Status = _loginTask.IsCompletedSuccessfully
                ? ClientText.Get("Login.Failure." + _loginTask.Result.Failure)
                : ClientText.Get("Login.Unreachable");
            _login.InputEnabled = true;
            _phase = Phase.Login;
        }

        _loginTask = null;
        _loginCts?.Dispose();
        _loginCts = null;
        return succeeded ? LoginResult.Succeeded : LoginResult.None;
    }

    public void Draw(Viewport viewport) => _login.Draw(viewport);

    public void Dispose()
    {
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        if (_loginTask is not null)
        {
            _ = ObserveLoginAsync(_loginTask);
        }
    }

    private async Task ObserveLoginAsync(Task<LoginOutcome> login)
    {
        try
        {
            await login;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Error($"[Login] canceled request failed: {ex}");
        }
    }
}
