using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class RenderFidelityTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("mip chains reach 1x1 for every texture and average colour in linear light", MipChainLinearAsync);
        yield return ("mip chains weight colour by alpha so transparent key colours do not bleed", MipChainAlphaWeightedAsync);
        yield return ("cutout mip chains preserve the base alpha-test coverage", MipChainCoverageAsync);
        yield return ("data mip chains average the stored values and keep each packed normal pair a unit direction", MipChainDataAsync);
        yield return ("vertex animation clock wraps on a common period without a phase jump", VertexClockAsync);
        yield return ("frame profiler accumulates CPU sections without GPU timers", ProfilerAsync);
        yield return ("original light levels map to illuminance by the calibrated power curve", LightLevelsAsync);
    }

    private static Task VertexClockAsync()
    {
        var periodMs = PristonShaderLibrary.VertexAnimationPeriodSeconds * 1000.0;
        var beforeWrap = (uint)Math.Floor(periodMs);
        var afterWrap = beforeWrap + 1;
        var before = PristonShaderLibrary.VertexAnimationSeconds(beforeWrap);
        var after = PristonShaderLibrary.VertexAnimationSeconds(afterWrap);
        Assert.True(after < before, "fixture does not straddle the wrap");
        foreach (var frequency in new[] { 1.7, 1.9, 0.45, 0.5 })
        {
            // One millisecond apart, the displacement may only move by about frequency * 1 ms.
            var delta = Math.Abs(Math.Sin(frequency * before) - Math.Sin(frequency * after));
            Assert.True(delta < frequency * 0.002, $"frequency {frequency} jumps by {delta} across the clock wrap");
        }

        var oldWrap = Math.Abs(Math.Sin(1.7 * 99.999) - Math.Sin(1.7 * 0.0));
        Assert.True(oldWrap > 0.1, "the old 100 s wrap no longer reproduces the jump this test guards against");
        return Task.CompletedTask;
    }

    private static Task LightLevelsAsync()
    {
        static float Radiance(float level) =>
            PristonTale.Engine3D.Rendering.Lighting.PristonPhotometry.LevelToIlluminance(level)
            / PristonTale.Engine3D.Rendering.Lighting.PristonPhotometry.NightReferenceIlluminance;
        Assert.Near(1f, Radiance(1f), 1e-5f);
        Assert.Near(MathF.Pow(0.5f, PristonTale.Engine3D.Rendering.Lighting.PristonPhotometry.LevelExponent), Radiance(0.5f), 1e-5f);
        Assert.True(Radiance(1.05f) > 1f && Radiance(0f) == 0f && Radiance(-1f) == 0f, "levels above 1 or at 0 lost their curve");
        // Mixed light keeps more of its level than the sum of fully decoded parts would.
        var mixed = Radiance(0.16f) + Radiance(0.52f);
        Assert.True(mixed > 0.16f * 0.16f + 0.52f * 0.52f && mixed < 0.68f, $"mixed dusk light {mixed} left the expected range");
        return Task.CompletedTask;
    }

    private static Task ProfilerAsync()
    {
        using var profiler = new PristonTale.Engine3D.Diagnostics.PristonFrameProfiler();
        profiler.BeginFrame();
        profiler.Begin(PristonTale.Engine3D.Diagnostics.RenderSection.Opaque);
        Thread.SpinWait(20_000);
        profiler.End(PristonTale.Engine3D.Diagnostics.RenderSection.Opaque);
        profiler.BeginFrame();
        Assert.True(profiler.CpuLast(PristonTale.Engine3D.Diagnostics.RenderSection.Opaque) > 0, "CPU section was not recorded");
        Assert.True(profiler.CpuMeans().ContainsKey("Opaque") && !profiler.CpuMeans().ContainsKey("Sky"), "unused sections must be omitted");
        Assert.True(!profiler.GpuTimersAvailable && profiler.GpuMeans().Count == 0, "GPU timers must stay off until enabled on the render thread");
        return Task.CompletedTask;
    }

    private static Task MipChainLinearAsync()
    {
        var pixels = new[] { Color.Black, Color.White, Color.Black, Color.White, Color.White, Color.Black, Color.White, Color.Black };
        var levels = PristonMipChain.Build(pixels, 4, 2, 0);
        Assert.True(levels.Length == 3 && levels.Length == PristonMipChain.LevelCount(4, 2), $"expected 3 levels, got {levels.Length}");
        Assert.True(levels[2].Length == 1, "the chain did not end at 1x1");
        // Half black, half white is 0.5 linear, which encodes as sRGB 188; a gamma-space box would give 127.
        var middle = levels[1][0];
        Assert.True(middle.R is >= 187 and <= 189 && middle.A == 255, $"linear average expected sRGB 188, got {middle}");
        return Task.CompletedTask;
    }

    private static Task MipChainDataAsync()
    {
        var levels = PristonMipChain.BuildData([Color.Black, Color.White], 2, 1);
        Assert.True(levels[1][0].R is 127 or 128, $"a data chain did not average the stored value: {levels[1][0]}");
        // Two packed pairs: +X beside +Y (Z rebuilt as 0) average to 45 degrees once made unit length again, and two
        // flat texels stay flat, X and Y 0.5.
        var normal = PristonMipChain.BuildNormalPairs([new Color(255, 128, 128, 128), new Color(128, 255, 128, 128)], 2, 1)[1][0];
        var xy = new Vector2(normal.R, normal.G) / 255f * 2f - Vector2.One;
        Assert.True(MathF.Abs(xy.Length() - 1f) < 0.02f && MathF.Abs(xy.X - xy.Y) < 0.02f,
            $"the first pair is not unit length or turned: {xy}");
        Assert.True(normal.B is 127 or 128 && normal.A is 127 or 128, $"a flat second pair did not stay flat: {normal}");
        return Task.CompletedTask;
    }

    private static Task MipChainAlphaWeightedAsync()
    {
        var pixels = new[] { new Color(255, 0, 0, 255), new Color(0, 255, 0, 0) };
        var levels = PristonMipChain.Build(pixels, 2, 1, 0);
        var mip = levels[1][0];
        Assert.True(mip.R == 255 && mip.G == 0 && mip.B == 0, $"transparent texel bled into the visible colour: {mip}");
        Assert.True(mip.A is 127 or 128, $"alpha must stay the plain coverage average, got {mip.A}");
        var transparent = PristonMipChain.Build([new Color(0, 0, 255, 0), new Color(0, 0, 255, 0)], 2, 1, 0)[1][0];
        Assert.True(transparent.B == 255 && transparent.A == 0, $"fully transparent footprints keep the authored colour, got {transparent}");
        return Task.CompletedTask;
    }

    private static Task MipChainCoverageAsync()
    {
        // A checkerboard of alpha 100 and 0 averages to 50 per 2x2 block, below the 60 cutoff, so a plain box
        // filter would make the whole first mip disappear while half of the base level passes the test.
        var pixels = new Color[16];
        for (var index = 0; index < pixels.Length; index++)
        {
            var x = index % 4;
            var y = index / 4;
            pixels[index] = new Color(10, 200, 10, (x + y) % 2 == 0 ? 100 : 0);
        }

        var plain = PristonMipChain.Build(pixels, 4, 4, 0);
        Assert.True(plain[1].All(static texel => texel.A < PristonTextureLoader.AlphaCutoffByte), "fixture no longer erodes without correction");
        var cutout = PristonMipChain.Build(pixels, 4, 4, PristonTextureLoader.AlphaCutoffByte);
        Assert.True(cutout[1].All(static texel => texel.A >= PristonTextureLoader.AlphaCutoffByte), "coverage-preserving mip lost the cutout");
        Assert.True(cutout[0] == pixels, "the base level must remain the decoded image");
        return Task.CompletedTask;
    }
}
