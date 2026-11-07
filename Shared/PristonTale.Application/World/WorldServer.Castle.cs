using PristonTale.Application.Castle;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// Bless Castle's siege on the tick: the castle field places its defenders when the siege opens (or when the field opens
// during one), and the world publishes what stands and the clans' damage at most once a second while it changes.
public sealed partial class WorldServer : ICastleWorld
{
    private CastleSiege? _castleSiege;
    private CastleSiegeView _castleSiegeView = CastleSiegeView.None;
    private long _castleOwnerClanId;
    private int _castleSkill;
    private int _castleTax;
    private bool _castleTaxChanged;
    private long _castleTaxGold;
    private long _clanSkillRenewAt;

    public CastleSiegeView Siege => Volatile.Read(ref _castleSiegeView);

    // The castle's tax in force on the fees players pay (0 without an owner).
    public int CastleTax => Volatile.Read(ref _castleTax);

    // The owner clan, the clan skill and the tax in force; the skill is renewed and the tax reaches the fields on the tick
    // that follows.
    public void SetOwner(long clanId, CastleClanSkill skill, int tax)
    {
        Volatile.Write(ref _castleOwnerClanId, clanId);
        Volatile.Write(ref _castleSkill, (int)skill);
        Volatile.Write(ref _clanSkillRenewAt, 0);
        Volatile.Write(ref _castleTax, CastleRules.TaxInForce(clanId, tax));
        Volatile.Write(ref _castleTaxChanged, true);
    }

    public long TakeTaxGold() => Interlocked.Exchange(ref _castleTaxGold, 0);

    // Gold players spent (the currency report) and the castle's tax part of it, gathered for the treasury.
    internal void RecordSpending(GameMasters.CurrencySink sink, long spent, long taxGold)
    {
        Currency.Record(sink, spent);
        if (taxGold > 0)
        {
            Interlocked.Add(ref _castleTaxGold, taxGold);
        }
    }

    private void ApplyCastleTax()
    {
        if (!Volatile.Read(ref _castleTaxChanged))
        {
            return;
        }

        Volatile.Write(ref _castleTaxChanged, false);
        foreach (var field in _fields.Values)
        {
            ApplyOptions(field, _options, CastleTax);
        }
    }

    public Task OpenSiegeAsync(CastleSiegeOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return EnqueueAsync(NoOwner, () =>
        {
            _castleSiege = new CastleSiege(order);
            if (_fields.TryGetValue(FieldCodes.Castle, out var field))
            {
                field.OpenSiege(_castleSiege);
            }

            PublishSiege(_castleSiege);
            return true;
        }, waitForCapacity: true);
    }

