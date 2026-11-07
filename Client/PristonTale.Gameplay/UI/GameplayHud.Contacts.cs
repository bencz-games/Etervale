using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Contacts;
using PristonTale.Gameplay.UI.Parties;

namespace PristonTale.Gameplay.UI;

// The friends page of the party window: its lists from the server, the recent players (whispers and trades), the friend
// button under a reached player's portrait, and the friends' presence asked again every ContactRefreshMs while it shows.
public sealed partial class GameplayHud
{
    private const double ContactRefreshMs = 10_000;
    private double _contactsAskedMs = double.MinValue;

    // Sends the contact list requests; set by the screen.
    public IContactClient Contacts { get; set; }

    public void ObserveContacts(PresenceUpdate update)
    {
        if (update.Contacts is { } lists)
        {
            _partyWindow.Friends.Apply(lists);
        }
    }

    private void SetUpContacts()
    {
        _sprites.Request(HudContactArt.All);
        var friends = _partyWindow.Friends;
        friends.Changed = (list, add, name) => Contacts?.ChangeContactAsync(list, add, name);
        friends.Whispered = _chat.Window.WhisperTo;
        _chat.WhisperPartner = friends.Met;
    }

    private void UpdateContacts(double nowMs)
    {
        if (_partyWindow.Visible && _partyWindow.Page == HudPartyPage.Friends && nowMs - _contactsAskedMs >= ContactRefreshMs)
        {
            _contactsAskedMs = nowMs;
            Contacts?.RefreshContactsAsync();
        }
    }

    // The friend button: the player goes on the friends list and the friends page opens on it, as the original's does.
    private void BefriendReached()
    {
        if (Combat.PlayerName(ButtonsPlayerId) is not { } name)
        {
            return;
        }

        Contacts?.ChangeContactAsync(ContactList.Friends, true, name);
        _partyWindow.Friends.Met(name);
        _questWindow.Close();
        _contactsAskedMs = double.MinValue;
        _partyWindow.ShowFriends(HudFriendsView.Friends);
    }
}
