# monogame/runtime: Etervale's MonoGame native runtime fork

Etervale's client runs on MonoGame's Native platform: `MonoGame.Framework` (the fork's C# half, built from source in
`../framework`) plus a native runtime library (`libmgruntime.so` / `mgruntime.dll` / `libmgruntime.dylib`). MonoGame
publishes the runtime as `MonoGame.Runtime.{Linux,Windows,Mac}.Vulkan`; the client uses none of those packages but this
fork, so this directory holds:
- the runtime's **source**, vendored;
- the **built libraries** (git-ignored).

The fork as a whole (runtime, framework, the change documents in `../issues`, the upstream notes in `../upstream`) is
described in [../README.md](../README.md).

Everything the build needs is in the repository except SDL2 and FAudio, which are fetched at pinned commits.

| Path | Contents |
|---|---|
| `src/` | MonoGame v3.8.6's `native/monogame` with the fork's changes applied: `common/`, `include/`, `sdl/`, `vulkan/` (with the stock effects compiled for Vulkan, `*.vk.mgfxo.h`, so no shader tool is needed), `faudio/` and `premake5.lua`. Only the Vulkan runtime (`desktopvk`) is built, so MonoGame's `opengl/`, `directx12/` and `xaudio/` are left out. |
| `src/external/` | Vendored, with their licences: volk (`volk.h`, `volk.c`), VulkanMemoryAllocator (`vk_mem_alloc.h`), the Vulkan C headers, stb (`stb_image.h`, `stb_image_write.h`). Fetched by `build.sh` (git-ignored): `sdl2/sdl`, `faudio`. |
| `externals.txt` | Every dependency pinned by commit (the commits of MonoGame v3.8.6's submodules), with its licence and whether it is vendored or fetched. |
| `monogame-v3.8.6.patch` | The record of what `src/` changes against upstream (unified diff of `premake5.lua`, `vulkan/MGG_Vulkan.cpp`, `sdl/MGP_sdl.cpp`, `include/api_enums.h` and `faudio/MGA_faudio.cpp`), rewritten by `regenpatch.sh` after a change to `src/`. |
| `bin/<rid>/` | Built libraries, **git-ignored: never committed**. `build.sh` writes the one of the host (Linux, macOS); `build-windows.ps1` writes `bin/win-x64/`. The client (`Client/MonoGameRuntime.targets`) copies the one for its platform next to its executable and fails to build without it. |
| `build-windows.ps1` | Builds the runtime on Windows into `bin/win-x64/` (see Building). |
| `build.sh` | Builds the runtime for this machine (Linux x64/arm64, macOS) into `bin/<rid>/`: fetches SDL2 and FAudio, applies `patches/`, builds them (again when their patches or configuration change), always relinks the runtime, and prints SDL2's audio backends. On Linux SDL2 has the ALSA, PulseAudio and PipeWire backends (loaded at run time); the build stops with the install command when their headers are missing. |
| `patches/<name>/*.patch` | the fork's fixes to a fetched dependency, applied after the checkout, one issue document each: `sdl2/0001-alsa-default-info-null-name.patch` (`../issues/010`: the ALSA backend wrote the default device's name through FAudio's NULL pointer). |
| `repro/` | The fork's verification programs, built with net10.0, nullable and warnings as errors (`repro/Directory.Build.props`). `StateCacheRepro`: a stress test of the state caches (change 6) and of render-target read-back (change 7) that checks every pixel against the blend equation, plus a banded target (change 8), 80 000 draws before one `Present` (change 9) and draws that change only a pixel-shader constant (change 11, `Content/PixelConstants.fx`); it runs on the fork (the framework of `../framework` and this runtime) or, with the property `EtervaleFork=false`, on the official MonoGame packages. `ComputeRepro`: the compute path of change 21 (`../issues/005`–`008`), one check per extension: a storage image written by a dispatch and sampled by a draw, a mip of a storage texture, RW structured and raw buffers, indirect dispatch, uploads and dispatches in frame order, an upload from another thread, dispatch constants across frames in flight, and the refusal of SM 6.5 ray queries; its effects (`Content/Compute.fx`, `Content/RayQuery.fx`) are built by the fork's processor (`../pipeline`). `TextureLoadRepro`: textures a pixel shader reads only with `Load` (`../issues/011`), at t0 with a `sampler_state` naming it, at t1 without, and a sampled control, each copied exactly. `DepthSurfaceRepro`: a depth surface (`../issues/016`) drawn by passes without colour and read back exactly with `Load` and `GatherRed`, kept into a later pass and cleared. `Directory.Packages.props` pins their MonoGame packages. None is part of the solution. |
| `../upstream/*.md` | One note per change: the problem and the fix, as material for an issue the owner files himself. `05-effect-array-elements.md` is about MonoGame's C# (worked around in application code, not part of the patch). MonoGame accepts no contributions written with generative AI (its CONTRIBUTING.md), so these changes are not sent as pull requests. |
| `../LICENSE-MonoGame.txt`, `CREDITS.md` | MonoGame's licence (MS-PL) and the credits of the vendored and fetched code. |

## The changes (`monogame-v3.8.6.patch`)

1. **`independentBlend`.** The runtime creates the Vulkan device without this feature, which a per-target blend state
   on MRT needs (`BlendState.IndependentBlendEnable`): for example a glass pass that blends colour premultiplied and
   leaves a linear-depth target alone.
2. **Render targets keep their content across frames.** The runtime started every render target with
   `VK_ATTACHMENT_LOAD_OP_DONT_CARE` the first time it was bound in a frame, whatever its `RenderTargetUsage`.
   - NVIDIA happens to keep the pixels. Tiled GPUs (Apple through MoltenVK) do not (MonoGame issue #9459).
   - Renderers draw over their targets across frames: TAA, volumetric and IBL histories, banded shadow and
     environment maps.
   - A target now loads its previous content unless it has never been written: the OpenGL behaviour of the DesktopGL
     reference.
3. **Render targets start in the shader-read layout.** A target sampled before its first draw (a history on its first
   frame) was in `COLOR_ATTACHMENT_OPTIMAL` while its descriptor expected `SHADER_READ_ONLY_OPTIMAL`: 20 validation
   errors per run, with the official runtime too.
4. **GPU timestamps.** `MGG_TimestampPool_{Create,Destroy,Reset,Write,Read,GetPeriod}` record `vkCmdWriteTimestamp`
   in the frame's own command buffer. The framework's `GpuTimestampPool` wraps them (`../issues/004`), and a per-section
   profiler can time every frame section with it. Against frame intervals they agree to 0.1 %.
5. **Vertex inputs by semantic.** The runtime gave the i-th input element location i. The elements follow the inputs
   mgfxc reflected, which are only the inputs the shader reads, while DXC keeps the original locations of the others.
   A vertex shader that leaves an input unread (a shadow caster reading position and UVs of the full vertex struct)
   got its UVs from nothing (`VUID-VkGraphicsPipelineCreateInfo-Input-07904`). `MGG_Shader_Create` now reads each
   input's location and semantic from the SPIR-V, and `MGG_InputLayout_Create` matches the elements by semantic.
6. **Exact-match caches, ids instead of addresses.** The blend, depth-stencil and rasterizer state caches, the
   render-pass/framebuffer cache and the pipeline cache looked entries up by a 32-bit FNV-1a value alone, and the
   pipeline key held raw pointers.
   - On a collision the runtime returned the wrong object. Everyday blend states collide: `DestinationColor/Zero`
     (multiply) and `Zero/InverseSourceAlpha` (alpha `BlendFactor/SourceAlpha`), both RGB, give `0x880212a5`, and the
     second drew as a multiply (`repro/StateCacheRepro`).
   - Each cached object now has an id that is never reused; pipelines are keyed by the ids, and every lookup compares
     the full key or the full description. A pipeline built for a destroyed object can no longer be found for a new
     one allocated at the same address (input layouts were deleted without purging their pipelines).
   - A state destroyed while the device still points at it is cleared from the current pipeline state (the device
     wrote the last-use frame into freed memory).
7. **`GetData` of a render target written this frame.** The flush before the copy checked only whether the target had
   been sampled this frame. A target drawn into and read back without being sampled was copied before its commands
   were submitted, so the read returned the previous frame's pixels.
8. **Viewport and scissor set after `SetRenderTarget`.** The render pass begins lazily at the next draw, and it reset
   the viewport and scissor rectangle to the full target, so a band or tile chosen in between was lost: a banded
   shadow map was overwritten by its last band and its shadow vanished. The full-target defaults now apply
   only where the application set nothing after binding the targets.
9. **Uniform buffer full before `Present`.** Every draw appends its constants to the frame's 32 MB uniform buffer,
   which starts over only at `Present`. Rendering the scene several times before one `Present` (a colour-chart
   capture: 8 frames per display transform) ran past its end, and in release the constants were silently not written
   (every measurement black). With less than 1 MB left, the commands so far are now submitted and the buffer starts over.
10. **Exact-match descriptor set cache.** The descriptor sets were cached by a 32-bit hash of the bound texture and
    sampler ids alone, like the state caches of change 6: a collision bound another draw's textures. Each set now keeps
    its full key, and the lookup compares it.
11. **Pixel-shader constants.** A change of constants was marked with one bit per shader stage and tested against the
    shader's mask of buffer slots, which matched only the vertex stage: a pixel shader's new constants were ignored
    unless a texture or the vertex shader's constants changed too. The GGX prefilter of the environment map drew every
    roughness level with the first level's roughness. The test now uses the stage's bit.
12. **Resize in the middle of a frame.** A window larger than the work area is resized by the window manager while a
    frame records; the swapchain rebuild restarted the frame's command buffer with a render pass still marked open and
    no target set, and the next `Clear` crashed. The commands so far are now submitted first and the targets rebound.
13. **Build:** `premake5.lua` builds only the Vulkan runtime, with the vendored stb, and writes to `../build`.

Changes 14–17 came from frames-in-flight races that the capture checks could not see, because `GetData` serialises the
frames. A video of normal play showed a preview with black frames, the ground darkening for single frames and the top
of the screen flickering.

14. **Buffers read by frames in flight** (`upstream/13-frames-in-flight-buffers.md`).
    - Draws stamp their vertex and index buffers.
    - Discarded buffers are reused only after `swapchainCount` frames, not after two (three swapchain images here).
    - `SetData` without `Discard` on a buffer still read by a frame renames it instead of writing the mapped memory in
      place.
15. **Texture uploads in frame order** (`upstream/14-texture-upload-order.md`).
    - `Texture.SetData` on the render thread goes into the frame's command buffer, ordered with the draws as in OpenGL.
    - Upstream submitted it ahead of the whole frame.
    - Uploaded textures are visible to vertex shaders too.
16. **A mid-frame submission waits for the swapchain image** (`upstream/15-flush-without-acquire.md`).
    - A flush, the resize path and `Present` all wait for the image acquisition, but only once per frame.
17. **Asynchronous frame capture** (`MGG_FrameCapture_*`, `upstream/16-async-frame-capture.md`).
    - Each `Present` copies the presented image into a host buffer of its frame slot, inside the frame's command
      buffer.
    - The copy is read when the slot's fence has been waited anyway, so capturing does not change the overlap of the
      frames.
    - A glitch check uses it, through the framework's `GraphicsDevice.BeginFrameCapture`/`TryPopFrame`
      (`../issues/003`).

18. **Unreachable enum defaults** (`MGVK_UNREACHABLE`, `upstream/17-unreachable-defaults.md`).
    - The 13 enum conversions that assert in their `default:` and fall off the end of the function tell the compiler
      the default cannot be reached (`__assume(0)` / `__builtin_unreachable()`).
    - Falling off the end was undefined behaviour in release builds, and MSVC reported it as C4715.

19. **Hidden window for automation** (`sdl/MGP_sdl.cpp`). With `MG_HIDDEN_WINDOW=1` in the environment, `Show` keeps
    the SDL window hidden (for automated captures and checks), and the runtime still presents to it, so an automated
    capture window does not cover a game being played on the same desktop. `regenpatch.sh` now covers
    `sdl/MGP_sdl.cpp` too.

Since the C# half was forked too (`../issues/001-framework-from-source.md`), each change has a document in
`../issues`:

20. **Interface version** (`MGG_Etervale_RuntimeVersion`, `../issues/002-runtime-version.md`): the framework refuses a
    library built from other sources before anything else.
21. **Compute, storage images and storage buffers** (`../issues/005-compute-and-uav.md`,
    `../issues/006-structured-buffers.md`; interface version 2):
    - `MGShaderStage::Compute` and `MGBufferType::Storage` (`include/api_enums.h`);
    - compute shaders get their own descriptor set layout and compute pipeline;
    - `MGG_Texture_CreateStorage` (storage images, refused for formats without storage support);
    - `MGG_GraphicsDevice_SetShaderImage` and `MGG_GraphicsDevice_SetShaderBuffer` (u slots and storage buffers per
      stage, in the descriptor cache key);
    - `MGG_GraphicsDevice_Dispatch` and `MGG_GraphicsDevice_DispatchIndirect` in the frame's command buffer, between a
      barrier from everything the frame wrote and one to everything that reads after, with the images' layouts;
    - `MGG_Buffer_SetStorageData`: ordered in the frame on the render thread; on another thread, done entirely by that
      thread with a dedicated staging allocation, because a pooled one held VMA's block-list lock across a 35 ms
      `vkAllocateMemory` and stalled `Present`;
    - `MGG_Buffer_GetStorageData`; a storage image written this frame is read back after the frame is submitted.
    `repro/ComputeRepro` checks each of them.
22. **Streaming audio buffers reused one at a time** (`faudio/MGA_faudio.cpp`, `../issues/009-audio-buffer-reuse.md`,
    `upstream/18-audio-buffer-reuse.md`): `MGA_Voice_AppendBuffer` used one free copy buffer and lost every earlier
    match when three or more fitted (a native leak under a streaming mixer).
23. **SDL2: the ALSA default-device crash and the missing sound servers** (`patches/sdl2`, `build.sh`,
    `../issues/010-sdl2-alsa-default-device.md`, `upstream/19-sdl2-alsa-default-device.md`): `ALSA_GetDefaultAudioInfo`
    wrote `*name` although `SDL_GetDefaultAudioInfo` allows NULL, which FAudio passes, so opening the device on ALSA
    segfaulted; SDL2 was configured without PipeWire and PulseAudio when their headers were missing, and the runtime
    was not relinked after SDL2 changed.
24. **Pipeline statistics** (`../issues/012-pipeline-statistics.md`; interface version 3): with `MG_PIPELINE_STATISTICS=1`
    at device creation and `VK_KHR_pipeline_executable_properties` present, pipelines are created with their statistics
    captured; `MGG_GraphicsDevice_GetPipelineStatistics` returns those of the last bound graphics or compute pipeline
    (registers, binary and stack size... as the driver reports them).

25. **NoOverwrite uploads in place** (`MGG_Buffer_SetDataNoOverwrite`, `../issues/013-no-overwrite-uploads.md`; interface
    version 4): `SetDataOptions.NoOverwrite` on a dynamic buffer no longer renames the buffer and copies its old
    content back from mapped memory (change 14 did it for every non-discard write), so `DrawUser*Primitives` and
    SpriteBatch keep their DesktopGL cost.
26. **32-bit float depth** (`MGDepthFormat::Depth32Float`, `../issues/014-float-depth.md`; interface version 5):
    `VK_FORMAT_D32_SFLOAT` without stencil, for a reversed depth range.
27. **Multisampled targets keep their samples** (`../issues/015-msaa-keeps-samples.md`): a pass that binds a
    multisampled target again loads and stores its samples instead of starting from undefined ones, as change 2 does
    for every other target.
28. **Depth surfaces** (`MGSurfaceFormat::Depth32Float`, `../issues/016-depth-surface-targets.md`; interface version
    6): a render target whose image is a `VK_FORMAT_D32_SFLOAT` depth buffer, drawn into by passes without colour and
    sampled through a depth view.
29. **Maximized state** (`MGP_Window_GetIsMaximized`, `MGP_Window_Maximize`, `../issues/017-window-maximize.md`;
    interface version 7): whether the SDL window is maximized, and maximizing it.
30. **Mono pan** (`common/mg_audio.cpp`, `../issues/019-mono-pan.md`, `upstream/20-mono-pan.md`): a mono voice's right
    channel was `-pan - 1` at or left of the centre (phase inverted, louder to the left); it is `pan + 1`.

The runtime also prints the swapchain image count and present mode, an early submission when the uniform buffer is
full, and a warning when two resources of one shader share a binding.

## Building

A fresh clone builds the runtime once before the client (the client build stops with this instruction otherwise; see
also the Build section of [../../README.md](../../README.md#build)):

```bash
monogame/runtime/build.sh          # ≈ 1–2 min at JOBS=2 (fetches SDL2/FAudio the first time)
```

`Directory.Build.props` in this folder stops MSBuild from importing the repository's C# settings (warnings as
errors at level 9999, analyzers) into the C++ project premake generates for Windows.

On Windows, `build-windows.ps1` builds the runtime: SDL2 and FAudio at the pinned commits, built statically with the
static C runtime by the CMake that ships with Visual Studio, premake 5.0.0-beta8 downloaded into `work/`, MSBuild of
`desktopvk` with the installed toolset (Visual Studio 2022 or 2026), and the library copied to `bin/win-x64/`. The
Vulkan SDK is not needed (volk and the headers are vendored):

```powershell
powershell -ExecutionPolicy Bypass -File monogame/runtime/build-windows.ps1
```

Linux needs cmake, make, a C++17 compiler, premake5, pkg-config and the development headers of the three sound
servers SDL2 is built for (Fedora: `alsa-lib-devel pulseaudio-libs-devel pipewire-devel`; Debian/Ubuntu:
`libasound2-dev libpulse-dev libpipewire-0.3-dev`), plus the version-control client that fetches SDL2 and FAudio. macOS needs Xcode and the Vulkan SDK (MoltenVK), with
`VULKAN_SDK` set. Windows uses `build-windows.ps1` (above).

A local build needs the glibc of the machine that built it (Fedora 44: 2.43). A portable Linux library is built on an
older distribution (Ubuntu 22.04: glibc 2.35), and release packages should ship that one.

## Updating MonoGame

1. Replace `src/` with the new tag's `native/monogame` (same subset).
2. Reapply the changes, guided by `monogame-v3.8.6.patch`.
3. Regenerate the stock effects with `dotnet-mgfxc <fx> <name>.vk.mgfxo.h /Profile:Vulkan`.
4. Update the commits in `externals.txt` and the vendored files.
5. Rebuild every platform; drop each change merged upstream.
6. Update `../framework` and `../pipeline` to the same tag (their READMEs), `MonoGame.Content.Builder.Task` in
   `Client/PristonTale/PristonTale.csproj` and `dotnet-mgcb` in `.config/dotnet-tools.json`.

## Licence

The runtime is MonoGame's code, under the Microsoft Public License (`../LICENSE-MonoGame.txt`):
- `src/` (with the fork's changes), the patch and the built libraries are distributed under the MS-PL;
- the vendored and statically linked libraries keep their own licences (`CREDITS.md`);
- Etervale's own code uses the runtime as a separate library.
