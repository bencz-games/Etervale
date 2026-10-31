# Rendering (Engine3D)

`Client/PristonTale.Engine3D/`: the original model, stage, animation and camera-path readers, asset path resolution,
client collision queries, the HDR renderer (scene, lighting, shadows, sky, post chain) and its diagnostics. Shader
sources are in [shaders.md](shaders.md); option fields in [client-settings.md](client-settings.md).

## Read this when

- Changing pass order, a render target, the light model, exposure, shadows, fog, IBL, SSR, volumetric light or AA.
- A surface is too bright/dark, shimmers, ghosts under TAA, or loses its shadow or occlusion.
- Loading a new original asset kind, or a stage/model/texture fails to load or looks wrong.
- Field streaming hitches (GPU upload steps) or a frame costs too much (statistics, profiler, `--benchmark-render`).
- Adding something that draws into the HDR scene (effects, portraits, tools).
- Changing how the weather tints the sky, light and fog, or how the rain and its splashes draw.
- Changing the realistic sky: its atmosphere, sun, moon, stars, clouds, their shadow, the aurora or the rainbow.
- Touching the boundary with the MonoGame Vulkan fork.

## Code map

| Path | Responsibility |
|---|---|
| `Formats/PristonSmdReader.cs` | Binary stage and model files (`.smd` stage, `.smd`/`.smb` model and skeleton): header kinds, material group, vertices, faces, texture links, lights, area index, animation keys. |
| `Formats/PristonInxReader.cs` | Model-info files (`.inx`): mesh and motion file names, the sub model (`SubModelFile`, an alternate model on its own skeleton for the motions the main one lacks: client.md), LOD groups, motion table, talk motions. |
| `Formats/PristonActorLoader.cs` | Builds a `PristonActor` (mesh + skeleton + motions) from an `.inx`, or a skinned/rigid model without one. |
| `Formats/PristonPhysiqueRepair.cs` | Moves a stray Physique vertex (bound four or more joints from the bone its edge neighbours follow, and twice as near that bone) to their bone, keeping its bind position, so it no longer pulls a spike out of a bending joint. |
| `Formats/PristonCameraPath.cs` | Cinematic camera paths (`.cam`) of the opening. |
| `Formats/PristonFormats.cs` | Format records (`PristonStage`, `PristonModel`, `PristonMaterial`, `PristonStageLight`, motions) and `PristonFormatConstants`. |
| `Formats/BinaryUtil.cs`, `PristonAssetCache.cs` | Bounds-checked `BinaryCursor`; case-insensitive cache of parsed stages and models. |
| `IO/ClientSettingsStore.cs` | `client-settings.json` persistence ([client-settings.md](client-settings.md)). |
| `IO/PristonWaveDecoder.cs` | WAV decoding for the audio library. |
| `IO/ItemIconFiles.cs` | Item inventory sprites (`it<KEY>.bmp`) by key and the inventory cells a sprite covers (22 pixels each); used by the HUD and the world importer. |
| `Physics/` | `PristonCollisionWorld`: client view of the shared `CollisionMesh` (`Shared/PristonTale.Geometry`) built from the stage faces flagged for collision; floor height and segment traces. |
| [`Rendering/PristonRenderer.cs`](../Client/PristonTale.Engine3D/Rendering/PristonRenderer.cs) | The renderer: state, construction, quality, the frame update and animation, frame graph (`Draw`), post chain (`ComposeScene`), disposal. One partial per topic: `.Actors` (actor API), `.ActorGeometry` (skinned meshes, bone maps, actor GPU resources, motion selection), `.Stages` (stage loading, budgeted streaming uploads, static shadow aggregates), `.StageGeometry` (groups, animated stage models and merges, bounds), `.Sky`, `.Lighting` (glow tuning, emitter colours, light selection), `.Shadows` (cascades, sun and lamp passes), `.Drawing` (queues, batches, depth pre-pass, outlines, temporal blend), `.Appearance` (gameplay looks), `.Portrait` (actor portraits with a studio light). |
| `Rendering/PristonSceneTargets.cs` | HDR scene, motion, pre-pass targets; sky composition; caller target capture and restore. |
| `Rendering/PristonShaderLibrary.cs` | Surface effects, frame constants, technique selection. |
| `Rendering/PristonMaterial*.cs`, `PristonMaterialAdapter.cs` | Original material to runtime material: queue classification, textures and layers, roughness/reflectance/shading model. |
| `Rendering/PristonTextureLoader.cs`, `PristonImageDecoder.cs`, `PristonMipChain.cs` | Texture decode off-thread, mip chain with alpha-coverage preservation, GPU upload. |
| `Rendering/RenderBatch.cs`, `RenderStageHandle.cs`, `RenderQueueBuilder.cs`, `PristonRenderQueue.cs`, `BatchUploader.cs`, `PristonGpuMesh*.cs` | Batches, per-stage residency, culling and sorting, dynamic-buffer uploads. |
| `Rendering/PristonModelAnimator.cs`, `AnimatedModelRuntime.cs`, `PristonSkinPalette.cs`, `PristonModelNormals.cs`, `PristonMotionSelection.cs`, `CrowdAnimationLod.cs` | Keyframe evaluation, animated scenery, bone palettes (current and previous pose), rebuilt normals, motion clip choice, the crowd level of detail of actor poses. |
| `Rendering/PristonCamera*.cs` | Orbit camera, collision, field-of-view limits. |
| `Rendering/PristonDayNightCycle.cs`, `DayNightSettings.cs` | Time of day, sun/moon direction and illuminance, sky colour, lamp factor, night hue. |
| `Rendering/Lighting/` | Photometry and exposure, stage baked light and floor light field, shadow cascades, lamp shadows, light culling, transient lights, environment lighting, volumetric light, calibration spheres. |
| `Rendering/Post/` | Post chain (`PristonPostProcessor`), auto exposure, bloom, colour grade, display transform CPU reference, GTAO/contact shadows, SSR, TAA, SMAA (`Smaa/` tables and credits), full-screen triangle. |
| `Rendering/Sky/` | Sky manifest (layers per profile), sky model library, profile selection, the fog's sky capture. |
| `Rendering/Atmosphere/`, `PristonRenderer.Atmosphere.cs` | The realistic sky (`PristonPhysicalSky`, `PristonCloudNoise`): physical atmosphere, sun, moon, stars, aurora, rainbow, cirrus, volumetric clouds and their shadow. |
| `Rendering/Weather/`, `PristonRenderer.Weather.cs` | `PristonWeather` (cloud, rain, flash, wind), the weather's sky, light and fog, `PristonRain` (GPU rain streaks and splashes) and `PristonRainOcclusion` (the surfaces rain stops on). |
| `Rendering/GlowRenderer.cs`, `PristonUnlitEffect.cs`, `PristonVfxEffect.cs`, `PristonMeshEffect.cs` | Lamp coronas and the effect shaders other projects draw with. |
| `Rendering/ClientSettings.cs`, `GraphicsOptions.cs`, `DistanceFogMode.cs`, `PresentationSettings.cs` | Options and the change event the renderer listens to. |
| `Diagnostics/` | `PristonFrameProfiler` (CPU and GPU timestamps per `RenderSection`), `PristonStreamingStats`, `FrameTiming`, `FramePacer`, `IClientLog`. |

