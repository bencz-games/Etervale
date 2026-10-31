using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.World;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Atmosphere;
using PristonTale.Engine3D.Rendering.Weather;
using PristonTale.Gameplay.Weather;
using Xna = Microsoft.Xna.Framework;

namespace PristonTale.Tests;

internal static class WeatherTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the rain follows the original's cycle of clouds, rain and clouds; each cycle draws its own rain and wind, and the rain swells and comes and goes", ScheduleAsync);
        yield return ("dungeons, rooms, the ice fields and Bellatra never take rain", FieldsAsync);
        yield return ("a game master's /weather sets every open field's weather, a dungeon stays clear, and auto gives it back", GameMasterAsync);
        yield return ("the weather reaches the client once when it changes and its packet refuses an unknown kind", WireAsync);
        yield return ("the client eases the clouds and the rain in and out, blows the wind its way and shows nothing with the setting off", PresentationAsync);
        yield return ("a storm's lightning keeps no beat, mostly far and faint with a few near and bright, thunders by its distance; plain rain never does", LightningAsync);
        yield return ("the rain stops on the highest surface of each cell, reading only the cells a moving centre uncovers", OcclusionAsync);
        yield return ("the realistic sky's clouds gather with the weather, its rainbow needs a light rain under a low sun, the moon turns through its phases and the cloud noise tiles", SkyAsync);
    }

    private static long At(long cycle, int minute) => (cycle * WeatherSchedule.DefaultCycleMinutes + minute) * WorldClock.MinuteMs;

    private static Task ScheduleAsync()
    {
        var start = WeatherSchedule.CloudsMinute;
        var end = start + WeatherSchedule.CloudyMinutes;
        var ramp = WeatherSchedule.RampMinutes;
        Assert.True(WeatherSchedule.At(At(3, start)) == WeatherState.Clear && WeatherSchedule.At(At(3, end)) == WeatherState.Clear,
            "the sky was not clear at the cloudy spell's edges");
        Assert.True(WeatherSchedule.At(At(3, start + 1)).Kind == WeatherKind.Overcast && WeatherSchedule.At(At(3, start + ramp)).Kind == WeatherKind.Overcast
            && WeatherSchedule.At(At(3, end - ramp)).Kind == WeatherKind.Overcast, "the clouds did not come an hour before and after the rain");
        var first = WeatherSchedule.At(At(3, start + ramp + 1));
        Assert.True(first.Raining && WeatherSchedule.At(At(3, start + 1)).Intensity == 0, $"the rain after the clouds was {first}");

        var kinds = new HashSet<WeatherKind>();
        for (var cycle = 0; cycle < 200; cycle++)
        {
            // The rain of a cycle keeps its kind and wind, starts lighter than it falls later and comes and goes.
            var densities = new List<byte>();
            for (var minute = start + ramp + 1; minute < end - ramp; minute++)
            {
                var rain = WeatherSchedule.At(At(cycle, minute));
                Assert.True(rain.Raining && rain.Kind == WeatherSchedule.At(At(cycle, start + 200)).Kind
                    && rain.WindHeading == WeatherSchedule.At(At(cycle, start + 200)).WindHeading, $"cycle {cycle} changed its rain at {minute}");
                densities.Add(rain.Intensity);
            }

            kinds.Add(WeatherSchedule.At(At(cycle, start + 200)).Kind);
            Assert.True(densities[0] < densities.Max() && densities.Max() <= WeatherSchedule.StormRain && densities.Min() >= WeatherSchedule.DensityStep
                && densities.Distinct().Count() > 3, $"cycle {cycle} rained {string.Join(",", densities.Distinct())}");
        }

        Assert.True(kinds.SetEquals([WeatherKind.Rain, WeatherKind.Storm]), "the cycles did not draw both rain and storms");
        var shortCycle = WeatherSchedule.MinimumCycleMinutes;
        Assert.True(WeatherSchedule.At((shortCycle + start + ramp + 1) * (long)WorldClock.MinuteMs, shortCycle).Raining,
            "a shorter cycle did not rain in its second cycle");
        return Task.CompletedTask;
    }

    private static Task FieldsAsync()
    {
        FieldDef Field(FieldState state, int code = 1) => new("f", "F", state, new FieldBackdrop("", "", ""), "", []) { FieldCode = code };
        Assert.True(WeatherSchedule.Rains(Field(FieldState.Forest)) && WeatherSchedule.Rains(Field(FieldState.Village))
            && WeatherSchedule.Rains(Field(FieldState.Desert)), "an open field did not take rain");
        Assert.True(!WeatherSchedule.Rains(Field(FieldState.Dungeon)) && !WeatherSchedule.Rains(Field(FieldState.Room))
            && !WeatherSchedule.Rains(Field(FieldState.Ice)) && !WeatherSchedule.Rains(Field(FieldState.Forest, FieldCodes.Bellatra)),
            "a covered field, an ice field or Bellatra took rain");
        return Task.CompletedTask;
    }

    private static async Task GameMasterAsync()
    {
        var open = new ChatFixture();
        var master = await open.JoinAsync("Master", 0, 0, gmLevel: 2);
        await open.SayAsync(master, "/weather storm 200");
        open.Tick(1);
        Assert.True(ChatFixture.Latest(master)?.Weather == new WeatherState(WeatherKind.Storm, 200, 32, 192),
            $"the field's weather was {ChatFixture.Latest(master)?.Weather} after /weather storm 200");
        Assert.True(master.Link.Last.StartsWith("> Weather Storm 200"), $"the command answered {master.Link.Last}");
        await open.SayAsync(master, "/weather clear");
        open.Tick(1);
        Assert.True(ChatFixture.Latest(master)?.Weather == WeatherState.Clear, "/weather clear left the rain");
        await open.SayAsync(master, "/weather auto");
        Assert.True(master.Link.Last.EndsWith("(schedule)"), $"/weather auto answered {master.Link.Last}");

        var dungeon = new ChatFixture(state: FieldState.Dungeon);
        var below = await dungeon.JoinAsync("Below", 0, 0, gmLevel: 2);
        await dungeon.SayAsync(below, "/weather rain");
        dungeon.Tick(1);
        Assert.True(ChatFixture.Latest(below)?.Weather == WeatherState.Clear, "it rained in a dungeon");
    }

    private static Task WireAsync()
    {
        var storm = new WeatherState(WeatherKind.Storm, 255, 200, 180);
        var buffer = new byte[WeatherPacket.Size];
        Assert.True(WeatherPacket.Read(buffer.AsSpan(0, new WeatherPacket(storm).Write(buffer))).Weather == storm, "the weather did not read back");
        buffer[WeatherPacket.Size - 4] = (byte)WeatherKind.Storm + 1;
        Assert.Throws<InvalidDataException>(() => WeatherPacket.Read(buffer), "an unknown weather kind was read");

        var wire = new WireRoundTrip(1);
        var self = new EntityView(1, EntityKind.Player, true, Vector3.Zero, 0f, MotionState.Stand, 1, 100, 100, default);
        WeatherState? Received(WeatherState weather) =>
            wire.Push(new WorldSnapshot(1, [self], fieldId: 3) { Weather = weather }).LastOrDefault(static update => update.Weather is not null)?.Weather;
        Assert.True(Received(WeatherState.Clear) == WeatherState.Clear, "the first snapshot did not send the weather");
        Assert.True(Received(WeatherState.Clear) is null, "an unchanged weather was sent again");
        Assert.True(Received(storm) == storm, "the storm did not reach the client");
        return Task.CompletedTask;
    }

    private static Task PresentationAsync()
    {
        var rain = new WeatherState(WeatherKind.Rain, 204, 64, 255);
        var weather = new WeatherPresentation(new Random(1));
        weather.Update(rain, enabled: true, WeatherPresentation.RainSeconds / 2f);
        Assert.True(weather.Rain is > 0.45f and < 0.55f && weather.Cloud is > 0.3f and < 0.4f,
            $"the rain eased in to {weather.Rain} and the clouds to {weather.Cloud} in two seconds");
        for (var second = 0; second < 10; second++)
        {
            weather.Update(rain, enabled: true, 1f);
        }

        Assert.True(weather.Cloud == 1f && MathF.Abs(weather.Rain - 0.8f) < 1e-4f, $"the rain settled at {weather.Rain}, the clouds at {weather.Cloud}");
        Assert.True(weather.Wind.X > WeatherPresentation.MaxWind * 0.99f && MathF.Abs(weather.Wind.Y) < 1f, $"the wind toward +X blew {weather.Wind}");
        weather.Update(rain, enabled: false, 1f);
        Assert.True(weather.Rain < 0.8f && weather.Rain > 0.5f, "turning the setting off did not ease the rain out");
        weather.Snap(rain, enabled: false);
        Assert.True(weather.View == PristonWeather.Clear with { Wind = Xna.Vector2.Zero }, "the setting off still showed weather");
        // A light rain breaks over the sun now and then; an overcast sky and a heavy rain never do.
        var breaks = new WeatherPresentation(new Random(3));
        var opened = 0f;
        for (var second = 0; second < 600; second++)
        {
            breaks.Update(new WeatherState(WeatherKind.Rain, 90, 0, 40), enabled: true, 1f);
            opened = MathF.Max(opened, breaks.SunBreak);
        }

        Assert.True(opened > 0.5f, "ten minutes of a light rain never broke over the sun");
        for (var second = 0; second < 600; second++)
        {
            breaks.Update(new WeatherState(WeatherKind.Rain, 200, 0, 40), enabled: true, 1f);
        }

        Assert.True(breaks.SunBreak == 0f, "a heavy rain kept a break over the sun");
        weather.Snap(WeatherState.Clear with { Kind = WeatherKind.Overcast }, enabled: true);
        Assert.True(weather.Cloud == 1f && weather.Rain == 0f, "an overcast sky did not snap to full cloud without rain");
        return Task.CompletedTask;
    }

    private static Task LightningAsync()
    {
        var weather = new WeatherPresentation(new Random(7));
        var thunders = new List<float>();
        weather.Thunder += thunders.Add;
        var strikes = new List<float>();
        var brightest = new List<float>();
        var flashing = false;
        const float step = 1f / 60f;
        const int minutes = 30;
        for (var frame = 0; frame < 60 * 60 * minutes; frame++)
        {
            weather.Update(new WeatherState(WeatherKind.Storm, 255, 0, 200), enabled: true, step);
            // A strike begins when the sky flashes after a dark moment; its brightness is the brightest of its flickers.
            if (weather.Flash > 0.2f && !flashing)
            {
                strikes.Add(frame * step);
                brightest.Add(0f);
            }

            flashing = weather.Flash > 0.01f;
            if (flashing && brightest.Count > 0)
            {
                brightest[^1] = MathF.Max(brightest[^1], weather.Flash);
            }
        }

        // A storm's strikes keep no beat: the waits vary from a quick follow-up to a long lull.
        var waits = strikes.Zip(strikes.Skip(1), static (a, b) => b - a).ToList();
        var mean = waits.Average();
        var spread = MathF.Sqrt(waits.Select(wait => (wait - mean) * (wait - mean)).Average()) / mean;
        Assert.True(strikes.Count is > minutes * 3 and < minutes * 20 && waits.Min() < 2.5f && waits.Max() > 25f && spread > 0.8f,
            $"{strikes.Count} strikes in {minutes} minutes, waits {waits.Min():F1} to {waits.Max():F1} s, spread {spread:F2}");
        Assert.True(brightest.Count(flash => flash < 0.5f) > brightest.Count / 2 && brightest.Max() > 0.8f,
            "most strikes were not far and faint, or none was near and bright");
        Assert.True(thunders.Count > minutes * 2 && thunders.Count < strikes.Count * 2
            && thunders.All(static volume => volume is >= WeatherPresentation.LeastThunder and <= 1f),
            $"a storm thundered {thunders.Count} times for {strikes.Count} strikes");

        var rain = new WeatherPresentation(new Random(7));
        rain.Thunder += _ => Assert.True(false, "plain rain thundered");
        for (var frame = 0; frame < 60 * 60; frame++)
        {
            rain.Update(new WeatherState(WeatherKind.Rain, 255, 0, 200), enabled: true, step);
            Assert.True(rain.Flash == 0f, "plain rain flashed");
        }

        return Task.CompletedTask;
    }

    private static Task OcclusionAsync()
    {
        // A roof 100 units up over x in [0, 64), the ground at 0 elsewhere, nothing past x = 1500.
        var stage = new CountingStage();
        var map = new PristonRainOcclusion();
        map.Reset(stage);
        for (var frame = 0; frame < 16; frame++)
        {
            map.Update(Xna.Vector3.Zero);
        }

        Assert.True(map.Ready && stage.Reads == PristonRainOcclusion.Cells * PristonRainOcclusion.Cells, $"filling the map read {stage.Reads} cells");
        Assert.True(map.HeightAt(30f, 10f) == 100f && map.HeightAt(-30f, 10f) == 0f && map.HeightAt(5000f, 0f) == PristonRainOcclusion.NoSurface,
            "the map did not hold the roof, the ground and nothing outside it");
        Assert.True(map.Sheltered(new Xna.Vector3(30f, 0f, 10f), 40f) && !map.Sheltered(new Xna.Vector3(-30f, 0f, 10f), 40f), "the roof did not shelter");
        var before = stage.Reads;
        map.Update(new Xna.Vector3(PristonRainOcclusion.CellSize * 3f, 0f, 0f));
        Assert.True(stage.Reads - before == PristonRainOcclusion.Cells * 3, $"moving three cells read {stage.Reads - before} cells");
        Assert.True(map.HeightAt(1500f, 0f) == PristonRainOcclusion.NoSurface, "a cell past the stage held a surface");
        return Task.CompletedTask;
    }

    private static Task SkyAsync()
    {
        var fair = PristonRenderer.CloudCoverage(0f, 0f);
        var overcast = PristonRenderer.CloudCoverage(1f, 0f);
        var storm = PristonRenderer.CloudCoverage(1f, 1f);
        Assert.True(fair is > 0.2f and < 0.45f && overcast > fair && storm > overcast && storm < 1f,
            $"the coverage ran {fair} fair, {overcast} overcast, {storm} in a storm");

        var lowSun = 0.3f;
        Assert.True(PristonRenderer.RainbowStrength(0.2f, 0.2f, lowSun) > 0.5f && PristonRenderer.RainbowStrength(1f, 0.2f, lowSun, sunBreak: 1f) > 0.5f,
            "a light rain under a low sun, or a sunshower's break, showed no rainbow");
        Assert.True(PristonRenderer.SunThrough(1f, 0f) == 0f && PristonRenderer.SunThrough(0f, 0f) == 1f && PristonRenderer.SunThrough(1f, 1f) > 0.5f,
            "an overcast sky let the sun through, a clear one held it back, or a break did not open it");
        Assert.True(PristonRenderer.RainbowStrength(1f, 0.2f, lowSun) == 0f, "an overcast light rain showed a rainbow without a break");
        Assert.True(PristonRenderer.RainbowStrength(0.2f, 0f, lowSun) == 0f && PristonRenderer.RainbowStrength(1f, 1f, lowSun) < 0.01f
            && PristonRenderer.RainbowStrength(0.2f, 0.2f, 1.2f) == 0f && PristonRenderer.RainbowStrength(0.2f, 0.2f, -0.1f) == 0f,
            "a rainbow showed without rain, in a downpour, at noon or after sunset");

        const long day = WorldClock.MinuteMs * (long)WorldClock.MinutesPerDay;
        Assert.True(WorldClock.MoonPhaseAt(0) == 0f && MathF.Abs(WorldClock.MoonPhaseAt(day * WorldClock.LunarDays / 2) - 0.5f) < 1e-4f
            && WorldClock.MoonPhaseAt(day * WorldClock.LunarDays) == 0f, "the moon did not run from new to full and back in its days");

        Span<float> low = stackalloc float[4];
        Span<float> high = stackalloc float[4];
        PristonCloudNoise.At(0f, 0.3f, 0.7f, low);
        PristonCloudNoise.At(1f, 0.3f, 0.7f, high);
        for (var channel = 0; channel < 4; channel++)
        {
            Assert.True(MathF.Abs(low[channel] - high[channel]) < 1e-4f && low[channel] is >= 0f and <= 1f, $"the noise's channel {channel} did not tile");
        }

        return Task.CompletedTask;
    }

    private sealed class CountingStage : IPristonCollisionQuery
    {
        public int Reads { get; private set; }

        public bool TryGetFloorHeight(Xna.Vector3 position, float referenceHeight, float stepUp, out float height)
        {
            Reads++;
            height = position.X is >= 0f and < 64f ? 100f : 0f;
            return position.X < 1500f;
        }

        public bool TryTraceSegment(Xna.Vector3 start, Xna.Vector3 end, out Xna.Vector3 hitPoint, out Xna.Vector3 hitNormal)
        {
            (hitPoint, hitNormal) = (default, default);
            return false;
        }
    }
}
