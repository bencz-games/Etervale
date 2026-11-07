# Glossary

Domain terms, abbreviations and original format names used in the code and the other docs. Each entry names the doc
that explains it in depth.

## Read this when

- A type, column, setting or message uses a term you do not recognise.
- You need the unit or encoding of a value (coordinates, angles, item codes, light levels).

## World, units and coordinates

| Term | Meaning | Doc |
|---|---|---|
| World unit | Coordinate unit of fields and actors; authored and wire positions store it as fixed point ×256 | world.md |
| Fixed point ×256 (`FixedScale`) | Wire, stage and model coordinates are value × 256 (`GameUnits.FixedScale`, Contracts) | architecture.md |
| Angle 4096 | A full turn is 4096 angle units (`GameUnits.AngleTurn`) | architecture.md |
| Tick (70 Hz) | Authored behaviour and effect timing step, 1/70 s (`GameUnits.TicksPerSecond`, `TickMs`) | architecture.md |
| Frame ticks | An animation frame is 160 ticks of the clip timeline and lasts 33 ms at rate 1 (`GameUnits.FrameTicks`, `FrameMs`) | architecture.md |
| PT-global coordinates | Wire and authored `(x, z)`; the client's render world uses `(x, -z)` (`WorldSpace`, `RemoteEntityManager.ApplyServer`) | client.md |
| Field | A map area with its own stage, spawns, NPCs and gates; live on the server as a `FieldInstance` | world.md |
| Field code | Integer field id; the server keys live fields by it. The string field id is the stage base name | world.md |
| Field clock | Per-field millisecond time (`NowMs`) starting at 0; cooldowns are rebased on field change | application.md |
| Stage | The static mesh of a field (`.smd`); the server reads only its collision faces | world.md |
| Check face | Stage face whose material flag makes it block and support bodies | world.md |
| Gate | Seamless boundary between adjacent fields; crossing keeps the position | world.md |
| Warp gate | Portal with a trigger box, destination point and level limit | world.md |
| Village / town | Field state where combat and skill casts are refused and player buffs end | world.md |

## Characters and items

| Term | Meaning | Doc |
|---|---|---|
| Attributes | Strength, Spirit, Talent, Agility, Health; 5 status points per level | simulation.md |
| Derived stats | Life, mana, stamina, attack rating, damage, defence, absorption, speeds, max weight; recomputed from attributes, level, gear and modifiers | simulation.md |
| Function profile | Per-class selectors choosing which derived-stat formula a class uses | simulation.md |
| Brood (character) | A class's race: Tempskron or Morion (`CharacterBrood`) | simulation.md |
| Brood (monster) | Monster family: Normal, Undead, Mutant, Demon, Mechanic, User (`MonsterBrood`); used by skill bonuses and filters | skills.md |
| Tier / job tier | Class rank 0..4 (`Job1`..`Job5`); opens skill-window rows; below tier 3 experience stops at level 100 | simulation.md |
| JobBitCode | Class bit mask for item restrictions; mapped from `PlayerClass` by `JobBits`; differs from `MotionJobMask` (`1 << (Class-1)`) used by animation tables | simulation.md |
| Catalog key | 5-character authored item key, 3-character category + 2 digits (`WA101`) | simulation.md |
| Entity id | 64-bit id of a player, monster or NPC in the world; the wire, the views and the client use it (`EntityId`, `NpcId`, `TargetId`) | protocol.md |
| Life, mana, stamina | The three pools; every layer names them `Life`/`MaxLife`, `Mana`/`MaxMana`, `Stamina`/`MaxStamina` (the world tables keep a `hp` column) | simulation.md |
| Item code | 32-bit item code `family << 8 \| ordinal` derived from the item key by `ItemCodes` when the catalog loads | simulation.md |
| Item roll | The values one item copy was made with (`ItemRoll`): stats, requirements, price, specialization | simulation.md |
| Shop form | The lowest form of an item, as NPC shops sell it (`ItemRoller.Lower`) | simulation.md |
| Specialization | A copy made for one class (`ItemRoll.SpecJob`); grants its bonuses only to that class | simulation.md |
| Carried weight | Weight of every owned item (potions one per unit) against `MaxWeight`; over it a character walks only | simulation.md |
| Item definition | What copies of an item code are made from: its regular, quest or named-unique variant (`item_definitions`) | persistence.md |
| Catalog language | A `languages` code; every catalog name and NPC line is stored per language, English (`en`) required | persistence.md |
| Anchor | Inventory cell index (`y * Width + x`) of an item's top-left corner | simulation.md |
| Rolled bonus | Per-instance `ItemStatBlock` added to the definition's base stats | simulation.md |
| Absorption | Flat damage reduction against players, percent reduction against monsters | simulation.md |
| Carry limit | Maximum gold a character may hold, by level and class tier (`MoneyLimits.CarryLimit`) | world.md |
| Revision | Optimistic-concurrency counter on inventory, progression, storage and warehouse; commands quote the expected value | application.md |
| `CombatRng` | Seeded random stream, one per field instance (seeded with the field code), shared by spawner, AI, combat and skills | simulation.md |