## MonoGame fork

The client runs MonoGame's Native platform on Vulkan through the fork in `monogame/` (framework from source, native
runtime `mgruntime`, SM 6 effect processor); see [monogame/README.md](../monogame/README.md). Engine3D references
`monogame/framework/MonoGame.Framework/MonoGame.Framework.Native.csproj`. Engine3D relies on fork features: compute
effects and `StructuredBuffer` (light culling, exposure, SH), `GpuTimestampPool` (profiler), resolve of multisampled
float targets. Display limits of the platform (primary monitor only, desktop refresh rate) are handled in
`Client/PristonTale/Display/DisplayController.cs`. Fork changes go into `monogame/` with its own issue notes, not here.

## Threading and lifecycle

- All GPU work and every `PristonRenderer` member run on the main (game) thread. `Update` advances time of day,
  transient lights, sky-profile blending (2 s cross-fade) and animation; `Draw` renders the scene.
- Callers draw in this order (`Client/PristonTale.Gameplay/World/GameScene.Draw`): `PristonRenderer.Draw(camera)` ->
  effects that add to the HDR scene (monster mesh effects, return effect, original particles, skill VFX) ->
  `PristonRenderer.ComposeScene()` -> UI in sRGB. The HDR scene stays bound between `Draw` and `ComposeScene`.
- Asset preparation is off-thread: `PrepareStageTexturesAsync` / `PrepareActorResourcesAsync` decode textures (two
  decode slots) and build actor geometry on the thread pool; the field streamer
  (`Client/PristonTale.Gameplay/World/Streaming/FieldResourceStreamer.cs`) awaits them.
- GPU upload is incremental on the main thread: `AddStageIncrementally` returns an enumerator; each `MoveNext` does one
  indivisible step (one texture upload, one mesh upload, a material setup, or a poll of a worker task) and reports it
  to `PristonStreamingStats`. The streamer advances it within `FieldStreamingPolicy.UploadBudgetMilliseconds` (2 ms) and
  at most 32 steps per frame. The stage publishes (joins `_stages`) only at the end; an abandoned enumerator disposes
  its GPU resources.
- `RequirePreparedTextures()` (set by `GameScene`) makes an unprepared texture upload throw instead of decoding
  synchronously on the main thread. The synchronous `AddStage`/`Load` path is for loading screens and tools.
- Settings: `PresentationSettings.RenderingChanged` calls `ApplyQuality` on the main thread at the frame boundary; it
  may reallocate the sun shadow atlas (releasing the old one first) and reload the colour-grade LUT from disk.
- `ResetTemporalState()` after camera cuts and teleports: drops TAA history and snaps exposure.

## Original formats

Described in Etervale's terms; readers keep every field, including padding, for round-trip checks.

| Format | Reader | Contents and conventions |
|---|---|---|
| Stage `.smd` | `PristonSmdReader.ReadStage` | Header string `SMD Stage data Ver 0.72` (also 0.73), material group, vertices (fixed-point position, BGRA 16-bit colour = authored light), faces (three indices, material, texture-link pointer), texture links (per-corner UVs, chained for extra layers), lights (28-byte records: type flags, position, range, colour), the area index, a source trailer. Positions convert as (x, y, -z). |
| Model `.smd`, skeleton `.smb` | `PristonSmdReader.ReadModel` | Header `SMD Model data Ver 0.62` or a binary model layout; objects with parent names, base transform, rotation/position/scale keys and frame ranges; physique bone per vertex. Positions convert as (x, z, -y). |
| Model info `.inx` | `PristonInxReader` | Mesh, motion and sub-model file names, high/default/low LOD object groups, 512 motion entries of 172 bytes, talk and NPC motions; two layouts differ only in the item-code list width. |
| Camera path `.cam` | `PristonCameraPath.ReadCam` | Position and rotation keys of the opening cinematic, 160 ticks per frame. |
| Textures `.bmp`, `.tga`, `.jpg` | `PristonImageDecoder` | BMP (always opaque; a variant stores its first 14 header bytes with i*i added to byte i), TGA (alpha), others through StbImageSharp; a missing extension is retried as `.bmp`, `.tga`, `.jpg`, `.jpeg`. |
| Sky manifest | `PristonSkyManifest` | Latin-1 text: profiles in braces, layer lines `FileName: ... FadeDelay: ... RGBA: r g b a`. Profile numbers come from field backdrop codes (`PristonSkyProfiles.Resolve`). |

