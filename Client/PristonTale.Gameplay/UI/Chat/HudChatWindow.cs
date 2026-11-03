using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.Social;

namespace PristonTale.Gameplay.UI.Chat;

// The chat window in the bottom-left corner, in 800x600 design units with the bottom offset added. Enter opens it with
// the input line focused; with a status, skill or inventory window open it shrinks to a small box above them. Its tab
// buttons filter the log (All, Clan, Party, Trade, Whisper); the notice button cycles the assistant's page: off, three
// assistant lines over eight chat lines, or the assistant's page alone.
public sealed class HudChatWindow
{
    // The most messages a frame walks back through the log, and the painter's delegates and the input's visible tail,
    // made once instead of every frame.
    private const int MostMessages = 32;
    private Func<char, bool> _canDraw;
    private Func<string, int> _measure;
    private string _inputText;
    private string _inputShown = "";

    private const string Chat = "image/Sinimage/Inter/Chat/";
    public const string Default = Chat + "ChatDefault-1.tga";
    public const string Small = Chat + "ChatSmall-1.tga";
    public const string InputLeft = Chat + "ChatDoc-1.tga";
    public const string InputRight = Chat + "ChatDoc-2.tga";
    public const string TabBar = Chat + "Chatting06.tga";
    public const string DownButton = Chat + "ChatDownButton.bmp";
    public const string AssistLine = Chat + "n_line.bmp";
    public static readonly string[] Tabs = Enumerable.Range(1, 5).Select(index => $"{Chat}ChatTap_0{index}.bmp").ToArray();
    public static readonly string[] NoticeButtons = [Chat + "ChatTap_N11.bmp", Chat + "ChatTap_N12.bmp", Chat + "ChatTap_N22.bmp"];
    public static readonly string[] Art = [Default, Small, InputLeft, InputRight, TabBar, DownButton, AssistLine, .. Tabs, .. NoticeButtons];

    // A side window lifts the input line and the log by this much.
    public const int SideLift = 170;

    // Lines wrap at 44 six-pixel characters.
    public const int WrapPixels = 44 * 6;
    public const int MessageX = 16;

    // The typed line starts after the 22-pixel chat button drawn at x 7.
    public const int InputX = 32;
    public const int InputPixels = 266;
    private static readonly Color InputColor = Color.White;
    private readonly List<(Rectangle Bounds, HudChatEntry Entry)> _drawnLines = new();
    private readonly HudWrapCache _wrap = new(WrapPixels, 255);

    public HudChatLog Log { get; } = new();
    public HudChatInput Input { get; } = new();

    // The window shows (the classic chat flag) and the input line has the keyboard.
    public bool Open { get; private set; }
    public bool Focused { get; private set; }

    // 0 chat only, 1 assistant lines over the chat, 2 the assistant's page.
    public int NoticeMode { get; private set; }

    // Messages drawn in the last frame; Page Up and Page Down scroll by this many.
    public int ShownMessages { get; private set; }

    public void OpenInput()
    {
        Open = true;
        Focused = true;
    }

    // The input line takes the keyboard holding "/:name " (a whisper line clicked, a party member double-clicked).
    public void WhisperTo(string name)
    {
        Input.WhisperTo(name);
        OpenInput();
    }

    // A whisper (and Enter) opens the window without taking the keyboard.
    public void Reveal() => Open = true;

    public void Unfocus()
    {
        Focused = false;
        Log.ResetScroll();
    }

    public void Close()
    {
        Open = false;
        Focused = false;
        Input.Clear();
        Log.ResetScroll();
    }

    public static Rectangle TabButton(HudLayout layout, int tab) => new(100 + tab * 40, 563 + layout.BottomOffset, 40, 16);

    public static Rectangle NoticeButton(HudLayout layout) => new(0, 563 + layout.BottomOffset, 45, 16);

    public static Rectangle Toggle(HudLayout layout, bool side) => new(7, 579 - Lift(side) + layout.BottomOffset, 15, 19);

    public static Rectangle Box(HudLayout layout, bool side) =>
        side ? new Rectangle(0, 294 + layout.BottomOffset, 300, 116) : new Rectangle(0, 408 + layout.BottomOffset, 305, 192);

    public bool Contains(HudLayout layout, Point pointer, bool side) =>
        Open && (Box(layout, side).Contains(pointer) || InputBox(layout, side).Contains(pointer)) || Toggle(layout, side).Contains(pointer);

    // Tabs, the notice button, the toggle and a whisper line; true when the click belonged to the chat.
    public bool Click(HudLayout layout, Point pointer, bool side)
    {
        if (Toggle(layout, side).Contains(pointer))
        {
            Open = !Open;
            if (!Open)
            {
                Focused = false;
            }

            return true;
        }

        if (!Open)
        {
            return false;
        }

        if (!side)
        {
            for (var tab = 0; tab < Tabs.Length; tab++)
            {
                if (TabButton(layout, tab).Contains(pointer))
                {
                    Log.SelectTab((HudChatTab)tab);
                    return true;
                }
            }

            if (NoticeButton(layout).Contains(pointer))
            {
                NoticeMode = (NoticeMode + 1) % 3;
                return true;
            }
        }

        foreach (var (bounds, entry) in _drawnLines)
        {
            if (bounds.Contains(pointer) && WhisperPartner(entry) is { } partner)
            {
                WhisperTo(partner);
                return true;
            }
        }

        return Box(layout, side).Contains(pointer) || InputBox(layout, side).Contains(pointer);
    }

