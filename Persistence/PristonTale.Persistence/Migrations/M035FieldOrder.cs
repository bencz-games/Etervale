using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The action field took code 39, so the Endless Tower floors, the Cursed Temple's third floor, the Ice Mine and the second
// SoD stage moved up one (39-44 became 40-45). Saved characters, logged transfers and the imported fields follow, the
// highest first so the fields' unique code never collides.
[Migration(35)]
public sealed class M035FieldOrder : Migration
{
    private const int FirstMoved = 39;
    private const int LastMoved = 44;

    private static readonly (string Table, string Column)[] Columns =
    [
        ("characters", "field_id"),
        ("item_transfers", "field_id"),
        ("fields", "field_code")
    ];

    public override void Up()
    {
        for (var code = LastMoved; code >= FirstMoved; code--)
        {
            Move(code, code + 1);
        }
    }

    public override void Down()
    {
        for (var code = FirstMoved + 1; code <= LastMoved + 1; code++)
        {
            Move(code, code - 1);
        }
    }

    private void Move(int from, int to)
    {
        foreach (var (table, column) in Columns)
        {
            Execute.Sql($"UPDATE {table} SET {column} = {to} WHERE {column} = {from}");
        }
    }
}
