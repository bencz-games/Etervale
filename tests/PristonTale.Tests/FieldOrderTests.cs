using System.Numerics;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class FieldOrderTests
{
    private const int ActionField = 39;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the fields after the action field keep the authored codes, and the Teleport Core reaches them", CodesAsync);
        yield return ("a warp with several exits lands where the client came out and asks only its own level", ExitsAsync);
        yield return ("wings and teleporters ask a field's travel level, walking in its own", TravelLevelAsync);
        yield return ("stored characters, logged transfers and imported fields follow the fields that moved up", MigrationAsync);
    }

    private static Task CodesAsync()
    {
        var world = new WorldCatalog();
        string[] moved = ["dun-7", "dun-8", "dun-6", "dun-9", "mine-1", "sod-2"];
        for (var index = 0; index < moved.Length; index++)
        {
            var field = world.GetField(moved[index]);
            Assert.True(field.FieldCode == ActionField + 1 + index, $"{field.Id} has code {field.FieldCode}");
        }

        foreach (var (row, id) in new[] { (20, "dun-6"), (30, "dun-7"), (31, "dun-8"), (32, "dun-9"), (33, "mine-1") })
        {
            Assert.True(TeleportCoreDestinations.TryGet(row, out var destination) && world.GetFieldByCode(destination.FieldCode).Id == id,
                $"Teleport Core row {row} does not lead to {id}");
        }

        var dungeon = world.GetField("dun-6");
        Assert.True(dungeon.TravelLevel == 80 && dungeon.LimitLevel == 0, "Cursed Temple 3F's travel level is not 80 with free walking");
        return Task.CompletedTask;
    }

    private static Task ExitsAsync()
    {
        var gate = new FieldWarpGate(100, 100, 0, 48, 0, "second", 1000, 1000, 5, 20, 0)
        {
            MoreExits = [new FieldVector(3000, 7, 3000), new FieldVector(5000, 9, 5000)]
        };
        var first = Field("first", 1, 0) with { WarpGates = [gate] };
        var second = Field("second", 2, 90);
        var origin = new Vector3(100, 0, 100);
        Assert.True(FieldTransition.TryResolve(first, second, origin, new Vector3(3010, 0, 2990), 20, out var arrival)
            && arrival.IsWarp && arrival.Position == new Vector3(3000, 7, 3000), $"the warp came out at {arrival.Position}");
        Assert.True(FieldTransition.TryResolve(first, second, origin, new Vector3(-50000, 0, -50000), 20, out arrival)
            && arrival.Position == new Vector3(1000, 5, 1000), "a far report did not come out at the nearest exit");
        Assert.True(!FieldTransition.TryResolve(first, second, origin, new Vector3(1000, 0, 1000), 19, out _), "the warp ignored its own level");

        var walk = Field("walk", 3, 0) with { Gates = [new FieldGate("second", 100, 100, 0)] };
        Assert.True(!FieldTransition.TryResolve(walk, second, origin, origin, 89, out _)
            && FieldTransition.TryResolve(walk, second, origin, origin, 90, out arrival) && !arrival.IsWarp,
            "walking into a field did not ask its level");
        return Task.CompletedTask;
    }

    private static Task TravelLevelAsync()
    {
        var plain = Field("plain", 1, 55);
        var travelled = Field("travelled", 2, 0) with { TravelLevel = 40 };
        Assert.True(plain.TravelLevel == 55 && travelled.TravelLevel == 40 && travelled.LimitLevel == 0,
            "a field without its own travel level does not travel at its walking level");
        return Task.CompletedTask;
    }

    private static async Task MigrationAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-field-order-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "fields.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(34);
            }

            var account = await provider.GetRequiredService<IAccountRepository>().CreateAsync("mover", "hash");
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var classes = new CharacterClassRegistry();
            var tower = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, "Climber", 0, PlayerClass.Fighter, 3));
            var temple = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, "Pilgrim", 1, PlayerClass.Fighter, 3));
            Assert.True(await characters.UpdateAsync(tower with { FieldId = 39 }) && await characters.UpdateAsync(temple with { FieldId = 38 }),
                "the fixture characters were not stored");

            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await connections.OpenAsync();
            await connection.ExecuteAsync("INSERT INTO fields (stage, field_code) VALUES ('dun-7', 39), ('mine-1', 43), ('sod-2', 44)");
            await connection.ExecuteAsync(
                "INSERT INTO item_transfers (occurred_at, kind, transfer_id, field_id, amount) VALUES (@At, 1, 'move', 44, 1)",
                new { At = DateTime.UtcNow });

            using (var scope = provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
            }

            var climber = await characters.FindByNameForAccountAsync(account.Id, "Climber");
            var pilgrim = await characters.FindByNameForAccountAsync(account.Id, "Pilgrim");
            Assert.True(climber?.FieldId == 40 && pilgrim?.FieldId == 38, $"characters stand in {climber?.FieldId} and {pilgrim?.FieldId}");
            var codes = (await connection.QueryAsync<(string Stage, int Code)>("SELECT stage, field_code FROM fields ORDER BY stage")).ToList();
            Assert.True(codes.SequenceEqual([("dun-7", 40), ("mine-1", 44), ("sod-2", 45)]), $"imported fields hold {string.Join(", ", codes)}");
            Assert.True(await connection.ExecuteScalarAsync<int>("SELECT field_id FROM item_transfers WHERE transfer_id = 'move'") == 45,
                "a logged transfer kept its old field");
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

    private static FieldDef Field(string id, int code, int level) =>
        new(id, id, FieldState.Dungeon, new FieldBackdrop("", "", ""), "", []) { FieldCode = code, LimitLevel = level };
}
