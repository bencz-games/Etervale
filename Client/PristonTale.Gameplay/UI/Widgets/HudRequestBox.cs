using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.UI.Widgets;

// A request box's art and places, relative to the box: OK and cancel draw their idle image (null: the box's own art shows
// it) or, under the pointer, the hovered one; who asks and the request line. BlinkOver: the blinking square draws over the
// art (the party's box) rather than under it (the trade's).
public sealed record HudRequestBoxArt(string Box, string OkIdle, string OkHovered, Rectangle Ok, string CancelIdle, string CancelHovered, Rectangle Cancel,
    Point Name, Point Line, bool BlinkOver)
{
    public string[] All => new[] { Box, OkIdle, OkHovered, CancelIdle, CancelHovered }.Where(static path => path is not null).ToArray();
}

// The original's request box at (513, 3) a party or trade request opens: its art, a blue square blinking with it (shown
// frames 16..30 of every 31 at 70 per second), who asks and the request line. It answers once from OK or cancel and closes
// with its answer or after its time.
public sealed class HudRequestBox(HudRequestBoxArt art, long lifeMs)
{
    private const int FramesPerSecond = GameUnits.TicksPerSecond;
    private static readonly Point Origin = new(513, 3);
    private static readonly Color Blink = Color.FromNonPremultiplied(0, 200, 255, 80);
    private double _openedMs;
    private string _line;

    public Rectangle Ok => Offset(art.Ok);
    public Rectangle Cancel => Offset(art.Cancel);
    public bool IsOpen => FromName is not null;
    public ulong FromId { get; private set; }
    public string FromName { get; private set; }

    public void Show(ulong fromId, string fromName, string line, double nowMs)
    {
        (FromId, FromName, _line, _openedMs) = (fromId, fromName, line, nowMs);
    }

    public void Close() => FromName = null;

    public void Update(double nowMs)
    {
        if (IsOpen && nowMs - _openedMs >= lifeMs)
        {
            Close();
        }
    }

    public bool Captures(Point pointer) => IsOpen && (Ok.Contains(pointer) || Cancel.Contains(pointer));

    // The answer a click gives (true accept, false refuse), or null when it missed both buttons.
    public bool? Click(Point pointer)
    {
        if (!IsOpen)
        {
            return null;
        }

        var answer = Ok.Contains(pointer) ? true : Cancel.Contains(pointer) ? false : (bool?)null;
        if (answer is not null)
        {
            Close();
        }

        return answer;
    }

    public void Draw(HudPainter painter, Point pointer, double nowMs)
    {
        if (!IsOpen)
        {
            return;
        }

        var blink = (long)((nowMs - _openedMs) * FramesPerSecond / 1000) % 31 > 15;
        if (blink && !art.BlinkOver)
        {
            painter.Fill(new Rectangle(Origin.X + 5, Origin.Y + 5, 160, 53), Blink);
        }

        painter.Sprite(art.Box, Origin.X, Origin.Y);
        if (blink && art.BlinkOver)
        {
            painter.Fill(new Rectangle(Origin.X + 5, Origin.Y + 5, 160, 53), Blink);
        }

        painter.Text(FromName, Origin.X + art.Name.X, Origin.Y + art.Name.Y);
        if (_line is not null)
        {
            painter.Text(_line, Origin.X + art.Line.X, Origin.Y + art.Line.Y);
        }

        DrawButton(painter, Ok, Ok.Contains(pointer) ? art.OkHovered : art.OkIdle);
        DrawButton(painter, Cancel, Cancel.Contains(pointer) ? art.CancelHovered : art.CancelIdle);
    }

    private static void DrawButton(HudPainter painter, Rectangle bounds, string image)
    {
        if (image is not null)
        {
            painter.Sprite(image, bounds.X, bounds.Y);
        }
    }

    private static Rectangle Offset(Rectangle bounds) => new(Origin.X + bounds.X, Origin.Y + bounds.Y, bounds.Width, bounds.Height);
}