    // The other side of a whisper line ("From> name: ..." or "To> name: ...").
    public static string WhisperPartner(HudChatEntry entry) => WhisperPartnerOf(entry.Kind, entry.Text);

    public static string WhisperPartnerOf(ChatKind kind, string text)
    {
        if (kind != ChatKind.Whisper)
        {
            return null;
        }

        var start = text.StartsWith("From> ", StringComparison.Ordinal) ? 6 : text.StartsWith("To> ", StringComparison.Ordinal) ? 4 : -1;
        var colon = start < 0 ? -1 : text.IndexOf(':', start);
        return colon > start ? text[start..colon].Trim() : null;
    }

    public void Draw(HudPainter painter, HudLayout layout, bool side)
    {
        Input.CanDraw = _canDraw ??= painter.CanDraw;
        _measure ??= painter.Measure;
        _drawnLines.Clear();
        ShownMessages = 0;
        if (!Open)
        {
            return;
        }

        var bottom = layout.BottomOffset;
        if (side)
        {
            painter.Stretch(Small, new Rectangle(0, 294 + bottom, 300, 116));
        }
        else
        {
            painter.Stretch(Default, new Rectangle(0, 600 - 192 + bottom, 305, 210));
            painter.Stretch(TabBar, new Rectangle(100, 564 + bottom, 256, 16));
            var tab = (int)Log.Tab;
            painter.Sprite(Tabs[Math.Clamp(tab, 0, Tabs.Length - 1)], 100 + Math.Clamp(tab, 0, Tabs.Length - 1) * 40, 564 + bottom);
            painter.Sprite(NoticeButtons[NoticeMode], 4, 564 + bottom);
            if (NoticeMode == 1)
            {
                painter.Sprite(AssistLine, 10, 460 + bottom);
            }
        }

        var lift = Lift(side);
        if (side)
        {
            painter.Stretch(InputLeft, new Rectangle(4, 576 - lift + bottom, 242, 32));
            painter.Stretch(InputRight, new Rectangle(246, 576 - lift + bottom, 164, 32));
        }
        else
        {
            painter.Stretch(InputLeft, new Rectangle(4, 576 + bottom, 296, 32));
        }

        if (Focused)
        {
            painter.Sprite(DownButton, 7, 580 - lift + bottom);
            // The input's visible tail is cut again only when the text changes.
            var text = Input.Text;
            if (!ReferenceEquals(text, _inputText))
            {
                _inputText = text;
                var start = 0;
                while (start < text.Length && painter.Measure(text[start..]) > InputPixels)
                {
                    start++;
                }

                _inputShown = start == 0 ? text : text[start..];
            }

            var visible = _inputShown;

            painter.Text(visible, InputX, 584 - lift + bottom - (Math.Max(12, HudPainter.LineHeight) - 12) / 2, InputColor);
        }

        painter.Flush();
        DrawMessages(painter, layout, side);
    }

    private void DrawMessages(HudPainter painter, HudLayout layout, bool side)
    {
        var bottom = layout.BottomOffset;
        if (side)
        {
            DrawLines(painter, NoticeMode == 2 ? HudChatTab.Assist : Log.Tab, 570 - SideLift - 5 + bottom, 8, NoticeMode == 2 ? 0 : Log.Scroll);
            return;
        }

        switch (NoticeMode)
        {
            case 1:
                DrawLines(painter, HudChatTab.Assist, 570 - 125 + bottom, 3, 0);
                DrawLines(painter, Log.Tab, 570 - 20 + bottom, 8, Log.Scroll);
                break;
            case 2:
                DrawLines(painter, HudChatTab.Assist, 570 - 20 + bottom, 12, Log.Scroll);
                break;
            default:
                DrawLines(painter, Log.Tab, 570 - 20 + bottom, 12, Log.Scroll);
                break;
        }
    }

    // The newest message sits at y; older messages go up line by line, each wrapped and drawn from its last line upward.
    private void DrawLines(HudPainter painter, HudChatTab tab, int y, int maxLines, int scroll)
    {
        var lineHeight = Math.Max(12, HudPainter.LineHeight);
        // The classic lines are 12 pixels high; a taller line keeps the newest line's bottom where the classic one ended
        // and draws fewer lines so each block stays inside its classic area.
        y -= lineHeight - 12;
        maxLines = Math.Max(1, maxLines * 12 / lineHeight);
        var drawn = 0;
        var messages = 0;
        var available = Math.Min(Log.NewestCount(tab, scroll), MostMessages);
        for (var n = 0; n < available; n++)
        {
            var entry = Log.NewestAt(tab, scroll, n);
            if (drawn >= maxLines)
            {
                break;
            }

            var lines = _wrap.Wrap(entry.Text, maxLines, _measure);
            messages++;
            var color = HudChatLog.ColorOf(entry.Kind);
            for (var index = lines.Count - 1; index >= 0 && drawn < maxLines; index--)
            {
                painter.Text(lines[index], MessageX, y, color, true, new Point(-1, -1));
                _drawnLines.Add((new Rectangle(MessageX, y, WrapPixels, lineHeight), entry));
                y -= lineHeight;
                drawn++;
            }
        }

        ShownMessages = Math.Max(ShownMessages, messages);
    }

    private static int Lift(bool side) => side ? SideLift : 0;

    private static Rectangle InputBox(HudLayout layout, bool side) =>
        side ? new Rectangle(4, 576 - SideLift + layout.BottomOffset, 406, 32) : new Rectangle(4, 576 + layout.BottomOffset, 296, 24);
}
