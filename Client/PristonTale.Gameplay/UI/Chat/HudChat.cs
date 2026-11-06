using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Social;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI.Chat;

// The chat on the client: the window, the input line, the speech bubbles and the lines the server sends. A line over a
// character in view becomes a bubble (and, from a player, a log line in the player colour); other lines go to the log in
// their own colour, and the first whisper or notice while the window is closed opens it with the whisper sound.
public sealed class HudChat
{
    private static readonly Color HoveredName = Color.White;

    public HudChatWindow Window { get; } = new();
    public HudChatBubbles Bubbles { get; } = new();

    // Sends a typed line (party: the party channel); set by the screen.
    public Func<string, bool, Task> Send { get; set; }

    // What the client knows of an entity in view (HudCombat): its kind and a summon's owner.
    public Func<ulong, (EntityKind Kind, ulong OwnerId)> Lookup { get; set; } = static _ => (EntityKind.None, 0);

    public string PlayerName { get; set; } = "";
    public ulong SelfId { get; set; }

    // Raised when a whisper or notice opens the closed window (the whisper sound).
    public event Action WhisperArrived;

    // The other side of each whisper line, sent or received (the friends page's recent players).
    public Action<string> WhisperPartner { get; set; }

    // Raised when the server asks the client to save and quit.
    public event Action CloseRequested;

    public bool KeyboardCaptured => Window.Focused;

    public void Observe(PresenceUpdate update, double nowMs)
    {
        foreach (var line in update.Chat)
        {
            Receive(line, nowMs);
        }

        if (update.CloseRequested)
        {
            CloseRequested?.Invoke();
        }
    }

    public void Receive(ChatLineInfo line, double nowMs)
    {
        var text = line.Line.IsNone ? line.Text : ClientText.Format(line.Line);
        var (kind, _) = line.SpeakerId == 0 ? (EntityKind.None, 0u) : Lookup(line.SpeakerId);
        if (kind != EntityKind.None && line.Kind != ChatKind.Party)
        {
            Bubbles.Say(line.SpeakerId, text, nowMs, ChatRules.OtherBubbleMs);
            if (kind == EntityKind.Player)
            {
                Window.Log.Add(text, ChatKind.Player);
            }

            return;
        }

        Window.Log.Add(text, line.Kind);
        if (HudChatWindow.WhisperPartnerOf(line.Kind, text) is { } partner)
        {
            WhisperPartner?.Invoke(partner);
        }

        if (line.Kind is ChatKind.Whisper or ChatKind.Notice && !Window.Open)
        {
            Window.Reveal();
            WhisperArrived?.Invoke();
        }
    }

    // A line of the client's own (results of the player's actions) in the server colour.
    public void System(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Window.Log.Add(text, ChatKind.Server);
        }
    }

    // A quest hint in the assistant's colour and page.
    public void Assist(string text) => Window.Log.Add(text, ChatKind.Assist);

    // Enter opens the input or sends its line (Escape closes the window through the HUD); Page Up and Page Down scroll; Up and Down recall
    // commands. Typed characters (backspace included) arrive from the window's text input.
    public void Update(KeyboardState keyboard, KeyboardState previous, IEnumerable<char> typed, double nowMs, HudChatTab tab)
    {
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !previous.IsKeyDown(key);
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (!Window.Focused)
        {
            if (Pressed(Keys.Enter) && !control)
            {
                Window.OpenInput();
            }

            return;
        }

        foreach (var character in typed)
        {
            if (character == '\b')
            {
                Window.Input.Backspace();
            }
            else
            {
                Window.Input.Type(character);
            }
        }

        if (Pressed(Keys.PageUp))
        {
            Window.Log.ScrollBy(Math.Max(1, Window.ShownMessages));
        }

        if (Pressed(Keys.PageDown))
        {
            Window.Log.ScrollBy(-Math.Max(1, Window.ShownMessages));
        }

        if (Pressed(Keys.Up))
        {
            Window.Input.Recall(older: true);
        }

        if (Pressed(Keys.Down))
        {
            Window.Input.Recall(older: false);
        }

        if (Pressed(Keys.Enter) && !control)
        {
            Submit(nowMs, tab);
        }
    }

    private void Submit(double nowMs, HudChatTab tab)
    {
        if (Window.Input.Text.Length == 0)
        {
            Window.Unfocus();
            return;
        }

        var send = Window.Input.Submit(PlayerName, tab, nowMs, out var complaint);
        if (complaint is not null)
        {
            Bubbles.Say(SelfId, complaint, nowMs, ChatRules.FloodPenaltyMs);
        }

        if (send is not { } line)
        {
            Window.Input.Clear();
            return;
        }

        switch (line.Kind)
        {
            case HudChatSendKind.Spoken:
                Window.Log.Add(line.Shown, ChatKind.Player);
                Bubbles.Say(SelfId, line.Shown, nowMs, ChatRules.SelfBubbleMs);
                break;
            case HudChatSendKind.Party:
                Window.Log.Add(line.Shown, ChatKind.Party);
                break;
        }

        _ = Send?.Invoke(line.Text, line.Kind == HudChatSendKind.Party);
    }

    // Whether a player is in the local player's party (its name takes the party colour).
    public Func<ulong, bool> InParty { get; set; } = static _ => false;

    // The colour of a speaker's name in a bubble: players, the party and owned summons, monsters, NPCs; the hovered or
    // selected speaker is drawn brighter.
    public Color NameColor(ulong id, ulong hovered)
    {
        var (kind, owner) = id == SelfId ? (EntityKind.Player, 0u) : Lookup(id);
        if (id == hovered)
        {
            return kind == EntityKind.Monster && owner == 0 ? HudNameColors.HoveredMonster : HoveredName;
        }

        return kind switch
        {
            EntityKind.Monster => owner != 0 ? HudNameColors.Party : HudNameColors.Monster,
            EntityKind.Npc => HudNameColors.Npc,
            _ => InParty(id) ? HudNameColors.Party : HudNameColors.Player
        };
    }
}