- Fixed-point coordinates divide by `PristonFormatConstants.FixedScale` = 256. Distances are in Priston units (a map
  cell is 64 units).
- Animation time is in ticks of 160 per frame. Stage props advance 2000 ticks per second and start one frame in
  (160); actor motions use 160000/33 ticks per second scaled by the motion's playback rate.
- A skeleton merged from several motion files keeps, per bone and channel, the key range of each file. Only the range
  holding the frame is sampled: a bone with no keys there takes its bind pose (relative to its parent), a missing
  rotation the base rotation, a missing position the base position, a missing scale none. An actor's rotation does not
  interpolate toward a key past its motion's end frame; the key before it holds.
- Crowd level of detail (`CrowdAnimationLod`): a root actor samples its skeleton again every frame within 400 units of
  the focus of the last frame drawn (on screen or not), every second frame to 900, every fourth beyond and every eighth
  off screen (its last world bounds outside the view), staggered by its slot; a new motion is sampled at once, an actor
  coming into view at once, and its world moves the palette every frame. The palette's pose is a copy (`ActorPose`), so
  bone queries and portraits evaluating into the animator's buffer never change what is drawn; an attached actor (a
  held weapon) hangs from that pose. The render statistics show the actors posed and the actors in the last update
  (`Pose n/m`, `LastAnimationCounts`). With 176 actors in Ricarten (`--benchmark-render` with a crowd of 150, GTX 1050
  Ti) the animation update costs about 1 ms of CPU a frame and allocates about 0.3 KB.
- Materials: a TGA or opacity-mapped texture is a cut-out (alpha test at 60/255). Original blend types 2-6 draw late
  (blended queue) unless the material is an opaque cut-out; one layer above the base is carried over (multiply, add or
  sphere-map shine); there is no roughness or metalness in the data (`PristonMaterialAdapter` assigns 0.85 rough
  dielectric, water 0.12, ice 0.3, shine 0.4).

## Scene data

- A stage becomes a `RenderStageHandle`: static batches (merged by material when it has no scroll, wind, layers or
  transparency), animated scenery groups sharing bone palettes, a static shadow aggregate per grid cell, the stage's
  lights split into ground lights and glow (corona) lights, `PristonStageBakedLight` and `PristonStageLightField`.
- Stage normals: the stage file has none per vertex, so `PristonStageNormals` computes one per face corner over the whole
  stage before the faces are split by material: the area-weighted normals of every face touching the corner's position
  that leans at most 60 degrees (`CreaseDegrees`) from the corner's face. Faces of materials never drawn (invisible or without a texture) take no part, so a collision-only ramp or blocker on the ground's vertices does not bend it. Ground stays smooth across material and UV
  seams (the sun's N.L no longer breaks along ground triangles) and walls keep their edge with the floor. The normal
  rides on `PristonCornerVertex.Normal`; corners without one (models) are still averaged over their welded faces, and
  scrolling materials keep per-triangle normals.
- Actors (`AddActor`) get GPU resources shared per `PristonActor`, a bone palette (at most 64 bones,
  `PristonShaderLibrary.MaxBones`), rebuilt normals, and can be attached to a parent bone (`AttachActor`). A corner keeps
  the file's smooth normal (as the original shades a body) while it lies within 40 degrees of the normal rebuilt from
  the bind-pose faces, and takes the rebuilt one beyond (a hard edge the file averaged across). The rebuilt normal
  averages the faces within 60 degrees of the corner's own, so hard surfaces keep their edges, except around a fold (a face creased against the faces across all three of its edges, the bend of a
  low-polygon body), where every face around a position is averaged so the bend shades without a flat triangle.
- Gameplay looks (`PristonRenderer.Appearance.cs`): `SetActorAppearance(handle, PristonActorAppearance)` sets a signed
  light tint (`Tint`, 1 = a white light added to the lit colour, negative takes light away; the surface shader's
  `ActorTint`), `Opacity` (below 1 the actor's batches move to the transparent queue, blend over the scene without
  depth writes and cast no shadow), `Wobble` (vertices shift up to 2 units along 32-unit sine waves, `ActorParams`),
  `CastsShadow`, an `Overlay` texture added as the second layer on uv0 and scrolled by a texture form state
  (`PrepareActorOverlayAsync` decodes it first; it shows once ready), and `Scale` of an attached item about its bone.
  `AddActorEcho(source, lagFrames, parent)` adds a copy that plays the source's clip `lagFrames` behind and hides while
  that frame lies before the clip start (afterimages), or a copy of an attached item on `parent` or the item's own
  parent (an enlarged weapon). Echoes cast no shadow; remove them with `RemoveActor`.
- `RenderQueueBuilder` frustum-culls batches by bounds and sorts: opaque and alpha-tested by state key then front to
  back, transparent back to front, shadow casters by state key. Shadow casters are collected before culling.
- Queues: `Opaque`, `AlphaTest`, `Transparent` (`PristonMaterialRuntimeFactory.Classify`).

## Frame graph

`PristonRenderer.Draw`, then `ComposeScene`. Each row is a `RenderSection` of the profiler.

