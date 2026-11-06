using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.PersonalShops;

// The original's shop information box: the sign a shop hangs over its seller, typed under the question (up to the sign's
// bytes, only characters the HUD font draws, the end of a long sign in view). OK or Enter opens the shop with it; Cancel
// or Escape (GameplayHud.ClosePanel) closes the box.
public sealed class HudSignBox
{
    private static string Question => ClientText.Get("PersonalShop.SignTitle");
    private readonly StringBuilder _text = new();
    private Action<string> _accepted;
    private string _shown = "_";
    private int _bytes;

    public bool IsOpen { get; private set; }

    public void Open(Action<string> accepted)
    {
        _text.Clear();
        _bytes = 0;
        _shown = "_";
        _accepted = accepted;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _accepted = null;
    }

    // Typed characters and the keys pressed this frame; canDraw tells the characters the HUD font has, measure their width.
    public void Type(ReadOnlySpan<char> typed, Func<Keys, bool> pressed, Func<char, bool> canDraw, Func<string, int> measure)
    {
        var changed = false;
        foreach (var character in typed)
        {
            if (character == '\b')
            {
                if (_text.Length > 0)
                {
                    _bytes -= Encoding.UTF8.GetByteCount([_text[^1]]);
                    _text.Remove(_text.Length - 1, 1);
                    changed = true;
                }
            }
            else if (!char.IsControl(character) && canDraw(character) && _bytes + Encoding.UTF8.GetByteCount([character]) <= PersonalShopLimits.MaxSignBytes)
            {
                _bytes += Encoding.UTF8.GetByteCount([character]);
                _text.Append(character);
                changed = true;
            }
        }

        if (changed)
        {
            Show(measure);
        }

        if (pressed(Keys.Enter))
        {
            Accept();
        }
    }

    public void Click(Point pointer)
    {
        if (HudMessageBox.Accept.Contains(pointer))
        {
            Accept();
        }
        else if (HudMessageBox.Reject.Contains(pointer))
        {
            Close();
        }
    }

    public void Draw(HudPainter painter, Point pointer) => HudMessageBox.Draw(painter, Question, _shown, pointer);

    // The end of the sign that fits the box, with the caret after it.
    private void Show(Func<string, int> measure)
    {
        var text = _text.ToString() + "_";
        var start = 0;
        while (start < text.Length - 1 && measure(text[start..]) > HudMessageBox.VisibleWidth - 16)
        {
            start++;
        }

        _shown = text[start..];
    }

    private void Accept()
    {
        var accepted = _accepted;
        var sign = _text.ToString().Trim();
        if (sign.Length == 0)
        {
            return;
        }

        Close();
        accepted?.Invoke(sign);
    }
}
