# Asset recovery on Fedora — November 2026

This documents the **local** recovery of the PristonTale model indexes used by Etervale, the evidence for each change, and how to repeat the two *static* INX builds. It is not an asset distribution, nor a declaration that every historical game resource is available or redistributable.

Related reading: [AssetDump and WorldImport](tools.md), [world import workflow](world-import.md), [Fedora setup](fedora-setup.md). The corresponding application changes were committed separately (`/unlocktier`, Meteo timing, then binary model-header support in AssetDump).

## Initial symptoms and findings

The first NPC/monster motion audit had **six unresolved cases**. After recovery, the full WorldImport run on **2026-11-08** reported **428 model references → 425 motion tables, 3,072 clips and one unresolved motion**. The remaining model is Mythcreature. This does **not** mean that all catalog cross-references are resolved: the same import retained **80 dangling references** (76 shop item codes, three fixed NPCs and one drop item code). The 212 import warnings are not all missing-model warnings.

| Character / resource | Logical assets under `assets/` | Diagnosis | Recovery / caveat |
| --- | --- | --- | --- |
| Red Gladiator / Hellbeast | `char/monster/mpthell/mpthellbeast.{inx,smd,smb}` | Files missing from the initial local asset set | Recovered the trio from a donor installation; no substitute animations were invented |
| Son of Battle / Villain | `char/npc/villain/villain.{inx,smd,smb}` | Files missing from the initial set | Recovered the trio from a donor installation |
| Lava Golem | `char/monster/rptlavagolem1/rptlavagolem1.inx` | Initially reported as unresolved; a valid INX was located in the available asset sets | Resolved by locating/restoring the existing index, not by synthesizing motion data |
| Kadomatsu | `char/npcxms/kadomatsu.{smd,smb,ini,inx}` | Model geometry/skeleton found in a donor set, but no original INX recovered | Compiled a **synthetic static** index from the recipe below (`Stand` 0–1) |
| Zinjya / Zizzyadorii | `char/npcxms/zizzyadorii.{smd,smb,ini,inx}` | Original and other inspected INX variants declared motions but exposed **zero usable clips**; Linux path also differed in case | Retained backup of the original INX, compiled a **synthetic static** `Stand` 0–1 and reconciled case-sensitive paths |
| Mythcreature | `char/npc/mythcreature_round_4/mythcreature_round_4.{inx,smd,smb}` | No matching files found in the searched installations | **Unresolved**. NPC catalog entry 85 exists, but has no placement in the 55 imported fields, nor references in the checked source folders. Dynamic uses or unsearched data cannot be ruled out |

### Linux directory-case issue, independent of missing files

The imported logical path uses `char/npcxms/` (all lowercase), but the source assets also held `char/npcXms/` (capital `X`). Those are distinct directories on Fedora. Zinjya and Christmas content (including `ChTree_Longbo.inx`, `ChTree_Longbo01.smd` and `.smb`) were affected by this path mismatch. **Do not remove either tree blindly.** The local non-overwriting reconciliation was:

```bash
# Run from the root of the original ZIP workspace, NOT this Git clone without assets.
cp -a -n assets/char/npcXms/. assets/char/npcxms/
```

`cp -n` protects files already in the lowercase directory; it does not prove that every donor model is correct. Audit the exact paths before importing.

## Read-only inventory

From the Git clone (the original ZIP workspace remains separate):

```bash
python3 scripts/audit_recovered_models.py \
  --assets "$HOME/Transferências/Etervale-main/assets"
```

The script classifies each expected file as **present**, **case mismatch** (found under a case-insensitive match only), or **missing**, and checks the known hashes of the two reconstructed INX files. It does **not** decode animation tables or change assets; if an original INX has no usable motions, the presence check alone cannot catch it. `--strict` returns an error if required files are not at their exact paths, intentionally exempting Mythcreature.

To inspect a binary-header model and the actual usable motion slots using the committed engine readers:

```bash
dotnet run --project tools/PristonTale.AssetDump -c Release -- \
  "$HOME/Transferências/Etervale-main/assets/char/npcxms/zizzyadorii.smd" --objects

dotnet run --project tools/PristonTale.AssetDump -c Release -- \
  "$HOME/Transferências/Etervale-main/assets/char/npcxms/zizzyadorii.inx" --motions
```

Evidence for Zinjya's SMD: **one object (`zizzya`), 205 vertices, 274 faces, three materials, `animated: False`, `lastFrame: 0`**. Kadomatsu also had static SMD/SMB data (`lastFrame: 0`). A stand clip on these models is a safe *static placeholder*, **not a recovery of real, absent animation frames**.

## Rebuilding the two static INX caches

The **UTF-8, reviewable** sources live in:

- `tools/model-recovery/recipes/kadomatsu.utf8.ini`
- `tools/model-recovery/recipes/zizzyadorii.utf8.ini`

