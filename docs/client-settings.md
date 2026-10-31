# Client settings

Every field of the client's `client-settings.json`: defaults, valid values, validation and fallback, quality presets,
the in-game Settings panel, and how a change reaches the display, the renderer and the audio. Code:
`Client/PristonTale.Engine3D/Rendering/ClientSettings.cs` and `IO/ClientSettingsStore.cs`, applied by
`Client/PristonTale/Display/`.

## Read this when

- Adding, renaming or removing a client option.
- A user's settings file is ignored ("using defaults") or a value silently resets.
- Changing a quality preset or the Settings panel rows.
- Investigating why a display change reverted after 15 seconds.
- Running a benchmark or capture with a specific settings file (`--settings`, `--benchmark-render`).

## Code map

| Path | Responsibility |
|---|---|
| [`ClientSettings.cs`](../Client/PristonTale.Engine3D/Rendering/ClientSettings.cs) | The record: fields and defaults, `Validate`, `WithQuality` (presets), `HasSameRendering`, `HasSameDisplay`, `FrameLimit`. |
| `Client/PristonTale.Engine3D/Rendering/GraphicsOptions.cs` | Enums `GraphicsQuality`, `AntiAliasingMode`, `TonemapperMode`, `BloomQuality`, `ShadowFilterMode` (serialized by name). |
| `Client/PristonTale.Engine3D/Rendering/DistanceFogMode.cs` | `DistanceFogMode` and its converter (accepts older booleans). |
| `Client/PristonTale.Engine3D/Rendering/PristonFrameContext.cs` | `PristonDebugView`; `PristonDistanceFog.For` (fog mode to fog parameters). |
| `Client/PristonTale.Engine3D/IO/ClientSettingsStore.cs` | Load, version check, read-only protection of unreadable files, asynchronous atomic save. |
| `Client/PristonTale.Engine3D/Rendering/PresentationSettings.cs` | Shared state between the UI and the display controller: `Requested`, confirmation flags, `ApplyRendering` and the `RenderingChanged` event. |
| `Client/PristonTale/Display/PresentationController.cs` | Applies requested settings at the frame boundary, display preview and 15-second confirmation, rollback, frame pacing, save on accept. |
| `Client/PristonTale/Display/DisplayController.cs` | Window mode, back-buffer size and vertical sync through the Vulkan platform. |
| `Client/PristonTale.Engine3D/Rendering/WindowPlacement.cs` | Where the windowed window goes: centred on the primary monitor, or its saved position while the title bar stays on screen (`DisplayController` applies it and maximizes a window that was). |
| `Client/PristonTale/Display/DisplayValidationProbe.cs` | `--validate-display REPORT.json`: cycles display configurations and checks pacing. |
| `Client/PristonTale.Gameplay/UI/Widgets/HudSettingsPanel.cs` | The in-game Settings panel (system menu, entry Settings). |
| `Client/PristonTale.Gameplay/UI/DisplayConfirmation.cs` | Confirm (Enter or button) / revert (Escape or button) prompt of a display change. |
| `Client/PristonTale.Engine3D/Rendering/PristonRenderer.cs` (`ApplyQuality`) | Reads the rendering fields when `RenderingChanged` fires. |
| `tests/PristonTale.Tests/ClientSettingsTests.cs`, `PhotorealTests.cs` | Validation, older fog booleans, persistence, invalid documents, pacing; preset contents. |

## File location and lifecycle

- Path: `client-settings.json` beside the client executable (`AppContext.BaseDirectory`); the game never stores
  settings in a per-user profile folder. `PristonTale --settings PATH` uses another file (`Client/PristonTale/Program.cs`).
  A missing file is created with every key at its default.
