# WorldImport unresolved-reference audit (read-only)

The model/INX recovery work is recorded in [asset-recovery-2026-11.md](asset-recovery-2026-11.md). This document concerns a **different** data issue: references that the WorldImport importer preserved in its `world_import_unresolved_refs` SQLite table instead of silently dropping them.

## Historical observation (2026-11-08)

The previous full import reported **80 unresolved reference rows**: 76 shop empty-slot rows, 3 fixed-NPC rows and 1 drop item-code row. These are **rows**, not necessarily 80 different missing codes. The 76 `npc_shop` values of `none` are the importer's representation of the authored Korean empty-shop-slot marker `없음` (`KeyMaps.DropNone`), **not missing item definitions**. They remain preserved in the audit and are counted separately, rather than being erased. The 212 import warnings cover more than references. One unresolved **Mythcreature motion/model** is separate from these cross-reference rows and was not placed in the 55 imported fields. Do not assume the historical counts still describe the current database.

The database preserves `kind`, `owner_key`, `raw_reference` and JSON details for every unresolved row. Before changing source data, inspect the actual references and their owners.

## Read the existing database without importing

From the Git clone:

```bash
DB="$HOME/Transferências/Etervale-main/data/pristontale.db"
python3 scripts/audit_world_import_refs.py --sqlite "$DB"
```

This script opens the existing SQLite DB with `mode=ro` and enables `PRAGMA query_only`. It **does not run migrations**, import data, modify tables or create a database when the path is wrong. It displays up to ten owner/reference pairs per `kind`, counts occurrences separately from distinct reference codes, and keeps their owner names for investigation.

To view every owner/reference pair:

```bash
python3 scripts/audit_world_import_refs.py --sqlite "$DB" --all
```

To inspect the original per-row JSON metadata for the four non-placeholder rows without dumping all 76 shop placeholders:

```bash
python3 scripts/audit_world_import_refs.py --sqlite "$DB" --details
```

In the 2026-11-09 audit the four non-placeholder rows were two `BigCuepy.npc` placements in `forever-fall-01`, one `tmh-c03.inf` field placement in `dun-3`, and one `or228` drop code owned by Bad Wolf. This is an observation, not a diagnosis of why the source reference is unresolved. Inspect the original asset/source files and the returned `details_json` before editing any importer mapping.

Use `--json` for a structured report printed to standard output; it does **not write** a file unless you explicitly redirect it. Run the tool against a backup/snapshot if the live server is updating the DB. It does not use `immutable=1`, because that option could overlook WAL transactions.

**Important:** `dotnet run --project tools/PristonTale.WorldImport -- verify` currently runs database migrations before printing checks. Do **not** use that command as a strictly read-only audit of your main game database. Likewise, `import` rebuilds catalog tables and must not be used merely to inspect missing references.

## How to interpret the results

- `npc_shop`: the value `none` is a **known empty slot**, not a missing item; any different unresolved code needs inspection against the item catalog. The report preserves both sorts of row, and explicitly separates their counts.
- `field_fixed_npc`: a field placement refers to an NPC definition the importer did not resolve. Check the source file path and its spelling/case.
- Monster drop-related kinds: a dropped item did not resolve to a catalog item. Preserve the owning monster to trace the source.
- Any other `kind` must be investigated from its actual rows; this tool deliberately prints unknown kinds instead of quietly reclassifying them.

Do not erase unresolved rows, invent fake item definitions, alter the base import to suppress warnings, or rewrite the database based only on these counts. The first investigation is the raw owner/reference list. A later commit can address concrete reference mismatches only after the original sources are checked.

## Tests

```bash
python3 -m unittest discover -s scripts/tests -p 'test_world_import_audit.py' -v
```

The tests use throwaway SQLite databases, verify grouped counts and owners, and check that reading does not alter the database or create missing paths.
