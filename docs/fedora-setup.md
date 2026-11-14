# Fedora development setup (Etervale)

This setup reproduces the working **Fedora 43** environment used to build the Etervale
server, MonoGame Vulkan client and WorldImport on 2026-11-08. It is intended to be
rerun safely: DNF leaves packages that are already installed alone, and `dotnet`
restores its pinned dependencies. The script never alters the game database.

## Install

From a Git checkout of Etervale:

```bash
bash scripts/install-fedora.sh             # packages + .NET tool/package restore
bash scripts/install-fedora.sh --check     # read-only diagnostics
bash scripts/install-fedora.sh --build     # additionally build native runtime and C# projects
```

`--packages-only` skips .NET downloads; `--help` lists all modes. Run under a normal
user account with `sudo`, **not** through `sudo bash`: only DNF needs elevated
permissions. The script does not enable third-party repositories, configure a GPU
vendor driver, download game data, create accounts or import a world.

## What is installed and why

| Dependency | Purpose |
| --- | --- |
| `dotnet-sdk-10.0` | .NET 10 projects, compiler and test runner |
| `dotnet-runtime-8.0` | MonoGame MGCB and effect-processing tools target .NET 8 |
| `git`, `cmake`, `make`, `gcc`, `gcc-c++`, `premake5`, `pkgconf-pkg-config` | Build the vendored MonoGame native runtime, SDL2 and FAudio |
| `alsa-lib-devel`, `pulseaudio-libs-devel`, `pipewire-devel` | SDL2 audio backends; the runtime build explicitly checks all three |
| `vulkan-loader`, `vulkan-tools` | Vulkan dynamic loader and `vulkaninfo` diagnostic |
| `python3`, `sqlite`, `unzip`, `ripgrep`, `patch`, `findutils` | Existing Etervale inspection, unpacking and development workflows |

The `dotnet tool restore` step retrieves the repository-pinned MonoGame content
builder (`dotnet-mgcb` 3.8.6) and related editor tools. `dotnet restore` obtains
the solution's NuGet packages. `--build` additionally runs
`monogame/runtime/build.sh`, which fetches the **pinned** SDL2 and FAudio
revisions, builds `libmgruntime.so`, and then compiles the C# solution and the
standalone WorldImport tool. Neither Wine nor the Vulkan SDK is required for
this project's native client build.

A functioning GPU driver with **Vulkan 1.1+** is required separately. A Vulkan
loader alone does not provide a GPU implementation; install the appropriate
driver for the machine (Mesa for supported AMD/Intel hardware, or the relevant
NVIDIA driver). Use `vulkaninfo --summary` to diagnose the driver. Headless
servers need no GPU for the server and importer workflows.

## Assets, database and running

The original Priston Tale game data is **not in Git** and must be provided
separately under `assets/`; see the root [README](../README.md#requirements)
for the referenced archive and expected layout. `assets/GameServer/` holds the
authored world configuration, and `assets/char/` holds models and INX files.
These files may be subject to separate redistribution rights.

With assets present and the native runtime built:

```bash
# Populate / update the world catalog (stop the server and back up any existing DB first):
dotnet run --project tools/PristonTale.WorldImport -c Release -- import \
  --sqlite data/pristontale.db --source assets/GameServer --assets assets

# World catalog diagnostics (does not import into the DB):
dotnet run --project tools/PristonTale.WorldImport -c Release -- export-npc-motions \
  --source assets/GameServer --assets assets \
  --output /tmp/etervale-npc-motions.json

# Start server and client in separate terminals:
dotnet run --project Server/PristonTale.Server -c Release --no-build
dotnet run --project Client/PristonTale -c Release --no-build
```

For the regression suite, use `dotnet run --project tests/PristonTale.Tests -c Release`;
this is a console test runner, **not** a `dotnet test`-discovered suite.

## Recovered Fedora 43 setup sequence

The initial local installation hit three missing-dependency errors, in this order:

1. `monogame/runtime/build.sh` required the ALSA, PulseAudio and PipeWire
   development headers. Installing their Fedora `-devel` packages fixed that.
2. SDL2/FAudio then required `cmake`, which was not installed.
3. The next native build required `premake5`; Fedora DNF resolved that
   command to its `premake` package.

Afterwards `monogame/runtime/build.sh` produced
`monogame/runtime/bin/linux-x64/libmgruntime.so`, `dotnet tool restore` retrieved
the MonoGame 3.8.6 tools, and the .NET 10 solution, WorldImport and tests built.
This repository script consolidates these manual steps for future Fedora installs.

The setup command is **not** a record of every application installed on the
machine; it covers the dependencies established by the project build and the
Fedora troubleshooting log. Game assets, developer credentials and local SQLite
databases should remain outside commits.
