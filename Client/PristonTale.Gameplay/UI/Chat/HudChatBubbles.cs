using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.UI.Chat;

// Where a speaker's bubble hangs: the screen point above its head (in pixels) and its depth from the camera.
public readonly record struct HudOverheadPoint(Vector2 Screen, float Depth);

// Speech bubbles over characters. A line shows five seconds over another character and seven over the local player,
// only within 768 units of the camera; nearer bubbles draw over farther ones and the selected character's on top. The
// speaker's name, up to the first colon, takes the speaker's colour; the rest is white. A player selling from a personal
// shop shows its sign ("Name: sign") in the shop's frame while it says nothing.
public sealed class HudChatBubbles
{
    public const float MaxDepth = HudNameColors.OverheadDepth;
    public const int WrapPixels = 264;
    public const int MaxLines = 12;
    public const int MaxLineChars = 63;
    private const string Box = "StartImage/MessageBox/ChatBox/";
    public static readonly string[] Frame = Enumerable.Range(0, 10).Select(index => $"{Box}MessageBox00{index}.tga").ToArray();
    public static readonly string[] SelectedFrame = Enumerable.Range(0, 10).Select(index => $"{Box}MessageBoxa00{index}.tga").ToArray();
    // The shop sign's frame has no fill of its own: it takes the bubble's.
    public static readonly string[] ShopFrame = Enumerable.Range(0, 10).Select(index => $"{Box}BMessageBox00{index}.tga").ToArray();
    public static readonly string[] ShopSelectedFrame = Enumerable.Range(0, 10).Select(index => $"{Box}BMessageBoxA00{index}.tga").ToArray();
    public static readonly string[] Art =
        [.. Frame, .. SelectedFrame.Where((_, index) => index != 4), .. ShopFrame.Where((_, index) => index != 4), .. ShopSelectedFrame.Where((_, index) => index != 4)];
    private static readonly Color DefaultName = new(255, 220, 100);
    private static readonly string[][] Plain = [Frame];
    private static readonly string[][] PlainSelected = [Frame, SelectedFrame];
    private static readonly string[][] Shop = [ShopFrame];
    private static readonly string[][] ShopSelected = [ShopFrame, ShopSelectedFrame];
    private readonly Dictionary<ulong, (string Text, double UntilMs)> _lines = new();
    private readonly List<(ulong Id, HudOverheadPoint Point, bool Sign)> _visible = new();
    // Made once: the expired speakers' list, the sort (the selected speaker last, the rest back to front) and the measure.
    private readonly List<ulong> _expired = new();
    private Comparison<(ulong Id, HudOverheadPoint Point, bool Sign)> _backToFront;
    private ulong _selected;
    private Func<string, int> _measure;
    private readonly HudWrapCache _wrap = new(WrapPixels, MaxLineChars);

    // The frame is drawn unless the player switched frames off; a named line always keeps its frame and tail.
    public bool Framed { get; set; } = true;

    public int Count => _lines.Count;

    // The signs of the players selling from a personal shop, by player (RemoteEntityManager.ShopSigns, read only).
    public Dictionary<ulong, string> Signs { get; set; } = RemoteEntityManager.NoShopSigns;

    public bool TryGet(ulong entityId, out string text)
    {
        text = _lines.TryGetValue(entityId, out var line) ? line.Text : "";
        return text.Length > 0;
    }

    public void Say(ulong entityId, string text, double nowMs, int durationMs) => _lines[entityId] = (text, nowMs + durationMs);

    public void Forget(ulong entityId) => _lines.Remove(entityId);

    public void Draw(HudPainter painter, HudLayout layout, double nowMs, Func<ulong, HudOverheadPoint?> project, Func<ulong, Color> nameColor, ulong selected)
    {
        _visible.Clear();
        _measure ??= painter.Measure;
        _expired.Clear();
        foreach (var (id, line) in _lines)
        {
            if (line.UntilMs <= nowMs)
            {
                _expired.Add(id);
                continue;
            }

            if (project(id) is { } point && point.Depth < MaxDepth)
            {
                _visible.Add((id, point, false));
            }
        }

        foreach (var id in _expired)
        {
            _lines.Remove(id);
        }

        foreach (var (id, _) in Signs)
        {
            if (!_lines.ContainsKey(id) && project(id) is { } point && point.Depth < MaxDepth)
            {
                _visible.Add((id, point, true));
            }
        }

        _selected = selected;
        _visible.Sort(_backToFront ??= (left, right) => (left.Id == _selected) != (right.Id == _selected)
            ? (left.Id == _selected ? 1 : -1)
            : right.Point.Depth.CompareTo(left.Point.Depth));
        var scale = Math.Max(1, layout.Scale);
        foreach (var (id, point, sign) in _visible)
        {
            painter.Flush();
            var text = sign ? Signs[id] : _lines[id].Text;
            DrawBubble(painter, layout, text, (int)(point.Screen.X / scale), (int)(point.Screen.Y / scale), nameColor(id), id == selected, sign);
        }
    }

