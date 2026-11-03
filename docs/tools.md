# Tools

Developer programs under `tools/` (asset inspection, glTF export, world import, accounts, smoke and load clients, world
JSON, UI font) and the Blender helper scripts in `3d_glb/blender/`. None of them runs inside the game or the server.

## Read this when

- You need to inspect the bytes of a `.smd`, `.smb`, `.inx`, `.bmp` or `.tga` asset before changing a reader or a
  material rule.
- You want a field, model or character as `.glb`/`.gltf` to look at in Blender.
- You need accounts, characters or load-test bots in a database.
- You want an end-to-end check against a running server (login, world entry, presence, a skill cast) or a load test.
- You are changing the UI font or the field gate/warp data that external prototypes consume.
- A tool's command line fails and you need its exact arguments.

## Code map

| Path | Responsibility |
| --- | --- |
| `tools/PristonTale.AssetDump/` | `ptdump`: headless asset inspector and field texture manifest export (`FieldTextureManifest.cs`) |
| `tools/PristonTale.GltfExport/` | `ptgltf`: `.smd`/`.smb`/`.inx` to glTF 2.0 via SharpGLTF; also referenced by the regression runner |
| `tools/PristonTale.WorldImport/` | `ptworld`: migrations plus import of the CP949 GameServer configuration; see [world-import.md](world-import.md) |
| `tools/PristonTale.DevAccounts/` | `ptdevaccounts`: accounts, characters, load-test accounts, game-master levels and audit through the real repositories |
| `tools/PristonTale.SmokeClient/` | Protocol smoke test: login, character, world entry, presence, quest journal and request, chat, one skill cast |
| `tools/PristonTale.LoadTest/` | Headless bots that log in, enter the world and walk while reporting movement |
| `tools/PristonTale.WorldJson/` | `ptworldjson`: dumps `WorldCatalog` fields, gates and warps to `world.json` |
| `tools/PristonTale.VfxImport/` | `ptvfx`: converts the original effect scripts and installs the authored effect definitions into `<assetRoot>/Effect/Vfx` |
| `tools/PristonTale.DataImport/` | `ptdata`: copies folders of another client's game data into `assets/`, unmasking XOR-masked files and refusing what the game cannot read |
| `tools/ui/build_font.py` | Builds the UI font subset and its MonoGame `.spritefont` description |
| `3d_glb/blender/` | Blender scripts for viewing exported GLBs (`frame_map.py`, `play_animation.py`) |

Solution membership: SmokeClient, GltfExport, LoadTest, VfxImport and DataImport are in `PristonTale.sln` and are built by the
solution build.
AssetDump, WorldImport, DevAccounts and WorldJson are not; build them explicitly. The rest of `3d_glb/` is git-ignored
local export output (`chars/<class>/_animations.glb`, `bodies/`, `heads/`, `fields/`, `monsters/`, `npcs/` and others).

## Common conventions

- Run every command from the repository root.
- Build: `dotnet build tools/<Project>/<Project>.csproj -c Release -m:1 -warnaserror`.
- Run: `dotnet run --project tools/<Project> -c Release --no-build -- <arguments>`.
- Tools that touch a database go through `PristonTale.Persistence.*` and run `IDatabaseMigrator.MigrateAsync` first,
  so they create or upgrade the schema of whatever database they are given. Use a copy for experiments.
- The default server address is `tcp://localhost:10009` (`HostingExtensions.DefaultGamePort`).

## AssetDump (`ptdump`)

Prints the decoded structure of one asset using the real `PristonTale.Engine3D` readers. No graphics device.

```bash
dotnet build tools/PristonTale.AssetDump/PristonTale.AssetDump.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.AssetDump -c Release --no-build -- assets/Field/forest/fore-1.smd --lights
dotnet run --project tools/PristonTale.AssetDump -c Release --no-build -- --export-field-textures assets village-2 /tmp/tex
```

| Form | Behavior |
| --- | --- |
| `<asset-path> [flags]` | `.inx` index dump; `.bmp`/`.tga` texture stats; other files by header (`SMD Stage data`/`SMD Model data`) |
| `--export-field-textures <asset-root> <field-id> <output-dir>` | Exactly four arguments; see below |
| no arguments | Usage, exit 1; `-h`/`--help` prints usage, exit 0 |

