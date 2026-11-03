using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Units;

namespace PristonTale.Effects.Vfx;

internal sealed class CompiledComponent
{
    public CompiledComponent(VfxComponentDefinition source, Texture2D[] textures, PristonMeshEffect? model)
    {
        Source = source;
        Textures = textures;
        Model = model;
        Offset = VfxDefinition.Vec3(source.Offset);
        BeamFrom = VfxDefinition.Vec3(source.BeamFrom);
        BeamTo = VfxDefinition.Vec3(source.BeamTo);
        SpawnMin = VfxDefinition.Vec3(source.SpawnMin);
        SpawnMax = VfxDefinition.Vec3(source.SpawnMax);
        VelocityMin = VfxDefinition.Vec3(source.VelocityMin);
        VelocityMax = VfxDefinition.Vec3(source.VelocityMax);
        Color = new Vector4(source.Color[0], source.Color[1], source.Color[2], source.Color[3]);
        // Events are applied in time order; a set event's ramp is the next ramp of the same kind after it.
        var events = source.Events.OrderBy(static e => e.T).ToArray();
        Events = events;
        Values = events.Select(static e => VfxKeys.ToVector4(e.Value)).ToArray();
        NextRamp = VfxKeys.NextRamps(events.Length, end => events[end].Ramp, (start, end) => events[end].Kind == events[start].Kind);
    }

    public VfxComponentDefinition Source { get; }
    public Texture2D[] Textures { get; }
    public PristonMeshEffect? Model { get; }
    public Vector3 Offset { get; }
    public Vector3 BeamFrom { get; }
    public Vector3 BeamTo { get; }
    public Vector3 SpawnMin { get; }
    public Vector3 SpawnMax { get; }
    public Vector3 VelocityMin { get; }
    public Vector3 VelocityMax { get; }
    public Vector4 Color { get; }
    public VfxComponentEvent[] Events { get; }
    public Vector4[] Values { get; }
    public int[] NextRamp { get; }
}

// The animated values of a component, or of one particle of a particle component. Colours stay in the original
// 0..255 display scale; sizes are full extents.
public struct VfxElement
{
    public bool Live;
    public Vector4 Color;
    public Vector4 ColorStep;
    public Vector3 Size;
    public Vector3 SizeStep;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Direction;
    public bool Beam;
    public float Timer;
    public float EventTimer;
    public float EndTime;
    public float Frame;
    public int TextureFrame;
    public float TextureTimer;
    internal int NextEvent;
    internal bool Started;
}

// One component of a running effect. Quads and models have one element; particle components a pool of 100. The rules
// follow the original effect groups: the clock starts after Delay; quads end a loop after EndTime and models after
// ModelFrames, and every loop restarts the events at time 0 (without resetting values a ramp left behind); quads draw
// with their initial colour while delayed; particles are born at Rate up to Count x Loop and keep their world position.
internal sealed class ComponentState
{
    public const int MaxParticles = 100;
    // Component models play 30 clip frames a second.
    private const float ModelTicksPerSecond = 30 * GameUnits.FrameTicks;

    public CompiledComponent Component = null!;
    public VfxElement Element;
    public VfxElement[] Particles = [];
    public bool Live;
    public int LoopCount;
    public float Timer;
    // Where the component is attached this frame (anchor plus offset), and its facing.
    public Vector3 Origin;
    // The anchor position: taken once, or every frame for following components.
    public Vector3 Anchor;
    public bool Anchored;
    private float _emitCarry;
    private float _emitted;
    private int _liveParticles;

    public void Begin(CompiledComponent component)
    {
        Component = component;
        Live = true;
        Anchored = false;
        LoopCount = 0;
        Timer = 0f;
        _emitCarry = 0f;
        _emitted = 0f;
        _liveParticles = 0;
        var source = component.Source;
        Element = new VfxElement
        {
            Live = source.Kind != VfxComponentKind.Particles,
            Color = component.Color,
            Size = new Vector3(source.SizeMin[0], source.SizeMin[1], 20f),
            EndTime = source.EndTime[0]
        };
        if (source.Kind == VfxComponentKind.Particles)
        {
            if (Particles.Length != MaxParticles)
            {
                Particles = new VfxElement[MaxParticles];
            }
            else
            {
                Array.Clear(Particles);
            }
        }
    }

