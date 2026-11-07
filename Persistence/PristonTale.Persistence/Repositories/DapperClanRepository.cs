using System.Data.Common;
using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Clans;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperClanRepository(IDbConnectionFactory connectionFactory) : IClanRepository
{
    public async Task<(IReadOnlyList<ClanRecord> Clans, IReadOnlyList<ClanMemberRecord> Members)> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var clans = await connection.QueryAsync<(long Id, string Name, long State, long CreatedAt, long DeadlineAt, string Intro, long MarkVersion)>(
            new CommandDefinition("SELECT id, name, state, created_at, deadline_at, intro, mark_version FROM clans ORDER BY id", cancellationToken: ct));
        var members = await connection.QueryAsync<(long ClanId, long CharacterId, long Rank, long JoinedAt, string Name, long JobCode, long Level, long AccountId)>(
            new CommandDefinition(
                """
                SELECT m.clan_id, m.character_id, m.rank, m.joined_at, c.name, c.job_code, c.level, c.account_id
                FROM clan_members m JOIN characters c ON c.id = m.character_id ORDER BY m.clan_id, m.joined_at
                """, cancellationToken: ct));
        return (clans.Select(static row => new ClanRecord(row.Id, row.Name, (short)row.State, row.CreatedAt, row.DeadlineAt, row.Intro, (int)row.MarkVersion))
                .ToArray(),
            members.Select(static row => new ClanMemberRecord(row.ClanId, row.CharacterId, (short)row.Rank, row.JoinedAt, row.Name, (int)row.JobCode,
                (int)row.Level, row.AccountId)).ToArray());
    }

    public async Task<long> FoundAsync(ClanRecord clan, ClanMemberRecord chief, CharacterRecord payment, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var taken = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM clans WHERE name_key = @key", new { key = Key(clan.Name) }, transaction, cancellationToken: ct));
        if (taken > 0 || !await PayAsync(connection, transaction, payment, ct))
        {
            await transaction.RollbackAsync(ct);
            return 0;
        }

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO clans (name, name_key, state, created_at, deadline_at, intro, mark_version)
            VALUES (@Name, @key, @State, @CreatedAt, @DeadlineAt, @Intro, @MarkVersion) RETURNING id
            """,
            new { clan.Name, key = Key(clan.Name), clan.State, clan.CreatedAt, clan.DeadlineAt, clan.Intro, clan.MarkVersion }, transaction, cancellationToken: ct));
        await InsertMemberAsync(connection, transaction, chief with { ClanId = id }, ct);
        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task<bool> AddMemberAsync(ClanMemberRecord member, short state, long deadlineAt, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var inClan = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM clan_members WHERE character_id = @CharacterId", new { member.CharacterId }, transaction, cancellationToken: ct));
        if (inClan > 0 || !await SetStateAsync(connection, transaction, member.ClanId, state, deadlineAt, ct))
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await InsertMemberAsync(connection, transaction, member, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(long clanId, long characterId, short state, long deadlineAt, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM clan_members WHERE clan_id = @clanId AND character_id = @characterId", new { clanId, characterId }, transaction, cancellationToken: ct));
        if (removed == 0 || !await SetStateAsync(connection, transaction, clanId, state, deadlineAt, ct))
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> SetRanksAsync(long clanId, IReadOnlyList<ClanRankChange> changes, CharacterRecord? payment = null, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (payment is not null && !await PayAsync(connection, transaction, payment, ct))
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        foreach (var change in changes)
        {
            if (await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE clan_members SET rank = @Rank WHERE clan_id = @clanId AND character_id = @CharacterId",
                    new { clanId, change.CharacterId, change.Rank }, transaction, cancellationToken: ct)) == 0)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DisbandAsync(long clanId, IReadOnlyList<long> members, long cooldownUntil, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM clan_members WHERE clan_id = @clanId", new { clanId }, transaction, cancellationToken: ct));
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM clans WHERE id = @clanId", new { clanId }, transaction, cancellationToken: ct));
        if (deleted == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        foreach (var characterId in members)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO clan_cooldowns (character_id, until_at) VALUES (@characterId, @cooldownUntil)
                ON CONFLICT (character_id) DO UPDATE SET until_at = excluded.until_at
                """, new { characterId, cooldownUntil }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<long> CooldownAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT until_at FROM clan_cooldowns WHERE character_id = @characterId", new { characterId }, cancellationToken: ct)) ?? 0;
    }

    public async Task<bool> IsMemberAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM clan_members WHERE character_id = @characterId", new { characterId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SetMarkVersionAsync(long clanId, int version, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE clans SET mark_version = @version WHERE id = @clanId", new { clanId, version }, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyDictionary<long, string>> ClanNamesAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long CharacterId, string Name)>(new CommandDefinition(
            """
            SELECT m.character_id, k.name FROM clan_members m JOIN clans k ON k.id = m.clan_id
            JOIN characters c ON c.id = m.character_id WHERE c.account_id = @accountId
            """, new { accountId }, cancellationToken: ct));
        return rows.ToDictionary(static row => row.CharacterId, static row => row.Name);
    }

    private static string Key(string name) => name.Trim().ToLowerInvariant();

    private static async Task<bool> PayAsync(DbConnection connection, DbTransaction transaction, CharacterRecord payment, CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, payment, transaction, cancellationToken: ct)) > 0;

    private static async Task<bool> SetStateAsync(DbConnection connection, DbTransaction transaction, long clanId, short state, long deadlineAt,
        CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE clans SET state = @state, deadline_at = @deadlineAt WHERE id = @clanId", new { clanId, state, deadlineAt }, transaction,
            cancellationToken: ct)) > 0;

    private static Task InsertMemberAsync(DbConnection connection, DbTransaction transaction, ClanMemberRecord member, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO clan_members (character_id, clan_id, rank, joined_at) VALUES (@CharacterId, @ClanId, @Rank, @JoinedAt)",
            new { member.CharacterId, member.ClanId, member.Rank, member.JoinedAt }, transaction, cancellationToken: ct));
}
