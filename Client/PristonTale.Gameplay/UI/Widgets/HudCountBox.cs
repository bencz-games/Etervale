using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The original's count box, the quantity question (items to drop, goods to sell or buy, money to move): two lines and a
// number typed with the digit keys (Backspace erases) or stepped with the arrows. A number typed above the maximum shows
// the maximum at once (the arrows stay within it too). OK or Enter accepts, Cancel or Escape (GameplayHud.ClosePanel)
// closes. It starts at zero; accepting zero does nothing unless the question takes it (the gold a trade offers).
public sealed class HudCountBox
{
    private const int MaxDigits = 9;
    private string _first = "";
    private string _second = "";
    private int _maximum;
    private Action<int> _accepted;
    private bool _zeroAccepted;
    private string _text = "0";

    public static Rectangle Bounds => HudMessageBox.Bounds;
    public static Rectangle Field => new(Bounds.X + 34, Bounds.Y + 62, 109, 25);
    public static Rectangle Up => new(Bounds.X + 124, Bounds.Y + 62, 19, 13);
    public static Rectangle Down => new(Bounds.X + 124, Bounds.Y + 75, 19, 13);
    public static Rectangle Ok => HudMessageBox.Accept;
    public static Rectangle Cancel => HudMessageBox.Reject;

    public bool IsOpen { get; private set; }
    public int Count { get; private set; }

    // maximum: as much as the question allows (gold may pass what the box can count; it stops there).
    public void Open(string first, string second, long maximum, Action<int> accepted, bool zeroAccepted = false)
    {
        (_first, _second, _maximum, _accepted, _zeroAccepted) = (first, second, (int)Math.Clamp(maximum, 0, int.MaxValue), accepted, zeroAccepted);
        Show(0);
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _accepted = null;
    }

    // Typed characters and the keys pressed this frame (pressed: true only on the frame a key goes down).
    public void Type(ReadOnlySpan<char> typed, Func<Keys, bool> pressed)
    {
        foreach (var character in typed)
        {
            if (char.IsAsciiDigit(character) && _text.Length < MaxDigits)
            {
                Show((long)Count * 10 + (character - '0'));
            }
            else if (character == '\b')
            {
                Show(Count / 10);
            }
        }

        if (pressed(Keys.Enter))
        {
            Accept();
        }
    }

    public void Click(Point pointer)
    {
        if (Up.Contains(pointer))
        {
            Show(Count + 1);
        }
        else if (Down.Contains(pointer))
        {
            Show(Count - 1);
        }
        else if (Ok.Contains(pointer))
        {
            Accept();
        }
        else if (Cancel.Contains(pointer))
        {
            Close();
        }
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        var bounds = Bounds;
        painter.Sprite(HudArt.MessageBox, bounds.X, bounds.Y);
        painter.TextCentered(painter.Fit(_first, HudMessageBox.VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 20, HudTextColors.BoxTitle);
        painter.TextCentered(painter.Fit(_second, HudMessageBox.VisibleWidth - 16), bounds.Left, bounds.Right, bounds.Y + 40);
        painter.Sprite(HudArt.CountField, Field.X, Field.Y);
        painter.Text(_text, Field.X + 8, Field.Y + 6);
        if (Up.Contains(pointer))
        {
            painter.Sprite(HudArt.CountUp, Up.X, Up.Y);
        }
        else if (Down.Contains(pointer))
        {
            painter.Sprite(HudArt.CountDown, Down.X, Down.Y);
        }

        painter.Sprite(Ok.Contains(pointer) ? HudArt.OkYellow : HudArt.OkGray, Ok.X, Ok.Y);
        painter.Sprite(Cancel.Contains(pointer) ? HudArt.CancelYellow : HudArt.CancelGray, Cancel.X, Cancel.Y);
    }

    private void Show(long count)
    {
        Count = (int)Math.Clamp(count, 0, _maximum);
        _text = Count.ToString();
    }

    private void Accept()
    {
        var accepted = _accepted;
        var count = Count;
        var zeroAccepted = _zeroAccepted;
        Close();
        if (count > 0 || zeroAccepted)
        {
            accepted?.Invoke(count);
        }
    }
}
