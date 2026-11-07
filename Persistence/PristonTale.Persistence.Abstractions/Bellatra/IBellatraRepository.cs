using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Bellatra;

// Bellatra's day (the server's local date as yyyymmdd) and the clan operating it that day.
public sealed record BellatraState(int Day, long OperatingClanId)
{
    public static BellatraState None { get; } = new(0, 0);
}

public sealed record BellatraClanPoints(long ClanId, long Points);

// Bellatra's clans: each day's points per clan, the operating clan and each clan's income balance, with the movements of
// the clan money ledger (castle_treasury, TreasuryKind.Bellatra and BellatraRedemption).
public interface IBellatraRepository
{
    Task<BellatraState> LoadStateAsync(CancellationToken ct = default);

    Task SaveStateAsync(BellatraState state, CancellationToken ct = default);

    // A day's clans by points, best first, at most count.
    Task<IReadOnlyList<BellatraClanPoints>> TopAsync(int day, int count, CancellationToken ct = default);

    Task<long> BalanceAsync(long clanId, CancellationToken ct = default);

    // A paid prize: its entrant's clan (none: 0) gains the points for the day, the operating clan (none: 0) the prize's
    // tax as income with its movement; in one write.
    Task RecordAsync(int day, long clanId, long points, long operatingClanId, long income, long atSeconds, CancellationToken ct = default);

    // The chief takes amount out of its clan's balance, stored with its character and the movement; false when the
    // character is stale or the balance is short.
    Task<bool> RedeemAsync(long clanId, long amount, CharacterRecord character, long atSeconds, CancellationToken ct = default);
}