    public Task CloseSiegeAsync() =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_castleSiege is { } siege)
            {
                if (_fields.TryGetValue(FieldCodes.Castle, out var field))
                {
                    field.CloseSiege();
                }

                Volatile.Write(ref _castleSiegeView, View(siege) with { Running = false });
                _castleSiege = null;
            }

            return true;
        }, waitForCapacity: true);

    // Living players in the castle field outside the owner clan go to a random start point of the castle (the dead rise
    // there anyway).
    public Task<IReadOnlyList<CastleArrival>> EndSiegeAsync(long ownerClanId) =>
        EnqueueAsync<IReadOnlyList<CastleArrival>>(NoOwner, () =>
        {
            Volatile.Write(ref _castleSiegeView, CastleSiegeView.None);
            if (!_catalog.TryGetFieldByCode(FieldCodes.Castle, out var definition))
            {
                return [];
            }

            var arrivals = new List<CastleArrival>();
            foreach (var connection in _connections.Values.Where(connection => connection.FieldId == FieldCodes.Castle).ToList())
            {
                var player = connection.Player;
                if (player.IsDead || CastleSiegeRules.HoldsCastle(player.Clan?.ClanId ?? 0, ownerClanId)
                    || FieldArrival(FieldCodes.Castle, Random.Shared) is not { } arrival)
                {
                    continue;
                }

                var position = MoveTo(connection, definition, arrival.Position with { Y = 0f }, onFloor: true);
                arrivals.Add(new CastleArrival(player.EntityId, FieldCodes.Castle, position));
            }

            return arrivals;
        }, waitForCapacity: true);

    public Task<PreparedNpcPayment?> PrepareOwnerPaymentAsync(Sessions.PlayerSession session, ulong npcEntityId, long fee) =>
        PrepareNpcPaymentAsync(session, npcEntityId, Simulation.World.Npcs.NpcServiceKind.BlessCastle, fee);

    public Task<bool> CommitOwnerPaymentAsync(Sessions.PlayerSession session, PreparedNpcPayment payment) => CommitNpcPaymentAsync(session, payment);

    public Task<Sessions.PlayerSession?> FountainRecipientAsync(long ownerClanId, ulong after) =>
        EnqueueAsync<Sessions.PlayerSession?>(NoOwner, () =>
        {
            Sessions.PlayerSession? first = null;
            Sessions.PlayerSession? next = null;
            foreach (var connection in _connections.Values)
            {
                if (connection.Session is not { IsPlaying: true } session || !AtFountain(connection, ownerClanId))
                {
                    continue;
                }

                if (first is null || session.EntityId < first.EntityId)
                {
                    first = session;
                }

                if (session.EntityId > after && (next is null || session.EntityId < next.EntityId))
                {
                    next = session;
                }
            }

            return next ?? first;
        }, waitForCapacity: true);

    // Checked again on the tick that prepares it: the recipient may have left the fountain or the owner clan since.
    public Task<PreparedCharacterOperation<bool>?> PrepareFountainPotionAsync(Sessions.PlayerSession session) =>
        PrepareIntoBagAsync(NoOwner, session, connection =>
            AtFountain(connection, Volatile.Read(ref _castleOwnerClanId)) && _worldData.Current.Items.TryGet(CastleRules.FountainPotion, out var potion)
                ? ItemRoller.Create(potion, 1, _fields[connection.FieldId].Rng)
                : null, waitForCapacity: true);

    public Task<bool> CommitFountainPotionAsync(Sessions.PlayerSession session, PreparedCharacterOperation<bool> operation) =>
        CommitIntoBagAsync(session, operation, Persistence.Abstractions.GameLogs.ItemTransferKind.CastleFountain, "a fountain potion");

    private static bool AtFountain(Connection connection, long ownerClanId) =>
        connection.FieldId == FieldCodes.Castle && CastleSiegeRules.HoldsCastle(connection.Player.Clan?.ClanId ?? 0, ownerClanId)
        && CastleRules.AtFountain(connection.Player.Position);

    // False without a siege; the castle field weakens its defenders now, or when it places them.
    public Task<bool> WeakenSiegeAsync() =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_castleSiege is not { } siege)
            {
                return false;
            }

            siege.Weakened = true;
            if (_fields.TryGetValue(FieldCodes.Castle, out var field))
            {
                field.WeakenSiege();
            }

            return true;
        }, waitForCapacity: true);

    // Every CastleClanSkills.RenewSeconds: the owner clan's members get the clan skill in force again; everyone else
    // (and a member holding another skill) loses it.
    private void RenewClanSkill(long nowSeconds)
    {
        if (nowSeconds < Volatile.Read(ref _clanSkillRenewAt))
        {
            return;
        }

        Volatile.Write(ref _clanSkillRenewAt, nowSeconds + CastleClanSkills.RenewSeconds);
        var owner = Volatile.Read(ref _castleOwnerClanId);
        var skill = (CastleClanSkill)Volatile.Read(ref _castleSkill);
        foreach (var connection in _connections.Values)
        {
            var player = connection.Player;
            var field = _fields[connection.FieldId];
            var holds = skill != CastleClanSkill.None && CastleSiegeRules.HoldsCastle(player.Clan?.ClanId ?? 0, owner);
            foreach (var other in CastleClanSkills.All)
            {
                var code = CastleClanSkills.StatusCode(other);
                if ((!holds || other != skill) && player.Character.Effects.TryGet(code, out _))
                {
                    field.EndItemEffect(player, code);
                }
            }

            if (holds)
            {
                field.QueueItemEffect(player.EntityId, CastleClanSkills.Spec(skill), CastleClanSkills.LastSeconds * 1000f);
            }
        }
    }

    private void OpenSiegeIn(FieldInstance field)
    {
        if (field.FieldId == FieldCodes.Castle && _castleSiege is { } siege)
        {
            field.OpenSiege(siege);
        }
    }

    // After the fields stepped: the siege's view when it changed and a second passed since the last one, or at once when
    // the Tower of Valhalla fell.
    private void PublishSiege(long nowSeconds)
    {
        if (_castleSiege is not { } siege || !siege.Changed && siege.Board.Version == siege.PublishedVersion)
        {
            return;
        }

        if (nowSeconds > siege.PublishedSecond || siege.ValhallaFallen)
        {
            siege.PublishedSecond = nowSeconds;
            PublishSiege(siege);
        }
    }

    private void PublishSiege(CastleSiege siege)
    {
        siege.Changed = false;
        siege.PublishedVersion = siege.Board.Version;
        Volatile.Write(ref _castleSiegeView, View(siege));
    }

    private static CastleSiegeView View(CastleSiege siege) => new(true, siege.DoorDown, siege.Crystals, [.. siege.Mercenaries], siege.ValhallaFallen,
        siege.Winner, siege.Board.Top(CastleSiegeRules.TopClans), siege.Board.Total);
}
