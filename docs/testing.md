# Testing

The regression runner in `tests/PristonTale.Tests`: default cases, how they are registered and run, the opt-in CPU
benchmarks, and the asset/GPU probes that read game data or create a Vulkan device.

## Read this when

- You changed behavior and need to run or extend the regression suite before finishing.
- You are adding a regression case, a fixture, or a disposable SQLite database to a case.
- You need a timing number (server tick, world load, parsing, render frame) or a visual capture.
- You want to check shaders, the look of real scenes, the HUD, skills or equipment against the game data.
- A runner command "does nothing" or runs the whole suite instead of a probe.

## Code map

| Path | Responsibility |
| --- | --- |
| [Program.cs](../tests/PristonTale.Tests/Program.cs) | Probe/benchmark flag dispatch, case discovery, `--filter`, run loop, exit code |
| `PristonTale.Tests.csproj` | Console exe; embeds `Fixtures/*.json`; imports `Client/MonoGameRuntime.targets` (native runtime) |
| `Assert.cs` | `Assert.True`, `Assert.Near`, `Assert.Throws<T>`/`ThrowsAsync<T>` (the expected type; an `AssertionException` never counts) |
| `Fixtures/` | Embedded tables: `class-skill-source.json` (class skills), `job-head-source.json` (416 job-head rows) |
| `SkillFixtures.cs`, `SkillScenario.cs` | Synthetic field, players, monsters, weapons and 50 ms tick stepping for skill cases |
| `MemoryCharacters.cs` | In-memory `ICharacterRepository` with update hooks for persistence-failure cases |
| `RecordingLog.cs` | `IGameLog` that keeps the item transfers the world reports |
| `ImmediateSaves.cs` | `ICharacterSaves` that stores each capture at once, so a case reads storage right after the operation that queued it |
| `WorldPump.cs` | Ticks a `WorldServer` at 50 ms until an operation it serves completes (service and transaction cases) |
| `WireRoundTrip.cs` | Snapshots through server `PresenceEncoder` and client `PresenceDecoder` over original packet bytes |
| `MultiplayerServer.cs`, `MultiplayerClient.cs` | A whole server in the runner (real host, disposable SQLite, ephemeral port, synthetic world) and real TCP clients with the world each one observed |
| `AllocationSampler.cs` | Allocation ticks by type for benchmarks (`PRESENCE_ALLOCATIONS=1`) |
| `AbsoluteContentManager.cs` | `ContentManager` that opens `.xnb` files from an absolute content root (GPU probes) |
| `*Probe.cs`, `*Probe.<Topic>.cs` | Opt-in probes (see Probes); never part of the default suite |
| `*Benchmark.cs`, `RenderBenchmarkProbe.cs` | Opt-in benchmarks; `DomainTests.BenchmarkAsync` is the `--benchmark` tick benchmark |

The csproj references Gameplay, Server, Protocol, GltfExport and WorldImport, so the runner sees every layer.
Default case files by area (all `tests/PristonTale.Tests/<Name>.cs`):