## Modifiers and skills

| Term | Meaning | Doc |
|---|---|---|
| `EffectSpec` / `EffectContainer` | Immutable modifier template / per-entity set of active timed and permanent effects | simulation.md |
| `StatKind` | Any quantity a modifier can change; Flat adds, Percent scales `(base + flat)` | simulation.md |
| `StackRule` | Reapplication behaviour: Refresh, Stack, KeepStrongest, PerSource | simulation.md |
| Skill code | `group << 24 \| tierBit << 16 \| slot`, written as hex in data | skills.md |
| Window index | Position 0..15 of a skill in its class file; sets learning order, price, tier gate | skills.md |
| Final-tier skill points | Separate point pool for window indices 12..15, one per even level from 60 | skills.md |
| Skill rule | `SkillRule`: how a skill resolves on the server, parsed from the same JSON object as its definition | skills.md |
| Per-rank table | JSON array of exactly ten entries, ranks 1..10 | skills.md |
| Proficiency / uses | Per-skill counter (0..10000) that shortens the cooldown | skills.md |
| Mode | One of several rule variants drawn per cast (for example an element) | skills.md |
| Charge skill | Hold-to-charge skill; early release gives a partial charge | skills.md |
| Familiar | Buff-attached striker that hits on an interval and never misses or crits | skills.md |
| Mouse socket | Left or right skill slot used by a click; basic attack id `0x11111111` | skills.md |
| `RangeSlack` | 24 world units of extra cast range the server allows | skills.md |

## Monsters, spawning and motion

| Term | Meaning | Doc |
|---|---|---|
| Home anchor | A monster's spawn position; the leash radius is measured from it | world.md |
| Leash / stuck manoeuvre | Scripted returns home when a monster strays too far or stops making progress | world.md |
| Sound code | Original sound family id of a monster; also selects AI and combat behaviour | world.md |
| Spawn pass | Periodic population top-up of a field | world.md |
| Bonus drop | Independent per-10,000 chance roll on top of the weighted main drops | world.md |
| Kill payout | Kill result: credited player, experience, rolled drops | application.md |
| Motion clip | INX frame window plus event offsets; 160 ticks per frame | world.md |
| Monster / NPC code | The importer's registry id (`Identities/*.json`); the same in every database and across imports | world-import.md |

## Server, sessions and transactions

| Term | Meaning | Doc |
|---|---|---|
| Tick | One `WorldServer.Tick` at 20 Hz; the only thread that mutates world state | architecture.md |
| Command queue | Bounded single-reader channel of world commands drained by the tick | application.md |
| Command quota | `World:MaxPendingCommandsPerPlayer` queued commands per player; over it, `WorldBusyException` | application.md |
| Prepare / commit | Validate and build a candidate on the tick, apply it on the tick, then queue it for storage (a transfer is stored between the two) | application.md |
| Candidate | Clone of the character a transaction is applied to before commit | application.md |
| `SaveGate` | Per-session semaphore serialising saves and durable character operations | application.md |
| Save sequence | `characters.save_seq`: the order of a character's captures; storage keeps the highest it was given | persistence.md |
| Checkpoint | The world's capture of every playing character once per `World:CheckpointSeconds` for background storage | application.md |
| Item transfer | A logged change of hands of items or gold: trade, personal shop, warehouse, ground throw and pickup, game-master gift, Bless Castle's soul fountain (`item_transfers`) | application.md |
| Currency sink | Where gold leaves the economy (`CurrencySink`); the totals per period are logged in `currency_sinks` | application.md |
| Save journal | Captures storage refused while the server stopped, stored by the next start before players enter | application.md |
| Operation id | Client GUID per durable command; same id and command replays, different command conflicts | application.md |
| `WorldSnapshot` | Per-field state built each tick and specialised per receiver with `OwnerState` | application.md |
| `WorldOutbox` | Per-connection channel keeping the latest entity state and every event; a reader 4096 events behind loses them (`EventsLost`) | application.md |
| Typed NPC service | `npc_services` row (`NpcServiceKind`, values, text) in click order | world-import.md |

