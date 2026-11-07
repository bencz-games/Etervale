using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Bellatra;
using PristonTale.Persistence.Abstractions.Castle;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperBellatraRepository(IDbConnectionFactory connectionFactory) : IBellatraRepository
{
    public async Task<BellatraState> LoadStateAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long Day, long OperatingClanId)>(new CommandDefinition(
            "SELECT day, operating_clan_id FROM bellatra_state WHERE id = 1", cancellationToken: ct));
        return rows.Select(static row => new BellatraState((int)row.Day, row.OperatingClanId)).FirstOrDefault() ?? BellatraState.None;
    }

    public async Task SaveStateAsync(BellatraState state, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bellatra_state (id, day, operating_clan_id) VALUES (1, @Day, @OperatingClanId)
            ON CONFLICT (id) DO UPDATE SET day = excluded.day, operating_clan_id = excluded.operating_clan_id
            """, state, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<BellatraClanPoints>> TopAsync(int day, int count, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long ClanId, long Points)>(new CommandDefinition(
            "SELECT clan_id, points FROM bellatra_points WHERE day = @Day ORDER BY points DESC, clan_id LIMIT @Count",
            new { Day = day, Count = count }, cancellationToken: ct));
        return [.. rows.Select(static row => new BellatraClanPoints(row.ClanId, row.Points))];
    }

    public async Task<long> BalanceAsync(long clanId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT balance FROM bellatra_balances WHERE clan_id = @ClanId", new { ClanId = clanId }, cancellationToken: ct)) ?? 0;
    }

    public async Task RecordAsync(int day, long clanId, long points, long operatingClanId, long income, long atSeconds, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (clanId != 0 && points > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bellatra_points (day, clan_id, points) VALUES (@Day, @ClanId, @Points)
                ON CONFLICT (day, clan_id) DO UPDATE SET points = bellatra_points.points + excluded.points
                """, new { Day = day, ClanId = clanId, Points = points }, transaction, cancellationToken: ct));
        }

        if (operatingClanId != 0 && income > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bellatra_balances (clan_id, balance) VALUES (@ClanId, @Income)
                ON CONFLICT (clan_id) DO UPDATE SET balance = bellatra_balances.balance + excluded.balance
                """, new { ClanId = operatingClanId, Income = income }, transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(DapperCastleRepository.InsertMove,
                new { ClanId = operatingClanId, Kind = (short)TreasuryKind.Bellatra, Amount = income, CharacterId = (long?)null, At = atSeconds }, transaction,
                cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<bool> RedeemAsync(long clanId, long amount, CharacterRecord character, long atSeconds, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await connection.ExecuteAsync(new CommandDefinition("UPDATE bellatra_balances SET balance = balance - @Amount WHERE clan_id = @ClanId AND balance >= @Amount",
                new { ClanId = clanId, Amount = amount }, transaction, cancellationToken: ct)) == 0
            || await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(DapperCastleRepository.InsertMove,
            new { ClanId = clanId, Kind = (short)TreasuryKind.BellatraRedemption, Amount = -amount, CharacterId = (long?)character.Id, At = atSeconds }, transaction,
            cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return true;
    }
}