| Area | Files | Covers |
| --- | --- | --- |
| Domain | `DomainTests`, `ProgressionAndOwnershipTests`, `ProgressionDataTests`, `AuthTests`, `OutboxTests` | Attributes, ownership, mailbox, XP, login, outbox |
| Transactions | `ProgressionTransactionTests`, `InventoryTransactionTests`, `GroundPickupTransactionTests`, `PotionTests` | Storage-first commit, replay |
| Yahoo | `YahooTests` | Chat words, the Space window and its stamina, queued cheers, the capsule, motion and wire |
| Weather | `WeatherTests` (with `ChatFixture`) | The rain schedule and its draws, the fields without rain, `/weather`, the snapshot and wire; the client's easing, lightning and thunder, the rain occlusion map; the realistic sky's coverage, rainbow, moon phase and noise tiling |
| Usable items | `SpecialItemTests` | Item rules, premium timers and formulas, use flow, pets, persistence, wire |
| Crystals | `MonsterCrystalTests` | Crystal tables, pet summon and hunt, the pet following and recalled, its own status per pet and the crystal it shows, dismissal box and wire, its end with a dismissal, its death, the owner's death or field change, pets parting from one spot, the shared delay, clan, mercenary, soul and town refusals |
| Castle items | `CastleItemTests` | Castle-only scrolls and stones, scroll exclusions and replacement, leaving the castle, Restoration |
| Death and restart | `DeathPenaltyTests` | Death shares, the level 85 reduction, castle exemption, field rule, towns; town and field restarts stored and applied; the Resurrection Scroll; a refused field restart; saving while dead; the restart window's timing |
| Event items | `EventItemTests` | Foods, gift boxes, chocolates, the soccer-ball head, boosters (offline pause), skill delay, item events |
| Puzzles | `PuzzleTests` | Lucky bag seal and reveal, board completion and exchange, sealed ground drops |
| Equipment | `EquipmentTests`, `RangedEquipmentTests` | Catalog rules, hands, class masks, bone attachments |
| Item rules | `ItemRulesTests` | Wearable slots, requirements, two-handed swaps, drop and shop rolls, the item stat pipeline, block, carried weight, saved rolls, item details on the wire, item reader |
| Protocol | `BinaryProtocolTests`, `ClientTests`, `NetworkHardeningTests` | Message layouts, framing, TLS and certificate checks, send backlog and batches, presence, abuse limits, connection caps |
| World tick | `WorldTickTests` | The tick's absolute timeline: late ticks caught up, stalls skipped |
| Starter gifts | `StarterGiftTests` | A new character's gifts: order, pace, perfect copies of its class's specialization worn or kept, the class's own weapon family and off-hand, chat line, stored count |
| Multiplayer | `MultiplayerMovementTests`, `MultiplayerCombatTests`, `MultiplayerPresenceTests`, `MultiplayerSessionTests`, `MultiplayerSocialTests`, `PresencePipelineTests` | Propagation between real clients over TCP (see Multiplayer propagation); frames, shared encoding, revisions, crowd cap, event ticks, grid, status book |
| World data | `WorldImportTests`, `NpcPlacementMigrationTests`, `MonsterNameTests`, `NpcServiceTests` | Import and migrations on temp SQLite |
| Rewards, spawns | `WorldRewardsTests`, `SpawnGateTests`, `SpawnScheduleTests` | Loot, XP, spawn masks, boss hours |
| Movement | `MovementAndStreamingTests`, `GroundTraversalTests`, `TerrainTests`, `EntitySweepTests`, `SpatialIndexTests` | Reports, stacked floors, ramps, gaps, steps |
| Camera | `CameraTests` | Camera collision across fields, clearance, follow, edge orbit, middle drag |
| Monsters, NPCs | `MonsterMovementTests`, `MonsterNavigationTests`, `MonsterCombatTests`, `MonsterSkillTests`, `MonsterSkillStatusTests`, `MonsterBossSkillTests`, `NpcMovementTests` | AI on synthetic stages; monster skill families on a one-field world (`MonsterSkillField`) |
| Monster effects | `MonsterPresentationTests` | Effect families, clip triggers, auras, status visuals, authored monster data |
| Skill data | `ClassSkillDataTests`, `SkillCatalogTests`, `SkillSystemTests`, `PassiveSkillTests`, `SkillWireTests` | Catalog, formulas, wire |
| Skill mechanics | `SkillAllyTests`, `SkillAreaTests`, `SkillChargeTests`, `SkillCompanionTests`, `SkillControlTests` | Server resolution |
| Skill mechanics | `SkillDefenseTests`, `SkillEnchantTests`, `SkillResurrectionTests`, `SkillStealthTests`, `SkillSummonTests` | Server resolution |
| Skill client | `SkillPresentationTests`, `VfxLayerTests`, `VfxImportTests` | VFX definitions, catalog, layers, converter, authored data, cooldown and status books |
| Quests | `QuestTests` (with `QuestFixture`), `QuestServiceTests`, `QuestHudTests` | Every quest family's steps, rank-ups and the tier; journal storage, quest messages, durable requests, kill credit, upkeep, the arena duel; quest HUD flows and layout |
| Trades | `TradeTests` (with `ChatFixture`), `TradeHudTests` | Requests, offers, locks and their delay, the stored exchange, refusals, how a trade ends (also while it is stored), offered items changed elsewhere, both sides locking at once, rules, wire; the trade window |
| Contacts | `ContactTests` (with `ChatFixture` and SQLite) | The lists' rules, the server's answers with presence, ignored whispers, storage, wire; the friends page |
| Parties | `PartyTests` (with `ChatFixture`), `PartyHudTests` | Requests, answers and recommendations, the leader's commands, leaving the world, shared experience and gold, rules, wire; the party window, request box and player buttons |
| Chat, game masters | `ChatRoutingTests`, `GameMasterCommandTests` (with `ChatFixture`), `ChatClientTests`, `GameMasterStorageTests` | Spoken range, whispers, channels, notices, shutdown; every command and the level table; window, input, flood and bubbles; `gm_level`, audit and character moves on SQLite |
| Client | `RemotePlaybackTests`, `AttackInputTests`, `FrameTimingTests`, `ClientSettingsTests`, `FieldAudioTests`, `ClientLogTests` | Playback, input, settings, the client log |
| HUD and screens | `HudInteractionTests`, `CharacterHeadTests`, `CharacterSelectTests` | 800x600 layout, hit tests |
| Rendering | `RenderTests`, `RenderFidelityTests`, `PhotorealTests`, `MaterialAdapterTests`, `ShadowProjectionTests`, `SkyTests` | CPU-side renderer |
| Meshes, effects | `SkinPaletteTests`, `ModelNormalTests`, `AnimationFallbackTests`, `TransientLightTests` | Skinning, lights |
| Formats | `BinaryCursorTests`, `EffectLoadingTests`, `GltfExportTests` | Binary readers, shader load failure, glTF export |

