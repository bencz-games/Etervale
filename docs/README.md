# Etervale documentation

Reference documentation for working on Etervale's code, written so a coding agent can open one or two files and know
where to look. Setup, build and run commands are in the root [README.md](../README.md); working rules are in
[AGENTS.md](../AGENTS.md).

## How to use these docs

1. Find your task in the routing table below and open the listed doc.
2. Every area doc has the same shape: scope, `Read this when`, `Code map`, topic sections, `How to`, `Pitfalls`,
   `Related`. Read the code map first; open source files only after it narrows the search.
3. Use [glossary.md](glossary.md) for domain terms and original format names.
4. When you change behavior, a contract, a data format, a setting or a command, update the doc that describes it in the
   same change. A doc that disagrees with the code is a bug.

## Documents

| Doc | Covers |
|---|---|
| [architecture.md](architecture.md) | Projects, dependency direction, where each kind of rule goes, shared codes (`Shared/PristonTale.Contracts`), server and client threading, path of a player action |
| [glossary.md](glossary.md) | Domain terms, abbreviations and original formats |
| [simulation.md](simulation.md) | `Shared/PristonTale.Simulation` core: characters, classes, items, combat, modifiers, progression |
| [world.md](world.md) | Simulation world rules: fields, terrain, AI, spawning, loot, motion, NPCs, physics, `Shared/PristonTale.Geometry` |
| [skills.md](skills.md) | Skill system end to end: class skill data, server resolution, opcodes, client skill UI and VFX hooks |
| [quests.md](quests.md) | Quests end to end: quest families, rank-ups and the class tier, server authority, arena duels, quest UI |
| [special-items.md](special-items.md) | Usable items end to end: premium timers, pets, travel items, scrolls, force orbs, firecrackers, item HUD |
| [costumes.md](costumes.md) | Cosmetic wardrobe, gender policy, body mappings and inventory presentation |
| [chat-and-gm.md](chat-and-gm.md) | Chat end to end: window, bubbles, routing, whispers, notices; game-master levels, commands and audit |
| [application.md](application.md) | `Shared/PristonTale.Application`: use cases, sessions, `WorldServer`, tick, transactions, NPC services |
| [server.md](server.md) | `Server/PristonTale.Server`: startup, hosted services, TCP edge, settings |
| [protocol.md](protocol.md) | Wire protocol: TLS transport, login and world messages, Etervale opcodes, server capacity |
| [persistence.md](persistence.md) | Repositories, migrations, schema, SQLite and PostgreSQL providers |
| [world-import.md](world-import.md) | `tools/PristonTale.WorldImport`: authored data formats, parsers, catalog import, verify |
| [client.md](client.md) | Client app, screens, game scene, HUD and windows, remote actors, audio, client networking |
| [rendering.md](rendering.md) | `Client/PristonTale.Engine3D`: original formats, renderer passes, lighting, shadows, post-processing |
| [shaders.md](shaders.md) | Shader library under `Client/PristonTale/Content/Shaders` |
| [effects.md](effects.md) | `Client/PristonTale.Effects` and the VFX JSON format |
| [client-settings.md](client-settings.md) | Every `client-settings.json` field, presets and validation |
| [tools.md](tools.md) | Every project in `tools/` and the `3d_glb` scripts |
| [testing.md](testing.md) | Regression runner, adding cases, benchmarks and probes |

The MonoGame fork documents itself in [monogame/README.md](../monogame/README.md).

## Task routing

| Task | Start with | Then |
|---|---|---|
| Change a damage, accuracy, stat or level formula | simulation.md | application.md (combat pipeline) |
| Add or change a class skill | skills.md | effects.md, protocol.md |
| Change a quest, a rank-up or the class tier | quests.md | application.md, client.md |
| Add or change a usable or premium item | special-items.md | application.md, protocol.md |
| Change monster behavior, spawns or drops | world.md | application.md |
| Add a player command or NPC service | application.md | protocol.md, server.md, client.md |
| Change chat routing, the chat window or a game-master command | chat-and-gm.md | application.md, client.md, persistence.md |
| Add a packet or opcode | protocol.md | server.md, client.md |
| Add a column, table or migration | persistence.md | world-import.md, application.md |
| Import a new kind of authored data | world-import.md | persistence.md |
| Add a HUD window or screen | client.md | rendering.md |
| Change lighting, shadows or a render pass | rendering.md | shaders.md, client-settings.md |
| Add a client option | client-settings.md | client.md |
| Add a skill or hit effect | effects.md | skills.md |
| Change a server setting or connection limit | server.md | protocol.md |
| Add a regression case | testing.md | the area doc |
| Run or extend a tool | tools.md | |

## Repository map

| Path | Content |
|---|---|
| `Shared/` | Contracts, Geometry, Assets, Simulation, Protocol, Application (shared by server and client) |
| `Server/` | Game server host |
| `Persistence/` | Storage abstractions, repositories, migrations, providers |
| `Client/` | Engine3D, Effects, Client.Net, Gameplay, the client executable |
| `tests/` | Regression runner |
| `tools/` | Importer and developer tools |
| `monogame/` | Vendored MonoGame fork (Vulkan) |
| `docs/` | This documentation |
| `assets/`, `data/` | Game data archive and local databases; not in Git |

## Writing rules for these docs

- English only; prose wrapped at 120 characters.
- Describe the current state. No dates, changelogs or review notes; Git history is the record of changes.
- Name code by repository-relative path and type or member name, not by line number.
- Never cite the original source; describe original behavior and formats in Etervale's own terms (see AGENTS.md).
- Keep each fact in one doc and link to it from the others.
