using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Tests;

internal static class ChatClientTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("typed and received chat lines carry UTF-8 text and 64-bit speakers", WireAsync);
        yield return ("the chat log files lines into the five tabs and the assistant page with their colours", LogAsync);
        yield return ("the chat input composes per tab, keeps a command history and follows the flood rules", InputAsync);
        yield return ("received lines become bubbles over characters in view, other lines go to the log, whispers open the window", ReceiveAsync);
        yield return ("Enter opens and sends, Page keys scroll, the tabs and notice button switch the log", WindowAsync);
    }

    private static Task WireAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var say = new ChatSayPacket(false, "/:Bob olá ✓");
        var size = say.Write(buffer);
        Assert.True(size == 8 + 4 + 2 + System.Text.Encoding.UTF8.GetByteCount(say.Text) && BitConverter.ToInt32(buffer, 0) == size,
            "a typed line must be {header, version, channel, UTF-8 text}");
        Assert.True(ChatSayPacket.Read(buffer.AsSpan(0, size)) == say, "typed line did not read back");
        var party = ChatSayPacket.Read(buffer.AsSpan(0, new ChatSayPacket(true, "pull").Write(buffer)));
        Assert.True(party.Party && party.Text == "pull", "party lines lost their channel");
        size = say.Write(buffer);
        buffer[10] = 2;
        Assert.Throws<InvalidDataException>(() => ChatSayPacket.Read(buffer.AsSpan(0, size).ToArray()), "a malformed ChatSayPacket was read");
        var cut = new ChatSayPacket(false, new string('é', ChatWire.MaxTextBytes));
        Assert.True(ChatSayPacket.Read(buffer.AsSpan(0, cut.Write(buffer))).Text.Length == ChatWire.MaxTextBytes / 2,
            "a long line must be cut at a whole character");

        var decoder = new PresenceDecoder(new WorldRenderCatalog(), static _ => { });
        var line = new ChatLinePacket(ChatKind.Whisper, 0, "From> Ann: hi");
        Assert.True(decoder.TryHandle(buffer.AsSpan(0, line.Write(buffer)), out var update)
            && update!.Chat.Single() == new ChatLineInfo(ChatKind.Whisper, 0, "From> Ann: hi"), "a server chat line did not decode");
        var spoken = new ChatLinePacket(ChatKind.Player, 42, "Ann: olá");
        Assert.True(decoder.TryHandle(buffer.AsSpan(0, spoken.Write(buffer)), out update)
            && update!.Chat.Single() == new ChatLineInfo(ChatKind.Player, 42, "Ann: olá"), "a spoken line lost its speaker or text");
        var worded = new ChatLinePacket(ChatKind.Server, 0, "", GameText.Of(GameTextId.PlayerOnline, "Bob"));
        Assert.True(decoder.TryHandle(buffer.AsSpan(0, worded.Write(buffer)), out update)
            && update!.Chat.Single() == new ChatLineInfo(ChatKind.Server, 0, "", GameText.Of(GameTextId.PlayerOnline, "Bob")), "a server line to word lost its game text");
        var wide = new ChatLinePacket(ChatKind.Player, 1UL << 40, "far");
        Assert.True(ChatLinePacket.Read(buffer.AsSpan(0, wide.Write(buffer))).Speaker == 1UL << 40, "64-bit speakers round trip");
        Assert.True(decoder.TryHandle(buffer.AsSpan(0, CloseRequestPacket.Write(buffer)), out update) && update!.CloseRequested, "the close request did not decode");
        return Task.CompletedTask;
    }

    private static Task LogAsync()
    {
        var log = new HudChatLog();
        log.Add("Ann: hello", ChatKind.Player);
        log.Add("From> Bob: psst", ChatKind.Whisper);
        log.Add("Notice> maintenance", ChatKind.Notice);
        log.Add("Cid> clan", ChatKind.Clan);
        log.Add("hint", ChatKind.Assist);
        log.Add("Dee: party", ChatKind.Party);
        log.Add("Eve: wts", ChatKind.Trade);
        string[] Texts(HudChatTab tab) => log.Newest(tab, 0).Select(entry => entry.Text).Reverse().ToArray();
        Assert.True(Texts(HudChatTab.All).SequenceEqual(["Ann: hello", "From> Bob: psst", "Notice> maintenance", "Cid> clan", "Dee: party", "Eve: wts"]),
            string.Join("|", Texts(HudChatTab.All)));
        Assert.True(Texts(HudChatTab.Whisper).SequenceEqual(["From> Bob: psst", "Notice> maintenance"]), string.Join("|", Texts(HudChatTab.Whisper)));
        Assert.True(Texts(HudChatTab.Clan).SequenceEqual(["Notice> maintenance", "Cid> clan"]) && Texts(HudChatTab.Assist).SequenceEqual(["hint"]),
            "server lines must show in every channel tab and hints only on the assistant's page");
        Assert.True(HudChatLog.ColorOf(ChatKind.Server) == new Microsoft.Xna.Framework.Color(255, 128, 96) && HudChatLog.ColorOf(ChatKind.Whisper) == new Microsoft.Xna.Framework.Color(150, 190, 255)
            && HudChatLog.ColorOf(ChatKind.Notice) == new Microsoft.Xna.Framework.Color(230, 160, 255) && HudChatLog.ColorOf(ChatKind.Party) == new Microsoft.Xna.Framework.Color(200, 255, 0)
            && HudChatLog.ColorOf(ChatKind.Clan) == new Microsoft.Xna.Framework.Color(255, 255, 80) && HudChatLog.ColorOf(ChatKind.Trade) == new Microsoft.Xna.Framework.Color(200, 255, 255)
            && HudChatLog.ColorOf(ChatKind.Assist) == new Microsoft.Xna.Framework.Color(200, 128, 128) && HudChatLog.ColorOf(ChatKind.Player) == new Microsoft.Xna.Framework.Color(255, 255, 192),
            "the line colours changed");
        log.ScrollBy(3);
        log.Add("new", ChatKind.Player);
        Assert.True(log.Scroll == 4, "a new line must keep a scrolled log in place");
        log.ScrollBy(100);
        Assert.True(log.Scroll == HudChatLog.ScrollLimit, "scrolling must stop 32 lines before the oldest kept line");
        log.SelectTab(HudChatTab.Trade);
        Assert.True(log.Scroll == 0 && log.Tab == HudChatTab.Trade, "selecting a tab must reset the scroll");
        for (var index = 0; index < 100; index++)
        {
            log.Add($"line {index}", ChatKind.Player);
        }

        Assert.True(log.Count(HudChatTab.All) == HudChatLog.Capacity && log.Newest(HudChatTab.All, 0).First().Text == "line 99", "the log keeps 64 lines");
        return Task.CompletedTask;
    }

    private static Task InputAsync()
    {
        var input = new HudChatInput { CanDraw = static value => value is >= ' ' and <= '~' or >= '\u00A0' and <= '\u0103' };
        foreach (var character in new string('a', 90))
        {
            input.Type(character);
        }

        Assert.True(input.Text.Length == ChatRules.InputLimit, "the input line holds 78 characters");
        input.Set("");
        Assert.True(input.Type('ã') && input.Text == "ã", "a character the font draws must be accepted");
        input.Set("");
        Assert.True(!input.Type('中') && input.Text.Length == 0, "a character the font cannot draw was accepted");
        var now = 0.0;
        HudChatSend? Send(string text, HudChatTab tab = HudChatTab.All)
        {
            input.Set(text);
            now += 6000;
            return input.Submit("Ann", tab, now, out _);
        }

        Assert.True(Send("hello") == new HudChatSend(HudChatSendKind.Spoken, "hello", "Ann: hello"), "a spoken line");
        Assert.True(Send("hello", HudChatTab.Clan) == new HudChatSend(HudChatSendKind.Command, "/CLAN> hello", "hello"), "the clan tab");
        Assert.True(Send("hello", HudChatTab.Trade) == new HudChatSend(HudChatSendKind.Command, "/TRADE> hello", "hello"), "the trade tab");
        Assert.True(Send("hello", HudChatTab.Party) == new HudChatSend(HudChatSendKind.Party, "hello", "Ann: hello"), "the party tab");
        Assert.True(Send("@go", HudChatTab.Clan) == new HudChatSend(HudChatSendKind.Party, "go", "Ann: go"), "the party prefix");
        Assert.True(Send("/where Bob", HudChatTab.Trade) == new HudChatSend(HudChatSendKind.Command, "/where Bob", "/where Bob"), "commands go as typed");
        input.Set("");
        input.Recall(older: true);
        Assert.True(input.Text == "/where ", "a command is remembered by its first word");
        input.Recall(older: false);
        Assert.True(input.Text == "", "recalling past the newest empties the line");
        Assert.True(Send("~/call Bob") == new HudChatSend(HudChatSendKind.Command, "/call Bob", "/call Bob")
            && Send("`/where Bob") == new HudChatSend(HudChatSendKind.Command, "/where Bob", "/where Bob"), "the classic routing marks are dropped");
        Assert.True(Send("~hello") == new HudChatSend(HudChatSendKind.Spoken, "~hello", "Ann: ~hello"), "a tilde without a slash is spoken");
        input.WhisperTo("Bob");
        Assert.True(input.Text == "/:Bob ", "a whisper conversation starts with the name");
        input.Type('h');
        input.Type('i');
        now += 6000;
        Assert.True(input.Submit("Ann", HudChatTab.All, now, out _)!.Value.Text == "/:Bob hi" && input.Text == "/:Bob ",
            "the whisper line must come back after sending");
        input.Set("again");
        now += 6000;
        Assert.True(input.Submit("Ann", HudChatTab.All, now, out _) is not null, "fixture line");
        input.Set("again");
        Assert.True(input.Submit("Ann", HudChatTab.All, now + 1000, out _) is null, "the same line twice within five seconds was sent");
        var flood = new HudChatInput();
        string? complaint = null;
        var sent = 0;
        for (var line = 0; line < 13; line++)
        {
            flood.Set($"line {line}");
            if (flood.Submit("Ann", HudChatTab.All, 100_000 + line * 100, out var said) is not null)
            {
                sent++;
            }

            complaint ??= said;
        }

        Assert.True(sent == ChatRules.FloodLines + 1 && complaint == "Ann : I talk too much, my tongue has got frozen! WOW -_-;", $"flood: {sent} sent, {complaint}");
        flood.Set("/:Bob hi");
        Assert.True(flood.Submit("Ann", HudChatTab.All, 100_000 + 11 * 100 + ChatRules.FloodPenaltyMs - 10, out _) is null, "a frozen chat sent a whisper");
        flood.Set("thawed");
        Assert.True(flood.Submit("Ann", HudChatTab.All, 100_000 + 13 * 100 + ChatRules.FloodPenaltyMs + 3000, out _) is not null, "the chat stayed frozen");
        return Task.CompletedTask;
    }

    private static Task ReceiveAsync()
    {
        var chat = new HudChat { SelfId = 1 };
        chat.Lookup = id => id switch
        {
            2 => (EntityKind.Player, 0u),
            3 => (EntityKind.Npc, 0u),
            4 => (EntityKind.Monster, 2u),
            _ => (EntityKind.None, 0u)
        };
        var whispers = 0;
        chat.WhisperArrived += () => whispers++;
        chat.Receive(new ChatLineInfo(ChatKind.Player, 2, "Bob: hi"), 1000);
        Assert.True(chat.Bubbles.TryGet(2, out var bubble) && bubble == "Bob: hi" && chat.Window.Log.Newest(HudChatTab.All, 0).First() == new HudChatEntry("Bob: hi", ChatKind.Player),
            "a visible player's line must show over the player and in the log");
        chat.Receive(new ChatLineInfo(ChatKind.Server, 3, "Lena: welcome"), 1000);
        Assert.True(chat.Bubbles.TryGet(3, out _) && chat.Window.Log.Count(HudChatTab.All) == 1, "an NPC's line belongs over the NPC only");
        chat.Receive(new ChatLineInfo(ChatKind.Party, 2, "Bob: party"), 1000);
        Assert.True(chat.Bubbles.TryGet(2, out bubble) && bubble == "Bob: hi" && chat.Window.Log.Newest(HudChatTab.Party, 0).First().Text == "Bob: party",
            "party lines go to the log without a bubble");
        chat.Receive(new ChatLineInfo(ChatKind.Player, 9, "Far: away"), 1000);
        Assert.True(!chat.Bubbles.TryGet(9, out _) && chat.Window.Log.Newest(HudChatTab.All, 0).First().Text == "Far: away", "a line from a speaker out of view");
        Assert.True(!chat.Window.Open && whispers == 0, "the window opened for an ordinary line");
        chat.Receive(new ChatLineInfo(ChatKind.Whisper, 0, "From> Bob: psst"), 1000);
        Assert.True(chat.Window.Open && !chat.Window.Focused && whispers == 1, "a whisper must open the closed window with the sound");
        chat.Receive(new ChatLineInfo(ChatKind.Whisper, 0, "From> Bob: again"), 1000);
        Assert.True(whispers == 1, "the sound must play only when the window was closed");
        chat.Receive(new ChatLineInfo(ChatKind.Server, 0, "server"), 1000);
        Assert.True(chat.Window.Log.Newest(HudChatTab.All, 0).First() == new HudChatEntry("server", ChatKind.Server), "a server line");
        chat.Receive(new ChatLineInfo(ChatKind.Server, 0, "", GameText.Of(GameTextId.PlayerOnline, "Bob")), 1000);
        Assert.True(chat.Window.Log.Newest(HudChatTab.All, 0).First() == new HudChatEntry("Bob is currently online", ChatKind.Server),
            "a server line was not worded by its game text");
        Assert.True(chat.NameColor(4, 0) == new Microsoft.Xna.Framework.Color(220, 255, 160) && chat.NameColor(3, 0) == new Microsoft.Xna.Framework.Color(180, 180, 255)
            && chat.NameColor(2, 2) == Microsoft.Xna.Framework.Color.White, "bubble name colours changed");
        Assert.True(HudChatWindow.WhisperPartner(new HudChatEntry("From> Bob: psst", ChatKind.Whisper)) == "Bob" && HudChatWindow.WhisperPartner(new HudChatEntry("To> Cid: x", ChatKind.Whisper)) == "Cid"
            && HudChatWindow.WhisperPartner(new HudChatEntry("Bob: x", ChatKind.Player)) is null, "whisper partners");
        var closed = false;
        chat.CloseRequested += () => closed = true;
        chat.Observe(new PresenceUpdate(1, [], [], [], []) { CloseRequested = true }, 1000);
        Assert.True(closed, "the close request did not reach the screen");
        var wrapped = HudChatText.Wrap("alpha beta gamma delta", 66, 12, 255, text => text.Length * 6);
        Assert.True(wrapped.SequenceEqual(["alpha beta", "gamma delta"]), string.Join("|", wrapped));
        return Task.CompletedTask;
    }

    private static Task WindowAsync()
    {
        var chat = new HudChat { PlayerName = "Ann", SelfId = 1 };
        var sent = new List<(string Text, bool Party)>();
        chat.Send = (text, party) =>
        {
            sent.Add((text, party));
            return Task.CompletedTask;
        };
        var none = new KeyboardState();
        var enter = new KeyboardState(Keys.Enter);
        chat.Update(enter, none, [], 0, chat.Window.Log.Tab);
        Assert.True(chat.Window.Open && chat.Window.Focused && chat.KeyboardCaptured, "Enter must open the chat with the input focused");
        chat.Update(none, enter, "hey\bllo", 10, chat.Window.Log.Tab);
        Assert.True(chat.Window.Input.Text == "hello", $"typed text {chat.Window.Input.Text}");
        chat.Update(enter, none, [], 20, chat.Window.Log.Tab);
        Assert.True(sent.Single() == ("hello", false) && chat.Window.Log.Newest(HudChatTab.All, 0).First() == new HudChatEntry("Ann: hello", ChatKind.Player)
            && chat.Bubbles.TryGet(1, out var own) && own == "Ann: hello" && chat.Window.Focused, "sending must echo the line, keep the input and show the bubble");
        chat.Update(none, enter, [], 30, chat.Window.Log.Tab);
        chat.Update(enter, none, [], 40, chat.Window.Log.Tab);
        Assert.True(chat.Window.Open && !chat.Window.Focused, "Enter on an empty line releases the keyboard and keeps the window");
        var layout = Gameplay.UI.HudLayout.Create(800, 600);
        Assert.True(chat.Window.Click(layout, HudChatWindow.TabButton(layout, 4).Center, side: false) && chat.Window.Log.Tab == HudChatTab.Whisper,
            "the whisper tab did not select");
        Assert.True(chat.Window.Click(layout, HudChatWindow.NoticeButton(layout).Center, side: false) && chat.Window.NoticeMode == 1
            && chat.Window.Click(layout, HudChatWindow.NoticeButton(layout).Center, side: false) && chat.Window.NoticeMode == 2
            && chat.Window.Click(layout, HudChatWindow.NoticeButton(layout).Center, side: false) && chat.Window.NoticeMode == 0, "the notice button cycles three pages");
        Assert.True(!chat.Window.Click(layout, HudChatWindow.TabButton(layout, 1).Center, side: true), "the small window has no tabs");
        Assert.True(chat.Window.Click(layout, HudChatWindow.Toggle(layout, false).Center, side: false) && !chat.Window.Open, "the toggle closes the window");
        Assert.True(HudChatWindow.Box(layout, false) == new Microsoft.Xna.Framework.Rectangle(0, 408, 305, 192)
            && HudChatWindow.Box(layout, true) == new Microsoft.Xna.Framework.Rectangle(0, 294, 300, 116), "the chat window moved");
        var tall = Gameplay.UI.HudLayout.Create(800, 700);
        Assert.True(HudChatWindow.TabButton(tall, 0).Y == 563 + 100, "the chat window must follow the bottom edge");
        return Task.CompletedTask;
    }
}
