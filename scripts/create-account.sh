#!/usr/bin/env bash
# Create a local dev account in the same existing SQLite database as run-server.sh.
# The password is sent on stdin, never as a command-line argument.
set -euo pipefail
set +x

if (( $# != 1 )) || [[ -z "$1" || "$1" == -* || "$1" =~ [[:space:]] ]]; then
  printf 'Usage: bash scripts/create-account.sh <username>\n' >&2
  exit 2
fi

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
db="$root/data/pristontale.db"
if [[ ! -f "$db" ]]; then
  printf 'Database not found: %s\nAccount creation will not initialize or import a new world.\n' "$db" >&2
  exit 1
fi

password=''
confirmation=''
trap 'unset password confirmation' EXIT
if ! IFS= read -r -s -p 'Password: ' password; then
  printf '\nFailed to read password.\n' >&2
  exit 1
fi
printf '\n' >&2
if ! IFS= read -r -s -p 'Repeat password: ' confirmation; then
  printf '\nFailed to read password confirmation.\n' >&2
  exit 1
fi
printf '\n' >&2
if [[ -z "$password" || "$password" != "$confirmation" ]]; then
  printf 'Passwords are empty or do not match; account was not created.\n' >&2
  exit 1
fi
unset confirmation

cd "$root"
printf '%s\n' "$password" | dotnet run --project tools/PristonTale.DevAccounts -c Release --no-launch-profile -- \
  create-account "$1" --password-stdin --sqlite "$db"
