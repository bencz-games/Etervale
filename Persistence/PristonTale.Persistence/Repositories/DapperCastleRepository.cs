using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Castle;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperCastleRepository(IDbConnectionFactory connectionFactory) : ICastleRepository
{
    private const string Upsert =
        """
        INSERT INTO castle (id, owner_clan_id, tax, next_tax, towers, mercenary_a, mercenary_b, mercenary_c, clan_skill, skill_in_force,
            fountain_potions, treasury)
        VALUES (1, @OwnerClanId, @Tax, @NextTax, @Towers, @MercenaryA, @MercenaryB, @MercenaryC, @ClanSkill, @SkillInForce, @FountainPotions, @Treasury)
        ON CONFLICT (id) DO UPDATE SET owner_clan_id = excluded.owner_clan_id, tax = excluded.tax, next_tax = excluded.next_tax,
            towers = excluded.towers, mercenary_a = excluded.mercenary_a, mercenary_b = excluded.mercenary_b, mercenary_c = excluded.mercenary_c,
            clan_skill = excluded.clan_skill, skill_in_force = excluded.skill_in_force, fountain_potions = excluded.fountain_potions,
            treasury = excluded.treasury
        """;

    public async Task<CastleRecord> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long OwnerClanId, long Tax, long NextTax, string Towers, long MercenaryA, long MercenaryB, long MercenaryC,
            long ClanSkill, long SkillInForce, long FountainPotions, long Treasury)>(new CommandDefinition(
            """
            SELECT owner_clan_id, tax, next_tax, towers, mercenary_a, mercenary_b, mercenary_c, clan_skill, skill_in_force, fountain_potions, treasury
            FROM castle WHERE id = 1
            """, cancellationToken: ct));
        return rows.Select(static row => new CastleRecord(row.OwnerClanId, (int)row.Tax, (int)row.NextTax, row.Towers, (int)row.MercenaryA,
            (int)row.MercenaryB, (int)row.MercenaryC, (int)row.ClanSkill, (int)row.SkillInForce, (int)row.FountainPotions, row.Treasury))
            .FirstOrDefault() ?? CastleRecord.Empty;
    }

    public async Task SaveAsync(CastleRecord castle, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Upsert, castle, cancellationToken: ct));
    }
}
