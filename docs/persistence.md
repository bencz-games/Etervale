# Persistence

Accounts, characters, warehouses and the imported world catalog: repository contracts, Dapper implementations,
FluentMigrator migrations, the schema, and the SQLite and PostgreSQL providers. Projects: `Persistence/*`.

## Read this when

- Adding or changing a column, table, index or migration.
- Changing what a character, inventory, skill set or warehouse saves, or how saved JSON is validated.
- Debugging a Dapper mapping error (`A parameterless default constructor or one matching signature ...`).
- Switching the server or tools between SQLite and PostgreSQL, or upgrading an old database.
- Making the runtime read a new world-catalog field produced by the importer.
- Investigating a refused warehouse write or a character that fails to load.

## Code map

| Path | Responsibility |
| --- | --- |
| `Persistence/PristonTale.Persistence.Abstractions/` | Provider-neutral contracts and records; references only Simulation |
| `.../IDbConnectionFactory.cs`, `IDatabaseMigrator.cs` | Open a provider connection; apply pending migrations |
| `.../Accounts/` | `AccountRecord` (with `GmLevel`), `IAccountRepository` |
| `.../GameMasters/IGameMasterRepository.cs` | Set a game-master level, append and read the audit trail, move a character between accounts |
| `.../GameLogs/GameLogRecords.cs` | `IGameLogRepository` and the game log records: item transfers, currency sinks, server events |
| `.../Characters/CharacterRecord.cs` | One `characters` row; positional record, JSON columns as `JsonText` |
| `.../Characters/CharacterJson.cs` | `JsonText` wrapper, shared JSON options |
| `.../Characters/CharacterStateCodec.cs` | Owned-state JSON (`SavedItems`, `SavedSkills`): capture, strict restore |
| `.../Characters/CharacterRecordFactory.cs` | New starter rows; capture a live `PlayerCharacter` into a save snapshot |
| `.../Warehouses/` | `WarehouseRecord`, `IWarehouseRepository`, `WarehouseStateCodec` |
| `.../WorldData/` | Read-side catalog records and [WorldDataFactory](../Persistence/PristonTale.Persistence.Abstractions/WorldData/WorldDataFactory.cs) |
| `.../Security/PasswordHasher.cs` | PBKDF2-SHA256 hashes: `pbkdf2-sha256$<iterations>$<salt b64>$<key b64>` |
| `Persistence/PristonTale.Persistence/Repositories/` | `Dapper*Repository` implementations shared by both providers |
| `Persistence/PristonTale.Persistence/PersistenceServiceCollectionExtensions.cs` | `AddPristonRepositories`: the repositories and the migration runner both providers register, given the provider's migration processor |
| `Persistence/PristonTale.Persistence/Migrations/` | `M013Baseline`, `M014PortraitData`, `M015NpcServices`, `M016QuestData`, `M019GameMasters`, `M021MonsterSkillData`, `M022MonsterExperience64`, `M023ItemFootprints`, `M024WorldDataFields`, `M026Contacts`, `M027CatalogIdentities`, `M028WorldCatalog`, `M029NpcPlacementFacts`, `M030CharacterSaveSequence`, `M031GameLogs`, `M032StarterGifts`, `M033HelpPet`, `M034StarterOffHand`, `M035FieldOrder`, `M036CatalogPortuguese`, `FluentMigratorDatabaseMigrator` |
| `Persistence/PristonTale.Persistence.Sqlite/` | `AddPristonSqlite`, connection factory (WAL), Dapper type handlers |
| `Persistence/PristonTale.Persistence.Postgres/` | `AddPristonPostgres`, `NpgsqlDataSource` factory, `jsonb` handler for `JsonText` |
| `docker-compose.yml` | Local PostgreSQL 17 for development |

Consumers: `Server/PristonTale.Server/Hosting/HostingExtensions.cs` (provider selection),
`DatabaseMigrationService` (migrates at host start), `Shared/PristonTale.Application` services
(`AuthAppService`, `CharacterAppService`, `SessionAppService`, `NpcAppService`, `WorldDataAppService`),
`tools/PristonTale.WorldImport` (owns all catalog write SQL) and `tools/PristonTale.DevAccounts`.

## Repository contracts

