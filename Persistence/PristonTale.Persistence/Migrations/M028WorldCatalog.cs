using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The world catalog as the game's domain: items and their definitions (the regular one and its quest and named-unique
// variants), monsters with their drop lines, NPCs (one per definition file), fields keyed by their stage, and every
// player-facing text in its own table per language (English is required, other languages are optional translations).
// The catalog tables are rebuilt empty: import again afterwards. Player tables lose the columns nothing reads.
[Migration(28)]
public sealed class M028WorldCatalog : Migration
{
    private static readonly string[] Elements =
        ["bionic", "earth", "fire", "ice", "lightning", "poison", "water", "wind"];

    private static readonly string[] DroppedTables =
    [
        "field_fixed_npcs", "field_boss_spawns", "field_monster_spawns", "field_spawn_points", "fields",
        "npc_shop_stock", "npc_services", "npc_dialog_lines", "npc_labels", "npcs",
        "monster_drops_plus", "monster_drop_items", "monster_drops", "monster_labels", "monsters",
        "item_labels", "item_template_special", "item_template_job_random", "item_templates", "item_sets", "mailboxes"
    ];

    public override void Up()
    {
        foreach (var table in DroppedTables)
        {
            Delete.Table(table);
        }

        Delete.FromTable("world_entity_variants").AllRows();
        Delete.FromTable("world_import_unresolved_refs").AllRows();
        // Both providers drop a plain column in place (SQLite from 3.35).
        Execute.Sql("ALTER TABLE warehouses DROP COLUMN buy_area_count");
        Execute.Sql("ALTER TABLE characters DROP COLUMN derived_stats");

        Create.Table("languages")
            .WithColumn("code").AsString(16).PrimaryKey()
            .WithColumn("name").AsString(64).NotNullable();
        Insert.IntoTable("languages").Row(new { code = Abstractions.WorldData.CatalogLanguages.English, name = "English" });

        CreateItems();
        CreateMonsters();
        CreateNpcs();
        CreateFields();
    }

    public override void Down() =>
        throw new NotSupportedException("M028WorldCatalog rebuilds the world catalog; restore a backup to go back.");

