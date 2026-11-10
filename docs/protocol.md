# Network protocol

Etervale is online only. Client and server speak Etervale's own message set over TLS: every message has an Etervale code (`0x4554xxxx`), entity ids and sequences are 64-bit, and text is
UTF-8. The server is authoritative; the client sends requests and reports and draws what the server decides.
The codecs live in `Shared/PristonTale.Protocol`; the server edge is `Server/PristonTale.Server/Net`
([server.md](server.md)) and the client edge is `Client/PristonTale.Client.Net` ([client.md](client.md)).

## Read this when

- You add an opcode, a packet or a field, or change a layout or size.
- A packet fails to decode, the connection closes with `protocol: ...`, or sizes disagree between client and server.
- You work on presence: presence entries, the presence clock, remote playback timing, view ranges or bandwidth.
- You change server movement authority, skill cast validation or NPC service messages on the wire.
- You measure or tune server capacity.

## Code map

| Path | Responsibility |
| --- | --- |
| [Binary/EtervaleOpcodes.cs](../Shared/PristonTale.Protocol/Binary/EtervaleOpcodes.cs) | `EtervaleOpcodes` (`0x4554xxxx`), grouped by domain. |
| `Binary/FrameCodec.cs` | `FrameCodec`: the `{size, code}` header and the 8192-byte limit. |
| `Binary/PacketReader.cs`, `Binary/PacketWriter.cs` | Little-endian sequential reader/writer; the writer patches the size in `Finish()`. |
| `Binary/Utf8Text.cs` | Text fields: `{uint16 ByteLength, UTF-8}`, cut at a whole character. |
| `Binary/GameTextWire.cs` | Game text fields: `{uint16 Id, uint16 Count, texts}`. |
| `Binary/WireHeader.cs` | `{uint16 Version, uint16 Flags = 0}` body header: `Write(version)`, `Read(name, minimumVersion = 1)` (newer versions append fields). Domain helpers (`NpcWire.Header`, `ItemUseWire.ReadHeader`...) delegate to it. |
| `Binary/WireList.cs` | `IWireEntry<T>` (static `Size`, `Read`, instance `Write`) and `WireList`: `{int32 Count, entries}` with the entries filling the rest exactly; the cooldown, timer, item effect, shop quote and item detail lists use it (the skill list, the object removals and the aging progress still check their counts themselves). |
| `Packets/IFramePacket.cs` | `IFramePacket`: a message that writes itself as one frame; every client-to-server message implements it. |
| `Packets/CastlePackets.cs` | `CastleState` (Bless Castle's owner, league mode and siege board), the owner's window (`CastleOwnerRequest`, `CastleOwnerState`, `CastleOwnerResult`). |
| `Packets/BellatraPackets.cs` | Bellatra's secretary (`BellatraClanRequest`, `BellatraClanState`, `BellatraClanResult`). |
| `Packets/AccountPackets.cs` | `ProtocolVersion`, `ServerHello`, `Login`/`LoginResult`, `CharacterList`, `CharacterCommand`/`CharacterResult`, `Heartbeat`. |
| `Packets/PlayerActionPackets.cs` | `MovementReport`, `AttackRequest` and `FieldWarp`. |
| `Packets/WingGatePackets.cs` | `WingGateRequest` and `WingGateResult`. |
| `Packets/PresencePackets.cs` | `PresenceEntries`: `VarInt`, `PresencePoint`, `PresenceFields`, `PresenceEntry`, `PresenceWire` (entry codec), writer and reader. |
| `Packets/EventWire.cs` | What the compact combat, skill and status event lists share: entities by presence handle (`IEventHandleWriter`, `IEventHandleReader`), times, points, header and batching. |
| `Packets/WorldEntryPackets.cs` | `EnterWorld`, `WorldEntry`, `JoinWorld`, `WorldJoined` and `LeaveWorld`. |
| `Packets/GroundItemPackets.cs` | `GroundItems` and `GroundItemsRemoved`. |
| `Packets/ChatPackets.cs` | `ChatSay` (typed line), `ChatLine` (received line, kind `ChatKind` from Contracts). |
| `Packets/EtervalePackets.cs` | Owner status, inventory parts, skills, inventory/progression commands and results, combat events, removals, presence clock. |
| `Packets/ItemPackets.cs` | Owner item details (`ItemDetailEntry`, `ItemDetailsPartPacket`), the equipment status (`OwnerItemStatusPacket`) and the aging progress (`AgingProgressPacket`). |
| `Packets/SkillPackets.cs` | Skill cast, cast result, skill events, status effects, cooldowns. |
| `Packets/PersonalShopPackets.cs` | Personal shop open, close, view and buy requests; result, listing and details. |
| `Packets/PartyPackets.cs` | Party invite, answer and command requests; the request a player receives and the party's members. |
| `Packets/TradePackets.cs` | Trade request, answer, offer, lock and cancel; the request a player receives, the trade window and its items' values. |
| `Packets/ContactPackets.cs` | Friends page: a change to a list, a refresh, and one list with the friends' presence. |
| `Packets/NpcPackets.cs` | NPC result, service request/result, shop catalog and quotes, warehouse, teleport list, item work result. |
| [Transport/FrameConnection.cs](../Shared/PristonTale.Protocol/Transport/FrameConnection.cs) | One TLS stream: receive loop and coalesced single-writer send. |
| `Transport/TlsTransport.cs` | Server and client handshakes, `ServerCertificatePolicy` (system trust, loopback, pin). |
| `Transport/IPacketSink.cs` | `Send`; implemented by `FrameConnection` and test sinks. |
| `Server/PristonTale.Server/Net/` | `ClientSession` dispatch, `PresenceEncoder`, `WireMapping`, `NpcWireMapping`. |
| `Client/PristonTale.Client.Net/GameServerConnection.cs` | Client requests, heartbeat, request/reply correlation. |
| `Client/PristonTale.Client.Net/Online/PresenceDecoder.cs` | Client decoding of presence packets into `PresenceUpdate`s. |
| `tests/PristonTale.Tests/BinaryProtocolTests.cs`, `SkillWireTests.cs`, `NpcServiceTests.cs`, `WireRoundTrip.cs` | Layouts, TLS connection and certificate checks, encoder-decoder round trips. |

The protocol project's `obj/` folder can contain stale generated sources (for example `messages/Session.cs`) that a text
search finds; they are not part of the build.

## Conventions

| Item | Rule |
| --- | --- |
| Byte order | Little-endian everywhere. |
| Header | `{int32 size, uint32 code}`; `size` includes the header. |
| Positions | Fixed point, world units × 256 (`GameUnits.ToFixed` from Contracts, `FixedShift` 8). |
| Angles | Skill messages: 4096 per turn (`GameUnits.ToAngle`); movement reports: `uint16`, 65536 per turn; presence: `uint8`, 256 per turn. |
| Entity ids | 64-bit (`uint64` or `varuint`) on the wire and in memory; players count from 1, monsters, NPCs and summons from `2^32`. |
| Text | UTF-8 with a `uint16` byte length, cut at a whole character to the field's byte limit (`Utf8Text`); malformed UTF-8 is refused. |
| Game text | A line for the player, worded by the client in its language: `{uint16 Id, uint16 Count, Count texts (64 B)}` (`GameTextWire`), at most 4 texts. `Id` is `GameTextId` (append only), the texts fill its phrase's placeholders in order. |
| Item identity | Item codes derived from the item key (`ItemCodes`), e.g. gold is `GG101`. |
| Reading | `PacketReader` requires `size == span length` and `size >= minimum`; trailing bytes are accepted. Overruns throw `InvalidDataException`. |
| Versioned bodies | Messages with `{uint16 Version, uint16 Flags}` reject version 0. Fixed-size bodies accept newer versions that append fields; list bodies check their length exactly. |

## Transport

| Item | Value |
| --- | --- |
| Port | TCP 10009; the server's `Network:Port` can change it |
| Security | TLS 1.2 or 1.3 (`TlsTransport`) around the whole stream; the handshake must finish within `Network:HandshakeTimeoutSeconds` (10) |
| Frame | `int32 size` (including the header), `uint32 code`, 8 to 8192 bytes, inside the TLS stream; anything else closes the connection |
| Send backlog | More than 576 KiB pending closes the connection (room for a full event outbox and 256 entities described at once above the 64 KiB presence backlog); the server sends a slow reader fewer presence snapshots, and closes one whose backlog stays above 64 KiB for 30 s |

The server presents the certificate in `Network:Certificate:Path` (PKCS#12, default `data/server-certificate.pfx`,
password `Network:Certificate:Password`). When the file is missing it creates a self-signed ECDSA P-256 certificate for
`localhost` and the machine name and logs its SHA-256. The client (`ServerCertificatePolicy`) accepts a certificate the
system trusts for the host name, any certificate when the host is loopback, or exactly the certificate pinned with
`cert=<sha-256 hex>` in the address (`tcp://host:10009?cert=...`); a pin replaces every other rule.

`FrameConnection` reads into a 16 KiB buffer and awaits the handler for each frame before reading the next one. The
packet memory is valid only until the handler's task completes. Sends from any thread append to one pending buffer that
a single writer flushes, so all frames queued during one write leave together in the same TLS records.

## Login, character select and world entry

Bodies start with `uint16 Version = 1`; text is UTF-8 with a `uint16` byte length.

| Step | Packet | Notes |
| --- | --- | --- |
| Connect | S→C `ServerHello` (`0x45540F00`) | `{uint16 Version, uint16 0, uint32 Protocol, MinimumProtocol, int32 FullPlayers, text ServerName (32 B)}`: `FullPlayers` = players in the world when the player limit (`server.max_users` or `/user_limit`) is reached, else 0 (then only game masters may log in); `ServerName` is `server.name`. |
| Log in | C→S `Login` (`0x45540F01`) | `{uint16 Version, uint16 0, uint32 Protocol, text Account (64 B), text Password (128 B), text Language (16 B)}`. `Protocol` is `ProtocolVersion.Current` (26); a change of any message's layout raises it and `ProtocolVersion.Minimum`, so a client built before it is refused instead of misreading the world (a login without the language, from a client before protocol 4, is still read so the server can answer it is outdated). `Language` is the client's language (`en` or `pt-BR`; any other value is English): the server sends names and NPC dialog in it, falling back to English. |
| Result | S→C `CharacterList` (`0x45540F03`), then `LoginResult` (`0x45540F02`) | `LoginResult` = `{uint16 Version, uint8 Status, uint8 0}`: 0 ok, 1 bad credentials (also unknown accounts), 2 account blocked, 3 already logged in on this connection, 4 busy (throttled, full or storage failure), 5 client protocol older than the server's minimum. A failed login sends no list. |
| Character list | S→C `CharacterList` | `{uint16 Version, uint16 0, int32 Count (at most 6)}`, then `{text Name, uint32 Class (PlayerClass), int32 Level, uint8 Tier, Face, Hair, Flags (1 in a village), uint32 WeaponCode, ArmorCode, OffHandCode, uint32 0, text ClanName (48 B; empty out of a clan)}`. |
| Create, delete | C→S `CharacterCommand` (`0x45540F04`), S→C `CharacterResult` (`0x45540F05`) | Command `{uint16 Version, uint8 Action (1 create, 2 delete), uint8 0, int32 Class (PlayerClass), FaceIndex, text Name}`; result `{uint16 Version, uint8 Action, uint8 Ok, int32 Failure (CreateCharacterFailure), text Name}`. |
| Select | C→S `EnterWorld` (`0x45540E00`) | `{uint16 Version = 1, uint16 0, text Name (64 B)}`. |
| Entry | S→C `WorldEntry` (`0x45540E01`) | `{uint16 Version, uint8 Status (0 entered, else EnterWorldFailure), uint8 0, uint64 EntityId, int32 Field, int32 X, Z (x256)}`. The owner's stats, inventory and skills follow on the presence stream. |
| Join | C→S `JoinWorld` (`0x45540E02`), S→C `WorldJoined` (`0x45540E03`) | `JoinWorld` = `{uint16 Version, uint16 0, uint64 EntityId}`, which must be the id from `WorldEntry`; `WorldJoined` = `{uint16 Version, uint8 GameMasterLevel, uint8 0, uint64 EntityId, int64 ServerTimeMs, int64 GameClockMs}`; the level only enables client-side game master controls; `GameClockMs` is the server's game clock (`WorldClock`), which the client's sky follows from then on on its local clock. |
| Heartbeat | `Heartbeat` (`0x45540F06`) every 4 s | `{uint16 Version, uint16 0, uint32 Counter, int64 TimeMs}`; echoed with the server's time. The server closes connections silent for 60 s. |
| Leave | `LeaveWorld` (`0x45540160`) | `{uint16 Version, uint8 Saved, uint8 0}`: the client sends Saved 0; the server answers with Saved 1 after it stored the character. |

Successful creation and deletion send a fresh `CharacterList` before the result. Storage failures answer the pending
request (`LoginResult` busy, a `CharacterResult` with `Rejected`, or a `WorldEntry` with `Rejected`) instead of
closing.

## In-world messages

| Message | Direction | Use |
| --- | --- | --- |
| `MovementReport` (`0x45540D00`) | C→S | The client's own position, facing, motion, action target and field (see [Player actions](#player-actions)). Sent every 200 ms while moving, on state changes (at most one per 50 ms) and every second while idle. |
| `PresenceEntries` (`0x45540900`) | S→C | Nearby objects under the receiver's handles: spawns, changed values and positions (see [Presence entries](#presence-entries)). |
| `DescribeEntity` / `EntityDescription` (`0x45540901` / `0x45540902`) | C→S / S→C | The server pushes an object's description ahead of its spawn or revision change; the client asks for one it still lacks (see [Entity descriptions](#entity-descriptions)). Monsters and NPCs are cached by template key; owned summons and crystal pets carry template 0 in their presence entries and are always described on their own. The client asks again after a second without an answer (the server drops requests beyond its bucket). |
| `FieldWarp` (`0x45540D02`) | S→C | The owner's authoritative field and position: a field confirmation, or forced (a correction after a rejected report, a teleport, a restart). |
| `AttackRequest` (`0x45540D01`) | C→S | The clicked target and the request sequence. Monster: attack lock (held or one swing). NPC: opens it. |
| `CloseRequest` (`0x45540D03`) | S→C | Asks the client to quit (the end of a game master's shutdown countdown). |
| `WingGateRequest` / `WingGateResult` (`0x45540D04` / `0x45540D05`) | C→S / S→C | A trip from a warp stone to a stone of the wing warp map, correlated by operation id; a trip that warped also sends a forced `FieldWarp` ([world.md](world.md#wing-warp-gates)). |
| `YahooKey` (`0x45540D06`) | C→S | Space within the minute a yahoo word opened; the server checks the window, the motion and the stamina, and the cheer shows as the presence motion `yahoo` ([chat-and-gm.md](chat-and-gm.md#server-routing)). |
| `SwingRequest` (`0x45540D07`) | C→S | Space with nothing ahead in an action field; the server swings at nothing, the way the player faces, when it may swing and holds no attack (`WorldServer.SwingAtNothingAsync`), and the swing shows as the presence motion `attack`. |

### Player actions

Bodies start with `uint16 Version = 1`; positions are fixed point (x256), entity ids and sequences 64-bit.

| Message | Layout after the version | Size |
| --- | --- | --- |
| `MovementReport` | `uint8 Motion (0 stand, 1 walk, 2 run), uint8 0, int32 Field (-1 keeps the field), int32 X, Y, Z, uint16 Facing (65536 per turn), uint16 0, uint64 ActionTarget` | 40 |
| `AttackRequest` | `uint8 Flags (1 held attack), uint8 0, uint64 Target (0 stops), uint64 Sequence` | 28 |
| `FieldWarp` | `uint8 Flags (1 forced), uint8 0, int32 Field, int32 X, Y, Z` (the client puts the owner on the floor nearest Y) | 28 |
| `CloseRequest` | `uint16 0` | 12 |
| `YahooKey` | `uint16 0` | 12 |
| `SwingRequest` | `uint16 0` | 12 |
| `WingGateRequest` | `uint16 0, Guid OperationId, uint8 Destination (map index 0..8), uint8 0, uint16 0` | 32 |
| `WingGateResult` | `uint16 0, Guid OperationId, uint8 Status (WingGateStatus: 0 warped, 1 rejected, 2 not at a stone, 3 beyond the wing, 4 level too low, 5 not enough gold, 6 busy, 7 storage failed), uint8 Destination, uint16 0, int64 Fee` | 40 |


Other field values:

- A presence spawn carries the descriptor revision (1 to 255) of the entity's description. The field
  keeps one revision per entity for every viewer (`FieldDescriptors`) and increments it when level, max HP, template,
  speed, attack rate, equipment, looks (hair, face, pets, big head), village flag, tier, owner or scale change.

### Authority

The server validates client reports instead of trusting them (`WorldPlayer.ReportMovement`):

- Reports spend a distance budget that refills at 1.35 times the run speed (a game master's at 1.35 times the dash
  speed, 3000/256 units per original tick) and holds at most two seconds of travel plus 32 units, so many small reports
  cannot add up to more than one long report could.
- During a swing, flinch or potion, drift beyond 192 units is rejected and smaller drift is ignored. Reports from a dead
  player are ignored without a correction.
- With the field's stage loaded, a report is walked in pieces of at most 48 units; each follows the wall slide and may
  end at most 8 units from its point on the reported path. The height follows the floor and falls off ledges at 560
  units a second since the last accepted report, so it can stay above the floor while the player falls. A report is
  rejected when an 8-unit step finds no walkable floor within 24 units of climb (unless the player is falling) or when it ends
  over no floor. The client sends a report when a fall begins and when it lands.

A rejected report is answered with a forced `FieldWarp` (at most every 300 ms). A field change in the report is
accepted only when the player stands at a warp gate or field gate leading to that field and meets its level limit
(`FieldTransition.TryResolve`); otherwise the server forces the player back. The client walks into attack range itself,
and the server swings only once the reported position is in range. Damage, experience, drops and inventory are computed
on the server.

## Etervale extension opcodes

Every message code has the high word `0x4554` ("ET"); every message keeps the `{size, code}` header and the
8192-byte frame limit.

| Code | Name | Direction | Content |
| --- | --- | --- | --- |
| `0x45540100` | `OwnerStatus` | S→C | Level, unsigned 64-bit experience (current, level start, next level), 64-bit money, resources, attributes, combat values and revisions, then the elite skill points and the class tier (0..4), the bag page shown and the weapon pair held (a byte each, 0 A, 1 B). 158 B. |
| `0x45540101` | `Skills` | S→C | Skill revision and (code, rank, uses) entries, at most 512. |
| `0x45540102` | `Inventory` | S→C | Inventory revision split into parts of up to 48 items, each `{Guid, uint32 Code, int32 Kind, Count, Anchor, Slot, int16 Width, Height, text Name (64 B), text Link (48 B: a bound Union Core's clan member)}`; the client replaces its inventory when all parts of a revision arrived. |
| `0x45540110` / `0x45540111` | `InventoryCommand` / `InventoryResult` | C→S / S→C | Transactional inventory operation, correlated by operation id. A ground pickup (`InventoryAction.Pickup`) names the drop in `ItemId`, with `Destination` 0 (received as any item) or `InventoryCommand.PickupToBag` (1: into the bag as a stack of its own, for the client's cursor, or to the hand when the page shown is full; anything else is refused); a throw (`InventoryAction.Drop`) names the bag item or the item in the hand and the amount in `Count`; `Move`, `Merge` and `Swap` naming the item in the hand set it down on the page shown; a gold throw (`InventoryAction.DropGold`) only the amount. |
| `0x45540120` / `0x45540121` | `ProgressionCommand` / `ProgressionResult` | C→S / S→C | Attribute allocation and skill learning. |
| `0x45540130` | `NpcResult` | S→C | Completes an NPC click: name, typed services with availability and the line the NPC says. |
| `0x45540131` / `0x45540132` | `NpcServiceRequest` / `NpcServiceResult` | C→S / S→C | One typed NPC service request (open, close, refresh, buy, sell, repair, warehouse moves and gold, teleport, puzzle exchange, aging), correlated by operation id. |
| `0x45540133` | `ShopCatalog` | S→C | The NPC's stock with server prices (at most 96), sent before the result of a shop open. |
| `0x45540134` | `ShopQuotes` | S→C | Sell price and repair cost of every owned item at the inventory revision (at most 128). |
| `0x45540135` | `Warehouse` | S→C | The account warehouse: revision, gold, weight and items by grid cell (at most 100). |
| `0x45540136` | `TeleportList` | S→C | A teleporter's destinations with price and minimum level (at most 16). |
| `0x45540137` | `ItemWork` | S→C | What a work on an item (an aging) came to, sent before its result. |
| `0x45540140` | `CombatEvents` | S→C | Resolved swings, hits, potions and monster skill impacts. The original lets each client roll its own hits. |
| `0x45540150` | `RemoveObjects` | S→C | Serials leaving the receiver's view. The original client times objects out after 4-30 seconds. |
| `0x45540160` | `LeaveWorld` | C→S, echo | Leave the world after the server saves the character. |
| `0x45540170` | `PresenceClock` | S→C | Server tick of the events that follow when no presence entries of that tick carried it (see below). |
| `0x45540180` | `Weather` | S→C | `{uint16 Version = 1, uint16 0, uint8 Kind, uint8 Intensity, uint8 WindHeading, uint8 Wind}`: the receiver's field weather (`WeatherState`), when it enters the world and at each change. 16 B. |
| `0x45540200` | `SkillCast` | C→S | Cast request (see below). |
| `0x45540201` | `SkillCastResult` | S→C | Answer to the caster's own cast, by sequence. |
| `0x45540202` | `SkillEvents` | S→C | Skill starts, impacts, releases and cancels on the presence timeline. |
| `0x45540203` | `StatusEffects` | S→C | Modifier effects added, refreshed, removed or ticking, on the presence timeline. |
| `0x45540204` | `SkillCooldowns` | S→C | The owner's running cooldowns whenever the set changes. |
| `0x45540300` | `ItemDetails` | S→C | The values of every owned item copy at an inventory revision, in parts sent before its `Inventory` parts. |
| `0x45540301` | `OwnerItemStatus` | S→C | Carried and maximum weight, critical, block, range, attack speed, belt capacity, regeneration, magic mastery and the eight resistances; sent before every `OwnerStatus`. |
| `0x45540302` | `AgingProgress` | S→C | The counts of the owner's worn aging copies toward their goals, whenever a count moves. |
| `0x45540400` / `0x45540401` | `ItemUseCommand` / `ItemUseResult` | C→S / S→C | Use of one carried item, correlated by operation id; a travel item also sends a forced `FieldWarp`. |
| `0x45540402` | `PremiumTimers` | S→C | The owner's running premium timers whenever they change. |
| `0x45540403` | `ItemEffects` | S→C | Item effects around players (firecrackers, learn ring, mastery, pet strikes), on the presence timeline. |
| `0x45540404` | `DismissCrystalPets` | C→S | `{uint16 Version = 1, uint16 0, uint32 StatusCode}`: the owner confirmed dismissing the pet its status code names (each pet has its own); the status's removal answers it. |
| `0x45540500` / `0x45540501` | `QuestRequest` / `QuestResult` | C→S / S→C | One quest step (talk, proceed, accept, class change, reward choice, skill redistribution), correlated by operation id (see below). |
| `0x45540502` | `QuestJournal` | S→C | The owner's quest journal view whenever its revision changes. |
| `0x45540503` | `QuestNotices` | S→C | Quest notices of the world tick and a quest dialog the world opened. |
| `0x45540600` .. `0x45540603` | `PersonalShopOpen`, `PersonalShopClose`, `PersonalShopView`, `PersonalShopBuy` | C→S | Open a personal shop over bag items, close it, show a seller's shop, buy from it (see below). |
| `0x45540604` | `PersonalShopResult` | S→C | The answer to a personal shop request, by operation id. |
| `0x45540605` / `0x45540606` | `PersonalShopListing` / `PersonalShopDetails` | S→C | A seller's shop as the viewer sees it and its copies' values, sent before the result of a view or a purchase while the shop is open. |
| `0x45540700` .. `0x45540702` | `PartyInvite`, `PartyAnswer`, `PartyCommand` | C→S | Ask a player to group, answer a request, a party window button (see below). |
| `0x45540703` / `0x45540704` | `PartyRequest` / `PartyMembers` | S→C | A player asks the receiver to group; the receiver's party when it changed. |
| `0x45540800` .. `0x45540804` | `TradeRequest`, `TradeAnswer`, `TradeOffer`, `TradeLock`, `TradeCancel` | C→S | Ask a player to trade, answer a request, the side's whole offer, lock it, close the trade (see below). |
| `0x45540805` .. `0x45540807` | `TradeAsked`, `TradeState`, `TradeDetails` | S→C | A player asks the receiver to trade; the trade window when it changed, after its partner items' values. |
| `0x45541000` / `0x45541001` | `ContactChange` / `ContactRefresh` | C→S | Put a name on the friends or ignored list or take it off; send the lists again. |
| `0x45541002` | `ContactList` | S→C | One list (friends, then ignored) answering each change or refresh. |
| `0x45541100` | `ClanRequest` | C→S | A clan action (`ClanAction`): open, found, invite, accept, refuse, leave, expel, disband, appoint, discharge, entrust. |
| `0x45541101` | `ClanState` | S→C | The player's clan with its members, or none; on entering the world, on opening the Clan Master and after every change. |
| `0x45541102` | `ClanInvite` | S→C | A recruiter's invitation to a clan. |
| `0x45541103` | `ClanResult` | S→C | How a `ClanRequest` or a `ClanMark` ended (`ClanStatus`); a refusal's line comes in the chat. |
| `0x45541104` | `ClanMark` | C→S | The chief's new clan mark, fitted by the client. |
| `0x45541200` | `CastleState` | S→C | Bless Castle: on entering the world and on every change of its owner, tax in force, league mode or siege ([application.md](application.md#bless-castle)); one packet is made per state and shared by every connection; it carries the tax for the prices the client works out. |
| `0x45541201` | `CastleOwnerRequest` | C→S | The owner's window at the castle's butler: open it, save the setup or withdraw from the treasury. |
| `0x45541202` | `CastleOwnerState` | S→C | The owner's window, after every request (none when the player's clan does not hold the castle). |
| `0x45541203` | `CastleOwnerResult` | S→C | How a `CastleOwnerRequest` ended (`CastleOwnerStatus`, 0 done). |
| `0x45541300` | `BellatraClanRequest` | C→S | Bellatra's secretary: open the window or redeem the clan's income. |
| `0x45541301` | `BellatraClanState` | S→C | The secretary's window: the operating clan, its chief, the tax, the player's clan's income and yesterday's best clans ([application.md](application.md#bellatra)). |
| `0x45541302` | `BellatraClanResult` | S→C | How a `BellatraClanRequest` ended (`BellatraClanStatus`, 0 done). |
| `0x45541303` | `BellatraBoard` | S→C | The player's Bellatra board whenever it changes while the player is in the arena, hidden once it leaves ([application.md](application.md#bellatra)). |
| `0x45541304` | `BellatraResultRequest` | C→S | A Bellatra result window: the player's game's (or every team's) or a team booth's. |
| `0x45541305` | `BellatraResult` | S→C | A Bellatra result window, best score first: the game's final result or a team's live one. |
| `0x45540A00` / `0x45540A01` | `RestartRequest` / `RestartResult` | C→S / S→C | A dead player's restart (field, town, exit), correlated by operation id; a stored restart also sends a forced `FieldWarp`. |
| `0x45540A02` | `RestartOffer` | S→C | While the owner is dead, what a restart would cost, whenever it changes. |
| `0x45540B00` / `0x45540B01` | `GroundItems` / `GroundItemsRemoved` | S→C | Drops entering and leaving the receiver's view: within the ground-item range (squared `0xC8000` units, about 905 units), private drops only to their owner. |
| `0x45540C00` / `0x45540C01` | `ChatLine` / `ChatSay` | S→C / C→S | A chat line for the receiver; a typed line (spoken, command or party). Layouts and routing: [chat-and-gm.md](chat-and-gm.md#wire). |

### Owner, inventory and combat layouts

These bodies have no version header, except the combat event list's (see [Event lists](#event-lists)).

| Message | Layout after the header | Size |
| --- | --- | --- |
| `InventoryCommand` | `Guid OperationId, int64 ExpectedRevision, int32 Action, Guid ItemId, int32 Destination, Count, Slot` | 64 |
| `ProgressionCommand` | `Guid OperationId, int64 ExpectedRevision, int32 Action, Attribute, Amount, uint SkillCode, uint64 NpcId` | 56 |
| `InventoryResult` / `ProgressionResult` | `Guid OperationId, int32 Ok, int64 Revision, game text Message` (none for a plain success) | 40 + texts |
| `Inventory` | `int64 Revision, int32 Part, Parts, Count`, then items `{Guid, uint Code, int32 Kind, Count, Anchor, Slot, int16 Width, Height, text Name (64 B)}` (at most 64 per part) | 28 + up to 106n |
| `Skills` | `int64 Revision, int32 Count`, then `{uint Code, int32 Rank, int32 Uses}` | 20 + 12n |
| `CombatEvents` | `uint16 Version (1), uint16 Count`, then compact entries (see [Event lists](#event-lists)) | 12 + 4 to 65 per entry |
| `RemoveObjects` | `int32 Count`, then `uint64 EntityId` | 12 + 8n |
| `GroundItems` | `uint16 Version (1), uint16 Flags, int32 Count`, then `{Guid Id, uint32 ItemCode, uint8 Flags (1 gold), uint8 0, uint16 0, int32 Amount, X, Y, Z, text Name (64 B)}` (1 to 64) | 16 + up to 106n |
| `GroundItemsRemoved` | `uint16 Version (1), uint16 Flags, int32 Count`, then `Guid Id` | 16 + 16n |
| `ItemDetails` | `uint16 Version (1), uint16 Flags, int64 Revision, int32 Part, Parts, Count`, then 236-byte entries (at most 24) | 32 + 236n |
| `OwnerItemStatus` | `uint16 Version (1), uint16 Flags, int32 CarriedWeight, MaxWeight, CriticalChance, BlockChance, ShootingRange, AttackSpeed, PotionSpace, float LifeRegen, ManaRegen, StaminaRegen, MagicMastery, int16 Resistances[8]` | 72 |
| `AgingProgress` | `uint16 Version (1), uint16 Count`, then `{Guid InstanceId, int32 Count, Goal}` (at most 16) | 12 + 24n |

An item details entry is `{Guid InstanceId, int32 DurabilityCurrent, DurabilityMax, UpgradeLevel, DamageMin, DamageMax,
AttackRating, Defence, AddLife, AddMana, AddStamina, RequiredLevel, RequiredStrength, RequiredSpirit, RequiredTalent,
RequiredAgility, RequiredHealth, Price, Critical, AttackSpeed, ShootingRange, PotionSpace, Weight, UniqueItem, Flags
(1 two-handed, 2 the owner's class may use it), AgingCount, AgingGoal (0 of 0 when the copy is not maturing),
MixSheltom (the mix's highest sheltom, 0 never mixed), MixStats (`MixStat` bits of the stats the mix raised), uint SpecJob, int32 SpecDefence, SpecAttackSpeed, SpecCritical,
SpecShootingRange, SpecLevelLife, SpecLevelMana, SpecLevelAttackRating, SpecLevelDamage, float Absorb, Block,
MoveSpeed, LifeRegen, ManaRegen, StaminaRegen, MagicMastery, SpecAbsorb, SpecMoveSpeed, SpecBlock, SpecMagicMastery,
SpecLifeRegen, SpecManaRegen, SpecStaminaRegen, int16 Resistances[8]}` (`ItemDetailEntry`). The client attaches the
entries to the inventory of the same revision (`OwnedItemView.Details`) and folds the status into the owner state.

A combat entry is `{uint8 Kind, uint8 Flags (1 critical, 2 killed, 4 dealt by a skill or a pet rather than a basic attack), uint8 Fields, entity Attacker}`, then
what `Fields` names in its bit order: 1 entity Target, 2 time DurationMs and HitAtMs, 4 varint Damage, 8 varint Resources,
16 varint SoundCode, 32 point X, Y, Z, 64 varint Angle, 128 varint Clip (absent: Target 0, times 0, values 0, Clip -1). `Kind` is
`CombatEventKind` (Contracts): 1 swing begin, 2 hit, 3 potion begin, 4 potion applied, 5 monster skill impact, 6 heal (amount
in `Damage`), 7 evade, 8 revive (returned life in `Damage`), 9 block (a player blocked a monster's normal attack), 10 miss (a player's swing missed; its
weapon's miss sound plays), 11 level up (a player gained a level; everyone near plays the level-up burst), 12 skill learned
(a player trained a skill rank; the skill-learn light and sound). The field emits 11 and 12 from `WorldPlayer.TakeProgressCues`
once per tick for any change, whatever caused it.
`Clip` is the motion-table slot of the clip a monster's swing plays (-1: the state's first clip); flag 4 marks a
monster's skill swing and the hits of its skill. A monster drinking a potion or changing shape sends the original
motions `Eat` and `Warp` in its presence entries.

### Skills and status effects

Every skill message body but the event lists starts with `{uint16 Version, uint16 Flags}` (version 1, flags 0, unless
stated); readers reject version 0, `SkillCast` rejects versions before 3 (the 32-bit target), and `SkillCast` and
`SkillCastResult` accept newer versions that append fields (the cooldown list checks its length exactly). The event
lists (`SkillEvents`, `StatusEffects`) follow [Event lists](#event-lists). Entity ids are 64-bit; the event lists name
entities by presence handle. Positions are fixed point (×256, `GameUnits`) and angles 4096 per turn.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `SkillCast` (version 3) | `uint Sequence, uint SkillCode, uint64 TargetId, int X, Y, Z, int16 Angle, int16 0, uint ClientTimeMs` | 48 |
| `SkillCastResult` | `uint Sequence, uint SkillCode, byte Status, byte Rank, int16 0, uint CooldownMs, uint RemainingMs, int Mana, int Stamina, uint ServerTick` | 44 |
| `SkillEvents` (version 4) | `uint16 Version, uint16 Count`, then compact skill entries ([Event lists](#event-lists)) | 12 + 9 to 62 per entry |
| `StatusEffects` (version 3) | `uint16 Version, uint16 Count`, then compact status entries ([Event lists](#event-lists)) | 12 + 7 to 49 per entry |
| `SkillCooldowns` | `uint ServerTick, int Count`, then `{uint SkillCode, uint RemainingMs, uint DurationMs}` (at most 256) | 20 + 12n |

`SkillCast` flags carry the command (`SkillCastCommand`): 0 a new cast, 1 the release of the held charge of that skill, 2
and 3 order the caster's summon of that skill to fight only the caster's target or to pick the nearest enemy; flags
above 3 fail to decode. A zero `X, Y, Z` means "no ground point". The server
writes 0 in `SkillCastResult.ServerTick`.

A skill entry is `{uint8 Phase (1 start, 2 impact, 3 cancel, 4 release), uint8 Fields, varuint Flags (1 area, 2
projectile, 4 self, 8 chain link from X, Y, Z to Target, 16 familiar strike, 32 support reaching another player, 64
counter strike, 128 full charge, bits 8-9 the resolution mode drawn for the cast, 1024 an enemy an area struck at
X, Y, Z, for a rule with a `targetEffect`, 2048 the caster's buff defended it from Target, for a rule with a
`defenseEffect`), uint32 SkillCode, uint8 Rank, entity
Caster}`, then what `Fields` names: 1 entity Target, 2 time DurationMs and HitAtMs, 4 point X, Y, Z, 8 varint Angle, 16
varuint TargetCount, 32 varint Radius. A release entry gives the shortened remaining motion in `DurationMs` and the time
to its next hit in `HitAtMs`. A status entry is `{uint8 Op (1 add, 2 refresh, 3 remove, 4 tick), uint8 Fields, uint32
StatusCode, entity Entity}`, then what `Fields` names: 1 entity Source, 2 uint8 Stacks, 4 uint8 SourceKind, 8 uint8
Control, 16 varuint Flags (1 harmful, 2 permanent), 32 varuint DurationMs, 64 varuint RemainingMs, 128 varint Value.
Permanent entries carry zero timers.

### Event lists

`CombatEvents`, `SkillEvents` and `StatusEffects` share one compact form (`EventWire`): `{size, code, uint16 Version,
uint16 Count}` and `Count` entries that fill the message exactly; another version is refused. An entry carries only the
values its event has, named by a `Fields` byte.

- An entity is a varuint: 0 none, 1 followed by the varuint entity id, n >= 2 the receiver's presence handle n - 2 (the
  one its `PresenceEntries` spawn bound). The server names by handle every entity it has shown the receiver and by id
  any other; spawns and removals reach the receiver before the events of the same tick, so the reader resolves a
  handle against the same table the writer used, and an unknown handle is a malformed message.
- A time is a varuint in sixteenths of a millisecond.
- A point is the entry's fixed-point (x256) X, Y, Z held at 1/8 of a unit as three int24, like `PresencePoint`.

A message holds the entries that fit one frame; the next message continues. A hit by a shown attacker on the receiver
takes 6 to 8 bytes. A skill event's `Fields` has no bits above 32; a reader refuses them.

The client sends only the skill, the target and its own position. The server takes rank, costs, range, facing and timing
from its own state and the class skill files (`Shared/PristonTale.Simulation/Skills/Data/Classes/<Class>.json`) and
refuses a cast that is unlearned, of another class, cooling down, short of mana or stamina, without the required weapon
or shield, out of range (rule range plus 24 units, `FieldSkills.RangeSlack`), aimed at a missing or dead target, cast in
a village, or sent while the player is dead, busy or stunned; `AlreadyActive` (14) refuses a buff whose effect, or one
it cannot combine with, is already running on its holder. `Status` is the refusal reason (`SkillCastVerdict`, from
Contracts like the other skill and combat codes). An accepted cast spends mana and stamina, starts the cooldown, puts the caster in the skill
motion and emits a `Start` event whose `DurationMs` and `HitAtMs` are the motion's length and first event at the
server's rates; the hits resolve at the motion's event times (projectiles after their flight at 900 units per second,
`SkillProjectile.UnitsPerSecond`) and emit an `Impact` event with the number of targets hit, then the usual
`CombatEvents`. Statuses are source-agnostic: `SourceKind` is item, player skill, monster skill, consumable or aura,
`Control` the control states (stun, root, silence, slow, hidden); a viewer that starts seeing an entity (it came into
range, joined, or the entity arrived from another field) receives the statuses already running on it as `Add` entries
with the time left. The original client sent damage, buffs and heals itself and the original server checked
none of mana, stamina or cooldown. See [skills.md](skills.md) for the rules behind these messages.

### Usable items

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; text is UTF-8 with a `uint16` byte length.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `ItemUseCommand` | `Guid OperationId, int64 ExpectedRevision, Guid ItemId, int32 Argument, text Text (64 B)` | 58 + text |
| `ItemUseResult` | `Guid OperationId, int32 Ok, int64 Revision, uint16 Status, uint16 0, uint32 ItemCode, game text Message` | 52 + texts |
| `PremiumTimers` | `int64 Revision, int32 Count`, then `{uint8 Kind, uint8 0, uint16 0, int32 Value, uint32 RemainingSeconds, DurationSeconds}` (at most 32) | 24 + 16n |
| `ItemEffects` | `int32 Count`, then `{uint64 Entity, uint32 ItemCode, uint8 Effect, uint8 0, uint16 0, int32 X, Y, Z, Variant, uint64 Target}` (at most 64) | 16 + 40n |

`Status` is `ItemUseStatus`; `Kind` is `PremiumEffectKind`; `Effect` is `ItemEffectKind` (1 firecracker with its roll
in `Variant`, 2 mastery, 3 force orb, 4 learn, 5 pet strike with the pet kind in `ItemCode` and the struck entity in
`Target`, 6 return sparkle at the given position, 7 head change, 8 drink, 9 gift box, 10 a new character's starter gift
with its order in `Variant`, for its owner's beginner's guide, 11 an aged copy of its owner reached its level, since
protocol 26). Player descriptions carry the
Phoenix flag, the helper pet and the head look (1-2 big head, 3 soccer ball). A sealed puzzle piece travels as the PZ100
code with the name "Lucky Bag". The puzzle
collector's exchange is the NPC service action `Exchange` (40) on `QuestEvent`.
Rules: [special-items.md](special-items.md).

### Entity descriptions

Bodies start with `{uint16 Version = 3, uint16 Flags = 0}` (version 2 appended the player's shop sign to
`EntityDescription`, version 3 then its clan: `varuint ClanId` (0 none), `text ClanName` (48 bytes), `varuint ClanMark`;
a reader fills each only from its version); text is UTF-8 with a `uint16` byte length (name 64 bytes, model paths 160).

| Message | Layout after the header |
| --- | --- |
| `DescribeEntity` | `varuint EntityId` |
| `EntityDescription` | `varuint EntityId, uint8 Revision, uint8 Kind (1 player, 2 monster, 3 NPC), varuint Level, varuint MoveSpeed, float32 AttackRate, text Name, text Model`, then the player or template part |
| Player part | `varuint Job, Tier, Face, Hair, text HeadModel, uint32 Weapon, Armor, OffHand (item codes, 0 none), uint8 Flags (1 village, 2 Phoenix), uint8 HelperPet, uint8 BigHead, glow WeaponGlow, glow OffHandGlow, text ShopSign (128 B, empty when the player sells nothing)` |
| Glow (`DescribedGlow`, 16 bytes) | `int8 Blink (0 none, below 0 steady), int16 R, G, B, A, float32 Scale, int8 Mix (-1 none), uint8 MixScroll, uint8 Flags (1 class-up light)` (`HeldItemGlow`) |
| Template part | `varuint Template, float32 TemplateScale, varuint SoundCode, varint PortraitY, PortraitZ, uint8 Brood, varuint Owner, float32 InstanceScale, uint32 NpcServices, varuint Class, varint ShadowSize` (the monster class: 1 boss, 200 hammer; the ground shadow -1 none, 0 small to 3 large) |

An owned summon or crystal pet names its owner (64-bit) and its own scale (0 keeps the template's) and carries the user
brood; NPC services are bit `1 << NpcServiceKind`. A description is encoded once per frame and entity for every
receiver (`SharedPresenceWire`).

### Presence clock and remote playback

`PresenceClock` is 20 bytes: `{int32 size, uint32 code, uint16 Version = 1, uint16 Flags = 0, uint32 ServerTick, uint32
TickIntervalMs = 50}`. Every sample and event until the next clock belongs to `ServerTick × TickIntervalMs` on the server
timeline. A `PresenceEntries` message carries the clock of its tick in its header, so the server sends `PresenceClock`
only on a tick whose `CombatEvents`, `SkillEvents` or `StatusEffects` come without entries before them (and for the
earlier ticks of a reader that fell behind). Readers reject version 0 and a zero interval.

### Presence entries

`PresenceEntries` is `{int32 size, uint32 code, uint16 Version = 2, uint16 TickIntervalMs, uint32 Tick, uint16 Count,
uint16 0}` followed by `Count` entries that fill the message exactly; another version is refused. An entry is
`{varuint Handle, uint8 Head}`: bits 0 to 3 of `Head` are the `MotionState` (`Shared/PristonTale.Contracts`; 0 stand,
1 walk, 2 run, 3 attack, 4 damage, 5 dead, 6 skill, 7 eat, 8 warp, 9 yahoo; a larger value or bit 7 is refused), bit 4
Spawn, bit 5 Absolute, bit 6 Fields. Then, in order: `varuint EntityId` when Spawn; the position, `int24 X, Y, Z`
(world units × 8) when Absolute, otherwise `zigzag varint dX, dY, dZ`, the change from the last position this receiver
was sent for the handle; `uint8 Facing` (256 per turn); and when Fields, `uint8 PresenceFields` then each field it
names in its bit order: `varuint Life` (bit 0), `MaxLife` (1), `Target` (2), `AttackSequence` (3),
`uint8 Revision` (4), `varuint Template` (5), `zigzag varint FieldCode` (6); bit 7 is refused. A spawn must be
Absolute and carry every field. Changes are 32-bit. `varuint` is little-endian base-128 (`VarInt`, up to 64 bits; an
overlong encoding or a tenth byte above 1 is refused). Positions cover the world's ±1,000,000 units at 1/8 of a unit
(held to ±2^23 eighths).

A handle is the receiver's own number for an entity: a spawn binds it to the 64-bit entity id, and once the entity's
`RemoveObjects` went out the server may give it to another entity (a field change frees them all). The server spawns an
entity when it enters the receiver's view, with Absolute and every field. Afterwards an entry carries the fields that
changed since the last one sent; one with fields or a changed motion goes out at once, a position-only one (about 8
bytes walking) at the near or far rate. The position is sent whole when that is not larger than the change.
`Template` is the shared template key (`PresenceWire.TemplateKey`: a monster's code, or an NPC's code with bit 31 set,
since the two are numbered apart) of monsters and NPCs that are not owned, 0 otherwise; `FieldCode` is the receiver's
field. The level belongs to the description: a level change changes the revision.

The server pushes an entity's `EntityDescription` ahead of the entry that spawns it or changes its revision, except a
shared template's description of the revision it last sent that receiver (the client keeps the last description of each
template). `DescribeEntity` remains for what the client still lacks: a template of another revision, or a description
that did not come.

The client plays remote objects back on that timeline
(`Client/PristonTale.Gameplay/World/Remote/RemoteSnapshotBuffer.cs`): it keeps the timestamped samples of each object
and interpolates position, the server's height and facing (shortest arc) linearly at a render time 150 ms behind the
server, longer (up to 600 ms) when the object's samples arrive at the 2 Hz far-ring cadence. Motion states and combat
events of the object apply when the render time reaches them. A walking or running object may be extrapolated for 250 ms
when samples are late, and a jump above 256 units snaps. An object standing still gets no entries, so after a silence
longer than 750 ms the object waits at its last place until one server tick before the next sample. Walk and run clips
play at a rate proportional to the interpolated ground speed, and a walk or run state without ground motion is shown
as standing.

### NPC services

Every NPC and quest message body starts with `{uint16 Version = 3, uint16 Flags = 0}` (version 2 widened the NPC id
to 64 bits, version 3 added the service request's `Covered`; older versions are refused); readers accept newer versions that append fields, except `ShopQuotes`, whose
list length is checked exactly. Text is UTF-8 with a `uint16` byte length (names up
to 64 bytes, item names 40, messages 128, dialog lines 256), and counts are 32-bit. The click itself is an
`AttackRequest` on the NPC id; the server answers with `NpcResult`.

- `NpcResult`: `{uint64 NpcId, int32 Ok, text Name, game text Message, int32 ServiceCount, services {uint8 Kind, uint8
  Available, uint16 PricePercent, int32 Value}, int32 LineCount, text lines}` (at most 32 services and 20 lines). `Kind` is
  `NpcServiceKind`, `Value` the authored argument (class-change group, teleport route, mix mode, quest code...). One
  dialog line is sent, chosen at random as the original does.
- `NpcServiceRequest` (108 bytes, more with materials): `{Guid OperationId, uint64 NpcId, uint8 Service, uint8
  Action, uint16 0, int64 ExpectedRevision, int64 ExpectedStorageRevision, uint32 ItemCode, Guid ItemId, int32 Anchor,
  int32 Count, int64 Amount, Guid Covered}` (Covered: the one item a warehouse item set down lies over), then for a work on an item `{uint8 Count (1..16), Guid Materials[Count]}` (one unit of an
  owned item per entry). The client names a stock entry, an owned item, a grid cell (-1 = first free), a destination
  index or the materials; it never sends an item, a price or a total. The original buy and warehouse layouts, which carry a whole client-built item
  or warehouse, are not used.
- `NpcServiceResult`: `{Guid OperationId, uint64 NpcId, uint8 Service, uint8 Action, uint16 Status, int32 Ok, int64
  Revision, int64 StorageRevision, game text Message}`. `Status` is `NpcServiceStatus` (`NotAvailable` for services Etervale
  does not run, `OutOfReach`, `Rejected`, `Stale`, `NotEnoughGold`, `NoSpace`, `GoldLimit`, `Busy`, `StorageFailed`,
  `Conflict`, `NotOpen`, `OverWeight`, `WrongPuzzle`, `WrongMaterials`, `BellatraClosed`, `BellatraLevel`,
  `BellatraNoTickets`, `BellatraTeamFull`, `BellatraEntered`). The state messages a request produces
  (`ShopCatalog`, `ShopQuotes`, `Warehouse`, `TeleportList`, `ItemWork`) carry the same operation id and precede the
  result.
- `ItemWork`: `{uint16 Version, uint16 0, Guid OperationId, uint64 NpcId, uint8 Service, uint8 Result, uint16 0,
  Guid ItemId, uint32 Code}`. For the aging NPC `Result` is `AgingResult` (0 lost, 1 down one, 2 down two, 3 success,
  4 doubled) and `ItemId` is empty when the item was lost; for the mixing NPC it is `MixResult` (0 failed, 1 mixed,
  2 reconstructed, 3 refined, `ItemId` empty after refining); for the smelter `SmeltResult` (1 smelted), the craftsman
  `ManufactureResult` (0 failed, `ItemId` empty, 1 made) and the mixture reset 1. `PricePercent` of a dialog service is the server's price
  setting of the aging, mixing and alchemy services (100 for the others), without Bless Castle's tax, which the client
  adds to the price it works out (`CastleState`). A teleport also sends a forced `FieldWarp`.

The server checks every request against the NPC in the player's field (alive, within 96 units, offering the service),
writes durable changes before applying them and records the operation id with the character, so a retried request
returns its first outcome without charging again.

### Personal shops

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; text is UTF-8 with a `uint16` byte length (the sign
128 bytes, names 64, item names 40, messages 128); a shop holds at most 30 items (`PersonalShopLimits`, Contracts).

| Message | Layout after the header |
| --- | --- |
| `PersonalShopOpen` | `Guid OperationId, text Sign, int32 Count, entries {Guid ItemId, int64 UnitPrice, int32 Cell}` (a store grid cell) |
| `PersonalShopClose` | `Guid OperationId` |
| `PersonalShopView` | `Guid OperationId, uint64 SellerId` |
| `PersonalShopBuy` | `Guid OperationId, uint64 SellerId, Guid ItemId, int64 UnitPrice, int32 Count, int32 0, int64 ExpectedRevision` (the buyer's inventory) |
| `PersonalShopResult` | `Guid OperationId, uint8 Action (0 open, 1 close, 2 view, 3 buy), uint8 0, uint16 Status, int32 Count, int64 Revision, int64 UnitPrice, game text Message` |
| `PersonalShopListing` | `Guid OperationId, uint64 SellerId, int32 Count, offers {StoreItemEntry, int64 UnitPrice}`; a store item entry is `{Guid, uint32 Code, uint8 Kind, uint8 0, uint16 0, int32 Count, int32 Cell, int16 Width, int16 Height, text Name}` (the warehouse's too) |
| `PersonalShopDetails` | `Guid OperationId, uint64 SellerId, int32 Count, ItemDetailEntry entries` |

`Status` is `PersonalShopStatus` (Simulation): 0 ok, `Rejected`, `NotInTown`, `NotListable`, `GoldLimit`, `Closed`,
`SoldOut`, `CountChanged` and `PriceChanged` (then `Count` and `UnitPrice` name what the shop offers now),
`NotEnoughGold`, `NoSpace`, `OverWeight`, `Busy`, `StorageFailed`, `Stale`, `OutOfReach`, `Conflict`, `Selling` (the
requester's own shop holds it). The client never sends a total or an item: the server prices the purchase from the
listing and moves the copies it holds. A player's sign reaches the others in its `EntityDescription`.

### Parties

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; names are UTF-8 with a `uint16` byte length (64
bytes). Requests are not answered: `PartyMembers` and chat lines show what changed.

| Message | Layout after the header |
| --- | --- |
| `PartyInvite` | `uint64 TargetId` |
| `PartyAnswer` | `uint64 FromId` (the request's), `uint8 Accept` (0 or 1; anything else is refused) |
| `PartyCommand` | `uint8 Command` (`PartyCommand`: 0 delegate, 1 disband, 2 kick, 3 leave), `uint64 MemberId` (0 for disband and leave) |
| `PartyRequest` | `uint64 FromId, text FromName, text Recommended` (the player a member recommends to its leader; empty for a request to join) |
| `PartyMembers` | `uint8 Count` (0..6; 0 once the receiver has no party), members `{uint64 EntityId, text Name, int32 Level, int32 Life, int32 MaxLife, float X, float Z}`, the leader first |

### Trades

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; names are UTF-8 with a `uint16` byte length (64
bytes); a side offers at most 20 items (`TradeLimits`, Contracts). Requests are not answered: `TradeState` and chat lines
show what changed.

| Message | Layout after the header |
| --- | --- |
| `TradeRequest` | `uint64 TargetId` |
| `TradeAnswer` | `uint64 FromId` (the request's), `uint8 Accept` (0 or 1; anything else is refused) |
| `TradeOffer` | `int64 Money, int32 Count, entries {Guid ItemId, int32 Cell}` (a cell of the 9x4 trade grid) |
| `TradeLock`, `TradeCancel` | nothing |
| `TradeAsked` | `uint64 FromId, text FromName` |
| `TradeState` | `uint64 PartnerId` (0 once the trade ended), `text PartnerName, uint8 Flags (1 own offer locked, 2 the partner's), int64 OwnMoney, int64 PartnerMoney, int32 Count, own entries, int32 Count, the partner's StoreItemEntry items` (anchored at their trade grid cells) |
| `TradeDetails` | `int32 Count, ItemDetailEntry entries`: the partner items' values for the receiver, sent just before `TradeState` |

### Contacts

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; names are UTF-8 with a `uint16` byte length (64 bytes).
Both requests are answered by a `ContactList` for each list (one per message, as both would pass the frame limit); a
refused change also by a chat line.

| Message | Layout after the header |
| --- | --- |
| `ContactChange` | `uint8 List` (`ContactList`: 0 friends, 1 ignored), `uint8 Add` (0 or 1), `text Name` |
| `ContactRefresh` | nothing |
| `ContactList` | `uint8 List, uint8 Count` (at most 100), entries `{text Name, uint8 Online}` in the order added (`Online` 0 on the ignored list) |

### Clans

Every body starts with `{uint16 Version = 1, uint16 Flags = 0}`; names are UTF-8 with a `uint16` byte length: a clan's name
at most 48 bytes (`ClanWire.MaxClanNameBytes`: 16 characters), a character's 64.

| Message | Layout after the header |
| --- | --- |
| `ClanRequest` | `uint8 Action` (`ClanAction`: 0 open, 1 found, 2 invite, 3 accept, 4 refuse, 5 leave, 6 expel, 7 disband, 8 appoint, 9 discharge, 10 entrust; 11 mark names a `ClanMark`'s result), `varuint Id` (the Clan Master for found and entrust, the player for invite), `text Name` (the new clan, or the member) |
| `ClanState` | `uint8 HasClan`; with a clan `varuint ClanId, text Name, uint8 State` (0 registering, 1 registered), `int64 Deadline` (Unix seconds, 0 none), `uint8 Rank` (0 member, 1 vice, 2 chief), `uint8 Capacity, text Intro` (256 bytes), `varuint MarkVersion, uint16 Count` (at most 80), members `{text Name, uint8 Class, varuint Level, uint8 Rank, uint8 Online}` (chief, vice, then by name) |
| `ClanInvite` | `varuint RecruiterId, text ClanName, text ChiefName` |
| `ClanResult` | `uint8 Action, uint8 Status` (`ClanStatus`, 0 done) |
| `ClanMark` | `uint16 Size` (32, `ClanWire.MarkSize`), then Size x Size pixels of `uint8` red, green, blue and alpha (straight), rows from the top: 4,096 bytes, one frame |
| `CastleState` | `varuint OwnerClanId` (0 none), `text OwnerName` (48 bytes), `uint8 Tax` (in force on fees), `uint8 Siege` (0 peace, 1 running, 3 over), `uint8 Flags` (1 the server runs the league, 2 door down), `varuint SecondsLeft` (a running siege's, when the state was made; 0 otherwise), `uint8 Crystals`, `uint8 Count` (at most 10, `CastleLimits.TopClans`), then per clan `varuint ClanId, text Name, varuint Damage`, then `varuint TotalDamage` |
| `CastleOwnerRequest` | `uint8 Action` (0 open, 1 save, 2 withdraw), `varuint Npc` (the butler), `uint8 Tax, uint8 Skill`, `uint8 x6 Towers` (0 none, 1 ice, 2 lightning, 3 fire), `uint8 x3 Mercenaries`, `varuint Amount` (a withdrawal's gold) |
| `CastleOwnerState` | `uint8 HasView`; with one `varuint Treasury, uint8 Tax` (in force), `uint8 NextTax, uint8 Skill` (chosen), `uint8 SkillInForce`, `uint8 x6 Towers, uint8 x3 Mercenaries, uint8 CanEdit` (the chief) |
| `BellatraClanRequest` | `uint8 Action` (0 open, 1 redeem), `varuint Npc` (the secretary), `varuint Amount` (a redemption's gold) |
| `BellatraClanState` | `uint8 HasView`; with one `varuint OperatingClanId` (0 none), `text OperatingName` (48 bytes), `text Chief` (64 bytes), `uint8 Tax`, `varuint Balance` (the player's clan's income), `uint8 CanRedeem` (its chief), `uint8 Count` (at most 10, `BellatraLimits.TopClans`), then per clan `text Name, varuint Points` (yesterday's) |
| `BellatraResultRequest` | `uint8 Team` (0 the player's game or every team, 1 to 4 a team booth's) |
| `BellatraResult` | `uint8 Final` (0 live), `uint8 Count` (at most `BellatraLimits.ResultLines`, 10), `{text Name (64 bytes), uint8 Class (`PlayerClass`), varuint Score, varuint Kills} x Count` |
| `BellatraBoard` | `uint8 Shown`; with it `uint8 Round` (0 between rounds), `uint8 Team` (0 to 3), `uint8 Outcome` (0 playing, 1 the team goes on, 2 out, 3 the game is over), `uint8 StageTeams`, `uint8 GoingOn` (at the last round start that sent teams on, the teams that played it and those going on, a bit a team), `varuint Score`, `varuint x4 TeamScores` (`BellatraLimits.Teams`) |
| `BellatraClanResult` | `uint8 Action, uint8 Status` (`BellatraClanStatus`: 0 done, 1 not the chief, 2 not enough income, 3 out of reach, 4 too much gold, 5 busy, 6 unavailable) |
| `CastleOwnerResult` | `uint8 Action, uint8 Status` (`CastleOwnerStatus`: 0 done, 1 not the owner clan, 2 not the chief, 3 out of reach, 4 busy, 5 not enough gold, 6 invalid setup, 7 during a siege, 8 not enough in the treasury, 9 too much gold, 10 unavailable) |

### Death and restart

Bodies start with `{uint16 Version = 1, uint16 Flags = 0}`.

| Message | Layout after the header | Size |
| --- | --- | --- |
| `RestartRequest` | `Guid OperationId, uint8 Choice (1 field, 2 town, 3 exit), uint8 0, uint16 0` | 32 |
| `RestartResult` | `Guid OperationId, uint8 Status (RestartStatus), uint8 Choice, uint8 ScrollUsed, uint8 0, uint64 ExperienceLost, int64 GoldLost` | 48 |
| `RestartOffer` | `uint8 Flags (1 field restart open, 2 a Resurrection Scroll waives the shares), uint8 0, uint16 0, uint64 ExperienceShare, int64 GoldShare` | 32 |

`RestartStatus`: 0 restarted, 1 not dead, 2 field refused, 3 busy, 4 storage failed, 5 rejected. Rules:
[application.md](application.md#players-worldplayer).

### Quests

Quest bodies start with the NPC header `{uint16 Version = 3, uint16 Flags = 0}` (`NpcWire`); text is UTF-8 with a `uint16` byte length (64 bytes).
A dialog is `{uint16 Code, uint8 Followup, uint8 0, int32 State, int32 Choice, text Document, game text Title,
ConfirmLine1, ConfirmLine2, ConfirmDetail, Subject (what the document's `$$%s` names), int32 ChoiceCount, (text Choice, text ChoiceDocument)...}` (at most 16
choices); a notice list is `{int32 Count, (uint8 Kind, uint8 0, game text)...}` (at most 32). Codes and enums are
`QuestCode`, `QuestAction`, `QuestStatus`, `QuestFollowup`, `QuestNoticeKind`, `QuestTimerKind` (see
[quests.md](quests.md)).

- `QuestRequest` (48 bytes): `{Guid OperationId, uint64 NpcId, uint8 Action, uint8 0, uint16 Code, int32 State,
  int32 Choice}`. `NpcId` is 0 only for the redistribution offer and the victory dialog of a duel.
- `QuestResult`: `{Guid OperationId, uint8 Status, uint8 Flags (1 ok, 2 class change offered, 4 redistribution
  offered, 8 dialog follows), uint16 0, int64 Revision, game text Message, [dialog], notices}`.
- `QuestJournal`: `{int64 Revision, uint16 Code, uint8 Timer, uint8 RedistributionOffered, int32 State, text
  TargetName, int32 Kills, KillsNeeded, int64 TimerMs, int32 WeaponStage, WeaponKillsLeft, int32 ProgressCount,
  (text Title, text Description)..., int32 FinishedCount, lines}` (at most 16 running and 32 finished lines; a
  description names a file under the client's quest board folder).
- `QuestNotices`: `{uint8 Flags (1 dialog follows), uint8 0, uint16 0, notices, [dialog]}`; more than 32 notices
  are sent as several messages, the dialog with the last.

The server answers a request only after a changed character is stored; the presence stream carries the journal and
notices of the tick (kill counts, timers, duel results) to the owner alone. A step that sends the player to or from the
quest arena arrives as the usual field confirmation `FieldWarp`.

## Server capacity

The original server ran all game logic on one thread under one lock, scanned every connection and monster slot for
visibility, and held 600 suspended worker threads with a shared 1024-slot send queue. Its default cap was 800 players
per process. Etervale keeps the original view ranges and distance-weighted detail and changes the mechanics. Who
receives what, in which order and how often is specified in [server.md](server.md#presence-propagation); the design:

- Fields step in parallel on the tick. They share only immutable catalogs and the thread-safe id allocator.
- Who sees whom is prepared once per field and tick, not per pair: a `PresenceFrame` holds the views of every entity
  with its field-wide descriptor revision, an `EntityGrid` (64-unit planar cells, sorted keys, one binary search per
  column), the index of who targets or owns whom, and the NPC list. Frames are reused tick after tick (a receiver
  pins the frame it encodes from on striped counters), so the tick allocates no views, grids or encode buffers. The
  frame also routes the tick's events by the entities they name (`EventRoutes`), so a receiver collects the events of
  the entities it sees instead of scanning every event of its field.
- Each receiver's `PresenceEncoder` (on its connection's pump, never on the tick) queries the grid, keeps at most 256
  other entities in view, at most 100 of them players (the nearest players, then the nearest others, plus its NPCs, its
  attack target, whoever targets it, its summons and its party; an entity already shown stays out to 1.15 times the
  cut-off), and sends an object only when it changed: spawns and discrete
  changes (motion, HP, max HP, target, attack, template, description) immediately, positions at 5 Hz inside the high-detail range
  (squared `0x48000`, about 543 units) and 2 Hz out to the connect range (squared `0x120000`, about 1086 units); an
  object is forgotten beyond 1.15 times the connect range. Position updates share a budget of 2048 bytes per receiver
  per tick, ranked by how overdue they are (a bounded heap keeps only what the budget can carry), which plays the role
  of the original per-receiver user limit and round-robin cursor.
- Entity descriptions are encoded once per frame and entity (`SharedPresenceWire`) and copied by every receiver that
  needs them; each takes a buffer from the shared array pool, given back when the field reuses the frame, so a frame
  costs only the entities its tick describes. Presence entries are each receiver's own (handles, changes from what it
  was sent).
- Movement reports travel through the command queue as values (no closure or task per report); the verdict comes back
  through `IMovementVerdictSink`.
- Sockets use .NET asynchronous I/O (IOCP on Windows) with pooled buffers and coalesced writes; the writer and the
  presence pump wait without allocating.
- Every field's stage collision (`Shared/PristonTale.Assets` reader, `Geometry` mesh) is loaded at startup, as the original server does: 42
  stages, about 1.93 million collision triangles and 179 MiB, in under a second. Monsters step like the original
  server's next-move check (slide along walls with two side probes, climb 20 units, collide only with other monsters);
  NPCs stop at walls and steps above 10 units; spawns, respawns and drops settle on the floor. The client builds its
  collision from the same `CollisionMesh`.
- SQLite runs in WAL mode with `synchronous=NORMAL`. The rollback journal limited a login burst to a few hundred writes
  per second.

### Measured capacity

All numbers come from one Windows machine (16 logical CPUs, .NET 10, Release, server GC). The tick budget is 50 ms.

The synthetic pipeline (`--benchmark-presence`, [testing.md](testing.md#benchmarks)) runs the real `WorldServer` tick
and one real `PresenceEncoder` per player without sockets: players walk with 5 Hz reports, a quarter fight a monster
beside them, a tenth cast an area skill every three seconds, monsters fight back. Crowded packs everyone into a
500-unit square; spread places players 300 units apart. Encoding runs in parallel after each tick, as the pumps do.

| Players | Layout | Tick p50 / p99 | Encode p50 / p99 (all receivers) | Out per client | Allocated per tick |
| --- | --- | --- | --- | --- | --- |
| 500 | spread | 2.18 / 3.94 ms | 1.82 / 6.41 ms | 2.7 KiB/s (140 B/tick) | 221 KiB |
| 1000 | spread | 3.31 / 28.63 ms | 1.21 / 2.44 ms | 2.8 KiB/s (144 B/tick) | 408 KiB |
| 2000 | spread | 6.67 / 16.76 ms | 3.43 / 6.18 ms | 2.9 KiB/s (149 B/tick) | 657 KiB |
| 5000 | spread | 15.42 / 28.32 ms | 6.63 / 10.59 ms | 3.0 KiB/s (154 B/tick) | 1640 KiB |
| 500 | crowded | 2.92 / 15.20 ms | 5.59 / 15.90 ms | 17.2 KiB/s (882 B/tick) | 194 KiB |
| 1000 | crowded | 5.05 / 15.56 ms | 8.96 / 16.85 ms | 22.2 KiB/s (1.1 KiB/tick) | 404 KiB |
| 2000 | crowded | 9.82 / 36.81 ms | 18.85 / 43.24 ms | 25.2 KiB/s (1.3 KiB/tick) | 880 KiB |
| 5000 | crowded | 13.36 / 70.00 ms | 31.37 / 55.90 ms | 27.4 KiB/s (1.4 KiB/tick) | 2459 KiB |

The rows include the descriptions the server pushes ahead of every spawn (the benchmark does not ask for them) and the
cap of 100 players a receiver shows. Run-to-run noise on this machine moves single percentiles by up to a third, so
compare runs taken back to back.

The end-to-end load test (`tools/PristonTale.LoadTest --mode mixed`: half the bots walk, three in ten hunt the nearest
monster with basic attacks, two in ten cast Mechanic Bomb) runs against the imported world on Forest 1 with full stage
collision, the server and every bot on the same machine; crowded bots start inside a 500-unit square, spread bots on an
80-unit grid, and each run is measured 60 s after the ramp.

Stored commands (`--mode items`, 2000 bots each switching the bag page once a second, PostgreSQL in Docker on the
same machine, `Database:MaxPoolSize` 20): the server answers about 1740 commands/s at p50 19 ms, p99 52 to 65 ms and
at most 95 ms (the prepare and the commit take a tick each), with no refusal and no lost leave save. The background
character store ([application.md](application.md#character-saves)) writes about 1450 characters/s in batches of 100
(65 ms each), its queue bounded by the characters playing, and the tick stays at p50 11 ms.

At 5000 bots the load-test process itself
uses most of the remaining cores, so the tick shares the machine; the encoders then fall behind the 20 Hz tick, their
outboxes coalesce, and each client receives fewer, larger updates (the lower bandwidth per connection) while every
event still arrives in order. The many position corrections in these runs come from the bots walking straight through
the stage's walls, not from the server. Reproduce a run with:

```bash
dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- --sqlite <copy.db> \
  create-bots cw 5000 bot-pw --class Mechanician --level 20 --field 2 --at 4184,-11016 --spacing 0 \
  --skills 0x01010002:5,0x01010003:3
dotnet run --project Server/PristonTale.Server -c Release --no-build -- --Database:SqlitePath=<copy.db> \
  --Network:Port=10037 --Network:MaxConnectionsPerIp=0 --Network:MaxHandshakesPerIp=0 --Server:ReportSeconds=10
dotnet run --project tools/PristonTale.LoadTest -c Release --no-build -- --count 2000 \
  --address tcp://127.0.0.1:10037 --prefix cw --mode mixed --seconds 60 --ramp 100 --server-pid <pid> --report 10
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- \
  --benchmark-presence 500,1000,2000,5000 spread,crowded 100
```

The server logs tick percentiles, process CPU, allocation rate, collections, heap and working set, and bandwidth per
connection every `Server:ReportSeconds` (30 by default).

## How to add an opcode or packet

1. **Code.** Add a constant to `EtervaleOpcodes` in `Binary/EtervaleOpcodes.cs`, in the range of its area
   (`0x455401xx` owner state, `0x4554011x` inventory, `0x4554012x` progression, `0x4554013x` NPC, `0x4554014x` combat,
   `0x4554015x` removals, `0x4554016x` leave, `0x4554017x` clock, `0x455402xx` skills, `0x455403xx` item details,
   `0x455404xx` usable items, `0x455405xx` quests, `0x455406xx` personal shops, `0x455407xx` parties, `0x455408xx`
   trades, `0x455409xx` presence, `0x45540Axx` restart, `0x45540Bxx` ground items, `0x45540Cxx` chat, `0x45540Dxx` player
   actions, `0x45540Exx` world entry, `0x45540Fxx` account, `0x455410xx` contacts, `0x455411xx` clans, `0x455412xx` Bless
   Castle, `0x455413xx` Bellatra).
2. **Packet type.** Add a record in `Packets/` with `Size` or `HeaderSize` constants, `int Write(Span<byte>)` built on
   `PacketWriter` (returning `Finish()`), and `static Read(ReadOnlySpan<byte>)` built on `PacketReader(packet,
   minimum)`. Start new bodies with `{uint16 Version, uint16 Flags}`, reject version 0, append fields only at the end,
   and bound every count by a `Max*` constant that keeps the packet within 8192 bytes; split longer lists into parts or
   batches.
3. **Server, C→S.** Add a `case` in `ClientSession.HandleAsync`, map the packet to an application input, catch the
   exceptions the call can throw and send a failed result if the client waits for one. Reply with
   `SendLarge(packet.Write, capacity)`.
4. **Server, S→C presence.** Expose the state on `WorldSnapshot`/`OwnerState` in `Shared/PristonTale.Application`, map
   it in `WireMapping` (or `NpcWireMapping`), emit it from `PresenceEncoder.EncodeLocked`, and call `SendClock` first if
   it belongs to the timeline.
5. **Client.** Presence messages: add a case in `PresenceDecoder.TryHandle` and a `PresenceUpdate` member in
   `Client/PristonTale.Client.Net/Presence`. Request replies: add a case in `GameServerConnection.HandleAsync` and
   complete the pending request by operation id or entity id. Requests: add a send method in `GameServerConnection` that
   writes under `_sendGate`.
6. **Tests.** Add a layout round trip next to its siblings (`BinaryProtocolTests`, `SkillWireTests`,
   `NpcServiceTests.ProtocolAsync`) and, for presence, a `WireRoundTrip` case; new test classes are registered in
   `tests/PristonTale.Tests/Program.cs` through `Cases()`. If login or entry changes, run
   `tools/PristonTale.SmokeClient` against a running server; `tools/PristonTale.LoadTest` shares `GameServerConnection`.
7. **Docs.** Update the tables in this file and, for new settings or handlers, [server.md](server.md).

To add a field to an existing versioned message, append it, raise `CurrentVersion`, and make `Read` fill the field only
when the received version has it.

## Pitfalls

- Text on the wire is UTF-8, but the HUD font draws only its own glyph ranges; the chat input refuses characters the
  font cannot draw (`HudPainter.CanDraw`).
- `PacketReader` accepts trailing bytes, so an old reader keeps working when a fixed-size body grows. List bodies
  (`SkillCooldowns`, `Inventory`, ...) check `Remaining == count * EntrySize` exactly, and the event lists that their
  entries end the message, so appending anything to them breaks every deployed reader; make readers version-aware first or
  add a new opcode.
- A malformed packet that makes a `Read` throw closes the whole connection (`protocol: ...`).
- Combat, skill and status codes (`CombatEventKind`, `SkillCastVerdict`, `SkillCastCommand`, `SkillEventPhase`,
  `SkillEventFlags`, `StatusEffectOp`) are declared once in `Shared/PristonTale.Contracts` and used on both sides of
  the wire as they are; append values, never renumber.
- `DescribeEntity` is outside the packet flood count but has its own token bucket; a client that requests every entity id
  each frame will see descriptions silently dropped. A client must ask again for an object still undescribed (the
  Etervale client does after a second), or a crowd entering view at once never appears.
- Two monsters of one template share a description only while both are wild: a summon's description names its owner.
  Never send a summon's template in its presence entry, and never cache a user-brood description by template.

## Related

- [server.md](server.md): connection lifecycle, packet-to-application mapping, limits and settings.
- [client.md](client.md): client networking, presence consumption and remote world.
- [skills.md](skills.md): skill rules behind `SkillCast`, `SkillEvents` and `StatusEffects`.
- [application.md](application.md): `WorldServer`, snapshots and transactions that produce the outbound messages.
- [world.md](world.md): collision, fields, gates and warps used by movement authority.
- [testing.md](testing.md): running the regression suite.