| Interface | Implementation | Operations |
| --- | --- | --- |
| `IAccountRepository` | `DapperAccountRepository` | `FindByUserIdAsync`, `CreateAsync` (INSERT ... RETURNING), `UpdateLastLoginAsync` |
| `ICharacterRepository` | `DapperCharacterRepository` | List by account (ordered by slot), find by name for account, global `NameExistsAsync`, `UsedSlotsAsync`, create (RETURNING), delete by name, `UpdateAsync`, `SaveAllAsync` (the background saves: every record in one transaction, skipping older captures and missing characters, returns the rows written), `UpdateAllAsync` (several characters in one transaction, all or none: a personal shop sale) |
| `IContactRepository` | `DapperContactRepository` | `ListAsync` (in the order added), `AddAsync` (false when the name is already on the list), `RemoveAsync` |
| `IWarehouseRepository` | `DapperWarehouseRepository` | `FindAsync`, `SaveWithCharacterAsync` (character + warehouse in one transaction) |
| `IWorldDataRepository` | `DapperWorldDataRepository` | Read-only bulk loads: items, monsters, NPCs, field spawns, motion models, motion slots |
| `IServerSettingsRepository` | `DapperServerSettingsRepository` | `SeedAsync` (insert missing rows, refresh descriptions, `ON CONFLICT` on both providers), `LoadAsync`, `SetAsync` |

- All repositories are DI singletons; every call opens its own connection through `IDbConnectionFactory` and
  disposes it. There is no unit of work across calls.
- `IWorldDataRepository` is read-only by design. Catalog writes live in the importer (`Importer`), see
  [world-import.md](world-import.md).
