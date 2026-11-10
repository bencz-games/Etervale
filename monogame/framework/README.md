# monogame/framework: MonoGame.Framework (C#), Native platform, from source

Etervale's MonoGame fork, C# half: `MonoGame.Framework.dll` for the Native platform (Vulkan through the runtime fork in
`monogame/runtime`), built from the source in this folder instead of the `MonoGame.Framework.Native` NuGet package.
The client projects (`Client/PristonTale`, `Client/PristonTale.Engine3D`, `Client/PristonTale.Gameplay`,
`Client/PristonTale.Effects`) reference `MonoGame.Framework/MonoGame.Framework.Native.csproj` as a project.

| Path | Contents |
|---|---|
| `MonoGame.Framework/` | MonoGame v3.8.6's `MonoGame.Framework` folder, only the files its `MonoGame.Framework.Native.csproj` compiles (479 with the two ports below): the shared XNA API and `Platform/Native` (the `MGG_*`/`MGP_*` interop to `mgruntime`). The other platforms (DesktopGL, WindowsDX, Android, iOS) are left out. |
| `MonoGame.Framework/MonoGame.Framework.Native.csproj` | The fork's project file: upstream's settings (netstandard2.1, C# 12, `NATIVE;NETSTANDARD;XNADESIGNPROVIDED;STBSHARP_INTERNAL`, unsafe code, XML documentation), assembly `MonoGame.Framework` 3.8.6, without upstream's packaging and the `IsAotCompatible` flag a netstandard2.1 library cannot carry (NETSDK1210). |
| `ThirdParty/StbImageSharp/src`, `ThirdParty/StbImageWriteSharp/src` | The C# ports of stb_image and stb_image_write, compiled into the assembly as upstream does (internal, `STBSHARP_INTERNAL`). |
| `Directory.Build.props` | Keeps the repository's C# settings out (its analyzers, nullable, implicit usings: upstream code was not written against them) and keeps every warning an error. |
| `CREDITS.md` | Licences of the code here. |

MonoGame's licence (MS-PL) is `../LICENSE-MonoGame.txt`.

## How it was obtained (pinned)

1. `git clone --depth 1 --branch v3.8.6 https://github.com/MonoGame/MonoGame.git` (tag commit
   `c610a529667336f63da323c6057274258727664b`, the same tag as the runtime in `../runtime`).
2. The compile set: `dotnet msbuild MonoGame.Framework/MonoGame.Framework.Native.csproj -getItem:Compile` in that
   checkout, with the `ThirdParty` submodules present; every listed file was copied with its path.
3. The submodules at the commits the tag pins (GitHub archives of the commits):
   `ThirdParty/StbImageSharp` `0ec6394bb7184e0576a3fa2e382a487c7ee82e9b` (https://github.com/StbSharp/StbImageSharp) and
   `ThirdParty/StbImageWriteSharp` `c14f96af618175c73c1cbedbf613c6a0d3c996ee` (https://github.com/StbSharp/StbImageWriteSharp).
   Only their `src/` is used.
4. Check: built unchanged, the assembly has the size of the NuGet package's `lib/netstandard2.1/MonoGame.Framework.dll`
   (762 880 bytes), so the compile set is the package's.

Updating MonoGame is a deliberate change (`../README.md`): the same steps on the new tag, the fork's changes reapplied
(listed below and in `../issues`), and rendered frames compared before and after.

## The fork's changes

Every change is marked `Etervale fork` in the source and has a document in `../issues` when it adds behaviour.

**Upstream warnings fixed at their cause** (the build treats warnings as errors):
- `CS8500`: `PtrTo<T>` constrained to `unmanaged`; `Texture2D`/`VertexBuffer` `SetData` from spans through
  `MemoryMarshal.AsBytes` instead of a `T*` of an unconstrained struct.
- `CS0618`: `ReflectionHelpers.SizeOf<T>` made private as upstream's own Obsolete text asked; the AOT-only Obsolete on
  `ManagedSizeOf` dropped (this build is not AOT); `ModelReader`'s use of the constructor deprecated for applications kept
  under a commented pragma.
- `CS0649`: `Video._frames` created (upstream never assigned it, so a decoded frame threw);
  `TouchPanelCapabilities.maximumTouchCount` set to 0 where there is no touch panel.
- `CS0067`: XNA events the Native platform never raises (`GraphicsDevice.DeviceLost`, `ResourceCreated`,
  `ResourceDestroyed`, `RenderTargetCube.ContentLost`, `Microphone.BufferReady`) kept for source compatibility under
  commented pragmas.
- `CS0162`, `CS0168`, `CS0169`: unreachable `Dispose` after a `return` in `Threading`, an unused catch variable, unused
  fields (`PresentationParameters.disposed`, `Shader._bytecode`).
- `CS1591`: XML comments on the `Dispose` overrides and finalizers that lacked them.

**Fixes:**
- `Platform/Native/GraphicsDeviceManager.Native.cs` (issue 018): the native window takes the requested size before the
  device creates its swapchain.

**The fork's additions:**
- `Platform/Native/Graphics.Interop.Etervale.cs`: the P/Invokes of the runtime fork's own exports, and the interface
  version check that runs before anything else (`MGG.EnsureRuntimeVersion`, issue 002).
- `GraphicsDevice.BeginFrameCapture`/`TryPopFrame`/`EndFrameCapture`, `PresentedFrame` (`Graphics/GraphicsDevice.FrameCapture.cs`,
  issue 003): the presented frames read back without synchronising.
- `GpuTimestampPool` (`Graphics/GpuTimestampPool.cs`, issue 004): GPU timestamps in the frame's command buffer.
- Compute and storage images (issue 005): `ShaderStage.Compute`; `EffectPass.IsCompute`, `ThreadGroupSizeX/Y/Z` and the
  compute `Apply`, which checks every storage parameter against the shader's declaration; `GraphicsDevice.Dispatch`,
  `DispatchIndirect`, `ComputeTextures`, `ComputeSamplerStates` (`Graphics/GraphicsDevice.Compute.cs`);
  `TextureShaderAccess` and the `Texture2D` constructor that takes it (`Graphics/TextureShaderAccess.cs`);
  `EffectParameterType.RWTexture2D`, `EffectParameter.SetValue(Texture2D, int mipLevel)`.
- Structured buffers (issue 006): `StructuredBuffer`, `BufferShaderAccess` (`Graphics/StructuredBuffer.cs`), `BufferType.Storage`;
  `EffectParameterType.StructuredBuffer`, `RWStructuredBuffer`, `ByteAddressBuffer`, `RWByteAddressBuffer`;
  `EffectParameter.SetValue(StructuredBuffer)`, `GetValueStructuredBuffer`.
- MGFX 12 (issues 007, 008): `Effect` reads versions 10 to 12; `Shader` reads the stage, the storage list
  (`StorageKind`, `StorageInfo`), the thread group size and the requirements (`ShaderRequirements`; a ray-query shader
  is refused with `NotSupportedException` until the runtime binds acceleration structures);
  `EffectParameterType.AccelerationStructure`.