| # | Section | Runs when | Work |
|---|---|---|---|
| 1 | `Lighting` | always | Day/night state, photometric light, exposure inputs; transient lights then nearest lamps up to `localLightLimit`. |
| 2 | `Queue` | always | Culling and sorting; shadow casters when a sun or lamp shadow redraws this frame. |
| 3 | `SunShadow` | sun shadows on and the refresh scheduler fires | Cascades of this refresh into their atlas tiles. |
| 4 | `LampShadows` | shadows on, lamps lit, rows to redraw | Cube faces of new lamp rows and one other row in turn. |
| 5 | `SkyCapture` | fog on, sky visible, sky changed | Four 32-px tiles of the dome for the fog colour. |
| 6 | `Environment` | IBL on, every 250 ms | Six dome faces, SH irradiance, octahedral map, prefiltered atlas. |
| 7 | `Sky` | always | Dome layers into the sky target (sRGB codes in half floats), then `SkyComposite` fills every scene target. |
| 8 | `Prepass` | no MSAA, not `Overdraw` | Depth, linear depth, normals (smoothness in alpha), motion of opaque surfaces and cut-outs. |
| 9 | `AmbientOcclusion` | pre-pass ran and AO or contact shadows on | GTAO and contact shadows at half resolution, blur. |
| 10 | `LightCulling` | any local light | 16x16 tile masks (depth-bounded after the pre-pass). |
| - | (outline) | an actor with `SetActorOutline` | `DrawOutlines`: the actor flat in one colour, shifted 1.4 px scaled by the screen height over 600 (times the outline strength) in four directions, pushed back 16 units and writing depth, so the lit actor covers it and only a rim stays. |
| 11 | `Opaque`, `Masked` | always | Lit pass; after the pre-pass it tests its surfaces for equal depth without writing it (cut-out texels the pre-pass discarded fail before shading). Without the pre-pass, TAA motion is written here. |
| 12 | `Reflections` | pre-pass, SSR on, a surface with roughness < 0.3 seen | Scene copy and screen-space reflections. |
| 13 | `Transparent` | always | Blended queue, back to front. |
| - | (calibration) | `calibrationSpheres` | Grey and chrome sphere through the lit pass. |
| 14 | `Glow` | always | Coronas of glow lights and glow-emitter props, scaled by the lamp factor. |
| 15 | `Volumetric` | pre-pass, volumetric on, sun shadows on, sun up | March, accumulate, composite over the scene. |
| - | effects | caller | Rain (`PristonRain`), then particles, VFX, mesh effects into the bound HDR scene. |
| 16 | `Temporal` | TAA | Resolve into history (`ComposeScene`). |
| 17 | `Exposure` | auto exposure | Histogram and adaptation, no read-back. |
| 18 | `Bloom` | bloom not `Off` | Six-level pyramid. |
| 19 | `DisplayTransform` | always | Final pass into the caller's target (or an 8-bit target for FXAA/SMAA). |
| 20 | `PostAntiAliasing` | FXAA or SMAA | Filter into the caller's target. |

Mode switches: `Taa` and `Msaa` are exclusive; MSAA (2-4 samples) disables the pre-pass and therefore AO, contact
shadows, SSR, volumetric light, soft particles and depth-bounded tiles. `Overdraw` disables TAA, the pre-pass, the sky
and IBL and draws everything additive. `Underground` removes sun, sky dome and shadows and lights with a fixed ambient
and lamps at full.

## Render targets

| Target | Format | Notes |
|---|---|---|
| Scene colour + depth | HalfVector4, Depth32Float (reversed) | Pre-exposed linear radiance; MSAA when `Msaa`; `PreserveContents`, filled by `SkyComposite`. |
| Motion | HalfVector4 | TAA only: uv motion, previous view depth, view depth (negative on cut-outs). |
| Linear depth, normals | R32F, RGBA8 | Pre-pass only; `SceneLinearDepth` exposes the depth to effects (null without pre-pass). |
| Sky | 8-bit sRGB | Dome layers with original blends, decoded into the scene. |
| Sun shadow atlas | D32F depth surface, `shadowMapSize` | 2x2 tiles from two cascades; casters write depth only (an opaque caster runs no pixel shader) and the lighting samples it; falls back by halves to 1024 if allocation fails. |
| Lamp shadow atlas | R32F | One row per lamp, six faces of `lampShadowTileSize`. |
| TAA history | 2 x HalfVector4 | Compressed colour and view depth, ping-pong. |
| Environment | 64-px face atlas, octahedral 128 base + 5 levels atlas, 32x32 BRDF LUT, SH buffer (10 entries) | Rebuilt every 250 ms. |
| Post | bloom pyramid (6 levels from half resolution), 8-bit LDR target for FXAA/SMAA, SMAA edges/weights | |

The caller's targets and viewport are captured in `PristonSceneTargets.Begin` and restored by `Compose`, so a scene can
render into the back buffer or any 8-bit target (portraits, captures).

## Light model

All light is photometric (`Lighting/PristonPhotometry.cs`, `PristonDayNightCycle.cs`):

- **Sun:** rises 05:30, sets 18:45 (`Sunrise`, `Sunset`), arc east (+X) to west, tilted toward -Z. Direct illuminance
  120000 lux reduced by air mass (Kasten-Young) and broadband extinction, faded over the disc crossing the horizon;
  reddened per channel by spectral extinction (the low sun turns orange).
- **Sky:** 1200 lux at the horizon plus 20000 x sin(elevation)^0.8, falling by e every 1.6 degrees below the horizon;
  cooler than the gradient's ambient when the sun is high.
