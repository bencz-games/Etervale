# Shader library

The client's SM 6.0 HLSL shaders in `Client/PristonTale/Content/Shaders/`, how they are built for Vulkan, their
conventions and the C# classes that own each effect. The frame graph that runs them is in [rendering.md](rendering.md).

## Read this when

- Adding or changing a shader pass, technique, constant, texture slot or vertex input.
- A shader fails to compile in the content build, or a parameter is `null` at runtime.
- Changing the material model, the light units, the BRDF, shadows, fog or the post chain at the shader level.
- A colour, exposure or tonemapping result disagrees with its CPU reference in the shader checks.
- Porting a Direct3D/OpenGL convention (clip space, uv origin, matrix order) into the library.

## Code map

| Path | Responsibility |
|---|---|
| `Client/PristonTale/Content/Content.mgcb` | Content project: `/platform:DesktopVK`, `/profile:HiDef`, one `#begin` block per effect with `SpirvEffectProcessor`; `Surface.fx` is built twice. |
| `Shaders/Core/` | `Common.hlsli` (constants, `SafeNormalize`, half-float cap, `SkyDepth` written for sky pixels and `SkyDepthThreshold` beyond which a view depth is sky; passes never declare their own; `InterleavedGradientNoise`, the one per-pixel jitter), `Color.hlsli` (exact sRGB curves, PBR Neutral, `DisplayEncode`), `Frame.hlsli` (`FRAME_CONSTANTS`), `Fullscreen.hlsli` (full-screen triangle). |
| `Shaders/Lighting/` | `Octahedral.hlsli` (direction mapping), `Brdf.hlsli` (GGX, height-correlated Smith visibility, Schlick, specular anti-aliasing), `LightingDecl.hlsli` (`LIGHTING_CONSTANTS`, `LIGHTING_RESOURCES`), `Shading.hlsli` (cascaded sun shadows PCF/PCSS, lamp cube shadows, screen-space occlusion, tiled local lights, image-based and hemispherical ambient, specular occlusion). |
| `Shaders/Atmosphere/` | `FogDecl.hlsli`, `Fog.hlsli`: distance fog toward the captured sky with a forward-scattering sun term. |
| `Shaders/Materials/` | `MaterialDecl.hlsli` (`MATERIAL_CONSTANTS`, `MATERIAL_RESOURCES`, shading model and layer mode codes), `Material.hlsli` (`SampleSurface`: base colour, second layer, alpha test, visible-face normal). |
| `Shaders/Vertex/` | `SkinningDecl.hlsli` + `Skinning.hlsli` (rigid one-bone skinning, current and previous pose, `MAX_BONES 64`), `VertexAnimationDecl.hlsli` + `VertexAnimation.hlsli` (water ripple and wind). |
| `Shaders/Passes/*.fx` | One file per effect asset (table below). |
| `Client/PristonTale.Engine3D/Rendering/PristonShaderLibrary.cs` | Owner of the two surface effects: frame block upload (`ApplyFrame`), technique selection (`SelectStaticSurface`, `SelectSkinnedSurface`, `SelectSky`, shadow casters), white-texture slot binding, `LoadRequired`. |
| `Client/PristonTale.Engine3D/Rendering/Post/PristonDisplayTransform.cs`, `PristonColorGrading.cs` | CPU references of the display transforms, mesopic vision and the colour grade. |
| `Client/PristonTale.Engine3D/Rendering/Post/Smaa/` | SMAA area and search tables (gzip, embedded) and `CREDITS.md` (MIT licence). |
| `tests/PristonTale.Tests/ShaderValidationProbe*.cs` | `--verify-shaders`: GPU checks of every technique against C# formulas. |

## Effects