## Running

```bash
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror
dotnet run --project tests/PristonTale.Tests -c Release --no-build
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --filter skill
git diff --check
```

- The runner is a console program, not a `dotnet test` suite; `dotnet test` discovers nothing.
- Each case prints `PASS <name>` or `FAIL <name>: <exception>` (stderr); the last line is `<passed>/<total> passed`.
- Exit code is 0 when every case passes, 1 when a case fails, 2 for an unknown option or a probe flag with the wrong
  argument count.
- `--filter <text>` keeps only cases whose name contains the text (ordinal, case-insensitive).
- Probes run through `dotnet run ... -- <flag> ...` or directly as
  `dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll <flag> ...` (used below for GPU probes).

### Registration and execution model

- Every test class is `internal static class <Area>Tests` in namespace `PristonTale.Tests` with
  `public static IEnumerable<(string, Func<Task>)> Cases()` that `yield return`s `("behavior sentence", MethodAsync)`.
- `Program.cs` discovers every class with a public static `Cases()` of that type by reflection; nothing to register.
- Cases run sequentially, classes in full-name order and cases in their `Cases()` order. Each is awaited with a 15 s
  timeout (`WaitAsync`). A thrown exception of any type fails the case and the runner continues with the next one; a
  timeout fails the case and stops the run, since the case is still running and would share the process with the cases
  after it.
- Production assemblies expose internals to the runner through `InternalsVisibleTo` (Application, Geometry, Protocol,
  Client.Net, Engine3D, Gameplay, Server, GltfExport).

### What default cases may touch

- Synthetic data built in code, embedded `Fixtures/`, original-protocol byte arrays, loopback TCP (an exchange, or a
  whole in-process server on an ephemeral port with real clients), and files and SQLite databases created and deleted
  under `Path.GetTempPath()`.
- Never game assets, a production database, `data/pristontale.db`, or reference directories.
- One exception: the `SkillPresentationTests` case "game data effects cover every effect ..." walks up from the runner
  for an `assets/` directory with `Effect/Vfx`, checks that every effect, child, texture, animation, model and sound the
  rules and definitions use exists there, and prints `(skipped: no assets/Effect/Vfx next to the repository)` when none
  is found. The authored definitions are checked without assets from `tools/PristonTale.VfxImport/Authored/`.

## Multiplayer propagation

The `multiplayer:` cases prove that what one player does reaches exactly the right other players. Each starts a
`MultiplayerServer`: the real host composition (`AddPristonServer`) on a disposable SQLite database, `Network:Port=0`
(the port comes from `GameTcpServer.BoundPort`), `Assets:AllowMissingTerrain` with an empty assets root (no game
data is read), and a synthetic `WorldData` (monsters, spawn points beside the scenario, a staff and usable items)
swapped in before anyone joins. Players are accounts with one character each, placed by `PlayerSetup` (class, level,
field, position, learned skills, weapon, carried items) through the real repositories. Each `MultiplayerClient` is a
real `GameServerConnection` with its play channel; a reader folds every `PresenceUpdate` into an `ObservedWorld`
(entities with every position sample and its server time, removals, combat, skill, status and item events, chat,
ground items, warps, owner state), and cases wait on it with timeouts (`WaitAsync`, `WaitUntilAsync`). `WalkAsync`
moves at 5 Hz below the slowest run speed; `HuntAsync` keeps an attack locked and walks into reach like the client.

