# 018. The window takes the requested size before the swapchain is created

Part: framework.

## Symptom

A game that asks for a back buffer size before the device exists (`PreferredBackBufferWidth`/`Height` set in its
constructor) got a first swapchain of the window's default size, 800 × 480. On Windows the driver then reports the
swapchain out of date and the runtime recreates it at the window's new size (the log shows `Swapchain: 2 images of
800x480`, `Swapchain out of date. Recreating swapchain...`, then the requested size). Where the driver does not report
it (seen on Linux), the swapchain kept 800 × 480 while the back buffer reported the requested size, until the player
resized the window by hand.

## Cause

`GraphicsDeviceManager.PlatformInitialize` creates the native window at the window's own size and the device then
creates its swapchain from that window's surface. The requested size reaches the window only through
`OnPresentationChanged` after the device exists, which resizes the window but not the swapchain; a resize by hand works
because `ClientResize` resizes the window and then the swapchain.

## Change

`Platform/Native/GraphicsDeviceManager.Native.cs`: after creating the window, `PlatformInitialize` passes the
presentation parameters to the window (`OnPresentationChanged`: the size, and full screen when asked) before the
device is created, for every backend but OpenGL, whose back buffer is the window surface.

Found and fixed by Gabriel Asafe.

## Test

Windows, GTX 1050 Ti: `--verify-shaders` (an 800 × 600 window) creates one swapchain of 800 × 600 and no longer
recreates it; `--validate-display` creates its first swapchain at the 1280 × 720 it asks for and goes through windowed,
full-screen (1024 × 768) and borderless (1920 × 1080) modes and back. Not verified on Linux and macOS.
