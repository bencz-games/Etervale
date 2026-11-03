using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

// The party: its page of the party window (D or the bar's party button; it shares its place with the quest page, so one
// closes the other), the request box, the buttons under a reached player's portrait, and the members for the minimap,
// the name boxes and the chat. The window opens when a party forms and closes when it is released, as in the original.
public sealed partial class GameplayHud
{
    private readonly HudPartyWindow _partyWindow = new();
    private readonly HudRequestBox _partyRequest = new(HudPartyArt.Request, Simulation.Parties.PartyRules.InviteMs);
    private readonly List<Vector3> _minimapParty = new(Contracts.Social.PartyLimits.MaxMembers);

    // Sends the party requests; set by the screen.
    public IPartyClient Parties { get; set; }

    public PartyInfo Party { get; private set; } = PartyInfo.None;

    // The player whose portrait shows the trade, party and friend buttons (the scene reached it); 0 for none.
    public ulong ButtonsPlayerId { get; set; }

    // A button under the portrait was pressed: the row closes until the player is reached again.
    public event Action PlayerButtonsUsed;

    // The party part of each presence update. As in the original, a request is not shown while the warehouse is open.
    public void ObserveParty(PresenceUpdate update)
    {
        if (update.Party is { } party)
        {
            var formed = Party.Members.Count == 0 && party.Members.Count > 0;
            var released = Party.Members.Count > 0 && party.Members.Count == 0;
            Party = party;
            _partyWindow.Refresh(party);
            if (formed)
            {
                _questWindow.Close();
                _partyWindow.Show();
            }
            else if (released && !_partyWindow.ShowsFriends)
            {
                _partyWindow.Close();
            }
        }

        if (update.PartyRequest is { } request && !_warehouse.IsOpen)
        {
            var line = request.Recommended.Length > 0
                ? Simulation.Parties.PartyMessages.Format(Simulation.Parties.PartyMessages.Recommends, request.Recommended)
                : Simulation.Parties.PartyMessages.Request;
            _partyRequest.Show(request.FromId, request.FromName, line, _clock.Elapsed.TotalMilliseconds);
        }
    }

    public bool InParty(ulong entityId) => entityId != SelfId && Party.Contains(entityId);

    private void ToggleQuestWindow()
    {
        _partyWindow.Close();
        _questWindow.Toggle();
    }

    private void TogglePartyWindow()
    {
        _questWindow.Close();
        _partyWindow.Toggle();
    }

    private void SetUpParty()
    {
        _sprites.Request(HudPartyArt.All);
        _partyWindow.Commanded = (command, member) => Parties?.CommandPartyAsync(command, member);
        _partyWindow.Whispered = _chat.Window.WhisperTo;
        _chat.InParty = InParty;
        Combat.InParty = InParty;
    }

    private bool ShowsPlayerButtons => ButtonsPlayerId != 0 && TargetId == ButtonsPlayerId;

    private bool ClickParty(HudLayout layout, Point pointer)
    {
        var from = _partyRequest.FromId;
        if (_partyRequest.Click(pointer) is { } answer)
        {
            Parties?.AnswerPartyAsync(from, answer);
            return true;
        }

        if (ShowsPlayerButtons && HudPlayerButtons.At(HudCombat.TargetFrame(layout), pointer) is { } button)
        {
            if (button == HudPlayerButton.Party)
            {
                Parties?.InviteToPartyAsync(ButtonsPlayerId);
            }
            else if (button == HudPlayerButton.Trade)
            {
                Trades?.RequestTradeAsync(ButtonsPlayerId);
            }
            else
            {
                BefriendReached();
            }

            PlayerButtonsUsed?.Invoke();
            return true;
        }

        return _partyWindow.Click(layout, pointer, Party, SelfId, _clock.Elapsed.TotalSeconds);
    }

    private bool CapturesParty(HudLayout layout, Point pointer) =>
        _partyRequest.Captures(pointer) || _partyWindow.Contains(layout, pointer)
        || ShowsPlayerButtons && HudPlayerButtons.At(HudCombat.TargetFrame(layout), pointer) is not null;

    // The other members' last known places, for the minimap.
    private List<Vector3> MinimapParty()
    {
        _minimapParty.Clear();
        var members = Party.Members;
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index].EntityId != SelfId)
            {
                _minimapParty.Add(new Vector3(members[index].Position.X, 0f, members[index].Position.Y));
            }
        }

        return _minimapParty;
    }
}
