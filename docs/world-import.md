# World import

`tools/PristonTale.WorldImport` (`ptworld`) turns the authored server data in `assets/GameServer` into the world
catalog tables, and the authored file formats it reads. Also covers the small `tools/PristonTale.WorldJson` and
`tools/PristonTale.DevAccounts` command-line tools.

## Read this when

- Running `import` or `verify`, or deciding which options to pass.
- A monster, item, NPC, shop, drop, spawn or NPC service is missing or wrong in game.
- Reading or changing a parser for item, monster, NPC or field files.
- Reconciling the import report, `world_entity_variants` or `world_import_unresolved_refs`.
- Importing a new kind of authored data or a new key of an existing file.
- Seeding accounts or characters for local testing.

## Code map

| Path | Responsibility |
| --- | --- |
| [Program.cs](../tools/PristonTale.WorldImport/Program.cs) | CLI: option parsing, defaults, provider setup, migration, report and `verify` output |
| [Importer.cs](../tools/PristonTale.WorldImport/Importer.cs) | Parse all sources, choose catalog entries, replace the catalog in one transaction |
| `tools/PristonTale.WorldImport/Importer.Items.cs`, `.Monsters.cs`, `.Npcs.cs`, `.Fields.cs` | The catalog writes of each domain |
| `tools/PristonTale.WorldImport/Tokenizer.cs` | Line tokenizer of the `*key value` text format; C-style integer and float parsing |
| `tools/PristonTale.WorldImport/KeyMaps.cs` | Every recognised Korean key, the name and dialog keys, value literals, resistance index maps |
| `tools/PristonTale.WorldImport/IdentityRegistry.cs`, `Identities/monsters.json`, `Identities/npcs.json` | The stable id and English key of every monster and NPC (embedded in the importer), see [Identities](#identities) |
| `tools/PristonTale.WorldImport/NamedUniqueRegistry.cs`, `Identities/named-uniques.json` | The stable number of every named unique, by item code and English name |
| `tools/PristonTale.WorldImport/EnglishNames.cs` | English names where the data gives Portuguese or Korean or misspells them; English translations of the Korean-only NPC lines |
| `tools/PristonTale.WorldImport/Parsers/AuthoredLabels.cs` | Name labels by key; Korean text is not kept |
| `tools/PristonTale.WorldImport/ImportReport.cs` | Counters and warnings printed after `import` |
| `tools/PristonTale.WorldImport/Parsers/SourceTextReader.cs` | Reads a text file and follows its linked-file chain |
| `tools/PristonTale.WorldImport/Parsers/ItemTxtParser.cs` | Item definition files → `ParsedItem` |
| `tools/PristonTale.WorldImport/Parsers/MonsterInfParser.cs` | Monster definition files → `ParsedMonster` |
| `tools/PristonTale.WorldImport/Parsers/NpcParser.cs` | NPC definition and name files → `ParsedNpc` with typed services |
| `tools/PristonTale.WorldImport/Parsers/SpmParser.cs`, `SppReader.cs`, `SpcReader.cs` | Field spawn table, spawn points, fixed NPC placements → `ParsedField` |
| `tools/PristonTale.WorldImport/Parsers/MotionTableExtractor.cs` | Motion clip timing from model INX files |
| `tools/PristonTale.WorldImport/Parsers/Parsed*.cs` | Import DTOs; also serialized into `world_entity_variants.data_json` |
| `tools/PristonTale.WorldImport/*Export.cs` | JSON export commands without database access |
| `tools/PristonTale.WorldJson/Program.cs` | Dumps `WorldCatalog` fields, gates and warps to JSON |
| `tools/PristonTale.DevAccounts/Program.cs` | Creates accounts, load-test accounts and characters |

The importer project is not in `PristonTale.sln`; build it separately. It references the persistence providers,
Simulation, `PristonTale.Assets` (`AssetFiles`, `Cp949`) and `PristonTale.Engine3D` (for `PristonInxReader`); it never initializes
graphics. `tests/PristonTale.Tests` references it.

## Commands

```bash
dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --sqlite data/pristontale.db --source assets/GameServer --assets assets
dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify --sqlite data/pristontale.db
```

| Command | Database | Effect |
| --- | --- | --- |
| `import` | yes | Migrate, parse, write the catalog and preservation tables, print the report and row counts. Exit 1 if the source root does not exist |
| `verify` | yes | Migrate, print row counts, orphan checks (drop items, shop items, spawn monsters), NPC services by kind, NPCs with dialog and with a name. On a new database it creates the schema and prints zero counts |
| `export-item-names` | no | `[{Code, Name}]` of item files, first per code, to `--output` |
| `export-monster-names` | no | `[{Id, Key, DisplayName, SourceFile}]`, first per identity; fails on a file without identity or `*Name` |
| `export-npc-placements` | no | Fixed NPC placements of every `WorldCatalog` field (field code, NPC id and key, position, angle, source file) |
| `export-npc-motions` | no | `{FormatVersion: 1, Models, Slots}` motion rows of NPC models; requires an assets root |

The usage text lists every command. Exit code 0 on success, 1 on any exception (message and stack trace on stderr)
or an unknown command.

| Option | Default | Notes |
| --- | --- | --- |
| `--sqlite <path>` | `%TEMP%/pristontale-world.db` when neither database option is given | Relative paths resolve against the working directory |
| `--postgres <conn>` | - | Npgsql connection string; wins over `--sqlite` |
| `--source <dir>` | see below | GameServer root |
| `--assets <dir>` | nearest ancestor of the executable that contains `assets/` | Root for the motion pass; the pass is skipped with a warning only when no root is found |
| `--output <path>` | - | Required by the export commands |

Options are taken by name from anywhere after the command; unrecognised arguments are ignored.

Source root lookup when `--source` is absent: `./assets/GameServer`, then `./GameServer`, then each ancestor of the
executable directory for `assets/GameServer` or `GameServer`; otherwise `./GameServer`.

## Pipeline

`Importer.RunAsync`:

1. Parse items, monsters, NPCs and fields from disk; record every parsed definition as a variant.
2. Open one connection and one transaction.
3. Delete every catalog table and `world_import_unresolved_refs`, children first (`Importer.CatalogTables`).
4. Write in foreign-key order: item sets and items (each definition with its English name, bonuses and random
   classes), monsters (name, drop lines, bonus drops), NPCs (name, dialog, services, shop), fields (spawn points,
   monster spawns, bosses and their hours, NPC placements), motion timing, `world_source_files`,
   `world_entity_variants`, then the other languages' texts.
5. Commit. Any exception rolls back the whole import.

The other languages' texts come from `Translations/<language>.json`, embedded in the importer
(`Importer.Translations`): `npcs` (by NPC key: `name` and `dialog`, a line per English ordinal), `monsters` (by monster
key) and `items` (by item code, every variant of the code). Each text is a `*_texts` or `npc_dialog_lines` row of that
language beside the English one, so a re-import keeps them; a key the catalog lacks is a warning. `pt-BR.json` holds
the Brazilian Portuguese names and dialog lines of every NPC; monsters and items keep their English names.

Directories are matched case-insensitively (`OpenItem`, `Monster`, `NPC`, `Field`); file patterns too, because the
data mixes `.npc`/`.NPC` and `.inf`/`.INF`. Only the top level of each directory is parsed (for example
`Monster/*.inf`, not `Monster/<subdir>/`); linked files in subdirectories are reached through links. Files are processed
in ordinal path order, which makes variant selection deterministic.

## Text format

Item, monster, NPC and spawn-table files share one line format, decoded as CP949 (code page 949):

- A line is `key value value ...`. The key is the first word; words are split on space and tab.
- A value starting with `"` is read up to the closing quote and may contain spaces. Quoted text is kept verbatim,
  including leading and trailing spaces.
- There is no comment syntax. Lines whose first word is not a recognised key (`//...`, blank, prose) are ignored, so
  `//*key` disables a key.
- Numbers follow C `atoi`/`atof`: leading numeric prefix, anything invalid reads as 0, a missing value reads as 0.
- Keys are compared exactly (ordinal) unless noted: `*Name`, the `*E_*`/`*B_*` locale keys and the ASCII spawn-table
  aliases are case-insensitive.
- Linked-file key `*연결파일 <path>`: reading stops at that line and continues in the linked file. The path resolves
  segment by segment, case-insensitively, against the directory of the first file of the chain. A cycle or a missing
  file ends the chain silently.

## Parsers and file formats

### Items: `OpenItem/*.txt` (+ linked `name/*.zhoon`)

| Key (see `KeyMaps`) | Column |
| --- | --- |
| `*코드` | `code`, the 5-character natural key |
| `*Name`, then `*E_NAME`, then `*B_NAME`, then `*이름` when it is not Korean, then the code | English `item_texts.name` (first non-empty, not Korean), through `EnglishNames` |
| `*내구력`, `*무게`, `*가격`, `*공격력` (4 values), `*명중력`, `*방어력`, `*흡수력`, `*블럭율`, `*이동속도`, ... | Stat ranges |
| Element keys (8) | `res_<element>_min/max` in bionic, earth, fire, ice, lightning, poison, water, wind order |
| Regen, add and restore keys | Each has two spellings (`KeyMaps.ItemManaRegen` and siblings) |
| `**특화` class names | `spec_class`: the `JobBitCode` of the last known class name (English or Korean; Assassin and Shaman included; `Atalanter` is Atalanta) |
| `**특화랜덤` class names | `item_spec_random_classes` rows in order; names of classes the game does not have (`MartialArtist`, misspellings) are skipped |
| Other `**` stat keys | `item_spec_bonuses`, one row per `ItemSpecBonusKind` (`min_value`, `max_value`; a key written twice keeps the last): 0 absorb, 1 defence, 2 move speed, 3 block, 4 attack speed, 5 critical, 6 range, 7 magic mastery, 8 mana per level, 9 life per level, 10 attack rating per level, 11 damage per level (`max_value`), 12 mana regeneration, 13 life regeneration, 14 stamina regeneration, 15 life regeneration range (`**LifeRegenRange`) |
| `*최대수량`, `*보유공간`, magic mastery keys | `max_stack` (min 1), `potion_space`, `magic_mastery_min/max` (a copy rolls whole points between them) |
| `*유니크` | `unique_item` (1 when the key has no value) |
| `*유니크색상 r g b a blink [scale]` | `glow_r/g/b/a`, `glow_blink` (ticks), `glow_scale`: the glow of a unique weapon in the hand |
| `*이펙트설정` | `display_effect`: non-zero shows the class-up light on the held weapon |
| `*발생제한` | `daily_drop_limit`: drops of the item per day across the server (0 unlimited) |
| `*세트아이템 <set> <pieces>` | `set_code`, `set_pieces`; the comment line above it states the bonus (`item_sets`) |

`items` holds the code, its `kind` (from the key: `ItemKinds.Classify` in Simulation, the table the runtime uses; the
files have no kind key) and its footprint. What a copy of the item is made from is an `item_definitions` row:

| `variant` | Source | Runtime |
| --- | --- | --- |
| 0 regular | The catalog entry of the code | `ItemCatalog` |
| 2 quest | The first file in path order whose `*유니크` is 2 (quest weapons, rank-up rings, Fury of Phantom rewards) | `ItemCatalog.TryGetQuestVariant`, handed out by quests ([quests.md](quests.md)) |
| 1 named unique | Each distinct name, the first file in path order, among the files whose `*유니크` has no value (1) and that lost the selection of their code (Vega, Ancient Rage, Bone Crasher...); `variant_ordinal` is its number in `Identities/named-uniques.json` | `ItemCatalog.NamedUniques`; saved items name one by `ItemDefinition.NamedUniqueBase` + the number. They never drop |

Each definition has its own English name, bonuses and random classes.

`item_sets` has one row per set code: the most pieces any of its items asks and the bonus `SetBonusParser` reads from
the comment above `*세트아이템` (terms "stat+value" separated by commas; a term without a value takes the next value;
`체력` life, `흡수력` absorption, `블럭율` block, a term ending in `재생` regeneration of the pools its letters name: 체/생
life, 근 stamina, 기 mana). A bonus it cannot read is a warning and gives nothing.

### Item code mapping

An item key is a family of two letters and a letter or digit plus two digits (`WA101`, `DA304`). `ItemCodes.TryParse`
(`Shared/PristonTale.Simulation/Items/ItemCodes.cs`) derives the 32-bit item code from the key alone, so every family has
one, and the runtime derives it again when it loads the catalog. A key of another shape is imported, listed in one
report warning and skipped by the runtime catalog.

### Item footprints

`ItemFootprints.Measure` (`tools/PristonTale.WorldImport/ItemFootprints.cs`) sizes every item from its inventory sprite,
`<assets>/image/Sinimage/Items/**/it<KEY>.bmp` (`ItemIconFiles`, outside `DropItem`): one cell per whole 22 pixels in
each direction, at least one. An item without a sprite takes the most common footprint of its family (the first three
characters of its key), and one cell when the family has no sprite either; those items are listed in one report warning.
The footprint is stored in `items.grid_width` and `grid_height`. Without `--assets` every item takes one cell.

`WorldDataFactory.ToItemDefinition` turns each definition into the shop form plus an `ItemTemplate` with every range
and the specialization bonuses (`ToItemTemplate`); see [simulation.md](simulation.md#items).

Code lookups are case-insensitive: drop and shop references (often lower case) are rewritten to the casing stored in
`items.code`.

### Monsters: `Monster/*.inf` (+ linked name files)

- Identity: `*이름`, read verbatim with its spaces because spawn tables reference it byte for byte, and turned into the
  monster's id and English key ([Identities](#identities)); it is not stored. English name (`monster_texts`):
  `*E_NAME`, then `*Name`, then `*B_NAME`, trimmed, not Korean, through `EnglishNames`; without one the key is
  written and a warning given.
- Model: `*모양파일` (quoted path, usually `.ini`; the runtime and motion pass use the `.inx` sibling).
- Resistances use a different key set from items and a different order; `KeyMaps.MonsterResistances` maps them to the
  same eight-element order.
- `*시야` is stored raw. `*활동시간`: `낮` day +1, `밤` night -1, anything else 0. `*품성`: `good`, `evil`, else
  natural. `*몬스터종족`: undead, mutant, demon, mechanic, else normal; `*언데드 유|있음` undead, anything else normal.
  `*두목` or `*보스` (no value) marks a boss; `*계급` sets the class number. `*스턴율`/`*스턴률`: flinch chance, default
  100.
- `*속성 적` hostile (`hostile`), anything else not; `*크기` the shadow size (`size_level`: 소형 0, 중형 1, 중대형 2, 대형 3,
  없음 or any other word -1, 0 without the key); `*매직` magic resistance (`res_magic`); `*이동타입` (`move_type`); `*특수공격률` (`special_attack_pct`);
  `*골드 <min> <max> <percent>` gold dropped on death on top of the drop table (`gold_min/max/pct`); `*예비모델` the model
  worn during the hammer event (`event_model`); `*이벤트코드`, `*이벤트정보` the event creature code and its field
  (`event_code`, `event_info`: code 100 walks at night).
- Sound keys resolve through `CharacterSoundCodes.Resolve`; `*모델크기` is the model scale; `*화면보정 <height>
  <distance>` is the target-frame portrait correction.
- Quest keys: `*구별코드` is the classification code quest hunts count (`quest_code`); `*이벤트아이템 <code>` an item
  every kill hands out (`event_item`, rewritten to the stored code casing; an unknown code is stored as null and
  recorded as unresolved); `*물약보유수` and `*물약보유률` the potions the monster carries and the chance to carry
  them (`potion_count`, `potion_percent`; arena opponents drink the count).
- Drops: each `*아이템` line is a `monster_drop_lines` row in authored order: `<weight> 없음` (nothing),
  `<weight> 돈 <min> <max>` (gold), or `<weight> <code>...`, whose codes are `monster_drop_line_items` rows (the runtime
  splits the weight among them). `*추가아이템 <chance> <code>` is a `monster_bonus_drops` row.
  `*아이템모두` makes drops visible to all players (`drops_seen_by_all`); `*아이템카운터` caps items per kill
  (`max_drops_per_kill`).

### NPCs: `NPC/*.npc` (+ linked `name/*.zhoon`)

- Every definition file is an NPC: fields place a file, and files sharing an authored identity (`*이름`) differ in
  model, lines and services. The file name is the NPC's identity in the registry ([Identities](#identities)); model
  `*모양파일`; `*연결파일` is followed for more keys.
- English name (`npc_texts`): `*E_NAME`, then `*B_NAME`, then `*Name`, not Korean, through `EnglishNames` (by file).
  A file without one takes the first name among the files of the same authored identity; otherwise it warns and the
  key is written.
- English dialog (`npc_dialog_lines`, at most 20 lines): the `*E_CHAT` lines, else the `*B_CHAT` lines, else the
  Korean `*대화` lines in their English translation (`EnglishNames.Dialog`; a line without one is dropped and listed in
  the import report). Lines in
  Korean, or saved through another code page (they decode as Korean syllables), are not kept. Runs of spaces and tabs
  collapse to one space (name files wrap text at a fixed width).
- Shop lists `*무기판매`, `*방어구판매`, `*잡화판매` fill tabs 0, 1, 2, up to 32 codes each. The placeholder `없음`
  keeps its ordinal and becomes an unresolved `npc_shop` reference; the shop exists only if a tab has a real code.
- `*이동범위` move range (default 64), sound keys, `*모델크기`, `*화면보정` as for monsters.
- Class and size: `*계급` (from 1000 a quest code, the `QuestGiver` service) and `*크기` (shadow size as for monsters).
  The original opens a placed NPC over its placement's character record, so a file that sets neither keeps the
  placement's (`NpcParser.TakePlacement`: the first placement in stage and record order; placements that disagree are a
  warning). A file never placed and without `*크기` has size 0.
- `*속성 적` hostile, `*레벨` level; `*출현간격 <slot> <cycle>` the appearance draw
  (`appear_slot`, `appear_cycle`: the NPC appears only when the field's draw modulo the cycle is the slot).

### NPC services

`NpcParser.BuildServices` writes one `npc_services` row per service in the order the original answers a click
(`ordinal` 0..n). Keys that set the same service keep the last value read.

| Order | Kind | Source key and value |
| --- | --- | --- |
| 1 | `QuestGiver` | `*계급 <code>` with code >= 1000 |
| 2 | `QuestScript` | `*퀘스트코드 <code> <param>` |
| 3 | `Shop` | any real stock code |
| 4 | `SkillMaster` | `*스킬수련`; value = `*직업전환` code (default 1) when present, else 0 |
| 5 | `Warehouse` | `*아이템보관` |
| 6 | `ItemMix` | `*아이템조합` value 1, `*아이템연금` value 200 |
| 7-8 | `Smelting`, `Manufacture` | `*아이템제련`, `*아이템제작` |
| 9 | `CollectMoney` | `*모금함` |
| 10-11 | `EventGift`, `GiftExpress` | `*경품추첨`, `*경품배달` |
| 12 | `WarpGate` or `QuestEvent` | `*윙퀘스트` (default 1) or `*퀘스트이벤트` (default 2) |
| 13 | `Teleporter` | `*텔레포트` route (default 1) |
| 14 | `StarPoint` | `*별포인트적립` minimum level (default 20) |
| 15 | `GiveMoney` | `*기부함` |
| 16 | `EventBooth` | `*이벤트매표소` (default 1) |
| 17-19 | `ItemAging`, `Clan`, `BlessCastle` | `*아이템에이징`, `*클랜기능`, `*블레스캐슬` (default 1) |
| 20 | `Polling` | `*설문조사` (default 1) |
| 21 | `Media` | `*동영상경로` and `*동영상제목` both present; text = path, line feed, title |
| 22 | `MixtureReset` | `*믹스처리셋` |
| 23 | `EventMonster` | `*이벤트코드`, `*이벤트정보` |
| 24+ | `Unported` | one per key in `KeyMaps.NpcUnportedServices`, text = its label |

Which services run in game is described in [world.md](world.md) and [application.md](application.md). The Korean
title of the media service is written in English (`EnglishNames.Text`).

### Fields: `Field/<base>.spm`, `.spp`, `.spc`

Every stage with a spawn file is a `fields` row keyed by its stage, the file name without `.<ext>` or `.ase.<ext>`
(case-insensitive). `field_code` is the code of the `WorldCatalog.AllFields` (Simulation) field whose stage model has
that stem, or null for a stage no field uses yet; the runtime loads only stages with a code.

| File | Format |
| --- | --- |
| `.spm` (text) | `*출연자`/`*ACTOR "<monster>" <weight>`; `*출연자두목`/`*BOSS_ACTOR "<boss>" "<escort>" <count> <hours...>` (up to 32 hours); `*최대동시출현수`/`*MAX_ACTOR_POS` concurrent limit; `*출현수`/`*MAX_ACTOR` monsters per wave; `*출현간격`/`*DELAY <n> [seconds]`: `spawn_interval = (1 << n) - 1`, minimum 1, optional seconds × 1000 into `per_point_cooldown_ms` |
| `.spp` (binary) | 200 records of 12 bytes, little-endian `int32 state, x, z`, no header. All-zero records are skipped; `point_index` is the record index; `state != 0` means active. Coordinates are world units |
| `.spc` (binary) | 100 records of 504 bytes, little-endian, no header. Live when `int32` at +4 is non-zero. +104: 60-byte CP949 path of the NPC definition file. +180: `int32` size level. +392: `uint16` class (`*계급`); both go to the NPC file that lacks them. +476/+480/+484: `int32 x, y, z` in 1/256 world units. +492: heading (4096 per turn). The record index is `record_index` |

A placement stores the id of the NPC file its record names (matched by file name, case-insensitively); the name stored
in the record is ignored, because it can be stale.

### Motion timing from INX

`MotionTableExtractor` runs after the catalog writes, for every monster model, NPC model and player class body
(`CharacterClassRegistry` default visuals).

- Key: `MotionTimingCatalog.NormalizeKey` (extension `.inx`, forward slashes, lower case); the file is
  `<assets>/<key>`, opened through `AssetFiles`.
- Clips are motion entries from `PristonFormatConstants.CharacterMotionBase` on with `State != 0` and
  `EndFrame > StartFrame`; `slot` keeps the entry index. Up to four event frames and the repeat flag are stored.
- A model INX without usable clips borrows the table of its linked motion file (player bodies share one biped table);
  `motion_table_key` then names that file.
- Failures (missing or invalid INX, no link) add a warning and `MotionUnresolvedModels`; the import continues.
- `motion_models` rows are upserted; each extracted table's `motion_slots` are deleted and reinserted. A slot keeps its
  INX motion index (the clip clients play for a monster's swing) and the clip's key character (`key_code`), which
  selects the skill some monsters perform.

## Identities

Monsters and NPCs have no code in the source. A monster is named by its authored identity (`*이름`, Korean, spaces
included), which spawn and boss tables reference byte for byte; an NPC by its definition file, which placements
reference (case-insensitive). The importer turns each into a stable id and a unique English key through
`IdentityRegistry`, read from `Identities/monsters.json` and `Identities/npcs.json` (`{id, key, source}` per entry,
embedded in the importer). Every catalog table and the game use only the id (the runtime code) and the key; outside the
registry the authored identity remains only in the source preservation tables (`world_source_files`, and
`world_import_unresolved_refs` for a reference that resolves to nothing).

- Ids are never reused or renumbered. Keys are the English label, numbered when several monsters share one (`Fury`,
  `Fury 2`...: the one the fields spawn first, then by the identity's padding, then by path); a few name the creature
  the shared label hides (`Mini Santa Goblin`, `Typhoon`, `Cyclone`, `Big Ghost`).
- An identity the registry lacks takes the next free id and its English label as key (each word capitalised when the
  label is lower case, numbered when taken, `Monster <id>` or `NPC <id>` without a label); the import report lists the
  entry to add (`Identities/monsters.json lacks ...`). Add it to the file so the id stays the same in every database.
- Crystals (`MonsterCrystals`) and skill summons name monsters by key; `WorldImportTests` checks that every one is
  registered.
- Named uniques keep a number per item code (`NamedUniqueRegistry`, `Identities/named-uniques.json`,
  `{code, number, name}`), because saved copies name a unique by it. A named unique the registry lacks takes the next
  number of its code and the report lists the entry to add (`Identities/named-uniques.json lacks ...`); numbers are
  never reused, and renaming a unique in `EnglishNames` needs its registry entry renamed too. An entry no file defines
  any more is reported (`... lists ...`): saved copies of it no longer load.

## Catalog entries, variants and unresolved references

| Kind | Catalog entry among files sharing an identity | Unkeyed file |
| --- | --- | --- |
| Item | File whose name equals the code (case-insensitive), else first in path order | Variant only, warning |
| Monster | The first file of its `*이름` in the server's load order (file names compared upper-cased, as NTFS lists them, so `_` sorts after the letters); a boss line's master and escort take the last file of their name, which joins the catalog under its file name (identity source) and the first file's English name when it is not the first | Variant only, warning |
| NPC | Every file | - |

Every parsed file is a `world_entity_variants` row (`is_catalog_entry` marks the winner); the other item and monster
files also produce a "retained outside the catalog" warning. Parse exceptions skip the file with a warning. The original server also resolves
a spawn's monster to the first file of its name (and a boss line's to the last), so the source must not carry another release's files ahead of the
original ones: a second data set's variant of an identity replaces the original monster's stats, drops and quest items.

Names follow Etervale's English standard. `EnglishNames` holds the names the data gives only in Portuguese or Korean,
or misspells: a monster's or NPC's label by its file and an item's name by its code, applied where each parser picks the
name; a monster variant that the original set also has takes that monster's English label verbatim. Name labels and
dialog lines in Korean, or saved through another code page, are not kept; the NPC lines the data gives only in
Korean are translated in `EnglishNames.Dialog`.

`world_import_unresolved_refs.kind` values:

| Kind | Owner | Effect on the catalog |
| --- | --- | --- |
| `monster_drop` | monster | Line item kept with `item_code = NULL`, so its share of the line stays |
| `monster_bonus_drop` | monster | Bonus row kept with `item_code = NULL` |
| `monster_event_item` | monster | `event_item` stored as null |
| `npc_shop` | NPC | Shop item not written; the empty-slot placeholder is recorded as `none` |
| `field_monster_spawn` | field base | Spawn entry not written; warning |
| `field_boss` | field base | Boss entry not written; warning |
| `field_boss_escort` | field base | Boss written without escort |
| `field_fixed_npc` | field base | Placement not written |

`owner_key` is the monster's or NPC's English key or the field base; `raw_reference` is the source text that resolved to
nothing (an item code, or a monster or NPC identity no file defines).

`world_source_files` stores every file under the source root (including archives, executables and unparsed
subdirectories) with its SHA-256 and Base64 content. For `.txt`, `.inf`, `.inf_`, `.npc`, `.spm` and `.zhoon` it also
stores each `*key` line (line number, key, values) in `fields_json`. Preserving a key does not make it a gameplay
feature; behavior lives in Simulation and Application.

## Idempotent re-import

- The catalog tables and the three preservation tables are cleared and rewritten; `motion_models` upserts by model
  and each extracted table's `motion_slots` are replaced.
- Running `import` twice on the same source gives the same rows (asserted by `WorldImportTests` and
  `NpcServiceTests`); monster and NPC ids come from the registry, so they do not move between runs.
- `accounts`, `characters` and `warehouses` are never touched. An item removed from the source leaves the catalog, so
  a saved copy of it fails to load ([persistence.md](persistence.md#pitfalls)).
- `item_definitions.id` is an identity and changes on every import: nothing outside the catalog refers to it.

## Import report

Printed after `import`: source files and preserved key lines, variants, unresolved rows; per entity rows and files
parsed with child counts; field children; motion models, tables, clip rows and unresolved models; resolved/dangling
counts for drop items, shop items, spawn monsters, bosses and fixed NPCs; then every warning and the row counts of the
item, monster, NPC, field and preservation tables. The supplied snapshot references some files and codes it does not
contain, so dangling counts are expected; check `world_import_unresolved_refs` when reconciling data.

Every key the parsers do not read is reported once per kind and key with the number of files and the first one
(`unknown key monster *...`). The supplied data has only corrupted keys there (Korean saved through another code page);
fix the file rather than the parser.

## Other tools

`tools/PristonTale.WorldJson` (`ptworldjson [output]`, default `world.json`): writes every `WorldCatalog` field (id,
name, code, model path stem, centre, start points, gates, warp gates) plus streaming radii and camera constants for an
external field-streaming prototype. No database.

`tools/PristonTale.DevAccounts` (`ptdevaccounts`, not in the solution) creates accounts, bots and characters and sets
game-master levels; its commands and options are in [tools.md](tools.md).

## How to

### Import a new key of an existing file

1. Add the token to `KeyMaps` with a comment giving its meaning in Etervale terms.
2. Read it in the parser and carry it on the `Parsed*` DTO.
3. Add the column and propagate it as in [persistence.md](persistence.md) "Add a column to a world catalog table"
   (migration, record, repository, insert column and value lists, `WorldDataFactory`, Simulation definition). Text a
   player reads goes in the entity's `*_texts` table, in English.
4. Extend the CP949 fixture of `tests/PristonTale.Tests/WorldImportTests.cs` (or `NpcServiceTests`) and assert the
   value after two import runs.

### Import a new kind of authored data

1. Schema: a structural migration with a parent table keyed by the authored code or a registry id, child tables
   keyed by the parent and an ordinal, cascading foreign keys, and a `*_texts` table for player-facing text
   ([persistence.md](persistence.md)).
2. Parser: `Parsers/<Kind>Parser.cs` returning `Parsed<Kind>`. Use `SourceTextReader.ReadChain` for linked text files,
   `Tokenizer` for lines, `Cp949` (`Shared/PristonTale.Assets`) for bytes, `BinaryPrimitives` little-endian for binary records; tokens in
   `KeyMaps`. Keep identities verbatim if other files reference them.
3. Importer: a `Parse<Kind>` method that enumerates with `EnumerateFiles`, chooses the catalog entry, and calls
   `CreateVariant` (kind at most 16 characters); a `Write<Kind>Async` in `Importer.<Kind>.cs`, called in the transaction
   after the tables it references, that inserts the rows and calls `RecordUnresolvedAsync` (kind at most 32
   characters) for every reference it cannot resolve; its tables join `Importer.CatalogTables`, children first.
4. Report: counters in `ImportReport`, a line in `PrintReport`, the tables in `PrintRowCounts`, and an integrity
   query in `PrintCrossRefCheck` when the kind has references.
5. Preservation: add text extensions with `*key` lines to `Importer.ReadSourceFields`.
6. Runtime: record and read method in `IWorldDataRepository`/`DapperWorldDataRepository`, conversion in
   `WorldDataFactory`, loading in `WorldDataAppService.BuildAsync` and `WorldData`.
7. Tests: a disposable-database test in `tests/PristonTale.Tests` that writes CP949 fixtures, imports twice and
   checks rows, variants and unresolved references; register it in the runner.
8. Docs: this file, [persistence.md](persistence.md), and the README import section if commands change.

## Pitfalls

- Stop the server before importing into its database: the import rewrites the catalog in one long transaction that
  holds SQLite's write lock for tens of seconds, and the running server keeps the catalog it loaded at start anyway.
- Never trim identity names: spawn tables and placements reference monster and NPC identities with their spaces,
  and the registry matches them byte for byte.
- A monster or NPC added to the source keeps an id only once its registry entry is added: until then each import gives
  it the next free id, which another database may give to another identity.
- A key after `*연결파일` in the same file is never read; put it before the link or in the linked file.
- Missing linked files produce no warning and no unresolved row.
- Monster drop weights for multi-code lines are split with integer division, as the original does; the remainder
  is lost.
- `world_entity_variants.data_json` serializes the DTOs with `IncludeFields`, so value-tuple lists (item specials,
  NPC shop stock) appear as `Item1`, `Item2`... objects. `world_source_files` holds the complete source.
- A Korean `*대화` line without an entry in `EnglishNames.Dialog` is dropped and listed in the report; add its
  translation.
- `--assets` is not required inside the repository: the default finds the repository `assets/` from the executable
  path. Pass it explicitly when running a published copy.
- The default database without `--sqlite`/`--postgres` is a temporary file, not `data/pristontale.db`.
- After a migration that rebuilds the catalog (M027, M028) the catalog is empty until `import` runs again.

## Related

- [persistence.md](persistence.md): schema, migrations, providers, repository reads.
- [world.md](world.md): spawning, NPCs, loot and motion at runtime.
- [simulation.md](simulation.md): item and monster definitions.
- [rendering.md](rendering.md): INX format; [architecture.md](architecture.md): `AssetFiles`.
- [tools.md](tools.md): other tools.
- [testing.md](testing.md): running the regression suite.