## Protocol

| Term | Meaning | Doc |
|---|---|---|
| Certificate pin | `cert=<sha-256>` in the client's server address; only that certificate is accepted | protocol.md |
| Extension opcode | Etervale message with high word `0x4554` ("ET"), ignored by an original client | protocol.md |
| Play data group | Server batch of nearby object updates | protocol.md |
| Descriptor revision | Play data byte that tells the client to re-request an object's description | protocol.md |
| Presence clock | Extension packet stamping following samples and events with server time | protocol.md |
| Movement budget | Per-receiver byte budget per tick for position-only updates | protocol.md |
| Snapshot buffer | Client ring of remote samples played back with a delay (`RemoteSnapshotBuffer`) | client.md |

## Data and formats

| Term | Meaning | Doc |
|---|---|---|
| GameServer data | Authored server data in `assets/GameServer` (items, monsters, NPCs, fields), CP949 text | world-import.md |
| CP949 | Korean code page of every authored text file and of names inside binary records | world-import.md |
| Identity name | Verbatim authored monster name spawn tables reference; differs from the English name. NPCs are identified by their file | world-import.md |
| Variant | Every parsed definition kept in `world_entity_variants`; the catalog keeps one per item code and monster identity, and every NPC file | world-import.md |
| Unresolved reference | Reference with no target, kept in `world_import_unresolved_refs` | world-import.md |
| `.zhoon` | Name/locale file linked from item, monster and NPC definitions | world-import.md |
| `.inf` / `.npc` / `.txt` | Monster, NPC and item definition files | world-import.md |
| `.spm` / `.spp` / `.spc` | Field spawn table / binary spawn points / binary fixed-NPC placements | world-import.md |
| SMD / SMB / INX | Original stage-or-model mesh / skeleton / model-info and motion table files | rendering.md |
| `.vfx.json` | Etervale's data-driven visual effect definition | effects.md |
| `JsonText` | JSON column wrapper: TEXT on SQLite, `jsonb` on PostgreSQL | persistence.md |

## Rendering

| Term | Meaning | Doc |
|---|---|---|
| EV100 | Exposure value at ISO 100 used by the camera model | rendering.md |
| Pre-exposure | Multiplier applied to scene radiance so a white lit surface is near 1 | rendering.md |
| Original light level | Authored light value, mapped to illuminance as level^1.4 × 10 lux | rendering.md |
| Mesopic vision | Final-pass blend of dim pixels toward scotopic luminance in the night hue | rendering.md |
| Floor light | Actor tint from the stage's authored vertex light under its feet | rendering.md |
| Depth pre-pass | Depth, normals and motion of opaque surfaces and cut-outs; needed by AO, SSR, volumetric light, soft particles | rendering.md |
| Cascade atlas | The sun's stabilised shadow cascades in one texture | rendering.md |
| Tile light mask | Bit mask of local lights per 16x16-pixel tile | rendering.md |
| Prepared texture | Texture decoded off-thread; in the world, uploading an unprepared texture throws | client.md |

## Tools and tests

| Term | Meaning | Doc |
|---|---|---|
| Regression runner | Console program `tests/PristonTale.Tests`; not a `dotnet test` suite | testing.md |
| Case / probe | Default regression case / opt-in runner mode that reads assets or creates a GPU device | testing.md |
| `ptworld`, `ptdevaccounts`, `ptdump`, `ptgltf`, `ptworldjson` | Assembly names of WorldImport, DevAccounts, AssetDump, GltfExport, WorldJson | tools.md |
