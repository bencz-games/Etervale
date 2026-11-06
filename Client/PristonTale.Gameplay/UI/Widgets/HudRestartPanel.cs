using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Widgets;

// The original restart window of a dead player: ReStartMain.bmp centred 100 pixels above the middle of the screen with
// the field, town and exit buttons (44 x 44) that light up under the pointer, and two lines of help below them. It opens
// a hundred death-motion frames after the fall and chooses the town by itself after twenty seconds. Where the field
// restart is closed the field button stays dark and its help reads red; in the castle field it shows nothing.
public sealed class HudRestartPanel(IClientLog log)
{
    private static readonly RestartChoice[] Choices = Enum.GetValues<RestartChoice>();
    // A hundred frames of the dead motion at the original's 70 frames a second.
    public const double OpenDelayMs = 100 * 1000d / GameUnits.TicksPerSecond;
    public const double TimeoutMs = 20_000;
    public const int Width = 188;
    public const int Height = 128;
    private static readonly Color Refused = new(255, 0, 0);
    private static readonly string[] Art = [HudArt.RestartField, HudArt.RestartTown, HudArt.RestartExit];
    private static readonly (string First, string Second)[] Help =
    [
        ("Restart.Field", "Restart.Penalty"),
        ("Restart.Town", ""),
        ("Restart.Quit", "")
    ];

    // The two lines of a choice's help, by their phrase keys (an empty key is an empty line).
    private static (string First, string Second) Words((string First, string Second) keys) =>
        (ClientText.Get(keys.First), keys.Second.Length == 0 ? "" : ClientText.Get(keys.Second));

    private double _deadSinceMs = -1;
    // When the window last opened; the town is chosen twenty seconds after, as the original counted while it showed.
    private double _openSinceMs;
    private readonly HudRequest<RestartOutcome> _request = new(log, "Restart failed");
    // The server stood the player up; the window stays shut until the presence shows it alive.
    private bool _restarted;

    // Sends the choice to the server; set by the screen.
    public Func<RestartChoice, Task<RestartOutcome>> Restart { get; set; }

    // The exit restart was stored: the client quits.
    public event Action ExitRequested;

    // The window shows: the player is dead, the death motion ran long enough and no choice is in flight.
    public bool Open { get; private set; }

    public static Point Origin(HudLayout layout) => new(layout.Width / 2 - 187 / 2, layout.Height / 2 - 127 / 2 - 100);

    public static Rectangle Box(HudLayout layout) => new(Origin(layout), new Point(Width, Height));

    public static Rectangle Button(HudLayout layout, RestartChoice choice)
    {
        var origin = Origin(layout);
        return new Rectangle(origin.X + 21 + 51 * ((int)choice - 1), origin.Y + 22, 44, 44);
    }

    public void Update(bool dead, double nowMs)
    {
        Collect();
        if (!dead)
        {
            _deadSinceMs = -1;
            _restarted = false;
            Open = false;
            return;
        }

        if (_deadSinceMs < 0)
        {
            _deadSinceMs = nowMs;
        }

        var open = !_request.Pending && !_restarted && nowMs - _deadSinceMs >= OpenDelayMs;
        if (open && !Open)
        {
            _openSinceMs = nowMs;
        }

        Open = open;
        if (Open && nowMs - _openSinceMs >= TimeoutMs)
        {
            Choose(RestartChoice.Town);
        }
    }

    // True when the click landed in the window (it is consumed even off the buttons).
    public bool Click(HudLayout layout, Point pointer, bool fieldOpen)
    {
        if (!Open || !Box(layout).Contains(pointer))
        {
            return false;
        }

        foreach (var choice in Choices)
        {
            if (Button(layout, choice).Contains(pointer) && (choice != RestartChoice.Field || fieldOpen))
            {
                Choose(choice);
            }
        }

        return true;
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer, bool fieldOpen, bool castle)
    {
        if (!Open)
        {
            return;
        }

        var origin = Origin(layout);
        painter.Sprite(HudArt.RestartMain, origin.X, origin.Y);
        foreach (var choice in Choices)
        {
            var button = Button(layout, choice);
            if (!button.Contains(pointer) || choice == RestartChoice.Field && castle)
            {
                continue;
            }

            var refused = choice == RestartChoice.Field && !fieldOpen;
            if (!refused)
            {
                painter.Sprite(Art[(int)choice - 1], button.X, button.Y);
            }

            var (first, second) = refused ? (ClientText.Get("Restart.NoExperience"), ClientText.Get("Restart.NoExperience2")) : Words(Help[(int)choice - 1]);
            var color = refused ? Refused : Color.White;
            painter.Text(first, origin.X + 23, origin.Y + 75, color);
            if (second.Length > 0)
            {
                painter.Text(second, origin.X + 23, origin.Y + 91, color);
            }
        }
    }

    private void Choose(RestartChoice choice)
    {
        if (_request.Pending || Restart is null)
        {
            return;
        }

        _request.Start(() => Restart(choice), _ => new RestartOutcome(RestartStatus.Rejected, choice));
        Open = false;
    }

    // The answer of the choice in flight, read on the game thread.
    private void Collect()
    {
        if (!_request.TryTake(out var outcome))
        {
            return;
        }

        _restarted = outcome.Ok;
        if (outcome.Ok && outcome.Choice == RestartChoice.Exit)
        {
            ExitRequested?.Invoke();
        }

        // Busy, refused or not stored: the window opens again for another choice while the player is still down.
    }
}
