# Architecture

How the Etervale solution is split into projects, how they depend on each other, where each kind of rule lives and how
work moves between threads on the server and the client. Read this before touching code in more than one project.

## Read this when

- You need to decide which project a new type, rule or query belongs in.
- You are adding a project reference and must keep the dependency direction.
- You touch the world tick, the command queue, a database write that changes game state, or the client frame loop.
- You need the end-to-end path of a player action (client input to server rule to database to every client).

## Runtime topology

```text
PristonTale (client, MonoGame/Vulkan)  --TLS over TCP 10009, Etervale frames-->  PristonTale.Server (generic host)
                                                                                   |  WorldServer tick (20 Hz)
                                                                                   |  Application services
                                                                                   v
                                                         SQLite data/pristontale.db (default) or PostgreSQL
tools/PristonTale.WorldImport  --migrate + import authored assets/GameServer data-->  same database
```

The game is online only. The server is authoritative for movement validation, combat, loot, skills, inventory,
progression and NPC services. The client moves its own avatar and reports the position; the server validates each
report against a distance budget and terrain and corrects the client only with a warp. Everything else the client
shows comes from server snapshots.

## Projects

| Project | Kind | Owns |
|---|---|---|
| `Shared/PristonTale.Contracts` | library, no deps | Codes every layer shares, declared once: `MotionState` (the server's motion, the world view, the presence wire byte, the client's playback) and `MotionCode` (a model's motion table code, read by the renderer and the server's timing) with `MotionCodes.Of` between them; `EntityKind` (world view, descriptions, client, collision); `PlayerClass` (storage, wire, outcomes, client); `CharacterNames` and `CharacterSlots` (name length, six characters per account and the first free slot); `ChatKind` (server lines, wire, chat log); combat, skill and status codes (`CombatEventKind`, `SkillCastVerdict`, `SkillCastCommand`, `SkillEventPhase`, `SkillEventFlags`, `StatusEffectOp`); the outcomes the client reads (login, characters, world entry, restart, wing trips); `GameUnits`, the authored units (8.8 fixed point, 4096 angles per turn, 70 ticks per second, 160 ticks and 33 ms per animation frame) |
| `Shared/PristonTale.Geometry` | library, no deps | Pure geometry used by simulation and renderer |
| `Shared/PristonTale.Assets` | library, Contracts, Geometry | Asset access without MonoGame, for the server, the client and the tools: `AssetFiles` (case-insensitive resolution of original paths, assets root discovery), `Cp949` (authored Korean text), `DataPath` (where a relative data file such as the database or the certificate lives), `SmdLayout` (the signatures and record sizes of the `.smd` format, shared by every reader), `StageCollisionReader` (the collision triangles of a stage `.smd`) |
| `Shared/PristonTale.Simulation` | library | Game rules and data models: characters, classes, items, combat, modifiers, progression, skills, world rules (fields, AI, spawning, loot, motion, NPCs), physics. No IO, no transport, no MonoGame |
| `Shared/PristonTale.Protocol` | library, no deps | Wire format: framing, TLS transport, messages and codecs, Etervale opcodes |
| `Shared/PristonTale.Application` | library | Transport-free use cases (auth, characters, sessions, NPC services) and the authoritative `WorldServer` with its tick |
| `Persistence/PristonTale.Persistence.Abstractions` | library | Repository interfaces and persisted records |
| `Persistence/PristonTale.Persistence` | library | Dapper repositories, FluentMigrator migrations |
| `Persistence/PristonTale.Persistence.Sqlite`, `.Postgres` | library | Provider wiring (`AddPristonSqlite`, `AddPristonPostgres`) |
| `Server/PristonTale.Server` | exe | Generic host, DI composition, hosted services (migrations, catalog and terrain load, tick, TCP server), TCP edge mapping packets to application calls |
| `Client/PristonTale.Engine3D` | library | Original asset formats, IO, renderer, lighting, post-processing, sky, client settings |
| `Client/PristonTale.Effects` | library | Particles and data-driven VFX |
| `Client/PristonTale.Client.Net` | library | Client side of the protocol: connection, presence decoding, world catalog from the server |
| `Client/PristonTale.Gameplay` | library | Screens, game scene, HUD and windows, remote actors, audio, skill presentation |
| `Client/PristonTale` | exe | `Game1`, display management, content build (shaders, fonts) |
| `tests/PristonTale.Tests` | exe | Console regression runner (not `dotnet test`) |
| `tools/*` | exe | Importer, dev accounts, smoke client, load test, asset dump, glTF export, world JSON |
| `monogame/` | vendored | MonoGame 3.8.6 fork with the Vulkan backend, effect processor and native runtime |

