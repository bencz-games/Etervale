using System.Data.Common;
using Dapper;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.World.Loot;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

public sealed partial class Importer
{
    private const string MonsterInsert = """
        INSERT INTO monsters (
            id, key, model, level, hp, attack_min, attack_max, attack_speed, attack_range, accuracy, defence, absorb_pct,
            block_pct, stun_pct, sight, skill_damage_min, skill_damage_max, skill_distance, skill_range, skill_rating_pct,
            skill_curse_code, move_speed, move_range, iq, group_min, group_max, exp, active_hour, nature, brood, class, hostile,
            res_bionic, res_earth, res_fire, res_ice, res_lightning, res_poison, res_water, res_wind, res_magic, size_level,
            sound_code, model_scale, portrait_offset_y, portrait_offset_z, quest_code, event_item, potion_count, potion_percent,
            move_type, special_attack_pct, gold_min, gold_max, gold_pct, event_model, event_code, event_info,
            max_drops_per_kill, drops_seen_by_all)
        VALUES (
            @Id, @Key, @Model, @Level, @Life, @AttackMin, @AttackMax, @AttackSpeed, @AttackRange, @Accuracy, @Defence, @AbsorbPct,
            @BlockPct, @StunPct, @Sight, @SkillDamageMin, @SkillDamageMax, @SkillDistance, @SkillRange, @SkillRatingPct,
            @SkillCurseCode, @MoveSpeed, @MoveRange, @Iq, @GroupMin, @GroupMax, @Exp, @ActiveHour, @Nature, @Brood, @Class, @Hostile,
            @R0, @R1, @R2, @R3, @R4, @R5, @R6, @R7, @MagicResistance, @SizeLevel,
            @SoundCode, @ModelScale, @PortraitOffsetY, @PortraitOffsetZ, @QuestCode, @EventItem, @PotionCount, @PotionPercent,
            @MoveType, @SpecialAttackPct, @GoldMin, @GoldMax, @GoldPct, @EventModel, @EventCode, @EventInfo,
            @MaxItemsPerKill, @AllPlayersSee)
        """;

    private static async Task WriteMonstersAsync(DbConnection conn, DbTransaction tx, IReadOnlyList<CatalogMonster> monsters,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> monsterIds, IReadOnlyDictionary<string, string> itemCodes, ImportReport report,
        CancellationToken ct)
    {
        foreach (var (m, source, displayName) in monsters)
        {
            var identity = monsterIds[source];
            var eventItem = await ResolveItemAsync(conn, tx, m.EventItem, "monster_event_item", identity.Key, new { }, itemCodes, report, ct);
            await conn.ExecuteAsync(new CommandDefinition(MonsterInsert, new
            {
                identity.Id, identity.Key, m.Model, m.Level, m.Life, m.AttackMin, m.AttackMax, m.AttackSpeed, m.AttackRange, m.Accuracy,
                m.Defence, m.AbsorbPct, m.BlockPct, m.StunPct, m.Sight, m.SkillDamageMin, m.SkillDamageMax, m.SkillDistance, m.SkillRange,
                m.SkillRatingPct, m.SkillCurseCode, m.MoveSpeed, m.MoveRange, m.Iq, m.GroupMin, m.GroupMax, m.Exp, m.ActiveHour,
                m.Nature, m.Brood, m.Class, m.Hostile,
                R0 = m.Resistances[0], R1 = m.Resistances[1], R2 = m.Resistances[2], R3 = m.Resistances[3],
                R4 = m.Resistances[4], R5 = m.Resistances[5], R6 = m.Resistances[6], R7 = m.Resistances[7],
                m.MagicResistance, m.SizeLevel, m.SoundCode, m.ModelScale, m.PortraitOffsetY, m.PortraitOffsetZ, m.QuestCode, EventItem = eventItem,
                m.PotionCount, m.PotionPercent, m.MoveType, m.SpecialAttackPct, m.GoldMin, m.GoldMax, m.GoldPct, m.EventModel, m.EventCode,
                m.EventInfo, m.MaxItemsPerKill, m.AllPlayersSee,
            }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO monster_texts (monster_id, language, name) VALUES (@Id, @Language, @Name)",
                new { identity.Id, Language = CatalogLanguages.English, Name = displayName.Length > 0 ? displayName : identity.Key }, tx,
                cancellationToken: ct));
            report.MonsterRows++;

            for (var line = 0; line < m.DropLines.Count; line++)
            {
                var drop = m.DropLines[line];
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_drop_lines (monster_id, line, weight, kind, gold_min, gold_max) VALUES (@Id, @Line, @Weight, @Kind, @GoldMin, @GoldMax)",
                    new { identity.Id, Line = line, drop.Weight, Kind = (int)drop.Kind, drop.GoldMin, drop.GoldMax }, tx, cancellationToken: ct));
                report.MonsterDropLineRows++;
                for (var ordinal = 0; ordinal < drop.Codes.Count; ordinal++)
                {
                    var code = await ResolveItemAsync(conn, tx, drop.Codes[ordinal], "monster_drop", identity.Key, new { Line = line, drop.Weight },
                        itemCodes, report, ct);
                    await conn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO monster_drop_line_items (monster_id, line, ordinal, item_code) VALUES (@Id, @Line, @Ordinal, @Code)",
                        new { identity.Id, Line = line, Ordinal = ordinal, Code = code }, tx, cancellationToken: ct));
                    report.MonsterDropItemRows++;
                }
            }

            for (var ordinal = 0; ordinal < m.DropsPlus.Count; ordinal++)
            {
                var bonus = m.DropsPlus[ordinal];
                var code = await ResolveItemAsync(conn, tx, bonus.ItemCode, "monster_bonus_drop", identity.Key, new { bonus.Chance }, itemCodes, report, ct);
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO monster_bonus_drops (monster_id, ordinal, chance, item_code) VALUES (@Id, @Ordinal, @Chance, @Code)",
                    new { identity.Id, Ordinal = ordinal, bonus.Chance, Code = code }, tx, cancellationToken: ct));
                report.MonsterDropPlusRows++;
            }
        }
    }

    // The item code as the catalog spells it; null for none, and for a code the catalog lacks, which is recorded.
    private static async Task<string?> ResolveItemAsync(DbConnection conn, DbTransaction tx, string? code, string kind, string owner, object details,
        IReadOnlyDictionary<string, string> itemCodes, ImportReport report, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        if (itemCodes.TryGetValue(code, out var canonical))
        {
            report.DropItemCodesResolved++;
            return canonical;
        }

        report.DropItemCodesDangling++;
        await RecordUnresolvedAsync(conn, tx, kind, owner, code, details, report, ct);
        return null;
    }
}