| File | Covers |
| --- | --- |
| `MultiplayerMovementTests` | Walk, run, facing and stop at every viewer; the view range and other fields (nothing about a player reaches them; crossing the range spawns and removes); gate and warp field changes for the viewers of both fields; a rejected report corrects only the mover |
| `MultiplayerCombatTests` | Swings, hits, the kill, a public drop, its pickup and the level up identical at a watching player; a skill's start, impact and damage and the monster's life; a monster's positions and attacks identical at three viewers for the same server ticks |
| `MultiplayerPresenceTests` | A buff at viewers and at a player who arrives later; a summon and a crystal pet owned by one player beside wild monsters of their kind; equipment, big head, firecracker and scroll status; death and respawn at both places; descriptions the rate limit dropped asked again |
| `MultiplayerSessionTests` | Leaving and coming back, a dropped connection freeing the character, two logins racing for one character |
| `MultiplayerSocialTests` | Spoken lines within range only, whispers only to their two ends, a hidden game master leaving and rejoining presence |

Tolerances: positions within 0.05 units of the reported or server point (fixed point plus floor), facing within 0.01
rad, events compared exactly (in server order, aligned on the first one both clients saw). Each case takes 3 to 8 s
inside the runner's 15 s limit. Add a case by starting a server with the world the scenario needs, entering the
players with `MultiplayerMovementTests.Enter`, acting through the client and asserting on the other clients'
`ObservedWorld`.

## Benchmarks

All CPU-only; no GPU, assets or database unless stated.

