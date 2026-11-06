using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Repositories;

// A requested translation falls back to English; a name is null when neither language has text
// (WorldDataFactory shows its code or key).
public sealed class DapperWorldDataRepository(IDbConnectionFactory connectionFactory) : IWorldDataRepository
{
    private sealed record CatalogTextRecord(long Id, string? Name);
    private sealed record LanguageParameters(string Language, string Fallback);

    private static LanguageParameters Languages(string language) => new(CatalogLanguages.Normalize(language), CatalogLanguages.English);

    public async Task<IReadOnlyList<ItemTemplateRecord>> GetAllItemTemplatesAsync(string language = CatalogLanguages.English, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var languages = Languages(language);
        var items = (await connection.QueryAsync<ItemTemplateRecord>(new CommandDefinition(
            """
            SELECT
                d.id, i.code, d.variant, d.variant_ordinal, t.name, i.kind, i.grid_width, i.grid_height,
                d.unique_item, d.spec_class, d.durability_min, d.durability_max, d.weight, d.price,
                d.damage_min_low, d.damage_min_high, d.damage_max_low, d.damage_max_high, d.attack_rating_min, d.attack_rating_max,
                d.critical, d.defence_min, d.defence_max, d.block_min, d.block_max, d.absorb_min, d.absorb_max,
                d.move_speed_min, d.move_speed_max, d.life_regen_min, d.life_regen_max, d.mana_regen_min, d.mana_regen_max,
                d.stamina_regen_min, d.stamina_regen_max, d.add_life_min, d.add_life_max, d.add_mana_min, d.add_mana_max,
                d.add_stamina_min, d.add_stamina_max, d.req_level, d.req_strength, d.req_spirit, d.req_talent, d.req_agility,
                d.req_health, d.restore_life_min, d.restore_life_max, d.restore_mana_min, d.restore_mana_max,
                d.restore_stamina_min, d.restore_stamina_max, d.shooting_range, d.attack_speed,
                d.res_bionic_min, d.res_bionic_max, d.res_earth_min, d.res_earth_max, d.res_fire_min, d.res_fire_max,
                d.res_ice_min, d.res_ice_max, d.res_lightning_min, d.res_lightning_max, d.res_poison_min, d.res_poison_max,
                d.res_water_min, d.res_water_max, d.res_wind_min, d.res_wind_max, d.max_stack, d.potion_space,
                d.magic_mastery_min, d.magic_mastery_max, d.glow_r, d.glow_g, d.glow_b, d.glow_a, d.glow_blink, d.glow_scale,
                d.display_effect, d.daily_drop_limit, d.set_code, d.set_pieces
            FROM item_definitions d
            JOIN items i ON i.code = d.item_code
                LEFT JOIN item_texts t ON t.definition_id = d.id AND t.language = @Language
            ORDER BY i.code, d.variant, d.variant_ordinal
            """, languages, cancellationToken: ct))).ToList();
        if (items.Count == 0)
            return items;
        if (languages.Language != CatalogLanguages.English)
        {
            var fallback = await CatalogTextsAsync(connection, "item_texts", "definition_id", languages.Fallback, ct);
            items = items.Select(item => item with { Name = item.Name ?? fallback.GetValueOrDefault(item.Id) }).ToList();
        }

        var bonuses = (await connection.QueryAsync<ItemSpecBonusRecord>(new CommandDefinition(
                "SELECT definition_id, bonus, min_value, max_value FROM item_spec_bonuses ORDER BY definition_id, bonus", cancellationToken: ct)))
            .ToLookup(static bonus => bonus.DefinitionId);
        var classes = (await connection.QueryAsync<ItemSpecClassRecord>(new CommandDefinition(
                "SELECT definition_id, ordinal, class FROM item_spec_random_classes ORDER BY definition_id, ordinal", cancellationToken: ct)))
            .ToLookup(static spec => spec.DefinitionId);

        return items
            .Select(i => i with
            {
                SpecBonuses = bonuses[i.Id].ToList(),
                RandomSpecClasses = classes[i.Id].Select(static spec => spec.Class).ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ItemSetRecord>> GetAllItemSetsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return (await connection.QueryAsync<ItemSetRecord>(new CommandDefinition(
            "SELECT set_code, pieces, life, absorb, block, life_regen, mana_regen, stamina_regen FROM item_sets ORDER BY set_code",
            cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<MonsterRecord>> GetAllMonstersAsync(string language = CatalogLanguages.English, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var languages = Languages(language);
        var monsters = (await connection.QueryAsync<MonsterRecord>(new CommandDefinition(
            """
            SELECT
                m.id, m.key, t.name, m.model, m.level, m.hp, m.attack_min, m.attack_max, m.attack_speed,
                m.attack_range, m.accuracy, m.defence, m.absorb_pct, m.block_pct, m.stun_pct, m.sight, m.skill_damage_min,
                m.skill_damage_max, m.skill_distance, m.skill_range, m.skill_rating_pct, m.skill_curse_code, m.move_speed,
                m.move_range, m.iq, m.group_min, m.group_max, m.exp, m.active_hour, m.nature, m.brood, m.class, m.hostile,
                m.res_bionic, m.res_earth, m.res_fire, m.res_ice, m.res_lightning, m.res_poison, m.res_water, m.res_wind,
                m.res_magic, m.size_level, m.sound_code, m.model_scale, m.portrait_offset_y, m.portrait_offset_z, m.quest_code,
                m.event_item, m.potion_count, m.potion_percent, m.move_type, m.special_attack_pct, m.gold_min, m.gold_max,
                m.gold_pct, m.event_model, m.event_code, m.event_info, m.max_drops_per_kill, m.drops_seen_by_all
            FROM monsters m
            LEFT JOIN monster_texts t ON t.monster_id = m.id AND t.language = @Language
            ORDER BY m.id
            """, languages, cancellationToken: ct))).ToList();
        if (monsters.Count == 0)
            return monsters;
        if (languages.Language != CatalogLanguages.English)
        {
            var fallback = await CatalogTextsAsync(connection, "monster_texts", "monster_id", languages.Fallback, ct);
            monsters = monsters.Select(monster => monster with { Name = monster.Name ?? fallback.GetValueOrDefault(monster.Id) }).ToList();
        }

        var lineItems = (await connection.QueryAsync<MonsterDropLineItemRecord>(new CommandDefinition(
                "SELECT monster_id, line, ordinal, item_code FROM monster_drop_line_items ORDER BY monster_id, line, ordinal", cancellationToken: ct)))
            .ToLookup(static item => (item.MonsterId, item.Line));
        var lines = (await connection.QueryAsync<MonsterDropLineRecord>(new CommandDefinition(
                "SELECT monster_id, line, weight, kind, gold_min, gold_max FROM monster_drop_lines ORDER BY monster_id, line", cancellationToken: ct)))
            .Select(line => line with { Items = lineItems[(line.MonsterId, line.Line)].Select(static item => item.ItemCode).ToList() })
            .ToLookup(static line => line.MonsterId);
        var bonus = (await connection.QueryAsync<MonsterBonusDropRecord>(new CommandDefinition(
                "SELECT monster_id, ordinal, chance, item_code FROM monster_bonus_drops ORDER BY monster_id, ordinal", cancellationToken: ct)))
            .ToLookup(static drop => drop.MonsterId);

        return monsters
            .Select(m => m with { DropLines = lines[m.Id].ToList(), BonusDrops = bonus[m.Id].ToList() })
            .ToList();
    }

    public async Task<IReadOnlyList<NpcRecord>> GetAllNpcsAsync(string language = CatalogLanguages.English, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var languages = Languages(language);
        var npcs = (await connection.QueryAsync<NpcRecord>(new CommandDefinition(
            """
            SELECT n.id, n.key, t.name, n.model, n.level, n.hostile, n.size_level, n.move_range,
                   n.model_scale, n.sound_code, n.portrait_offset_y, n.portrait_offset_z, n.appear_slot, n.appear_cycle
            FROM npcs n
            LEFT JOIN npc_texts t ON t.npc_id = n.id AND t.language = @Language
            ORDER BY n.id
            """, languages, cancellationToken: ct))).ToList();
        if (npcs.Count == 0)
            return npcs;
        if (languages.Language != CatalogLanguages.English)
        {
            var fallback = await CatalogTextsAsync(connection, "npc_texts", "npc_id", languages.Fallback, ct);
            npcs = npcs.Select(npc => npc with { Name = npc.Name ?? fallback.GetValueOrDefault(npc.Id) }).ToList();
        }

        var stock = (await connection.QueryAsync<NpcShopItemRecord>(new CommandDefinition(
                "SELECT npc_id, tab, ordinal, item_code FROM npc_shop_items ORDER BY npc_id, tab, ordinal", cancellationToken: ct)))
            .ToLookup(static s => s.NpcId);
        var services = (await connection.QueryAsync<NpcServiceRecord>(new CommandDefinition(
                "SELECT npc_id, ordinal, kind, value, value2, text FROM npc_services ORDER BY npc_id, ordinal", cancellationToken: ct)))
            .ToLookup(static s => s.NpcId);
        var dialogRows = (await connection.QueryAsync<NpcDialogLineRecord>(new CommandDefinition(
                "SELECT npc_id, ordinal, text FROM npc_dialog_lines WHERE language = @Language ORDER BY npc_id, ordinal",
                languages, cancellationToken: ct))).ToList();
        if (languages.Language != CatalogLanguages.English)
        {
            var fallbackRows = await connection.QueryAsync<NpcDialogLineRecord>(new CommandDefinition(
                "SELECT npc_id, ordinal, text FROM npc_dialog_lines WHERE language = @Language ORDER BY npc_id, ordinal",
                new { Language = languages.Fallback }, cancellationToken: ct));
            var selected = fallbackRows.ToDictionary(static line => (line.NpcId, line.Ordinal));
            foreach (var line in dialogRows)
            {
                selected[(line.NpcId, line.Ordinal)] = line;
            }

            dialogRows = selected.Values.OrderBy(static line => line.NpcId).ThenBy(static line => line.Ordinal).ToList();
        }

        var dialog = dialogRows.ToLookup(static line => line.NpcId);

        return npcs
            .Select(n => n with
            {
                ShopItems = stock[n.Id].ToList(),
                Services = services[n.Id].ToList(),
                Dialog = dialog[n.Id].Select(static line => line.Text).ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FieldSpawnRecord>> GetAllFieldSpawnsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var fields = (await connection.QueryAsync<FieldSpawnRecord>(new CommandDefinition(
            """
            SELECT stage, field_code, max_concurrent_monsters, max_per_spawn_point, spawn_interval, per_point_cooldown_ms
            FROM fields WHERE field_code IS NOT NULL ORDER BY field_code
            """, cancellationToken: ct))).ToList();
        if (fields.Count == 0)
            return fields;

        var points = (await connection.QueryAsync<FieldSpawnPointRecord>(new CommandDefinition(
                "SELECT stage, point_index, active, x, z FROM field_spawn_points ORDER BY stage, point_index", cancellationToken: ct)))
            .ToLookup(static p => p.Stage);
        var spawns = (await connection.QueryAsync<FieldMonsterSpawnRecord>(new CommandDefinition(
                "SELECT stage, ordinal, monster_id, weight FROM field_monster_spawns ORDER BY stage, ordinal", cancellationToken: ct)))
            .ToLookup(static s => s.Stage);
        var hours = (await connection.QueryAsync<FieldBossHourRecord>(new CommandDefinition(
                "SELECT stage, boss_ordinal, hour FROM field_boss_hours ORDER BY stage, boss_ordinal, hour", cancellationToken: ct)))
            .ToLookup(static h => (h.Stage, h.BossOrdinal));
        var bosses = (await connection.QueryAsync<FieldBossRecord>(new CommandDefinition(
                "SELECT stage, ordinal, monster_id, escort_monster_id, escort_count FROM field_bosses ORDER BY stage, ordinal", cancellationToken: ct)))
            .Select(b => b with { Hours = hours[(b.Stage, b.Ordinal)].Select(static h => h.Hour).ToList() })
            .ToLookup(static b => b.Stage);
        var npcs = (await connection.QueryAsync<FieldNpcRecord>(new CommandDefinition(
                "SELECT stage, record_index, npc_id, x, y, z, angle FROM field_npcs ORDER BY stage, record_index",
                cancellationToken: ct)))
            .ToLookup(static n => n.Stage);

        return fields
            .Select(f => f with
            {
                SpawnPoints = points[f.Stage].ToList(),
                MonsterSpawns = spawns[f.Stage].ToList(),
                Bosses = bosses[f.Stage].ToList(),
                Npcs = npcs[f.Stage].ToList(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MotionModelRecord>> GetAllMotionModelsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return (await connection.QueryAsync<MotionModelRecord>(new CommandDefinition(
            "SELECT model_key, motion_table_key FROM motion_models", cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<MotionSlotRecord>> GetAllMotionSlotsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        // ORDER BY (table, slot): slot order IS the first-clip-per-state selection order the client
        // renderer uses — WorldDataFactory.ToMotionTimings relies on it (grouped tables, ascending slots).
        return (await connection.QueryAsync<MotionSlotRecord>(new CommandDefinition(
            """
            SELECT motion_table_key, slot, state, start_frame, end_frame,
                   event_frame0, event_frame1, event_frame2, event_frame3, repeat, key_code
            FROM motion_slots ORDER BY motion_table_key, slot
            """, cancellationToken: ct))).ToList();
    }

    public async Task<bool> HasCatalogTextsAsync(string language, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM item_texts WHERE language = @Language)
                OR EXISTS (SELECT 1 FROM monster_texts WHERE language = @Language)
                OR EXISTS (SELECT 1 FROM npc_texts WHERE language = @Language)
                OR EXISTS (SELECT 1 FROM npc_dialog_lines WHERE language = @Language) THEN 1 ELSE 0 END
            """, new { Language = CatalogLanguages.Normalize(language) }, cancellationToken: ct)) == 1;
    }

    private static async Task<IReadOnlyDictionary<long, string?>> CatalogTextsAsync(
        System.Data.Common.DbConnection connection, string table, string ownerColumn, string language, CancellationToken ct)
    {
        var query = $"SELECT {ownerColumn} AS id, name FROM {table} WHERE language = @Language";
        return (await connection.QueryAsync<CatalogTextRecord>(new CommandDefinition(query, new { Language = language }, cancellationToken: ct)))
            .ToDictionary(static text => text.Id, static text => text.Name);
    }
}
