using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperGameMasterRepository(IDbConnectionFactory connectionFactory) : IGameMasterRepository
{
    public async Task<bool> SetGmLevelAsync(string userId, int level, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET gm_level = @level WHERE user_id = @userId",
            new { userId, level }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task AppendAuditAsync(GmAuditEntry entry, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO gm_audit (account_id, user_id, character_name, gm_level, command, required_level, accepted, result, created_at)
            VALUES (@AccountId, @UserId, @CharacterName, @GmLevel, @Command, @RequiredLevel, @Accepted, @Result, @now)
            """,
            new
            {
                entry.AccountId,
                entry.UserId,
                entry.CharacterName,
                entry.GmLevel,
                entry.Command,
                entry.RequiredLevel,
                entry.Accepted,
                entry.Result,
                now = DateTime.UtcNow
            }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<GmAuditRecord>> RecentAuditAsync(int limit, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<GmAuditRecord>(new CommandDefinition(
            """
            SELECT id, account_id, user_id, character_name, gm_level, command, required_level, accepted, result, created_at
            FROM gm_audit ORDER BY id DESC LIMIT @limit
            """,
            new { limit = Math.Max(1, limit) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CharacterTransfer> MoveCharacterAsync(string characterName, string targetUserId, int maxSlots, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var account = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT id FROM accounts WHERE user_id = @targetUserId", new { targetUserId }, transaction, cancellationToken: ct));
        if (account is null)
        {
            await transaction.RollbackAsync(ct);
            return CharacterTransfer.NoAccount;
        }

        var character = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT id FROM characters WHERE name = @characterName", new { characterName }, transaction, cancellationToken: ct));
        if (character is null)
        {
            await transaction.RollbackAsync(ct);
            return CharacterTransfer.NoCharacter;
        }

        var used = await connection.QueryAsync<short>(new CommandDefinition(
            "SELECT slot FROM characters WHERE account_id = @account", new { account }, transaction, cancellationToken: ct));
        if (CharacterSlots.FirstFree(used) is not { } slot || slot >= maxSlots)
        {
            await transaction.RollbackAsync(ct);
            return CharacterTransfer.NoFreeSlot;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE characters SET account_id = @account, slot = @slot WHERE id = @character",
            new { account, slot, character }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return CharacterTransfer.Moved;
    }
}