Flags (case-insensitive; flags that do not apply to the asset kind are ignored):

| Flag | Stage | Model | INX |
| --- | --- | --- | --- |
| (none) | Summary (see below) | Summary (see below) | Summary (see below) |
| `--lights` | Every light: type, range, position, colour | | |
| `--materials` | One line per material | One line per material | |
| `--matbounds` | Per-material face count, centre and size | | |
| `--objects` | | Every object: parent, vertex/face counts, animated, base position, transform | |
| `--physique` | | Distinct skinning bone names referenced by vertices | |
| `--normals` | | Authored normal stats per object (zero count, average length, samples) | |
| `--motions` | | | Usable motion slots (slot ≥ 10, state ≠ 0, end > start) with raw and resolved event frames |
| `--full` | All of the above | All of the above | All of the above |

Summaries: a stage prints counts, light types (night, lens, dynamic), whether each two-layer material's faces carry a
second UV set, bounds and material aggregates; a model prints object/material counts, animation, bounds, totals and
material aggregates; an INX prints its files, LOD groups, motion counts, usable motions and the OR of the motion job
bits. A texture prints size, bit count, average colour, alpha range, achromatic/green/near-black shares and samples.

`--export-field-textures` resolves the complete texture set of one `WorldCatalog` field with the production readers
and writes to `<output-dir>`: `manifest.json` (per texture: path, SHA-256, size, source alpha and its hash, opacity-only
use, preview, backup, consuming fields; plus fields that could not be inspected), `originals/<relative path>` (byte
copies; refuses to overwrite a differing backup) and `previews/<hash16>-<name>.png`. It fails when the field has an
unresolved texture. Exit 1 with `error exporting field textures: ...` on I/O or data errors.

## GltfExport (`ptgltf`)

Exports a stage, model or skinned actor to glTF for inspection in Blender or other viewers.

```bash
dotnet run --project tools/PristonTale.GltfExport -c Release --no-build -- assets/Field/Ricarten/village-2.smd /tmp/v2.glb
dotnet run --project tools/PristonTale.GltfExport -c Release --no-build -- <armor>.inx 3d_glb/chars/<class>/bodies/<armor>.glb \
  --no-anims --t-pose
dotnet run --project tools/PristonTale.GltfExport -c Release --no-build -- <armor>.inx 3d_glb/chars/<class>/_animations.glb \
  --anims-only --t-pose --job-mask 0x2
```

| Argument | Meaning |
| --- | --- |
| `<asset-path>` | First argument: `.smd` (stage, else model), `.smb` model, or `.inx` skinned actor |
| `[output]` | Optional single positional; `.glb` or `.gltf`; default is the input path with `.glb` |
| `--asset-root <dir>` | `.inx` only: the `assets` root; default is the nearest ancestor directory named `assets` |
| `--recenter` | Move geometry to the origin; ignored with a note for `.inx` (cancels during skinning) |
| `--scale <f>` | Positive finite uniform scale (default 1) |
| `--flip-winding` | Reverse triangle winding |
| `--flip-v` | Write `1 - v` texture coordinates |
| `--no-anims` | Geometry in bind pose, no clips |
| `--anims-only` | `.inx` only: skeleton and clips without meshes; exclusive with `--no-anims` |
| `--job-mask <mask>` | `.inx` only: keep clips whose motion job bits match; nonzero decimal or `0x` hex; bit is `1 << (job code - 1)` |
| `--lit-stage` | Stages get lit materials instead of the default unlit ones (stage lighting is baked into vertex colours) |
| `--stage-object` | `.smd`/`.smb` only: play model animation at the field-object rate over its `[160, last]` loop |
| `--t-pose` | `.inx` only: rebind the biped with horizontal arms; use it for both the armor and its `--anims-only` file |

- Option names are case-insensitive; options that take a value accept it once; any other `--` argument is an error.
- A physique `.smd` model with a sibling `.smb` is exported skinned to that skeleton.
- Textures are converted to PNG in a temporary directory that is deleted afterwards.
- Alpha modes follow the engine's queue (`PristonMaterialRuntimeFactory.Classify`): a cut-out is `MASK` even when
  flagged to draw late (the foliage), a late colour blend or a transparent material is `BLEND`, the rest `OPAQUE`.