- `UpdateAsync` writes every mutable column; identity columns (`id`, `account_id`, `name`, `slot`, `job_code`,
  `created_at`) are never updated. Every character write (`UpdateAsync`, `SaveAllAsync`, `UpdateAllAsync`, the
  warehouse's `SaveWithCharacterAsync`) shares `DapperCharacterRepository.UpdateSql`; transactions over several
  characters write them in id order, so a save batch and a trade never wait on each other. `UpdateSql` writes only
  when the stored `save_seq` is not higher than the record's: background saves may reach storage out of order, and an
  older capture never replaces a newer one (writing the same capture again is harmless). `UpdateAsync` returns false when no row
  took the record.
- Child collections are loaded with one query per child table and joined in memory with `ToLookup` on the parent key
  (`definition_id`, `monster_id`, `npc_id`, `stage`). Names and dialog lines are read in the requested catalog language,
  with English (`CatalogLanguages.English`) as fallback; a record's `Name` is null when the entity has no text in either.

### Runtime catalog build

`WorldDataAppService.BuildAsync` reads all six catalog sets for one language and converts them with `WorldDataFactory`:

| Factory method | Contract |
| --- | --- |
| `ToItemDefinitions`, `ToQuestVariantDefinitions`, `ToNamedUniqueDefinitions` | The regular, quest and named-unique definitions (`variant`); a code `ItemCodes` cannot parse is skipped. Runtime item code = `ItemCodes.Parse(code)`; `CatalogKey` = `code`; `Name` = the selected-language name, else the code. The shop form takes the low end of every range; a named unique's `UniqueVariant` is its `variant_ordinal` |
| `ToMonsterDefinitions` | Runtime code = the record's `id`; event forms take the codes after the highest id. `Key` = `key`, `Name` = the selected-language name, else the key. The stored model path keeps its authored extension; the definition uses the `.inx` sibling (also for NPCs). A drop line of items splits its weight among them with integer division; an item the runtime catalog lacks keeps its share as an empty slot. Bonus drops whose item is missing are dropped |
| `ToNpcDefinitions` | Runtime code = the record's `id`; `Name` = `key`, `DisplayName` = the selected-language name (empty without one). Services from `npc_services` ordered by `ordinal`; unknown kinds skipped; `Shop` removed when no stock code maps to a runtime item; `Dialog` = the selected-language `npc_dialog_lines` |
| `ToFieldSpawnData` | Only stages with a field code. Spawns, bosses, escorts and placements carry the monster and NPC ids, which are the runtime codes; boss hours from `field_boss_hours` |
| `ToMotionTimings` | Groups `motion_slots` by table in row order, then maps each `motion_models.model_key` to its table |

## Dapper rules

- Typed queries only: `QueryAsync<T>`, `QuerySingleAsync<T>`, `ExecuteScalarAsync<T>`, value tuples for ad-hoc
  projections. Never the untyped `Query(...)`, which yields `dynamic` (AGENTS.md forbids it).
- Wrap SQL in `new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)` so cancellation and the
  transaction flow through.
- Parameters are anonymous objects or the record itself (`@PascalCase` names from record properties).
- `DefaultTypeMap.MatchNamesWithUnderscores = true` is set by both providers: `snake_case` columns map to
  `PascalCase` members.
- Records are positional. Dapper picks a constructor whose parameter count equals the column count and whose
  parameter names match the columns in order. The selected column list must therefore follow the constructor order;
  every read names its columns (no `SELECT *`), so the physical column order of a table is not part of the contract.
  Child tables are read `ORDER BY id`, the order the importer wrote them in, so drop and spawn tables keep their order
  on both providers.
- JSON columns bind as `JsonText`, never as `string`: each provider registers a `JsonText` handler (`TEXT` on SQLite,
  `NpgsqlDbType.Jsonb` on PostgreSQL). A plain string parameter against a `jsonb` column fails on PostgreSQL.
- SQL is shared by both providers: `INSERT ... RETURNING`, `INSERT ... ON CONFLICT ... DO UPDATE`, `excluded.*`,
  `SELECT EXISTS (...)`. Keep new SQL in that common subset.

## Transactions and concurrency

| Write | Scope |
| --- | --- |
| Account, character create, update, delete | Single statement, autocommit |
| `SaveWithCharacterAsync` | One transaction: character `UPDATE` (shared `DapperCharacterRepository.UpdateSql`) then warehouse write; rollback and `false` if either touches no row |
| World import | One transaction for the whole import run (see [world-import.md](world-import.md)) |
| Migrations | FluentMigrator runner, synchronous `MigrateUp()` inside `FluentMigratorDatabaseMigrator.MigrateAsync` |

Warehouse optimistic concurrency: `warehouses.revision` must equal `expectedRevision`. A missing row counts as
revision 0, so only `expectedRevision == 0` may insert (`ON CONFLICT (account_id) DO UPDATE ... WHERE
warehouses.revision = 0`). The caller (`NpcAppService`) supplies the new revision in the record. The character
and its account warehouse are always written together, so a gold or item move between them cannot half-apply.

## Owned-state JSON

`CharacterStateCodec`, `SavedPremium`, `QuestStateCodec` and `WarehouseStateCodec` own the JSON stored in
`characters.inventory`, `equipment`, `skills`, `premium`, `quests` and `warehouses.items`.

| Column | Shape | Limits on restore |
| --- | --- | --- |
| `inventory` | `SavedItems` v1: items with `Anchor` (grid cell), `Slot = null`, plus `Revision`, inventory `Operations`, `ServiceOperations`, `ShopPurchases`; the item in the hand has `Held = true` and `Anchor = -1` (written only when set) | `CharacterStateCodec.MaxSavedItems` (166: both bag pages, belt, special item box, hand) |
| `equipment` | `SavedItems` v1: items with `Slot` set and `Anchor = -1` | `CharacterStateCodec.MaxSavedItems` |
| `skills` | `SavedSkills` v1: points, elite points, `(Code, Rank, Uses)`, progression `Revision` and `Operations` | 128 skills, points >= 0 |
| `warehouses.items` | `SavedItems` v1 with anchors in the warehouse grid | `WarehouseStorage.MaxItems` |
| `premium` | `SavedPremium` v1 (`PremiumStateCodec.cs`): premium timers `(Kind, Value, DurationSeconds, ExpiresAtSeconds)` with Unix-second expiry, item-use `Operations`, `GravityScrolls`, head mode (1-2 big head, 3 soccer ball) and expiry, `SavedAtSeconds` (the last tick's clock); `{}` is empty | 17 timers; expired timers and heads are dropped on restore; boosters and skill delay move their expiry by the time since `SavedAtSeconds` |
| `quests` | `SavedQuests` v1: the running quest (`SavedQuest`), level and beginner logs, finished codes, `FinishedCount`, the redistribution offer, `Revision` and quest `Operations` (see [quests.md](quests.md)); `{}` reads as an idle journal | 32 finished codes, 64 operations, defined quest codes and timer kinds, non-negative counts |

- Restore is strict: unknown JSON members are rejected, constructor parameters without defaults are required, max
  depth 16, at most `CharacterStateCodec.MaxStateLength` (4 MiB) characters, `Version` must be 1, instance ids must be unique and non-empty, the item code must
  exist in the runtime `ItemCatalog`, counts must fit `MaxStack`, `UpgradeLevel` must not be negative. Any violation
  throws `InvalidDataException`.
- Each `SavedItem` carries the copy's `Roll` (`ItemRoll`: rolled stats, requirements, price, specialization and its
  bonuses). An item saved without one (older rows) restores in its shop form with its saved `JobSpecMask`;
  `RolledBonus` is read and ignored. Saved equipment only needs to fit its slot (`PlayerCharacter.CanHold`): a worn item
  whose requirements are no longer met is restored worn.
- `[]` (inventory, skills, warehouse) and `{}` (equipment) are the blank values and restore as empty state.
- An inventory item whose saved anchor no longer fits its footprint moves to the first free cell.
- `SavedItem.Unique` is 2 for the quest variant of a code (quest weapons, the rank-up ring, Fury rewards); such an item
  restores as the code's quest variant, or as the regular definition when the catalog has no variant.
- Enums serialize as strings (`JsonStringEnumConverter`).

## Migrations

FluentMigrator scans `PristonTale.Persistence` for `[Migration(n)]` classes. Version history is in the
`VersionInfo` table. The server, the importer and DevAccounts all migrate before doing anything else.

| Version | Class | Effect |
| --- | --- | --- |
| 13 | `M013Baseline` | Empty database: creates the tables of that release (M028 replaced its catalog), then on PostgreSQL converts the JSON columns to `jsonb`. Database with an `accounts` table: requires `VersionInfo` to contain version 12, then adds `item_templates.max_stack/potion_space/magic_mastery`, `monsters.sound_code/model_scale`, `npcs.model_scale` and creates the three `world_*` source tables |
| 14 | `M014PortraitData` | `monsters.portrait_offset_y/z`; `npcs.display_name` (128), `npcs.portrait_offset_y/z`. Columns append in record constructor order |
| 15 | `M015NpcServices` | `npc_services` table and index; `warehouses.revision`; `npcs.display_name` only if missing |
| 16 | `M016QuestData` | `monsters.quest_code`, `event_item` (item code key, empty when none), `potion_count`, `potion_percent` |
| 19 | `M019GameMasters` | `accounts.gm_level` (default 0); `gm_audit` table and its `account_id` index |
| 21 | `M021MonsterSkillData` | `motion_slots.key_code` (the clip's authored key character, 0 when none) |
| 22 | `M022MonsterExperience64` | PostgreSQL widens `monsters.exp` to `bigint` (two bosses give more than 2^31); re-import afterwards |
| 23 | `M023ItemFootprints` | `item_templates.grid_width`, `grid_height` (default 1), the inventory footprint; re-import afterwards |
| 24 | `M024WorldDataFields` | `monsters.hostile`, `size_level`, `res_magic`, `move_type`, `special_attack_pct`, `gold_min/max/pct`, `event_model`, `event_code`, `event_info`; `npcs.hostile`, `level`, `size_level`, `appear_slot`, `appear_cycle`; `item_templates.glow_r/g/b/a`, `glow_blink`, `glow_scale`, `display_effect`, `daily_drop_limit`, `set_code`, `set_pieces`; the `item_sets` table; re-import afterwards |
| 25 | `M025ServerSettings` | `server_settings` table (`name` PK, `value`, `description`, `updated_at`); the server fills it at start |
| 26 | `M026Contacts` | `character_contacts` table and its unique index |
| 27 | `M027CatalogIdentities` | Drops and recreates `monsters`, `npcs` and every table that references them, keyed by id; adds `monster_labels`, `npc_labels`, `npc_dialog_lines`, `item_labels`; empties `world_entity_variants` and `world_import_unresolved_refs`; drops the `svc_*` columns and `npcs.dialog`. Import again afterwards. `Down` throws: restore a backup instead |
| 28 | `M028WorldCatalog` | Drops every world catalog table of the earlier migrations and `mailboxes`; drops `warehouses.buy_area_count` and `characters.derived_stats`, which nothing read; creates `languages` (seeded with `en`) and the catalog [below](#world-catalog); empties `world_entity_variants` and `world_import_unresolved_refs`. Import again afterwards. `Down` throws: restore a backup instead |
| 29 | `M029NpcPlacementFacts` | Drops `field_npcs.quest_trigger` and `size_level`: the class and size a placement record carries belong to the NPC file that lacks them, and the importer gives them to it. Import again afterwards |
| 30 | `M030CharacterSaveSequence` | `characters.save_seq` (int64, default 0): the save sequence of the stored capture |
| 31 | `M031GameLogs` | The game log tables (`item_transfers`, `currency_sinks`, `server_events`) and their indexes |
| 32 | `M032StarterGifts` | `characters.starter_gifts_received` (int, default 5): the starter gifts a character received; characters stored before count as served |
| 33 | `M033HelpPet` | `characters.help_pet` (int16, default 0) and `help_pet_hidden` (bool, default false): the beginner's helper pet and whether the player hid it |
| 34 | `M034StarterOffHand` | The off-hand became the third starter gift: a `starter_gifts_received` of 3 or more rises by one, so a served character stays served |
| 35 | `M035FieldOrder` | The action field took code 39: field codes 39-44 rise by one in `characters.field_id`, `item_transfers.field_id` and `fields.field_code`, the highest first |
| 36 | `M036CatalogPortuguese` | Adds `pt-BR` (Portuguese (Brazil)) to `languages` |

Upgrade path: databases from the earlier migration set at version 12 upgrade in place. Databases at
versions 1-11 make `M013Baseline` throw; bring them to 12 with the previous release or back up and recreate them.
After upgrading, run `import` again so existing rows receive the new columns (display names, services, portrait data).

The PostgreSQL `jsonb` columns are exactly `M013Baseline.JsonColumns`: `characters.inventory`, `equipment`, `skills`,
`quests`, `premium`, `warehouses.items` (`derived_stats` and `mailboxes.items` until M028, `npcs.dialog` until M027). The `*_json`
columns of the `world_*` tables are plain text on both providers.

## Schema

`PK` primary key, `FK` foreign key (`cascade` unless noted). Integer columns are `int32` unless stated.

### Accounts and security

| Table | Columns and constraints |
| --- | --- |
| `accounts` | `id` int64 identity PK; `user_id` unique; `password_hash` (`PasswordHasher` format); `is_blocked`, `is_tester` bool; `created_at` UTC default; `last_login_at` nullable; `gm_level` 0 to 4 (default 0, player) |
| `server_settings` | `name` PK; `value` text; `description`; `updated_at` UTC default. Rows and meanings: [server.md](server.md#server-settings-table) |
| `gm_audit` | `id` int64 identity PK; `account_id` int64 (indexed, no FK so the trail outlives the account); `user_id`, `character_name`, `gm_level`, `command`, `required_level`, `accepted` bool, `result`; `created_at` UTC default |

### Characters

| Table | Columns and constraints |
| --- | --- |
| `characters` | `id` int64 identity PK; `account_id` FK `accounts`; `name` globally unique; `slot` int16, unique with `account_id`; `job_code` (`PlayerClass`); `level`, `exp` int64 holding the unsigned 64-bit experience's bit pattern (past 2^63 it reads negative in SQL), `money` int64, `brood`; attributes `strength`, `spirit`, `talent`, `agility`, `health`, `stat_points`; vitals `life`, `mana`, `stamina`; `field_id`, `pos_x`, `pos_z` float; `model_name`, `model_name2` (head model for the chosen face); `face_index`, `tier` int16, `hair_index`, `size_level`; JSON `inventory`, `equipment`, `skills`, `quests`, `premium`; `save_seq` int64 (M030); `starter_gifts_received` int (M032; 0 for a new character, `StarterGifts.Count` once served); `help_pet` int16 (0 until
drawn, 1 to 4) and `help_pet_hidden` bool (M033); `created_at`, `last_played_at` nullable. Index `characters_account_id_idx` |
| `character_contacts` | `id` int64 identity PK (the order added); `character_id` FK `characters`; `list` int16 (`ContactList`); `name`; unique (`character_id`, `list`, `name`) (M026) |

### Inventory and warehouse

| Table | Columns and constraints |
| --- | --- |
| `warehouses` | `account_id` PK and FK `accounts`; `money` int64; `revision` int64 (M015); `items` JSON |

Character inventory and equipment are JSON columns of `characters`, not tables.

### World catalog

Written only by the importer, which replaces it whole on every run. It models the game, not the source files: an item
code and its definitions, monsters with their drop lines, NPCs, and fields keyed by their stage. Monsters and NPCs are
keyed by the importer's stable id, which is also their runtime code, and named by a unique English key
(`tools/PristonTale.WorldImport/Identities`, see [world-import.md](world-import.md#identities)); every reference to
them holds the id. No Korean text is stored.

Player-facing text lives in `*_texts` tables, one row per entity and language (`languages`, BCP-47 codes). Every
entity has its English name (`en`, `CatalogLanguages.English`); another language is an optional translation added as
`languages` and `*_texts` rows (the world importer writes them from its `Translations`, [world-import.md](world-import.md#pipeline)).
The server loads supported languages once and selects the session's language, falling back
to English; an entity without a name in either language shows its code or key.

| Table | Columns and constraints |
| --- | --- |
| `languages` | `code` (16) PK, BCP-47; `name`. Seeded with `en` English |
| `items` | `code` (16) PK, the authored 5-character code; `kind` (`ItemKind`); `grid_width`, `grid_height` (inventory cells, from the item's sprite) |
| `item_definitions` | `id` int64 identity PK; `item_code` FK `items`; `variant` int16 (`ItemVariant`: 0 regular, 1 named unique, 2 quest); `variant_ordinal` (the named unique's number, from 1; 0 otherwise); unique (`item_code`, `variant`, `variant_ordinal`); `unique_item`; `spec_class` int64 (the class `JobBitCode` the item is specialised for); durability, weight, price; damage min/max low/high; attack rating, critical, defence, block, absorb, move speed; life/mana/stamina regen (float) and add; requirements; restore life/mana/stamina min/max; `shooting_range`, `attack_speed`; `res_<element>_min/max` for bionic, earth, fire, ice, lightning, poison, water, wind; `max_stack`, `potion_space`, `magic_mastery_min/max`; `glow_r/g/b/a`, `glow_blink`, `glow_scale`; `display_effect`; `daily_drop_limit`; `set_code` nullable FK `item_sets` (set null); `set_pieces` |
| `item_texts` | PK (`definition_id` FK `item_definitions`, `language` FK `languages`); `name` (128) |
| `item_spec_bonuses` | PK (`definition_id` FK, `bonus` int16 `ItemSpecBonusKind`); `min_value`, `max_value` float: what the specialisation adds |
| `item_spec_random_classes` | PK (`definition_id` FK, `ordinal`); `class` int64: the classes a drop may roll as its specialisation |
| `item_sets` | `set_code` PK; `pieces` (worn for the bonus); `life`; `absorb`, `block`, `life_regen`, `mana_regen`, `stamina_regen` float |
| `monsters` | `id` int32 PK (registry id); `key` (128) unique, English; `model`; combat stats; `sight` (raw authored reach); skill damage, distance, range, rating, curse; `move_speed`, `move_range`, `iq`, `group_min/max`, `exp` int64, `active_hour` (+1 day, -1 night, 0 any); `nature`, `brood`, `class` int16; `hostile`; `res_<element>`, `res_magic`; `size_level`; `stun_pct` (default 100); `sound_code`; `model_scale`; `portrait_offset_y/z`; `quest_code`; `event_item` nullable FK `items` (set null); `potion_count`, `potion_percent`; `move_type`, `special_attack_pct`, `gold_min/max/pct`, `event_model`, `event_code`, `event_info`; `max_drops_per_kill` (0 no cap); `drops_seen_by_all` |
| `monster_texts` | PK (`monster_id` FK `monsters`, `language` FK); `name` (128) |
| `monster_drop_lines` | PK (`monster_id` FK, `line`); `weight`; `kind` (`DropKind`); `gold_min/max` |
| `monster_drop_line_items` | PK (`monster_id`, `line`, `ordinal`), FK (`monster_id`, `line`) `monster_drop_lines`; `item_code` nullable FK `items` (set null): the items a line splits its weight among |
| `monster_bonus_drops` | PK (`monster_id` FK, `ordinal`); `chance`; `item_code` nullable FK `items` (set null): rolled on every kill besides the drop lines |
| `npcs` | `id` int32 PK (registry id); `key` (128) unique, English; `model`; `level`; `hostile`; `size_level`; `move_range` (default 64); `model_scale`; `sound_code`; `portrait_offset_y/z`; `appear_slot`, `appear_cycle` |
| `npc_texts` | PK (`npc_id` FK `npcs`, `language` FK); `name` (128) |
| `npc_dialog_lines` | PK (`npc_id` FK, `language` FK, `ordinal`); `text`: at most 20 lines per NPC and language |
| `npc_services` | PK (`npc_id` FK, `ordinal`: click order); `kind` (`NpcServiceKind`); `value`, `value2`; `text` |
| `npc_shop_items` | PK (`npc_id` FK, `tab` (0 weapons, 1 armour, 2 goods), `ordinal`); `item_code` FK `items` |
| `fields` | `stage` (64) PK, the stem of the stage model; `field_code` nullable unique (`WorldCatalog` field code; null for a stage no field uses); `max_concurrent_monsters` (10); `max_per_spawn_point` (3); `spawn_interval` (tick mask); `per_point_cooldown_ms` |
| `field_spawn_points` | PK (`stage` FK `fields`, `point_index`); `active`; `x`, `z` world units |
| `field_monster_spawns` | PK (`stage` FK, `ordinal`); `monster_id` FK `monsters`; `weight` |
| `field_bosses` | PK (`stage` FK, `ordinal`); `monster_id` FK `monsters`; `escort_monster_id` nullable FK `monsters` (set null); `escort_count` |
| `field_boss_hours` | PK (`stage`, `boss_ordinal`, `hour`), FK (`stage`, `boss_ordinal`) `field_bosses`: the hours the boss appears |
| `field_npcs` | PK (`stage` FK, `record_index`); `npc_id` FK `npcs`; `x`, `y`, `z` in 1/256 world units; `angle` (4096 per turn) |
| `motion_models` | `model_key` (256) PK, normalized model path; `motion_table_key` |
| `motion_slots` | PK (`motion_table_key`, `slot`); `state`; `start_frame`, `end_frame`; `event_frame0..3` int64 holding unsigned 32-bit values; `repeat`; `key_code` (M021) |

Children are keyed by their parent and an ordinal, so the parent key leads every child primary key. The two
composite foreign keys (`monster_drop_line_items`, `field_boss_hours`) are plain `CREATE TABLE` SQL in the migration,
because FluentMigrator cannot add one on SQLite. `motion_models` and `motion_slots` have no foreign key between them
and are the only catalog tables the importer upserts instead of replacing.

### Game logs

Rows only ever inserted, by the server's background writers, and deleted by age (`Logs:RetentionDays`,
[server.md](server.md)). No foreign keys: a log outlives the characters, accounts and items it names.
`IGameLogRepository` (`DapperGameLogRepository`) appends each kind in one transaction and prunes all three tables.

| Table | Columns and constraints |
| --- | --- |
| `item_transfers` | `id` int64 identity PK; `occurred_at` UTC; `kind` int16 (`ItemTransferKind`: 0 trade, 1 personal shop, 2 warehouse deposit, 3 warehouse withdrawal, 4 ground throw, 5 ground pickup, 6 game-master gift); `transfer_id` (36: the rows of one transfer; a throw and the pickup of what it threw share the ground drop's id); `from_character_id`, `to_character_id` int64 nullable (null: the warehouse, the ground or a game master); `account_id` (the warehouse's account); `field_id`; `item_code` (16, the catalog key; null for gold); `instance_id` (36); `amount` int64 (items, or gold). Indexes on `occurred_at`, `from_character_id`, `to_character_id`, `instance_id` |
| `currency_sinks` | `id` int64 identity PK; `occurred_at` UTC (end of the period); `sink` int16 (`CurrencySink`); `gold` int64. Index on `occurred_at` |
| `server_events` | `id` int64 identity PK; `occurred_at` UTC; `kind` int16 (`ServerEventKind`: 0 started, 1 stopped); `server_name` (64); `detail`. Index on `occurred_at` |

### World source preservation

These tables keep what the catalog tables cannot express. They are rewritten on every import.

| Table | Columns | Content |
| --- | --- | --- |
| `world_source_files` | `source_path` (1024) PK, `kind` (32), `sha256` (64), `content_base64`, `fields_json`, `field_count` | Every file under the source root, byte for byte. `kind` is the lower-cased first path segment (`openitem`, `monster`, `npc`, `field`). `fields_json` lists the `*key` lines of text files with line number and values |
| `world_entity_variants` | `source_path` PK, `kind` (16: `item`, `monster`, `npc`), `identity_key` (256: the item code, or the monster's or NPC's English key; empty for an unkeyed file), `is_catalog_entry`, `data_json` | Every parsed definition, including duplicates and unkeyed files; `is_catalog_entry` marks the one written to the catalog |
| `world_import_unresolved_refs` | `id` PK, `kind` (32), `owner_key`, `raw_reference`, `details_json` | References that did not resolve; kinds listed in [world-import.md](world-import.md) |

## Providers

| Aspect | SQLite (`AddPristonSqlite`) | PostgreSQL (`AddPristonPostgres`) |
| --- | --- | --- |
| Connection | New `SqliteConnection` per call, `Foreign Keys=True` | Pooled `NpgsqlDataSource` singleton |
| Journal | First open per factory runs `PRAGMA journal_mode=WAL` (persisted in the file); every open runs `PRAGMA synchronous=NORMAL` | Server default |
| JSON columns | `TEXT` | `jsonb` (converted in `M013Baseline`) |
| Type handlers | `SqliteTypeMapping`: bool as 0/1, int16/int32 sent as int64, float sent as double, `JsonText` as string, `DateTime` read as UTC | `JsonTextNpgsqlHandler` only |
| Registration | `SqliteTypeMapping.EnsureRegistered()` runs from both the DI extension and `SqliteConnectionFactory` | In `AddPristonPostgres` |
| Path | Server and DevAccounts resolve relative paths against the directory holding `PristonTale.sln` (`DataPath.Resolve`); the parent directory is created | Connection string |

SQLite writes `-wal` and `-shm` files next to the database while in use; copy all three, or stop every process
first, when copying a database.

Server configuration (`Server/PristonTale.Server/appsettings.json`): `Database:Provider` (`Sqlite` default or
`Postgres`), `Database:SqlitePath` (default `data/pristontale.db`), `ConnectionStrings:Postgres`. Any other provider
value stops startup.

The PostgreSQL pool holds at most `Database:MaxPoolSize` connections (`AddPristonPostgres`, default 20, replacing the
connection string's `Maximum Pool Size`), below the server's default `max_connections` (100): a pool larger than the
server answers "too many clients" under load.

`docker-compose.yml` runs `postgres:17` as container `pristontale-postgres`, database, user and password
`pristontale`, host port 5433 mapped to 5432, named volume `pristontale-pgdata`, health check `pg_isready`. Start with
`docker compose up -d --wait`; `docker compose down -v` deletes the volume. The default connection string in
`appsettings.json` matches it.

## How to

### Add a column to a world catalog table

1. New migration `Persistence/PristonTale.Persistence/Migrations/M0NN<Name>.cs` with the next `[Migration(NN)]`:
   `Alter.Table(...).AddColumn(...).NotNullable().WithDefaultValue(...)` and a matching `Down`. Never edit an
   applied migration; `M013Baseline` has already run on existing databases.
2. Record in `Persistence/PristonTale.Persistence.Abstractions/WorldData/`: append a constructor parameter with a
   default, at the end, so constructor order equals column order on new and upgraded databases.
3. Repository `DapperWorldDataRepository`: add the column to the explicit select list, in constructor order.
4. Importer `tools/PristonTale.WorldImport`: parsed DTO in `Parsers/Parsed*.cs`, the parser, and the column and value
   lists of the insert in `Importer.<Kind>.cs`.
5. Runtime catalog: map it in `WorldDataFactory` into the Simulation definition (`ItemDefinition`,
   `MonsterDefinition`, `NpcDefinition`, `FieldSpawnData`).
6. Both providers: new CLR types need a SQLite handler in `SqliteTypeMapping` if Microsoft.Data.Sqlite cannot bind
   them; a JSON column needs `JsonText` and an `IfDatabase("PostgreSQL")` conversion to `jsonb` in the migration.
7. Tests in `tests/PristonTale.Tests`: `WorldImportTests`, `NpcServiceTests`, `MonsterNameTests` and the schema
   checks of `NpcPlacementMigrationTests` use disposable SQLite databases; extend the relevant one. Check PostgreSQL
   with `docker compose` and `ptworld import --postgres ...` against a disposable database.
8. Update this document's schema table and [world-import.md](world-import.md).

### Add a world catalog table

Steps 1, 4-8 above, plus: create the table keyed by its parent's key and an ordinal (or a natural key), with
foreign keys `OnDelete(Cascade)` to the parent; player-facing text goes in a `*_texts` table per language, never in a
column. Add a record and an `IWorldDataRepository` method with
`DapperWorldDataRepository` and every test double; load it in `WorldDataAppService.BuildAsync`; write it in the
importer's transaction in foreign-key order and to `Importer.CatalogTables` (children first); add it to `PrintRowCounts` in `tools/PristonTale.WorldImport/Program.cs`.

### Add a character column

1. Migration as above.
2. Append the parameter to `CharacterRecord` (no default needed if every construction site passes it).
3. `DapperCharacterRepository`: `Columns`, the `INSERT` column and value lists, and `UpdateSql` (unless immutable).
4. `CharacterRecordFactory.NewStarter` and `Capture`; check `CharacterAppService`, `SessionAppService` and
   `tools/PristonTale.DevAccounts`, which build records through the factory.
5. If it is JSON, use `JsonText`, add the column to the PostgreSQL `jsonb` conversion of the new migration.

### Add a field to saved inventory, skills or warehouse JSON

Add a constructor parameter with a default to `SavedItem`, `SavedItems`, `SavedSkill` or `SavedSkills`, so rows
written before the change still deserialize under `RespectRequiredConstructorParameters`. Removing a member breaks
old rows (`UnmappedMemberHandling.Disallow`); bump `Version` and add a conversion instead.

## Pitfalls

- A select list out of constructor order breaks the record mapping at server start.
- Runtime monster and NPC codes are the registry ids, the same on both providers and across imports; the event forms
  of monsters take the codes after the highest id, so they move when a monster is registered.
- `GetAllMotionSlotsAsync` must return rows ordered by `(motion_table_key, slot)`; `ToMotionTimings` groups
  consecutive rows and the first slot per state is the clip the client plays.
- Dapper configuration is process-global (`MatchNamesWithUnderscores`, type handlers). Use one provider per
  process; the SQLite int and float handlers would also apply to PostgreSQL parameters.
- A saved item whose code is missing from the runtime catalog makes the character or warehouse fail to load. Item
  codes removed from the source, or whose key `ItemCodes` cannot parse, affect existing saves.
- `M013Baseline.Down` drops every table, including on a database that came through the upgrade branch.
- The importer resolves a relative `--sqlite` path against the working directory, unlike the server and DevAccounts.

## Related

- [world-import.md](world-import.md): how the catalog and preservation tables are filled.
- [application.md](application.md): when characters and warehouses are loaded and saved.
- [server.md](server.md): host configuration and startup order.
- [simulation.md](simulation.md), [world.md](world.md): the definitions the catalog becomes.
- [testing.md](testing.md): the regression runner.