    public bool Drawn => Component.Source.Kind switch
    {
        VfxComponentKind.Model => Element.Live && Component.Source.Delay <= Timer,
        VfxComponentKind.Particles => _liveParticles > 0,
        _ => Element.Live
    };

    // Positions the component, then advances it. Returns whether it is still running.
    public bool Update(VfxInstance instance, float dt, bool emitting, Vector3 anchor, ref uint random)
    {
        if (!Live)
        {
            return false;
        }

        if (!emitting)
        {
            Live = false;
            Element.Live = false;
            _liveParticles = 0;
            return false;
        }

        var source = Component.Source;
        Origin = anchor + source.OffsetTurn switch
        {
            VfxOffsetTurn.Facing => Vector3.TransformNormal(Component.Offset, instance.Orientation),
            VfxOffsetTurn.Reverse => Vector3.TransformNormal(new Vector3(-Component.Offset.X, Component.Offset.Y, -Component.Offset.Z), instance.Orientation),
            _ => Component.Offset
        };
        switch (source.Kind)
        {
            case VfxComponentKind.Model:
                UpdateModel(dt);
                break;
            case VfxComponentKind.Particles:
                UpdateParticles(dt, ref random);
                break;
            default:
                UpdateQuad(dt);
                break;
        }

        return Live;
    }

    private void UpdateQuad(float dt)
    {
        var source = Component.Source;
        ref var element = ref Element;
        element.Position = Origin;
        if (source.Delay <= Timer)
        {
            element.Color += element.ColorStep * dt;
            if (source.Kind != VfxComponentKind.Beam)
            {
                element.Size += element.SizeStep * dt;
            }

            element.EventTimer += dt;
            if (element.EventTimer >= source.EndTime[0])
            {
                element.EventTimer = 0f;
                element.Started = false;
                LoopCount++;
                if (source.Loop > 0 && LoopCount >= source.Loop)
                {
                    Live = false;
                    element.Live = false;
                }
            }

            RunEvents(ref element);
            AdvanceTexture(ref element, dt);
        }

        Timer += dt;
    }

    private void UpdateModel(float dt)
    {
        var source = Component.Source;
        ref var element = ref Element;
        element.Position = Origin;
        if (source.Delay <= Timer)
        {
            element.Frame += ModelTicksPerSecond * dt;
            element.EventTimer += dt;
            element.Color += element.ColorStep * dt;
            if (element.Frame >= (int)(source.ModelFrames * GameUnits.FrameTicks))
            {
                element.Frame = 0f;
                element.EventTimer = 0f;
                element.Started = false;
                LoopCount++;
                if (source.Loop > 0 && LoopCount >= source.Loop)
                {
                    Live = false;
                    element.Live = false;
                }
            }

            RunEvents(ref element);
        }

        Timer += dt;
    }

    private void UpdateParticles(float dt, ref uint random)
    {
        var source = Component.Source;
        Timer += dt;
        var wanted = source.Rate * dt;
        if (_emitCarry >= 1f)
        {
            _emitCarry -= 1f;
            wanted += 1f;
        }

        var spawn = (int)wanted;
        _emitted += spawn;
        if (source.Loop > 0 && _emitted >= source.Count * source.Loop)
        {
            spawn = 0;
            if (_liveParticles == 0)
            {
                Live = false;
            }
        }

        _emitCarry += wanted - spawn;
        if (source.Count <= _liveParticles)
        {
            _emitCarry = 0f;
        }

        for (var slot = 0; slot < Particles.Length; slot++)
        {
            ref var particle = ref Particles[slot];
            if (particle.Live)
            {
                particle.Position += particle.Velocity * dt;
                particle.Color += particle.ColorStep * dt;
                particle.EventTimer += dt;
                particle.Timer += dt;
                RunEvents(ref particle);
                if (particle.EndTime <= particle.Timer)
                {
                    particle = default;
                    _liveParticles--;
                }
            }

            if (spawn > 0 && !particle.Live)
            {
                Emit(ref particle, ref random);
                _liveParticles++;
                spawn--;
                RunEvents(ref particle);
            }
        }
    }

