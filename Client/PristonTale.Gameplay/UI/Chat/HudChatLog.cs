using Microsoft.Xna.Framework;
using PristonTale.Contracts.Social;

namespace PristonTale.Gameplay.UI.Chat;

// The chat tabs, in their button order: All, Clan, Party, Trade, Whisper, and the assistant's page the notice toggle shows.
public enum HudChatTab
{
    All = 0,
    Clan = 1,
    Party = 2,
    Trade = 3,
    Whisper = 4,
    Assist = 5
}

public readonly record struct HudChatEntry(string Text, ChatKind Kind);

// The chat history the window shows: one ring of 64 lines per tab. A line lands in the tab of its kind; clan, party,
// trade and whisper lines also land in All, and server, system and notice lines also land in those four tabs. The
// scroll position counts lines back from the newest and moves with new lines while scrolled.
public sealed class HudChatLog
{
    public const int Capacity = 64;

    // The scroll position stops this many lines before the oldest kept line.
    public const int ScrollLimit = Capacity - 32;

    private static readonly Color ServerColor = new(255, 128, 96);
    private static readonly Color WhisperColor = new(150, 190, 255);
    private static readonly Color NoticeColor = new(230, 160, 255);
    private static readonly Color PartyColor = new(200, 255, 0);
    private static readonly Color ClanColor = new(255, 255, 80);
    private static readonly Color TradeColor = new(200, 255, 255);
    private static readonly Color AssistColor = new(200, 128, 128);
    private static readonly Color DefaultColor = new(255, 255, 192);

    private readonly HudChatEntry[][] _tabs = Enumerable.Range(0, 6).Select(_ => new HudChatEntry[Capacity]).ToArray();
    private readonly int[] _counts = new int[6];

    public int Scroll { get; private set; }

    public HudChatTab Tab { get; private set; }

    // Lines added since the start; a change tells the window to show the newest line.
    public int Added { get; private set; }

    public static Color ColorOf(ChatKind kind) => kind switch
    {
        ChatKind.Server => ServerColor,
        ChatKind.Whisper or ChatKind.System => WhisperColor,
        ChatKind.Notice => NoticeColor,
        ChatKind.Party => PartyColor,
        ChatKind.Clan => ClanColor,
        ChatKind.Trade => TradeColor,
        ChatKind.Assist => AssistColor,
        _ => DefaultColor
    };

    public static HudChatTab TabOf(ChatKind kind) => kind switch
    {
        ChatKind.Clan => HudChatTab.Clan,
        ChatKind.Party => HudChatTab.Party,
        ChatKind.Trade => HudChatTab.Trade,
        ChatKind.Whisper => HudChatTab.Whisper,
        ChatKind.Assist => HudChatTab.Assist,
        _ => HudChatTab.All
    };

    public void Add(string text, ChatKind kind)
    {
        var entry = new HudChatEntry(text, kind);
        var tab = TabOf(kind);
        Push(tab, entry);
        if (tab is >= HudChatTab.Clan and <= HudChatTab.Whisper)
        {
            Push(HudChatTab.All, entry);
        }

        // Server, system and notice lines show in every tab; spoken lines only in All.
        if (tab == HudChatTab.All && kind != ChatKind.Player)
        {
            for (var shared = HudChatTab.Clan; shared <= HudChatTab.Whisper; shared++)
            {
                Push(shared, entry);
            }
        }

        if (Scroll != 0)
        {
            Scroll = Math.Min(ScrollLimit, Scroll + 1);
        }

        Added++;
    }

    public void SelectTab(HudChatTab tab)
    {
        Tab = tab;
        Scroll = 0;
    }

    public void ScrollBy(int lines) => Scroll = Math.Clamp(Scroll + lines, 0, ScrollLimit);

    public void ResetScroll() => Scroll = 0;

    public int Count(HudChatTab tab) => Math.Min(Capacity, _counts[(int)tab]);

    // The lines of a tab from the newest backwards, starting the given number of lines back.
    // How many entries the tab holds past the newest skip, and the nth of them from the newest: read every frame without
    // an iterator.
    public int NewestCount(HudChatTab tab, int skip) => Math.Max(0, Math.Min(_counts[(int)tab], Capacity) - skip);

    public HudChatEntry NewestAt(HudChatTab tab, int skip, int n) => _tabs[(int)tab][(_counts[(int)tab] - 1 - skip - n) % Capacity];

    public IEnumerable<HudChatEntry> Newest(HudChatTab tab, int skip)
    {
        var count = _counts[(int)tab];
        for (var index = count - 1 - skip; index >= Math.Max(0, count - Capacity); index--)
        {
            yield return _tabs[(int)tab][index % Capacity];
        }
    }

    private void Push(HudChatTab tab, HudChatEntry entry)
    {
        var index = (int)tab;
        _tabs[index][_counts[index] % Capacity] = entry;
        _counts[index]++;
    }
}
