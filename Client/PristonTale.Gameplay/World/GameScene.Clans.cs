using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.World;

// Clan requests through the world connection; the clan's state and results come back as presence updates.
public sealed partial class GameScene
{
    public void SendClan(ClanAction action, ulong id, string name) =>
        SendSocial(_playChannel?.SendClanAsync(action, id, name, _presenceCts.Token), "clan request");

    public void SendClanMark(byte[] pixels) => SendSocial(_playChannel?.SendClanMarkAsync(pixels, _presenceCts.Token), "clan mark");
}