## Dependency graph

Project references, from the `.csproj` files. Keep this direction; never add a reverse edge. Contracts and Geometry
reference nothing.

| Project | References |
|---|---|
| Assets | Contracts, Geometry |
| Protocol | Contracts |
| Simulation | Contracts, Geometry |
| Persistence.Abstractions | Simulation |
| Persistence | Persistence.Abstractions, Simulation |
| Persistence.Sqlite / .Postgres | Persistence |
| Application | Simulation, Persistence.Abstractions |
| Server | Application, Protocol, Simulation, Assets, Persistence.Sqlite, Persistence.Postgres |
| Engine3D | Contracts, Geometry, Assets, MonoGame framework |
| Effects | Engine3D, MonoGame framework |
| Client.Net | Protocol, Simulation (no Application: the client never ships the server's authority) |
| Gameplay | Engine3D, Effects, Simulation, Client.Net, MonoGame framework |
| PristonTale | Gameplay, MonoGame framework, effect processor |
| Tests | Gameplay, Server, Protocol, WorldImport, GltfExport |

Consequences:

- A code that crosses layers (a motion state, a kind, a status) is declared once in Contracts and used as it is on
  every side; a layer declares its own enum only when the meaning differs (a model's motion table code is not a
  `MotionState`), with one mapping beside it. The authored units (`GameUnits`) are the same: a scale, angle, tick or
  frame conversion uses them instead of a local 256, 4096, 70, 160 or 33.
- Simulation and Application never see sockets, packets, SQL or MonoGame. Packet structs stay in Protocol; mapping
  between packets and application inputs/outcomes lives in `Server/PristonTale.Server/Net` (server) and
  `Client/PristonTale.Client.Net` (client).
- SQL lives only in `Persistence/`. Application talks to storage through the abstractions' repository interfaces.
- The client reuses Simulation for shared rules (stats, items, skills data, collision), so a rule changed there changes
  both sides. Rules that only the authority evaluates belong in Application.
- `Client.Net` and `Gameplay` never reference Application: what the client reads from the server lives in Contracts.

## Where a rule goes

| Kind of code | Project |
|---|---|
| Formula, table, validation that both client and server evaluate | `Shared/PristonTale.Simulation` |
| Authoritative decision, world state mutation, transaction preparation | `Shared/PristonTale.Application` |
| Byte layout, opcode, transport | `Shared/PristonTale.Protocol` |
| Packet to command mapping, connection policy | `Server/PristonTale.Server/Net` |
| SQL, schema change | `Persistence/` (migration + repository + both providers) |
| Parsing authored `GameServer` files into the catalog tables | `tools/PristonTale.WorldImport` |
| Original model, stage and animation formats, GPU work | `Client/PristonTale.Engine3D` |
| Particle scripts and VFX data | `Client/PristonTale.Effects`, `<assetRoot>/Effect/Vfx` written by `tools/PristonTale.VfxImport` |
| HUD, windows, input, presentation of server events | `Client/PristonTale.Gameplay` |

Original asset paths are resolved with `AssetFiles`; do not build original paths by hand.

## Server threading model

- `WorldServer` (`Shared/PristonTale.Application/World/WorldServer.cs`) owns all live world state. Only the tick thread
  touches fields, players, monsters and NPCs.
- `WorldTickService` (`Server/PristonTale.Server/World/WorldTickService.cs`) calls `WorldServer.Tick` at
  `WorldServer.TickHz` (20 Hz) on its own thread, on an absolute timeline (`TickSchedule`), and logs p50/p99/max tick
  time and its phases every 30 s.
- Every other thread reaches the world through `EnqueueAsync`: the operation is queued as an `Action` on a bounded
  single-reader channel and its result returns as a `Task`. Each tick drains at most `CommandsPerTick` commands, then
  steps every active `FieldInstance` in parallel (fields share only immutable catalogs and the thread-safe
  `EntityIdAllocator`). A player may have at most `World:MaxPendingCommandsPerPlayer` commands waiting; beyond that
  `WorldBusyException` is thrown synchronously.
- After stepping, each field fills one reused `PresenceFrame` (views, grid, field-wide descriptor revisions) and writes
  one `WorldSnapshot` over it, specialised with the owner's state and cooldowns, to each connected player's outbound
  channel. The TCP session encodes snapshots into packets on its own task, pinning the frame while it reads; entries
  every receiver shares are encoded once per frame. Who receives what: [server.md](server.md#presence-propagation).
- The tick never awaits IO. State changes that must be durable use prepare and commit, and storage follows:
  1. An application service takes the session's `SaveGate`, then asks the world to prepare (on the tick): validate and
     build a candidate state plus a save snapshot (`Prepared*Operation` records).
  2. The service enqueues the commit, which applies the candidate on the tick, then queues the snapshot for the
     background character store (`ICharacterSaves`). A transfer out of the character (trade, personal shop, warehouse,
     ground) instead writes the snapshot first and commits only if storage took it.
  Operation ids make retries idempotent. `NpcAppService` and `SessionAppService` show the pattern; every playing
  character is also captured once a minute ([application.md](application.md#character-saves)).
- Catalogs (`WorldDataHolder.Current`, `WorldCatalog`, `FieldTerrainCatalog`) are loaded by hosted services at startup
  and treated as immutable afterwards.

## Client threading model

- `Game1` (`Client/PristonTale/Game1.cs`) hosts `GameClient`, which runs the screen flow; `Update`/`Draw` run on the
  MonoGame thread, which is also the only thread that issues GPU work.
- Network receive runs on background tasks in `GameServerConnection`; decoded updates cross to the game thread through
  bounded channels and are applied during `Update`.
- Field streaming and asset decoding are prepared off the frame and uploaded incrementally on the game thread.
- `Update` and `Draw` must not block on network, disk, database, tasks or long locks.

Details: [client.md](client.md), [rendering.md](rendering.md).

## Path of a player action

Example: a player buys an item from an NPC shop.

1. Client UI (`Client/PristonTale.Gameplay/UI/Npc`) sends the Etervale NPC service packet through `Client.Net`.
2. `Server/PristonTale.Server/Net/ClientSession` decodes it and calls `NpcAppService`.
3. `NpcAppService` takes the `SaveGate`, calls `WorldServer.PrepareNpcServiceAsync` (tick: reach, stock, price, carry
   limit), then `CommitNpcServiceAsync` (tick: apply inventory and gold); the character snapshot goes to the save queue.
4. The reply packet goes back to the requester; inventory changes appear in the owner state of following snapshots.

Combat, skills, pickup and potions follow the same shape with their own `Prepared*Operation` or tick-only command.

## Data lifecycle

1. Authored `assets/GameServer` files (CP949 text) are parsed by `tools/PristonTale.WorldImport` into catalog tables;
   source files, duplicate definitions and unresolved references are preserved.
2. The server migrates the schema, loads catalogs into `WorldDataHolder` and `WorldCatalog`, and loads stage collision
   from `assets/` into `FieldTerrainCatalog`.
3. The client receives the catalogs it needs from the server and loads models, textures and sounds from its local
   `assets/`.

Details: [world-import.md](world-import.md), [persistence.md](persistence.md), [server.md](server.md).

## Related

- [README.md](README.md) — documentation index and task routing.
- [protocol.md](protocol.md), [application.md](application.md), [simulation.md](simulation.md), [world.md](world.md).