    private void CreateItems()
    {
        Create.Table("item_sets")
            .WithColumn("set_code").AsInt32().PrimaryKey()
            .WithColumn("pieces").AsInt32().NotNullable()
            .WithColumn("life").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("block").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("life_regen").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("mana_regen").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("stamina_regen").AsFloat().NotNullable().WithDefaultValue(0);

        Create.Table("items")
            .WithColumn("code").AsString(16).PrimaryKey()
            .WithColumn("kind").AsInt32().NotNullable()
            .WithColumn("grid_width").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("grid_height").AsInt32().NotNullable().WithDefaultValue(1);

        var table = Create.Table("item_definitions")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("item_code").AsString(16).NotNullable().ForeignKey("items", "code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("variant").AsInt16().NotNullable()
            .WithColumn("variant_ordinal").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("unique_item").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("spec_class").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("durability_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("durability_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("price").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_min_low").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_min_high").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_max_low").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_max_high").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_rating_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_rating_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("critical").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("defence_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("defence_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("block_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("block_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("move_speed_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("move_speed_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("life_regen_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("life_regen_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("mana_regen_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("mana_regen_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("stamina_regen_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("stamina_regen_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("add_life_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_life_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_mana_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_mana_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_stamina_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_stamina_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_strength").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_spirit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_talent").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_agility").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_health").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_life_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_life_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_mana_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_mana_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_stamina_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_stamina_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("shooting_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_speed").AsInt32().NotNullable().WithDefaultValue(0);

        foreach (var element in Elements)
        {
            table = table
                .WithColumn($"res_{element}_min").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn($"res_{element}_max").AsInt32().NotNullable().WithDefaultValue(0);
        }

        table
            .WithColumn("max_stack").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("potion_space").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("magic_mastery_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("magic_mastery_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("glow_r").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("glow_g").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("glow_b").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("glow_a").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("glow_blink").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("glow_scale").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("display_effect").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("daily_drop_limit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("set_code").AsInt32().Nullable().ForeignKey("item_sets", "set_code").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("set_pieces").AsInt32().NotNullable().WithDefaultValue(0);
        Create.UniqueConstraint("item_definitions_variant_uq").OnTable("item_definitions").Columns("item_code", "variant", "variant_ordinal");

        Create.Table("item_spec_random_classes")
            .WithColumn("definition_id").AsInt64().NotNullable().PrimaryKey()
                .ForeignKey("item_definitions", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("class").AsInt64().NotNullable();

        Create.Table("item_spec_bonuses")
            .WithColumn("definition_id").AsInt64().NotNullable().PrimaryKey()
                .ForeignKey("item_definitions", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("bonus").AsInt16().NotNullable().PrimaryKey()
            .WithColumn("min_value").AsFloat().NotNullable()
            .WithColumn("max_value").AsFloat().NotNullable();

        CreateTexts("item_texts", "definition_id", "item_definitions", longOwner: true);
    }

    private void CreateMonsters()
    {
        var table = Create.Table("monsters")
            .WithColumn("id").AsInt32().PrimaryKey()
            .WithColumn("key").AsString(128).NotNullable().Unique()
            .WithColumn("model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("hp").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_speed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("accuracy").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("defence").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("block_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("stun_pct").AsInt32().NotNullable().WithDefaultValue(100)
            .WithColumn("sight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_damage_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_damage_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_distance").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_rating_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_curse_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_speed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("iq").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("group_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("group_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("exp").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("active_hour").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("nature").AsInt16().NotNullable().WithDefaultValue(0x80)
            .WithColumn("brood").AsInt16().NotNullable().WithDefaultValue(0)
            .WithColumn("class").AsInt16().NotNullable().WithDefaultValue(0)
            .WithColumn("hostile").AsBoolean().NotNullable().WithDefaultValue(true);

        foreach (var element in Elements)
        {
            table = table.WithColumn($"res_{element}").AsInt32().NotNullable().WithDefaultValue(0);
        }

        table
            .WithColumn("res_magic").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("size_level").AsInt32().NotNullable().WithDefaultValue(-1)
            .WithColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1)
            .WithColumn("portrait_offset_y").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("portrait_offset_z").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("quest_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("event_item").AsString(16).Nullable().ForeignKey("monsters_event_item_fk", "items", "code").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("potion_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("potion_percent").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_type").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("special_attack_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("event_model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("event_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("event_info").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("max_drops_per_kill").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("drops_seen_by_all").AsBoolean().NotNullable().WithDefaultValue(false);

        CreateTexts("monster_texts", "monster_id", "monsters", longOwner: false);

        Create.Table("monster_drop_lines")
            .WithColumn("monster_id").AsInt32().NotNullable().PrimaryKey()
                .ForeignKey("monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("line").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("weight").AsInt32().NotNullable()
            .WithColumn("kind").AsInt32().NotNullable()
            .WithColumn("gold_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_max").AsInt32().NotNullable().WithDefaultValue(0);

        // A composite foreign key: SQLite takes it only in the table definition, so the table is plain SQL both providers read.
        Execute.Sql(
            """
            CREATE TABLE monster_drop_line_items (
                monster_id integer NOT NULL,
                line integer NOT NULL,
                ordinal integer NOT NULL,
                item_code varchar(16) NULL REFERENCES items (code) ON DELETE SET NULL,
                PRIMARY KEY (monster_id, line, ordinal),
                FOREIGN KEY (monster_id, line) REFERENCES monster_drop_lines (monster_id, line) ON DELETE CASCADE)
            """);

        Create.Table("monster_bonus_drops")
            .WithColumn("monster_id").AsInt32().NotNullable().PrimaryKey()
                .ForeignKey("monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("chance").AsInt32().NotNullable()
            .WithColumn("item_code").AsString(16).Nullable().ForeignKey("monster_bonus_drops_item_fk", "items", "code").OnDelete(System.Data.Rule.SetNull);
    }

    private void CreateNpcs()
    {
        Create.Table("npcs")
            .WithColumn("id").AsInt32().PrimaryKey()
            .WithColumn("key").AsString(128).NotNullable().Unique()
            .WithColumn("model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("hostile").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("size_level").AsInt32().NotNullable().WithDefaultValue(-1)
            .WithColumn("move_range").AsInt32().NotNullable().WithDefaultValue(64)
            .WithColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1)
            .WithColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("portrait_offset_y").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("portrait_offset_z").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("appear_slot").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("appear_cycle").AsInt32().NotNullable().WithDefaultValue(0);

        CreateTexts("npc_texts", "npc_id", "npcs", longOwner: false);

        Create.Table("npc_dialog_lines")
            .WithColumn("npc_id").AsInt32().NotNullable().PrimaryKey()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("language").AsString(16).NotNullable().PrimaryKey()
                .ForeignKey("npc_dialog_lines_language_fk", "languages", "code")
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("text").AsString().NotNullable();

        Create.Table("npc_services")
            .WithColumn("npc_id").AsInt32().NotNullable().PrimaryKey()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("kind").AsInt32().NotNullable()
            .WithColumn("value").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("value2").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("text").AsString().NotNullable().WithDefaultValue("");

        Create.Table("npc_shop_items")
            .WithColumn("npc_id").AsInt32().NotNullable().PrimaryKey()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("tab").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("item_code").AsString(16).NotNullable().ForeignKey("npc_shop_items_item_fk", "items", "code").OnDelete(System.Data.Rule.Cascade);
    }

    private void CreateFields()
    {
        Create.Table("fields")
            .WithColumn("stage").AsString(64).PrimaryKey()
            .WithColumn("field_code").AsInt32().Nullable().Unique()
            .WithColumn("max_concurrent_monsters").AsInt32().NotNullable().WithDefaultValue(10)
            .WithColumn("max_per_spawn_point").AsInt32().NotNullable().WithDefaultValue(3)
            .WithColumn("spawn_interval").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("per_point_cooldown_ms").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("field_spawn_points")
            .WithColumn("stage").AsString(64).NotNullable().PrimaryKey().ForeignKey("fields", "stage").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("point_index").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("active").AsBoolean().NotNullable()
            .WithColumn("x").AsInt32().NotNullable()
            .WithColumn("z").AsInt32().NotNullable();

        Create.Table("field_monster_spawns")
            .WithColumn("stage").AsString(64).NotNullable().PrimaryKey().ForeignKey("fields", "stage").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("monster_id").AsInt32().NotNullable().ForeignKey("field_monster_spawns_monster_fk", "monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("weight").AsInt32().NotNullable();

        Create.Table("field_bosses")
            .WithColumn("stage").AsString(64).NotNullable().PrimaryKey().ForeignKey("fields", "stage").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("monster_id").AsInt32().NotNullable().ForeignKey("field_bosses_monster_fk", "monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("escort_monster_id").AsInt32().Nullable().ForeignKey("field_bosses_escort_fk", "monsters", "id").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("escort_count").AsInt32().NotNullable().WithDefaultValue(0);

        Execute.Sql(
            """
            CREATE TABLE field_boss_hours (
                stage varchar(64) NOT NULL,
                boss_ordinal integer NOT NULL,
                hour integer NOT NULL,
                PRIMARY KEY (stage, boss_ordinal, hour),
                FOREIGN KEY (stage, boss_ordinal) REFERENCES field_bosses (stage, ordinal) ON DELETE CASCADE)
            """);

        Create.Table("field_npcs")
            .WithColumn("stage").AsString(64).NotNullable().PrimaryKey().ForeignKey("fields", "stage").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("record_index").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("npc_id").AsInt32().NotNullable().ForeignKey("field_npcs_npc_fk", "npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("x").AsInt32().NotNullable()
            .WithColumn("y").AsInt32().NotNullable()
            .WithColumn("z").AsInt32().NotNullable()
            .WithColumn("angle").AsInt32().NotNullable()
            .WithColumn("quest_trigger").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("size_level").AsInt32().NotNullable().WithDefaultValue(0);
    }

    // The name of an entity in one language; every entity has its English name.
    private void CreateTexts(string table, string ownerColumn, string ownerTable, bool longOwner)
    {
        var owner = Create.Table(table).WithColumn(ownerColumn);
        (longOwner ? owner.AsInt64() : owner.AsInt32()).NotNullable().PrimaryKey().ForeignKey(ownerTable, "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("language").AsString(16).NotNullable().PrimaryKey().ForeignKey($"{table}_language_fk", "languages", "code")
            .WithColumn("name").AsString(128).NotNullable();
    }
}