- JSON: camelCase property names, case-sensitive, indented on save. Enums are written and read by name.
- Load (`ClientSettingsStore.Load`, on the main thread before the window is created):
  - no file: defaults, and the defaults are queued for saving;
  - `version` other than 1, malformed JSON, `null`, or **any unknown property** (`UnmappedMemberHandling.Disallow`, so a
    typo such as `maxFPS` counts): defaults are used, the error is logged (the panel says "Settings need
    attention"), and the store turns read-only so the user's file is never overwritten in that session;
  - otherwise the document is deserialized (missing fields take their defaults) and `Validate` replaces each invalid
    field with its default, logging `[Settings] Invalid presentation values replaced with defaults.`
- Save: only when a change is accepted (`PresentationController.Accept`). A bounded channel (capacity 1, drop oldest)
  feeds a background writer that writes a temporary file and moves it over the target; the latest snapshot wins and is
  drained on shutdown. Save errors are logged and shown, never thrown into the game loop.
- A file is not rewritten just because it lacks fields or holds invalid values; it is rewritten at the next accepted
  change.

## Fields

"Panel" marks fields the in-game Settings panel edits; every other field is edited in the file. "Display" fields
(`HasSameDisplay`) recreate the swap chain and need confirmation; "Rendering" fields (`HasSameRendering`) reach the
renderer through `PresentationSettings.ApplyRendering` and apply at once.

### Display, audio, pacing and gameplay

| Field | Type, default | Valid values (else default) | Panel | Effect |
|---|---|---|---|---|
| `version` | int, `1` | must be 1 | | Any other value: defaults and a read-only store. |
| `monitor` | int, `-1` | >= -1 | yes | -1 current. The Vulkan platform offers the primary monitor only; a value above 0 logs a warning and uses the primary monitor. Display. |
| `windowWidth`, `windowHeight` | int, `1280`, `720` | 800-16384, 600-16384 | yes (resolution) | Windowed size (the window is resizable; a manual resize is accepted and saved, except while maximized: the restored size is kept). Fullscreen picks the closest primary-monitor mode; borderless uses the desktop size. Display. |
| `windowX`, `windowY`, `windowMaximized` | int or null, null; bool, `false` | any | no (saved on exit) | Where the windowed window was left: its client area's top-left in desktop coordinates (MonoGame's `Window.Position`) while not maximized, and whether it was maximized. The first windowed apply restores the position while the title bar lands on the primary monitor (otherwise, or without a position, the window opens centred on it with its title bar on screen), then maximizes the window if it was. Later applies centre a window that no longer fits. A platform that places windows itself (a Wayland session) may ignore the position. Not display fields: moving or maximizing the window applies nothing. |
| `windowMode` | `Windowed` (default), `Borderless`, `Fullscreen` | defined enum | yes | Fullscreen is an exclusive mode switch. Display. |
| `refreshRate` | int, `0` | 0-1000 | yes (fullscreen only) | 0 desktop frequency. The platform switches at the desktop frequency only; a nonzero value logs a warning. Display. |
| `verticalSync` | bool, `false` | | yes | Applied without a display reset. |
| `musicEnabled`, `soundEnabled` | bool, `true`, `true` | | yes (BGM, Audio) | Read by `PristonAudioLibrary.ApplySettings` every update; the login and character screens ignore them. |
| `maxFps` | int, `0` | 0 or 15-1000 | yes: 0, 30, 60, 90, 120, 144, 165, 240, 360 | Foreground frame limit; 0 unlimited. Paced by `PresentationController.Pace` (sleeps in 1-4 ms steps). |
| `backgroundMaxFps` | int, `30` | 0 or 15-1000 | yes (same list) | Limit while the window is inactive: `min(maxFps, backgroundMaxFps)`, or `backgroundMaxFps` when `maxFps` is 0; 0 means "use `maxFps`". |
| `uiScale` | float, `1` | 0.75-1.5 | | Validated and stored only; the in-game HUD always uses the largest integer scale that fits. |
| `classicTargetHealthBar` | bool, `false` | | yes (Target HP) | `false`: the target frame always shows the HP bar with level and life percentage. `true`: the bar, without numbers, appears only after you attack a monster at full life. Either way the frame shows a live 3D portrait of the target's head, its name and, for monsters, the race icon. |
| `classicNamePlates` | bool, `false` | | yes (Name plates) | `false`: the name boxes over monsters and NPCs (rounded, in the speech bubble's colours) hold smaller text, a monster's life bar shows once it was clicked or attacked, and NPCs get no hover outline. `true`: the same box, the HUD text size, the bar while the cursor is on the monster, and a green NPC outline. |
| `levelGuide` | bool, `true` | | yes (the guide window's On/Off) | On reaching a level the game guide has a page for, a blinking notice offers its summary. |
| `realisticSky` | bool, `true` | | yes (Sky) | The physical sky (sun, moon, stars, aurora, rainbow, clouds) instead of the original's painted domes ([rendering.md](rendering.md#realistic-sky)). |
| `cloudQuality` | `Off`, `Low`, `Medium` (default), `High` | others: the default | yes (Clouds) | The realistic sky's volumetric clouds and their shadows: off, or their hemisphere rendered at 1024 x 256, 1536 x 384 or 2048 x 512; presets Low and Medium take `Low`, High `Medium`, Ultra `High`. |
| `weatherEnabled` | bool, `true` | | yes (Weather) | The weather the server sends: clouds, rain, splashes, lightning, the rain and thunder sounds ([client.md](client.md#weather)). `false` keeps the sky clear and the field ambience on, as the original's rain switch did. |

### Rendering

| Field | Type, default | Valid values (else default) | Panel | Effect |
|---|---|---|---|---|
| `quality` | `High` (default), `Low`, `Medium`, `Ultra`, `Custom` | defined enum | yes (Graphics quality) | Label of the preset the fields came from. The panel applies a preset with `WithQuality`; editing any preset field in the panel sets `Custom`. Loading a file never applies a preset. |
| `antiAliasing` | `Taa` (default), `Off`, `Fxaa`, `Smaa`, `Msaa` | defined enum | yes | `Taa`: jittered projection, motion vectors (skinned actors from their previous pose), history resolve. `Msaa`: multisampled HDR scene, no depth pre-pass. `Fxaa`/`Smaa`: 1x filter on the 8-bit display image. |
| `multiSampleCount` | int, `4` | 0, 2, 4, 8 | label only | Samples of the HDR scene with `antiAliasing` `Msaa` only; the renderer clamps to 2-4 (`PristonRenderer.ApplyQuality`). Also a Display field: a change goes through confirmation. |
| `autoExposure` | bool, `true` | | | GPU histogram and adaptation; `false` holds the light model's incident reading. |
| `exposureCompensation` | float stops, `0` | finite, -3 to 3 | | Added to the automatic or fixed exposure. |
| `tonemapper` | `PbrNeutral` (default), `AgX`, `Aces` | defined enum | | Display transform of the final pass. |
| `bloom` | `High` (default), `Low`, `Off` | defined enum | | Bloom pyramid mixed at 5 % (High) or 3 % (Low). |
| `colorGrading` | bool, `true` | | | Applies the 3D LUT grade after the display transform. |
| `colorGradingLut` | string, `""` | `null` becomes `""` | | Path of a `.cube` file (any size, display-referred sRGB input) replacing the built-in grade. Relative paths resolve against the process working directory; an unreadable or malformed file logs a warning and keeps the built-in grade. |
| `sharpening` | float, `0.35` | finite, 0-1 | | Contrast-adaptive sharpening in the final pass. |
| `localLightLimit` | int, `32` | 8, 16, 32 | | Local lights shaded per pixel: transient combat lights first (at most 8), then the stage lamps nearest the camera focus. |
| `shadowsEnabled` | bool, `true` | | yes (Shadows) | All shadows: sun cascades and lamp cube shadows. |
| `shadowMapSize` | int, `4096` | 1024, 2048, 4096, 8192 | yes (Sun shadow) | Size of the sun's cascade atlas (a 32-bit float depth surface). If allocation fails the renderer halves the size down to 1024. |
| `shadowCascades` | int, `4` | 1-4 | | Stabilized cascades over 9000 units of view depth; from 2 cascades on the atlas is a 2x2 grid. |
| `shadowFilter` | `Pcss` (default), `Pcf` | defined enum | | PCSS widens the penumbra with the blocker distance; the moon always uses PCF. |
| `shadowUpdateRate` | int Hz, `60` | 0, 30, 60, 120 | yes (Shadow animation: 30, 60, 120, every frame) | Sun cascade refit and refresh rate; 0 every frame. Far cascades refresh less often; at night the rate is halved (at least 30 Hz). The two near cascades redraw every frame regardless, so characters cast from their current pose. |
| `lampShadowCount` | int, `4` | 0, 2, 4 | | Stage lamps nearest the focus whose cube shadows actors and animated props cast, while the lamps are lit (sun below about 4 degrees). |
| `lampShadowTileSize` | int, `512` | 256, 512, 1024 | | Cube-face size of each lamp shadow. |
| `ambientOcclusion` | bool, `true` | | | Half-resolution GTAO from the depth pre-pass (not with MSAA). |
| `contactShadows` | bool, `true` | | | Screen-space contact shadows toward the sun (by day only, not by moonlight; not with MSAA). |
| `imageBasedLighting` | bool, `true` | | | Sky-dome irradiance (spherical harmonics) and prefiltered reflections, refreshed every 250 ms; `false`: hemispherical sky/ground ambient. |
| `screenSpaceReflections` | bool, `false` | | | Screen-space reflections of smooth surfaces (water, ice) over the sky reflection; needs the depth pre-pass; skipped when no smooth surface is visible. |
| `volumetricLight` | bool, `true` | | | Sunlight scattered by a thin height fog, with shadow shafts, at quarter resolution up to the distance-fog start; by day only, needs the depth pre-pass and sun shadows. |
| `distanceFog` | `Atmospheric` (default), `Classic`, `Off`; older `true`/`false` | defined enum | | `Atmospheric`: from 2208 units the surface fades toward the captured sky, at most 85 %, with forward scattering toward the sun. `Classic` is accepted for old files and draws exactly as `Atmospheric`. `true` reads as `Atmospheric`, `false` as `Off`; saving writes the name. |
| `calibrationSpheres` | bool, `false` | | | Look development: an 18 % grey and a chrome sphere beside the camera focus. |
| `debugView` | `Shaded` (default), `Albedo`, `VertexColour`, `Normals`, `Lighting`, `Overdraw`, `ExposureFalseColour`, `Roughness`, `Metallic`, `AmbientOcclusion`, `Specular` | defined enum | | Development views. `AmbientOcclusion`: occlusion in red, contact shadows in green. `Specular`: specular light alone. `ExposureFalseColour`: EV100 false colour in the final pass. `Overdraw`: additive, no sky, pre-pass or TAA. |

`Validate` checks enums with `Enum.IsDefined`, numbers against the lists above and floats with `float.IsFinite`. The
booleans have no validation. An unknown enum name is a JSON error (whole file ignored), not a per-field fallback.

## In-game Settings panel

`HudSettingsPanel` rows, in order: Window, Monitor, Resolution, Frequency, VSync, BGM, Audio, Maximum FPS, Background
FPS, Anti-aliasing, Sun shadow, Graphics quality, Shadow animation, Shadows, Target HP, Name plates, Weather, Sky,
Clouds. Click cycles
forward, Shift+click backward. Resolution offers only the sizes the primary monitor reports (at least 800x600,
`HudSettingsPanel.Resolutions`): any of them in fullscreen, those that fit on the desktop below the window's title bar
when windowed, and none when borderless (it covers the desktop). Graphics quality cycles Low, Medium, High, Ultra;
from `Custom` it goes to Low (forward) or Ultra (backward). Anti-aliasing, Sun shadow, Shadow animation and Shadows set
`quality` to `Custom`. Rows are inert while a display change awaits confirmation.

## Apply and confirm

`PresentationController.UpdateBoundary` runs once per frame on the main thread:

1. If `Requested` differs from the applied settings and no confirmation is pending, it validates them.
2. Display fields changed (`HasSameDisplay` false): `DisplayController.Apply` resizes or switches the window, then a
   15-second confirmation starts. Enter or the confirm button accepts; Escape, the revert button or the timeout
   restores the last accepted settings.
3. Otherwise the change is accepted immediately and saved.
4. Rendering fields go to `PresentationSettings.ApplyRendering`, which fires `RenderingChanged` only when
   `HasSameRendering` is false; `PristonRenderer.ApplyQuality` then reallocates the shadow atlas if its size changed,
   reconfigures cascades and lamp shadows and copies the post options.
5. A failing apply (`InvalidOperationException`, `ArgumentException`, `NoSuitableGraphicsDeviceException`) rolls back to
   the accepted settings (defaults on the first apply) and shows the error.

The saved display applies at start without confirmation: it was confirmed when it was chosen. A first apply that fails
falls back to the default windowed settings.

## Quality presets

`WithQuality` sets only the rendering fields below; display, audio, gameplay, `multiSampleCount`, `imageBasedLighting`,
`calibrationSpheres`, `distanceFog`, `debugView`, `exposureCompensation`, `tonemapper` and `colorGradingLut` keep their
values. The defaults equal the High preset (checked by `PhotorealTests`).

| Field | Low | Medium | High (default) | Ultra |
|---|---|---|---|---|
| `antiAliasing` | `Off` | `Smaa` | `Taa` | `Taa` |
| `sharpening` | 0 | 0.2 | 0.35 | 0.35 |
| `shadowMapSize` | 2048 | 2048 | 4096 | 4096 |
| `shadowCascades`, `shadowFilter` | 2, `Pcf` | 3, `Pcf` | 4, `Pcss` | 4, `Pcss` |
| `shadowUpdateRate` | 30 | 60 | 60 | 0 (every frame) |
| `ambientOcclusion`, `contactShadows` | off, off | on, off | on, on | on, on |
| `lampShadowCount` x `lampShadowTileSize` | 0 x 512 | 2 x 512 | 4 x 512 | 4 x 1024 |
| `bloom` | `Off` | `High` | `High` | `High` |
| `localLightLimit` | 8 | 16 | 32 | 32 |
| `volumetricLight` | off | off | on | on |
| `screenSpaceReflections` | off | off | off | on |
| `shadowsEnabled`, `autoExposure`, `colorGrading` | on | on | on | on |

Reference GPU frame totals, measured with `--benchmark-render` at 1920x1080 on a GTX 1050 Ti in Ricarten at noon and in
the lamp-lit street at 21:00, cascades refreshed at 60 Hz: Low about 2.6 ms (day) and 3.4 ms (night; FXAA would add
about 0.7 ms), Medium about 3.6 ms and 4.8 ms, High about 5.7 ms and 5.7 ms; Ultra costs High plus the every-frame
cascades and the reflections. At 60 frames per second or less every frame refreshes the cascades (about 1 ms more on
High). Re-measure after renderer changes; see [testing.md](testing.md) for the benchmark arguments.

## Debug keys (not saved)

In the gameplay screen (`GameplayScreen.UpdateDebugKeys`): `Ctrl+F3` toggles the render statistics in the window title,
`Ctrl+F5` toggles the renderer's shadows, `Ctrl+F10` cycles `debugView`, `,` and `.` move the time of day by one hour,
`P` pauses or resumes the day cycle. These change the live renderer only; the file keeps its values.

## How to

- **Add a setting:** add an `init` property with its default to `ClientSettings` (a comment only if its meaning is not
  obvious); add its range to `Validate`; if the renderer reads it, add it to `HasSameRendering` and read it in
  `PristonRenderer.ApplyQuality` (or `PristonPostSettings.From` for post options); if it changes the swap chain, add it
  to `HasSameDisplay` and `DisplayController.Apply`; decide whether `WithQuality` sets it per preset; add a panel row in
  `HudSettingsPanel` (`_labels` length, `Click`, `RefreshLabels`, and the box height `SizeH` if needed); extend
  `ClientSettingsTests` (validation, round trip) and `PhotorealTests` for presets; update this document. Existing files
  without the field load with the default, so no version bump is needed.
- **Rename or remove a field:** existing files with the old name then fail to load (unknown property) and the user gets
  defaults. Keep accepting the old shape with a converter (as `DistanceFogModeJsonConverter` does for booleans) or bump
  `ClientSettings.CurrentVersion` with a migration in `ClientSettingsStore.Load`.
- **Change a preset:** edit `ClientSettings.WithQuality`, keep the defaults equal to High (or update `PhotorealTests`),
  re-measure with `--benchmark-render` and update the preset table here.

## Pitfalls

- One unknown or misspelled property makes the whole file unusable for that session (defaults, read-only). Field names
  are case-sensitive camelCase.
- Editing `quality` in the file has no effect on the other fields; presets apply only through the panel.
- `multiSampleCount` is ignored unless `antiAliasing` is `Msaa`, and 8 runs as 4; yet changing it triggers the display
  confirmation because it belongs to `HasSameDisplay`.
- MSAA disables the depth pre-pass, so ambient occlusion, contact shadows, screen-space reflections, volumetric light,
  soft particles and light-tile depth bounds are all off with `Msaa`.
- `Classic` distance fog is not a separate look: `PristonDistanceFog.For` maps it to `Atmospheric`.
- A changed `colorGradingLut` is read and sampled on the thread pool (`PristonPostProcessor.EnsureLut`); the previous
  grade stays on screen until the new LUT is uploaded, a frame or more later.
- `ClientSettings` lives in Engine3D but holds gameplay (`classicTargetHealthBar`) and audio fields; keep the record
  free of Gameplay types so Engine3D does not depend on Gameplay.

## Related

- [rendering.md](rendering.md): what each rendering option switches in the frame graph.
- [shaders.md](shaders.md): the passes behind the options.
- [client.md](client.md): screens, HUD and the system menu.
- [testing.md](testing.md): `--benchmark-render`, `--validate-display` and the settings tests.
