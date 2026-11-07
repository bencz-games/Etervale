using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The clan money ledger: the clan, the kind (TreasuryKind: 0 a Bellatra income, 1 castle tax, 2 a castle withdrawal, 3 a
// Bellatra redemption), the amount (negative out), the character that received it and when.
[Migration(39)]
public sealed class M039CastleTreasury : Migration
{
    public override void Up()
    {
        Create.Table("castle_treasury")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("clan_id").AsInt64().NotNullable()
            .WithColumn("kind").AsInt16().NotNullable()
            .WithColumn("amount").AsInt64().NotNullable()
            .WithColumn("character_id").AsInt64().Nullable()
            .WithColumn("at").AsInt64().NotNullable();
        Create.Index("ix_castle_treasury_clan").OnTable("castle_treasury").OnColumn("clan_id");
    }

    public override void Down() => Delete.Table("castle_treasury");
}