- **Night:** the day/night gradient (`DayNightSettings.DefaultGradient`) gives original light levels; a level becomes
  illuminance as level^1.4 x 10 lux (`LevelToIlluminance`), at half scale for moon and night sky. The moon
  shines from a fixed high direction with the colour of sunlight; the night's blue is applied by mesopic vision in the
  final pass ([shaders.md](shaders.md)), so specular light keeps the colour of the diffuse light.
- **Ground bounce:** 0.2 albedo of the horizontal illuminance for downward surfaces.
- **Stage lamps:** authored colour as a original level x 2, inverse-square from a shared 150-unit reference, windowed to
  zero at the authored range (600 when unset), scaled by the field's lamp intensity and range and by the lamp factor
  (off above 4 degrees of sun elevation, full at 2 degrees below). Lamps flagged as lens lights get coronas only
  unless also dynamic.
- **Transient lights** (`PristonTransientLights`, combat and spells): at most 80 alive, the 8 nearest the focus shaded,
  power decaying 70 times a second; by day scaled to 12 % of the daylight so they still read.
- **Shader units:** light colour = illuminance / pi x pre-exposure. Pre-exposure = pi x 0.4 / 2^EV100 of the incident
  reading, so a white surface under the metered light is near 1 at every hour.
- **Exposure** (`PristonFrameExposure`, `Post/PristonAutoExposure`): incident EV100 = log2(E x 100 / 250) of the
  horizontal illuminance; display scale adds `CompensationStops` (+0.5 stop day key, a dimming of up to 1.9 stops below
  EV 15 for dusk and night) and the user's compensation. With `autoExposure` the GPU meters a reflected reading
  (K = 12.5) from a 128-bin histogram, blends it with the incident reading by day (weight 0.6, night: image only) and
  adapts in 0.6 s toward brighter, 2.2 s toward darker; the light model's own change applies at once.
- **Self-illumination and display-referred art** (sky, glows, particles, VFX) divide by the display scale; at night
  emissive albedo 1 is 40 cd/m^2 (`EmissiveLuminance`). Characters and monsters are not light sources: their
  self-illuminated parts stay at the display level by day and by night (`RenderBatch.IsActor`, `ActorReceiver`).
- **Authored per-vertex light:** stage vertex colours carry the art's shading and tint the albedo in linear light.
  Scenery models (windmills, cranes, flags, static or animated) have no vertex light, so each surface takes the mean
  linear luminance of the stage's vertex light on faces with the same texture, or the stage mean
  (`PristonStageBakedLight`). Actors take the light of the highest upward floor triangle under their feet, averaged over
  a few points (`PristonStageLightField`, `RenderBatch.FloorLight`, shader `DrawLight`); `ActorFloorLight = false`
  (character select) and portraits use full light. The authored light also occludes the sky's reflections.
- **Distance fog** (`PristonDistanceFog`): from 2208 units, opacity 0.85 x (1 - exp(-0.0003 d)), colour from the sky
  capture plus forward-scattered sun (Henyey-Greenstein g = 0.7, Schlick form); the sun term fades out by 7 degrees
  below the horizon.
  `Classic` draws as `Atmospheric`.
- **Foliage:** every cut-out outside the blended queue, including late-flagged leaves, is drawn opaque by the pre-pass
  with its own depth, motion and occlusion; the lit pass shades it at that depth. Two-sided lit cut-outs transmit 45 %
  of the sun from behind.

## Realistic sky

With `realisticSky` on (`PristonRenderer.RealisticSky`), `PristonPhysicalSky` (`Shaders/Passes/Atmosphere.fx`) paints the
sky instead of the painted domes over a game field outdoors (`FieldSky`, set by the game scene; the opening and the other
screens keep their domes); the domes stay the classic sky. It writes display-referred sRGB into the
same sky targets the domes use (`DrawPhysicalSky`), so the scene, the fog's sky capture and the image-based
lighting take it as they took the domes.

