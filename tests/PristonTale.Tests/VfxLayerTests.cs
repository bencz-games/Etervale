using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;

namespace PristonTale.Tests;

// Runtime rules of the component, sequence, child and part layers.
internal static class VfxLayerTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("component layers keep the original keyed-event, loop and delay rules", ComponentsAsync);
        yield return ("component particle pools respect their count, loops and the 100-particle cap", ComponentParticlesAsync);
        yield return ("sequence layers emit their budget, loop on clock events and stop on request", SequencesAsync);
        yield return ("child effects start late, follow their anchor with the particles that live until they stop, and stop with the parent", ChildrenAsync);
        yield return ("component and sequence updates allocate nothing once warmed up", AllocationsAsync);
        yield return ("parts wait for their delays, start on their ring and end with their life", PartRingAsync);
        yield return ("parts fly to their goal, start their end effect there and fade out", PartGoalAsync);
        yield return ("orbiting parts with a goal sweep at the goal's distance and bearing", PartOrbitAsync);
        yield return ("spiral parts follow their straight path turned by the swirl, settle at their move time and fade", PartSpiralAsync);
        yield return ("a handle reaches only its own effect once the pooled instance runs another", HandleAsync);
        yield return ("a part's degree angles turn it about X, then Y, then Z", RotationAsync);
    }

    private static Task RotationAsync()
    {
        foreach (var degrees in new[] { Vector3.Zero, new Vector3(90f, 0f, 0f), new Vector3(30f, -45f, 120f), new Vector3(-170f, 260f, 15f) })
        {
            var expected = Matrix.CreateRotationX(MathHelper.ToRadians(degrees.X)) * Matrix.CreateRotationY(MathHelper.ToRadians(degrees.Y))
                * Matrix.CreateRotationZ(MathHelper.ToRadians(degrees.Z));
            var actual = VfxAngles.Rotation(degrees);
            for (var cell = 0; cell < 16; cell++)
            {
                Assert.True(MathF.Abs(actual[cell] - expected[cell]) < 1e-5f, $"rotation {degrees} differs at {cell}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task HandleAsync()
    {
        var (library, system) = Setup(new VfxDefinition
        {
            Name = "test.short",
            Duration = 0.2f,
            Parts = [new VfxPartDefinition { Texture = "x.bmp", Count = 1, Lifetime = [0.1f, 0.1f] }]
        });
        library.TryGet("test.short", out var effect);
        var first = system.Spawn(effect, new VfxSpawn(0, 0, Vector3.Zero, 0f));
        Run(system, 1f);
        var second = system.Spawn(effect, new VfxSpawn(0, 0, Vector3.Zero, 0f));
        Assert.True(!first.Alive && second.Alive && ReferenceEquals(first.Instance, null), "the ended effect's handle still reaches the pooled instance");
        first.StopNow();
        Run(system, 0.05f);
        Assert.True(second.Alive, "a stale handle stopped the effect that reused its instance");
        return Task.CompletedTask;
    }

    private static (VfxLibrary Library, VfxSystem System) Setup(params VfxDefinition[] definitions)
    {
        var library = new VfxLibrary(_ => null, null!);
        foreach (var definition in definitions)
        {
            library.Register(definition);
        }

        return (library, new VfxSystem(new Anchors(), null, library));
    }

    private static Task PartRingAsync()
    {
        var (library, system) = Setup(new VfxDefinition
        {
            Name = "test.ring",
            Duration = 5f,
            Parts = [new VfxPartDefinition { Texture = "x.bmp", Count = 4, DelayStep = 0.1f, Ring = [10f, 10f], RingStep = 90f, Lifetime = [0.3f, 0.3f] }]
        });
        library.TryGet("test.ring", out var effect);
        var instance = system.Spawn(effect, new VfxSpawn(0, 0, new Vector3(5f, 0f, 0f), 0f)).Instance!;
        Run(system, 0.05f);
        Assert.True(instance.PartStates[0].LiveCount == 1, "only the first part is due");
        Run(system, 0.3f);
        var parts = instance.PartStates[0];
        Assert.True(parts.LiveCount == 3, $"{parts.LiveCount} parts live; the first ended with its life, the rest started");
        var second = parts.Parts[1].Position;
        Assert.True(Vector3.Distance(second, new Vector3(5f, 0f, 10f)) < 0.01f, $"second part at {second}");
        Run(system, 0.5f);
        Assert.True(!instance.Alive, "the effect ends when its parts have");
        return Task.CompletedTask;
    }

    private static Task PartGoalAsync()
    {
        var anchors = new Anchors { Target = new Vector3(100f, 0f, 0f) };
        var library = new VfxLibrary(_ => null, null!);
        library.Register(new VfxDefinition
        {
            Name = "test.shot",
            Duration = 5f,
            Parts =
            [
                new VfxPartDefinition
                {
                    Texture = "x.bmp", Anchor = VfxAnchor.Caster, Goal = VfxPartGoal.Target, Speed = 200f, End = VfxPartEnd.Hold, FadeOut = 0.2f,
                    EndEffect = "test.boom"
                }
            ]
        });
        library.Register(new VfxDefinition { Name = "test.boom", Duration = 1f, Parts = [new VfxPartDefinition { Texture = "x.bmp", Lifetime = [0.4f, 0.4f] }] });
        var system = new VfxSystem(anchors, null, library);
        library.TryGet("test.shot", out var effect);
        var shot = system.Spawn(effect, new VfxSpawn(1, 2, Vector3.Zero, 0f)).Instance!;
        Run(system, 0.3f);
        Assert.True(system.Instances.All(static instance => instance.Effect.Name != "test.boom"), "the end effect started before arrival");
        Run(system, 0.25f);
        var boom = system.Instances.FirstOrDefault(static instance => instance.Effect.Name == "test.boom");
        Assert.True(boom is not null && Vector3.Distance(boom.Spawn.Point, anchors.Target) < 0.01f, "the end effect starts where the part arrived");
        var part = shot.PartStates[0].Parts[0];
        Assert.True(Vector3.Distance(part.Position, anchors.Target) < 0.01f && shot.PartStates[0].Fade(part) < 1f, "the part holds and fades");
        Run(system, 0.25f);
        Assert.True(!shot.Alive, "the part ended after its fade");
        return Task.CompletedTask;
    }

    private static Task PartOrbitAsync()
    {
        var anchors = new Anchors { Caster = Vector3.Zero, Target = new Vector3(0f, 0f, 30f) };
        var library = new VfxLibrary(_ => null, null!);
        library.Register(new VfxDefinition
        {
            Name = "test.sweep",
            Duration = 5f,
            Parts =
            [
                new VfxPartDefinition
                {
                    Shape = VfxPartShape.Ribbon, Texture = "x.bmp", Anchor = VfxAnchor.Caster, Goal = VfxPartGoal.Target, OrbitRadius = 5f, OrbitSpeed = 90f,
                    Lifetime = [2f, 2f], Turn = false
                }
            ]
        });
        var system = new VfxSystem(anchors, null, library);
        library.TryGet("test.sweep", out var effect);
        var sweep = system.Spawn(effect, new VfxSpawn(1, 2, Vector3.Zero, 0f)).Instance!;
        system.Update(0.001f);
        var start = sweep.PartStates[0].Parts[0].Position;
        Assert.True(Vector3.Distance(start, anchors.Target) < 0.2f, $"the sweep starts at the target, not at {start}");
        Run(system, 1f);
        var later = sweep.PartStates[0].Parts[0].Position;
        Assert.True(MathF.Abs(later.Length() - 30f) < 0.01f && Vector3.Distance(later, start) > 30f, $"a quarter turn at the target's distance: {later}");
        return Task.CompletedTask;
    }

    // Moving 10 units a second along X for one second: a quarter turn at 90 degrees a second, or a swirl growing at 180
    // degrees a second each second, puts the part at -Z (the straight path turned), where it settles and fades.
    private static Task PartSpiralAsync()
    {
        static VfxPartDefinition Part(float swirl, float growth) => new()
        {
            Texture = "x.bmp", VelocityMin = [10f, 0f, 0f], VelocityMax = [10f, 0f, 0f], Swirl = [swirl, swirl], SwirlGrowth = [growth, growth],
            Spiral = true, MoveTime = [1f, 1f], End = VfxPartEnd.Hold, FadeOut = 0.5f
        };

        var (library, system) = Setup(new VfxDefinition { Name = "test.spiral", Duration = 5f, Parts = [Part(90f, 0f), Part(0f, 180f)] });
        library.TryGet("test.spiral", out var effect);
        var instance = system.Spawn(effect, new VfxSpawn(0, 0, Vector3.Zero, 0f)).Instance!;
        Run(system, 1.1f);
        foreach (var (layer, tolerance) in new[] { (0, 0.05f), (1, 0.3f) })
        {
            var parts = instance.PartStates[layer];
            var part = parts.Parts[0];
            Assert.True(Vector3.Distance(part.Position, new Vector3(0f, 0f, -10f)) < tolerance, $"layer {layer} ended at {part.Position}");
            Assert.True(part.Exiting && MathF.Abs(part.Velocity.Length() - 10f) < 0.01f && parts.Fade(part) < 1f,
                $"layer {layer} settles keeping its velocity and fades");
        }

        var settled = instance.PartStates[0].Parts[0].Position;
        Run(system, 0.2f);
        Assert.True(Vector3.Distance(instance.PartStates[0].Parts[0].Position, settled) < 1e-4f, "a settled part does not move");
        return Task.CompletedTask;
    }

    private static void Run(VfxSystem system, float seconds, float step = 0.01f)
    {
        for (var elapsed = 0f; elapsed < seconds - 1e-4f; elapsed += step)
        {
            system.Update(step);
        }
    }

    private static Task ComponentsAsync()
    {
        // A ramp at time 0 does not set its value: the colour moves from the initial colour to the ramp at 0.2 s.
        var (library, system) = Setup(new VfxDefinition
        {
            Name = "test.quad",
            Duration = 10f,
            Components =
            [
                new VfxComponentDefinition
                {
                    Kind = VfxComponentKind.Billboard, Texture = "x.bmp", EndTime = [0.5f, 0.5f], Loop = 2, Color = [255f, 255f, 255f, 50f],
                    Events =
                    [
                        new VfxComponentEvent(0f, VfxComponentEventKind.Color, true, [255f, 150f, 50f, 255f]),
                        new VfxComponentEvent(0.2f, VfxComponentEventKind.Color, true, [255f, 150f, 50f, 255f]),
                        new VfxComponentEvent(0.4f, VfxComponentEventKind.Size, false, [40f, 40f, 0f])
                    ]
                },
                new VfxComponentDefinition { Kind = VfxComponentKind.Ground, Texture = "x.bmp", Delay = 0.3f, EndTime = [0.2f, 0.2f], Loop = 1 }
            ]
        });
        library.TryGet("test.quad", out var effect);
        var instance = system.Spawn(effect, new VfxSpawn(0, 0, Vector3.Zero, 0f)).Instance!;
        Run(system, 0.1f);
        var quad = instance.ComponentStates[0];
        // Nine of the ten frames apply the ramp: the first frame only starts the clock.
        Assert.Near(142.25f, quad.Element.Color.W, 1f);
        Assert.Near(207.75f, quad.Element.Color.Y, 1f);
        Assert.True(instance.ComponentStates[1].Drawn, "a delayed quad is not drawn with its initial colour");
        Run(system, 0.35f);
        Assert.True(quad.Element.Size.X == 40f, "the size event did not apply");
        Run(system, 0.3f);
        Assert.True(quad.Live && quad.LoopCount == 1, "the first loop did not restart the quad");
        Run(system, 0.6f);
        Assert.True(!quad.Live && !instance.ComponentStates[1].Live, "the quads did not end after their loops");
        Run(system, 0.1f);
        Assert.True(system.Instances.Count == 0, "the effect stayed alive after its components ended");
        return Task.CompletedTask;
    }

    private static Task ComponentParticlesAsync()
    {
        var (library, system) = Setup(new VfxDefinition
        {
            Name = "test.pool",
            Duration = 10f,
            Components =
            [
                new VfxComponentDefinition
                {
                    Kind = VfxComponentKind.Particles, Texture = "x.bmp", Count = 300, Rate = 1000, Loop = 1, EndTime = [5f, 5f],
                    Spawn = VfxSpawnShape.Sphere, SpawnMin = [10f, 0f, 0f], SpawnMax = [10f, 0f, 0f], Outward = true, VelocityMin = [20f, 0f, 0f],
                    VelocityMax = [20f, 0f, 0f]
                }
            ]
        });
        library.TryGet("test.pool", out var effect);
        var instance = system.Spawn(effect, new VfxSpawn(0, 0, new Vector3(100f, 0f, 0f), 0f)).Instance!;
        Run(system, 1f);
        var pool = instance.ComponentStates[0];
        var live = pool.Particles.Count(static particle => particle.Live);
        Assert.True(live == ComponentState.MaxParticles, $"{live} particles live");
        var first = pool.Particles.First(static particle => particle.Live);
        var distance = Vector3.Distance(first.Position, new Vector3(100f, 0f, 0f));
        Assert.True(distance > 10.5f, $"outward particles did not move away ({distance})");
        Run(system, 6f);
        Assert.True(system.Instances.Count == 0, "the pool did not end after its loop");
        return Task.CompletedTask;
    }

    private static Task SequencesAsync()
    {
        var (library, system) = Setup(new VfxDefinition
        {
            Name = "test.sequence",
            Duration = 30f,
            Sequences =
            [
                new VfxSequenceDefinition
                {
                    Texture = "x.bmp", Count = 5, Loops = 2, Rate = [100f, 100f], Lifetime = [0.3f, 0.3f], GravityMin = [0f, -10f, 0f],
                    GravityMax = [0f, -10f, 0f],
                    Events =
                    [
                        new VfxSequenceEvent { T = 0f, Set = VfxSequenceProperty.Size, Min = [10f], Max = [10f] },
                        new VfxSequenceEvent { T = 0.2f, Ramp = true, Set = VfxSequenceProperty.Size, Min = [30f], Max = [30f] }
                    ]
                },
                new VfxSequenceDefinition
                {
                    Texture = "x.bmp", Count = 1, Rate = [10f, 10f], Lifetime = [100f, 100f],
                    Events =
                    [
                        new VfxSequenceEvent { T = 0.25f, Set = VfxSequenceProperty.Alpha, Min = [0f], Max = [0f] },
                        new VfxSequenceEvent { T = 0.5f, Ramp = true, Set = VfxSequenceProperty.Alpha, Min = [200f], Max = [200f] },
                        new VfxSequenceEvent { T = 1f, Set = VfxSequenceProperty.Clock, Min = [0.25f], Max = [0.25f] }
                    ]
                }
            ]
        });
        library.TryGet("test.sequence", out var effect);
        var instance = system.Spawn(effect, new VfxSpawn(0, 0, Vector3.Zero, 0f)).Instance!;
        Run(system, 0.1f);
        var stream = instance.SequenceStates[0];
        Assert.True(stream.LiveCount == 5, $"{stream.LiveCount} live of a pool of 5");
        Run(system, 1.5f);
        Assert.True(stream.Finished && stream.LiveCount == 0, "a stream of two loops kept emitting");
        var pulse = instance.SequenceStates[1];
        // The clock goes back to 0.25 s every second: the alpha restarts from 0 and never grows past one cycle's ramp.
        var peak = 0f;
        for (var frame = 0; frame < 300; frame++)
        {
            system.Update(0.01f);
            peak = MathF.Max(peak, pulse.Particles[0].Color.W);
        }

        Assert.True(pulse.LiveCount == 1 && peak is > 150f and < 650f && pulse.Particles[0].Color.W < peak,
            $"the clock event did not keep the pulse looping (peak {peak}, now {pulse.Particles[0].Color.W})");
        instance.Stop();
        Run(system, 0.1f);
        Assert.True(system.Instances.Count == 1, "stopping killed the live particle instead of letting it play out");
        return Task.CompletedTask;
    }

    private static Task ChildrenAsync()
    {
        var anchors = new Anchors { Caster = new Vector3(10f, 0f, 0f) };
        var library = new VfxLibrary(_ => null, null!);
        library.Register(new VfxDefinition
        {
            Name = "test.child",
            Duration = 30f,
            Sequences = [new VfxSequenceDefinition { Texture = "x.bmp", Count = 1, Rate = [10f, 10f], Lifetime = [100f, 100f] }]
        });
        library.Register(new VfxDefinition
        {
            Name = "test.parent",
            Duration = 1f,
            Children = [new VfxChildDefinition { Effect = "test.child", Anchor = VfxAnchor.Caster, Offset = [0f, 5f, 0f], Delay = 0.2f, Follow = true }]
        });
        var system = new VfxSystem(anchors, null, library);
        library.TryGet("test.parent", out var parent);
        var instance = system.Spawn(parent, new VfxSpawn(1, 0, Vector3.Zero, 0f)).Instance!;
        Run(system, 0.1f);
        Assert.True(system.Instances.Count == 1, "the child started before its delay");
        Run(system, 0.2f);
        Assert.True(system.Instances.Count == 2, "the child did not start");
        var child = system.Instances.First(item => item != instance);
        anchors.Caster = new Vector3(50f, 0f, 0f);
        Run(system, 0.05f);
        Assert.Near(50f, child.Spawn.Point.X, 0.01f);
        Assert.Near(5f, child.Spawn.Point.Y, 0.01f);
        Assert.Near(50f, child.SequenceStates[0].Particles[0].World.X, 0.01f);
        instance.Stop();
        Run(system, 0.05f);
        Assert.True(child.SequenceStates[0].LiveCount == 1, "a stopped child must let its particles play out");

        // A child whose delay passes the parent's duration never starts, though the parent's particles keep it alive.
        library.Register(new VfxDefinition
        {
            Name = "test.short",
            Duration = 0.1f,
            Sequences = [new VfxSequenceDefinition { Texture = "x.bmp", Count = 1, Rate = [100f, 100f], Lifetime = [100f, 100f] }],
            Children = [new VfxChildDefinition { Effect = "test.child", Anchor = VfxAnchor.Caster, Delay = 0.3f }]
        });
        library.TryGet("test.short", out var shortParent);
        var late = new VfxSystem(anchors, null, library);
        late.Spawn(shortParent, new VfxSpawn(1, 0, Vector3.Zero, 0f));
        Run(late, 0.5f);
        Assert.True(late.Instances.Count == 1 && late.Instances.Single().Effect.Definition.Name == "test.short",
            "a child started after its parent's emission ended, or the parent did not stay alive");
        return Task.CompletedTask;
    }

    private static Task AllocationsAsync()
    {
        var (library, system) = Setup(new VfxDefinition
        {
            Name = "test.mix",
            Duration = 60f,
            Components =
            [
                new VfxComponentDefinition { Kind = VfxComponentKind.Particles, Texture = "x.bmp", Count = 50, Rate = 100, Loop = 0, EndTime = [0.3f, 0.5f] },
                new VfxComponentDefinition
                {
                    Kind = VfxComponentKind.Billboard, Texture = "x.bmp", EndTime = [0.2f, 0.2f],
                    Events = [new VfxComponentEvent(0.1f, VfxComponentEventKind.Color, true, [0f, 0f, 0f, 0f])]
                }
            ],
            Sequences =
            [
                new VfxSequenceDefinition
                {
                    Texture = "x.bmp", Shape = VfxSequenceShape.Trail, Count = 20, Rate = [40f, 40f], Lifetime = [0.5f, 0.5f],
                    Events = [new VfxSequenceEvent { T = 0f, Set = VfxSequenceProperty.Stretch, Min = [8f], Max = [8f] }]
                }
            ],
            Parts =
            [
                new VfxPartDefinition { Shape = VfxPartShape.Ribbon, Texture = "x.bmp", Count = 3, OrbitRadius = 10f, OrbitSpeed = 200f, OrbitPhaseStep = 120f },
                new VfxPartDefinition { Shape = VfxPartShape.Streak, Texture = "x.bmp", Count = 20, Burst = [50f, 80f], Lifetime = [3f, 3f] }
            ]
        });
        library.TryGet("test.mix", out var effect);
        system.Spawn(effect, new VfxSpawn(0, 0, Vector3.Zero, 0f));
        Run(system, 1f);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Run(system, 1f);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 2048, "layer updates allocated per frame");
        return Task.CompletedTask;
    }

    private sealed class Anchors : IVfxAnchors
    {
        public Vector3 Caster { get; set; }

        public Vector3 Target { get; set; }

        public bool TryGetPosition(ulong entityId, out Vector3 position)
        {
            position = entityId == 2 ? Target : Caster;
            return entityId is 1 or 2;
        }

        public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
        {
            position = Vector3.Zero;
            return false;
        }
    }
}
