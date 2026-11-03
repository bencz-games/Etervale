using System.Diagnostics;
using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Net;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The whole authoritative pipeline without sockets: WorldServer ticks one field with N players and N/4 monsters, every
// player has a real WorldOutbox and PresenceEncoder, and the encoders run in parallel after each tick like the per
// connection pumps. Players walk (reports at 5 Hz through the command queue), a quarter fight a monster beside them and
// a tenth cast an area skill every few seconds; the monsters fight back.
internal static class PresenceLoadBenchmark
{
    private const int Field = 2;
    private const uint MechanicBomb = 0x01010002;
    private const int WarmupTicks = 100;
    public static long ReadTicks;
    public static long EncodeTicks;

    public static void Run(int[] counts, string[] layouts, int ticks)
    {
        Console.WriteLine($"Presence pipeline: WorldServer tick + per-player PresenceEncoder; 20 Hz; runtime={Environment.Version}; CPUs={Environment.ProcessorCount}");
        Console.WriteLine("players layout | tick p50/p99 ms | encode p50/p99 ms | total p99 ms | CPU cores | out B/client/s | B/client/tick | alloc KiB/tick | gen0/1/2 | heap MiB");
        foreach (var layout in layouts)
        {
            foreach (var count in counts)
            {
                Measure(count, layout == "crowded", ticks);
            }
        }
    }