The original model-configuration format uses Korean directives in **CP949**; `scripts/build_static_npc_inx.py` converts each recipe back to CP949 and compiles it with the **external** `pt_asset_tool/model_cache_compiler.py` from the user's PT Asset Tool archive. The compiler code itself is **not** copied into this repository. Use only a trusted local copy of that archive; its Python code is executed by the script.

```bash
# From the clone. Point --compiler to your trusted downloaded ZIP or extracted directory.
python3 scripts/build_static_npc_inx.py \
  --compiler "$HOME/Transferências/ptassettool_01(5).zip" \
  --verify-only

python3 scripts/build_static_npc_inx.py \
  --compiler "$HOME/Transferências/ptassettool_01(5).zip" \
  --output build/model-recovery
```

The generated files are staged in `build/model-recovery/` (ignored by Git), **never installed automatically**. `--model kadomatsu` or `--model zizzyadorii` builds one; different output files are not overwritten unless `--force` is explicitly supplied. The script checks the byte-for-byte SHA-256 values recorded below and rejects a compiler version producing different bytes.

| Generated path suffix | Format | SHA-256 |
| --- | --- | --- |
| `kadomatsu.ini` | CP949 model recipe | `e10a5151d9235e770e749f48b8f93693ae006409ca861abaf914f7c03dfa9a65` |
| `kadomatsu.inx` | 95,268-byte INX (11 declared motions, one usable static `Stand` at slot 10) | `3a2fed3cbc9634b41e3115688039397ecfdd214b50dc26899cf95e8474485191` |
| `zizzyadorii.ini` | CP949 model recipe | `a43429c18abcfb5a4fc48c4aa2308c173ed976596df6f18c74d72ade1ceff6d5` |
| `zizzyadorii.inx` | 95,268-byte INX (11 declared motions, one usable static `Stand` at slot 10) | `7eed4bf48f7aace3f740bb794cd76eb9110c95f3bfe0195c390b9ee06f516948` |

The logical paths `char/npcxms/kadomatsu.inx` and `char/npcxms/zizzyadorii.inx` are **embedded in the compiler's checksum**. Do not rename the outputs or install them in an arbitrary directory and expect identical behavior. The `.ASE` strings in the recipes are the historical logical references; Etervale resolves the corresponding actual `.smd` and `.smb` files. The compiler does **not** reconstruct geometry or skeletal animation from ASE/SMD/SMB files.

### Safe local installation (only with permission to use the models)

Stop the game server. Confirm that `kadomatsu.smd/.smb` and `zizzyadorii.smd/.smb` are already available in the lower-case `assets/char/npcxms/` folder, and keep backups of any existing `.ini/.inx` files, especially the original Zinjya INX. You can stage the generated files beside the models, for example:

```bash
# Run from the Git clone. Review destination and existing files first.
DEST="$HOME/Transferências/Etervale-main/assets/char/npcxms"
ls -l "$DEST"/{kadomatsu,zizzyadorii}.{smd,smb}
cp -ai build/model-recovery/{kadomatsu,zizzyadorii}.{ini,inx} "$DEST/"
```

`cp -i` asks before overwriting; **copy the Zinjya original to a uniquely named backup beforehand** and do not continue if it has not been preserved. Then inspect with AssetDump as above.

## WorldImport verification and database safety

The export reads NPC configuration and model motions without modifying the SQLite database:

```bash
dotnet run --project tools/PristonTale.WorldImport -c Release -- \
  export-npc-motions \
  --source "$HOME/Transferências/Etervale-main/assets/GameServer" \
  --assets "$HOME/Transferências/Etervale-main/assets" \
  --output build/model-recovery/npc-motions.json
```

A full import **writes the database**. Only do this with the server stopped and a backup; point to the existing data directory explicitly (the clone has no populated assets or database):

```bash
DB="$HOME/Transferências/Etervale-main/data/pristontale.db"
cp -a "$DB" "$DB.before-recovery-$(date +%Y%m%d-%H%M%S).bak"
dotnet run --project tools/PristonTale.WorldImport -c Release -- import \
  --sqlite "$DB" \
  --source "$HOME/Transferências/Etervale-main/assets/GameServer" \
  --assets "$HOME/Transferências/Etervale-main/assets"
```

Expected **historical** result after the fixes above: 3,072 motion clips and **one unresolved model**, Mythcreature. Numbers can change when the game data or importer changes, so do not hardcode that as a universal assertion. Never delete Mythcreature's `.npc` definition just to silence the warning; investigate event/dynamic placement before classifying it as definitely unused.

## What must not go into Git accidentally

Do not commit copied proprietary SMD/SMB/INX assets, source installations, the SQLite database, database backups or personal log files without an explicit redistribution and privacy review. These scripts and the UTF-8 recipes are authored recovery instructions. The actual donor model binaries and their provenance stay outside the repository pending rights verification.
