using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Clans live on the game server: the clan (its unique name by name_key), one row per member (a character is in one clan
// at most; deleting the character or the clan removes the row) and the join cooldown of the members of dissolved clans.
[Migration(37)]
public sealed class M037Clans : Migration
{
    public override void Up()
    {
        Create.Table("clans")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("name").AsString(32).NotNullable()
            .WithColumn("name_key").AsString(32).NotNullable().Unique("ux_clans_name_key")
            .WithColumn("state").AsInt16().NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("deadline_at").AsInt64().NotNullable()
            .WithColumn("intro").AsString(256).NotNullable().WithDefaultValue("")
            .WithColumn("mark_version").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("clan_members")
            .WithColumn("character_id").AsInt64().PrimaryKey()
                .ForeignKey("clan_members_character_fk", "characters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("clan_id").AsInt64().NotNullable()
                .ForeignKey("clan_members_clan_fk", "clans", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("rank").AsInt16().NotNullable()
            .WithColumn("joined_at").AsInt64().NotNullable();
        Create.Index("ix_clan_members_clan").OnTable("clan_members").OnColumn("clan_id");

        Create.Table("clan_cooldowns")
            .WithColumn("character_id").AsInt64().PrimaryKey()
                .ForeignKey("clan_cooldowns_character_fk", "characters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("until_at").AsInt64().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("clan_cooldowns");
        Delete.Table("clan_members");
        Delete.Table("clans");
    }
}
