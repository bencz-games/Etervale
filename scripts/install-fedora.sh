#!/usr/bin/env bash
# Development dependencies for Etervale on Fedora (validated against Fedora 43).
# Run from any directory: bash scripts/install-fedora.sh [--check|--build|--packages-only]
# Does not download proprietary game assets, edit databases or install GPU drivers.
set -euo pipefail

usage() {
  cat <<'HELP'
Usage: bash scripts/install-fedora.sh [OPTION]

  (default)        Install Fedora packages, restore .NET tools and solution packages.
  --build          Additionally compile MonoGame's native runtime, solution and WorldImport.
  --packages-only  Install system packages only (no .NET downloads/build).
  --check          Check existing tools and SDK/audio dependencies; change nothing.
  -h, --help       Show this help.

Use a normal user account with sudo privileges. No root shell is required.
The Etervale game-data archive, GPU/driver and Vulkan ICD are separate requirements.
HELP
}

mode=setup
if (( $# > 1 )); then usage >&2; exit 2; fi
case "${1:-}" in
  '') ;;
  --build) mode=build ;;
  --packages-only) mode=packages ;;
  --check) mode=check ;;
  -h|--help) usage; exit 0 ;;
  *) usage >&2; exit 2 ;;
esac

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd -- "$here/.." && pwd)"

if [[ ! -r /etc/os-release ]]; then
  echo 'Cannot detect Linux distribution (/etc/os-release missing).' >&2
  exit 1
fi
# shellcheck disable=SC1091
source /etc/os-release
if [[ "${ID:-}" != fedora ]]; then
  echo "This installer targets Fedora, found ${PRETTY_NAME:-unknown OS}." >&2
  exit 1
fi
if [[ "${VERSION_ID:-}" != 43 ]]; then
  echo "Note: tested on Fedora 43; running on ${PRETTY_NAME:-Fedora}." >&2
fi

packages=(
  # .NET 10 is required by the solution and its test runner.
  dotnet-sdk-10.0
  # MonoGame MGCB and the forked effect compiler target net8.0.
  dotnet-runtime-8.0
  # Native MonoGame runtime (SDL2 + FAudio, built from pinned sources).
  git cmake make gcc gcc-c++ premake5 pkgconf-pkg-config
  alsa-lib-devel pulseaudio-libs-devel pipewire-devel
  # Vulkan loader and a diagnostic utility; GPU-specific drivers are not installed.
  vulkan-loader vulkan-tools
  # Useful for the project's asset inspection, import diagnostics and patches.
  python3 sqlite unzip ripgrep patch findutils
)

check() {
  local failed=0 cmd pc
  echo "Fedora: ${PRETTY_NAME:-unknown}"
  for cmd in git cmake make gcc g++ premake5 pkg-config dotnet python3 unzip; do
    if command -v "$cmd" >/dev/null 2>&1; then
      printf 'OK       %s\n' "$cmd"
    else
      printf 'MISSING  %s\n' "$cmd"
      failed=1
    fi
  done
  if command -v dotnet >/dev/null 2>&1 &&
     dotnet --list-sdks 2>/dev/null | grep -Eq '^10\.'; then
    echo 'OK       .NET 10 SDK'
  else
    echo 'MISSING  .NET 10 SDK'
    failed=1
  fi
  if command -v dotnet >/dev/null 2>&1 &&
     dotnet --list-runtimes 2>/dev/null | grep -Eq '^Microsoft\.NETCore\.App 8\.'; then
    echo 'OK       .NET 8 runtime (MonoGame MGCB)'
  else
    echo 'MISSING  .NET 8 runtime (MonoGame MGCB)'
    failed=1
  fi
  if command -v pkg-config >/dev/null 2>&1; then
    for pc in alsa libpulse libpipewire-0.3; do
      if pkg-config --exists "$pc"; then
        printf 'OK       pkg-config %s\n' "$pc"
      else
        printf 'MISSING  pkg-config %s\n' "$pc"
        failed=1
      fi
    done
  else
    echo 'MISSING  audio header checks (pkg-config unavailable)'
    failed=1
  fi
  if command -v vulkaninfo >/dev/null 2>&1; then
    echo 'OK       vulkaninfo executable'
  else
    echo 'MISSING  vulkaninfo (vulkan-tools)'
    failed=1
  fi
  echo 'Note: Vulkan 1.1+ GPU/driver and the licensed game data are checked separately.'
  return "$failed"
}

if [[ "$mode" == check ]]; then check; exit $?; fi

if ! command -v dnf >/dev/null 2>&1; then
  echo 'dnf is missing; cannot install Fedora packages.' >&2
  exit 1
fi

# Let dnf handle already-installed packages; sudo is only used for system installation.
echo 'Installing Fedora build and diagnostic dependencies:'
printf '  %s\n' "${packages[@]}"
if (( EUID == 0 )); then
  dnf install "${packages[@]}"
else
  if ! command -v sudo >/dev/null 2>&1; then
    echo 'sudo is needed for the package installation.' >&2
    exit 1
  fi
  sudo dnf install "${packages[@]}"
fi

check
if [[ "$mode" == packages ]]; then
  echo 'System dependencies installed. Restore .NET tools later using: dotnet tool restore'
  exit 0
fi

if [[ ! -f "$root/PristonTale.sln" || ! -f "$root/.config/dotnet-tools.json" ]]; then
  echo "Not an Etervale source checkout: $root" >&2
  exit 1
fi
cd "$root"
echo 'Restoring repository-local .NET tools (MonoGame MGCB)...'
dotnet tool restore
echo 'Restoring .NET solution packages...'
dotnet restore PristonTale.sln -warnaserror

if [[ "$mode" == build ]]; then
  echo 'Building MonoGame native runtime (downloads pinned SDL2/FAudio sources)...'
  bash monogame/runtime/build.sh
  echo 'Building solution...'
  dotnet build PristonTale.sln -c Release -m:1 -warnaserror --no-restore
  echo 'Building WorldImport...'
  dotnet build tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj -c Release -m:1 -warnaserror
fi

echo 'Fedora setup complete.'
echo 'You still need the game data in assets/ and a Vulkan-capable GPU/driver.'
echo 'See docs/fedora-setup.md before running the importer and game.'
