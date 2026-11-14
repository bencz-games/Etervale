# Local Fedora test workflow (non-destructive)

The three launchers in `scripts/` operate on this checkout, from any working
directory. The server and account tool share its **existing**
`data/pristontale.db`, and the client uses its `assets/` directory. They do not
copy assets, create placeholder NPCs, run WorldImport or create a new world
database. The server and account tool explicitly select SQLite even if
`PT_PG_CONN` was set.

Prerequisites: Fedora setup from [fedora-setup.md](fedora-setup.md), .NET 10,
MonoGame native runtime, assets, and an **already imported**
`data/pristontale.db`. Back up the database before any manual import.

## Run

In terminal one:

```bash
bash scripts/run-server.sh
```

In terminal two, once the server starts:

```bash
bash scripts/create-account.sh tester
```

The launcher asks twice for the password without echoing it. It passes it to
`PristonTale.DevAccounts create-account --password-stdin` through standard
input instead of process arguments. Do not enable shell command tracing around
credential entry or capture stdin in logs. An existing account is **not
reset**; the underlying tool reports it and leaves its password untouched.

In terminal three:

```bash
bash scripts/run-client.sh
```

To exercise GM-only `/unlocktier`, explicitly grant level 4 to **only a local
test account** after creation (do not grant this automatically to other users):

```bash
dotnet run --project tools/PristonTale.DevAccounts -c Release -- \
  set-gm tester 4 --sqlite data/pristontale.db
```

The client uses the already saved `client-settings.json` beside its binary.
For a local server, its `serverAddress` must be `localhost` or `localhost:10009`.
If it was configured for a remote host, correct that setting yourself; the
launcher intentionally does not rewrite the file. Stop server/client with
Ctrl+C or by closing the client window.

The server applies **schema migrations on startup** as its normal behavior. It
does **not** automatically import/update the world catalog. For this reason,
`run-server.sh` refuses to start when the SQLite file is absent, and
`create-account.sh` refuses to create an empty database. `run-client.sh`
checks that required asset directories exist before starting. Paths are
resolved relative to this checkout, never relative to the shell's current
working directory.

## Verify the original recovery issues before declaring them closed

1. Run `python3 -m unittest discover -s scripts/tests -p 'test_*.py'` and
   `dotnet run --project tests/PristonTale.Tests -c Release`. The .NET tests
   are a console runner; `dotnet test` is not the right command.
2. Run `dotnet run --project tools/PristonTale.WorldImport -c Release --
   export-npc-placements --source assets/GameServer --output
   /tmp/etervale-npc-placements.json`; read the diagnostics. The two
   `BigCuepy.npc` placements in `forever-fall-01` remain known missing sources,
   **not** a reason to create an invented NPC. The character-model
   `tmh-c03.inf` is not an NPC definition.
3. Use `scripts/audit_world_import_refs.py` against your existing SQLite
   database, with its documented command in
   [world-import-ref-audit.md](world-import-ref-audit.md). The previous
   import had 76 intentional `none` shop slots and four substantive unresolved
   placements/drops; an audit does not make them disappear.
4. Log in and test `/unlocktier` with a permitted GM account, then inspect
   **Meteo**, **Chain Lightning** and **Impulsion** in-game. The Meteo timing
   has automated tests; visual/gameplay behavior still requires observation.
5. Check the client log and screen for missing textures (including Ricarten's
   `tem_wall04.bmp`), white materials, animated recovered models, and models
   exceeding the 64-bone renderer limit. Do not claim these fixed until a
   reproducible in-game test passes.

The code and original data remain separate. In particular, the experimentally
recovered `toolsric.NPC` files should not be installed as a workaround for
`BigCuepy.npc`: they are unrelated definitions.
