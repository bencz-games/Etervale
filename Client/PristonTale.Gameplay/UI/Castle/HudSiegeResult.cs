using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Castle;

// The end of a siege of Bless Castle for a player in the castle, in the NPC panel's box: the clan holding the castle is
// told its victory and what its chief may set, everyone else that the siege is over and that they go back to the start
// of the castle's field. OK or Escape closes it; another window in that box hides it and takes its clicks.
public sealed class HudSiegeResult
{
    private bool _won;
    private bool _open;
    private string[] _lines;

    public bool IsOpen => _open;

    public void Show(bool won)
    {
        (_won, _open) = (won, true);
        _lines = null;
    }

    public void Close() => _open = false;

    public bool Contains(Point pointer) => IsOpen && HudNpcPanel.Box.Contains(pointer);

    public bool Click(Point pointer)
    {
        if (!Contains(pointer))
        {
            return false;
        }

        if (HudNpcPanel.OkButton.Contains(pointer))
        {
            Close();
        }

        return true;
    }

    public void Draw(HudPainter painter, Point pointer)
    {
        if (!IsOpen)
        {
            return;
        }

        _lines ??= Lines(painter);
        HudPanelPage.Frame(painter, ClientText.Get("Siege.Title"));
        for (var line = 0; line < _lines.Length && line < HudPanelPage.Capacity(0); line++)
        {
            painter.Text(_lines[line], HudNpcPanel.TextLeft, HudNpcPanel.TextTop + HudPanelPage.LineStep * line, line < _headlineLines ? HudPanelPage.Chosen : Color.White);
        }

        HudPanelPage.Buttons(painter, [], pointer, pending: false);
    }

    private int _headlineLines;

    // The headline and the text, each wrapped to the panel, a blank line between them.
    private string[] Lines(HudPainter painter)
    {
        float Measure(string text) => painter.Measure(text);
        var headline = HudTextLayout.Wrap(ClientText.Get(_won ? "Siege.Won" : "Siege.Ended"), HudNpcPanel.TextWidth, Measure);
        _headlineLines = headline.Count;
        return [.. headline, "", .. HudTextLayout.Wrap(ClientText.Get(_won ? "Siege.WonText" : "Siege.EndedText"), HudNpcPanel.TextWidth, Measure)];
    }
}
