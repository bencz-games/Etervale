using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Bellatra's clans: the day and the clan operating it (one row), each day's points per clan, and each clan's income
// balance; the clan money ledger (castle_treasury) takes kind 0 for an income and 3 for a redemption.
[Migration(40)]
public sealed class M040Bellatra : Migration
{
    public override void Up()
    {
        Create.Table("bellatra_state")
            .WithColumn("id").AsInt32().PrimaryKey()
            .WithColumn("day").AsInt32().NotNullable()
            .WithColumn("operating_clan_id").AsInt64().NotNullable();
        Create.Table("bellatra_points")
            .WithColumn("day").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("clan_id").AsInt64().NotNullable().PrimaryKey()
            .WithColumn("points").AsInt64().NotNullable();
        Create.Table("bellatra_balances")
            .WithColumn("clan_id").AsInt64().PrimaryKey()
            .WithColumn("balance").AsInt64().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("bellatra_balances");
        Delete.Table("bellatra_points");
        Delete.Table("bellatra_state");
    }
}