- `PTGLTF_DEBUG=1` prints full exception stacks and cleanup failures. Exit 0 on success, 1 on any error.

## WorldImport (`ptworld`)

Applies migrations and imports the original GameServer configuration into SQLite or PostgreSQL. Data contracts,
parsing rules and reconciliation are in [world-import.md](world-import.md).

```bash
dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --sqlite data/pristontale.db --source assets/GameServer --assets assets
dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify --sqlite data/pristontale.db
```

| Command | Arguments |
| --- | --- |
| `import` | `[--sqlite <path> \| --postgres <conn>] [--source <dir>] [--assets <dir>]` |
| `verify` | `[--sqlite <path> \| --postgres <conn>]`: row counts and persisted cross-reference check |
| `export-monster-names` | `--source <dir> --output <path>`; no database |
| `export-npc-motions` | `--source <dir> --assets <dir> --output <path>`; no database |
| `export-item-names` | `--source <dir> --output <path>`; no database |
| `export-npc-placements` | `--source <dir> --output <path>`; no database |

Defaults: without `--sqlite` and `--postgres` the database is `<temp>/pristontale-world.db`, not
`data/pristontale.db`. `--source` defaults to `assets/GameServer/` or `GameServer/` in the working directory or an
ancestor of the executable; `--assets` to the nearest ancestor `assets/` of the executable (the motion pass is skipped
without it).

## DevAccounts (`ptdevaccounts`)

Seeds the database the server uses, through the same repositories, password hasher and `CharacterRecordFactory` as the
server. See [world-import.md](world-import.md) for the database workflow.

```bash
dotnet build tools/PristonTale.DevAccounts/PristonTale.DevAccounts.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- create-account demo change-me
dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- add-char demo Hero Knight 20
```

| Command | Behavior |
| --- | --- |
| `create-account <user> <password>` | Creates the account; an existing account is left unchanged (exit 0) |
| `create-bots <prefix> <count> <password>` | Creates `<prefix>0001` to `<prefix><count:D4>` (1 to 100,000), skipping existing ones |
| `create-bots ... --class <class> --field <code> --at <x,z> [--spacing <s>] [--level <n>] [--skills <hex:rank,...>]` | Also gives each new account one character `Lt<prefix><index:D4>` of that class and level, with those skills, in that field: inside a 500-unit square around (x, z) when the spacing is 0 (a crowd), else on a square grid with that spacing |
| `add-char <user> <name> <class> [level]` | Starter in the first free slot (max 6), in its class's town (Ricarten or Pillai); see below |
| `list <user>` | Slots, names, classes, levels and attributes |
| `set-gm <user> <level>` | Game-master level 0 (player) to 4 (Administrator); applies at the account's next login ([chat-and-gm.md](chat-and-gm.md)) |
| `gm-audit [count]` | The newest game-master commands (default 20): time, account, character, level, command and result |

| Option | Meaning |
| --- | --- |
| `--sqlite <path>` | SQLite file; default `data/pristontale.db`; relative paths resolve as described below |
| `--conn "<npgsql>"` | PostgreSQL instead; exclusive with `--sqlite` |
| `PT_PG_CONN` (env) | PostgreSQL connection used when neither option is given |

- `add-char`: name 3-16 characters and unused; class by `PlayerClass` name (case-insensitive) or number; level ≥ 1
  (default 1).
- A relative `--sqlite` path resolves against the nearest ancestor of the executable that holds `PristonTale.sln`
  (`DataPath.Resolve`), so it lands in the repository regardless of the working directory.
- Options may appear anywhere in the arguments. Errors exit 1.

## SmokeClient

End-to-end check over the Etervale protocol through `PristonTale.Client.Net`, against a running server.

```bash
dotnet build tools/PristonTale.SmokeClient/PristonTale.SmokeClient.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.SmokeClient -c Release --no-build -- tcp://localhost:10009 smoke smoke-pw
dotnet run --project tools/PristonTale.SmokeClient -c Release --no-build -- tcp://localhost:10009 smoke smoke-pw \
  --character <name> --skill 0x01010003
```