    private static void Measure(int count, bool crowded, int ticks)
    {
        var monster = new MonsterDefinition(1, "Load target", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(40, 100, new MonsterRange(1, 1), 6, 50, 10, 0, 0, 100_000_000, ResistanceBlock.Zero), "")
        {
            Ai = new MonsterAi(5, MonsterNature.Evil, 200, 3, 200, new MonsterRange(1, 1), MonsterActiveHour.Any)
        };
        var holder = new WorldDataHolder
        {
            Current = new WorldData(new Simulation.Items.ItemCatalog([]), new MonsterCatalog([monster]), new Simulation.World.Npcs.NpcCatalog([]),
                new Dictionary<int, FieldSpawnData>(), Simulation.World.Motion.MotionTimingCatalog.Empty)
        };
        var world = new WorldServer(holder, new Simulation.World.WorldCatalog(), options: new WorldServerOptions { MaxPendingCommandsPerPlayer = 64 });
        var classes = new CharacterClassRegistry();
        var center = new Vector3(4184f, 0f, -11016f);
        var spacing = crowded ? 0f : 300f;
        var columns = (int)MathF.Ceiling(MathF.Sqrt(count));
        var players = new WorldPlayer[count];
        var outboxes = new WorldOutbox[count];
        var encoders = new PresenceEncoder[count];
        var sinks = new CountingSink[count];
        var homes = new Vector3[count];
        var random = new Random(17);
        var joins = new Task<bool>[count];
        for (var index = 0; index < count; index++)
        {
            homes[index] = crowded
                ? center + new Vector3((float)(random.NextDouble() * 500 - 250), 0f, (float)(random.NextDouble() * 500 - 250))
                : center + new Vector3((index % columns - columns / 2) * spacing, 0f, (index / columns - columns / 2) * spacing);
            var character = classes.CreatePlayer(PlayerClass.Mechanician, $"Load{index}", 40);
            character.Skills.Restore(MechanicBomb, 5);
            var player = new WorldPlayer((uint)index + 1, homes[index], character, false,
                new PlayerAppearanceView($"Load{index}", PlayerClass.Mechanician, 0, 0, 0, "", ""));
            players[index] = player;
            outboxes[index] = new WorldOutbox();
            encoders[index] = new PresenceEncoder(player.EntityId, holder);
            sinks[index] = new CountingSink();
            joins[index] = world.JoinAsync(Field, player, outboxes[index].Writer);
        }

        world.Tick(0.05f, 0);
        Assert.True(joins.All(join => join.IsCompletedSuccessfully && join.Result), "a benchmark player could not join");

        var field = world.FieldFor(Field)!;
        var sink = (ISpawnSink)field;
        var monsters = new ulong[(count + 3) / 4];
        for (var index = 0; index < monsters.Length; index++)
        {
            var at = homes[index * 4] + new Vector3(30f, 0f, 0f);
            monsters[index] = sink.Spawn(monster, at, -1, 0f).EntityId;
            field.SetAttackTarget(players[index * 4].EntityId, monsters[index], sustained: true);
        }

        var tickTimes = new List<double>(ticks);
        var encodeTimes = new List<double>(ticks);
        var totalTimes = new List<double>(ticks);
        long bytesStart = 0;
        long allocatedStart = 0;
        long tickAllocated = 0;
        long encodeAllocated = 0;
        var gcStart = (0, 0, 0);
        var cpuStart = TimeSpan.Zero;
        var wall = new Stopwatch();
        // PRESENCE_ALLOCATIONS=1 prints what the measured ticks allocated, by type.
        using var sampler = Environment.GetEnvironmentVariable("PRESENCE_ALLOCATIONS") == "1" ? new AllocationSampler() : null;
        for (uint tick = 1; tick <= WarmupTicks + ticks; tick++)
        {
            if (tick == WarmupTicks + 1)
            {
                bytesStart = sinks.Sum(value => value.Bytes);
                allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
                gcStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
                cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
                wall.Start();
                sampler?.Start();
            }

            Drive(world, field, players, homes, monsters, tick);
            var before = GC.GetTotalAllocatedBytes(precise: false);
            var started = Stopwatch.GetTimestamp();
            world.Tick(0.05f, tick);
            var ticked = Stopwatch.GetTimestamp();
            var afterTick = GC.GetTotalAllocatedBytes(precise: false);
            Parallel.For(0, count, index =>
            {
                var t0 = Stopwatch.GetTimestamp();
                while (outboxes[index].Reader.TryRead(out var snapshot))
                {
                    var t1 = Stopwatch.GetTimestamp();
                    Interlocked.Add(ref ReadTicks, t1 - t0);
                    encoders[index].Encode(snapshot, sinks[index]);
                    snapshot.ReleaseFrame();
                    Interlocked.Add(ref EncodeTicks, Stopwatch.GetTimestamp() - t1);
                    t0 = Stopwatch.GetTimestamp();
                }
            });
            var encoded = Stopwatch.GetTimestamp();
            if (tick > WarmupTicks)
            {
                tickAllocated += afterTick - before;
                encodeAllocated += GC.GetTotalAllocatedBytes(precise: false) - afterTick;
                tickTimes.Add(Stopwatch.GetElapsedTime(started, ticked).TotalMilliseconds);
                encodeTimes.Add(Stopwatch.GetElapsedTime(ticked, encoded).TotalMilliseconds);
                totalTimes.Add(Stopwatch.GetElapsedTime(started, encoded).TotalMilliseconds);
            }
        }

        wall.Stop();
        sampler?.Stop();
        var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds / wall.Elapsed.TotalMilliseconds;
        var bytes = sinks.Sum(value => value.Bytes) - bytesStart;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedStart;
        var seconds = ticks / (double)WorldServer.TickHz;
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{count,5} {(crowded ? "crowded" : "spread "),-7} | {P(tickTimes, 50),6:F2} / {P(tickTimes, 99),6:F2} | {P(encodeTimes, 50),6:F2} / {P(encodeTimes, 99),6:F2} | "
            + $"{P(totalTimes, 99),7:F2} | {cpu,5:F2} | {bytes / (double)count / seconds,9:F0} | {bytes / (double)count / ticks,7:F0} | "
            + $"{allocated / 1024.0 / ticks,8:F1} ({tickAllocated / 1024.0 / ticks:F0} tick, {encodeAllocated / 1024.0 / ticks:F0} encode) | "
            + $"{GC.CollectionCount(0) - gcStart.Item1}/{GC.CollectionCount(1) - gcStart.Item2}/{GC.CollectionCount(2) - gcStart.Item3} | "
            + $"{GC.GetTotalMemory(false) / 1048576.0,6:F0}"));
        sampler?.Print(ticks);
        Console.WriteLine($"    read {ReadTicks * 1000.0 / Stopwatch.Frequency / (WarmupTicks + ticks):F2} ms/tick cpu, encode {EncodeTicks * 1000.0 / Stopwatch.Frequency / (WarmupTicks + ticks):F2} ms/tick cpu");
        ReadTicks = 0;
        EncodeTicks = 0;
        foreach (var player in players)
        {
            _ = world.LeaveAsync(Field, player.EntityId, outboxes[player.EntityId - 1].Writer);
        }

        world.Tick(0.05f, (uint)(WarmupTicks + ticks + 1));
    }

    // Walkers report a circle at 5 Hz through the command queue; fighters stand by their monster; casters throw a bomb
    // every three seconds.
    private static void Drive(WorldServer world, FieldInstance field, WorldPlayer[] players, Vector3[] homes, ulong[] monsters, uint tick)
    {
        for (var index = 0; index < players.Length; index++)
        {
            if (index % 4 == 0)
            {
                continue;
            }

            if ((tick + (uint)index) % 4 == 0)
            {
                var angle = (tick + index * 7) * 0.05f * 100f / 60f / 4f;
                var position = homes[index] + new Vector3(MathF.Cos(angle) * 60f, 0f, MathF.Sin(angle) * 60f);
                world.TryReportMovement(Field, players[index].EntityId, position, angle + MathF.PI / 2f, true, false, 0, null);
            }

            if (index % 10 == 1 && (tick + (uint)index) % 60 == 0)
            {
                var player = players[index];
                player.Character.Mana = player.Character.MaxMana;
                player.Character.Stamina = player.Character.MaxStamina;
                field.CastSkill(player, new SkillCastRequest(tick, MechanicBomb, monsters[index / 4], default, false));
            }
        }
    }

    private static double P(List<double> values, int percentile)
    {
        var sorted = values.OrderBy(value => value).ToList();
        return sorted[Math.Min(sorted.Count - 1, sorted.Count * percentile / 100)];
    }

    private sealed class CountingSink : IPacketSink
    {
        public long Bytes;

        public bool Send(ReadOnlySpan<byte> packet)
        {
            Bytes += packet.Length;
            return true;
        }
    }
}