    private void Emit(ref VfxElement particle, ref uint random)
    {
        var source = Component.Source;
        particle = default;
        particle.Live = true;
        var local = SpawnOffset(ref random);
        particle.EndTime = VfxRandom.Range(source.EndTime[0], source.EndTime[1], ref random);
        particle.Color = Component.Color;
        var height = VfxRandom.Range(source.SizeMin[1], source.SizeMax[1], ref random);
        particle.Size = new Vector3(VfxRandom.Range(source.SizeMin[0], source.SizeMax[0], ref random), height, height);
        if (!source.Outward)
        {
            particle.Velocity = RandomVelocity(ref random);
            if (source.Beams)
            {
                particle.Beam = true;
                particle.Direction = VfxRandom.SafeNormalize(RandomVelocity(ref random));
            }
        }
        else
        {
            var speed = VfxRandom.Range(Component.VelocityMin.X, Component.VelocityMax.X, ref random);
            var outward = VfxRandom.SafeNormalize(local);
            particle.Velocity = outward * speed;
            if (source.Beams)
            {
                particle.Beam = true;
                particle.Direction = speed < 0f ? -outward : outward;
            }
        }

        particle.Position = Origin + local;
    }

    private Vector3 SpawnOffset(ref uint random)
    {
        switch (Component.Source.Spawn)
        {
            case VfxSpawnShape.Box:
                return VfxRandom.Box(Component.SpawnMin, Component.SpawnMax, ref random);
            case VfxSpawnShape.Sphere:
            {
                var radius = VfxRandom.Range(Component.SpawnMin.X, Component.SpawnMax.X, ref random);
                var x = VfxRandom.Range(0f, 4f, ref random);
                var y = VfxRandom.Range(0f, 4f, ref random);
                var z = VfxRandom.Range(0f, 4f, ref random);
                var rotation = Matrix.CreateRotationX(x) * Matrix.CreateRotationY(y) * Matrix.CreateRotationZ(z);
                return Vector3.Transform(new Vector3(0f, 0f, radius), rotation);
            }
            case VfxSpawnShape.Doughnut:
            {
                var jitter = new Vector3(VfxRandom.Range(Component.SpawnMin.Y, Component.SpawnMax.Y, ref random), VfxRandom.Range(Component.SpawnMin.Y, Component.SpawnMax.Y, ref random),
                    VfxRandom.Range(Component.SpawnMin.Y, Component.SpawnMax.Y, ref random));
                jitter.Z += VfxRandom.Range(Component.SpawnMin.X, Component.SpawnMax.X, ref random);
                var yaw = VfxRandom.Range(0f, 8f, ref random);
                return Vector3.Transform(jitter, Matrix.CreateRotationY(yaw));
            }
            default:
                return Vector3.Zero;
        }
    }

    private Vector3 RandomVelocity(ref uint random) => VfxRandom.Box(Component.VelocityMin, Component.VelocityMax, ref random);

    private void AdvanceTexture(ref VfxElement element, float dt)
    {
        var frames = Component.Source.Frames;
        if (frames <= 0)
        {
            return;
        }

        if (element.TextureTimer > Component.Source.FrameTime)
        {
            element.TextureFrame++;
            element.TextureTimer = 0f;
            if (element.TextureFrame >= frames)
            {
                element.TextureFrame = 0;
            }
        }
        else
        {
            element.TextureTimer += dt;
        }
    }

    // Events at time 0 apply when an element (re)starts; later events once its event clock reaches them.
    private void RunEvents(ref VfxElement element)
    {
        var events = Component.Events;
        if (!element.Started)
        {
            var at = 0;
            while (at < events.Length && events[at].T == 0f)
            {
                Visit(at, ref element);
                at++;
            }

            element.NextEvent = at;
            element.Started = true;
            return;
        }

        var index = element.NextEvent;
        while (index < events.Length && events[index].T <= element.EventTimer)
        {
            Visit(index, ref element);
            index++;
        }

        element.NextEvent = index;
    }

    private void Visit(int index, ref VfxElement element)
    {
        var current = Component.Events[index];
        var value = Component.Values[index];
        var next = Component.NextRamp[index];
        if (current.Kind == VfxComponentEventKind.Color)
        {
            if (!current.Ramp)
            {
                element.Color = value;
            }

            if (next >= 0)
            {
                element.ColorStep = (Component.Values[next] - element.Color) / VfxKeys.Span(current.T, Component.Events[next].T);
            }

            return;
        }

        var size = new Vector3(value.X, value.Y, value.Z);
        if (!current.Ramp)
        {
            element.Size = size;
        }

        if (next >= 0)
        {
            var target = Component.Values[next];
            element.SizeStep = (new Vector3(target.X, target.Y, target.Z) - element.Size) / VfxKeys.Span(current.T, Component.Events[next].T);
        }
    }
}
