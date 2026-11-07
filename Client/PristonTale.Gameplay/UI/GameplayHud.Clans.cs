using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Clans;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Gameplay.UI.Chat;

namespace PristonTale.Gameplay.UI;

// The clan: the Clan Master's window, the party window's clan page, the invitation box (the party's request box) and the
// clan button under a reached player's portrait for a chief or vice chief when that player has no clan.
public sealed partial class GameplayHud
{
    private readonly HudClanWindow _clanWindow;
    private readonly HudRequestBox _clanRequest = new(HudPartyArt.Request, ClanRules.InvitationSeconds * 1000);
    private Action<ClanAction, ulong, string> _sendClan;

    // Sends the clan requests; set by the screen.
    public Action<ClanAction, ulong, string> SendClan
    {
        set
        {
            _sendClan = value;
            _clanWindow.Send = value;
        }
    }

    // Sends the chief's new clan mark; set by the screen.
    public Action<byte[]> SendClanMark
    {
        set => _clanWindow.SendMark = value;
    }

    public ClanInfo Clan => _clanWindow.Clan;

    // The clan part of each presence update.
    public void ObserveClan(PresenceUpdate update)
    {
        if (update.Clan is { } clan)
        {
            _clanWindow.Observe(clan);
            _partyWindow.ObserveClan(clan);
            Combat.OwnClan = clan.ClanId;
            _wingGate.CastleOwner = HoldsCastle;
        }

        if (update.ClanInvitation is { } invitation)
        {
            _clanRequest.Show(invitation.RecruiterId, invitation.ChiefName, ClientText.Format("Clan.InviteLine", invitation.ClanName),
                _clock.Elapsed.TotalMilliseconds);
        }

        if (update.ClanResult is { } result)
        {
            _clanWindow.Result(result);
        }
    }

    private void SetUpClans()
    {
        _sprites.Request(HudClanArt.All);
        _partyWindow.Clan.Whispered = _chat.Window.WhisperTo;
        _partyWindow.Clan.Talk = () => _chat.Window.TalkOn(HudChatTab.Clan);
    }

    private void OpenClan(ulong npcId)
    {
        _npcOpen = false;
        _clanWindow.Open(npcId);
    }

    // The clan button shows for a recruiter beside a player without a clan.
    private bool ClanButton => Clan.CanRecruit && ButtonsPlayerId != 0 && Combat.ClanOf(ButtonsPlayerId) == 0;

    private bool ClickClan(Point pointer)
    {
        if (_clanRequest.Click(pointer) is { } answer)
        {
            _sendClan?.Invoke(answer ? ClanAction.Accept : ClanAction.Refuse, 0, "");
            return true;
        }

        return _clanWindow.Click(pointer);
    }

    private void InviteToClan(ulong playerId) => _sendClan?.Invoke(ClanAction.Invite, playerId, "");

    private bool CapturesClan(Point pointer) => _clanRequest.Captures(pointer) || _clanWindow.Contains(pointer);
}
