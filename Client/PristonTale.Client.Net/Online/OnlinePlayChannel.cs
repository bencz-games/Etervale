using System.Numerics;
using System.Runtime.CompilerServices;
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
using PristonTale.Protocol.Packets;

namespace PristonTale.Client.Net.Online;

// Opening sends JoinWorld and completes on WorldJoined; disposing sends the Etervale leave request so the
// server saves before the client returns to another screen.
internal sealed class OnlinePlayChannel : IPlayChannel
{
    private readonly GameServerConnection _connection;
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _start;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public int GameMasterLevel => _connection.GameMasterLevel;

    public long? GameClockMs => _connection.HasGameClock ? _connection.GameClockMs : null;

    public OnlinePlayChannel(GameServerConnection connection, CancellationToken cancellationToken = default)
    {
        _connection = connection;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _start = connection.OpenPlayAsync(_lifetime.Token);
    }

    public async Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplyProgressionAsync(command, ct);
    }

    public async Task<NpcInteraction> InteractNpcAsync(ulong entityId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.InteractNpcAsync(entityId, ct);
    }

    public async Task<NpcServiceReply> RequestNpcServiceAsync(NpcServiceCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.RequestNpcServiceAsync(command, ct);
    }

    public async Task<QuestOutcome> RequestQuestAsync(QuestCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.RequestQuestAsync(command, ct);
    }

    public async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.ApplyInventoryAsync(command, ct);
    }

    public async Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.UseItemAsync(command, ct);
    }

    public async Task<RestartOutcome> RestartAsync(RestartChoice choice, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.RestartAsync(choice, ct);
    }

    public async Task<PersonalShopReply> OpenPersonalShopAsync(string sign, IReadOnlyList<PersonalShopEntry> entries, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.OpenAsync(sign, entries, ct);
    }

    public async Task<PersonalShopReply> ClosePersonalShopAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.CloseAsync(ct);
    }

    public async Task<PersonalShopReply> ViewPersonalShopAsync(ulong sellerId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.ViewAsync(sellerId, ct);
    }

    public async Task<PersonalShopReply> BuyFromPersonalShopAsync(PersonalShopPurchase purchase, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.PersonalShops.BuyAsync(purchase, ct);
    }

    public async Task<WingGateOutcome> TravelWingGateAsync(int destination, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        return await _connection.TravelWingGateAsync(destination, ct);
    }

    public async Task SendMovementAsync(Vector3 position, float facing, bool moving, bool running, ulong actionTarget, int fieldCode, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendMovement(position, facing, moving, running, actionTarget, fieldCode);
    }

    public async Task SendAttackAsync(ulong targetEntityId, bool sustained, CancellationToken ct = default, ulong sequence = 0)
    {
        await _start.WaitAsync(ct);
        _connection.SendAttackRequest(targetEntityId, sustained, sequence);
    }

    public async Task SendSkillCastAsync(uint sequence, uint skillCode, ulong targetEntityId, Vector3? point, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSkillCast(sequence, skillCode, targetEntityId, point);
    }

    public async Task SendChatAsync(string text, bool party, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendChat(text, party);
    }

    public async Task SendYahooKeyAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendYahooKey();
    }

    public async Task SendDismissCrystalPetsAsync(uint statusCode, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendDismissCrystalPets(statusCode);
    }

    public async Task InviteToPartyAsync(ulong targetId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyInvitePacket(targetId));
    }

    public async Task AnswerPartyAsync(ulong fromId, bool accept, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyAnswerPacket(fromId, accept));
    }

    public async Task CommandPartyAsync(PartyCommand command, ulong memberId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new PartyCommandPacket(command, memberId));
    }

    public async Task RequestTradeAsync(ulong targetId, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeRequestPacket(targetId));
    }

    public async Task AnswerTradeAsync(ulong fromId, bool accept, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeAnswerPacket(fromId, accept));
    }

    public async Task OfferTradeAsync(Simulation.Trades.TradeOffer offer, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeOfferPacket(offer.Money, offer.Entries.Select(static entry => new TradeEntryWire(entry.ItemId, entry.Cell)).ToArray()));
    }

    public async Task LockTradeAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeSignalPacket(Protocol.Binary.EtervaleOpcodes.TradeLock));
    }

    public async Task CancelTradeAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new TradeSignalPacket(Protocol.Binary.EtervaleOpcodes.TradeCancel));
    }

    public async Task ChangeContactAsync(ContactList list, bool add, string name, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new ContactChangePacket(list, add, name));
    }

    public async Task RefreshContactsAsync(CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSocial(new ContactRefreshPacket());
    }

    public async Task SendSkillCommandAsync(uint sequence, uint skillCode, SkillCastCommand command, CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        _connection.SendSkillCast(sequence, skillCode, 0, null, command);
    }

    public async IAsyncEnumerable<PresenceUpdate> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await _start.WaitAsync(ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await foreach (var update in _connection.ReadPresenceAsync(linked.Token))
        {
            yield return update;
        }
    }

    public void Recycle(PresenceUpdate update) => _connection.Recycle(update);

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (!_start.IsCompleted)
            {
                await _lifetime.CancelAsync();
            }

            try
            {
                await _start;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            if (_start.IsCompletedSuccessfully)
            {
                await _connection.ClosePlayAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            await _lifetime.CancelAsync();
            _lifetime.Dispose();
        }
    }
}