| Argument | Default | Meaning |
| --- | --- | --- |
| positional 1 | `tcp://localhost:10009` | Server address |
| positional 2 | `smoke` | Account (must exist: create it with DevAccounts) |
| positional 3 | `smoke-pw` | Password |
| `--character <name>` | none | Use this existing character instead of creating one |
| `--skill <hex>` | `0x01010001` | Skill code, parsed as hex with or without `0x` |
| `--chat <text>` | none | A chat line or command to send after the whisper check; prints the answers for 3 s |

Sequence: login, list characters, create a Mechanician named `Smoke<6 hex>` (without `--character`), list again, enter
the world, read the world catalog, read presence for up to 10 s (requires at least one spawn, the owner status and the
quest journal), send a quest talk without an NPC (it must be refused as out of reach), whisper `SmokeNobody` (the
server must answer that the player cannot be found), send the `--chat` line if any, send one movement 20 units from
the spawn, cast the skill with no target and wait up to 8 s.

- Without `--character` any cast result passes; a new character normally gets a refusal (skill not learned, in town).
- With `--character` the cast must be accepted and produce its start and impact skill events. The character must have
  learned the skill and stand outside a town. Any status added is printed but not required.
- Prints `SMOKE OK` and exits 0 on success; exit 1 at the first failed step.

## LoadTest

Headless bots over the Etervale protocol. Each bot logs in as `<prefix><index:D4>`, uses the account's first character
(or creates a Fighter named `Lt<prefix><index:D4>` when there is none), enters the world and plays its mode, reporting
movement every 200 ms (5 Hz) and draining presence:

| Mode | Bots |
| --- | --- |
| `walk` | Walk a 150-unit circle through the spawn at 100 units/s; a forced warp restarts the circle from the authoritative position |
| `fight` | Lock a held basic attack on the nearest wild monster in sight (900 units) and walk to 28 units of it, reporting the target |
| `cast` | Walk to 70 units of the nearest wild monster and cast `--skill` at it, again when the cooldown the server answered ends (at least every 3 s) |
| `items` | Stand still and switch the bag page every `--item-ms`: a stored inventory command, queued for storage as a potion or a pickup is. The bot expects the newer of its last answer's revision and the owner state's, and switches away from the page the owner state shows |
| `mixed` | Half walk, three in ten fight, two in ten cast (by bot index) |