Each `.fx` in `Passes/` builds to `Effects/<Name>` (the asset name the C# owner loads).

| Asset | Source | Techniques | C# owner | Computes |
|---|---|---|---|---|
| `Effects/StaticSurface`, `Effects/SkinnedSurface` | `Surface.fx` (`SKINNED` define for the second) | `Prepass`, `PrepassMasked`, `Opaque`, `Masked`, `Sky` and `ShadowClear` (static only), `Outline` and `OutlineMasked` (skinned only), `ShadowCaster`, `ShadowCasterMasked` | `PristonShaderLibrary` | Stage, scenery, sky-layer and actor surfaces: depth pre-pass, lit HDR forward pass with motion output, sun and lamp shadow casters, and the hover outline (the actor flat in `OutlineColor`, shifted on screen by `OutlineParams.xy` and pushed back `OutlineParams.w` world units). |
| `Effects/Unlit` | `Unlit.fx` | `Unlit` | `PristonUnlitEffect` (`GlowRenderer`, `PristonMeshEffect`) | Unlit textured `VertexPositionColorTexture` geometry; vertex colour sRGB decode, `Tint` scales into radiance. |
| `Effects/Atmosphere` | `Atmosphere.fx` | `Transmittance`, `Multiple`, `SkyView`, `Clouds`, `CloudShadow`, `Sky` | `Atmosphere/PristonPhysicalSky` | The realistic sky: atmosphere lookup tables, the volumetric clouds' hemisphere (blended with its history) and shadow, and the sky with the sun, moon, stars, aurora, cirrus, clouds and rainbow into the sky target ([rendering.md](rendering.md#realistic-sky)). |
| `Effects/Rain` | `Rain.fx` | `Drops`, `Splashes` | `Weather/PristonRain` | Rain streaks placed, wrapped, stretched and widened to a least pixel width in the vertex shader from seeds and time; streak parts under the occlusion map's surface discarded; procedural splash rings and sprays ([rendering.md](rendering.md#weather)). |
| `Effects/Vfx` | `Vfx.fx` | `Vfx` | `PristonVfxEffect` (Effects `VfxRenderer`) | Skill and combat sprites in linear colour with the soft-particle fade against the pre-pass linear depth; `Mode` selects radiance, squared radiance (colour blends) or a multiplier / inverse multiplier for darkening blends. |
| `Effects/SkyComposite` | `SkyComposite.fx` | `SkyComposite`, `Background` | `PristonSceneTargets` | First pass of every scene: decodes the 8-bit sky dome (or a flat radiance) into all scene targets, sky motion marker and depth. |
| `Effects/AmbientOcclusion` | `AmbientOcclusion.fx` | `Occlusion`, `Blur` | `Post/PristonScreenOcclusion` | Half-resolution GTAO (two slices, three horizon steps each way) and contact shadows toward the sun; 4x4 depth-aware blur. Output x AO, y contact visibility, z view depth. |
| `Effects/LightCull` | `LightCull.fx` | `Cull` (compute) | `Lighting/PristonLightCulling` | One group per 16x16 tile: depth range from the pre-pass, sphere vs four side planes, a 32-bit light mask per tile. |
| `Effects/Environment` | `Environment.fx` | `Irradiance` (compute), `Octahedral`, `Prefilter` | `Lighting/PristonEnvironmentLighting` | Order-2 SH irradiance of the dome's six faces (Lanczos-windowed), HDR octahedral map, five GGX-prefiltered levels in one atlas. |
| `Effects/Reflections` | `Reflections.fx` | `Copy`, `Reflect` | `Post/PristonScreenReflections` | Scene copy, then 24 jittered screen-space steps plus binary refinement; adds Fresnel x confidence x (hit - environment). |
| `Effects/Volumetric` | `Volumetric.fx` | `March`, `Accumulate`, `Composite` | `Lighting/PristonVolumetricLight` | Quarter-resolution height-fog in-scatter (16 shadowed segments, exact optical depth), reprojected accumulation, joint bilateral composite. |
| `Effects/PostTemporal` | `PostTemporal.fx` | `Resolve` | `Post/PristonTemporalAntiAliasing` | TAA resolve on pre-exposed HDR: Gaussian current sample and variance box over 3x3, closest-depth motion, Catmull-Rom history clipped to a YCoCg variance box, depth disocclusion. |
| `Effects/PostExposure` | `PostExposure.fx` | `Histogram`, `Adapt` (compute) | `Post/PristonAutoExposure` | 128-bin log2 luminance histogram (2^-12 to 2^20 cd/m^2), percentile average, blend with the incident reading, asymmetric adaptation; state buffer read by the final pass. |
| `Effects/PostBloom` | `PostBloom.fx` | `DownsampleKaris`, `Downsample`, `Upsample` | `Post/PristonBloom` | Dual-filter pyramid without threshold; first downsample Karis-weighted; convex upsample blend. |
| `Effects/PostFinal` | `PostFinal.fx` | `Tonemap`, `Fxaa` | `Post/PristonPostProcessor` | Sharpening, scene + bloom, mesopic vision, exposure, PBR Neutral/AgX/ACES, sRGB, 3D LUT grade, dither, luma in alpha; FXAA on the 8-bit image. |
| `Effects/PostSmaa` | `PostSmaa.fx` | `EdgeDetection`, `BlendingWeights`, `NeighborhoodBlending` | `Post/PristonSmaa` | SMAA 1x (preset HIGH) on the 8-bit display image. |

### Surface.fx register map

Both stages use one cbuffer at `b0`: vertex `SurfaceVertexConstants` (view-projection, caster matrices, UV scrolls,
`DrawOpacity`, `DrawLight`, previous view-projection, `MotionParams` and `ActorParams`, then skinning or
vertex-animation constants); pixel `SurfacePixelConstants` (`FRAME_CONSTANTS`, `FOG_CONSTANTS`, `LIGHTING_CONSTANTS`,
`MATERIAL_CONSTANTS`, in that order). Actor looks: `ActorParams` (skinned only) holds the wobble amplitude and phase and
whether the second layer is an overlay read on uv0; `ActorReceiver` (material constants) marks a character or monster,
whose self-illumination stays at the display level and which takes no lamp shadows; `ActorTint` (material constants) adds `albedo x rgb / Exposure` to
the radiance (clamped at 0) and multiplies the output alpha by `a` after the alpha test.

| Slot | Resource |
|---|---|
| t0 / t1 | Base colour, second layer (anisotropic, wrap) |
| t2 | Sun cascade atlas (D32F depth surface, point) |
| t3 | Fog sky capture |
| t4 | Screen-space occlusion (half resolution) |
| t5 | Lamp cube-shadow atlas |
| t6 | Tile light masks (`StructuredBuffer<uint>`) |
| t7 / t8 / t9 | Prefiltered environment atlas, SH irradiance (`StructuredBuffer<float4>`, entry 9 holds the atlas scale and fill), split-sum BRDF table |

Material codes (`MaterialDecl.hlsli`, mirrored by `PristonMaterialDescription.cs`): `ShadingModel` 0 lit
(metallic-roughness), 1 emissive (self-illuminated or additive: the albedo is emitted radiance), 2 colour factor
(multiplicative blends write the albedo as the blend factor). `LayerMode` 0 none, 1 multiply (detail or light map),
2 add, 3 sphere-map reflection added to the specular term.

## Build

- Compiler: the fork's `SpirvEffectProcessor` (`monogame/pipeline`), HLSL to SPIR-V through DXC, `vs_6_0`/`ps_6_0`/
  `cs_6_0`, with D3D conventions (`-fvk-invert-y`, `-fvk-use-dx-layout`, `-fvk-use-dx-position-w`). It runs on every
  platform; no Wine. See [monogame/README.md](../monogame/README.md) for how the processor is built and referenced.
- `dotnet tool restore` provides `dotnet-mgcb`; the client build compiles `Content.mgcb`. `/processorParam:Defines=`
  sets preprocessor symbols (only `SKINNED` is used); `DebugMode=Auto` everywhere.
- A missing effect asset at runtime throws from `PristonShaderLibrary.LoadRequired` with a "Rebuild and deploy the
  client Content directory" message; a missing surface technique throws `ContentLoadException`.

## Units and light model

- Colours are linear Rec.709. Scene passes write pre-exposed HDR radiance into the HalfVector4 scene target and never
  tonemap; exposure, display transform and sRGB encoding happen once, in `PostFinal.fx`.
- Light colours are the radiance of a white Lambertian surface facing the light (illuminance / pi, times the frame's
  pre-exposure), so a lit diffuse surface is albedo x colour x n.l and the specular lobe carries the matching pi. A
  white surface under the metered light is near 1 at every hour (`Lighting/PristonPhotometry.cs`).
- Display-referred inputs (sky dome, self-illumination by day, particles, glows, VFX) are divided by the frame's
  nominal display scale (`PristonFrameContext.Exposure`), so they show at their authored level when the automatic
  exposure sits at the light model's reading. Self-illumination at night is a light source of fixed luminance
  (`EmissionScale`), except on characters and monsters, which stay at the display level.
- Original textures and vertex colours are sRGB-encoded 8-bit values. Scene textures upload as `ColorSRgb`, so the
  sampler decodes each texel before filtering (mips are averaged in linear light by `PristonMipChain`); vertex colours
  are decoded exactly in the shader, except VFX vertex colours, already linear. The sky's textures stay `Color`: its
  layers are composed display-referred (`PristonTextureLoader` with `displayReferred`), as are UI textures.
- The vertex colour scales the albedo in linear light on both paths: stage geometry carries its authored light; scenery
  models take the stage's light per texture (`PristonStageBakedLight`); skinned actors take the floor light through the
  `DrawLight` constant (`PristonStageLightField`). That light also occludes the sky's reflections.
- Stage lamps fall off with the inverse square from `LampReferenceDistance` = 150 units (`Shading.hlsli`), constant
  inside `LampMinDistance`, windowed to zero at the radius by (1 - (d/r)^4)^2. Transient combat lights stay radial
  (normal-independent).
- The original sphere-mapped shine (layer mode 3) reflects the sky and a quarter of the sun, occluded and shadowed, and
  counts as specular light.
- The sky dome is reflected at most at its visible level; the rest of the sky light is a uniform fill over the upper
  hemisphere in the sky light's colour (irradiance and reflections alike), so the dark night dome's horizon glow and
  moon are not magnified.
- Thin two-sided lit cut-outs (foliage) transmit `FoliageTranslucency` = 0.45 of the sun when lit from behind
  (`PristonMaterialAdapter.WithShape`).
- Mesopic vision (`PostFinal.fx`, CPU reference `PristonDisplayTransform.Mesopic`): before exposure, each pixel blends
  toward its scotopic luminance in the hue of the authored night light (`PristonDayNightCycle.NightHue`) by
  0.04 / (0.04 + Y) of its absolute luminance Y in cd/m^2. Moonlit ground turns blue-grey; lamp pools and daylight keep
  their colour.

## Conventions

- **One cbuffer per stage** (MonoGame's Vulkan effects): frame block first, then the pass's and the draw's values. A
  name is declared once per effect; a value both stages need is passed from the vertex stage as a varying. MonoGame
  uploads a stage's whole cbuffer when any parameter changes, so the C# side skips unchanged values and identical
  texture rebinds.
- **Shared declarations come in pairs:** `XDecl.hlsli` defines macros expanded inside the cbuffer (constants) and at
  file scope with the effect's registers (resources); `X.hlsli`, included after the cbuffer, holds the functions.
- **Every texture and sampler has an explicit register**, the sampler on its texture's index, at most 16 per stage.
  Every slot is bound before its first draw (`PristonShaderLibrary` binds a white 1x1 texture), so no draw samples an
  unbound slot. No uniform initialisers (the compiler drops them).
- **Vertex inputs:** each shader's input struct lists what it reads; the fork's runtime matches inputs by semantic.
  `Color` elements arrive as normalised floats.
- **Compute and storage resources:** a compute pass holds only its compute shader, with `[numthreads]`; read-only
  `StructuredBuffer` take t registers in any stage, `RW*` resources u registers in compute shaders only
  (`monogame/issues/005-compute-and-uav.md`, `006-structured-buffers.md`, `007-mgcb-profiles.md`,
  `008-shader-model-6.5.md`).
- **Clip space and uv:** D3D clip space, +Y up, depth 0 to 1. Scene and portrait depth is reversed, 1 at the near
  plane (`PristonDepth.Perspective`); shadow casters use the ordinary XNA range. Render
  target and texture coordinates start top-left, so a projected position maps to uv = (x, -y) * 0.5 + 0.5 (sun shadow
  lookup, full-screen triangle).
- **Matrices** are XNA row-vector matrices used as `mul(v, M)`. World space is Y-up, in Priston units.
- **Render targets:** MonoGame clears a `DiscardContents` target whenever it is bound, so targets rebound inside a
  frame (the scene, the shadow atlases, the LDR post target) use `PreserveContents` and are cleared explicitly.
- **Wind and water** share `VertexAnimationDecl.hlsli` between the lit and caster passes so displaced surfaces and their
  shadows stay aligned; `VertexAnimTime` wraps every 40 pi seconds, a whole number of cycles of each frequency
  (`PristonShaderLibrary.VertexAnimationPeriodSeconds`).
- **Motion target** (`SV_Target1` of the lit and pre-pass techniques): uv motion, previous view depth, view depth;
  negative depth on cut-outs marks them reactive for the resolve. Discarded when not bound.
- **Alpha cut-off** is 60/255 (`PristonTextureLoader.AlphaCutoffByte`), shared by the lit, pre-pass and caster
  techniques.

## Validation

`--verify-shaders <Content dir>` in the regression runner (see [testing.md](testing.md)) draws every technique on the
GPU and checks the material model against the same formulas in C#: emission, cut-out and UV scroll, the second layer,
the lit model and shadow receivers, distance fog toward a flat colour and a sky capture, caster depth (orthographic and
perspective), skinning, the selection rim, sky layers, display-referred output, the unlit effect, the post chain, the
motion target, exposure histogram and adaptation, bloom, the three display transforms, mesopic vision and the colour
grade against `PristonDisplayTransform`/`PristonColorGrading`, the temporal resolve, tiled light culling, image-based
lighting (night dome: no reflection brighter than the visible dome), lamp-lit actors behind occluders, vertex light on
both paths, screen-space reflections whose rays all miss, foliage coverage, and the volumetric light (scene x
transmittance + in-scatter with the exact optical depth). Sources: `tests/PristonTale.Tests/ShaderValidationProbe*.cs`.

## How to

- **Add a full-screen pass:** create `Passes/<Name>.fx` including `../Core/Common.hlsli` and `../Core/Fullscreen.hlsli`
  (use `VS_Fullscreen`), one pixel cbuffer, explicit registers; add a `#begin` block to `Content.mgcb` building
  `Effects/<Name>.fx`; add a C# owner that loads it with `PristonShaderLibrary.LoadRequired(content, "Effects/<Name>",
  log)` and draws with `PristonFullscreenTriangle.Draw`; call it from `PristonRenderer.Draw` or
  `PristonSceneTargets.Compose` inside a new `RenderSection` (`Diagnostics/PristonFrameProfiler.cs`); add a check to a
  `ShaderValidationProbe.*.cs` partial; update the pass table here and the frame graph in [rendering.md](rendering.md).
- **Add a compute pass:** the `.fx` holds only the compute technique (`cs_6_0`, `[numthreads]`); outputs are `RW*`
  in u registers; read results as `StructuredBuffer`/textures in later passes (no CPU read-back on the frame path).
- **Add a surface constant:** declare it once in the right cbuffer of `Surface.fx` (or in a `*Decl.hlsli` macro if
  several effects share it), set it in `PristonShaderLibrary` (per-frame values in `ApplyFrame`, per-draw values in the
  `Select*` path, skipping unchanged values), and cover it in the shader checks. Both `StaticSurface` and
  `SkinnedSurface` rebuild from the same source; guard factory-specific code with `#ifdef SKINNED`.
- **Add a texture slot to the surface:** pick the next free t/s index (16 max per stage), extend the resource macro or
  `Surface.fx`, and bind a fallback (`_textureLoader.White` or a neutral buffer) in `PristonShaderLibrary` so the slot
  is never unbound.
- **Change a display transform or the grade:** edit `PostFinal.fx` and its CPU reference
  (`PristonDisplayTransform.cs` or `PristonColorGrading.cs`) together; the probe compares them.
- **Change the exposure curve:** `CompensationStops` exists in both `PostExposure.fx` and
  `Lighting/PristonPhotometry.cs`; change both.
- **Change fog opacity:** `FogOpacity` in `Atmosphere/Fog.hlsli` mirrors `PristonDistanceFog.Level`; change both.

## Pitfalls

- An unused uniform is stripped by the compiler, so `effect.Parameters["Name"]` returns `null` and the C# setter throws
  a `NullReferenceException`; reference every parameter the owner sets, or look it up conditionally.
- Uniform initialisers and sampler-state defaults on uniforms are dropped; set values from C#.
- The surface effect's whole stage cbuffer re-uploads on any parameter change; redundant `SetValue` calls cost GPU
  bandwidth, not just CPU.
- The sky layers are display-referred art blended with original modes into an 8-bit target and decoded by
  `SkyComposite.fx`; do not move them into the HDR target or their additive glows change.
- Self-illuminated and additive materials take shading model 1; their brightness is display-referred by day and a fixed
  luminance at night. Changing `EmissiveLuminance` (`PristonRenderer`) changes night glows, not day ones, and never
  characters or monsters.
- The transient-light falloff (radial, original (1 - d/r)^2 range) differs from the stage-lamp inverse square on purpose.
- Skinned meshes cull back faces like the stage unless the material is two-sided; `VisibleFaceNormal` in
  `Material.hlsli` shades the side being seen of two-sided faces.
- Values shared between HLSL and C# with no compile-time link: `MAX_LOCAL_LIGHTS` (32, in `LightingDecl.hlsli` and
  `LightCull.fx`) / `PristonShaderLibrary.MaxLocalLightCount`, `MAX_BONES` (64) / `PristonShaderLibrary.MaxBones`,
  `TILE_SIZE` (16) / `PristonLightCulling.TileSize`, `BINS` (128) / `PristonAutoExposure.Bins`, `MAX_CASCADES` (4) /
  `PristonShadowCascades.MaxCascades`, the SMAA table sizes / `PristonSmaa`.

## Related

- [rendering.md](rendering.md): frame graph, targets, light model, settings flow.
- [client-settings.md](client-settings.md): options that switch passes on and off.
- [effects.md](effects.md): VFX data and the particle systems that draw through `Unlit` and `Vfx`.
- [testing.md](testing.md): regression runner and GPU probes.
- [monogame/README.md](../monogame/README.md): the Vulkan fork and its effect processor.
