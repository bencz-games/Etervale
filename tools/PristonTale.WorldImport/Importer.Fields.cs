using System.Data.Common;
using Dapper;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

public sealed partial class Importer
{
    // A spawn's actor is the catalog entry of its name; a boss line's master and escort are bossIds' (ParseMonsters).
    private static async Task WriteFieldsAsync(DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedField> fields,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds, IReadOnlyDictionary<string, IdentityRegistry.Entry> bossIds,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> npcIds, ImportReport report, CancellationToken ct)
    {
        foreach (var f in fields)
        {
            var stage = new { Stage = f.AseBase };
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO fields (stage, field_code, max_concurrent_monsters, max_per_spawn_point, spawn_interval, per_point_cooldown_ms)
                VALUES (@Stage, @FieldCode, @MaxConcurrentMonsters, @MaxPerSpawnPoint, @SpawnInterval, @PerPointCooldownMs)
                """,
                new { Stage = f.AseBase, f.FieldCode, f.MaxConcurrentMonsters, f.MaxPerSpawnPoint, f.SpawnInterval, f.PerPointCooldownMs }, tx,
                cancellationToken: ct));
            report.FieldRows++;

            foreach (var (index, state, x, z) in f.SpawnPoints)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_spawn_points (stage, point_index, active, x, z) VALUES (@Stage, @Index, @Active, @X, @Z)",
                    new { stage.Stage, Index = index, Active = state != 0, X = x, Z = z }, tx, cancellationToken: ct));
                report.SpawnPointRows++;
            }

            for (var ordinal = 0; ordinal < f.MonsterSpawns.Count; ordinal++)
            {
                var (monster, weight) = f.MonsterSpawns[ordinal];
                if (!monsterIds.TryGetValue(monster, out var spawned))
                {
                    report.SpawnMonsterRefsDangling++;
                    report.Warnings.Add($"field '{f.AseBase}' spawn names a monster no file defines");
                    await RecordUnresolvedAsync(conn, tx, "field_monster_spawn", f.AseBase, monster, new { Weight = weight }, report, ct);
                    continue;
                }

                report.SpawnMonsterRefsResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO field_monster_spawns (stage, ordinal, monster_id, weight) VALUES (@Stage, @Ordinal, @Monster, @Weight)",
                    new { stage.Stage, Ordinal = ordinal, Monster = spawned.Id, Weight = weight }, tx, cancellationToken: ct));
                report.MonsterSpawnRows++;
            }

            for (var ordinal = 0; ordinal < f.Bosses.Count; ordinal++)
            {
                await WriteBossAsync(conn, tx, f.AseBase, ordinal, f.Bosses[ordinal], bossIds, report, ct);
            }

            foreach (var placement in f.FixedNpcs)
            {
                if (!npcIds.TryGetValue(placement.SourceFile, out var placed))
                {
                    report.FixedNpcRefsDangling++;
                    await RecordUnresolvedAsync(conn, tx, "field_fixed_npc", f.AseBase, placement.SourceFile,
                        new { placement.Index, placement.X, placement.Z, placement.Angle, placement.Y }, report, ct);
                    continue;
                }

                report.FixedNpcRefsResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO field_npcs (stage, record_index, npc_id, x, y, z, angle)
                    VALUES (@Stage, @Index, @Npc, @X, @Y, @Z, @Angle)
                    """,
                    new { stage.Stage, placement.Index, Npc = placed.Id, placement.X, placement.Y, placement.Z, placement.Angle }, tx,
                    cancellationToken: ct));
                report.FixedNpcRows++;
            }
        }
    }

    private static async Task WriteBossAsync(DbConnection conn, DbTransaction tx, string stage, int ordinal, ParsedBoss boss,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds, ImportReport report, CancellationToken ct)
    {
        if (!monsterIds.TryGetValue(boss.MasterName, out var master))
        {
            report.BossMonsterRefsDangling++;
            report.Warnings.Add($"field '{stage}' boss names a monster no file defines");
            await RecordUnresolvedAsync(conn, tx, "field_boss", stage, boss.MasterName, new { boss.SlaveName, boss.SlaveCount, boss.Hours }, report, ct);
            return;
        }

        report.BossMonsterRefsResolved++;
        int? escort = null;
        if (boss.SlaveName is { } slave)
        {
            if (monsterIds.TryGetValue(slave, out var escorted))
            {
                escort = escorted.Id;
            }
            else
            {
                await RecordUnresolvedAsync(conn, tx, "field_boss_escort", stage, slave, new { Boss = master.Key, boss.SlaveCount, boss.Hours }, report, ct);
            }
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO field_bosses (stage, ordinal, monster_id, escort_monster_id, escort_count) VALUES (@Stage, @Ordinal, @Monster, @Escort, @Count)",
            new { Stage = stage, Ordinal = ordinal, Monster = master.Id, Escort = escort, Count = boss.SlaveCount }, tx, cancellationToken: ct));
        report.BossSpawnRows++;
        foreach (var hour in boss.Hours.Distinct())
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO field_boss_hours (stage, boss_ordinal, hour) VALUES (@Stage, @Ordinal, @Hour)",
                new { Stage = stage, Ordinal = ordinal, Hour = hour }, tx, cancellationToken: ct));
        }
    }
}
