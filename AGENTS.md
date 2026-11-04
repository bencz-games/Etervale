# Agent instructions

Applies throughout this repository. Follow the user's explicit instructions first.

## Understand before changing

- Read the code you will change, what calls it and what it calls, and its doc in `docs/`, before editing. Know how the
  feature works end to end (client, server, protocol, persistence, shaders) before deciding where a change belongs.
- Never write from an assumption about an API, format, rule or behavior: open it and check. If the task needs more
  than you have read, read more or narrow the task; never guess.
- Look for an existing helper, rule or table before adding one; extend it instead of writing a second copy.

## Working rules

- Start at [docs/README.md](docs/README.md): it routes each task to the doc that maps the code involved. Read that doc
  before searching the source.
- Keep documentation current when changing behavior, contracts, formats, settings, or commands: a change is done only
  when its docs match it, so update the matching doc in `docs/` in the same change. A doc that contradicts the code is
  a defect: fix it when you find it, or report it. Docs are in English and describe the current state only, never a
  changelog. Use [README.md](README.md) only for setup and run instructions; keep this file limited to agent guidance.
- Treat restore, build, and analyzer warnings as errors. Build in Release with `-warnaserror`; keep
  `TreatWarningsAsErrors=true`. Fix causes instead of suppressing diagnostics.
- Write readable C#: lines at most 240 characters, one statement per line, clear names, and braces on separate lines for
  control flow. Never use the C# `dynamic` type or implicitly dynamic Dapper queries.
- Keep gameplay `Update`/`Draw` and the authoritative tick free of blocking network, database, disk, task, and long lock
  waits. Prepare work outside the loop; apply GPU work on the correct thread.
- Add comments only for non-obvious invariants, external contracts, legacy formats, or formulas. Avoid XML comments,
  section banners, implementation history, and commented-out code.
- Verify claims against the current implementation. Preserve the user's changes and stay within the task.
  Inspect `git status` and `git diff`; do not run `git add`, `git commit`, or `git push`.
- When the owner asks for a commit, never add a `Co-Authored-By` trailer or any other co-author or attribution line to
  the message, whatever a tool's defaults suggest.
- Use the user's language when reporting work. State the changes, checks performed, and remaining limitations.
- Hard rule: keep the agent's temporary folder clean, `claude` under the system temporary directory
  (`%LOCALAPPDATA%\Temp\claude` on Windows, `$TMPDIR/claude` on macOS, `/tmp/claude` on Linux). Captures, probe output,
  scratch scripts and file copies live there only while a step needs them; delete them as soon as it is done, and
  leave nothing behind at the end of a task.
  - List what is there and decide file by file. Delete a file only when the work it served is finished and its result
    is kept where it belongs (applied and committed, or recorded in the repository).
  - Anything an ongoing task still uses stays until that task ends; a file worth keeping beyond it moves into the
    repository first.
- Hard rule: never read environment variables in the code (client, server, engine, tools or tests) to configure
  behaviour, tune values or switch features. Settings belong in the settings files beside the executable, command-line
  arguments or code constants. A variable may be read only in a throwaway local experiment, and that code is removed
  right after the experiment, before anything else is done; it never reaches a commit.

## Platforms

Etervale is multi-platform: the client, the server and the tools must build and run on Windows, Linux and macOS.

- Build paths with `Path.Combine` and resolve asset references through `AssetFiles` (case-insensitive on case-sensitive
  file systems); never hard-code separators, drive letters or a file name's case.
- No OS-only APIs in product code (registry, Win32 P/Invoke, COM, WinForms/WPF). A platform that needs its own code gets
  it behind one interface, implemented for all three.
- Files the game writes live beside the executable, never in user-profile folders (AppData, `~/Library`, `~/.config`).
- Graphics are Vulkan through the MonoGame fork in `monogame/`; macOS runs it on MoltenVK. Check that MoltenVK supports
  a Vulkan feature or shader capability before relying on it.
- Every build or tool script exists for each platform (`.sh` for Linux and macOS, `.ps1` for Windows), or is a `dotnet`
  command.
- Text in the repository is UTF-8 with LF line endings (`.gitattributes` normalizes it); read the original CP949 files
  through `Cp949`.
- When reporting, say which platforms were built and run; a change to platform-specific code (native runtime, scripts,
  file access) names the platforms not verified.

## Code organization and quality

- One implementation per rule: a formula, table or check lives in one place (gameplay rules in Simulation) and every
  caller uses it. Search before adding a helper; extend the one that exists instead of writing a second copy.
- A type that grows past one responsibility is split into topic partials or classes (`GameScene.*`,
  `PristonRenderer.*`, `GameplayHud.*`). Rule tables are data (typed tables or JSON), not switch chains repeated
  across files.
- Small immutable values are readonly record structs; equality-compared data are records; classes are for identity and
  lifetime. Use the shared codes (Contracts: `MotionCode`, `EntityKind`, ...) and named constants, never raw numbers.
- `Update`, `Draw` and the server tick allocate nothing per frame (no LINQ, closures, arrays or string formatting
  there): cache, reuse or use spans. A performance change is measured with the render benchmark before and after.
- MonoGame first: use what MonoGame offers; extend the fork in `monogame/` only for what it lacks, with its issue
  document, interface version and regenerated patch.
- Every behavior change gets a regression case on production code (`WorldPump` for world operations,
  `Assert.Throws<T>` for refusals, `ScriptedRng`/`SeededSimRng` for rolls). Visual changes are compared with captures
  against a baseline of the same build, after measuring the run-to-run noise.
- Pull requests follow these rules before merging; a reviewer adapts one that does not, and says what changed.
- Before a task is called done, its whole diff is reviewed against these rules, and the findings are fixed before the
  commit. The review looks for:
  - the same rule, formula, constant, geometry or lookup written twice, within the diff or alongside code that already
    exists (search the repository for each new helper, constant and loop);
  - raw numbers that repeat a named constant;
  - allocations in `Update`, `Draw` or the tick;
  - members the change left unused;
  - comments that restate the code;
  - code placed outside its domain.

  A large change also gets a fresh read-only review that is not anchored on the author's reasoning.

## Architecture and data

- Keep shared gameplay rules in `Shared/PristonTale.Simulation` and online authority in
  `Shared/PristonTale.Application`. Keep MonoGame in the client, transport mapping at the network edges, and SQL in
  persistence.
- Organize code and data by domain (a class, a mechanic, a topic), never by tier (`FirstTier`, `SecondTier`) or by
  faction.
- The wire protocol is Etervale's own: never limit a value to the original game's struct sizes (experience and entity
  ids are 64-bit, life is 32-bit).
- The original game is a reference, not a specification to copy: Etervale may do better. Judge visual work by its
  quality and validate it with captures.
- Use `AssetFiles` (`Shared/PristonTale.Assets`) for original asset paths. Check version and encoding before extracting
  behavior from local reference material.
- Never cite the original source in this repository: no file names, paths, line numbers, function or struct names
  from it in code, comments, identifiers, messages, tests or documentation, and no mention of where local reference
  material is kept. Describe behavior, rules and binary formats in Etervale's own terms.
- Keep database migrations structural. Import authored world data through `tools/PristonTale.WorldImport`, preserving
  duplicate definitions and unresolved references. Check importer, repository, runtime catalog, and both providers when
  data contracts change.

## Verification

Use the Release build and regression commands in [README.md](README.md). Build changed standalone tools separately. The
regression runner is a console app, not a `dotnet test` suite. Use disposable databases for import and migration tests.
For documentation-only changes, check facts, links, line length, and the diff; avoid unrelated tests.
