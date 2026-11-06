using PristonTale.Gameplay.UI.PersonalShops;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Gameplay.World;

// Personal shop requests through the world connection, and the signs of the players selling around.
public sealed partial class GameScene : IPersonalShopClient
{
    public Dictionary<ulong, string> ShopSigns => _remote?.ShopSigns ?? RemoteEntityManager.NoShopSigns;

    public Task<PersonalShopReply> OpenPersonalShopAsync(string sign, IReadOnlyList<PersonalShopEntry> entries) =>
        _playChannel?.OpenPersonalShopAsync(sign, entries, _presenceCts.Token) ?? Unavailable();

    public Task<PersonalShopReply> ClosePersonalShopAsync() => _playChannel?.ClosePersonalShopAsync(_presenceCts.Token) ?? Unavailable();

    public Task<PersonalShopReply> ViewPersonalShopAsync(ulong sellerId) => _playChannel?.ViewPersonalShopAsync(sellerId, _presenceCts.Token) ?? Unavailable();

    public Task<PersonalShopReply> BuyFromPersonalShopAsync(PersonalShopPurchase purchase) =>
        _playChannel?.BuyFromPersonalShopAsync(purchase, _presenceCts.Token) ?? Unavailable();

    private static Task<PersonalShopReply> Unavailable() =>
        Task.FromResult(new PersonalShopReply(new PersonalShopOutcome(false, PersonalShopStatus.Rejected, 0, PristonTale.Contracts.Texts.GameText.None)));
}
