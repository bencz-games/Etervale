using System.Text;

namespace PristonTale.Gameplay.UI;

public sealed class TextInputField
{
    private readonly StringBuilder _text = new();
    private double _caretSeconds;

    public bool Focused { get; set; }
    public bool Masked { get; init; }
    public int MaxLength { get; init; } = 20;

    public string Text => _text.ToString();
    public string DisplayText => Masked ? new string('*', _text.Length) : _text.ToString();
    public bool CaretVisible => Focused && _caretSeconds % 1.0 < 0.5;

    public void Clear() => _text.Clear();

    // The characters the window typed since the last frame (TypedText): the system's own key repeat makes a held
    // Backspace ('\b') or letter repeat, as in any text box. Letters, digits, '.', '-' and '_' are taken.
    public void Update(ReadOnlySpan<char> typed, double dtSeconds)
    {
        _caretSeconds += dtSeconds;
        if (!Focused)
            return;

        foreach (var ch in typed)
        {
            if (ch == '\b')
            {
                if (_text.Length > 0)
                    _text.Remove(_text.Length - 1, 1);
            }
            else if (_text.Length < MaxLength && Accepts(ch))
            {
                _text.Append(ch);
            }
        }
    }

    private static bool Accepts(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_';
}
