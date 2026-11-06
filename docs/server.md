# Game server

The headless game server host in `Server/PristonTale.Server`: process startup, dependency injection, configuration, the
TCP edge in `Net/` and the world tick driver. Game rules live in `Shared/PristonTale.Application` and below; wire
layouts live in [protocol.md](protocol.md).

## Read this when

- The server does not start (migrations, empty catalogs, missing stages, `Assets:Root`, database provider).
- You add or change a server setting, or need a default for one.
- You handle a new client packet, or change how a packet maps to an application call and back.
- You change connection limits, login throttling, timeouts or log sampling.
- You register a new service or hosted service in the server's DI container.
- You run the server against PostgreSQL, a copied database or a load test.

## Code map

| Path | Responsibility |
| --- | --- |
| [Program.cs](../Server/PristonTale.Server/Program.cs) | Generic host: `Host.CreateApplicationBuilder(args)`, `AddPristonServer()`, `Run()`. No HTTP endpoints. |
| [Hosting/HostingExtensions.cs](../Server/PristonTale.Server/Hosting/HostingExtensions.cs) | `AddPristonServer`: every DI registration, provider selection, `WorldServerOptions`, `DefaultGamePort` (10009, the default of `Network:Port`). |
| `Hosting/DatabaseMigrationService.cs` | Hosted service: `IDatabaseMigrator.MigrateAsync` on start. |
| `Hosting/GameLogService.cs` | Hosted service: the item transfer writer, the currency sinks every `Logs:CurrencyMinutes`, the server's start and stop, the daily prune. |
| `Hosting/CharacterSaveService.cs` | Hosted service: stores the save journal on start, runs `CharacterSaveQueue` until the host stops, logs its counts every `Server:ReportSeconds`. |
| `Hosting/ReportInterval.cs` | The period of the report lines (`Server:ReportSeconds`), read once by the world tick, the TCP server and the character saves. |
| `Hosting/WorldDataLoadService.cs` | Hosted service: builds the English authoritative catalog into `WorldDataHolder` and, for every other supported language that has stored text (`HasCatalogTextsAsync`), a catalog in that language into `WorldDataLanguagesHolder`; a session in a language without text reads the English catalog. A session reading another catalog than the English one encodes its own entity descriptions instead of the frame's shared ones (`PresenceEncoder.SharesDescriptions`). Logs catalog counts. |
| `Hosting/FieldTerrainLoadService.cs` | Hosted service: reads every field's stage `.smd` collision in parallel into `FieldTerrainCatalog`. |
| `World/WorldTickService.cs` | `BackgroundService` calling `WorldServer.Tick(1/20 s, tick)` on its own thread, on the absolute timeline of `TickSchedule` (owed ticks run back to back, a stall past 5 ticks is skipped); logs tick percentiles, ticks caught up and skipped, the command and field phases, process CPU, allocation rate, collections, heap and working set. |
| `World/StarterGiftService.cs`, `World/SessionWork.cs` | `BackgroundService` reading `WorldServer.StarterGiftRequests` and running `StarterGiftAppService.GiveAsync`; `SessionWork` is the loop it shares with `QuestUpkeepService` (a few sessions at a time, a failure logged). |
| `World/QuestUpkeepService.cs` | `BackgroundService` reading `WorldServer.QuestUpkeepRequests` and running `QuestAppService.RunUpkeepAsync`, at most 8 players at a time ([quests.md](quests.md)). |
| [Net/GameTcpServer.cs](../Server/PristonTale.Server/Net/GameTcpServer.cs) | Listener, connection and handshake caps (in all and per address), TLS handshake, one `ClientSession` per connection, housekeeping (idle/login timeouts, bandwidth log). |
| `Net/ServerCertificate.cs` | Loads the TLS certificate, or creates and stores a self-signed one. |
| [Net/ClientSession.cs](../Server/PristonTale.Server/Net/ClientSession.cs) | Per-connection state machine: packet dispatch to application services and replies. |
| `Net/ClientSession.Chat.cs` | The session's `IChatLink`: chat lines, forced warps, close requests and kicks from game masters. |
| `Net/GameServerControl.cs` | `IServerOperations` for game masters: user limit, blocked logins, the shutdown countdown and host stop. |
| `Net/PresenceEncoder.cs` | Turns the receiver's `WorldSnapshot` into presence packets (presence entries, events, drops, owner state, the restart offer); the crowd cap, the movement budget and descriptions. |
| `Net/SharedPresenceWire.cs` | The entity descriptions of one presence frame, encoded once for every receiver into pooled buffers. |
| `Net/VisibleSet.cs`, `Net/MotionCandidates.cs` | The receiver's visible crowd (nearest 100 players and up to 256 entities in all, plus essentials, keep band) and its most overdue position updates. |
| `Net/WireMapping.cs`, `Net/NpcWireMapping.cs`, `Net/QuestWireMapping.cs` | Authoritative views to wire structs (presence entries, descriptions, events, NPC service state, quest messages). |
| `Net/NetworkLimits.cs` | `Network:*` settings with defaults and validation; IPv4-mapped address normalisation. |
| `Net/ConnectionGuards.cs` | `PacketRateLimiter`, `TokenBucket`, `ConnectionLimiter`, `LoginThrottle`, `LogSampler`. |
| `Application/README.md` | Placeholder rule: server-only orchestration goes here; use cases stay in `Shared/PristonTale.Application`. |
| `appsettings.json` | Logging level, PostgreSQL connection string, `Database:Provider` and `Database:SqlitePath`. |

