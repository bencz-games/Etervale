using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Bless Castle: one row (id 1) holds the owner clan (0 for none), the tax in force and the next one, the owner's setup
// (the tower elements as digits, the mercenaries of each kind, the clan skill chosen and the one in force), the soul
// fountain's potions and the treasury.
[Migration(38)]
public sealed class M038Castle : Migration
{
    public override void Up()
    {
        Create.Table("castle")
            .WithColumn("id").AsInt32().PrimaryKey()
            .WithColumn("owner_clan_id").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("tax").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("next_tax").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("towers").AsString(16).NotNullable().WithDefaultValue("")
            .WithColumn("mercenary_a").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("mercenary_b").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("mercenary_c").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("clan_skill").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_in_force").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("fountain_potions").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("treasury").AsInt64().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Table("castle");
    }
}