- Atmosphere (after Hillaire 2020): the transmittance table (256 x 64) and the multiple scattering table (32 x 32) are
  built once; the sky view (256 x 128, the sky's radiance around the viewer for the sun and the moon) every frame
  (`UpdatePhysicalSky`, before the scene's targets bind). Rayleigh, Mie and ozone of a clear Earth day, lengths in km.
- Sun: the hour's direction (`PristonDayNightCycle`), 120000 lux outside the atmosphere in the frame's pre-exposed
  units; its disc (0.008 rad, limb-darkened, a soft edge) dimmed once by the air toward it, with the aureole the air
  scatters around it, and hidden by any cloud with a body (the share a cloud lets through would still clip white; the
  light through it is the cloud's own scattering). A morning mist veils the disc and widens the glow, so the sun shows
  as a soft bright patch. A soft knee above 0.6 of the display keeps a bright sky's and a sunlit cloud's hue and
  shading, and far past white (the sun) bleaches toward white-gold.
- Moon: a fixed high direction, its light 0.3 lux times its lit share; its disc (0.013 rad) lit as a sphere by its
  phase (`MoonPhase`, from the server clock: `WorldClock.MoonPhaseAt`, a cycle of 8 game days), mottled maria and a
  faint earthshine.
- Stars and the Milky Way: one star in a share of cells of a sky turning with the hour, twinkling, tinted; a faint
  mottled band; with the airglow they fade in as the sun sinks from 2 to 12 degrees below the horizon.
- Aurora (`Aurora`, the ice fields at night): thin folded green curtains turning violet at their tops, in 20 layers
  that stand them in rays, waving slowly between the horizon and the zenith.
- Rainbow (`RainbowStrength`): the primary bow at 40.6 to 42.5 degrees from the antisolar point and the fainter
  reversed secondary at 50.5 to 53.5, while a light rain falls (none in a downpour or without rain) through a sky the
  sun shines through, with the sun between 3 and 42 degrees up.
- Cirrus: a thin stretched sheet at 8 km under a clear sky, fading as the clouds gather.
- Volumetric clouds (`cloudQuality`; after Schneider's Nubis): a cumulus layer from 1.5 to 4 km in a 64^3 half-float tiling
  noise volume (`PristonCloudNoise`: Perlin-Worley shape, Worley fBm detail) under a broad coverage pattern; the coverage
  grows from a fair sky's 30 % to 95 % of the visible sky as the clouds gather and closes further in rain, where a
  stratus deck fills the layer's lower half and the denser clouds go dark below. 48 marching steps, 5 toward the
  light; single scattering with the powder effect, a diffuse multiple-scattering term that keeps a deck's base grey, and
  the sky's light; a strike lights the clouds from inside on its side of the sky (`PristonWeather.FlashAzimuth`, added
  as the sky draws, since the flash is too brief for the clouds' history). The clouds drift with a base wind plus the
  weather's and stay in place as the camera moves. Their hemisphere (azimuth by elevation, the elevation's square root
  down the height, so the low sky holds most texels) renders every texel every frame at 768 x 192, 1024 x 256 or 1536 x
  384, blended 10 % into the last frame's: the march's jitter averages over about ten frames, and moving and changing
  clouds never leave blocks of older texels; the sky reads it through a cubic B-spline, so the magnified texels show no
  grid. The sun lights the clouds by day and the moon by night, and at night they keep a glow of their own (the night
  sky's, the moon's and the town's), so a stormy night shows its deck; under a closing sky the far haze takes the deck's
  grey instead of opening onto the horizon. A cloud with a body hides the stars and the moon as it hides the sun. Off:
  an overcast sky turns a flat grey instead.
- Cloud shadows: the clouds' transmittance toward the sun over 6 km around the camera (256^2), read by the surfaces'
  sun term (`Surface.fx` `CloudShadowAt`, `PristonCloudShadowFrame`) at the clear share of the sky (an overcast sky
  already dims the sun evenly), only by day.

## Weather

`PristonRenderer.Weather` (`PristonWeather`: cloud and rain 0..1, the lightning flash 0..1, the wind) changes the frame
(`PristonRenderer.Weather.cs`):

- Sky: with the classic sky, half the sky clouded shows the rain dome (`FIELD_BACKIMAGE_RAIN`, profile 7) at every
  hour, as the original switched to it, cross-faded like any profile change (`SkyProfileNow`); the realistic sky draws
  the weather's clouds instead.
- Light (`WeatherLight`, in `BuildFrameLighting`): the clouds cover 60 % of the sky without rain and all of it in full
  rain, and the direct sun keeps the clear share to the power 1.5 (a quarter through an overcast sky, an eighth through
  a light rain, so soft shadows still fall, nearly none in a downpour); full cloud keeps 70 % of the sky light, 60 % of
  it turned grey; the exposure meters the scene 0.62 times as bright (and 0.85 of that in full rain) so the weather
  darkens it; rain lights the lamps by its share, fully in a downpour; a full flash adds 0.6 of the metered
  illuminance as cold sky light the exposure does not meter (about twice a storm's light at its peak, at any hour) and
  draws the sky dome three times as bright (`SkyFlash`).
- Fog (`WeatherFog`): full rain starts the distance fog at 35 % of its distance and makes it four times as dense; a
  field with a morning mist (`MorningMist`: a forest's 1, or the strength `FieldDef.MorningMist` names, Ricarten's 0.8
  for a town in the clouds) brings it to 1 / (1 + 3 x strength) of its distance and makes it 1 + 3 x strength times
  as dense at 6:00, rising from 4:30 and lifting by 9:00, and thickens the volumetric light's medium 1 + 6 x strength
  times, so the low sun draws shafts through it.
- `RainRadiance`: a drop's light, the sky light plus a quarter of the sun on a white surface, at least 0.07 of the
  metered level.

`PristonRain` draws between the opaque scene and the effects, testing the depth buffer without writing it:

- Drops (`Rain.fx` `Drops`): a fixed buffer of 18000 near and 10000 far streaks with seeds; the shader places each in a
  box 900 (near) or 2600 (far) units wide and 900 tall around a point 240 units ahead of the eye, falling at 0.8 to 1.2
  times 850 units per second plus the wind, wrapping as the camera moves, so nothing moves on the CPU. The rain's share
  of each layer is drawn. A streak spans 1/26 s of its velocity seen from the moving camera, at least 1.3 pixels wide at
  1080 lines; a drop thinner than that (0.5 units) fades by the ratio, so far rain reads as a veil. The part of a
  streak under the occlusion map's surface is discarded.
- Occlusion (`PristonRainOcclusion`): the highest walkable surface of every 8-unit cell in 256 x 256 cells around the
  player, from the loaded fields' collision, kept toroidally so a walk reads only the new rows (at most 4096 cells a
  frame); uploaded to an R32F texture at most every 0.1 s while it changes.
- Splashes (`Rain.fx` `Splashes`): up to 520 at full rain within 460 units of the player, on the map's surfaces (roofs
  and ground), 0.3 s each: a spreading ring and a camera-facing spray.

## Shadows

- **Sun cascades** (`Lighting/PristonShadowCascades.cs`): up to 4 over 9000 units of view depth from 60, practical
  split (lambda 0.8), each slice enclosed in a sphere, casters up to 4000 units beyond toward the light. A cascade's
  centre follows its focus by whole texels (`FollowFocus`: the sub-texel part of the focus's travel across the light is
  kept as a residual), so a still receiver keeps its place in the texel grid while the camera moves, and a turning sun
  pivots the grid about the focus. Snapping a fixed world anchor (the origin, tens of thousands of units from the towns)
  instead moved the anchor's rounding phase with every slight turn of the sun, and every shadow edge jumped a fraction of
  a texel at each refresh, a flicker at the refresh rate with nothing moving. Cascades 0-1 refresh every update, 2 every second, 3 every fourth; a new light direction or a
  focus jump over 350 units refreshes all. Refresh rate `shadowUpdateRate` (`FixedRateRefreshScheduler`), halved by
  moonlight. Between refreshes cascades 0-1 still redraw every frame on their current fit, so the characters near the
  camera cast from their current pose (a caster a refresh old darkens a running character lit from behind). Characters
  and monsters receive the sun's shadow 40 units further along the light (`ActorShadowBias`), so their own thin
  low-polygon bodies do not shade themselves in flickering triangles while buildings and trees still shade them. Over a
  cascade's blend band both cascades are sampled and blended, never one picked per pixel by noise. The filter is a
  continuous tent (`ShadowTent`): every texel it reaches is weighted by its distance to the point, so an edge slides
  continuously, and the result is the same in every frame. PCF (the moon): the 5x5 tent, nine gathers. PCSS by day
  (light radius tangent 0.01): the 3x3 tent (four gathers), whose blocker count two rings of corners three and five
  texels out extend; a penumbra wider than 1.5 texels (tall casters far above the receiver) blends by 3 texels into a
  16-tap disk of its radius, turned per pixel. A disk turned per pixel and frame everywhere left its noise to the
  temporal anti-aliasing, which cannot average it on an animated character's shadow or without TAA: the edges crawled. Casters draw two-sided; the wind/water displacement matches the lit pass.
- **Lamp shadows** (`Lighting/PristonLampShadows.cs`): the `lampShadowCount` lamps nearest the focus (after the
  transient lights) keep a row of six 90-degree cube faces storing distance / radius. A lamp keeps its row while it
  stays near; new rows draw at once, the others on every frame a skinned caster's bounds meet the lamp's sphere (and once
  more after the last one left, to clear its shadow), so the shadows follow moving actors without steps. Only skinned
  batches (actors, animated props)
  cast; the stage's own occlusion is in its authored light. The edge takes the 5x5 tent (`ShadowTent`), the same in every
  frame, not a few fixed taps that would print stepped copies of the silhouette. Characters and monsters do not receive lamp shadows
  (`ActorReceiver`): their smooth-shaded low-polygon bodies would be cut by the hard facets of their own and each
  other's silhouettes; they still cast onto the stage.
- `Ctrl+F5` toggles both in game; `PRISTON_SHADOW_DIAG=1` enables shadow diagnostics logging.
- **Ground shadows** while shadow maps are off (`GameScene.Shadows.cs`): every character darkens the floor with
  `image/bshadow.bmp` one unit above its feet, turned with it (`VfxRenderer.AddGroundQuad`, the darkening blend), by its
  shadow size: half extents 12x10, 17x14, 26x20 or 38x30 units across and along it (players 1; a monster's or NPC's from
  its description, none for -1); a dead character's shadow goes 40 frames of 70 per second into its death.

## Screen-space and image-based passes

- **GTAO and contact shadows** (`Post/PristonScreenOcclusion.cs`): radius 90 units, at most 48 half-res pixels; contact
  march 24 units toward the sun with 12-unit thickness and a fixed per-pixel jitter, sun only. The lit pass upsamples by
  depth; AO also gives specular occlusion. The sun's visibility is the darker of the contact shadow and the cascades,
  so a surface both shade is not darkened twice; characters and monsters take no contact shadows (the march across
  their thin bodies left dark patches that moved with the view and the pose).
- **Image-based lighting** (`Lighting/PristonEnvironmentLighting.cs`): dome faces at 64 px, SH irradiance scaled to the
  light model's sky light (dome at most at its visible level, a uniform fill for the rest), five GGX levels from a
  128-px octahedral base (64 samples), split-sum table with multiple-scattering compensation. Off:
  hemispherical sky/ground ambient.
- **SSR** (`Post/PristonScreenReflections.cs`): rays up to 3000 units, after the opaque queues; a miss keeps the IBL.
- **Volumetric light** (`Lighting/PristonVolumetricLight.cs`): extinction 4e-5 per unit with a 600-unit scale height
  around 50 units below the focus, anisotropy 0.6, march to the fog start (at most 6000), history weight 0.125.

## Anti-aliasing

- **TAA** (`Post/PristonTemporalAntiAliasing.cs`): Halton (2, 3) jitter of 8 samples; current weight 0.08 at rest to
  0.25 at 8 px/frame; variance clip gamma 1.25 over the 3x3 neighbourhood (a 5-tap cross left jittered stone and
  gold trims shimmering); depth tolerance 0.06; motion of skinned actors from the previous bone
  palette (`PristonSkinPalette` keeps the previous pose, stamped per motion frame), of wind and water from the previous
  animation time. Volumetric and AO noise rotate per frame only with TAA.
- **MSAA:** scene target multisampled (2-4), the back buffer never.
- **FXAA / SMAA 1x:** on the 8-bit display image after the final pass.

## Diagnostics

- `LastFrameStats` (`PristonRenderStats`): batches total/visible/culled, draw calls, triangles, state and texture
  changes, dynamic upload bytes, sun shadow update, lamp shadow cube maps redrawn (`L` in the title), shadow draw
  calls. Shown in the window title (`Ctrl+F3`).
- `Profiler` (`PristonFrameProfiler`): CPU time per `RenderSection`; GPU time from Vulkan timestamps after
  `EnableGpuTimers` (benchmark only), read back 4 frames later without waiting.
- `LastStreamingStats`: per `StreamingStep` count, total and worst main-thread milliseconds and bytes of the last stage
  upload (`[Stream] upload steps` log line).
- `--benchmark-render <assets> <content> <report.json> <fieldId> <settings.json> [seconds runs hour orbit [distance
  [pitch [focusX focusZ [yaw]]]]]` in the regression runner writes frame/update/draw/present times, `CpuSectionsMs`,
  `GpuSectionsMs`, `StreamingUploadSteps` and a `<report>.png` capture ([testing.md](testing.md)).
- `--verify-shaders`, `--capture-sky`, `--check-look` and the render test suites (`RenderTests`, `RenderFidelityTests`,
  `PhotorealTests`, `MaterialAdapterTests`, `ShadowProjectionTests`, `SkinPaletteTests`, `ModelNormalTests`,
  `TransientLightTests`, `SkyTests`, `CameraTests`) cover the renderer.

## How to

- **Add a scene pass:** shader and owner per [shaders.md](shaders.md#how-to); add a `RenderSection` value; call it at
  the right point of `PristonRenderer.Draw` (or `PristonSceneTargets.Compose` for post), rebind the scene with
  `_sceneTargets.Bind()` and `_stateCache.InvalidateDeviceState()` after any off-screen target; gate it on the settings
  it needs (pre-pass, sun) and dispose it in `Dispose`; add a setting if optional
  ([client-settings.md](client-settings.md)); update the frame-graph table here and the benchmark section names in
  [testing.md](testing.md).
- **Draw something new into the HDR scene from another project:** draw between `Draw` and `ComposeScene`, use
  `PristonUnlitEffect` or `PristonVfxEffect` (display-referred colour divided by `SceneExposure`) and read
  `SceneLinearDepth` only when non-null.
- **Change a lighting constant:** the CPU side lives in `PristonPhotometry`, `PristonDayNightCycle` or the constants at
  the top of `PristonRenderer`; mirrored shader values are listed in [shaders.md](shaders.md#pitfalls). Re-run
  `--verify-shaders`, the render tests and `--benchmark-render` captures by day and at 21:00.
- **Support a new original asset variant:** extend the reader in `Formats/`, keep unknown bytes in metadata, resolve paths
  through `AssetFiles`, add a fixture test (`BinaryCursorTests`, `RenderTests`), and describe the format here
  without citing the original source.
- **Add a streaming step:** yield a `StreamingWork` with a `StreamingStep` from `StageUploadSteps`; keep each step one
  indivisible GPU call; do CPU work in `Task.Run` and yield `StreamingWork.Wait` while polling.

## Pitfalls

- `Draw` leaves the HDR scene bound; anything drawn before `ComposeScene` lands in it, and the UI must come after.
- Scene depth is reversed (`PristonDepth`): 1 at the near plane, cleared to 0, tested with `GreaterEqual`, in a 32-bit
  float buffer. A camera, portrait or effect projection comes from `PristonDepth.Perspective`, and a scene draw uses
  `PristonDepth.Write`/`Read` (`Equal` for the lit pass over pre-passed surfaces, which must keep the pre-pass's vertex
  stage so their depth is bit-identical); MonoGame's `DepthStencilState.Default`/`DepthRead` and `Clear(Color)` (depth 1) reject
  everything there. Shadow maps keep the ordinary range. A 24-bit buffer with the near plane at 1 could not separate
  the original models' layered faces at range, and they flickered under the jitter.
- Binding a render target ends the Vulkan render pass; the profiler resets timestamps only outside one, so
  `_sceneTargets.Begin` precedes `Profiler.BeginFrame`.
- The sky capture is refreshed before the scene target is cleared, because binding another target may discard it.
- MonoGame clears `DiscardContents` targets on bind; scene, shadow and LDR targets are `PreserveContents` on purpose.
- Skinned meshes cull back faces like the stage (`PristonRasterizerStates.SingleSided`) unless the material is
  two-sided; drawn two-sided, the inner and back faces of clothes show through as dark triangles and fight the front
  faces while the model moves. Shadow casters draw two-sided.
- Cut-outs must stay in the alpha-tested queue (pre-pass depth, motion, AO); moving foliage to the blended queue makes
  it take the depth, motion and fog of what is behind (ghosted canopies under TAA).
- `_sunElevation` holds the sine of the elevation; thresholds such as `> -0.05` are in sine, not radians.
- Light selection is relative to the camera focus (`camera.Target`), not the camera position.
- Transient lights fill the first local-light slots; with `localLightLimit` 8 a fight can leave no stage lamps.
- Texture cache keys include the full directory: the same file name in two directories is two textures.
- Some comments in `Formats/` name functions of the original client; do not copy those names into new code or docs.
- A tool that renders every frame into a new 1920x1080 render target (created and disposed each frame) loses the
  Vulkan device after a few hundred frames; keep one target and reuse it, as `WeatherCaptureProbe` does.

## Related

- [shaders.md](shaders.md): passes, conventions, register maps, shader validation.
- [client-settings.md](client-settings.md): every option, presets and GPU timings.
- [effects.md](effects.md): particles and VFX drawn into the HDR scene.
- [client.md](client.md): screens, `GameScene`, field streaming, HUD portrait.
- [world.md](world.md): server-side stage collision and the shared geometry.
- [testing.md](testing.md): regression runner, probes, benchmark arguments.
- [architecture.md](architecture.md): projects and threading model.
