using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Application.World;

// The clan each player shows over its head (WorldPlayer.Clan, part of its looks); the Clan Master's fees go through
// PrepareNpcPaymentAsync.
public sealed partial class WorldServer
{
    // The clan the player shows (null for none); its looks change, so viewers are described it again.
    public Task SetClanTagAsync(ulong entityId, ClanTag? tag) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_connections.TryGetValue(entityId, out var connection))
            {
                connection.Player.Clan = tag;
            }

            return true;
        }, waitForCapacity: true);
}
