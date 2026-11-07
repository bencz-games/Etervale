using System.Data.Common;
using System.Threading.Channels;
using PristonTale.Application.Clans;
using PristonTale.Application.Saves;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Bellatra;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Bellatra;

public sealed record BellatraClanRank(string Name, long Points);

// What the secretary shows a player: the clan operating Bellatra today (none: 0), its chief, the tax on prizes, the
// player's clan's income balance and whether the player may redeem it (its chief), and yesterday's best clans.
public sealed record BellatraClanView(long OperatingClanId, string OperatingName, string OperatingChief, int Tax, long Balance, bool CanRedeem,
    IReadOnlyList<BellatraClanRank> Top);

// Bellatra's clans, by the server's local day (BellatraDays): a paid prize gives its entrant's clan its points for the
// prize's day and the clan operating Bellatra the prize's tax as income; at a new day the clan with the most points of
// the day before (one that still exists) operates Bellatra, none after a day without games, and no day turns while the
// clans are not loaded. A clan's chief redeems its income at the secretary, stored with the character before the gold
// reaches it. Prizes wait in their own queue (Queue, Records) so that storage never holds the payments; storage runs
// under one gate, a refused one losing a prize's points and income (RecordAsync answers false). The day's ranking and
// the balances read are kept in memory.
public sealed class BellatraClanAppService(IBellatraRepository repository, IClanDirectory clans, WorldServer world, TimeProvider? clock = null,
    ISessionRegistry? sessions = null, ICharacterSaves? saves = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly DurableOperations? _durable = saves is null ? null : new DurableOperations(saves);
    private readonly Channel<BellatraPrize> _records = Channel.CreateUnbounded<BellatraPrize>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly Dictionary<long, long> _balances = [];
    private BellatraState? _state;
    private (int Day, IReadOnlyList<BellatraClanRank> Top) _ranking = (-1, []);

    public ChannelReader<BellatraPrize> Records => _records.Reader;

    public void Queue(BellatraPrize prize) => _records.Writer.TryWrite(prize);

    public async Task<bool> RecordAsync(BellatraPrize prize)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await StateAsync();
            await repository.RecordAsync(prize.Day, prize.ClanId, prize.Points, state.OperatingClanId, prize.Tax, _clock.GetUtcNow().ToUnixTimeSeconds());
            if (state.OperatingClanId != 0 && prize.Tax > 0 && _balances.TryGetValue(state.OperatingClanId, out var balance))
            {
                _balances[state.OperatingClanId] = balance + prize.Tax;
            }

            return true;
        }
        catch (DbException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Null while storage refuses.
    public async Task<BellatraClanView?> ViewAsync(ulong entityId)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await StateAsync();
            var clanId = clans.ClanOf(entityId);
            var balance = clanId == 0 ? 0 : await BalanceAsync(clanId);
            var yesterday = BellatraDays.Before(state.Day);
            if (_ranking.Day != yesterday)
            {
                var top = await repository.TopAsync(yesterday, Contracts.Social.BellatraLimits.TopClans);
                _ranking = (yesterday, [.. top.Where(clan => clans.NameOf(clan.ClanId) is not null).Select(clan => new BellatraClanRank(clans.NameOf(clan.ClanId)!, clan.Points))]);
            }

            return new BellatraClanView(state.OperatingClanId, clans.NameOf(state.OperatingClanId) ?? "", clans.ChiefNameOf(state.OperatingClanId) ?? "",
                world.Options.Bellatra.Tax, balance, balance > 0 && clans.RankOf(entityId) == ClanRank.Chief, _ranking.Top);
        }
        catch (DbException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The chief takes gold out of its clan's Bellatra income at the secretary.
    public async Task<BellatraClanStatus> RedeemAsync(string sessionToken, ulong npcEntityId, long amount)
    {
        if (sessions is null || _durable is null || !sessions.TryGet(sessionToken, out var session))
        {
            return BellatraClanStatus.Unavailable;
        }

        await _gate.WaitAsync();
        try
        {
            var clanId = clans.ClanOf(session.EntityId);
            if (clanId == 0 || clans.RankOf(session.EntityId) != ClanRank.Chief)
            {
                return BellatraClanStatus.NotChief;
            }

            if (amount <= 0 || amount > await BalanceAsync(clanId))
            {
                return BellatraClanStatus.NotEnoughBalance;
            }

            using var lease = await _durable.AdmitAsync(sessions, sessionToken, CancellationToken.None);
            var status = await lease.RunAsync(
                s => world.PrepareNpcPaymentAsync(s, npcEntityId, NpcServiceKind.EventBooth, -amount, serviceValue: BellatraRules.SecretaryBooth),
                world.CommitNpcPaymentAsync,
                static prepared => prepared.Status switch
                {
                    NpcPaymentStatus.Ok => BellatraClanStatus.Ok,
                    NpcPaymentStatus.OutOfReach => BellatraClanStatus.OutOfReach,
                    NpcPaymentStatus.TooMuchGold => BellatraClanStatus.TooMuchGold,
                    _ => BellatraClanStatus.Busy
                },
                static failure => failure == DurableFailure.Busy ? BellatraClanStatus.Busy : BellatraClanStatus.Unavailable,
                async (s, prepared) =>
                {
                    try
                    {
                        return await repository.RedeemAsync(clanId, amount, s.Stamp(prepared.Snapshot!), _clock.GetUtcNow().ToUnixTimeSeconds())
                            ? null
                            : DurableFailure.StorageFailed;
                    }
                    catch (DbException)
                    {
                        return DurableFailure.StorageFailed;
                    }
                });
            if (status == BellatraClanStatus.Ok)
            {
                _balances[clanId] -= amount;
            }

            return status;
        }
        catch (DbException)
        {
            return BellatraClanStatus.Unavailable;
        }
        catch (WorldBusyException)
        {
            return BellatraClanStatus.Busy;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Under the gate.
    private async Task<long> BalanceAsync(long clanId)
    {
        if (!_balances.TryGetValue(clanId, out var balance))
        {
            balance = await repository.BalanceAsync(clanId);
            _balances[clanId] = balance;
        }

        return balance;
    }

    // Under the gate: the day in force, rolled to today with yesterday's best clan operating (not while the clans are
    // not loaded, when nobody operates the new day until they are).
    private async Task<BellatraState> StateAsync()
    {
        var state = _state ?? await repository.LoadStateAsync();
        var today = BellatraDays.Of(_clock.GetLocalNow());
        if (state.Day != today)
        {
            if (!clans.Available)
            {
                _state = state;
                return state with { OperatingClanId = 0 };
            }

            var best = state.Day == BellatraDays.Before(today)
                ? (await repository.TopAsync(state.Day, Contracts.Social.BellatraLimits.TopClans)).FirstOrDefault(clan => clans.NameOf(clan.ClanId) is not null)
                : null;
            state = new BellatraState(today, best?.ClanId ?? 0);
            await repository.SaveStateAsync(state);
        }

        _state = state;
        return state;
    }
}