The project uses `Microsoft.NET.Sdk.Web` for its hosting defaults and exposes internals to `PristonTale.Tests`.

## Startup sequence

`AddPristonServer` runs while the builder is configured; it throws immediately for an unknown `Database:Provider` or a
missing PostgreSQL connection string. The host then constructs every hosted service and starts them one after another in
registration order. An exception from any `StartAsync` stops the host before the next service starts.

| Order | Service | Work | Fails startup when |
| --- | --- | --- | --- |
| 1 | `DatabaseMigrationService` | Applies pending FluentMigrator migrations of the chosen provider. | The database cannot be opened or migrated. |
| 2 | `CharacterSaveService` | Stores the character save journal a previous run left (`save-journal/` beside the executable), then starts the background character store. | Never; a journal storage refuses is retried every 5 s, holding the start, because a player must not load a character older than its journaled capture. |
| 3 | `GameLogService` | Runs the item transfer writer; once the host has started (so the server name comes from the settings table and pruning does not hold the start) logs the start, then stores the currency sinks every `Logs:CurrencyMinutes` and prunes the game logs once a day; on stop stores the last currency and the stop. | Never; a failed log write is reported and the game goes on. |
| 4 | `WorldDataLoadService` | Loads items, monsters, NPCs, field spawns and motion timings from the database. | Repository errors only. An empty, un-imported database loads zero rows and starts an empty world. |
| 5 | `FieldTerrainLoadService` | Loads the collision of all 43 fields of `WorldCatalog.AllFields` (the quest arena included) (`FieldDef.StageSmd`). | No assets directory, or any stage missing or unreadable, unless `Assets:AllowMissingTerrain=true`. |
| 6 | `WorldTickService` | Starts the 20 Hz tick loop. | Never; a throwing tick is logged and the loop continues. |
| 7 | `ServerSettingsService` | Adds the missing `server_settings` rows with their defaults, reads the table and applies it on the tick, then reads it again every `server.settings_reload_minutes`. | The table cannot be read at start; later failures keep the settings in force. |
| 8 | `GameTcpServer` | Loads the TLS certificate, binds `[::]:<Network:Port>` (10009) dual-stack, backlog 512, and accepts. | The port is in use (the host stops). |
| 9 | `QuestUpkeepService` | Applies the quest work the tick queues (quest drops, quest weapon growth and expiry) and queues the character for storage. | Never; a failed upkeep is logged and retried on the field's next check. |
| 10 | `StarterGiftService` | Gives the starter gifts the tick finds due ([application.md](application.md#starter-gifts)). | Never; a failed gift is logged and offered again after the interval. |

Shutdown runs the services in reverse: the game server closes every connection and awaits their leave saves while the
tick still runs, and the character store stops last. Once the host is stopping, the first write storage refuses sends
everything the store holds, and every later capture, to the save journal without another attempt; one failed attempt
(a connection timeout, 15 s) is all a database that is down costs, inside the host's 60 s shutdown timeout
(`HostOptions.ShutdownTimeout`, set by `AddPristonServer`).

Terrain details:

- Assets root: `Assets:Root` when non-empty, otherwise `AssetFiles.FindAssetsRoot()`, which looks for an `assets`
  directory in the working directory and its ancestors, then in the executable directory and its ancestors.
- Stage paths are resolved case-insensitively with either separator (`AssetFiles.ResolveFile`, `Shared/PristonTale.Assets`), parsed with
  `StageCollisionReader.Read` and published once with `FieldTerrainCatalog.Publish`, keyed by field code.
- With `Assets:AllowMissingTerrain=true`, failures become a warning and those fields use `FieldTerrain.None` (no floor
  or wall checks). Use it only for tests and development.
- A full load logs `Field terrain loaded: 43/43 stages, ...` with about 1.93 million collision triangles and 179 MiB,
  well under a second.

`WorldServer` creates `FieldInstance`s lazily on first use and reads `WorldDataHolder.Current` and the terrain catalog
at that moment, so both loaders must finish before the listener accepts players; the sequential start guarantees that.

## DI composition

All registrations are singletons in `HostingExtensions.AddPristonServer`.

| Service | Implementation / factory | Notes |
| --- | --- | --- |
| `ISessionRegistry` | `SessionRegistry` | In-memory login sessions keyed by token; allocates player entity ids. |
| `CharacterClassRegistry` | type | Class definitions (`PristonTale.Simulation.Characters`). |
| `AuthAppService`, `CharacterAppService`, `SessionAppService`, `NpcAppService`, `QuestAppService` | types | Use cases called by `ClientSession` (and `QuestUpkeepService`). |
| `WorldCatalog` (`PristonTale.Simulation.World`) | `_ => new WorldCatalog()` | Factory so DI does not pick the collection constructor with an empty field set. |
| Persistence | `AddPristonSqlite(path)` or `AddPristonPostgres(connectionString, Database:MaxPoolSize)` | Repositories, `IDbConnectionFactory`, `IDatabaseMigrator`, FluentMigrator runner. See [persistence.md](persistence.md). |
| `WorldDataAppService`, `WorldDataHolder`, `WorldDataLanguagesHolder` | types | Catalog build and the immutable holders read by the world and the encoders. |
| `FieldTerrainCatalog` | type | Filled by `FieldTerrainLoadService`. |
| `WorldServer` | factory with `WorldServerOptions` (`World:MaxPendingCommandsPerPlayer` and `World:CheckpointSeconds` from the host), the save queue and the game log | The game settings arrive from the settings table through `ApplyOptionsAsync`. |
| `GameLogQueue`, `IGameLog` | factory: `IGameLogRepository` | The item transfer writer ([application.md](application.md#game-logs)); a failed batch is logged as an error. |
| `GmAuditQueue` | factory: `IGameMasterRepository` | The game masters' audit writer, run by `GameLogService`; a failed row is logged as an error and retried. |
| `CharacterSaveQueue`, `ICharacterSaves` | factory: the character repository, `SaveJournal` (`Database:SaveJournalPath`), `Database:SaveBatchSize` | The background character store ([application.md](application.md#character-saves)); a failed batch is logged as an error. |
| `ServerSettingsAppService` | factory | Reads the settings table and applies it to the world, `GameMasterCommands.Options` and `GameServerControl` (name, player limit). |

Hosted services are registered in the order of the startup table.

## Database provider

| Provider | Selection | Connection |
| --- | --- | --- |
| SQLite (default) | `Database:Provider` = `Sqlite` (case-insensitive) or unset | `Database:SqlitePath`, default `data/pristontale.db`. |
| PostgreSQL | `Database:Provider` = `Postgres` | `ConnectionStrings:Postgres`; `appsettings.json` points at the `docker-compose.yml` instance on port 5433. |

- A relative SQLite path is resolved by `DataPath.Resolve` against the first ancestor of the executable
  directory that contains `PristonTale.sln`, else against the executable directory. It does not depend on the working
  directory. The directory is created if missing.
- SQLite runs with WAL journaling and `synchronous=NORMAL`; `-wal` and `-shm` files sit next to the database.
- Environment form: `Database__Provider=Postgres`, `ConnectionStrings__Postgres="Host=...;Port=5433;..."`.
- Start PostgreSQL with `docker compose up -d --wait`. `docker compose down` keeps the named volume; `down -v` deletes
  it.
- The server migrates the schema itself but never imports world data. Import with `tools/PristonTale.WorldImport`
  ([world-import.md](world-import.md)) and create accounts with `tools/PristonTale.DevAccounts` ([tools.md](tools.md)).

## Settings

Sources, later ones winning: `appsettings.json`, `appsettings.{Environment}.json` (none exist), environment variables
(`__` for `:`, e.g. `Network__Port=10010`), command line (`--Network:Port=10010`). A `Network:*` limit of `0` disables
that limit; negative values stop startup. The game settings live in the database ([below](#server-settings-table)).

| Key | Default | Read by | Meaning |
| --- | --- | --- | --- |
| `Database:Provider` | `Sqlite` | `HostingExtensions` | `Sqlite` or `Postgres`; anything else throws. |
| `Database:SqlitePath` | `data/pristontale.db` | `HostingExtensions` | SQLite file; relative to the solution root (see above). |
| `ConnectionStrings:Postgres` | local compose string | `HostingExtensions` | Required when the provider is `Postgres`. |
| `Database:MaxPoolSize` | 20 | `AddPristonPostgres` | PostgreSQL connections at most; keep it below the server's `max_connections`. |
| `Database:SaveBatchSize` | 100 | `CharacterSaveQueue` | Characters stored per transaction; at least 1. |
| `Database:SaveJournalPath` | `save-journal` | `CharacterSaveService` | Directory of the save journal, relative to the executable. |
| `Assets:Root` | auto-detected `assets` | `FieldTerrainLoadService` | Directory holding the original asset tree with the field stages. |
| `Assets:AllowMissingTerrain` | `false` | `FieldTerrainLoadService` | Start without an assets directory or with missing/unreadable stages. |
| `World:MaxPendingCommandsPerPlayer` | 16 | `WorldServerOptions` | Commands one player may have waiting for the tick; at least 1. The queue holds 4096 players' default quota. |
| `Logs:CurrencyMinutes` | 10 | `GameLogOptions` | Period of the currency sink rows, 1 to 1440. |
| `Logs:RetentionDays` | 180 | `GameLogOptions` | Game log rows older than this are deleted once a day; 0 keeps them, negative stops startup. |
| `World:CheckpointSeconds` | 60 | `WorldServerOptions` | Every playing character is captured for storage once per this many seconds; at least 1. |
| `Network:Port` | 10009 | `GameTcpServer` | TCP game port, 0 to 65535; 0 binds a free port (in-process tests read it from `GameTcpServer.BoundPort`). The client always connects to 10009. |
| `Network:Certificate:Path` | `data/server-certificate.pfx` | `ServerCertificate` | TLS certificate (PKCS#12); relative paths resolve from the solution root. Created self-signed when missing. |
| `Network:Certificate:Password` | none | `ServerCertificate` | Password of the PKCS#12 file. |
| `Network:HandshakeTimeoutSeconds` | 10 | `NetworkLimits` | Deadline of the TLS handshake of an accepted socket; at least 1. |
| `Server:ReportSeconds` | 30 | `ReportInterval` | Period of the tick, TCP and character save report lines; at least 1. |
| `Network:PacketLimit` | 251 | `NetworkLimits` | Packets per window before the connection closes (`DescribeEntity` excluded). |
| `Network:PacketWindowSeconds` | 5 | `NetworkLimits` | Fixed window of the packet limit. |
| `Network:MaxConnectionsPerIp` | 8 | `NetworkLimits` | Open connections per remote address; extra sockets are closed on accept. |
| `Network:MaxConnections` | 6000 | `NetworkLimits` | Open sockets, from accept to the end of their session; past it a new socket is closed before its TLS handshake. 0 disables it. |
| `Network:MaxHandshakes` | 128 | `NetworkLimits` | TLS handshakes in progress; past it a new socket is closed before its handshake. 0 disables it. |
| `Network:MaxHandshakesPerIp` | 4 | `NetworkLimits` | TLS handshakes in progress per remote address, so a few hosts of silent sockets cannot hold every handshake. 0 disables it. |
| `Network:LoginTimeoutSeconds` | 30 | `NetworkLimits` | Close connections that have not logged in; checked every 5 s. |
| `Network:LoginFailuresPerAccount` | 5 | `NetworkLimits` | Bad-password logins per account per window before refusal. |
| `Network:LoginFailuresPerIp` | 20 | `NetworkLimits` | Bad-password logins per address per window before refusal. |
| `Network:LoginFailureWindowSeconds` | 300 | `NetworkLimits` | Window of both failure counters. |
| `Network:PlayerInfoBurst` | 512 | `NetworkLimits` | Token-bucket capacity for `DescribeEntity` requests. |
| `Network:PlayerInfoPerSecond` | 128 | `NetworkLimits` | Token refill rate for `DescribeEntity`. |
| `Network:StoredOperationBurst` | 40 | `NetworkLimits` | Token-bucket capacity for the requests that copy the character on the tick (inventory, progression, NPC services, item use, quests, restarts, wing gates); 0 disables the bucket. |
| `Network:StoredOperationsPerSecond` | 20 | `NetworkLimits` | Token refill rate of those requests; past the bucket they are refused as busy. |
| `Logging:LogLevel:Default` | `Information` | host | Standard .NET logging filter. |

Fixed values that are not settings: idle timeout 60 s, housekeeping period 5 s, report period 30 s,
tick rate 20 Hz, send backlog limit 512 KiB per connection (`FrameConnectionOptions.MaxPendingSendBytes`), presence
backlog 64 KiB (`ClientSession.PresenceBacklogBytes`) and its stall time 30 s. The send limit holds the presence backlog,
a full outbox's events and a full crowd's entries (`NetworkHardeningTests.SendLimitAsync`).

## Server settings table

The game settings are rows of `server_settings` (`name`, `value` as text, `description`, `updated_at`), described by
`ServerSettingCatalog`. At start the server adds the missing rows with their defaults (existing values stay, the
descriptions are refreshed); it reads the table again every `server.settings_reload_minutes` minutes, so an edited row
takes effect without a restart. A value the catalog refuses (not a number, out of range) keeps the default and is
logged as a warning; unknown rows are ignored. Game masters can read the table at once with `/reload_settings` and
show or write one row with `/setting name [value]` ([chat-and-gm.md](chat-and-gm.md)).

| Name | Default | Meaning |
| --- | --- | --- |
| `server.name` | `Etervale` | Up to 32 characters; sent in `ServerHello` and shown on the character screen. |
| `server.code` | 0 | Server number that prefixes the game-master information replies. |
| `server.max_users` | 0 | Players beyond which only game masters may log in; 0 for no limit. A game master's `/user_limit` lasts until this row changes. |
| `server.test` | false | Every game master may use `/expup`, `/GiveMeExp!` and `/GiveMeMoney!`. |
| `server.debug` | false | As `server.test`, and `/` with an item code drops that item. |
| `server.settings_reload_minutes` | 5 | Minutes between two readings, 1 to 1440. |
| `players.starter_gifts` | true | A new character receives its starter gifts ([application.md](application.md#starter-gifts)). |
| `gm.potion_monitor` | true | Enables `/get_potion`. |
| `experience.multiplier` | 5 | Times the monsters' experience, 1 to 1000. |
| `experience.event_percent` | 0 | Experience event: 0 off, else +100% (any value up to 199), +200%, +300% or +400% (400 and above) on each kill. |
| `world.clock_offset_minutes` | 0 | Game minutes added to the game clock (`WorldServer.GameClockMs` = Unix ms + offset x 800), -1440 to 1440. |
| `world.weather` | true | Rain over the fields that take it on the original's schedule ([world.md](world.md#weather)). |
| `world.weather_cycle_minutes` | 8192 | Game minutes from one rain to the next, 933 to 1000000. |
| `events.crystal` | 0 | Crystal event: 0 off, 1 from 12:00 to 23:59 server time, 2 all day ([special-items.md](special-items.md)). |
| `events.mandoo` | false | Rice cakes replace drop rolls (SP101 4%, SP102 2%). |
| `events.valentine_day`, `watermelon`, `valentine`, `candy_days`, `white_day`, `puzzle`, `gift_box` | 0 | Rate per 10000 drop rolls of each item event, 0 to 10000; 0 is off. |
| `aging.price_percent` | 100 | Aging price in percent of `(level + 1) * price / 2`; 0 makes aging free. The aging NPC's offer carries it to the client. |
| `aging.failure_percent` | 100 | Aging failure chance (fall or loss) in percent of each level's chance; 0 never fails. |
| `aging.double_percent` | 100 | Chance of a level at once below +10, in percent of each level's chance. |
| `mixing.price_percent` | 100 | Price of a mix or a reconstruction in percent of the item's price; carried to the client in the mixer's offer. |
| `alchemy.price_percent` | 100 | Price of turning sheltoms into force orbs, in percent of the price per sheltom. |

Settings apply on the tick (`WorldServer.ApplyOptionsAsync`): every open field and the fields opened later take the
experience, item event and aging values.

## TCP edge

### Connection lifecycle

1. `GameTcpServer` accepts, normalises IPv4-mapped addresses (`NetworkLimits.AddressOf`), and closes the socket
   without a reply when `MaxConnections` sockets are open, `MaxHandshakes` handshakes are in progress, or its address
   has `MaxHandshakesPerIp` handshakes in progress or `MaxConnectionsPerIp` sockets open (`ConnectionLimiter`).
2. `TlsTransport.AcceptAsync` runs the TLS handshake under its deadline; a failed or late handshake closes the socket.
   A `ClientSession` then wraps the `FrameConnection` (framing and coalesced writer; see
   [protocol.md](protocol.md#transport)) and sends the `ServerHello` (protocol version and minimum).
3. `FrameConnection.RunAsync` reads frames and awaits `ClientSession.HandleAsync` for each packet before reading the
   next: one connection's packets are processed strictly in order, and a slow database call delays that connection only.
4. Housekeeping (every 5 s): closes sockets silent for 60 s (`idle timeout`) and unauthenticated sockets older than the
   login timeout (`login timeout`; heartbeats do not extend it), prunes the login throttle and adds up traffic.
5. On close: `ClosePlayAsync` leaves the world and stores the capture taken as it left, the session token is removed
   from the registry, the per-address slot is released and the connection is disposed.
6. On shutdown every connection is closed with `server stopping` and awaited.

### Inbound packet mapping

The receive loop (`ClientSession.ReceiveAsync`) only reads: it applies the flood limit, answers `Heartbeat` at once and
copies every other packet into the session's `SessionPacketQueue`; `HandleQueuedAsync` takes them out one at a time, in
order, and `ClientSession.HandleAsync` switches on the decoded code. So a handler waiting on storage (a purchase, a
game master's account command, a slow or locked database) holds only the packets behind it, never the connection's
reading or its heartbeats. A movement report queued right behind another replaces it (the newer position wins), so a
held session catches up with one move. More than 512 waiting packets close the connection (`command backlog`); a
handler's unexpected failure closes it too, and is logged once the session has closed its play. Encryption is not
required per opcode; both envelopes reach the same switch.

| Packet | Handler | Application call | Reply |
| --- | --- | --- | --- |
| `Heartbeat` | receive loop | `ChatAppService.Heartbeat` (the standing notice) | Echo with the server's `Environment.TickCount64`. |
| `ChatSay` | `SayAsync` | `ChatAppService.SayAsync` (spoken line, player or game-master command) or `PartyAsync` (party flag) | Chat lines to the hearers or the other party members ([chat-and-gm.md](chat-and-gm.md)). |
| `PartyInvite`, `PartyAnswer`, `PartyCommand` | `InviteToPartyAsync`, `AnswerPartyAsync`, `CommandPartyAsync` | `PartyAppService` | None; chat lines, and `PartyRequest` / `PartyMembers` from the snapshots ([application.md](application.md#parties)). |
| `TradeRequest`, `TradeAnswer`, `TradeOffer`, `TradeLock`, `TradeCancel` | `RequestTradeAsync`, `AnswerTradeAsync`, `OfferTradeAsync`, `SignalTradeAsync` | `TradeAppService` | None; chat lines, and `TradeAsked` / `TradeDetails` + `TradeState` from the snapshots ([application.md](application.md#trades)). |
| `ContactChange`, `ContactRefresh` | `ChangeContactAsync`, `RefreshContacts` | `ContactAppService` | Two `ContactList` (friends, ignored); a refused change also a chat line ([application.md](application.md#contacts)). The lists load before the player joins the world and are sent once it joined; lists storage could not read stay empty and refuse changes. |
| `Login` | `LoginAsync` | `LoginThrottle.IsBlocked`, `AuthAppService.LoginAsync`, `ISessionRegistry.TryGet` | `CharacterList` + `LoginResult` 0, or `LoginResult` with the failure. The connection is logged in only once its character list is read: a list storage refuses answers busy, removes the new session and leaves the connection free to log in again. |
| `CharacterCommand` | `ApplyCharacterCommandAsync` | `CharacterAppService.CreateCharacterAsync(token, name, class, face)` or `DeleteCharacterAsync` | New list on success, then `CharacterResult`. |
| `EnterWorld` | `EnterWorldAsync` | `SessionAppService.EnterWorldAsync` | `WorldEntry` (id, field and spawn point, or the `EnterWorldFailure`). |
| `JoinWorld` | `JoinWorldAsync` | `PlayerSession.TryBeginPlay`, `WorldPlayerFactory.Create`, `WorldServer.JoinAsync` | `WorldJoined`; starts the presence pump. |
| `MovementReport` | `ReportMovement` | `WorldServer.MoveToFieldAsync` or `TryReportMovement` | Forced `FieldWarp` correction when rejected. |
| `AttackRequest` | `AttackAsync` | `WorldServer.InteractNpcAsync` (target is a known NPC) or `SetAttackTargetAsync` | `NpcResult`, or nothing (results arrive as presence). |
| `DescribeEntity` | `DescribeObject` | `PresenceEncoder.DescriptionPacket` | Plaintext `EntityDescription` when the entity is in view, from the last encoded frame. |
| `InventoryCommand` | `ApplyInventoryAsync` | `SessionAppService.ApplyInventoryAsync` | `InventoryResult`. |
| `ProgressionCommand` | `ApplyProgressionAsync` | `SessionAppService.ApplyProgressionAsync` | `ProgressionResult`. |
| `NpcServiceRequest` | `RequestNpcServiceAsync` | `NpcAppService.RequestAsync` | State messages, `NpcServiceResult`, and a forced `FieldWarp` for a teleport. |
| `QuestRequest` | `RequestQuestAsync` | `QuestAppService.RequestAsync` | `QuestResult`; travel to or from the arena arrives as the encoder's `FieldWarp`. |
| `SkillCast` | `CastSkillAsync` | `WorldServer.CastSkillAsync` | `SkillCastResult`. |
| `ItemUseCommand` | `UseItemAsync` | `ItemUseAppService.UseAsync` | `ItemUseResult` ([special-items.md](special-items.md)). |
| `RestartRequest` | `RestartAsync` | `RestartAppService.RestartAsync` | `RestartResult`; the arrival comes as the encoder's `FieldWarp`. |
| `WingGateRequest` | `TravelWingGateAsync` | `WingGateAppService.TravelAsync` | `WingGateResult`. |
| `PersonalShopOpen`, `PersonalShopClose`, `PersonalShopView`, `PersonalShopBuy` | `OpenPersonalShopAsync`, `ClosePersonalShopAsync`, `ViewPersonalShopAsync`, `BuyFromPersonalShopAsync` | `PersonalShopAppService` | `PersonalShopListing` and `PersonalShopDetails` for a view, then `PersonalShopResult`. |
| `YahooKey` | inline | `WorldServer.YahooAsync` | None; the cheer arrives as presence. |
| `DismissCrystalPets` | inline | `WorldServer.DismissCrystalPetsAsync` | None; the pets' end arrives as presence. |
| `LeaveWorld` | inline | `ClosePlayAsync` (leave, save) | `LeaveWorld` with `Saved` true when the character was stored. |
| a code without a handler | ignored | none | none |

Error handling inside `HandleAsync`:

| Exception | Effect |
| --- | --- |
| `InvalidSessionException`, `InvalidOperationException` (includes `WorldBusyException`) | Packet dropped, sampled `Rejected` warning; the connection stays open. |
| `DbException` | `ReplyStorageFailure`: login gets `LoginResult` busy, character commands get a `CharacterResult` with `CreateCharacterFailure.Rejected`, `EnterWorld` gets a `WorldEntry` with `EnterWorldFailure.Rejected`. |
| `InvalidDataException` (malformed packet from a `Read`) | Closes the connection (`handler failed`) and is logged as `Client session ... failed` once the session has closed its play. |
| anything else | Ends the session; logged as `Client session ... failed`. |

Inventory, progression, NPC service and quest handlers convert session, operation and storage exceptions into a failed
result packet instead, so the client's pending operation always completes. A `CommitInvariantException` (a commit
found the character or a ground reservation changed after its operation was stored, so storage is ahead of memory) is
not a refusal: it is logged as an error and closes the connection, and the next entry reloads the character.

### Outbound presence

After `WorldJoined`, `PumpAsync` reads the player's `WorldOutbox` and calls `PresenceEncoder.Encode` per snapshot on a
thread-pool task, separate from the receive loop, then releases the snapshot's frame. Before each read it waits until
the connection's backlog (`FrameConnection.WaitForBacklogAsync`, bytes queued or being written) is at most 64 KiB, so
a client that reads slower than its world changes gets fewer snapshots, each joining what it missed, instead of a
backlog that grows to the send limit; a backlog that stays above it for 30 s closes the connection (`send stalled`),
since its client stopped reading. A snapshot's packets are queued inside one `FrameConnection.Batch`, which holds back
the writer's wake-up, so they normally leave in one write.
Per snapshot, in order:
`FieldWarp` on a field change (after removing everything known), the events of earlier ticks the pump fell behind on
(each tick's events after its own `PresenceClock`), each `EntityDescription` pushed ahead of the entity's spawn or
revision change, `PresenceEntries` (one or more, carrying the tick's clock; `PresenceClock` only when the tick has events
and no entries), `RemoveObjects`, `CombatEvents`, `SkillEvents`, `StatusEffects` (with the running
statuses of entities the receiver starts seeing), `ItemEffects`, `GroundItems`/`GroundItemsRemoved`, `OwnerStatus` with `Inventory`
parts and `Skills` when their revisions changed, `SkillCooldowns`, then `QuestJournal` and `QuestNotices` from the
snapshot's quest update.
Events go to a receiver when they name an entity it knows (or itself). When the field's tick has more events than
twice the entities the receiver knows, the encoder collects them from the frame's `EventRoutes` (the tick's events
sorted by the entity ids they name; one binary search per known entity) instead of scanning every event of the field;
both ways give the same events in the field's order. A pump that fell behind gets merged copies of several ticks'
events and scans them.
In the quest arena (field 32) the encoder leaves out other players and monsters of other players' duels. View ranges,
rates and the movement budget are in [protocol.md](protocol.md#server-capacity). A reader 4096 events behind loses the
events it has not read, never its connection: its next snapshot has `EventsLost`, and the encoder sends again the
statuses running on every entity the receiver shows. A status removed early (dispelled, cancelled) among the lost
events stays on that client until its own time runs out.

### Movement and corrections

- A report naming a field other than the session field goes to `MoveToFieldAsync`; a refusal or a full queue sends
  a forced `FieldWarp` back to the encoder's last known self position.
- Other reports go to `WorldServer.TryReportMovement` as values; the session is the `IMovementVerdictSink`. A rejection
  queues the correction on the thread pool (the tick never waits for the encoder), which sends a forced `FieldWarp`,
  at most once per 300 ms. Reports keep a quota of their own (`WorldServer.MostPendingMovements`, 8), apart from
  the player's commands; a full queue or quota silently drops the report; the next report carries a newer position.
- `ClientSession` serialises its own scratch buffer with `_sendGate`.

### Presence propagation

What one player does reaches exactly the players that see it. The guarantees, all covered by the `multiplayer:` cases
of the regression suite ([testing.md](testing.md#multiplayer-propagation)):

| What | Who receives it | How and when |
| --- | --- | --- |
| An entity (player, monster, summon, pet, NPC) | Players of the same field within the connect range (about 1086 units); kept to 1.15 times that range; hidden game masters and other players' duel opponents never | Spawn (its `EntityDescription` pushed ahead of a presence spawn, unless it is a shared template's the receiver holds) the tick it enters the receiver's view; `RemoveObjects` the tick it leaves, dies away, logs out, disconnects or changes field |
| Position, facing, motion | Its viewers | Motion, HP, max HP, target, attack and template changes the tick they happen; positions at 5 Hz near (543 units), 2 Hz far, within 2048 bytes per receiver per tick, most overdue first |
| Description (level, equipment, looks, village flag, owner, size) | Its viewers | A new field-wide revision in the next entry, its `EntityDescription` pushed ahead of it |
| Combat, skill and item events | The players that know its attacker or target (and the player itself) | Once, in server order, on the tick they happened; dropped only for a reader 4096 events behind, which then gets the running statuses again |
| Statuses | The players that know the entity | `Add`, `Refresh`, `Remove` and ticks as they happen; a player that starts seeing the entity gets its running statuses with the time left |
| Ground drops | Players within about 905 units; private drops only to their owner | `GroundItems` when they enter range, `GroundItemsRemoved` (batched) on pickup, expiry or leaving range |
| Owner state, inventory, skills, cooldowns, premium timers, quests | The player itself | When their revisions change |
| Corrections | The player itself | Forced `FieldWarp`; viewers only ever see the authoritative position |

- A crowd shows at most `PresenceEncoder.MaxVisible` (256) other entities, of them at most `MaxVisiblePlayers` (100)
  players: the nearest players, then the nearest monsters and NPCs to the rest, plus the receiver's NPCs, its attack
  target, the entities targeting it, its summons and its party members; an entity already shown stays until it is 1.15
  times its crowd's cut-off distance away. A client draws a crowd of players at a cost a crowd of monsters does not have.
- Every viewer of an entity sees the same state for the same server tick: entries come from the field's single frame
  of that tick. Events follow the state of their tick.
- A pump that falls behind gets the latest state of each entity and every event since its last read, each event on its
  own tick (`WorldSnapshot.EventSegments`), so a slow client sees fewer position samples but nothing out of order.
- Two sessions for one character: the second entry is refused (`AlreadyActive`) until the first has left and saved;
  viewers never see two avatars of one character.

### Limits and abuse handling

| Guard | Scope | Behaviour |
| --- | --- | --- |
| Connection caps | process-wide and per address | Sockets past `MaxConnections` open, `MaxHandshakes` handshakes in progress or `MaxHandshakesPerIp` handshakes from one address are closed before TLS. |
| `ConnectionLimiter` | per remote address, process-wide | Refuses sockets above `MaxConnectionsPerIp`; released when the session ends. |
| `PacketRateLimiter` | per connection, fixed window | Counts every decoded packet except `DescribeEntity`; exceeding it closes with `packet flood`. |
| `TokenBucket` | per connection | `DescribeEntity` beyond the bucket is dropped (a few-byte request costs a description of up to a few hundred bytes). |
| Stored operations | per connection | A request that copies the character on the tick beyond `StoredOperationBurst` / `StoredOperationsPerSecond` is answered busy (the `TooManyRequests` game text). |
| `LoginThrottle` | per account (case-insensitive) and per address | Only `BadCredentials` counts. A blocked attempt gets `LoginResult` busy before the password hash is computed. Success clears the account counter only. At most 65536 keys per map. |
| Login timeout | per connection | Unauthenticated sockets close after `LoginTimeoutSeconds`. |
| Second login | per connection | `Login` after a successful login gets `LoginResult` 3 (already logged in). |
| World quota | per player | `WorldServer` refuses more than `MaxPendingCommandsPerPlayer` queued commands with `WorldBusyException`; the shared queue holds 65536. |
| Presence backlog | per connection | Past 64 KiB unsent the pump waits, and the outbox joins the snapshots meanwhile; still past it after 30 s closes the connection (`send stalled`). |
| Send backlog | per connection | More than 512 KiB unsent closes the connection (`send backlog exceeded ...`). |

`LoginResult` statuses: 1 bad credentials (unknown account, wrong or empty password), 2 account blocked, 3 already
logged in on this connection, 4 busy (throttled, full or storage failure), 5 outdated client protocol.

### Logging

| Line | Period |
| --- | --- |
| `World tick: N players, p50, p99, max over N ticks (budget 50 ms), N caught up late, N skipped; commands p50, p99, at most N a tick; fields p50, p99; process N cores, N MiB/s allocated, GC gen0/1/2, heap, working set` | `Server:ReportSeconds` (30 s) |
| `TCP: N connections, N in world, out KiB/s (KiB/s per connection), in KiB/s` | `Server:ReportSeconds` (checked every 5 s) |
| `Refused connection from ...` | at most one per 5 s, with the count of suppressed lines |
| `Closed ...: reason` | at most one per 1 s; normal reasons are never logged |
| `Rejected <code> from ...: reason` | at most one per 5 s across all connections |

Normal close reasons: `peer closed`, `receive ended`, `cancelled`, `server stopping`, `disposed`. Everything else (idle
or login timeout, packet flood, protocol error, send backlog, send stalled, presence failed, socket errors) is sampled.

## How to

- **Add a setting.** Read it in `HostingExtensions` (world, provider) or `NetworkLimits.From` (network, via `Read` so
  negatives fail), give it a default constant in the options type, validate it, add it to the settings table here and to
  `NetworkHardeningTests` (`NetworkConfigurationAsync` / `WorldOptionsAsync`).
- **Handle a new client packet.** Define the packet and opcode
  ([protocol.md](protocol.md#how-to-add-an-opcode-or-packet)), add a `case` in `ClientSession.HandleAsync`, convert it
  into an application input (`Shared/PristonTale.Application`), and reply with `SendLarge(packet.Write, Size)`. Catch the exceptions the call can throw and answer with a failed result when the client waits for one. Update
  the inbound table above.
- **Send new tick-driven state.** Add it to `WorldSnapshot`/`OwnerState` in the application layer, map it in
  `WireMapping`, emit it in `PresenceEncoder.EncodeLocked` (call `SendClock` first if it belongs on the timeline),
  decode it in the client's `PresenceDecoder`, and cover it with a `WireRoundTrip` test.
- **Register a startup step.** Add an `IHostedService` in `AddPristonServer` at the right position: anything the world
  reads must be registered before `WorldTickService` and `GameTcpServer`. Throw from `StartAsync` to stop startup.
- **Run against a copy or Postgres.** `--Database:SqlitePath=<copy.db>` or `Database__Provider=Postgres` with
  `ConnectionStrings__Postgres`; for local load tests add `--Network:MaxConnectionsPerIp=0 --Network:MaxHandshakesPerIp=0`.

## Pitfalls

- A relative `Database:SqlitePath` is not relative to the working directory but to the solution root found from the
  binary. `DevAccounts` resolves `--sqlite` the same way; `WorldImport` uses its `--sqlite` path as given (relative to
  the working directory). Run the tools from the repository root so all three open the same file.
- An un-imported database starts cleanly with no monsters or NPCs; check the `World catalogs loaded` line.
- `Assets:AllowMissingTerrain` leaves the affected fields on `FieldTerrain.None`: no floor or wall checks for movement
  reports, and positions keep the heights they are given.
- `HandleAsync` awaits application calls on the session's queue, never in the receive loop; never block there on the
  tick, and keep storage writes that need no answer (logs, the audit) on their background queues. World calls return tasks
  that complete inside the tick, so attacks are fire-and-forget; movement verdicts come back through the sink on the
  tick thread, where nothing may block (not even the encoder's lock).
- `WorldSnapshot.ReleaseFrame` must follow every snapshot read from a `WorldOutbox`: a pinned frame is never reused,
  so a reader that forgets it makes the field allocate new frames.
- `WorldBusyException` is thrown synchronously by `WorldServer` methods, not through the returned task; callers must
  wrap the call itself, as `ReportMovement` and `CastSkillAsync` do.
- An NPC click is recognised only when the `AttackRequest` target is an NPC the receiver's `PresenceEncoder` currently
  knows; an unknown entity id becomes an attack target change.
- The server owns the character record; the client never sends one. `ClientSession` saves the
  character on `LeaveWorld` and on disconnect (`SessionAppService.SaveCharacterAsync`, which waits until stored);
  inventory, progression and NPC transactions queue their own changes.
- The save journal is not a backup: it holds only captures storage refused while the server stopped, and the next start
  stores it before players enter. Do not delete it while the server is down unless those captures are to be lost.

## Related

- [protocol.md](protocol.md): wire formats, opcodes, presence timeline, capacity numbers.
- [application.md](application.md): `WorldServer`, sessions, transactions and the tick.
- [persistence.md](persistence.md): providers, migrations, repositories.
- [world-import.md](world-import.md): filling the catalogs the server loads.
- [architecture.md](architecture.md): process and threading model.
- [testing.md](testing.md): regression runner, smoke client and load test.
- [tools.md](tools.md): `DevAccounts`, `SmokeClient`, `LoadTest`.