| Command (after `dotnet run --project tests/PristonTale.Tests -c Release --no-build --`) | Measures |
| --- | --- |
| `--benchmark` | After the (filtered) suite: 5 runs of 128 players, 100 warmup and 1,000 measured 50 ms ticks; median, p95, max, bytes/tick |
| `--benchmark-world` | 128 players + 512 monsters at 20 Hz (AI, combat, collision, snapshots, 128 drained outboxes); spacing 128 and 64 units, 5 runs each |
| `--benchmark-presence <counts> <layouts> <ticks>` | The real `WorldServer` tick plus one `PresenceEncoder` per player (no sockets): e.g. `500,1000,2000,5000 spread,crowded 100`. Players walk, a quarter fight, a tenth cast; prints tick and encode p50/p99, CPU cores, bytes per client per second and per tick, allocations (tick and encode), collections and heap. `PRESENCE_ALLOCATIONS=1` adds the allocating types. Results: [protocol.md](protocol.md#measured-capacity) |
| `--benchmark-parsing <assets>` | Binary cursor microbenchmarks and `Field/forest/fore-1.smd` parses; requires 752 materials, 35,519 faces |
| `--profile-ricarten <assets>` | Ricarten stage and model parsing, collision build and texture preparation times |

Render and streaming benchmarks need the GPU; see Probes.

## Probes

Probes read game data (`<assets>` is the asset root, normally `assets`) and/or create a graphics device. Run them only
when reading those assets is authorized. GPU probes:

- Are MonoGame games on the Native platform (Vulkan): they need a Vulkan driver, a desktop session (a virtual X server
  without Vulkan presentation does not work) and the native runtime the build copies next to the runner (README.md,
  Build).
- Read the client's compiled content from `<content>`, normally `Client/PristonTale/bin/Release/net10.0/Content`; build
  the client first.
- Honor `MG_HIDDEN_WINDOW=1` to hide the window. It lengthens presentation (about 1.7 ms per frame measured on a GTX
  1050 Ti), so keep the window visible for timing runs.
- Report failure by throwing, which ends the process with a nonzero exit code.

### Argument reference

| Flag and exact arguments | GPU | Does |
| --- | --- | --- |
| `--verify-shaders <content>` | yes | Draws every technique into float targets and compares with C# references (see below) |
| `--benchmark-render <assets> <content> <report.json> <field-id> <settings.json> [options]` | yes | Frame timing report plus `<report>.png` |
| `--check-look <mode> <assets> <content> <output-dir>` | yes | Fails on look regressions; `mode` is `charselect`, `actor`, `opening`, `scenery` or `shade` |
| `--profile-stream <assets> <content>` | yes | Incremental stage upload with the game's policy at a fixed 60 Hz |
| `--profile-stream-baseline <assets> <content>` | yes | Same with an 8-step upload limit for comparison |
| `--capture-hud <assets> <content> <output-dir> [<background.png>]` | yes | In-game HUD scenarios to PNG (scales 2 and 3, `npc-*` dialogs and service windows (shop, warehouse, aging, mix, alchemy), `buffs-*` continuous-skill bar, `restart-*` restart window, `quest-*` quest dialog, boxes, window and timer, `help-*` help window, guide list and page, level guide notice and summary, `skill-right-select` prints the right socket after a right click) |
| `--capture-hud-target <assets> <content> <output-dir>` | yes | Ricarten at 14:00 with remote-roster entities; target frame, bar, resolutions |
| `--capture-chat <assets> <content> <output-dir> [<background.png>]` | yes | Chat window open, closed, unfocused, per tab, assistant split and page, shrunk beside the inventory, at 1280x720 and scale 2; speech bubbles over players, an NPC and a monster |
| `--capture-item-hud <assets> <content> <output-dir>` | yes | Premium timer column, hover and blink, Teleport Core pages and confirmation, item tooltips (orb, package, crystal, gift box, chocolate, castle stone, lucky bag), item effects, castle items and boosters in the effects bar, the puzzle board, finished puzzle and collector question, an item carried over a free cell, another item and the weapon slot, and the count box for a carried stack set down above the window |
| `--capture-shadows <assets> <content> <output-dir>` | yes | Two characters and Babel at Navisko's start by day (wide, close, and close without contact shadows, without shadows, and the occlusion view), beside the nearest lamp by night, a morning with the sun moving at the server clock's rate, a camera turning around them, and ten seconds of the mean brightness per frame with the sun moving (`Brightness <frame> <all> <ground>` lines, no images); Karina and Ariel where Navisko places them. At 1920x1080; consecutive frames with a change image (`-change.png`, eight times brighter) and the count of changed pixels |
| `--capture-item-effects <assets> <content> <output-dir>` | yes | Ricarten at night: pets idle and striking, firecracker bursts, the skill-learn light, force orb glow and hit, a big head, crystal pets with their plates in the return sparkle, the soccer-ball head |
| `--capture-login <assets> <content> <output-dir> <background.png>` | yes | Login screen edge cases (see below) |
| `--capture-skills <assets> <content> <output-dir> [<hex,hex,...>]` | yes | Casts skills with the server timeline (see below) |
| `--capture-sky <assets> <content> <output-dir>` | yes | One `sky-NN.png` per sky profile |
| `--capture-weather <assets> <content> <output-dir>` | yes | Ricarten's start under each weather at 1920x1080 after two seconds: clear, overcast, light, heavy and storm rain, a lightning flash, rain at dusk and at night, close under the eaves, the clouds' shadows and a morning mist; the realistic sky toward the horizon at noon, at sunrise with and without the mist, in a broken afternoon, at sunset and dusk, under a full and a new moon, overcast, in a storm and its lightning, in a storm at night and at dawn, with a rainbow and the aurora, and the classic dome (`WeatherCaptureProbe`) |
| `--capture-npcs <assets> <content> <output.png>` | yes | NPC presentation capture |
| `--capture-job-heads <assets> <content> <output.png>` | yes | Job-head presentation capture |
| `--capture-equipment <assets> <content> <output-dir>` | yes | Equipment captures |
| `--capture-ranged-equipment <assets> <content> <output-dir>` | yes | Bow and crossbow attachment captures |
| `--capture-ground-loot <assets> <content> <output.png>` | yes | Ground drop elevation, coordinate remapping, the drop names with `A` held (one hovered) and a label click target per drop spot, and the plain and highlighted name box in the corner; `<output>-close.png` shows how the drops lie (weapons flat) |
| `--capture-combat-effects <assets> <content> <output-dir>` | yes | Temporary lights, meteors and the return effect over time |
| `--capture-monster-skills <assets> <content> <output-dir> [<case,case,...>]` | yes | Real monsters' attack, skill and death clips with their effects, and monster statuses on a player |
| `--capture-potions <assets> <content> <output-dir>` | yes | The three potions (`ItemVfx.Potion`) over a dark floor at night at 0.15, 0.45, 0.8 and 1.2 s |
| `--capture-vfx <assets> <content> <output-dir> [<name,name,...>]` | yes | Plays named effects of `assets/Effect/Vfx` (see below) |
| `--verify-skill-motions <assets>` | no | Every skill rule's motions (index, length, events, class tag) against the class motion tables |
| `--verify-monster-skills <assets>` | no | Monster skill windup and duration from real models |
| `--verify-vfx-lifetimes <assets>` | no | Plays every effect of `Effect/Vfx` (duration up to 60 s) with a moving caster and target and lists those still alive 30 s after their duration; prints `0 of N` when none lingers |
| `--verify-player-timing <assets>` | no | Per-class attack, damage and EAT timing against actor files |
| `--verify-equipment <assets>` | no | Equipped attack clips against `PlayerEquipmentTiming` |
| `--export-equipment-timings <assets> <output.json>` | no | Writes the generated `World/Motion/Data/PlayerEquipmentTiming.json` (see Pitfalls) |
| `--verify-world-rewards <assets> <disposable.db>` | no | Real catalog checks on a copy of an imported database (see below) |
| `--validate-binary-assets <assets>` | no | Parses every `.inx`, `.smd`, `.smb` and `.cam` under the root and lists failures |
| `--inspect-actor <assets> <actor-path>` | no | Prints mesh, skeleton, material and bone details of one actor |
| `--inspect-ricarten-ground <assets>` | no | Reads only the Ricarten stage: stacked floors near the entrance gate and high cameras with an occluded focus |

`--benchmark-render` details:

- `[options]` is `<seconds> <runs> <hour> <orbit>` optionally followed by `<distance>`, then `<pitch>`, then
  `<focusX> <focusZ>`, then `<yaw>`, then `<crowd>` (walking and running actors added out to 1000 units around the focus, for the
  crowd level of detail; `NaN` keeps a default pitch or focus).
- Accepted argument counts after the flag: 5, 9, 10, 11, 13, 14 or 15; any other count runs the suite instead.
- Defaults without `[options]`: 60 s, 5 runs, hour 12, no orbit. `distance`, `pitch`, `focusX`, `focusZ` and `yaw` are
  floats; yaw is in radians (0 looks toward +Z, pi/2 toward -X).
- The settings file must be a complete, valid client settings document with `maxFps` 0 and a windowed mode.
- The JSON report holds frame, update, draw and present times; CPU time per render section (`CpuSectionsMs`); GPU time
  per pass from Vulkan timestamps (`GpuSectionsMs`, through the fork's `GpuTimestampPool`; `GpuTimestamps` is false when
  the device lacks them, and a frame that begins inside a render pass stays untimed); and the cost of each incremental
  stage-upload step (`StreamingUploadSteps`). `EffectiveMultiSampleCount` is the HDR scene target's sample count.
- Section means count only frames in which the section ran. Inside `Frame`: `SunShadow` (cascades refreshed that
  frame), `LampShadows`, `Prepass`, `AmbientOcclusion` (GTAO, contact shadows, blur), `LightCulling`, `Environment`
  (sky cube faces, prefiltering, irradiance; four times a second), `Reflections`, `Volumetric`. Outside `Frame` (post
  chain): `Temporal`, `Exposure`, `Bloom`, `DisplayTransform` (exposure, display transform, grade), `PostAntiAliasing`
  (FXAA or SMAA). `SkyCapture` is the atmospheric fog's sky copy; `SkyCaptureRefreshes` counts its redraws.

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --verify-shaders Client/PristonTale/bin/Release/net10.0/Content
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --benchmark-render assets \
  Client/PristonTale/bin/Release/net10.0/Content /tmp/ricarten.json village-2 settings.json 15 2 12 true
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --check-look charselect assets \
  Client/PristonTale/bin/Release/net10.0/Content /tmp/look
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --capture-hud assets \
  Client/PristonTale/bin/Release/net10.0/Content /tmp/hud
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --capture-skills assets \
  Client/PristonTale/bin/Release/net10.0/Content /tmp/skills
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --inspect-ricarten-ground assets
```

Other probe details:

- `--capture-skills` casts Fire Ball, Healing, Pike Wind, Extreme Shield and Impact, or the comma-separated hex codes
  given (for example `0x05040004,0x07080003`), through the client's skill presentation with the server's timeline; it
  fails when a cast starts no motion or shows no effect light. Besides `<class>-<skill>-<time>.png` (the lit stage) it
  saves `<...>.dark.png`, the effects alone on a dark backdrop, because the exposed stage hides additive light, and
  `<...>.actor.png`, the caster and the dummy at noon without effects, for skill looks on the actors (tints,
  translucency, afterimages, shield swaps, enlarged weapons). Custom codes are captured at 0.4, 0.8, 1.2, 1.7, 2.4 and
  4 s; self buffs add their status to the caster at the hit, harmful statuses land on the dummy with the impact.
- `--capture-vfx` plays each named effect (default: a representative set of converted scripts) with a caster at the
  origin and a target 85 units away, saves the scene and a `.dark.png` at 0.15, 0.45, 0.9 and 1.5 s, and fails when an
  effect draws nothing at all four moments. Run `ptvfx import assets` first (docs/tools.md).
- `--capture-login` draws before the first Update with a preloaded shared atlas, then covers a 1x1 viewport and its
  restoration, disabled credentials, keyboard navigation, masking and scaled mouse hit tests.
- `--capture-hud` shows the typed NPC dialog, teleport destinations, the shop (buy and sell) and the warehouse in its
  `npc-*` scenarios, the item box with rolled values, red requirements and a specialization (`inventory-item-hover-*`),
  the carried weight and unusable-item tint (`inventory-*`), the resistance boxes (`status-*`) and the block word
  (`block-800`). Its `quest-*` scenarios show a quest dialog with its document text, the yes/no box, the Fury reward
  list and reward view, the lit class-change button, the quest window with a description (800x600 and scale 2), the
  quest timer icon with and without effect icons and the redistribution offer. It reads the Ricarten stage only for the
  authored minimap rectangle.
- `--capture-hud-target` spawns monsters of two races, an NPC and a player through the remote entity roster and captures
  the target frame's live portrait, the classic bar, 1280x720, 1920x1080, scale 2 and the minimap under the cursor.
- `--verify-world-rewards` migrates the given database twice, then checks that every item drop code resolves, Hopy
  yields 525 XP, Ricarten has 40 fixed NPC placements, `pragma_foreign_key_check` is empty and every placed NPC mesh
  loads.

### `--verify-shaders` coverage

Draws the static and skinned surface effects into a float target and checks against the same formulas in C#
(`ShaderValidationProbe*.cs`, one partial per topic):

- Material model: emission, masked cutout, UV scroll, second layer (multiply, add), lit model (ambient, sun along the
  normal, shadow-map receiver), vertex light on both paths (albedo scaled in linear light), selection rim, unlit effect.
- Fog toward a flat colour and toward a sky capture; sky layers; a night dome with horizon glow and moon disk
  (reflections never brighter than the visible dome; a dark rough ground reflects no more sky than under a uniform sky
  of the same irradiance).
- Shadow casters' depth (orthographic and perspective); skinning by a bone translation; motion target (camera, jitter,
  a skinned bone's previous pose).
- Post chain: HDR composition, automatic exposure (histogram, adaptation), bloom pyramid, the three display transforms
  and the colour grade against CPU references, temporal resolve of a still scene, display-referred output, mesopic
  vision in the final pass.
- Lighting: tiled light culling (depth range, side planes), image-based lighting irradiance scale, a lamp-lit actor
  behind an occluder (never black; inverse-square falloff), screen-space reflections whose rays all miss.
- Foliage: a two-sided leaf texel above the alpha cut-off covers the background exactly as an opaque texel.
- Volumetric light: scene x transmittance + in-scatter with the exact optical depth; 24 accumulated frames of shafts
  through a striped shadow map deviate from the converged mean less than 40 % as much as one frame.

### `--check-look` modes

| Mode | Fails when |
| --- | --- |
| `charselect` | Character-select stage, braziers with their green flames (`CharSelectFlames`), roster: > 2 % of the frame clipped to white or > 5 % with a clipped channel |
| `actor` | NPCs at noon, front and back: the two-pixel silhouette ring is more than 1.25 times the interior luminance |
| `opening` | Login flythrough with meteors: the town in the lower third is more than 1.8 times redder than blue |
| `scenery` | Ricarten metal on a stage quad vs an animated scenery model, noon and 17:00: luminance > 8 % apart, saturation > 0.05 apart, |
| | or animated `Specular` debug view > 1.25 x the stage quad's |
| `shade` | NPC on an open floor and on a dark-lit (shaded) floor at 10:00: actor shade/open ratio off the floor's by > 1.5 x |

### Streaming probe

`--profile-stream` uses `FieldStreamingPolicy.MaximumUploadSteps` (32 steps) and the 2 ms budget of the game. It clears
the window instead of drawing the world, so timings isolate publication and do not establish in-game frame time or GPU
capacity. It reports batch and triangle counts and a digest of static vertex data plus skinned bounds and palettes;
this is not a readback of write-only GPU buffers or a visual comparison.

## How to

### Add a regression case

1. Pick the area file from the Code map, or create `tests/PristonTale.Tests/<Area>Tests.cs` with the shape above.
2. Add `yield return ("<lowercase sentence stating the guaranteed behavior>", <Name>Async);`. The sentence is what
   `--filter` matches and what `PASS`/`FAIL` print; make it unique and descriptive.
3. Write `private static Task <Name>Async()` (or `async Task`) using `Assert.True(condition, "what broke")`.
   Build data in code; reuse `SkillScenario`/`SkillFixtures` for skills, `MemoryCharacters` for persistence failures,
   `WorldPump` to run a service call through the world's ticks, `WireRoundTrip` for presence on the wire.
4. Keep each case under the 15 s timeout and free of assets, GPU and fixed ports.
5. Run the build and the suite (Running). Update this doc when you add an area file or a probe.

### Add a fixture

- Put the file in `tests/PristonTale.Tests/Fixtures/` and add `<EmbeddedResource Include="Fixtures/<name>" />` to the
  csproj.
- Read it with `typeof(<Area>Tests).Assembly.GetManifestResourceStream("PristonTale.Tests.Fixtures.<name>")`.
- Fixtures hold extracted tables in Etervale's own format; assert their row counts so truncation fails loudly.

### Use a disposable SQLite database

- Directory: `Path.Combine(Path.GetTempPath(), $"priston-<topic>-{Guid.NewGuid():N}")`.
- `services.AddLogging(); services.AddPristonSqlite(Path.Combine(directory, "world.db"));`, build the provider,
  `await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();`.
- In `finally`: `Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();` then delete the directory recursively
  (pooled connections keep the file locked on Windows otherwise).
- Template: `MonsterNameTests`, `NpcServiceTests`, `WorldImportTests`.

### Add a probe or benchmark

- Add the class (`<Topic>Probe.cs`, a `Game` subclass for GPU work) and a dispatch block in `Program.cs` before the
  case list, matching `args.Length` exactly and returning 0.
- Load compiled content through `AbsoluteContentManager`; write captures to the output path argument.
- Add a row to the Probes table above.

## Pitfalls

- Probe dispatch matches the flag and the exact argument count. A wrong count, an unknown `--check-look` mode or any
  other unknown argument stops the runner with exit code 2 instead of running the suite.
- `--benchmark` runs after the suite (and honors `--filter`); `--benchmark-world` skips the suite.
- `--no-build` runs the last build: rebuild after editing tests or production code.
- `--export-equipment-timings` overwrites a generated data file; review the diff before keeping it.
- `--verify-world-rewards` migrates the database it is given: pass a copy, never `data/pristontale.db`.
- GPU probe timings with `MG_HIDDEN_WINDOW=1` are not comparable with visible-window runs.

## Related

- [README.md](README.md) — documentation index
- [tools.md](tools.md) — SmokeClient and LoadTest for end-to-end checks against a running server
- [rendering.md](rendering.md), [shaders.md](shaders.md) — what the render probes measure
- [client-settings.md](client-settings.md) — the settings file `--benchmark-render` requires
- [persistence.md](persistence.md) — migrations used by the disposable-database cases
- [skills.md](skills.md) — skill data checked by the skill cases and probes