Hunters move from the position the server shows them at (the stage's walls change it), and every forced warp counts as
a correction. For hunting runs, create placed characters (see DevAccounts `create-bots ... --class`), for example
Mechanicians with Mechanic Bomb in Forest 1:

```bash
dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- --sqlite <copy.db> \
  create-bots cw 2000 bot-pw --class Mechanician --level 20 --field 2 --at 4184,-11016 --spacing 0 \
  --skills 0x01010002:5,0x01010003:3
dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Database:SqlitePath=<copy.db> \
  --Network:Port=10037 --Network:MaxConnectionsPerIp=0 --Network:MaxHandshakesPerIp=0 --Server:ReportSeconds=10
dotnet run --project tools/PristonTale.LoadTest -c Release --no-build -- --count 2000 --address tcp://127.0.0.1:10037 \
  --prefix cw --mode mixed --seconds 60 --ramp 100 --server-pid <pid> --report 10
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--count <n>` | required, ≥ 1 | Number of bots |
| `--address <uri>` | `tcp://localhost:10009` | Server |
| `--prefix <text>` | `bot` | Account prefix (matches `create-bots`) |
| `--password <text>` | `bot-pw` | Shared password |
| `--seconds <n>` | `60` | Measured time in the world after the ramp |
| `--ramp <n>` | `50` | Bots started per second (minimum 1) |
| `--mode <mode>` | `walk` | `walk`, `fight`, `cast`, `items` or `mixed` (see above) |
| `--skill <hex>` | `0x01010002` | Skill the casters use (Mechanic Bomb) |
| `--server-pid <pid>` | none | Also samples that local process's CPU (cores) and working set |
| `--report <n>` | `5` | Seconds between report lines |
| `--item-ms <n>` | `1000` | Milliseconds between a bot's stored commands in `items` mode (minimum 50) |

- Every report it prints bots playing, failures, received KiB/s (total and per bot), updates/s, the bots' own hits/s,
  casts/s and accepted casts/s, position corrections and, with `--server-pid`, server CPU and working set; at the end,
  entered, failed, corrections, leave timeouts, the mean KiB/s per bot and the refused casts by status. In `items` mode
  each report also prints the stored commands answered per second, the refusals, and the answer time's p50, p99 and
  maximum in milliseconds; the end lists the refusals by message. A refusal before the world answers no revision, and
  the bot keeps the one it expected.
- Exit 0 without failures, 2 with any bot failure, 1 on a usage error. Only the first 10 failures are printed.
- `--Network:MaxConnectionsPerIp=0 --Network:MaxHandshakesPerIp=0` disables the per-address connection and handshake
  limits (defaults 8 and 4); every bot connects from one address. Measured results and the server's capacity design are in [protocol.md](protocol.md) (Server capacity).

## WorldJson (`ptworldjson`)

Writes the shared `WorldCatalog` as JSON for external field-streaming prototypes.

```bash
dotnet build tools/PristonTale.WorldJson/PristonTale.WorldJson.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.WorldJson -c Release --no-build -- /tmp/world.json
```

- One optional positional argument: the output path (default `world.json` in the working directory).
- Output: `streamConnect` 1086 and `streamDisconnect` 1810 (world units), `startField` `village-2`, a camera block, and
  per field `id`, `name`, `code`, `glb` (stage path relative to `Field/` without extension, e.g. `Ricarten/village-2`),
  `center`, `start` points, `gates` (`dest`, `x`, `z`, `angle`) and `warps` (box, destination, `limitLevel`,
  `special`). Coordinates are world units before any export scale.

## VfxImport (`ptvfx`)

Produces the client's effect data. `assets/` is not in Git, so run it after restoring or updating the game data and
after editing an authored definition.

```bash
dotnet build tools/PristonTale.VfxImport/PristonTale.VfxImport.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.VfxImport -c Release --no-build -- import assets
```

- Converts every `Effect/NewEffect/*.lua` to `Effect/Vfx/Scripts/<File>.vfx.json` and every
  `Effect/Particle/Script/*.part` to `Effect/Vfx/Particles/<File>.vfx.json`, overwriting earlier output.
- Validates and copies `tools/PristonTale.VfxImport/Authored/**/*.vfx.json` (copied next to the tool by the build) to
  the same relative paths under `Effect/Vfx/` (`Common/`, `Classes/<Class>/`, `Costumes/`).
- Prints one line per construct kept with a caveat (missing models or textures, unreadable values, scripts the original
  rejects) and a summary; exits 1 when a file could not be converted or installed. Mapping rules: docs/effects.md.

## DataImport (`ptdata`)

Brings game data the archive lacks from another client's install. `assets/` is not in Git, so the copied files reach
other machines only through the published game data archive.

```bash
dotnet build tools/PristonTale.DataImport/PristonTale.DataImport.csproj -c Release -m:1 -warnaserror
dotnet run --project tools/PristonTale.DataImport -c Release --no-build -- copy <sourceRoot> assets [--mask 0x82] Field/HeartOfFire ...
```

- Each `<path>` (a folder or a file) is relative to both roots; its files keep their relative path under `assets/`.
- `--mask` XORs every byte of every file with the given byte first (clients that mask their whole data).
- Copies only `.smd`, `.smb`, `.bmp`, `.tga`, `.png`, `.jpg` and `.wav`; other files are reported as skipped. A file
  already in `assets/` is kept untouched.
- Refuses (and reports) an SMD whose signature is not one the game's reader knows (`SmdLayout`), a bitmap
  without a `BM`/`A8`/`C8` header, a TGA that is not true colour (stored or run-length) and other damaged headers; exits
  1 when anything was refused.
- The deep fields' stages that the archive lacked (Stemple, the Heart of Fire, the Crystal Nest and the Land of Nurwn)
  came from a client that masks its data with 0x82: `copy <client> assets --mask 0x82 Field/HeartOfFire Field/CrystalNest
  Field/LandofNurwn Field/SeaA Field/AncientW`.

## UI font (`tools/ui/build_font.py`)

Development-time font preparation; the game never converts fonts at run time. Needs Python 3 with `fontTools`.

```bash
python3 tools/ui/build_font.py <DejaVuSans.ttf> <LICENSE>
```

- `font`: `DejaVuSans.ttf` of the DejaVu fonts release (2.37, `dejavu-fonts-ttf-2.37.zip`); `license`: its `LICENSE`.
- The interface text is DejaVu Sans, the default face the original's portable build draws its 14-pixel interface
  text with; Etervale shows no Korean text, so the font carries Latin only.
- Copies the font to `assets/Hud/Fonts/DejaVuSans.ttf` and the license to `assets/Hud/Fonts/DejaVu-LICENSE.txt`, and
  regenerates `Client/PristonTale/Content/Fonts/UI.spritefont`: 21 points (a 28-pixel em at the pipeline's 96 dpi,
  drawn at half scale for the 14-pixel text: `HudPainter.BakePixels`, `TextScale`), kerning, and one character region
  per contiguous run of the glyphs it has among Basic Latin, Latin-1, Latin Extended-A and general punctuation.
  Rebuild the client afterwards to bake the glyphs.

## Blender scripts (`3d_glb/blender/`)

Run from Blender's Scripting tab after importing GLBs produced by `ptgltf`.

| Script | Use |
| --- | --- |
| `frame_map.py` | Selects the mesh with the most vertices (the field) and frames it in every 3D viewport, clip 1 to 100,000 |
| `play_animation.py` | Assigns the action named by `CLIP` to the selected armatures and fits the timeline to it |

- `frame_map.py` exists because a field exported in world units is kilometres wide and far from the origin, so the
  default viewport (clip end 1000, looking at the origin) shows nothing. It switches to Object mode before selecting.
- `play_animation.py`: set `CLIP` at the top (empty prints the available clips), select the armature(s) and run; on
  Blender 4.4+ it also binds the first suitable action slot.
- Character workflow: import `chars/<class>/bodies/<armor>.glb` (and optionally a head), then the class's
  `_animations.glb`. Bodies, heads and animations exported with `--t-pose` share one rest pose, so any clip of the
  class applies to any of its bodies and heads.

## How to

- Inspect an asset before changing a reader: `ptdump <path> --full`, then compare with the reader in
  `Client/PristonTale.Engine3D/Formats`; see [rendering.md](rendering.md).
- Export a character for Blender: `--no-anims --t-pose` for each body and head, `--anims-only --t-pose --job-mask`
  for the class clips, then `play_animation.py`.
- Seed a test login: `create-account`, then `add-char`, against the database the server uses.
- Check a protocol or skill change end to end: start the server, run SmokeClient without and with `--character`.
- Measure capacity: `create-bots` (with placed characters for hunting modes) on a copy, the `players.starter_gifts`
  setting off (characters LoadTest creates would receive their gifts), server with
  `--Network:MaxConnectionsPerIp=0 --Network:MaxHandshakesPerIp=0 --Server:ReportSeconds=10`, LoadTest, and the `--benchmark-presence` runner; update
  the tables in [protocol.md](protocol.md) when results change.
- Add a CLI option: update the tool's parsing and usage text and the matching table in this doc together.

## Pitfalls

- WorldImport without `--sqlite`/`--postgres` writes to a temporary file, not `data/pristontale.db`; DevAccounts
  defaults to `data/pristontale.db` resolved from the repository root.
- Every database tool migrates the database it opens: point experiments at a copy.
- LoadTest reads options strictly as `--name value` pairs from the first argument; a stray positional shifts every pair.
  An unknown option name is ignored.
- SmokeClient treats every argument that is not an option or an option value as positional; order matters.
- `ptgltf --recenter` has no effect on `.inx` actors; `--scale` does.

## Related

- [world-import.md](world-import.md) — importer, migrations order, data contracts, database workflow
- [testing.md](testing.md) — regression runner, benchmarks and asset probes
- [protocol.md](protocol.md) — wire protocol and server capacity measurements
- [server.md](server.md) — server configuration (`Network:*`, `Database:*`)
- [rendering.md](rendering.md) — asset formats the dump and export tools read
- [skills.md](skills.md) — skill codes for `--skill`
