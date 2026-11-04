# Etervale

Etervale is a C# and MonoGame reimplementation of Priston Tale. Gameplay authority runs on the game server.
The server uses SQLite by default; PostgreSQL remains an option.

The game is online only. The client and server speak Etervale's own protocol over TLS on port 10009, and the server
is authoritative.

This README covers setup, build and run. Commands run from the repository root and use the Release configuration.
How the code works is documented in [docs/](docs/README.md).

## Requirements

- .NET 10 SDK.
- [Game data archive](https://drive.google.com/file/d/1oE3oQwQAG8YghbTlqzXhtX23seGhH2pF/view?usp=sharing),
  **published 2026-11-04 at 15:50 (UTC-03:00)**.
- For the client: a GPU and driver with Vulkan 1.1 or later. The client uses MonoGame's Native platform on Vulkan
  through the fork in [monogame/](monogame/README.md); shaders compile with DXC on every platform (no Wine).
- The client's native runtime, built once per machine (see Build): on Windows, Visual Studio 2022 or 2026 with the
  "Desktop development with C++" workload, and Git; on Linux, cmake, make, a C++17 compiler, premake5, pkg-config, Git
  and the ALSA, PulseAudio and PipeWire development headers. The Vulkan SDK is not needed.
- Docker with Compose only if you choose PostgreSQL for the online server.

The game assets and authored world data are excluded from Git. `assets/` contains models, textures, audio, HUD
resources and INX animation data; `assets/GameServer/` contains the authored item, monster, NPC and field configuration
read by the importer. The MonoGame content build needs `assets/Hud/Fonts/DejaVuSans.ttf` (`tools/ui/build_font.py`, see docs/tools.md).

Expected layout:

```text
Etervale/
├── assets/
│   ├── char/
│   ├── Field/
│   ├── Effect/
│   │   └── Vfx/          (written by ptvfx import)
│   ├── image/
│   ├── sky/
│   ├── wav/
│   ├── Hud/
│   └── GameServer/
│       ├── OpenItem/
│       ├── Monster/
│       ├── NPC/
│       └── Field/
└── data/
```

The importer looks for `assets/GameServer/` or `GameServer/` in the working directory or an ancestor of its executable
and also accepts explicit `--source` and `--assets` paths. The client finds `assets/` by walking upward from its
executable; the server searches the working directory, then the executable directory, and their ancestors, unless
`Assets:Root` is set.

## Build

Build the MonoGame native runtime (`mgruntime`: Vulkan, SDL2, FAudio) once per machine, and again after any change
under `monogame/runtime/src`. It is written to `monogame/runtime/bin/<rid>/`, which is git-ignored; the client and the
regression runner copy it next to their executables, and their builds stop with these instructions when it is missing.

```powershell
# Windows (PowerShell): SDL2 and FAudio at the pinned commits, premake, MSBuild; about 5 minutes the first time.
powershell -ExecutionPolicy Bypass -File monogame/runtime/build-windows.ps1
```

```bash
# Linux and macOS.
monogame/runtime/build.sh
```

The Linux script is the fork's own and is used unchanged; see [monogame/runtime/README.md](monogame/runtime/README.md)
for its packages. Then build the solution and the importer:

```bash
dotnet tool restore
dotnet restore PristonTale.sln -warnaserror
dotnet build PristonTale.sln -c Release -m:1 -warnaserror
dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
```

`dotnet tool restore` installs the MonoGame content builder (`dotnet-mgcb`) of `.config/dotnet-tools.json`; the client
build compiles its shaders with the fork's effect processor (`monogame/pipeline`), which the solution builds first. The
standalone importer build is explicit because its Release output must be available for the `--no-build` commands below.

## Screenshots

<img width="1918" height="1012" alt="image" src="https://github.com/user-attachments/assets/2850e4b8-9c72-43b2-9171-6434a6313135" />
--
<img width="1918" height="1012" alt="image" src="https://github.com/user-attachments/assets/81668a53-e1ed-499d-b5d5-e13488e596dd" />
--
<img width="1918" height="1012" alt="image" src="https://github.com/user-attachments/assets/ac1b25ce-96f1-489f-9aa4-a2c9f6e70fb1" />

## Run: migrate, import, and connect

The server, importer and account tool share the SQLite file `data/pristontale.db`. The importer applies pending
migrations before importing and keeps accounts and characters on repeat runs; run `import` again after pulling new
migrations or data. SQLite runs in WAL mode, so `-wal` and `-shm` files appear next to the database while it is in use.

```bash
dotnet build tools/PristonTale.DevAccounts/PristonTale.DevAccounts.csproj -c Release -m:1 -warnaserror

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- import \
  --sqlite data/pristontale.db \
  --source assets/GameServer \
  --assets assets

dotnet run --project tools/PristonTale.WorldImport -c Release --no-build -- verify \
  --sqlite data/pristontale.db

dotnet run --project tools/PristonTale.DevAccounts -c Release --no-build -- create-account demo change-me
dotnet run --project Server/PristonTale.Server -c Release --no-build
```

Write the client's effect data into `assets/Effect/Vfx/` (again after updating the game data or an authored effect):

```bash
dotnet run --project tools/PristonTale.VfxImport -c Release --no-build -- import assets
```

Run the client in another terminal and log in with the account you created:

```bash
dotnet run --project Client/PristonTale -c Release --no-build
```

The client connects over TLS to `serverAddress` in `client-settings.json` beside its executable (`localhost` by
default; `host`, `host:port` or `tcp://host:port`, port 10009 when left out). On its first start the server creates a
self-signed certificate at `data/server-certificate.pfx` and logs its SHA-256; a client on the same machine accepts it,
and a remote client (the game, `SmokeClient`, `LoadTest`) pins it with `host:10009?cert=<sha-256>`. Set `Network:Certificate:Path` (and
`Network:Certificate:Password`) to use a certificate of your own. The server applies pending migrations at startup but does not run the
importer, so import first. A migration, catalog or stage-collision load failure stops server startup; without assets,
pass `--Assets:AllowMissingTerrain=true` to run fields without terrain checks.

Configuration:

- Server host settings (`Server/PristonTale.Server/appsettings.json`, environment variables such as `Network__Port`,
  or command-line `--Network:Port=10010`) and the game settings in the `server_settings` table, read again every few
  minutes: [docs/server.md](docs/server.md).
- Client settings (`client-settings.json` beside the client executable, created with every key on the first start):
  [docs/client-settings.md](docs/client-settings.md).

### PostgreSQL

Start `docker compose up -d --wait`, then use `--postgres "<connection string>"` with the importer,
`--conn "<connection string>"` with `PristonTale.DevAccounts`, and set `Database__Provider=Postgres` plus
`ConnectionStrings__Postgres` for the server. `docker compose down` keeps the named volume; `docker compose down -v`
deletes that volume and its data.

## Validate

```bash
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror
dotnet run --project tests/PristonTale.Tests -c Release --no-build
git diff --check
```

The regression runner is a console program; `dotnet test` does not discover it. Benchmarks, asset probes, the smoke
client and the load test are described in [docs/testing.md](docs/testing.md) and [docs/tools.md](docs/tools.md).

## Documentation

- [docs/README.md](docs/README.md): index of the code documentation and which doc to read for each task.
- [AGENTS.md](AGENTS.md): repository working rules for contributors and coding agents.

## AI-assisted development

From commit
[4ec9359](https://github.com/bencz-games/Etervale/commit/4ec935952be896286425ba3de01ff9b454a20472) onward (every class
skill for tiers 1-4, NPC services and the night lighting, dusk haze and HUD fixes), Etervale is developed with the help
of AI tools. The earlier commits were written without them. This note describes how Etervale is developed only, not
MonoGame or the other projects it builds on.

Opinions on AI-assisted development differ, and every one of them is respected.

## License

The project's original source code is licensed under the GNU General Public License, version 3 only
(`GPL-3.0-only`). See [LICENSE](LICENSE).

Public forks and other distributed modified versions must keep the GPLv3 license and make the corresponding source
code available to their recipients. Distributing binaries also requires providing the corresponding source as
specified by GPLv3. Private forks and modifications used only internally do not have to be published. Running a
modified server for remote players, without distributing the server program, does not by itself trigger GPLv3's
source-sharing requirement.

This license does not grant rights to separately supplied game assets, original reference source, or third-party
dependencies. Those remain subject to their own terms.
