using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;

namespace PristonTale.Tests;

internal static class MonsterNameTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a monster is keyed by id and English key, named by its English text with other languages beside it, and spawned and dropped by id", BaselineAsync);
    }

    private static async Task BaselineAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-monster-names-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));

        try
        {
            await using var provider = services.BuildServiceProvider();
            var migrator = provider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync();
            Assert.True(!await provider.GetRequiredService<IWorldDataRepository>().HasCatalogTextsAsync(CatalogLanguages.BrazilianPortuguese),
                "an empty catalog claimed Portuguese text");

            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            await using (var connection = await connections.OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO monsters (id, key, model, max_drops_per_kill) VALUES (196, 'HOPY', @Model, 2)",
                    new { Model = @"char\monster\Hopy\hopy.INI" });
                await connection.ExecuteAsync("INSERT INTO monsters (id, key) VALUES (197, 'Nameless')");
                await connection.ExecuteAsync("INSERT INTO monster_texts (monster_id, language, name) VALUES (196, 'en', 'Hopy'), (196, 'pt-BR', 'Hopi')");
                await connection.ExecuteAsync("INSERT INTO monster_drop_lines (monster_id, line, weight, kind) VALUES (196, 0, 9, 2)");
                await connection.ExecuteAsync("INSERT INTO monster_drop_line_items (monster_id, line, ordinal, item_code) VALUES (196, 0, 0, NULL), (196, 0, 1, NULL)");
                await connection.ExecuteAsync("INSERT INTO fields (stage, field_code) VALUES ('fore-1', 3), ('Slab', NULL)");
                await connection.ExecuteAsync("INSERT INTO field_monster_spawns (stage, ordinal, monster_id, weight) VALUES ('fore-1', 0, 196, 35)");
            }

            await migrator.MigrateAsync();

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var records = await repository.GetAllMonstersAsync();
            Assert.True(records[0] is { Id: 196, Key: "HOPY", Name: "Hopy", MaxDropsPerKill: 2 } && records[1] is { Id: 197, Name: null },
                "the runtime did not read the English name, or read a name a monster has not");
            var portuguese = await repository.GetAllMonstersAsync(CatalogLanguages.BrazilianPortuguese);
            Assert.True(await repository.HasCatalogTextsAsync(CatalogLanguages.BrazilianPortuguese), "a stored Portuguese name was not found");
            Assert.True(portuguese[0].Name == "Hopi" && portuguese[1].Name == null, "the catalog language did not select pt-BR with English fallback");
            Assert.True(records[0].DropLines.Single() is { Weight: 9, Items.Count: 2 }, "the drop line or its items were not read");

            var definitions = WorldDataFactory.ToMonsterDefinitions(records);
            Assert.True(definitions[0] is { Code: 196, Key: "HOPY", Name: "Hopy" } && definitions[1].Name == "Nameless",
                "the runtime code is not the id, the key or name differs, or a monster without a name does not show its key");
            // A line of two items the catalog lacks weighs nothing, so its weight is never rolled.
            Assert.True(!definitions[0].Drops.MainEntries.Any() && definitions[0].Drops.MainWeightTotal == 0,
                "a line of unknown items kept its weight");
            var fields = await repository.GetAllFieldSpawnsAsync();
            Assert.True(fields.Single().Stage == "fore-1", "a stage no field uses reached the runtime");
            var spawns = WorldDataFactory.ToFieldSpawnData(fields.Single());
            Assert.True(spawns.SpawnTable.Single().MonsterCode == 196, "the spawn does not name the monster by its id");

            await using var check = await connections.OpenAsync();
            Assert.True(await check.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0, "migration broke foreign keys");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
