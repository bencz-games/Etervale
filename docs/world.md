# World rules

Shared world rules the authoritative server runs: fields and their catalog, stage collision and ground queries,
movement checks, monsters and their AI, spawning, loot, motion timing and NPC services. Covers
`Shared/PristonTale.Simulation/World/`, `Shared/PristonTale.Simulation/Physics/`, `Shared/PristonTale.Geometry/` and `Shared/PristonTale.Assets/`.
How the tick calls these rules belongs to [application.md](application.md).

## Read this when

- You add or change a field: its stage, gates, warp gates, level limit or start points.
- A monster gets stuck, walks through walls, stands on a roof or floats, or a player movement report is refused.
- You change how monsters notice, chase, attack, flinch, return to their spawn area or move as summons.
- You change spawn density, group size, despawn timing or the scheduled boss spawns.
- You change drop tables, gold rolls, kill experience or kill credit.
- Attack, flinch or potion timing does not match the animation (INX motion data).
- You add an NPC service, a shop rule, a warehouse limit or a teleport route.

## Code map

| Path | Responsibility |
|---|---|
| [`WorldDefinitions.cs`](../Shared/PristonTale.Simulation/World/WorldDefinitions.cs) | `FieldDef`, its records (gates, warp gates, ambients, lighting, camera, day/night), `FieldState`, `WorldCatalog` |
| `World/Fields/*.cs` | One static `Definition` per field, grouped by region (`DungeonFields`, `IceFields`...); `FieldSpawnData` records |
| `World/FieldCodes.cs` | The field codes rules name (home towns, office room, inner ice field, Bellatra, quest arena, castle, boss field); the definitions and every rule use these, never a bare code |
| `World/FieldBackdrops.cs` | Sky backdrop texture keys (day, glow, night) shared by fields |
| [`World/FieldTerrain.cs`](../Shared/PristonTale.Simulation/World/FieldTerrain.cs) | Ground queries for one field: place on floor, step, wall crossing, wall slide, body sweep |
| `World/MonsterStepping.cs` | Server monster step: full step, then two side probes; `IStepOccupancy` |
| `World/WorldPosition.cs` | `IsValid`: finite and within ±1,000,000 units on every axis |
| `World/Monsters/` | `MonsterDefinition` (stats, AI block, skill block, potions, drops), `MonsterCatalog`, `MonsterInstance` (live state, motion as `MotionState`), `MotionStateExtensions` |
| `World/Monsters/Skills/` | Monster skill rules: `MonsterSkillRules` (curses, blow and skill riders, timings), `MonsterStatuses` (codes and effect specs), `MonsterLifeRules` (potions, virtual lives, Babel's regeneration), `MonsterTransformRules` |
| [`World/Ai/MonsterAiBrain.cs`](../Shared/PristonTale.Simulation/World/Ai/MonsterAiBrain.cs) | Wild monster state machine: target scan, chase, attack, leash and stuck manoeuvres, flinch, boss retarget |
| `World/Ai/MonsterAiTuning.cs` | Per-definition derived constants and clip timings; global AI constants |
| `World/Ai/SummonBrain.cs` | Owned summon behaviour (follow owner, attack ordered target) |
| `World/Ai/MonsterLocomotion.cs` | Monster walk/run speed |
| `World/Ai/IPreyView.cs`, `ISimRng.cs`, `MonsterAttackEvent.cs` | Seams the host implements: prey list, random source, landed swing |
| `World/Spawning/FieldSpawner.cs` | Population pass, idle despawn, scheduled bosses; `SpawnSeams.cs` holds `IPlayerProximity`, `ISpawnSink` |
| `World/Spawning/SpawnCadence.cs` | Optional bitmask spawn cadence on a 70 Hz frame clock |
| `World/Spawning/BossSpawnSchedule.cs` | Server-wide hourly boss pass at a random minute per day |
| `World/Loot/` | `DropTable`, `DropRoller`, `KillReward` (experience and kill credit), `GroundDropView` |
| `World/Motion/` | `MotionClip`, `MotionTiming`, `MotionTimingSet`/`MotionTimingCatalog`, player fallbacks |
| `World/Motion/PlayerEquipmentTiming.cs`, `World/Motion/Data/PlayerEquipmentTiming.json` | Player attack clips by class and weapon key, loaded from the generated embedded JSON (one entry per class and clip with its keys); do not hand-edit |
| `World/Npcs/` | `NpcDefinition`, `NpcCatalog`, typed services (`NpcServiceKind`), service commands and replies, shop and warehouse rules, teleport routes, sound code map |
| `Physics/EntityCollision.cs` | Circle-vs-circle blocking between bodies (players, monsters, NPCs) along a swept segment |
| `Physics/EntityCollisionRadii.cs`, `EntityNeighbour.cs` | Body radii (by `EntityKind`) and neighbour records |
| `Physics/SpatialIndex.cs` | XZ hash grid of entity positions with deterministic query order |
| [`Assets/StageCollisionReader.cs`](../Shared/PristonTale.Assets/StageCollisionReader.cs) | Reads collision triangles from a stage `.smd` |
| `Geometry/CollisionMesh.cs` | Immutable triangle grid: floor height, floor raycast, segment trace |
| `Assets/AssetFiles.cs` | Case-insensitive asset path resolution and assets root discovery |

`PristonTale.Geometry` has no dependencies and is referenced by `PristonTale.Simulation`, `PristonTale.Assets` and
the client's `PristonTale.Engine3D` (`Physics/PristonCollisionWorld.cs` wraps the same `CollisionMesh` in MonoGame space).

## Coordinates and units

| Quantity | Convention |
|---|---|
| World position | `Vector3` in Priston world units, stage file axes, Y up. The server and all rules here use these axes. |
| Fixed point | Stage vertices and NPC placements are stored ×256; divide by 256 to get world units. |
| Client space | MonoGame `(x, y, -z)`; conversion happens in client code, never in these rules. |
| Facing | Radians; forward is `(sin f, 0, cos f)`, so `f = atan2(dx, dz)`. |
| Authored and wire angles | 4096 steps per turn (`(angle & 4095) * Tau / 4096`). Monster side probes and stuck turns use this circle. |
| Time | Rules take `dtMs` (float milliseconds) or a field clock in ms; no wall clock except the boss schedule. |
| Distances | Most checks are planar (XZ) with a separate vertical gate; aggro uses full 3D distance. |

Coordinates in field definitions: `FieldPoint`, `FieldGate`, `FieldWarpGate` and `SpawnPoint` are integer world units.
`FixedNpc` X/Y/Z are fixed point (`WorldNpc.Place` divides by 256). `FieldLight` is in client engine units and is
presentation only. Spawn points carry no height: the host places them on the highest floor.

`PlayerNearRadiusSquared = 0x120000` (about 1086 units, in `FieldInstance`) is the "a player is near" radius for
spawning and despawning; the monster skill area test uses the same value.

## Fields

`FieldDef` is a code-defined record, one static `Definition` per field in `World/Fields/`. `WorldCatalog()` lists every
field; `WorldCatalog(IEnumerable<FieldDef>)` builds test catalogs. Lookups by `Id` (string, the stage base name such as
`village-2`) and by `FieldCode` (int). The server keys live fields by `FieldCode`; both must be unique. All 45 fields of
the original are there, the game room (`office`, 16, without a map) and Bellatra's second stage (`sod-2`, 44) included.

| Member | Use |
|---|---|
| `StageSmd` | Asset-relative stage path with Windows separators; source of collision and of the importer's join key |
| `StageObjectSmds` | Animated stage props; client only |
| `State` (`FieldState`) | `Village` sets the player's in-village flag on entry; `Dungeon` sets `Underground` (no sky) |
| `Center`, `StartPoints` | `StartPoints[0]` is the NPC teleport arrival; a field restart stands up at the start point nearest to the fall, a town restart at a random one; `Center` is the fallback |
| `Gates` | Seamless boundaries to an adjacent field (`DestinationFieldId`, X/Z, angle) |
| `WarpGates` | Portals: trigger X/Z/Y and Width/Height, destination field and X/Z/Y, `LimitLevel`, `SpecialEffect` (1: a delayed warp with the return sparkle; 2: a wing warp stone, its destination point the stone's exit in its own field; see [Wing warp gates](#wing-warp-gates)) |
| `LimitLevel` | Minimum character level to enter (gates, warps and teleports): Desert 3 (11) 55, Greedy Lake (34) 70, Iron 1 (27) 80, Iron 2 (28) 85, Eura (29), Ice 1 (31) and Ice 2 (35) 90, the Lost Temple (38) and both floors of the Endless Tower (39, 40) 100 |
| `Ambients`, `Lighting`, `Camera`, `DayNight`, `Backdrop`, `BackMusicCode`, `OnLoaded`, `OnUpdate` | Client presentation |
| `FieldEvent` | `Nightmare` (Navisko): see Night and day |
| `HazeReduction`, `SightCells` | The share of the distance haze the field clears (Ricarten 0.2) and the 64-unit cells of sight added before it starts (Ice Ura 1); client presentation |
| `ServerCode` | Authored label; no runtime rule reads it |
| `Spawns` | Defaults to empty and is not used by the server; spawn data comes from the database (see Spawning) |

`WorldCatalog.Neighbours(field)` returns fields linked by a gate in either direction; the client uses it to stream
adjacent stages.

### Field transitions

`FieldTransition.TryResolve` (in `Shared/PristonTale.Application/World/`) authorizes a field change from the player's
server position; the requested position is never trusted. Rules:

- Refused for an invalid origin, the same field, or `level < destination.LimitLevel`.
- Warp: a source warp gate to the destination, not a wing warp stone, `level >= warp.LimitLevel`, origin within
  `max(Width, 48) + 64` planar units of the trigger. Arrival is the warp's authored destination point, settled on the
  floor within 64 units.
- Gate: a gate between the two fields (either direction) within 1810 planar units of the origin. The player keeps the
  position.

## Stage collision

### Stage file subset

`StageCollisionReader.Read(byte[])` reads only what collision needs. All integers little endian.

| Part | Layout |
|---|---|
| File header | 556 bytes. ASCII signature in the first 24 bytes: `SMD Stage data Ver 0.72` or `... 0.73`. Material count (int32) at offset 28. |
| Stage block | 262,260 bytes after the header. Vertex count and face count (int32 each) at block offset `4 + 256*256*4 + 48`. |
| Material group | 88-byte header; the declared material count (uint32 at +8) must equal the file header count. |
| Material | 320 bytes: in-use flag (uint32 at 0), state flags (uint32 at 168; bit 0 = check face). |
| Name table | Follows each in-use material only: int32 length, then that many bytes of texture names. |
| Vertex | 28 bytes; X, Y, Z as int32 fixed point at offsets 8, 12, 16. |
| Face | 28 bytes; vertex indices (uint16) at 8, 10, 12; material index (uint16) at 14. |

Only faces whose material has the check-face bit become triangles; faces with out-of-range indices are skipped.
Malformed files throw `InvalidDataException`.

### CollisionMesh

- Every check-face triangle blocks (segment traces); triangles with `|normal.Y| >= 0.25` are also floors.
- Two XZ grids (floors, all triangles). Cell size `sqrt(area * 8 / triangles)` clamped to 32-512 units, one spare
  cell on each side; a triangle is registered in every cell its bounds overlap.
- `TryGetFloorHeight(x, z)` returns the highest floor under the point; the overload with `referenceHeight` and
  `stepUp` ignores floors above `referenceHeight + stepUp` (ceilings, upper storeys).
- `TryTraceSegment` returns the nearest hit and the triangle normal. The normal is unoriented (follows winding).
- Immutable after construction, so one mesh serves concurrent queries from parallel field steps.

### FieldTerrain

One per field; wraps an optional mesh. `FieldTerrain.None` (no mesh) accepts every query and keeps the caller's
height. Constants: `StepUp = 10`, `BodyProbeHeight = 30`, `DropTolerance = 32`.

| Method | Rule |
|---|---|
| `PlaceOnFloor(p)` | Highest floor under X/Z, any height. Used for spawns, summons, start and respawn points. |
| `TryPlaceOnFloor(p, out q)` | As `PlaceOnFloor`, false where the stage has no floor. World entry uses it: a saved place without a floor enters at the field's first start point (or `Center`). |
| `TryFloorNear(p, climb, out h)` | Highest floor at or below `p.Y + climb`. |
| `SettleNear(p, tol)` | Snaps to a floor in `[p.Y - tol, p.Y + tol]`, else keeps `p`. Used for drops, knockback, warp arrival. |
| `CrossesWall(a, b)` | Segment from `a` to `b` (at `a`'s height), both raised 30 units, hits any collision triangle. |
| `TryStep(a, b)` | No wall crossing and a floor within `StepUp` above `a.Y`; falls are allowed. NPC steps use it. |
| `TryBodyStep(a, b, halfWidth, stepUp)` | Centre path leads by `halfWidth`; both flanks swept. A flank may end nearer a wall only if it started nearer. |
| `Slide(a, b)` | Replica of the client wall slide: stop 0.5 units before a wall, project the rest along it, three tries; faces as flat as floors do not stop it. |

### Loading

The server's `FieldTerrainLoadService` (`Server/PristonTale.Server/Hosting/`) reads every field's `StageSmd` in
parallel at startup and publishes them once to `FieldTerrainCatalog` (Application). A missing assets root or stage stops
startup unless `Assets:AllowMissingTerrain=true`, which leaves those fields on `FieldTerrain.None`. See
[server.md](server.md).

## Movement and body collision

| Mover | Rule | Code |
|---|---|---|
| Player (server check of a report) | Distance budget, wall slide in 48-unit pieces, a floor at most 24 units above each 8-unit step; height follows the floor and falls (`Falling`) | `WorldPlayer.ReportMovement` (Application) |
| Wild monster and summon | `MonsterStepping.Step` against stage and other monsters only | `FieldInstance.MoveMonster` |
| NPC | `EntityCollision.Resolve` against all bodies, then `FieldTerrain.TryStep` | `FieldInstance.StepNpc` |
| Local player (client prediction) | `EntityCollision.Resolve` with player radius, then client terrain | `Client/PristonTale.Gameplay/World/GameScene.cs` |

`MonsterStepping.Step(terrain, from, heading, distance, occupancy, out accepted)`:

1. Probe the requested heading at the full distance with `TryBodyStep(halfWidth 12, stepUp 20)`.
2. Else probe heading -768 and +768 steps (67.5 degrees) at half the distance. Facing is not changed by the host.
3. Result `Moved`, `Slid`, `Crowded` (every terrain-legal probe ended on another monster) or `Blocked`.

Occupancy is monster against monster only: another live monster within 32 units planar and 32 vertical. A monster
already overlapping another may walk out. `Crowded` steps do not count as stuck in the AI.

`EntityCollision.Resolve` is all or nothing: it returns the old position if the swept segment newly enters any
neighbour circle (vertical overlap within the radius), and allows leaving an existing overlap. The pair distance is
`min(moverRadius, neighbourRadius)`, not the sum. Radii: player 16, monster 32, NPC 32.

`SpatialIndex` is a hash grid (default 256-unit cells, cell indices clamped to ±1,000,000). `Put` keeps an entry's
first `Order`; `Query` returns candidates sorted by `Order`, which keeps neighbour iteration deterministic.

## Monsters

`MonsterDefinition` is built from the database by `WorldDataFactory.ToMonsterDefinitions`
(`Persistence/PristonTale.Persistence.Abstractions/WorldData/`):

- `Code` is the monster's registry id ([world-import.md](world-import.md#identities)); 0 means unset. Spawn tables
  and boss spawns hold the same id. `Key` is its unique English name, which crystals and skill summons use.
- `Faction` is `Enemy` for a hostile monster (`*속성 적`), `Npc` otherwise; `Name` is the display name (the key when
  empty); `ModelInxPath` is normalized to `.inx`. `ShadowSizeLevel` is the authored shadow size.
- `DeathGold` with `DeathGoldChance`: gold dropped on death on top of the drop table. `EventCode`/`EventInfo`: event
  creatures (code 100 walks at night in field `EventInfo`). `EventModelInxPath`: the model of the hammer event; a monster
  with one gets an event form after every authored monster in the catalog (`EventFormCode`: the same monster, normal
  class, wearing that model). `MagicResistance` (-100..100) cuts a spell's damage like an element resistance (100 none
  but the least hit, -100 double): a spell is a damage rule whose power grows with spirit or spell power
  (`SkillDamageRule.Magic`), applied after its element's resistance (`SkillFormulas.PowerAgainst`; the main target's
  own spell hit of an area skill, `MainSpellPowerAgainst`, skips it). `MoveType` and
  `SpecialAttackPercent` are kept as authored (the original reads them and never uses them).
- `Combat.MeleeRange` is the attack reach; `Combat.DamageStunPercent` the flinch chance; `Ai.SightRadius` the notice
  radius; `Ai.Iq` enables running; `Ai.GroupSize` the spawn group. `Ai.MoveSpeed` is only sent to clients for
  presentation; `Ai.MoveRange`, `Ai.Nature` and `Ai.ActiveHour` are imported but no rule reads them.
- `SoundCode` is the original sound family code and doubles as a behaviour key (see Pitfalls).
- `PotionCount`/`PotionPercent`: potions a spawn may carry (Valento, Kelvezu and Mokova use the count as lives, see
  Monster skills). `Skill` keeps curse-only skills (no damage, no area, a rating and a curse code).
- `MonsterCatalog.TryFindKey(key)` matches the key case-insensitively; `TryFind(name)` (game-master commands, skill
  summons) matches the key, else the display name, and returns the lowest code (an event form shares its key).

`MonsterInstance` holds live state: `Position`, `Facing`, `Life`, `AiState`, `Motion`, `HomeAnchor` (spawn position),
`SpawnPointIndex` (-1 for summons), `TargetId`, timers, manoeuvre counters, `Effects` (modifier container stepped by the
field), summon fields (`OwnerId`, `ExpiresAtMs`, `Aggressive`, `ModelScale`) and the swing and skill state
(`ActionSlot`/`ActionKey` of the clip playing, its blow times, `Potions`, `VirtualLives`, `AttackAnger`, the ally a
power-lending skill aims at). `ApplyDamage` spends a virtual life at 10 % life or less, otherwise clamps at 0 and
enters `Dead`. `PresentedMotion` reports `Stand` instead of walk/run on ticks where the host did not actually move it.

Hammer event (`FieldInstance.Hammer`): a hammer-class monster (`MonsterClass.Hammer`, 200) counts the blows it lands in a
row on one player without a big head; the tenth gives the player a big head (mode 1 or 2) for 600 s with the head-change
burst, and the monster stops counting (`HammerSpent`) and, when it has an event form, is replaced by it after the monster
step with the life it had left. Its attack sound is the hammer sound of its bank (the description carries the class).

## Monster AI

`MonsterAiBrain.Tick(monster, tuning, prey, dtMs)` mutates the instance and returns a `MonsterAttackEvent` when a swing
or skill lands. The host then re-resolves the position the brain wrote (see Movement) and resolves the hit.
`MonsterAiTuning` is built per definition with its `MotionTimingSet` and cached per field by monster code.

States (`MonsterAiState`): `Idle`, `Wander`, `Chase`, `Leash` (a fixed-heading manoeuvre), `Attack`, `Dead`.

| Constant | Value | Meaning |
|---|---|---|
| `DecisionIntervalMs` | 60 | Decision cadence; between decisions the monster keeps moving |
| `RetargetIntervalMs` | 900 | Target scan cadence (suspended only while a boss target lock runs) |
| `VerticalAggroGate` | 140 | Prey this far above or below is ignored |
| `RunDistance`, `RunIqThreshold` | 512, 6 | Runs if `Iq >= 6`, not engaged and target closer than 512; a model without a run clip (`HasRunClip`) walks instead, at walking speed. Summons run to their owner and prey under the same clip rule |
| `StopMargin`, `ChaseHysteresis` | 8, 24 | Approach to `MeleeRange - 8`; engaged flag clears beyond reach + 24 |
| `DefaultLeashRadius` | 1280 | Planar distance from `HomeAnchor` that starts a leash manoeuvre |
| `ProgressProbeClips`, `StuckDistance` | 5, 32 | Every 5 locomotion clips, less than 32 units of progress means stuck |
| `LeashManoeuvreClips`, `StuckManoeuvreClips` | 6, 10 | Manoeuvre lengths in locomotion clips |
| `BossTargetLockMs` | 4000 | Boss keeps a new attacker as target this long |
| `FlinchFallbackMs`, `LocomotionCycleFallbackMs` | 700, 1000 | Used when no clip was imported |

Behaviour, in tick order:

1. Drinking a potion (`Eat`) or changing shape (`Warp`) plays out its clip, even under a stun; the host completes it.
   Stun or charm: stand still; an attack in progress is cancelled without landing. Slow: `dtMs` is scaled by
   `(100 + MoveSpeed%) / 100`, clamped 0.05-1, so movement, decisions and swings all slow down.
2. Flinch (`Damage` motion) blocks everything until `FlinchMsLeft` drains, then chase (target alive) or idle.
3. Attack state advances the swing (see below).
4. Target scan: nearest live prey by 3D distance within sight (scaled by the prey's `SightPercent` for stealth unless
   the monster is one of the four unyielding bosses), skipping prey beyond the vertical gate.
5. Clip bookkeeping for `Chase`/`Leash`: the progress probe can start a stuck manoeuvre (turn toward home ±45 degrees,
   run if able, 10 clips); a moving monster beyond the leash radius starts a leash manoeuvre (head home, 6 clips). Both
   keep the target and end in chase.
6. Decision: no target means idle; a new spawn walks forward along its spawn facing for two walk clips, then stands
   (idle monsters do not wander again). With a target: attack if `MonsterAttackReach.CanBegin` (planar distance below
   `MeleeRange`, height difference at most 64), else walk or run to a point `StopDistance` short of the target.
   A target in reach is attacked even during a manoeuvre.

Attacks: on entry the attack clip is drawn uniformly among the model's attack clips, then the skill roll
`rand(100) < SkillRatingPercent` (any definition with a rating and a skill clip, whatever its family) draws a skill clip
the same way. The clip's slot, key, duration and blow time drive the swing (`MonsterAiTuning.AttackClips`/`SkillClips`).
The hit fires at the blow time: a skill always fires (area placed at cast; the host filters victims); a basic attack
lands only if the target is alive and `MonsterAttackReach.CanLandBasic` (vertical at most 64, planar distance squared
below 48×256, or below 460² for ranged sound families). At clip end a hurt monster with potions drinks one and a
shape-shifter starts changing (see Monster skills); otherwise it swings again at once if the target is still in reach,
else chases or idles. Every blow (landed or not) adds one to `AttackAnger`.

Timing: with an imported attack clip, cooldown is the clip duration at rate 1 and windup its first event; without one,
`rate = 256 + 32 * clamp(AttackSpeed - 6, 0, 6)`, cooldown `1000 * 256 / rate` ms, windup `cooldown * 40 / 72`.
Babel's and Valento's skill blows land 120 frames (1714 ms) into the clip; Babel's normal attack strikes at its event
and again 40 frames later. Flinch duration is the damage clip at `MotionCodes.DamageAndEatRate`; drinking and changing shape use
their clips at rate 1 (one second without one); walk and run cycles come from their clips.

Host-called rules:

- `ApplyFlinchOnHit`: needs `damage * k >= MaxLife` (`k` 5 above level 14, else 10) or a critical, then
  `rand(100) < DamageStunPercent`; cancels a swing in progress.
- `ApplyBossRetargetOnHit`: a boss without an active lock takes the attacker as target for 4000 ms.
- `MonsterAiTuning.IsUnyielding`: the four great bosses by sound code; skills never knock back or taunt them and they
  see through stealth.
- A definition with `MeleeRange == 0` is immobile and never attacks (`IsImmobile`).

Speed (`MonsterLocomotion`): 70 units/s per step; a Dark Knight boss has 2 base steps; running adds one step (Greven 4,
Wolverlin 2). The brain integrates toward its target at this speed; terrain then accepts or slides the step.

### Monster skills

`FieldMonsterSkills` (Application) completes what the brain starts, on the field tick: `AfterTick` after each wild
monster's brain step (swing start rules, the `SwingBegin` event with the clip slot and skill flag, finished potions and
shape changes, spent lives, Babel's regeneration), `ResolveSkill` for skill blows, `BeginBlow`/`AfterBlow` around normal
blows on players. Rules live in `Simulation/World/Monsters/Skills/`. Statuses go through
`FieldSkills.ApplyMonsterStatus` with `EffectSourceKind.MonsterSkill`, so they are reported, timed and cleared on death
like skill statuses.

Generic skill: `SkillRange == 0` strikes the swing's target only (within 460 units); otherwise a circle of radius
`SkillRange` around the caster (3D distance, height below 140) or, with `SkillDistance`, a square of half-size
`SkillRange` centred `SkillDistance` ahead. A skill blow never misses and is not blocked: the power roll less the
player's absorption, at least 1 (a curse skill with no damage strikes for 1). The mystic's skill deals none.

Statuses on struck players (durations in 70 Hz frames; a resisted one lasts `frames x (100 - resistance) / 100`, at
least 4 frames):

| Family | Trigger | Effect |
|---|---|---|
| Web | normal blow | `WebSlow` 2 s: movement and attack clips at 180/256 |
| Cold Eye, Frozen / Frost, Valento | skill | `IceSlow` 4 s / 6 s at 200/256, ice resisted |
| Stingray (skill), King Bat (every blow) | skill, blow | `Poison` 5 s, poison resisted: 1 life per 2 frames (paid 7 per 14 frames), never the last |
| Bloody Knight, Chimera | skill | `Stun` 3 s: no walking (reports are ignored), swings or casts |
| Chaos Cara | skill with a clip key other than J | mana loss equal to the power roll |
| Succubus family above level 75 | every blow | 6 % of the maximum stamina (below level 80) or mana |
| Fire, lightning, ice tower crystal | skill | power resisted by fire; `LightningStun` 3 s unless stunned; `IceSlow` 4 s |

Curses (skill start, `CurseSkillCode`): 101 lowers the target's absorption by 2..6 (Mystic 10..16) for 10 s; 103
lowers its damage dealt by 10..15 % for 10 s; 102 aims the skill at another live monster within 200 units (height 80)
without `Power` and lends it 10..20 % damage dealt for 320 monster updates (18.3 s) when the clip ends; without one the
swing stays a normal attack. A curse monster drops its player after each normal blow and finds it again on its next
scan.

Boss and special rules:

- Babel: each normal blow has a 60 % chance to also strike the players within 100 units (height 60) of its target with
  half its damage roll, and then a target farther than 200 units takes 70 %. While hunting a player it regenerates once
  per clip that runs 120 frames: a table row per player within 260 units beyond six (at most nine), 1800..2800, or
  2300..3300 below 20 % life.
- Valento, Kelvezu, Mokova: `PotionCount` lives; at 10 % life or less one is spent and life refills (a lethal blow
  included). On every even number of lives left after a spend, Kelvezu calls two Chaos Cara and Mokova two Dark Guards
  within ±100 units, at most 8 monsters on the boss's spawn point. Views show one bar of `(lives + 1) x 100` points.
  Reinforcements and bees are the family's ordinary definition of the lowest level.
- Potions: a spawn carries `rand[1, PotionCount]` potions with `PotionPercent` chance; below 30 % life at a clip end it
  drinks one (`Eat` clip) and heals half its life when the clip ends (`PotionApplied` event). Quest duel opponents keep
  their own potion rules ([quests.md](quests.md)). The magic master drinks a mana potion instead of 20 % of its casts.
- Shape-shifters (Hungky, undead hopy, chick): below 30 % (hopy 20 %) life or after 80 blows they play their `Warp`
  clip, then the server replaces them with a full-life monster of the other form at the same place: Hungky a hulk below
  level 80 (80 and more when angry), the hopy a king hopy of level 108 and more, the chick its parent.
- Vampiric Machine: never moves; its blow sends a Vampiric Bee out instead of striking, and it waits while four
  monsters stand on its spawn point.
- Mystic: explodes when it dies, or by itself after 1920 updates (109.7 s): its skill damage on every player within
  `SkillRange`, double on a summon it fought.
- Ratoo's skill ends the stuns of the monsters within 200 units (height 80).
- Against other monsters (a wild boss on a summon, or a player's summoned boss on a wild monster), Babel, Chaos Cara,
  Valento, Mokova and Kelvezu deal 8 times their roll and Kelvezu's blow kills; the Guardian Saint's skill strikes every
  monster within its `SkillRange`. Valento drops a summon farther than 120 units.

Clients play each swing's clip from the `SwingBegin` slot and draw its effects from the clip itself
([effects.md](effects.md#monster-effects)).

### Summons

`SummonBrain.Tick(summon, tuning, orders, dtMs)` takes `SummonOrders` (owner position, target) from the skills layer.
It runs back to the owner (dropping the target) beyond 300 units with a target or 150 without, stops at 90; attacks
when within `max(80, MeleeRange + 32)` planar and 64 vertical; uses the same clip timing. `NeedsRecall` (500 units)
tells the host to place it beside the owner, at one of the eight spawn points around him that no monster stands on
when one is free (`FieldSkills.TryRecallBeside`). Only stun stops it. Wild monsters see summons as target-only prey
(they fight a summon that hits them, never pick it by sight). Summon skills are documented in [skills.md](skills.md);
crystal pets follow the same orders ([special-items.md](special-items.md#monster-crystals-and-soul-stones)).

## Night and day

`WorldClock` (Simulation): one game minute lasts 800 ms, a day 19.2 real minutes; night runs from 23:00 to 4:00. The
server keeps the clock (`WorldServer.GameClockMs`, the Unix time plus `world.clock_offset_minutes` game minutes) and sends
it in `WorldJoined`; each tick it sets `FieldInstance.Night` for every field.

`FieldInstance.Night` (`FieldInstance.Night.cs`), applied at the first step and at each change:

- Event creatures (`MonsterDefinition.EventCode` 100, field `EventInfo`: the bronze, silver and golden wolverines) walk
  at night at a random active spawn point of their field and come back when killed (once the corpse is gone); they stay
  until dawn even with no player near (`StaysUntilDawn`). At dawn they play their warp clip and leave (`VanishOnWarp`);
  clients show the return sparkle with the skill-learn sound for a wolverine's warp.
- NPCs with the event creature service (`NpcServiceKind.EventMonster` 100: the town's wolverine pets) leave at night and
  return at dawn.
- A nightmare field (`FieldDef.FieldEvent` = `Nightmare`: Navisko) spawns its monsters only at night; at nightfall each
  of its people turns into the night creature (the monster whose model is `Wolverin_A`) where it stands; at dawn its
  hostile monsters die without reward and its people return, those with an appearance draw (`NpcDefinition.AppearSlot`,
  `AppearCycle`) only when the dawn's new draw picks them.

## Weather

`WeatherSchedule` (Simulation) is the original's rain on the world clock: in every cycle of `world.weather_cycle_minutes`
game minutes (8192 by default, about 1 h 49 min) the sky clouds over after minute 512, rains from 60 minutes later until
60 minutes before minute 932 (4 real minutes of rain), then clears. The original always rained the same; here each
cycle's number draws its rain (`WeatherState`, Contracts): light, steady or heavy rain, or a storm (rain with lightning
and thunder), with a wind heading and strength, so every restart agrees on it. The rain does not fall evenly
(`WeatherSchedule.Varied`): it starts at a third of the cycle's rain and swells to it over 30 game minutes, eases off
over the last 30, and between them comes and goes in spells of 15 game minutes (60 % to all of it, eased from one
spell's draw to the next), in steps of 8, so the server sends a new weather about once a spell. Dungeons, rooms, the ice fields and
Bellatra never take rain (`WeatherSchedule.Rains`, set as `FieldInstance.Rains` when the field opens).

Each tick `WorldServer` takes the weather (a game master's `/weather` while one is set, else the schedule while
`world.weather` is on, else clear) and `FieldInstance.SetWeather` keeps it for a field that takes rain, clear otherwise.
Every snapshot carries the field's weather and the encoder sends `Weather` when it differs from the last one sent (the
first snapshot, a change, a field of another sky).

## Spawning

`FieldSpawnData` per field code comes from the database (`WorldDataFactory.ToFieldSpawnData`, imported from the field
spawn files; see [world-import.md](world-import.md)): `SpawnPoints` (X/Z, `Active`), weighted `SpawnTable`,
`BossSpawns` (boss, escort, escort count, hours), `FixedNpcs`, and knobs `MaxConcurrentMonsters` (10),
`MaxPerSpawnPoint` (3), `SpawnIntervalMask`, `PerPointCooldownMs`, `SpawnPassIntervalMs` (1800), `IdleDespawnMs`
(15000), `BossIdleDespawnFactor` (32). Unresolved monster or NPC names are dropped at load.

`FieldSpawner.MaintainPopulation`, once per pass:

1. Pass timing: every `SpawnPassIntervalMs`, or with `SpawnIntervalMask` set, `SpawnCadence` on a 70 Hz frame clock
   fires on the next frame number with no bit in the mask; missed opportunities after a hitch are skipped.
2. Stop if wild live monsters (summons excluded) reach `MaxConcurrentMonsters`.
3. Pick one definition by weight (half-open cumulative roll).
4. Pick the next spawn point round-robin that is active, below `MaxPerSpawnPoint`, off cooldown and has a player near.
5. Group size `rand[GroupMin, GroupMax]` (at least 1), capped by field and point room. Leader on the point, others
   within 64 units, random facings. The host places each on the floor. The point's cooldown starts.

`MaintainDespawns`: a live wild monster with no player near loses `dtMs` from `IdleDespawnMsLeft` (bosses get
`IdleDespawnMs * 32`); a player near resets it; expired monsters are returned for removal.

Bosses: `BossSpawnSchedule` (one per server) picks a random minute 1-45 each local day and opens a pass at that minute
of every hour, at most once per hour slot and never within 10 minutes of the last pass. `WorldServer` hands the hour to
every live field; `SpawnScheduledBosses(hour)` spawns each `BossSpawn` whose hours contain it at the first active
point with a player near (else the first active point), plus escorts scattered within 64 units.

## Loot and kill rewards

`DropTable` entries are `Nothing`, `Gold` (min-max) or `Item` (runtime item code), each with a weight and `IsBonus`.
Built by `WorldDataFactory.ToDropTable`: unresolved main drops stay as `Nothing` slots so probabilities keep their
shape; unresolved bonus drops are omitted.

`DropRoller.Roll(rng, table, hasEventItem, eventItemCode, extraRollChance, eventRoll)`:

1. Optional event item first, flagged `ToInventory` (ground loot rejects it; it needs its own reward transaction).
2. Each bonus entry rolls independently: `rand(10000) < Weight`.
3. `MaxItemsPerKill` weighted picks from the main entries (`Nothing` picks add nothing). Gold amount is
   `rand[GoldMin, GoldMax]` inclusive. A running item event (`eventRoll`, `ItemEventDrops`) may replace each pick with
   its item; a puzzle piece is flagged `Sealed` ([special-items.md](special-items.md)).

Quest and ingredient items (`QuestItems.IsQuestDrop`) are flagged `ToInventory` with `FromMainTable` set for main-table
picks; the field hands them to the credited player's quest upkeep instead of the ground ([quests.md](quests.md)).

`KillReward.ExpForKill(baseExp, monsterLevel, playerLevel, multiplier)`: `baseExp * multiplier`, then 100 / 85 / 70 /
55 percent for a level gap of at most 10 / 20 / 30 / more, computed in 128 bits and saturated at `ulong.MaxValue` (no
award wraps or throws). The server passes `experience.multiplier` and the experience event (`experience.event_percent`: +100% up to 199, then
+200%, +300%, +400% from 400).
`FirstAttackerStealsCredit`: the first attacker gets the kill over another killer if it dealt at least 40% of max life
and hit within the last 15 s (`FirstAttackerDisengageMs`).

`FieldCombat.FinishDrops` then drops every item over its daily limit (`DailyDropLedger`: one count per item and server
local date, shared by all fields) and adds the monster's death gold when its chance rolls.

Drops settle on the floor within 32 units of the corpse. `DropTable.AllPlayersSeeDrops` makes a drop public at once;
lifetimes and the owner window live in `FieldGroundLoot` ([application.md](application.md)).

## Motion timing

INX motion tables are extracted by the importer (`MotionTableExtractor`) into the database and loaded as a
`MotionTimingCatalog` keyed by `NormalizeKey(modelPath)`: extension `.inx`, forward slashes, lower case.

- `MotionClip`: `StartFrame`, `EndFrame`, four event offsets in ticks, `Repeat`. `Frames = EndFrame - StartFrame`.
  `TicksPerFrame = 160`; an event is valid only if `0 < ticks <= Frames * 160`.
- `MotionTiming.DurationMs(frames, rate) = frames * 33 / rate`;
  `EventOffsetMs(ticks, rate) = ticks * 33 / (160 * rate)`.
  `MotionCodes.DamageAndEatRate = 92 / 80` (Contracts) is used for flinch clips and the potion (`Eat`) clip, by the server's timing and the renderer alike.
- `MotionCode` (`Shared/PristonTale.Contracts`, shared with the renderer): Stand 0x40, Walk 0x50, Run 0x60, Attack 0x100,
  Damage 0x110, Dead 0x120, Eat 0x140, Skill 0x150, Restart 0x200, Warp 0x210, Yahoo 0x220, and the talk codes from
  0x400. `MotionCodes.Of(MotionState)` is the one mapping from a motion state to its clip code.
- `MotionTimingSet.TryGetClip(state)` returns the first clip of the state (the one the client plays);
  `ClipsForState` returns all (NPC stand variants). Zero-length clips are dropped.

Player action timing (`FieldInstance.PlayerActionTiming`): start from `PlayerMotionFallback.GetClip(class, state)`,
replace with the job body's catalog clip, and for attacks replace with `PlayerEquipmentTiming.TryGet(class, weaponKey)`.
Attack rate is `Character.AttackRate`. The hit lands at event 0, or at the clip end if there is none. The flinch lasts
the damage clip found this way (`WorldPlayer.FlinchMs`, set when the player enters a field).

`Data/PlayerEquipmentTiming.json` is generated from the shipped INX files by the test runner
(`--export-equipment-timings <assetsRoot> <outputFile>`; `--verify-equipment <assetsRoot>` checks it).

## NPCs and services

`NpcDefinition` comes from `WorldDataFactory.ToNpcDefinitions`, one per NPC definition file: `Code` is the file's
registry id, which field placements hold, `Name` its unique English key, `DisplayName` the English name (`Label`
falls back to `Name`), `Dialog` its English lines, `Services` in authored order with a bit set `ServiceSet`
(`1 << kind`), `Shop` stock in three tabs (weapons, armour, misc), `MoveRange` (default 64). A shop service without runtime stock is removed.

NPC placement and wandering (`WorldNpc`, Application): placed from `FixedNpc` (fixed point, 4096-step angle); walks
only with `MoveRange > 0` and a walk clip, with a 25% chance at the end of each stand clip, at 70 units/s.

| Type | Contract |
|---|---|
| `NpcServiceKind` | Stored in `npc_services.kind`, sent in the dialog and as bit `1 << kind`; append only. Argument meanings per member. |
| `NpcServiceAvailability.IsAvailable` | Running services: `Shop`, `Warehouse`, `SkillMaster`, quest givers and scripts, `ItemAging`, `ItemMix`, the wing exchange, the puzzle collector, `Teleporter` with a known route. Others are offered but unavailable. |
| `NpcInteraction` | Dialog reply; `Reach = 96` planar units for NPC service requests |
| `NpcServiceCommand` | `OperationId` (idempotency), NPC, service, action, expected revisions, item, `Anchor`, `Count`, `Amount`, `Materials` (compared by value) |
| `NpcServiceAction` | Persisted in the operation history and sent on the wire; append only. `IsDurable` is false for Open, Close, Refresh. |
| `NpcServiceStatus` | Outcome codes on the wire (`Ok` ... `NotOpen`, `OverWeight`); append only |
| `NpcServiceReply` | Outcome plus the view after it: shop catalog, quotes, warehouse, teleport destinations, arrival, the work on an item (`ItemWorkResult`) |

Shop rules (`NpcShopRules`):

- Buy: catalog price times count (count 1, or 1 to `MaxStack` for stackables); needs gold, a free cell and room under
  the weight limit (`OverWeight`; a potion weighs one per unit). The bought copy is the shop form of the item
  (`ItemRoller.Lower`, see [simulation.md](simulation.md#items)).
- Worn value: `intact = price * dur / maxDur` (0 counts as 1), `worn = intact + (price - intact) / 4`, where price is
  the copy's own price (a specialized drop costs a fifth more than its kind).
- Sell: `min(price / 5, worn / 5)`; stackables `(price / 5) * count` for the count of units asked (1 to the stack's
  count; an item sells whole); quest items and the quest weapon and ring a
  running rank-up holds cannot be sold; refused with `GoldLimit` when gold would exceed
  `MoneyLimits.CarryLimit(level, tier)`.
- Repair: `(price - worn) / 8` for items below max durability; requires gold strictly greater than the cost.
- Carry limit: 200,000 up to level 10; tier 0 `max(200,000, level * 200,000 - 1,800,000)`; tier 1 10M; tier 2 50M;
  higher 100M.

Warehouse (`WarehouseStorage`, one per account): 9×9 grid, at most 100 items, weight 1000, gold 100,000,000.
`CanStore` keeps out what the original does: what `BoundItems.CanHandOver` refuses (the bound items and the SP1 and CH1
families, which a trade refuses too) and potions (`RefusedOnSetDown`: the client refuses them only when set down). Deposit, Withdraw and Move set an item down at a cell; over one other item
the two are exchanged (`Inventory.Covered` finds it from the footprint; the request's `Covered` must name the same item,
or the client's view was stale), the covered one going to the moved item's cell or wherever its side has room.
`Inventory.CanExchange` checks both sides before either changes. Every change increments `Revision`;
`Clone` supports prepare-then-commit. A withdrawal that would carry the character over its weight limit is refused
(`OverWeight`), and an overweight character cannot open the warehouse (`NpcServiceTransactions.Check`).

Teleport routes (`TeleportRoutes`): route 0 means route 1. Route 1: index 1 field 0 "Mushroom Cave" 100 gold, 2 field 18
"Beehive Cave" 100, 3 field 7 "Cursed Land" 500, 4 field 12 "Forbidden Land" 1000. The destination field's
`LimitLevel` applies; arrival is its first start point (or `Center`) placed on the floor. The cave destinations are the
fields that hold the cave entrances: field 0 (`fore-3`) has the warp gate to `Tcave`, field 18 (`forever-fall-03`) the
one to `Mcave`.

## Aging

`AgingRules` and `AgingWork` (Simulation, `Items/Aging/`) hold the rules; each ageable family is one `AgingFamily` row
(counter, goal table, wear, and what a level adds: damage, attack rating, odd-level critical or block, mana, absorb
step, defence percent), which both a level gained and a fall read, and which `AgingRules.RaisedBy` turns into the
stats the item tooltip marks (`AgedStats`; bows and javelins also mark mana, as the original tooltip does); `NpcServiceTransactions` runs an aging
(`NpcServiceAction.Age`) on a candidate copy; `FieldInstance.Aging` counts maturity in the field.

- Ageable: axes, claws, hammers, staves, poles, bows, swords, javelins, armours, robes, shields, orbs, gloves, boots,
  armlets and the aging ring `OR201`; not unique items, quest items, costumes and timed items of those families, mixed
  copies, copies still maturing or copies at +20.
- The NPC takes the item from the bag, exactly the sheltoms its level asks (`SheltomsFor`: five to twelve of Lucidy..Ordo,
  laid out in four rows of three) and at most one aging stone (`BI110` aging stone, `BI111` copper ore, `BI160` elder
  copper ore, `BI161` super aging stone, `BI185` superior copper ore), for `(level + 1) * price / 2` gold. Materials and
  gold are spent whatever the roll gives (`WrongMaterials`, `NotEnoughGold` otherwise). `aging.price_percent`,
  `aging.failure_percent` and `aging.double_percent` scale the price and the chances (`AgingSettings`).
- The roll (`Roll`, three draws of 0..100: destroy, down, plus): a failure by the level's chance (none to +4, 95% at +18
  and +19; lower with the aging stone, the super stone and the superior ore) falls one or two levels or loses the item
  by the level's split; otherwise below +10 the level may come at once (doubled chance with the aging stone, never with
  the super stones). Copper ores and super stones turn a loss into a fall (81% one level).
- Every outcome but the loss starts the next level's maturity (`AgingMaturity`: count, goal, stone): a success at 0, a
  fall half way, a doubling after gaining a level. The goal follows the family's counter: attacks (axes, hammers,
  staves, bows, javelins), critical hits (claws, poles, swords), blocks (shields), blows taken (armours, robes, orbs,
  boots, gloves, armlets), or 1200 worn seconds (the ring).
- Counting (`FieldInstance.Aging`): the holder's own landed hit on a wild monster within ten levels counts once a tick
  (`Critical` on a critical hit, else `Attack`); a block counts for the shield and a blow taken for the armour, orb,
  boots, gloves and armlets; a worn ring counts each second. Counts live on the worn copies (`AgingRevision`) and are
  saved with the character; a copy that reaches its goal waits for the upkeep (`QuestTransactions.PrepareUpkeep` calls
  `MatureAgedItems`), which stores the level. A copy checked every five seconds asks again when a commit replaced it.
- A level (`LevelUp`): axes and hammers damage +1 (+2 from +10, +3 from +20), attack rating +10; claws, poles and swords
  damage, attack rating +5 and critical +1 on odd levels; staves damage, attack rating +10, mana +10; bows and javelins
  damage and critical on odd levels; shields block +1 on odd levels and absorb; orbs, boots and gloves defence +10%,
  armours and robes +5%, with absorb; armlets defence +10% and attack rating +5; worn families lose durability. Even
  levels raise the level requirement unless the item asks 100, 102 or 105 or the aging used the elder ore or a super
  stone. A fall (`LevelDown`) takes the same values back.
- The aging ring worn at +6 lowers the death's share to a fiftieth, at +8 to a hundredth (`DeathPenalty.Divisor`).
- `/AgingSetting code level [class]` (game master level 3) drops a weapon or armour already at the level.

## Mixing and alchemy

`MixFormulas`, `MixingWork` (Simulation, `Items/Mixing/`) hold the rules; `NpcServiceTransactions` runs `Mix` and
`Refine` for the `ItemMix` service (value 1 the mixer, 200 the alchemist).

- Mix: an item from the bag that can be worn, never aged nor mixed, not a quest, timed or clan item, with up to twelve
  sheltoms (Lucidy .. Ordo), for the item's price (`mixing.price_percent`). The sheltoms and the gold are spent whatever
  the result. The first of the 103 formulas (`MixFormulas.Table`) whose families hold the item's family (axes to
  javelins, armours and robes, shields, orbs) and whose counts of the nine sheltoms Lucidy .. Celesto are exactly those
  placed adds its bonuses: elements add their value; other stats add a whole value or a percent of the copy's own value.
  The copy records its highest sheltom (`MixSheltom`) and the stats the mix raised (`MixStats`); without a formula the
  mix fails and the item stays as it was. A mixed copy can no longer be aged or mixed.
- Reconstruction: two seals of one kind and nothing else, for an item whose level requirement is in the seal's band
  (SE101 0..105, SE102 40..79, SE103 0..39, SE104 0..80), rebuilds the item as a newly rolled copy under the same identity.
  With the respec stone (`BI190`) half the rebuilds take the owner's class specialization, the others never take it.
- Alchemy: each sheltom from Lucidy to the eleventh becomes the force orb of its rank, for 200, 500, 1000, 2000, 4000,
  5000, 10000, 20000, 30000, 40000 or 50000 gold (`alchemy.price_percent`); the later and the magic sheltoms are refused.
- Results (`MixResult`): 0 failed, 1 mixed, 2 reconstructed, 3 refined. A mixed copy whose highest sheltom is Devine to
  Mirage glows in the mixing colours (`HeldItemGlow`).

## Wing warp gates

`WingGates` (Simulation) holds the rules; `WingGateTransactions` (Application) runs a trip on the tick. Ten fields
have a warp stone (`FieldWarpGate` with `SpecialEffect` 2): Ricarten, Pillai, Land of Dusk (18), Bamboo Forest (1),
Ruinen Village (6), Navisko (9), Forbidden Land (12), Eura (29), Land of Chaos (37) and Bless Castle (33).

- Stepping on a stone opens the wing warp map. Its destinations, in order: Ricarten, Pillai, Land of Dusk, Bamboo
  Forest, Ruinen Village, Navisko Town, Forbidden Land (level 55), Eura Village (90), Land of Chaos (95); the
  destination field's own `LimitLevel` applies as well.
- Reach: without a wing the first two; with a wing in the pack the best one counts: Metal (`QW101`) 4, Silver 5,
  Gold 6, Dia 7, Chaos 8, Extreme (`QW106`) 9.
- Fee: Ricarten, Pillai and the field the player stands in are free; elsewhere the wing's fee: 100, 300, 500, 1000,
  2000, 4000 gold from Metal to Extreme. It counts as teleport spending in the currency report.
- The server checks that the living player stands within `max(Width, 48) + 64` planar units of a stone of its field,
  the reach, the level and the gold. The candidate pays the fee and is stored at the destination, then the commit
  takes the gold and moves the player (`Arrive`, also within the same field) to the destination stone's exit (its
  first start point when it has no stone).
- Bless Castle's stone has no map entry: the castle destination belongs to the castle's owner clan, and there is no
  castle ownership yet.

`CharacterSoundCodes` names every sound family code a rule or sound reads (NPCs, castle objects, and the monsters
with rules of their own); `CharacterSoundCodes.Resolve(key)` maps the authored sound keyword of a monster or NPC to its
sound family code. The
importer stores it; the client audio library and the AI rules key on it. The 54 keywords of monsters newer than the
original sound table (Greedy, Hestian, the invaders, the lizards, the mine and laboratory monsters...) have Etervale's
own codes from 0x6000, one per sound folder; MapleFairy, MapleTree, Furia, Lucifer, KhanDoor, LizardDoor, Sakura and
Soldier_D have no sound folder and stay silent.

## Driving surface (Application)

- `WorldServer` tick (20 Hz): `BossSpawnSchedule.TryBeginPass`, then every live `FieldInstance` steps in parallel.
  Field changes go through `FieldTransition.TryResolve`; entry, respawn and teleport use `FieldTerrain.PlaceOnFloor`
  or `SettleNear`.
- `FieldInstance.Step` order: rebuild prey, `FieldSpawner.MaintainPopulation`, `MaintainDespawns`,
  `SpawnScheduledBosses`, rebuild the `SpatialIndex`, `MonsterAiBrain.Tick` then `MonsterStepping.Step` per wild
  monster, summons, NPC steps, players, skills, corpses, ground loot.
- `FieldInstance` implements `ISpawnSink` (creates the `MonsterInstance` on the floor), `IPlayerProximity`,
  `IStepOccupancy` (monster occupancy) and the prey view (`IPreyView`).
- `FieldCombat`: `ApplyFlinchOnHit`, `ApplyBossRetargetOnHit`, `MonsterAttackReach.CanLandBasic`, `KillReward`,
  `DropRoller.Roll`.
- `WorldPlayer.ReportMovement`: `FieldTerrain.Slide`, `TryFloorNear`, `Falling.Step`.
- `NpcServiceTransactions`: `NpcShopRules`, `MoneyLimits`, `WarehouseStorage`, `TeleportRoutes`, `AgingWork`, `MixingWork`,
  `NpcServiceAvailability`.

Each `FieldInstance` owns a `CombatRng` seeded with its field code (shared by brain, spawner and drops) and is created
on first use. Fields share only immutable catalogs, terrain and the id allocator, which is what makes the parallel step
safe. Rules here run inside the tick and must not block: no I/O, no lock waits, no tasks.

## How to

- **Add a field**: create a static class with `Definition` in the region file under `World/Fields/` (unique `Id` =
  stage base name, unique `FieldCode`), add it to the `WorldCatalog()` list, add gates on both sides and warp gates
  with destinations. Re-run the importer so spawn files join by the stage base name; the server will refuse to start
  without the stage file. Check `tests/PristonTale.Tests/FieldAudioTests.cs` and `DomainTests.cs`.
- **Tune monster AI**: edit `MonsterAiTuning` constants or `MonsterAiBrain`; keep decisions on `ISimRng` for
  determinism. Run `MonsterMovementTests`, `MonsterNavigationTests`, `MonsterCombatTests`, `MonsterSkillTests`. Clients
  receive each swing's clip slot, duration and hit time in `SwingBegin` combat events.
- **Change a monster skill rule**: the family's case in `MonsterSkillRules`, `MonsterLifeRules` or
  `MonsterTransformRules` and its server side in `FieldMonsterSkills`; run `MonsterSkillTests`,
  `MonsterSkillStatusTests` and `MonsterBossSkillTests`.
- **Change terrain rules**: `FieldTerrain`/`MonsterStepping` and `TerrainTests`. If the stage subset changes, keep
  `StageCollisionReader` and the client SMD reader in parity (`TerrainTests` compares them).
- **Change spawning**: `FieldSpawner`/`SpawnCadence`/`BossSpawnSchedule`; data knobs need the importer
  (`tools/PristonTale.WorldImport/Parsers/SpmParser.cs`), the persistence records and
  `WorldDataFactory.ToFieldSpawnData`.
  Run `SpawnGateTests` and `SpawnScheduleTests`.
- **Change drops or experience**: `DropRoller`, `DropTable`, `KillReward`, `WorldDataFactory.ToDropTable`; run
  `WorldRewardsTests` (and the `--verify-world-rewards` probe against a real database).
- **Add an NPC service**: append to `NpcServiceKind` (and `NpcServiceAction` if needed), map the authored key in
  `tools/PristonTale.WorldImport/Parsers/NpcParser.cs`, enable it in `NpcServiceAvailability`, implement it in
  `Shared/PristonTale.Application/World/NpcServiceTransactions.cs`, map it in the protocol and client dialog, add
  `NpcServiceTests`. Update [application.md](application.md), [protocol.md](protocol.md),
  [world-import.md](world-import.md) and the README section on NPC services.
- **Add a teleport route**: add the route to `TeleportRoutes`; availability follows. Update `NpcServiceTests` and the
  README teleporter list.
- **Regenerate player attack timings**: run the test runner with `--export-equipment-timings <assetsRoot>
  Shared/PristonTale.Simulation/World/Motion/Data/PlayerEquipmentTiming.json`, then `--verify-equipment <assetsRoot>`.

## Pitfalls

- `SoundCode` selects behaviour: ranged basic reach (`MonsterAttackReach.HasRangedBasicAttack`), the monster skill rules
  (`MonsterSkillRules`; the named codes are `CharacterSoundCodes`), unyielding bosses, locomotion speed and the client's effect family. Changing a
  sound mapping changes combat and visuals.
- Motion clip slots are the model's INX motion indices and keys their authored key characters (`motion_slots.slot`,
  `key_code`); re-import after changing models so server swings and client clips agree.
- Monster and NPC codes are load-order positions, not persistent ids. Persist names, never codes.
- `PlaceOnFloor` takes the highest floor anywhere under a point, which can be a roof or bridge; use `SettleNear` or
  `TryFloorNear` when a reference height exists.
- Basic hit reach compares squared distance with `48 * 256` (about 111 units). This is intentional; do not turn it
  into a radius of 48.
- Entity pair distance is the smaller radius, not the sum. Monsters ignore players and NPCs when stepping.
- `FieldDef.Spawns` is never read by the server; spawn data lives in `WorldData.FieldSpawns` keyed by field code.
- Warp gates with `SpecialEffect == 2` never authorize a transition.
- `FieldTerrain.None` makes every move legal; a server started with `Assets:AllowMissingTerrain` has no wall checks.
- `MonsterAiTuning` is cached per field by monster code; a definition altered with `with` for a summon shares the
  original code's tuning.
- `Idle` monsters do not wander after the spawn walk; they only react to prey or damage.
- Do not hand-edit `Data/PlayerEquipmentTiming.json`; regenerate it.
- Appending is the only safe change to `NpcServiceKind`, `NpcServiceAction` and `NpcServiceStatus`: values are stored
  and sent on the wire.

## Related

- [architecture.md](architecture.md) - layering, tick and threading model
- [application.md](application.md) - `WorldServer`, `FieldInstance`, combat, ground loot, NPC transactions
- [simulation.md](simulation.md) - characters, items, combat formulas, modifiers
- [skills.md](skills.md) - skills, summons and monster-affecting effects
- [world-import.md](world-import.md) - importer and authored data contracts (spawn files, NPC files, INX extraction)
- [persistence.md](persistence.md) - world data schema and `WorldDataFactory`
- [server.md](server.md) - assets root and terrain loading configuration
- [rendering.md](rendering.md) - client SMD reader and stage presentation
- [testing.md](testing.md) - regression runner and probes
- [glossary.md](glossary.md)
