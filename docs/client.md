# Client

The game client: the executable (`Client/PristonTale`), the screen layer, game scene, HUD, remote world and audio
(`Client/PristonTale.Gameplay`) and the client side of the protocol (`Client/PristonTale.Client.Net`). Shaders,
rendering internals, skill presentation and VFX data have their own documents (see Related).

## Read this when

- You add or change a screen, a HUD window, a key binding or a mouse interaction.
- You handle a new server message on the client, or change how presence, combat or owner state reaches the HUD.
- You change local movement prediction, movement reports, warps or server corrections.
- You touch remote actor playback (interpolation delay, extrapolation, spawn deferral) or the target frame.
- You add asset loading to the client and need to keep it off the frame.
- You debug login, character select, world entry or the shutdown path.

## Code map

| Path | Responsibility |
|---|---|
| [`Client/PristonTale/Program.cs`](../Client/PristonTale/Program.cs) | Entry: no arguments, `--settings PATH`, or `--validate-display REPORT.json` (runs `DisplayValidationProbe`) |
| [`Client/PristonTale/Game1.cs`](../Client/PristonTale/Game1.cs) | MonoGame `Game`: device setup, variable timestep, asset root discovery, server address, Escape/exit, window-title stats, shutdown gating |
| `Client/PristonTale/Display/` | `PresentationController` (settings at the frame boundary, display confirmation, pacing, save), `DisplayController` (window mode, size, VSync; primary monitor only), `DisplayValidationProbe` |
| `Client/PristonTale/Content/Content.mgcb` | Content build: the shader library (see [shaders.md](shaders.md)) and `Fonts/UI.spritefont` |
| `Client/PristonTale/PristonTale.csproj`, `Client/MonoGameRuntime.targets` | References the MonoGame Vulkan fork in `monogame/`; copies the native runtime next to the executable |
| `Client/PristonTale.Gameplay/Screens/Framework/` | `GameClient` (owns bootstrap libraries, backend and the current screen), `GameScreen` (base), `ScreenContext` (shared services) |
| `Client/PristonTale.Gameplay/Screens/Opening/` | Opening cinematic, login form overlay and login polling |
| `Client/PristonTale.Gameplay/Screens/CharacterSelect/` | Character list, creation flow (race, job, face, name), delete, enter world; its own renderer, scene and camera |
| `Client/PristonTale.Gameplay/Screens/Gameplay/GameplayScreen.cs` | Wires `GameScene` and `GameplayHud`, debug keys, render-stats status text |
| [`Client/PristonTale.Gameplay/World/GameScene.cs`](../Client/PristonTale.Gameplay/World/GameScene.cs) | The in-world scene: state, construction, the frame update and draw, movement reports. One partial per topic: `.Presence` (play channel, drain, remap), `.Fields` (loading, streaming, active field, lighting), `.Warps` (gates, warps, server placements), `.Self` (the server's state and events for the local player, its clips), `.Input` (movement, loot and target picking, NPC approach), `.Ground` (floor, falls, collision, world matrix), plus `.Skills`, `.Items`, `.Audio`, `.Chat` and the others below |
| `Client/PristonTale.Gameplay/World/GameScene.Skills.cs` | Skill casting from the mouse sockets and the skill timeline glue ([skills.md](skills.md)) |
| `Client/PristonTale.Gameplay/World/Streaming/` | `FieldResourceStreamer` (off-thread field preparation, budgeted GPU upload), `FieldStreamingPolicy` (prefetch and retention distances, budgets) |
| `Client/PristonTale.Gameplay/World/Remote/` | Remote actors: `RemoteEntityManager`, `RemoteSnapshotBuffer`, `RemoteServerClock`, actor composers, equipment visuals, ground loot, picking |
| `Client/PristonTale.Gameplay/World/*.cs` (other) | `TargetSelection` (target frame owner), `NpcApproach`, `GroundTraversal`, `FieldCollisionQuery`, `LoadedField`, `EnteredCharacter` |
| `Client/PristonTale.Gameplay/Combat/` | `ActorMotionController` (local motion state machine), `AttackInputController` (attack press/release and server reconcile) |
| [`Client/PristonTale.Gameplay/UI/GameplayHud.cs`](../Client/PristonTale.Gameplay/UI/GameplayHud.cs) | In-game HUD root: windows, state, update and draw order. Partials: `.Input` (keys, click routing by priority, menu buttons and toggles, captures), `.Services` (NPC service windows and their actions, drops from the bag, icons), `.Quests` (dialog, boxes, quest window), `.Parties` (party window, request box, buttons under a player's portrait), `.PersonalShops`, `.Messages` (completed requests to the chat or the notice strip), `.HelpPet` (what the helper pet speaks of), `.Help` (the help window, the level guide and its switch) |
| `Client/PristonTale.Gameplay/UI/` (root) | `HudLayout` (800x600 design space), `LoginPanel`/`LoginLayout`/`TextInputField`, `DisplayConfirmation` |
| `Client/PristonTale.Gameplay/UI/Widgets/` | Bottom bar, status window, skill tree and sockets, minimap, target frame and combat history (`HudCombat`), 3D target portrait, NPC dialog, settings panel, system menu, notices |
| `Client/PristonTale.Gameplay/UI/Chat/` | Chat window, tabs, input line and speech bubbles (see [chat-and-gm.md](chat-and-gm.md)) |
| `Client/PristonTale.Gameplay/UI/Help/` | The help window and the game guide: `HudHelpWindow` (help, guide list, guide page), `LevelGuideBook` (the guide's data), `HudLevelGuide` (the level guide's notice and summary), `HudHelpArt` |
| `Client/PristonTale.Gameplay/UI/HelpPet/` | The beginner's helper pet's talk: `HelpPetMessages` (its texts) and `HudHelpPet` (help boxes and speech) |
| `Client/PristonTale.Gameplay/UI/Inventory/` | Inventory window layout, drag/drop and equip commands |
| `Client/PristonTale.Gameplay/UI/Npc/` | NPC service controller, shop and warehouse windows, store layout, NPC art |
| `Client/PristonTale.Gameplay/UI/Quests/` | Quest controller, quest dialog and yes/no boxes, quest window, quest texts and art (see [quests.md](quests.md#client)) |
| `Client/PristonTale.Gameplay/UI/Rendering/` | `HudSprites` (original UI images), `HudIconCache` (item icons), `HudAtlas` (login atlas), `HudCursor` (mouse pointer), `HudPainter`/`HudCanvas` |
| `Client/PristonTale.Gameplay/Weather/` | `WeatherPresentation`: the server's weather eased in, lightning and thunder timing |
| `Client/PristonTale.Gameplay/Audio/` | `PristonAudioLibrary` (bootstrap sound load), `FieldAudioController` (music, field and positional ambience, the rain loop and thunder), `NpcAudioController`, `TalkSound`, `CombatSounds` and `CharacterSoundBanks` (character, weapon and footstep sounds), `MonsterSoundCues`, `ItemSoundRules` (item sounds) |
| `Client/PristonTale.Gameplay/Effects/` | Effect glue: `EffectLibrary`, `VfxContent` (effect data from `<assetRoot>/Effect/Vfx`, loaded off the game thread), `SkillPresentation`, `ScriptEffects`, `SkillSoundBank`, `SkillState`, combat text, meteor, monster mesh and return effects ([effects.md](effects.md)) |
| [`Client/PristonTale.Client.Net/GameServerConnection.cs`](../Client/PristonTale.Client.Net/GameServerConnection.cs) | TCP session: connect, login, characters, world entry, request correlation, heartbeat, receive dispatch |
| `Client/PristonTale.Client.Net/IGameBackend.cs`, `IPlayChannel.cs` | Contracts the screens use: account and character requests; in-world commands and the presence stream |
| `Client/PristonTale.Client.Net/Online/` | `OnlinePlayChannel` (world session lifetime), `PresenceDecoder` (wire packets to `PresenceUpdate`) |
| `Client/PristonTale.Client.Net/Presence/` | Neutral presence model: `PresenceUpdate`, spawns, states (motion as `MotionState`), combat and skill events |
| `Client/PristonTale.Client.Net/WorldCatalog/` | `WorldRenderCatalog`: monster and NPC templates learned from the server |

## Startup

1. `Program` opens `client.log` beside the executable (`ClientLogFile`: the console copied with the local time on
   every line, errors with their stack, unhandled exceptions; the previous run's log kept as `client.previous.log`; the
   game runs without it when the file cannot be opened), then builds `Game1` with an optional settings path (default `ClientSettingsStore.DefaultPath`,
   `client-settings.json` beside the executable; fields in [client-settings.md](client-settings.md)).
2. `Game1` constructor: HiDef device, `PreferMultiSampling = false` (the renderer owns its multisampled HDR target),
   `IsFixedTimeStep = false`, resizable window, `Content.RootDirectory = "Content"`.
3. `Game1.LoadContent` creates `PresentationController` and `GameClient(GraphicsDevice, Content, ResolveAssetRoot(),
   "tcp://localhost:10009", presentation)`. The server address is the `Game1.ServerAddress` constant; there is no
   setting for it.
4. `GameClient.LoadContent` loads, synchronously on the game thread: the UI font, `HudAtlas.LoadBootstrap`,
   `PristonSkyLibrary.LoadBootstrap`, `EffectLibrary.LoadBootstrap`, `PristonAudioLibrary.LoadBootstrap`. It then
   creates the single `GameServerConnection` (not connected yet), the `ScreenContext` and the first screen,
   `OpeningScreen`.

### Asset discovery

- `Game1.ResolveAssetRoot` walks up from `AppContext.BaseDirectory` and returns the first ancestor's `assets/`
  directory; if none exists it falls back to `<base>/../../../../assets`. Running from `Client/PristonTale/bin/...`
  finds the repository's `assets/`.
- Every asset path is relative to that root (`ScreenContext.AssetRoot`, `GameScreen.AssetPath`). `GameScreen.AssetPath` and the
  scene join with `AssetFiles.Combine`. Original assets are read through `AssetFiles`
  (`Shared/PristonTale.Assets/AssetFiles.cs`), which resolves separators and rebuilds the real casing component by
  component, caching hits; use it (`Resolve`, `ResolveFile`, `Exists`, `ReadAllBytes`, `OpenRead`) instead of `File.*`
  for any path into `assets/`.
- Field definitions (stage `.smd`, scenery models, gates, warp gates, lighting, camera, music) come from the shared
  `WorldCatalog` in `Shared/PristonTale.Simulation/World/WorldDefinitions.cs`, not from the server. Monster and NPC
  models come from the server (`WorldRenderCatalog`, below).

## Screen state machine

```
OpeningScreen --login ok--> CharacterSelectScreen --enter world ok--> GameplayScreen
      ^                              |
      +------- Exit button ----------+
```

- `GameScreen` lifecycle: `Enter` (after the previous screen is disposed), `Update`, `Draw`, `HandleEscape`, `Dispose`.
- A screen requests a transition with `Context.RequestScreen(next)`. `GameClient` stores it as pending and swaps after
  the current `Update` returns: `Dispose` old, then `Enter` new. Never swap from `Draw`.
- `OpeningScreen`: cinematic (`StartImage/Opening/Map/stage.smd` and its camera path); any key or the end of the
  cinematic reveals the login form. `OpeningLoginController` starts `IGameBackend.LoginAsync` and polls the task each
  frame. The server accepts one login per connection, so when `IGameBackend.RequiresNewConnection` is set (a login
  succeeded or the connection closed) the controller first calls `ScreenContext.RenewBackend`: `GameClient` installs a
  new `GameServerConnection` and disposes the old one off the game thread.
- `CharacterSelectScreen`: sub-states `CharacterSelect`, `RaceSelect`, `JobSelect`, `FaceSelect`, `Name`. The roster
  shows each character in its armour, empty-handed. While a
  `Busy` operation (roster load, roster/job actor preparation, enter, delete, create) runs, input is ignored and
  `PollBusy` checks the task. A successful `EnterWorldAsync` builds an `EnteredCharacter` (class, name, level, face,
  field code, spawn x/z, tier, hair) and requests `GameplayScreen`. The Exit button returns to `OpeningScreen`; the next
  login opens a new connection.
- `GameplayScreen` has no transition out; leaving it means quitting.
- Escape: `Game1` calls `GameClient.HandleEscape` -> `GameScreen.HandleEscape`. `GameplayScreen` closes the open HUD
  panels (`GameplayHud.ClosePanel`) and returns true if anything was open; every other case exits the game. While a
  display change awaits confirmation, Escape reverts it and Enter confirms it instead.

### Shutdown

`Game1.OnExiting` cancels the exit until `GameClient.BeginShutdown` and `PresentationController.CompleteAsync`
(settings write) finish. `BeginShutdown` disposes the screen on the game thread (GPU resources), waits on the thread
pool for the screen's `Closing` task and then disposes the backend; meanwhile `Draw` shows "Saving and disconnecting…".
Disposing `GameScene` disposes the play channel (`GameScene.Closing`), which sends the Etervale leave-world request and
waits up to 5 s for its reply, so the server stores the character before the connection closes.

## Frame loop

`Game1.Update` -> `GameClient.Update` (audio settings, display confirmation, `screen.Update`, pending screen swap).
`Game1.Draw` -> `GameClient.Draw` -> `screen.Draw` and the confirmation overlay. `Game1.EndDraw` presents, then lets
`PresentationController` capture the window size, apply changed settings (`UpdateBoundary`) and pace the frame
(`Pace` sleeps toward the `maxFps` deadline; this is the frame limiter, after present).

`GameplayScreen.Update` order: hand a fetched catalog to the HUD, build `HudWorldView`, `GameplayHud.Update` (sets
`PointerCaptured`), copy HUD state into the scene (pointer capture, camera input enabled, mouse socket skills), debug
keys, `GameScene.Update`, copy the scene's selected target back to the HUD.

`GameScene.Update` order: NPC audio, equipment presentation, transient effects, field audio, one streaming upload step,
`DrainPresence`, skills, `UpdatePlayerInput` (picking, prediction, NPC approach, loot, attack approach), hover target
selection, warps, streaming scan, camera, renderer, movement report, field `OnUpdate` hook.

`GameplayScreen.Draw` order: `GameplayHud.RenderPortrait` (must be first: it binds its own render target, which discards
the back buffer), clear, `GameScene.Draw` (renderer, effects, `ComposeScene`), ground loot labels, floating combat text,
`GameplayHud.Draw`.

### What stays off the frame

`Update` and `Draw` never wait on network, disk, tasks or long locks (AGENTS.md). The patterns in use:

| Work | Off-thread part | Game-thread part |
|---|---|---|
| Server requests (login, characters, inventory, progression, NPC, services) | `Task` from `IGameBackend`/`IPlayChannel` | Poll `IsCompleted` each `Update`, or read a `volatile` result set by a continuation (`HudInventoryController`) |
| Upstream sends (movement, attack, skill cast) | `SendUpstream` attaches a fault logger; the connection enqueues into its send buffer | Fire and forget |
| Presence stream | `GameServerConnection` receive loop -> bounded channel -> `GameScene.ReadPresenceAsync` (`Task.Run`) -> `_presenceQueue` | `DrainPresence` reads at most 64 updates per frame; a failing update is logged and skipped, the rest still apply |
| Field streaming | `FieldResourceStreamer.Request`: stage and model parse, collision build, `PrepareStageTexturesAsync` on `Task.Run` | `Update` publishes through `AddStageIncrementally`: up to 32 steps and a 2 ms soft budget per frame |
| Remote actors | Composer `PrepareAsync` (`PristonActorLoader.Load` + `PrepareActorResourcesAsync`); a player's look prepares each part (body, head, weapon, shield) once for every look that wears it (`PlayerActorComposer`) | `RemoteEntityManager.Update` publishes one completed preparation per frame |
| Ground loot models | `GroundLootPresentation` preparation tasks | `GroundLootPresentation.Update` |
| HUD images and item icons | `HudSprites`/`HudIconCache` decode on the thread pool | Upload within a byte budget per `Update` (`HudSprites`: 512 KiB). The icon cache keeps only the last requested set (at most 512), so `GameplayHud.RequestIcons` always names the pack, the open shop and the open warehouse together |
| UI bitmap transparency | `PristonImageDecoder.Decode(..., bmpCoverage: true)`, `PristonUiPixels.Prepare` | A 32-bit BMP whose fourth byte varies (not all 0, not all 255) carries its coverage, which replaces the black key (such as item `DA304`, drawn over a near-black background); other BMPs take exact black as transparent; world textures stay opaque |
| Skill sounds | `SkillSoundBank` reads and decodes on a worker | Creates `SoundEffect`s in `Update`; a sound not ready is skipped |
| Display settings file | `ClientSettingsStore` writer task | `QueueSave` only |

Synchronous loads that are accepted because they happen at a screen boundary: `GameClient.LoadContent` bootstrap
libraries, the opening and character-select stages in `Enter`, and the first field in `GameScene.LoadEntered`.
`GameScene.BeginRemotePresence` then calls `PristonRenderer.RequirePreparedTextures()`: from that point the texture
loader throws if a texture reaches upload without an off-thread preparation, which catches accidental disk reads on the
frame.

## Network client

`GameServerConnection` implements `IGameBackend` over one TLS connection (`TlsTransport.ConnectAsync`, then
`FrameConnection`; `Shared/PristonTale.Protocol/Transport`). The address is `tcp://host:port`, optionally with
`?cert=<sha-256 hex>` to pin the server certificate. Wire details live in [protocol.md](protocol.md).

| Step | Client sends | Completes on |
|---|---|---|
| Connect | First `LoginAsync` opens the socket and starts `RunAsync` (receive) and `HeartbeatAsync` (`Heartbeat` every 4 s) | `ServerHello` (minimum protocol, server name for the character screen) |
| Login | Encrypted `Login` (protocol version, account, password) | `LoginResult` after the `CharacterList`; a non-zero status, or a `ProtocolVersion.Current` below the hello's minimum, is a denial |
| Characters | `ListCharactersAsync` returns the list cached at login; create/delete send `CharacterCommand` | `CharacterResult` (a fresh list arrives first on success) |
| Enter world | `EnterWorld` with the name | `WorldEntry` (id, field, spawn point; a non-zero status is a denial) |
| Open play | `OpenPlayChannel` -> `OnlinePlayChannel` sends `JoinWorld` with the entered id | `WorldJoined` |
| Leave | Disposing the play channel sends `LeaveWorld` | The answer (Saved tells whether the character was stored; 5 s timeout) |

Correlation rules (requests without an operation id):

- Login, character and world-entry requests, open play and leave are serialised by `_requestGate` and complete on
  their reply opcodes.
- Every request that waits for a reply goes through a `PendingReplies<TKey, TReply>` (`Client.Net/PendingReplies.cs`):
  `RequestAsync(key, send, ct)` registers, sends and waits; the reply handler calls `Complete(key, reply)`; `FailAll`
  ends every waiter. Inventory and progression commands carry a `Guid` operation id; `InventoryResult`/
  `ProgressionResult` complete `_operations`. Item uses, restarts, wing gates, NPC dialogs (keyed by NPC id), NPC
  services and quests have one table each. Every message the client sends implements `IFramePacket` and goes through
  `Send<TPacket>` (one send lock, no boxing).
- NPC interaction is an `AttackRequest` on the NPC id; one request per NPC id at a time; `NpcResult` completes it.
- NPC service requests carry an operation id. Shop catalog, quotes, warehouse and teleport list messages arrive before
  `NpcServiceResult` and are collected in `_serviceParts`, then returned with the result's outcome as one `NpcServiceReply`.
- Personal shop requests (`Online/PersonalShopRequests.cs`, `GameServerConnection.PersonalShops`): open, close, view
  and buy carry an operation id; the listing and the details of a view or a purchase arrive before
  `PersonalShopResult` and come back with it as one `PersonalShopReply` (the offers with their copies' values). A
  player's shop sign arrives in its description (`PlayerAppearanceInfo.ShopSign`); `RemoteEntityManager.ShopSigns`
  keeps the signs in view, the local player's included, and a new sign alone rebuilds no model.
- Trade requests (`IPlayChannel.RequestTradeAsync`, `AnswerTradeAsync`, `OfferTradeAsync`, `LockTradeAsync`,
  `CancelTradeAsync`) are sent without an answer; `TradeAsked` arrives as `PresenceUpdate.TradeRequest`, and `TradeState`
  (with the `TradeDetails` just before it) as `PresenceUpdate.Trade` (`TradeInfo`, `TradeInfo.None` once it ended).
- Party requests (`IPlayChannel.InviteToPartyAsync`, `AnswerPartyAsync`, `CommandPartyAsync`) are sent without an
  answer; `PartyRequest` and `PartyMembers` arrive as `PresenceUpdate.PartyRequest` and `PresenceUpdate.Party`
  (`PartyInfo`, the leader first, empty once the player has no party).
- A receive-loop failure fails every pending request and completes the presence channel with the error; later calls
  throw `IOException`.

Sends from any thread take `_sendGate` only to serialise into the shared scratch buffer; `FrameConnection` appends to
a pending buffer flushed by a single writer task (a backlog above 512 KiB closes the connection).

### Presence decoding

`HandleAsync` (receive thread) offers every packet to `PresenceDecoder.TryHandle` first; presence packets become a
`PresenceUpdate` published to a bounded channel (4096; when full the receive loop waits, so TCP pushes back on the
server).

- `PresenceEntries`: sets the presence clock from its header; a spawn binds a handle to an entity id, and later entries
  find the entity by handle and add their position change to the last position (`PresencePoint`) and apply the fields
  they carry. `RemoveObjects` frees the handle. The server pushes an `EntityDescription` ahead of the spawn or revision
  change it belongs to (the decoder keeps it until the spawn comes, at most 10 s); an object whose description is still
  missing, or a shared template's of another revision, triggers `DescribeEntity`. An object is published as a spawn only
  once its description is known. The connection's one-second timer (`RetryDescriptions`, which shares the decoder's lock
  with the receive thread) asks again for every object still undescribed after a second: the server drops requests
  beyond its bucket, and an object that stands still sends no entry that would ask again.
- The states of a `PresenceEntries` message (`EntityStateInfo`, a struct) go into a list the decoder lends to its update,
  sized to the message's entry count; the consumer gives it back once applied (`IPlayChannel.Recycle`, which the scene
  and the load test bots call; only the update it was lent to gives it back) and a later message reuses it. A message
  without spawns allocates only its `PresenceUpdate`; a consumer that never gives lists back costs one list per message,
  and the decoder forgets its loans once 1024 are outstanding. The pool has its own lock, so `Recycle` on the game
  thread never waits on a decode.
- `EntityDescription`: monster and NPC descriptions register a `MonsterRenderInfo`/`NpcRenderInfo` in `WorldRenderCatalog`
  (model path, scale, name, level, brood, NPC services, sound code, portrait offsets) and are shared by template code;
  a summon's or crystal pet's description (user brood, its owner) is never shared. A new description is re-published
  as a spawn of a known id (full refresh).
- Owner state: `OwnerStatus`, multi-part `Inventory` and `Skills` are combined into one `OwnerState` only when the
  status revisions match the received inventory and skills revisions. `OwnerItemDecoder` keeps the `ItemDetails` parts
  of a revision (attached to the items as `OwnedItemView.Details`) and the latest `OwnerItemStatus` (weight, critical,
  block, range, regeneration, resistances), folded into each published owner state.
- Also decoded: `RemoveObjects`, `CombatEvents`, ground items (`GroundItems`/`GroundItemsRemoved`), `FieldWarp`, `PresenceClock` (sets the
  server time stamped on later updates), skill events, status effects, cooldowns and cast results
  ([skills.md](skills.md)).

`IGameBackend.GetWorldCatalogAsync` returns the live catalog instance immediately; it fills as descriptions arrive.

### Presence on the game thread

`GameScene.DrainPresence` applies updates in arrival order until the queue is empty or 4 ms of the frame are spent,
never fewer than 8 a frame (`PresenceBudget`, `MinPresencePerFrame`), so a burst spreads over frames. Per update: record
field id, replace `Owner`, `RemoteEntityManager.ApplyServer`, apply a warp, ground loot, effects, skills, raise
`PresenceObserved` (the HUD target frame, combat history and chat lines; a `CloseRequested` update ends the game), then
give the update back (`Recycle`).

Coordinates: the wire uses PT-global `(x, z)` in fixed point (`GameUnits.FixedScale` = 256) and angles of 4096 per
turn; the client world uses `(x, -z)`. Every conversion goes through `WorldSpace` (`Client/PristonTale.Engine3D`):
`Mirror` for points (its own inverse), `Planar` for a transport `(x, z)` on the render plane, `MirrorFacing` for a
wire facing, `TransportFacing` for the facing the wire carries of a render forward, and `RenderYaw` for the
`CreateRotationY` angle of a render forward (facing = pi - yaw). `RemoteEntityManager.ApplyServer` mirrors spawns and
states as it reads them (no copy of the update) and the scene the warp position; `Apply` takes an update already in the
world's coordinates. Combat events, skill events, ground items and the update `PresenceObserved` sees keep transport
coordinates; their consumers mirror them with `WorldSpace` instead of negating Z inline.

### Remote actor playback

- `RemoteServerClock` maps local frame time onto the server clock from the clock of `PresenceEntries` and `PresenceClock`; early packets move the offset
  quickly, late ones slowly; a jump above 1 s resyncs. Without the extension samples are stamped with arrival time.
- `RemoteSnapshotBuffer` (per entity, 32 samples) renders at `now - delay`. The delay starts at 150 ms and follows the
  observed sample cadence up to 600 ms. Position, height and facing interpolate linearly; a Walk/Run sample may be
  extrapolated for 250 ms, then the lead returns to the last sample over 250 ms. A sample more than 256 units from the
  last or drawn position snaps and drops the history. Motion states apply when render time reaches their sample.
- `RemoteEntityBase` owns no rules: the server owns position and motion. Presentation-only choices: Walk/Run playback
  speed follows the interpolated ground speed; Walk/Run without ground motion shows Stand. Combat events share the
  sample timeline.
- `RemoteEntityManager`: the self entity's spawn/state/combat events go to `GameScene` sinks, not to an actor. A monster
  or NPC whose template is not in the catalog yet is held in `_pendingModels` until `SetCatalog`; a spawn whose models
  are loading waits in `_preparingActors` and keeps receiving the latest state. A player appearance change re-composes
  the actor from the drawn pose. Summons record their owner and are not pickable; their name boxes are the HUD's
  (`HudCombat.DrawNamePlates`).
- A monster or NPC model whose INX names a sub model on a skeleton of its own (`AlternateModel`; the mushroom's death,
  skeletons, golems, mummies, crystals and about fifty others) is composed with it hidden (`ModelActorComposer`,
  `ComposedModelActor.Alternate`). `RemoteModelEntity` shows the sub model in the main model's place while a motion only
  it has a clip for plays (most often the death) and the main model again for the others, as the original does; its
  `Actor` is the model shown, so sounds, effects, picking and outlines follow it. The renderer hides an actor with
  `SetActorHidden` (kept with its clip and world, left out of the batch lists).
- `GroundLootPresentation` lays each drop as the original does: a yaw drawn from its position
  (`(x + z) >> 2` in fixed point, 4096 per turn), and weapons (`0x01xxxxxx` codes) turned a quarter turn about X so they
  lie flat. The turned mesh rests on the floor by its lowest rotated corner (`GroundLootPresentation.Orientation`).
- Drop names: each anchored at its own drop's point 16 units up, recomputed every frame. Holding `A` names the drops
  32 to 768 units from the camera; the drop under the cursor (`GameScene.HoveredDropId`, picked in `Update` by its
  label or a sphere around it) is named at any distance. `GroundLootPresentation.CollectLabels` lists them nearest
  first by depth before the camera, in an order kept from frame to frame: a newly named drop is placed by its depth
  (`InsertByDepth`), and two neighbours swap only once the first lies more than `OrderHysteresis` (12 units, half a drop
  cell) farther than the second (`SettleOrder`), so drops of one row, at nearly one depth, do not trade places as the
  camera turns slightly while the player walks. `HudGroundLabels` draws them first in `GameplayHud.Draw`, under the
  monster and NPC names: a `HudPainter.NameBox` 36 pixels above the point in the classic name box geometry, the text in
  (150, 144, 206), (196, 186, 255) under the cursor. Labels never overlap: each keeps its spot unless it would cover a
  label already placed, and then rises just above it, 2 pixels apart (`HudGroundLabels.Stack`), so a pile of drops
  reads as a column of names over the nearest one. Labels move by whole pixels, with no grid, and keep their order, so
  the column follows the pile without reshuffling as the player walks; only turning the camera well round reorders it.
  A label that would leave the screen is skipped. Where each label landed goes back through
  `GameScene.PlaceGroundLabel` so a click on it picks the drop.

## Local player: prediction and authority

The client owns its own locomotion; the server validates and corrects.

- Click-to-move: `UpdatePlayerInput` advances `DestinationMovement` at `Locomotion` walk/run speed derived from the
  server-reported move speed (local derived stats until the first self state), slides against field collision
  (`MoveWithCollision`) and entity neighbours (`ResolveEntityCollision`) and follows the floor (`GroundTraversal`).
  Target reachability is checked with `GroundTraversal.CanReachSurface`.
- Reports: `UpdateMovementReport` sends `MovementReport` (position in PT-global, facing, moving, running, action target,
  field code) every 200 ms while moving, every 1000 ms idle, and at once when motion, run mode, target or field
  changes, or when a report is requested (`_reports.Request()`), never twice within 50 ms (the server closes a
  connection over 251 packets in 5 s). The cadence lives in `MovementReportPacer` (`World/MovementReportPacer.cs`); the
  scene only builds and sends the report. The walk toward a skill target reports only a new destination (`ApproachReportDistance`).
- Server echoes of the self state do not move the avatar. They update HP, move speed, attack rate, the attack-target
  reconcile (`AttackInputController.Reconcile` by sequence) and authoritative motions (Attack, Damage, Dead, Eat,
  the restart after death). The last echoed position is kept as `_serverPlanarPosition` for reach checks (NPC dialog,
  loot pickup). Each `SwingBegin` of the player's own swing turns the avatar toward that swing's target
  (`FaceSwingTarget`), as the server turns it: chained swings never leave the attack state, so the turn of the attack
  approach does not run between them. The approach, the swing and a skill cast in range turn through one
  `GameScene.FaceToward` (`AttackPresentation.FacingToward`: a target on the player's own spot keeps the forward).
- Only `FieldWarp` moves the avatar: a forced warp (server rejected a report), a warp that activates another field, or a
  difference above 200 units snaps the body and camera and stands it on the floor nearest the warp's height (within 64
  units, else the highest floor). A warp to another field code activates that field if loaded; otherwise it requests the
  field and holds the warp (`_pendingFieldWarp`, the player stops) until the field is in the world, then applies it. The
  held warp can be replaced by the unforced warp that confirms the field (which keeps the force of the held one), and a
  restart can move the player across before the field loads, so activating the field always places the height. A body
  left with no floor under it (placed before its field's ground was there) is put on the floor nearest the server's
  height each frame (`KeepOnFloor`). A warp gate's authored destination height is a hint: the body stands on the floor
  nearest it.
- A body more than 8 units above its floor falls 8 units a frame at 70 Hz (`GroundFall` over the shared `Falling`, applied
  in `KeepOnFloor`, as in the original); within 8 units it stands on the floor. The start and the landing of a fall each
  send a movement report, so the server's straight line between reports follows the way off the ledge. As in the original, the floor is followed only below the motions
  from 0x100 up (attacks, potions, damage, skills, landings, death): the body holds its height until they end, so a fall
  never cuts one short. A drop of more than 32 units plays FallDown; touching the floor plays FallStand, or FallDamage
  after more than 200 units (motion only, no damage). A landing holds the player like the other motions from 0x100 up
  (`LocomotionFrozen`) until the renderer returns the actor to Stand at the clip's end (`FinishLanding`). Walking off a
  ledge keeps the height (`CommitGroundMovement`) so the body falls the same way: a ledge is a drop of more than 8 units
  between two samples of `GroundTraversal.TryFollow` (4 units apart), so a steep slope crossed in one long frame is
  followed, not fallen. The player keeps moving across while FallDown holds against Walk and Run
  (`ActorMotionController.ApplyLocomotion`). A placement elsewhere (a warp, a warp gate, the special warp, a revive)
  ends a fall without a landing motion; with no floor anywhere under the body the fall stops. The special-warp hold
  keeps the gate's height and does not fall.
- The unforced warp that confirms a field the player is already near (within 200 units: a restart moved it across, a
  seamless border) sets only the height; snapping the place would pull back a player who has walked on.
- Field changes are local: across a seamless border the active field becomes the loaded field with the highest floor
  under the player (`UpdateActiveField`); warp gates are detected in `UpdateWarps` (level limit checked against the
  owner state's level, as are wing stones), and special-effect warp gates hold the player for 2 s with the return
  effect and portal sound before switching. A wing
  warp stone (`GameScene.WingGates.cs`) holds the player on it and opens the wing warp map; walking off closes it, and
  it opens again only after the player left the stone. Choosing a destination plays the return sparkle and the portal
  sound on the stone, and the request leaves two seconds later, as the original's wing trip does. The
  server learns the new field code from the next report, which a field change sends at once.
- `ActorMotionController`: `ApplyLocomotion` is ignored while Damage, Dead or Eat freeze locomotion;
  `ApplyAuthoritative` always wins.
- NPC click: `NpcApproach` walks the player to 0.65 x `NpcInteraction.Reach`, sends a stop report, and opens the dialog
  only when the server-echoed position is within 0.9 x reach.
- Loot click: `GroundLootInteraction` walks to the drop and sends an `InventoryAction.Pickup` command once both the
  local and server positions are within 28 units.

## Field streaming

The active field is the loaded field with the highest floor under the player. Every 8 frames `ScanStreaming` requests
neighbours whose gate seam, or warp-gate destinations whose gate, is within 1024 units, or ahead of a move target
within `PrefetchDistance(speed)` (1400 to 4500 units); fields beyond `RetentionDistance` (prefetch + 1200) unload. At
most 2 fields prepare at once; a field that failed once is not retried. `Warp` unloads every field but the destination.
Renderer side of stage publication: [rendering.md](rendering.md).

## Input

| Input | Where | Effect |
|---|---|---|
| Left click | `GameScene.UpdatePlayerInput` | In order: pick ground loot, cast the left socket skill, pick a monster (attack) or NPC (approach and talk), move. A skill motion is never cut: a click during it casts nothing; a picked monster or NPC is kept and approached once the motion ends, and a ground point is walked to then (`SelfCasting` holds the attack approach, the NPC approach and the walk, and `StopPredictedMovement` leaves the skill clip playing) |
| Double left click (350 ms) on the ground | same | Keep walking toward the cursor without holding the button, until blocked or the next click |
| `R`, or the bar's walk/run button | `GameplayHud.RunToggleRequested` -> `GameScene.ToggleRun` | Walk/run setting (running at first) for every move: clicks, monster, NPC and item approaches. A player tracing a target or holding the button keeps its pace while its stamina is at a quarter or less and walks once it runs out; overweight only walks |
| Shift held while running (game masters) | `GameScene.MovementSpeed` | Dash at 3000/256 units per original tick; `WorldJoined` carries the game master level and the server allows game masters that speed |
| Shift + click on a monster | same | Automatic attack (keeps attacking after release) |
| Right click | same | Attack if the right socket holds the basic attack, otherwise cast the right socket skill |
| Hold `A` | `GameScene.CollectGroundLabels`, `TryPickLoot` | Show and pick every ground item label |
| Middle drag, cursor at screen edge, wheel | `PristonCamera.UpdateOrbit` (Engine3D) | Orbit, edge orbit, zoom; ignored while the HUD captures the pointer |
| `I` or `V`, `C`, `S`, `X`, `Q`, `D` | `GameplayHud.UpdateKeys` | Inventory, status, skills, system menu, quest window, party window (ignored with Ctrl held) |
| `F1`-`F8` | `HudSkillBindings` | Select a skill shortcut; while pressing a skill in the tree, bind it ([skills.md](skills.md)) |
| `Enter`, `Page Up`/`Page Down`, `Up`/`Down` | `HudChat.Update` | Open the chat input or send its line, scroll the chat, recall commands; while typing, HUD, binding, debug and motion keys and the `A` labels are suppressed |
| `Esc` | `Game1` | Close the chat window first, then HUD panels, else quit |
| `Ctrl+F3` / `Ctrl+F5` / `Ctrl+F10` | `GameplayScreen.UpdateDebugKeys` | Toggle render stats in the window title / toggle shadows / cycle `PristonDebugView` |
| `Ctrl+1`..`Ctrl+7` | `GameplayScreen.UpdateMotionKeys` | Force Stand, Walk, Run, Attack, Damage, Dead, Skill on the local avatar |

`GameplayHud.PointerCaptured` (pointer over a HUD element or dragging, or any of the NPC and quest dialogs, message
boxes, the wing warp map, the Teleport Core list, the warehouse, aging and mixing windows open, as the original stops the
character under them) disables world clicks, hover picking and camera mouse input for that frame; a click the HUD took
stays captured when it closes the window under it. `GameplayHud.HoldsPlayer` (any NPC dialog or service window, the skill
master, a message box) holds the player: `GameScene.MovementLocked` stops a walk in progress and keeps every world click.
Keys are polled with `Keyboard.GetState()` and edge-detected against the previous frame per consumer. Typed characters
come from the window's text input (`Game1` to `GameClient.TypeCharacter` to `ScreenContext.TypedText`) and reach
`TextInputField` on the login and name forms and the chat input line.

## HUD and windows

- Layout: the HUD is authored in 800x600 logical units. `HudLayout.Create` picks the largest integer scale that fits;
  bottom-anchored elements move down by `BottomOffset`, right-anchored ones right by `RightOffset`. Convert the mouse
  with `HudLayout.ToLogical`.
- `HudPainter` draws original sprites at the integer scale with point sampling and queues text into a linear-filtered
  pass; call `Flush` before drawing a layer that must cover earlier text.
- Draw order (`GameplayHud.Draw`): speech bubbles, minimap, target frame, chat window (shrunk while a side window is
  open), status / skills / inventory windows (they share one screen area; opening one closes the others, `OpenOnly`),
  quest window, shop, warehouse, bottom bar, skill sockets, buffs and the quest timer icon, hover text, system menu,
  quest dialog or NPC dialog, settings, item overlays, quest yes/no boxes, notices.
- Server results reach the HUD as counters: controllers increment `Completions` and set `Message` and `LastOk`;
  `ForwardMessages` shows each new refusal once as a notice and each confirmation as a chat server line; skill notices
  go to the chat, quest hints to its assistant tab ([chat-and-gm.md](chat-and-gm.md#client-chat-window)).
- Inventory (`HudInventoryController`), carried as in the original: a left click picks an item up (an equipped one is
  taken off first; nothing is picked up while the player swings or casts) and it follows the cursor, centred; the next
  click sets it down where `HudInventoryPlacement` says: on the bag as `HudGridPlacement` (the original's 22-pixel item
  grids, shared with the warehouse: cells, the item under the pointer, landing) rounds its top-left to the nearest cell,
  or an equipment slot its frame (less half a cell a side) overlaps. `HudPlacement` holds the result and
  `HudPlacementColors` the original's colours; `HudItemCounts` caches the stack counts drawn on items. Free cells move it (Shift splits a stack), one covered item is exchanged
  (`Swap`, or the slot's item for an equip) and the covered item is then carried, a potion of the same kind is joined
  (`Merge`), two covered items or a slot of another kind or unmet requirements refuse, and above the window the item
  leaves the bag (`ExternalDrop`: the shop, the warehouse; otherwise `GameplayHud.ThrowFromInventory` puts it on the
  ground with `InventoryAction.Drop`, not while a shop, the warehouse, a work or travel window is open nor for an item
  `ItemDropRules` keeps; a stack first asks how many in the count box). Right click equips/unequips (the slot follows `ItemEquipmentMetadata`; rings take the left
  slot first), drinks a potion or uses a usable item (`HudItemUseController`: the Teleport Core window, confirmations,
  the item-use request). One command in flight; it carries the `InventoryRevision` it was built from. A modal
  (`GameplayHud.ModalOpen`: NPC or quest dialog, yes/no box, skill training box, warp map, Teleport Core list,
  confirmation) stops the bag and the warehouse, aging and mix windows; those work windows still trade with the bag.
- Step marks (`GameScene.Terrain.cs`, `PristonCollisionWorld.TryGetIceFloor` / `TryGetWaterHeight`): the stage's ice
  floors are its solid faces of an ice material, its water the faces of a water or see-through (over 0.1) material
  that do not collide. Wading in shallow water (the surface above the feet and less than 32 units up) splashes
  `particle.water` half a unit over the surface at each step 8 units ahead and every 64 frames of 70 per second standing,
  32 walking (8 ahead), 16 running (14 ahead), attacking or casting. Out of the water, each step in an ice field leaves a
  footprint on an ice floor (`image/mk-r.tga` 3 units right and 5 ahead, `mk-l.tga` left), 12 x 12 units, tilted with
  the ice, fading to a fifth over five seconds; at most 20 lie at a time. Local player only, as in the original.
- Held items (`GameScene.HeldItems.cs`): the weapon and off-hand glow and the class-up lights of every drawn player
  ([effects.md](effects.md#held-item-glow)).
- Wing warp map (`HudWingGate`): `WG_map.tga` (256 x 256) at (472, 120) with the icon of each stone the carried wing
  reaches, three rings growing and fading from the hovered icon, the help box at (50, 45) with the hovered stone's name
  and `WarpGateDoc` document (the fee table of `Default.sin`, its `%d` filled with the wings' fees, while none is
  hovered), and the cancel button at the map's lower right. A click checks the level and the gold, then asks for
  confirmation before `IPlayChannel.TravelWingGateAsync`; a refusal shows as a notice.
- Restart window (`HudRestartPanel`): opens 100 death-motion frames (about 1.4 s) after the local player falls, centred
  100 pixels above the middle; field, town and exit buttons light up under the pointer with their help lines; the field
  button stays dark with red help where the server's `RestartOffer` says the field restart is closed (nothing in the
  castle field). The town is chosen after 20 s open; a refused or busy answer opens it again, a stored exit quits the
  game. The scene (`GameScene.Restart.cs`) keeps the last offer and sends `RestartAsync`.
- Side buttons (`HudInventoryLayout.PageButton`, `PairButton`): the bag page A/B under the bag at (26, 572) and
  (48, 572), and the weapon pair A/B at (524, 525) and (546, 525) (the shipped `Inven.bmp`'s place, 5 pixels right of and
  11 below the original's table); `LeftArrow.bmp`/`RightArrow.bmp` light the shown page and the held pair, and the
  button under the pointer. A click or the keys E and W switch (`SwitchBagPage`, `SwitchWeaponSet`). The page switches
  with an item carried too: a carried bag item goes along to the page shown (the command names it, and
  `PlayerCharacter.TrySwitchBagPage` lays it on that page's first free cells, refusing the switch without room) and
  stays on the cursor; the pair does not switch while an item is carried. The side buttons take their clicks with an
  item carried, which is never set down on them. A pair asked for while the player swings or casts is held once that
  motion ends (the player stops, or the next swing or cast begins: `ActorMotionController.ActionStarts`), as the
  original swaps at the end of the motion.
  The button under the pointer shows its label `HudInventoryLayout.HintRise` (27) pixels above it, as the original's
  `Inven/InfoBox` art: `ButtonItemA`/`B` over the pages, `ButtonA`/`B` over the pairs (the shipped art reads
  "Equipemt2" over A and "Equipemt1" over B), and over the close button its exit sign and `ButtonExit` 8 pixels left.
- Gold coin (`HudInventoryLayout.GoldButton`, (120, 572)): a click with nothing carried asks how much gold in the count
  box (`GameplayHud.GoldFromInventory`). With the warehouse open it stores it (`DepositGold`, at most what is carried and
  what the warehouse still holds); otherwise it drops it on the ground (`InventoryAction.DropGold`), not while a work
  window or the wing gate is open, as the original refuses it. The warehouse's own coin takes gold out.
- Potion belt (`HudInventoryPanel.DrawBelt`, after the bottom bar, the window open or not): the three boxes of
  `PotionBack.bmp` at (495 + 26 x box, 565), each potion centred with its count. A right click drinks one (the window
  closed too); keys 1 to 3 drink from their box, and Shift with them fills the box from the bag potion under the pointer
  (`HudInventoryController.BeltKey`). A carried potion goes into a box (`BeltPut`; another kind's stack is picked up in
  exchange), and a belt potion goes back to free bag cells or onto a bag stack of its kind (`BeltTake`).
- Special item box (`HudInventoryController.QuestBoxShown`, a view of the client): its button at (70, 572)
  (`QuestItem_.bmp` closed, `QuestItem.bmp` open or under the pointer, labels `Questitem.bmp` and `close.bmp`) opens it
  over the bag, not while a service window is open; `QuestItembox.bmp` draws each of its 44-pixel boxes
  (`HudInventoryLayout.QuestBoxCell`), its items centred in them and the bag's items hidden. A box item is carried as any
  item and goes back to its box wherever it is set down; a box item left in the bag goes to the box when set down and
  there is room (`HudInventoryController.GoesToQuestBox`, `StoreQuestItem`), neither showing a landing colour. Opening a service window or receiving an item into the bag closes the box, a page button
  closes it instead of switching, and nothing is thrown while it is open.
- Count box (`HudCountBox`), the original's quantity question: `BoxMain.tga` in the message box's place
  (`HudMessageBox`, centred on the screen; the original pins it at (300, 200)) with two lines, the number
  in `COUNT.bmp` at (+34, +62), typed with the digit keys (Backspace erases) or stepped with the arrows at (+124, +62)
  and (+124, +75). A number typed above the question's maximum shows the maximum at once, and the arrows stay within
  it. OK or Enter accepts a non-zero count, Cancel or Escape closes. It is modal
  and takes the keyboard from the chat and the game keys while open.
- Inventory window (`HudInventoryPanel`): a worn two-handed weapon is also drawn in the empty off-hand slot, as the
  original draws it in both hands: the pointer there is on the weapon, and an off-hand item set down there exchanges it
  (`HudInventoryLayout.TwoHandedShadow`, used by `ItemAt` and `HudInventoryPlacement`). Carried/maximum weight centred
  in x 326..406 at y 471 of the window; items the character cannot use (level, attributes or class against `OwnedItemView.Details`, re-checked on every owner state)
  get the original red cell tint, in the bag and in their equipment slot. Under a carried item the landing cells or slot
  turn the original light blue, the item an exchange would pick up turns white, and a refused place turns red; the carried
  item is drawn on the cursor only, its place left empty, and once set down it stays where it was set down until the
  owner state shows where it went, or its own command fails; a later command's reply does not move it
  (`HudInventoryController.Held`). An item thrown whole to the ground (from the bag, or a worn item taken off, which
  the server first lays on the bag's first free cells) is drawn nowhere until the owner state shows it gone, or shows
  back in its place when the throw is refused (`HudInventoryController.Thrown`); part of a stack stays where it is.
  A ground item picked up with the window open and nothing carried comes to the cursor, as the original puts it on the
  mouse (with the window closed it goes where any received item goes: belt, empty slot, bag): `GroundLootInteraction`
  asks `ExpectPickup` as the pickup leaves and sends `InventoryCommand.PickupToBag`, and the bag item of its code that
  the reply's owner state adds is carried, shown nowhere until then (`HudInventoryController.Hides`); with the page
  shown full the server puts it in the hand instead, and the item in the hand (`OwnedItemView.Held`) is always carried
  (`CarryHeld`: at login, after a refused setting down, or once nothing else is carried or awaited), so it can be taken
  to the other page with the side buttons or E and set down there. The item on the
  cursor stays there when the window closes (V or I, the close button, another side window) or loses its input (a
  modal box, the settings), drawn at the pointer (`HudInventoryPanel.TrackHeld`), as the original keeps the mouse item;
  it is set down once the window is open again. Esc (`CloseAll`) lets go of it (`CancelDrag`): it stays where it lies
  in the bag.
  The item box (`HudItemDetailTooltip`) lists, one centred label-and-value line each,
  the name (unique,
  aged or normal colour), attack power, weapon speed, range, critical, defence, attack rating, absorb (with the catalog
  correction, one decimal), block, speed, integrity, the eight resistances, regeneration, added pools, potion storage,
  the requirements (orange when met, red when not), then the class specialization header and its bonuses in green, the
  shop price and the action hint. An aged copy shows its gauge under the name: the bar (`Aging/Bar.bmp`) filling toward
  the goal while it matures (with the live count from `AgingProgress` for worn copies), else the small plate
  (`Aging/Aging.bmp`), with `+level` in black; the stats its aging raises are light blue (119, 200, 254). A mixed copy's
  name is cyan (150, 255, 255) and the stats its mix raised are blue (110, 165, 250).
- Premium timers (`HudPremiumTimers`) form the column at x 8 under the running-effects bar; usable items have their
  own tooltips (`HudUsableTooltip`). Details: [special-items.md](special-items.md).
- Status window: attribute buttons send `ProgressionCommand` with `ProgressionRevision`; the organic, poison, fire,
  lightning and ice resistance boxes show the owner's resistances.
- Block feedback (`HudStringEffect`): when the player blocks a blow, the original `block.tga` word shows at (354, 488)
  of the 800x600 frame, opaque for 26 interface frames at 70 per second, then fades by 10 alpha per frame while it
  grows 3 pixels and drifts up-left.
- Overweight: while the owner state reports more carried weight than the limit, `GameScene` only walks (run toggles,
  Shift and double clicks walk), matching the server.
- NPC dialog (`HudNpcPanel`): name, the NPC's line, one button per offered service (grey when `Available` is false;
  quest roles get none); skill masters get the skill master box, whose class-change button works when the quests offer
  a rank-up; teleporter destinations replace the buttons. An NPC whose only working service is a shop, warehouse or the aging
  opens it directly (`DirectService`); the aging NPC also keeps its quest role (the rank-up brew). An NPC with a quest role also gets a quest `Talk` (`HudQuestController`); a quest
  dialog it answers with takes the help box, and its confirmation box is modal.
- HUD controllers keep their one request in flight in a `HudRequest<TOutcome>` (`UI/HudRequest.cs`): `Start(send,
  failed)` turns a thrown send into the controller's failure outcome (logged unless cancelled) and `TryTake` hands the
  outcome over once (progression, item use, NPC dialog and services, wing gates, quests).
- `HudNpcServiceController`: one request in flight; `Open` replies set the open service and its state; `OutOfReach`,
  `NotOpen` and `Conflict` close it. While the shop is open, an inventory revision change triggers a quote refresh.
- The service windows implement `IHudServiceWindow` (`UI/Npc/IHudServiceWindow.cs`): open and slide state, `BlocksWorld`
  (the work windows and the personal shop keep every click from the world, the shop only its area), `Contains`, `Hide`,
  body and overlay draw with one `HudServiceFrame`. `GameplayHud` keeps them in one list (`_serviceWindows`, draw order
  shop, warehouse, aging, mix, personal shop) for closing, advancing, capturing clicks and drawing; a new service window
  joins that list.
- Personal shop (`UI/PersonalShops/`, `HudPersonalShopController` with `HudPersonalShopWindow` on the store window,
  `HudSignBox`, art `HudPersonalShopArt`; `GameplayHud.PersonalShops.cs`): the inventory's shop button (98, 572) opens
  the player's own listing in a town, closing the other service windows and the special item box. While it does not
  sell, a click on a bag item the rules let go (`PersonalShopRules.IsListable`) asks its price per unit in the count
  box, bounded by `PersonalShopRules.MaxPrice` and the gold the player may still carry, and lays it at the first free
  place down the columns (`HudStoreLayout.FindSpace`); a listed item keeps its bag cell (`Withheld`) and a click on it
  in the window asks to take it back. The start button asks for the sign and sends the listing; the stop button closes
  the shop; hiding the window (exit, Escape, another service window) forgets an unsold listing. The player sells while
  its own sign hangs (`GameScene.ShopSigns`), or for a few seconds after an opened shop's reply while the sign is on its
  way: nothing in the bag moves, the player is held (`HoldsPlayer`), and a button at (10, 220) reopens the window. A
  count box or the sign box takes the keyboard from the chat and the game keys while it is open. A reply to a view or a
  purchase changes the window only while it still shows that shop (a view never replaces the player's own listing); one
  request is in flight at a time. A click on a selling player approaches it as an NPC (`GameScene.ShopSellerSelected`)
  and shows its shop: the price under the pointer, the player's gold, and a click on an offer asks how many (a stack, up
  to what the gold pays for) or whether to buy it. A reply shows the shop as it is now or closes it, and a refusal shows
  its text. Signs hang over their sellers in the shop's bubble frame while they say nothing (`HudChatBubbles.Signs`).
- Shop (`HudShopWindow`): buy, sell (click or drop an inventory item on the shop), repair, repair all; opens next to the
  inventory. Stock fills pages in authored order, each item at the first free place down the columns from the left
  (`HudStoreLayout.FindSpace`); an item that does not fit starts the next page. Goods sold in stacks (potions) ask how
  many in the count box, up to a stack and what the gold pays for, and a stack sold (clicked or set down on the shop)
  asks how many of it; other items buy or sell whole at a click. Warehouse
  (`HudWarehouseWindow`): items are carried as in the bag. A stored item set down on the grid sends Move, in the bag
  (`HudInventoryPlacement.OfStored`: bag cells only) Withdraw; a bag item set down on the grid sends Deposit
  (`GameplayHud.DropFromInventory` through the same `HudWarehouseWindow.SetDownOnGrid`). Over one item the request names
  it (`Covered`) and the HUD carries it once the reply shows it where it went; what `WarehouseStorage.CanStore` keeps
  out shows red, a potion is refused on the click with the original's message and stays carried. The coin of the upper
  bar opens the count box for gold to take out, bounded by the stored gold and the carry limit. Aging and mixing share
  their frame in `HudWorkbench` (slide, main item, stone and sheltom slots, drop placement, right-click take-back, start
  and exit, tooltips); each window supplies its placement rule, readiness, price, panel and start info, whether taking
  the main item back clears the window (aging) and when the start button shows, and its work (`Service`, `WorkAction`,
  `ResultText`, `Succeeded`; mixing keeps the formula it started with). `GameplayHud` sends and shows any bench's work
  through `ApplyWorkAction` and `ShowWorkResult`. Aging (`HudAgingWindow`, layout `HudCraftLayout`, art `HudCraftArt`):
  the shop frame with the aging title and the work panel; an inventory item dropped on it goes to the item box (refused
  with "Item can't be aged"), a sheltom to a cell whose grey hint asks it, a stone or ore to the stone cell. Placed
  items stay in the bag, dimmed and fixed; a right click gives one back. The plate shows `(level + 1) * price / 2`; the
  start button lights when every asked sheltom is in place and sends `Age` with the materials. The result shows the
  original message (+1, +2, -1, -2 or failed) with the drink sound or `sheltom-failure.wav`, and the window closes as
  the original's does. Mixing (`HudMixWindow`): the same frame with the mixing title and panel, or the alchemist's panel
  and title; an item in the item box (mixing only), sheltoms or seals in the twelve cells (alchemy: sheltoms up to the
  eleventh), the respec stone in the stone cell. The plate shows the item's price or the alchemy prices, both scaled by
  the offer's `PricePercent`; the result shows what the mix added (the formula's lines), the reconstruction, the
  refining or the failure. Server rules: [application.md](application.md), [world.md](world.md).
- Target frame (`HudCombat.DrawTarget`, `TargetSelection`): a clicked monster or NPC owns the frame; otherwise the
  hovered entity. The frame stays after an attack is cancelled and closes when the entity is removed or has been dead
  for 0x50 frames at 70 Hz. With `classicTargetHealthBar` false (default) the bar shows with level and life
  percentage; when true the bar shows without numbers, only after the local player started attacking that monster at
  full life.
- Name boxes over the scene (`HudCombat.DrawNamePlates`, drawn right after the drop names in `GameplayHud.Draw`, at
  the speech bubbles' head point): an NPC's name always within 768 units of the camera (180, 180, 255, as in the chat);
  a monster's only while hovered or the clicked target (`TargetSelection.HoveredId`, `TracedId`), in (255, 210, 210),
  or (255, 230, 200) when clicked. A summon or crystal pet's box shows within the NPC range in (220, 255, 160), with
  its owner's name in parentheses on a second line in (255, 255, 200) (the box grows upwards); the viewer's own also
  carries the life bar. Farther boxes draw first, the hovered and clicked ones last; a box that would leave
  the screen is skipped. Every box is `HudPainter.NameBox`: the speech bubble's dark translucent fill and gold rim (its
  texture's colours) with rounded corners, drawn from a generated 20-pixel nine-slice texture, its rim lighter under
  the cursor. With `classicNamePlates` false (default) the box holds the name at text scale 0.6 (`HudPainter.Text` with
  a scale), the capitals centred inside with room for descenders (`HudPainter.LetterHeightsAt`), and the 60x11 life
  bar (`DrawHealthBar`, the target frame's colours) shows under a monster once it was clicked or the local player swung
  at it (`Entity.Attacked`, set by `NoteAttack`); when true the box holds the name at the HUD text size and the bar
  shows while the cursor is on the monster (`ShowsPlateBar`).
- Hover outline (`GameScene.Outline.cs`, `PristonRenderer.SetActorOutline`): every actor of the hovered entity is
  outlined, red (255, 30, 0) for monsters and white for players; NPCs only with `classicNamePlates`, in green
  (0, 255, 100). The width share is 1 within 300 units of the camera and falls to 0 at 1300 (`OutlineStrength`). It is
  set again each frame and cleared when the cursor leaves.
- Mouse pointer (`HudCursor`, loaded by `GameClient`): the original yellow arrow (`image/Sinimage/Cursor`) as a system
  cursor tipped at its top-left pixel, on every screen. `GameClient` shows the screen's `GameScreen.Cursor` after each
  update; in play it is the attack arrow over a monster without an owner, the speech balloon over an NPC, the open hand
  (`GetItem_Cursor1`) over a drop and the closed hand (`GetItem_Cursor2`) while the left button is held, a drop winning
  over the monster or NPC behind it, and the plain arrow over players, summons, the HUD and the ground. The HUD imposes
  its own (`GameplayHud.CursorOverride`): the arrow over a dialog or message box, the pointer of the shop's mode
  (`BuyCursor`, `SellCursor`, `RepairCursor`) while a shop is open, and none while an item is dragged (a transparent
  cursor; the item's icon takes its place). A missing image keeps the system arrow.
- 3D portrait (`HudTargetPortrait`): rendered each frame into its own 4x MSAA target through
  `PristonRenderer.DrawActorPortrait`, framed on the `Bip01 Head` bone (fallback: model height) plus the authored
  portrait offsets from `MonsterRenderInfo`/`NpcRenderInfo`, with its own studio lighting.
- Settings (`HudSettingsPanel`, system menu entry Settings) edits `PresentationSettings.Requested`;
  `PresentationController` applies it at the next frame boundary. Display changes require confirmation within 15 s
  (`DisplayConfirmation`), otherwise they revert.
- System menu (`HudSystemMenu`, the X button): the original 128x64 `ExitMenu.tga` sheet at 698,504 (Help above Quit,
  with their hover plaques `Option.bmp` and `Exit.bmp`) and the Settings plaque (`Settings.bmp`) just above it. Quit
  closes the game; Help opens the help window.
- Help window (`UI/Help/`, `GameplayHud.Help.cs`): the help box at 208,11 (11 by 11 tiles) with three pages. Help shows
  the client's `image/Sinimage/Help/Help.sin` (code page 949; arrows the font lacks become `< > v ^`) in 18 lines of
  15 pixels; the icons at its top switch between help and the game guide. The guide's list holds the subject of every
  level up to the character's (`image/clanImage/Help/AutoHelp/LevelSubj.ini`), the latest first, ten at a time; a
  subject opens its page, the `.msg` text and `GHI-*.tga` picture that `LevelUp_Content.ini` names for the level and
  the character's faction (`LevelGuideBook`), its lines wrapping around the picture; "Guide Content" goes back to the
  list. The guide pages carry the level guide's On/Off switch (`ClientSettings.LevelGuide`). The wheel and the scroll bar
  move a long text; OK and Escape close the window, which keeps the world's clicks inside it.
- Level guide (`HudLevelGuide`): reaching a level the guide has a page for, or entering as a level-1 character without
  experience, shows a picture blinking every 0.7 s at the top left while the switch is on; a click opens its summary (a
  guide box with the first six lines beside the page's picture, Close, and ">>more..." opening the page in the help
  window when it is longer). A new character's first weapon and armour gifts (`ItemEffectKind.StarterGift`, order 0
  and 1) open the summaries of their beginner's pages at once, whatever the switch (once the guide's files are read).
  The window and the notice sit under the dialogs, the settings and the restart window, which take their clicks. The notice waits while the helper pet's box holds the
  corner; a later offer replaces an earlier one.
- Helper pet (`UI/HelpPet/`, `GameplayHud.HelpPet.cs`; the scene's `SelfHelperPet` gives the pet beside the player
  and the screen point over it): the texts are the client's `image/Sinimage/PetData/PetMessage.txt` (code page 949,
  `*Topic` sections closed by `end`). Below level 10, while the pet is shown, it speaks over itself in a chat bubble
  ("Name : message", 4 s) of the bar's buttons, toggles, skill sockets, gauges, day clock, minimap and chat window
  when they are pointed at, clicked or right clicked (each topic's message 0, 1 or 2), and of the C, I/V, S, X, Q, D, R
  and potion keys (message 3, the potions 3 to 5); it speaks when the pointer comes onto a part and keeps that part's
  line up while the pointer stays. In town it says an idle line every 30 s, and while fighting below level 8 one battle
  line each time the level's experience passes one or more of 5, 10, 20, 30, 35, 40, 50, 60, 70, 80 and 90%; neither
  repeats until its list is spent. Its help boxes (the `GH-Box`
  frame at the top left, the pet's portrait, OK `bltr_bt3`) queue and stay 15 s or until OK: the greeting of a level-1
  character without experience (its dragon and name, then the welcome of its class's start town), the guides of
  levels 2 to 10, the class guide at 90% of level 8 and the farewell at 90% of level 9 while fighting. The level-10
  guide still shows when the server's removal of the pet arrives before the level (within 2 s).
- Trade (`UI/Trades/`, art `HudTradeArt`; `GameplayHud.Trades.cs`, `GameScene.Trades.cs` as `ITradeClient`): the request
  box (`HudTradeArt.Request`, 10 s) is not shown while a service window is open. When the server opens the trade, the
  window (`HudTradeWindow`, a service window on the store frame) opens, closing the other service windows and opening
  the bag. The partner's grid (9x4 at (21, 86)) shows its offer with the values for the player, its name, gold, weight
  and lock mark; a click on a bag item lays it at the first free place down the columns of the player's grid (21, 195)
  and sends the whole offer, a click on an offered item takes it back, the gold button asks how much in the count box
  (zero accepted). The lock greys for 2 s after the partner's offer changed; a locked side changes nothing. Closing the
  window (its button, Escape, another service window) cancels the trade; the server's end closes it without a cancel.
  Offered items keep their bag cells (`Withheld`), and the player is held while the window shows.
- Party (`UI/Parties/`, art `HudPartyArt`; `GameplayHud.Parties.cs`, `GameScene.Parties.cs` as `IPartyClient`):
  - The party page (`HudPartyWindow`, D or the bar's party button) stands where the quest page does, so one closes the
    other; it opens when a party forms and closes when it is released. Members go down from (80, 38) at 30 px steps
    with the life bar (94 px), the mark (the leader's own) and the level in tens past 10 (`L4x`); a click picks a
    member for delegate and kick, a double click whispers to it. Delegate, disband and kick work for the leader, leave
    for any member, close always.
  - The request box (`HudRequestBox` with `HudPartyArt.Request`, the trade's too) at (513, 3) blinks, answers once from
    OK or cancel and closes after 20 s; a request is not shown while the warehouse is open.
  - A click on another player (not selling) approaches it as an NPC; once reached, its portrait shows the trade, party
    and friend buttons (`HudPlayerButtons`, on `cw-1.tga`) instead of the life bar while it stays the target within
    `TradeLimits.Reach`. Trade asks it to trade, party to group; friend puts it on the friends list and opens the
    friends page there.
  - The party window has two tabs at (60, 2) and (120, 2): the party and the friends page (`HudFriendsPage`, art
    `HudContactArt`, title `title-NoClan_chatting.tga`; `GameplayHud.Contacts.cs`, `GameScene.Contacts.cs` as
    `IContactClient`). Its buttons at (85, 220) show the recent players, the friends (an online or offline mark each,
    asked again every 10 s while the page shows) or the ignored players, 7 names a page from (100, 55); on the recent
    list "make friend" and "ignore" move the picked name onto a list, elsewhere the arrows page and "delete" takes it
    off. A click picks a name and a click on the picked one whispers to it (marked red). The recent players are the
    last seven whispered (either way), traded with or befriended, kept by the client for the session.
  - Members show on the minimap (`image/party.tga`, red past `PartyLimits.NearRange`), in name boxes with their life
    bar and in the bubbles' party colour.

## Audio

- `PristonAudioLibrary.LoadBootstrap` decodes every sound at startup through `PristonWaveDecoder`: opening and
  character-select music (`StartImage/...`), field music by BGM code (`wav/BGM`, `wav/Sod/BGM`), field ambience loops
  and positional ambience (`wav/Ambient`), NPC motion banks per sound code, and hashed NPC talk lines
  (`wav/Talk/<code>.wav`; optional, playback disabled when absent).
- `LoginAudio` plays the opening and character-select music, the character screen's three loops (`NewChar`, `Fire`,
  `Thunder`) and the screens' cues (`LoginCue`: button, back, new character, pick, delete, game start, login), with a
  0.5 s fade-in of the loops. The music and sound options do not mute them, as in the original.
- `TalkSound.GetSoundCode` hashes the CP949 bytes of an NPC line (ASCII letters uppercased) into the talk file
  code.
- `FieldAudioController.SelectField` picks music by `FieldDef.BackMusicCode` (3.6 s fade); field ambience depends on the
  field state and, in forests, the time of day; positional ambience uses the original distance falloff.
- `NpcAudioController` plays the dialog line or a motion sound of the NPC at its position relative to the listener.
- `MusicEnabled`/`SoundEnabled` come from client settings each frame (`ApplySettings`) and apply in the world.
- Skill sounds: `SkillSoundBank` ([skills.md](skills.md)).
- Character sounds (`CombatSounds`): `CharacterSoundBanks` maps every character sound code to its folder (monsters and
  summons by sound code, NPCs, player classes 1 to 10 by class code); a file plays for the motion its name starts with
  (`attack`, `damage`, `dead`, `neutral`, `move`, `disappear`/`change`, `skill`, `hammer`). Every folder is listed
  once on a worker when `CombatSounds` is built (the game thread never enumerates one); a character's files are
  decoded through a `SkillSoundBank` when it first appears, and a sound not ready yet is skipped.
  Monsters drawn smaller than their model play higher (3600/2205 of the rate), more than twice as large lower
  (1400/2205).
- `MonsterSoundCues` follows each monster's animation like `MonsterPresentation`: the attack or skill sound on the
  clip's first two event frames, the damage, death or vanishing sound when the monster enters that motion, and the idle
  sound one time in four when it stops.
- Players: a server `Hit` or `Miss` of a player's swing plays the weapon sound of the attacker's weapon (`CombatSounds.
  WeaponCode`: axes, swords with the first three as small blades, spears, claws, bows, staves cast by magicians and
  priestesses and struck by others, hammers, throwing weapons, punches without a weapon; one- or two-handed by
  `ItemEquipmentMetadata.IsTwoHanded`; a miss plays the hand's miss sound, a critical adds the critical sound). Damage
  and death play the class's sound, a `Revive` the restart chime. The local player's walk and run clips play the floor's
  footsteps (stone in towns and ruins, dungeon stone, soft ground in forests and deserts) on their first two event
  frames. Everything comes from the presence stream, so every client near the action plays it.
- Equipment looks (`EquipmentVisuals`, `EquipmentVisualTables`): every item with a model has its motion index (the
  weapon index the character motion files list; items newer than those files take the first item of their family and
  hand class), model (`image/sinimage/Items/DropItem/it<model>.smd`, also its ground model), hand and village bone. An
  armour or robe wears the body of its catalog ordinal (`ArmorSlots`: ordinals from 26 sit sixteen slots further, so
  DA126-129 wear the 25th-28th bodies, DA301/401 the 29th, DA302-304/402-404 the 30th-32nd and DA131+ the costume
  bodies) from its class row (A, B, C, D, mA, mB, mC, mD, E, mE; 75 slots each); premium costumes (`CA`) keep the class
  body and wear wings, and an armour missing from `ArmorSlots` keeps the class body.
- Progression (`GameScene.Progression`): a `LevelUp` combat event plays `vfx.player.levelup` (with the level-up chime) at
  the player, a `SkillLearned` event the skill-learn light (`ItemVfx.Learn`) and sound, for every client near.
- Item sounds (`ItemSoundRules`, `image/Sinimage/Sound`): an item sounds by catalog family (three characters of its key,
  then two: axes, claws, hammers, staves, spears, bows, swords, throwing weapons, armour, boots, gloves, shields,
  amulets, armlets, rings, sheltoms, potions...) with per-item exceptions; gold rings the coin. They play for the local
  player only: a ground pickup the server accepted (`GroundLootInteraction`), lifting an item in the pack, a successful
  move, split, merge, equip or unequip (`HudInventoryController.ItemSound`), a shop buy or sell or a warehouse
  deposit, withdrawal or move (`HudNpcServiceController.Succeeded`), and, as the original sounds every item it sets,
  an item the owner state shows arriving without a command of the window (a gift, a quest prize, a trade:
  `HudInventoryController.SoundArrival`; nothing for the first state after login or a bag page or weapon pair switch).
  `FieldAudioController.PlayItem` plays the same sound asked again within 300 ms once (a reply and its owner state).
- Weather sound (`FieldAudioController.UpdateWeather`, before `Update`): `wav/rain01.wav` loops at the rain's share of
  0.8, at 0.45 of that under a roof (`PristonRainOcclusion.Sheltered`, a surface over 40 units above the player), and
  the field ambience fades as the clouds gather (the original stopped it under its clouds and its rain);
  `wav/thunder1.wav` sounds each strike's thunder (`PlayThunder`).

## Weather

`GameScene.Weather.cs` takes the server's `Weather` message (`ServerWeather`, [world.md](world.md#weather)) and shows
it unless the `weatherEnabled` setting is off or the field shown takes no rain (`WeatherSchedule.Rains`; entering such
a field clears at once). `WeatherPresentation` eases it in: clouds over 6 s, rain over 4 s, the wind turning over 3 s
(heading and strength to world units per second, at most 360); the first message after entering the world snaps.
A storm strikes every 5 to 16 s: two or three flashes within a third of a second, the first the brightest, and the
thunder 0.3 to 3 s later, sooner and louder the nearer the strike. Each frame the scene hands the blend to the
renderer (`PristonRenderer.Weather`: sky, light and fog, [rendering.md](rendering.md#weather)), steps `PristonRain`
(occlusion map around the player, splashes) before the draw, draws it between the opaque scene and the skill effects,
and updates the weather sound. The loaded fields changing reads the occlusion map again (`InvalidateGround`).

## How to

### Add a HUD window

1. Create the window class in `UI/Widgets` (or `UI/Npc` for an NPC service, `UI/Inventory` for item views) with static
   layout rectangles in 800x600 design units, `Update`/`Click` for input and `Draw(HudPainter, ...)`.
2. Own it in `GameplayHud`: request its sprite paths through `HudSprites.Request` (add them to `HudArt` or
   `HudNpcArt` if they are fixed), route keys in `UpdateKeys`, clicks in `Click`, pointer capture in `Captures`, closing
   in `ClosePanel`, and draw it at the right place in `Draw`. If it shares the side area, open it through a `HudSlide`
   and `OpenOnly`.
3. Server calls go through a delegate passed into `GameplayHud` (as `applyInventory`, `requestNpcService`) that
   `GameplayScreen.Enter` wires to a `GameScene` method calling `IPlayChannel`. Keep one request in flight, poll the
   task in `Update`, report through `Completions`/`Message`.
4. Add layout or hit-test cases to `tests/PristonTale.Tests/HudInteractionTests.cs`; use `HudCaptureProbe`
   (`--capture-hud`) to check the picture. Update this document's HUD section.

### Add a screen

1. Derive from `GameScreen` in `Screens/<Name>/`; take dependencies from `ScreenContext`; load in `Enter`, release GPU
   resources and cancel backend calls in `Dispose`.
2. Start backend calls as tasks and poll them in `Update` (see `CharacterSelectScreen.PollBusy`).
3. Transition with `Context.RequestScreen(new NextScreen(Context, ...))`; override `HandleEscape` if Escape must not
   quit; override `StatusText` for the window title.
4. Skip input while `Context.Presentation.AwaitingConfirmation` is set.

### Handle a new server message on the client

1. Define the opcode and packet in `Shared/PristonTale.Protocol` ([protocol.md](protocol.md)) and its encoder on the
   server ([server.md](server.md)).
2. Stream data (world state, events): add a case to `PresenceDecoder.TryHandle` (both the `switch` and the opcode list
   in its return expression), convert to neutral types in `Client.Net/Presence`, and add an `init` property to
   `PresenceUpdate`. Mirror its positions with `WorldSpace` where they are consumed in world
   space. Consume it in `GameScene.DrainPresence` or through `PresenceObserved`.
3. Request/reply data: a `PendingReplies` table keyed by operation id (state messages go to `_serviceParts`), a case
   in `GameServerConnection.HandleAsync` that calls its `Complete`, its `FailAll` in `RunAsync`'s `finally`, and the
   request through `IPlayChannel`/`OnlinePlayChannel` (await `_start` first). The request message implements
   `IFramePacket`.
4. Add a wire round trip to `tests/PristonTale.Tests/RemotePlaybackTests.cs` or `ClientTests.cs` (`WireRoundTrip` runs
   the server encoder and the client decoder over real packet bytes); register new case classes in `Program.cs`.

### Load a new kind of asset in game

Prepare it off-thread (`Task.Run` plus a renderer `Prepare*Async` for textures) and publish it from `Update` within a
budget; never read files in `Update`/`Draw` after `BeginRemotePresence`. Resolve paths with `AssetFiles`.

## Pitfalls

- Spawns, states and warps reach the remote actors and the warp in world `(x, -z)`
  (`RemoteEntityManager.ApplyServer`); the update itself, its combat events, skill events, ground items and what
  `PresenceObserved` sees (so `HudCombat` positions and the minimap NPC list) stay in transport
  coordinates.
- An update's `States` list goes back to the decoder once the scene gives it back: an observer must not keep the list
  past `PresenceObserved`.
- `GameScene` planar vectors are `(x, worldZ)`, not `(x, ptZ)`; `EnteredCharacter.SpawnZ` and field gate coordinates
  are PT-global and are negated when placed.
- Self state echoes intentionally do not move the local avatar; adding a position correction from them causes rubber
  banding. Use a forced `FieldWarp` on the server for corrections.
- `RenderPortrait` must run before the world draw: binding its render target discards the back buffer contents.
- The presence channels are bounded; if the game thread stops draining (more than 64 updates per frame sustained), the
  receive loop's `TryWrite` fails and the connection ends.
- `WorldRenderCatalog` is filled on the receive thread; a template is registered before any spawn that references it is
  published, but spawns that arrived before `SetCatalog` reached the scene wait in `_pendingModels`.
- `GameplayHud.Update` has an `internal` overload taking keyboard and mouse state for tests; the public one reads the
  devices.
- HUD keys `I`, `V`, `C`, `S`, `X`, `Q`, `D` act on any press without Ctrl unless the chat input has focus
  (`GameplayHud.KeyboardCaptured`); a new key handler must check it too.
- Quest texts load on the thread pool; `GameplayHud.IconsIdle` waits for them so captures show the text.
- `IPlayChannel.SendSkillCommandAsync` has a default no-op implementation; test doubles inherit it silently.
- `PresentationController.Pace`, called from `Game1.EndDraw`, sleeps on the game thread by design (frame limiter after
  present); do not move frame work after it.

## Related

- [architecture.md](architecture.md) — process and threading model.
- [rendering.md](rendering.md) — `PristonRenderer`, stage upload, actors, portrait drawing, camera.
- [shaders.md](shaders.md) — content build of the shader library.
- [skills.md](skills.md) — skill sockets, casting, skill timeline, cooldowns, buffs.
- [effects.md](effects.md) — effects engine and VFX data.
- [client-settings.md](client-settings.md) — `client-settings.json` fields and presets.
- [protocol.md](protocol.md) — opcodes, packets, framing and TLS.
- [application.md](application.md), [server.md](server.md) — the authority behind each request.
- [testing.md](testing.md) — regression runner and capture probes.
