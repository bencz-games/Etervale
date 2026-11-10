using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Gameplay.World;

// Requests of Bless Castle's butler window through the world connection; the window's state and results come back as
// presence updates.
public sealed partial class GameScene
{
    public void SendCastleOwner(CastleOwnerAction action, ulong npcId, CastleSetup setup, long amount) =>
        SendSocial(_playChannel?.SendCastleOwnerAsync(action, npcId, setup, amount, _presenceCts.Token), "castle owner request");

    // A siege's end in the castle: the music stops and the fanfare plays, the castle's music back once the siege closes.
    public void PlaySiegeEnd()
    {
        _fieldAudio?.SetEventMusic("");
        _fieldAudio?.PlayEvent(Audio.EventCue.StageComplete);
    }

    public void RestoreFieldMusic() => _fieldAudio?.RestoreFieldMusic();

    public void SendBellatraClan(BellatraClanAction action, ulong npcId, long amount) =>
        SendSocial(_playChannel?.SendBellatraClanAsync(action, npcId, amount, _presenceCts.Token), "Bellatra secretary request");

    public void SendBellatraResult(int team) =>
        SendSocial(_playChannel?.SendBellatraResultAsync(team, _presenceCts.Token), "Bellatra result request");
}
