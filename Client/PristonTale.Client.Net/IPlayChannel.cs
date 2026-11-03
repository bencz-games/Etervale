using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.World;

namespace PristonTale.Client.Net;

public interface IPlayChannel : IAsyncDisposable
{
    // The player's game master level (0 for players), for client-side game master controls.
    int GameMasterLevel { get; }

    // The server's game clock in game milliseconds; null before the world is joined or without a server.
    long? GameClockMs { get; }

    Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default);
    Task<NpcInteraction> InteractNpcAsync(ulong entityId, CancellationToken ct = default);

    // One typed NPC service request; the reply carries the shop, warehouse or teleport state the server sent with it.
    Task<NpcServiceReply> RequestNpcServiceAsync(NpcServiceCommand command, CancellationToken ct = default);
    // One quest step at an NPC or from a quest dialog; the journal itself arrives in the presence stream.
    Task<QuestOutcome> RequestQuestAsync(QuestCommand command, CancellationToken ct = default);
    Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default);

    // Uses one owned item (premium items, travel items, firecrackers); the server consumes it and applies the effect.
    Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command, CancellationToken ct = default);

    // A dead player chooses where to stand up; the server takes the death's costs and places it.
    Task<RestartOutcome> RestartAsync(RestartChoice choice, CancellationToken ct = default);

    // Personal shops: open the player's own over bag items, close it, show a seller's, buy from one. A view or a purchase
    // answers the shop as the server shows it now (PersonalShopReply.Listing), null once it closed.
    Task<PersonalShopReply> OpenPersonalShopAsync(string sign, IReadOnlyList<PersonalShopEntry> entries, CancellationToken ct = default);

    Task<PersonalShopReply> ClosePersonalShopAsync(CancellationToken ct = default);

    Task<PersonalShopReply> ViewPersonalShopAsync(ulong sellerId, CancellationToken ct = default);

    Task<PersonalShopReply> BuyFromPersonalShopAsync(PersonalShopPurchase purchase, CancellationToken ct = default);

    // A player on a warp stone travels to a stone of the wing warp map; the server checks the wing, level and fee.
    Task<WingGateOutcome> TravelWingGateAsync(int destination, CancellationToken ct = default);

    // The client's own position (PT-global coordinates), motion and action target, plus its field code.
    Task SendMovementAsync(Vector3 position, float facing, bool moving, bool running, ulong actionTarget, int fieldCode, CancellationToken ct = default);

    Task SendAttackAsync(ulong targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0);

    // SkillCast: the server answers with a SkillCastResult in the presence stream (PresenceUpdate.CastResult), correlated
    // by sequence. point is the aimed ground position in PT-global coordinates, or null for an entity or self cast.
    Task SendSkillCastAsync(uint sequence, uint skillCode, ulong targetEntityId, Vector3? point, CancellationToken ct = default);

    // A command about a running skill (SkillCastCommand: the release of a held charge, an order to the summon).
    Task SendSkillCommandAsync(uint sequence, uint skillCode, SkillCastCommand command, CancellationToken ct = default);

    // A line typed in the chat window (party: the party channel); replies arrive as PresenceUpdate.Chat.
    Task SendChatAsync(string text, bool party, CancellationToken ct = default);

    // Space inside the minute a yahoo word opened; the cheer arrives on the presence timeline.
    Task SendYahooKeyAsync(CancellationToken ct = default);

    // The owner confirmed dismissing the pet of one status (the code its icon shows); the icon goes with the status's
    // removal.
    Task SendDismissCrystalPetsAsync(uint statusCode, CancellationToken ct = default);

    // Party requests: ask a player to group, answer a request (PresenceUpdate.PartyRequest), or a party window button. None
    // is answered: the members (PresenceUpdate.Party) and chat lines show what changed.
    Task InviteToPartyAsync(ulong targetId, CancellationToken ct = default);

    Task AnswerPartyAsync(ulong fromId, bool accept, CancellationToken ct = default);

    Task CommandPartyAsync(PartyCommand command, ulong memberId, CancellationToken ct = default);

    // Trade requests: ask a player to trade, answer a request (PresenceUpdate.TradeRequest), the side's whole offer, lock it,
    // cancel. None is answered: the window (PresenceUpdate.Trade) and chat lines show what changed.
    Task RequestTradeAsync(ulong targetId, CancellationToken ct = default);

    Task AnswerTradeAsync(ulong fromId, bool accept, CancellationToken ct = default);

    Task OfferTradeAsync(Simulation.Trades.TradeOffer offer, CancellationToken ct = default);

    Task LockTradeAsync(CancellationToken ct = default);

    Task CancelTradeAsync(CancellationToken ct = default);

    // The friends page: put a name on a list or take it off, or ask for the lists again (who is online); both are answered
    // by PresenceUpdate.Contacts for each list.
    Task ChangeContactAsync(ContactList list, bool add, string name, CancellationToken ct = default);

    Task RefreshContactsAsync(CancellationToken ct = default);

    IAsyncEnumerable<PresenceUpdate> ReadAllAsync(CancellationToken ct = default);

    // The consumer is done with an update read from ReadAllAsync: its lists may be reused for a later one.
    void Recycle(PresenceUpdate update);
}
