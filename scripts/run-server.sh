#!/usr/bin/env bash
# Start Etervale's local SQLite server. Does not import or reset world data.
set -euo pipefail

if (( $# != 0 )); then
  printf 'Usage: bash scripts/run-server.sh\n' >&2
  exit 2
fi

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
db="$root/data/pristontale.db"
if [[ ! -f "$db" ]]; then
  printf 'Database not found: %s\nRun WorldImport only after backing up and explicitly checking your source data.\n' "$db" >&2
  exit 1
fi
if [[ ! -d "$root/assets/Field" ]]; then
  printf 'Game assets not found: %s\n' "$root/assets/Field" >&2
  exit 1
fi

cd "$root"
export Database__Provider=Sqlite
export Database__SqlitePath="$db"
printf 'Starting Etervale server with existing SQLite: %s\n' "$db"
exec dotnet run --project Server/PristonTale.Server -c Release --no-launch-profile
