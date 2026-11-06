using System.Text;
using PristonTale.Simulation.Social;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Chat;

// What the input line sends: a spoken line of the local player (shown at once in the log and over the head), a party line
// (shown at once in the log), or a command the server answers.
public enum HudChatSendKind
{
    Spoken,
    Party,
    Command
}

public readonly record struct HudChatSend(HudChatSendKind Kind, string Text, string Shown);

// The chat input line: 78 characters, the last 16 commands recalled with Up and Down, and the flood rules. A line is
// composed for the selected tab (clan, party and trade tabs prefix their channel); a slash line goes as typed.
public sealed class HudChatInput
{
    public const int HistorySize = 16;
    private readonly StringBuilder _text = new();
    private readonly string[] _history = new string[HistorySize];
    private int _historyCount;
    private int _recall;
    private readonly ChatFloodGuard _flood = new();

    // The characters the HUD font draws; the window sets it from its painter. The wire carries any character.
    public Func<char, bool> CanDraw { get; set; } = static _ => true;
    private string _whisperName = "";
    private int _whisperPrefix;

    // The text as a string, made once per change (the window reads it every frame).
    public string Text => _shown ??= _text.ToString();
    private string _shown;

    public void Clear()
    {
        _text.Clear();
        _shown = null;
        _whisperName = "";
        _whisperPrefix = 0;
    }

    public void Set(string text)
    {
        _text.Clear().Append(text.Length > ChatRules.InputLimit ? text[..ChatRules.InputLimit] : text);
        _shown = null;
    }

    // Starts a whisper conversation: the line holds "/:name " and comes back to it after each line sent.
    public void WhisperTo(string name)
    {
        Set("/:" + name + " ");
        _whisperName = name;
        _whisperPrefix = _text.Length;
    }

    public bool Type(char value)
    {
        if (char.IsControl(value) || _text.Length >= ChatRules.InputLimit || !CanDraw(value))
        {
            return false;
        }

        _text.Append(value);
        _shown = null;
        return true;
    }

    public void Backspace()
    {
        if (_text.Length > 0)
        {
            _text.Remove(_text.Length - 1, 1);
            _shown = null;
        }
    }

    // Up walks back through the saved commands, Down forward; past the newest the line empties.
    public void Recall(bool older)
    {
        if (_historyCount == 0)
        {
            return;
        }

        if (older)
        {
            _recall--;
            if (_recall < 0)
            {
                _recall = _historyCount - 1;
            }
        }
        else
        {
            _recall++;
            if (_recall >= _historyCount)
            {
                _recall = _historyCount - 1;
                _text.Clear();
                _shown = null;
                return;
            }
        }

        Set(_history[_recall % HistorySize]);
    }

    // The line to send for Enter, or null when nothing goes out (empty line, frozen by flooding, a repeated line). The
    // spoken text the player sees is "Name: text"; a frozen chat answers with the overhead complaint instead.
    public HudChatSend? Submit(string playerName, HudChatTab tab, double nowMs, out string complaint)
    {
        complaint = null;
        var typed = ChatRules.WithoutRoute(Text.TrimEnd(' '));
        var continued = _whisperName.Length > 0 && Text.Length > _whisperPrefix;
        if (!continued)
        {
            _whisperName = "";
            _whisperPrefix = 0;
        }

        if (typed.Length == 0)
        {
            return null;
        }

        var command = ChatRules.IsCommand(typed) || typed[0] == '@';
        var whisper = ChatRules.IsWhisper(typed);
        switch (_flood.Check(typed, command, whisper, nowMs))
        {
            case ChatFloodVerdict.Froze:
                complaint = ClientText.Format("Chat.Frozen", playerName);
                return null;
            case ChatFloodVerdict.Frozen or ChatFloodVerdict.Repeated:
                return null;
        }

        if (command && typed.Length > 1 && typed[1] != '*' && typed[0] != '@')
        {
            Save(typed);
        }

        _text.Clear();
        _shown = null;
        if (continued)
        {
            _text.Append("/:").Append(_whisperName).Append(' ');
            _shown = null;
        }

        if (command)
        {
            return typed[0] == '@'
                ? new HudChatSend(HudChatSendKind.Party, typed[1..], playerName + ": " + typed[1..])
                : new HudChatSend(HudChatSendKind.Command, typed, typed);
        }

        return tab switch
        {
            HudChatTab.Clan => new HudChatSend(HudChatSendKind.Command, "/CLAN> " + typed, typed),
            HudChatTab.Party => new HudChatSend(HudChatSendKind.Party, typed, playerName + ": " + typed),
            HudChatTab.Trade => new HudChatSend(HudChatSendKind.Command, "/TRADE> " + typed, typed),
            _ when YahooRules.IsWord(typed) => new HudChatSend(HudChatSendKind.Command, typed, typed),
            _ => new HudChatSend(HudChatSendKind.Spoken, typed, ChatRules.Compose(playerName, typed))
        };
    }

    // A command is remembered by its first word and a space, once.
    private void Save(string line)
    {
        var space = line.IndexOf(' ');
        var word = (space < 0 ? line : line[..space]) + " ";
        for (var index = 0; index < Math.Min(_historyCount, HistorySize); index++)
        {
            if (_history[index] == word)
            {
                return;
            }
        }

        _history[_historyCount % HistorySize] = word;
        _historyCount++;
        _recall = _historyCount;
    }
}
