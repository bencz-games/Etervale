#!/usr/bin/env bash
# Start Etervale's local MonoGame client using its existing client settings.
set -euo pipefail

if (( $# != 0 )); then
  printf 'Usage: bash scripts/run-client.sh\n' >&2
  exit 2
fi

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ ! -d "$root/assets/char" || ! -d "$root/assets/Field" ]]; then
  printf 'Client assets are missing under %s/assets (expected char/ and Field/).\n' "$root" >&2
  exit 1
fi

cd "$root"
printf 'Starting Etervale client (check client-settings.json if it connects to a remote server).\n'
exec dotnet run --project Client/PristonTale -c Release --no-launch-profile