    // One line over a point that is no entity (the helper pet's speech), as an unselected speaker's.
    public void DrawAt(HudPainter painter, HudLayout layout, string text, HudOverheadPoint point)
    {
        var scale = Math.Max(1, layout.Scale);
        painter.Flush();
        DrawBubble(painter, layout, text, (int)(point.Screen.X / scale), (int)(point.Screen.Y / scale), default, false, false);
    }

    private void DrawBubble(HudPainter painter, HudLayout layout, string text, int anchorX, int anchorY, Color name, bool selected, bool sign)
    {
        var lines = _wrap.Wrap(text, MaxLines, _measure ??= painter.Measure);
        if (lines.Count == 0)
        {
            return;
        }

        var colon = text.IndexOf(':', 0, Math.Min(20, text.Length));
        var named = colon >= 0;
        var textWidth = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            textWidth = Math.Max(textWidth, painter.Measure(lines[index]));
        }

        var lineHeight = Math.Max(16, painter.TextHeight);
        var frameWidth = textWidth + 18;
        var frameHeight = 4 + lineHeight * lines.Count + 6 + 2;
        var x = Math.Clamp(anchorX - frameWidth / 2, 0, Math.Max(0, layout.Width - frameWidth));
        var y = Math.Clamp(anchorY - frameHeight - 12, 0, Math.Max(0, layout.Height - frameHeight));
        if (!Framed && !named)
        {
            x -= 2;
        }

        DrawFrame(painter, x, y, textWidth, lineHeight, lines.Count, named, selected, Framed || sign, sign);
        painter.Flush();
        for (var index = 0; index < lines.Count; index++)
        {
            painter.Text(lines[index], x + 9, y + 5 + index * lineHeight + 2, named ? Color.White : name == default ? Color.White : name, false);
        }

        if (named)
        {
            painter.Text(text[..(colon + 1)], x + 9, y + 5 + 2, name == default ? DefaultName : name, false);
        }
    }

    // The fill is one translucent tile stretched over the inside; the border is cut from the corner, edge and tail tiles
    // (the shop sign's own tiles for a sign).
    public static void DrawFrame(HudPainter painter, int x, int y, int width, int lineHeight, int lineCount, bool tail, bool selected, bool framed = true,
        bool shop = false)
    {
        var content = lineHeight * lineCount + 6;
        var bottom = y + 4 + content;
        painter.Stretch(Frame[4], new Rectangle(x + 4, y + 4, width + 10, content), new Rectangle(0, 0, 16, 16));
        if (!framed && !tail)
        {
            return;
        }

        var sets = shop ? selected ? ShopSelected : Shop : selected ? PlainSelected : Plain;
        foreach (var set in sets)
        {
            for (var offset = 0; offset < content; offset += 16)
            {
                var tile = Math.Min(16, content - offset);
                painter.Stretch(set[3], new Rectangle(x, y + 4 + offset, 4, tile), new Rectangle(0, 0, 4, tile));
                painter.Stretch(set[5], new Rectangle(x + 14 + width, y + 4 + offset, 4, tile), new Rectangle(12, 0, 4, tile));
            }

            var tailStart = tail && width >= 12 ? Math.Clamp(((width / 6 + 1) / 2 - 1) * 6, 0, width - 12) : -1;
            for (var offset = 0; offset < width; offset += 6)
            {
                var tile = Math.Min(6, width - offset);
                painter.Stretch(set[1], new Rectangle(x + 11 + offset, y, tile, 4), new Rectangle(0, 0, tile, 4));
                if (tailStart < 0 || offset < tailStart || offset >= tailStart + 12)
                {
                    painter.Stretch(set[7], new Rectangle(x + 11 + offset, bottom, tile, 2), new Rectangle(0, 4, tile, 2));
                }
            }

            if (tailStart >= 0)
            {
                painter.Stretch(set[9], new Rectangle(x + 11 + tailStart, bottom, 12, 10), new Rectangle(0, 4, 12, 10));
            }

            painter.Stretch(set[0], new Rectangle(x, y, 11, 4), new Rectangle(0, 0, 11, 4));
            painter.Stretch(set[2], new Rectangle(x + 11 + width, y, 7, 4), new Rectangle(9, 0, 7, 4));
            painter.Stretch(set[6], new Rectangle(x, bottom, 11, 2), new Rectangle(0, 4, 11, 2));
            painter.Stretch(set[8], new Rectangle(x + 11 + width, bottom, 7, 2), new Rectangle(9, 4, 7, 2));
        }
    }
}
