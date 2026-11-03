using System.Buffers;
using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// Positions the effects attach to. Entity ids are the server entityIds; positions are client world space.
public interface IVfxAnchors
{
    bool TryGetPosition(ulong entityId, out Vector3 position);

    bool TryGetBone(ulong entityId, string bone, out Vector3 position);

    // The bone's full world transform, for models attached to a hand or a shield; by default a translation only.
    bool TryGetBoneWorld(ulong entityId, string bone, out Matrix world)
    {
        var found = TryGetBone(entityId, bone, out var position);
        world = Matrix.CreateTranslation(position);
        return found;
    }
}

// For scenes whose effects attach to points only.
public sealed class VfxNoAnchors : IVfxAnchors
{
    public static readonly VfxNoAnchors Instance = new();

    public bool TryGetPosition(ulong entityId, out Vector3 position)
    {
        position = default;
        return false;
    }

    public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
    {
        position = default;
        return false;
    }
}

// Side effects of a layer that live outside the particle system: scene lights and sounds.
public interface IVfxServices
{
    void AddLight(Vector3 position, Color color, int power, int decay);

    void PlaySound(string path, Vector3 position, float volume);
}

public struct VfxParticle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Lifetime;
    public float Rotation;
    public float Spin;
}

public readonly record struct VfxSpawn(ulong CasterId, ulong TargetId, Vector3 Point, float Facing);

// Runs every native effect instance. Instances, particle arrays and trail buffers are pooled, so a frame allocates
// nothing once the pools have warmed up; the budget caps live instances so a crowd of casters cannot run away.
public sealed class VfxSystem
{
    public const int MaxInstances = 1024;
    // The longest step an update advances; a longer frame advances by this.
    public const float MaxStepSeconds = 0.1f;
    // The fade distance of soft strips, ribbons, parts and component quads into the scene (emitters take their own).
    public const float StripSoftness = 8f;
    internal const int TrailPoints = 32;

    private readonly List<VfxInstance> _live = new(64);
    private readonly Stack<VfxInstance> _free = new();
    private readonly IVfxAnchors _anchors;
    private readonly IVfxServices? _services;
    private readonly VfxLibrary? _library;
    private uint _seed = 0x9E3779B9;

    // library resolves child effects; without one, child layers are skipped.
    public VfxSystem(IVfxAnchors anchors, IVfxServices? services = null, VfxLibrary? library = null)
    {
        _anchors = anchors;
        _services = services;
        _library = library;
    }

    public IReadOnlyList<VfxInstance> Instances => _live;

    public int ParticleCount
    {
        get
        {
            var count = 0;
            foreach (var instance in _live)
            {
                foreach (var emitter in instance.EmitterStates)
                {
                    count += emitter.Count;
                }

                foreach (var sequence in instance.SequenceStates)
                {
                    count += sequence.LiveCount;
                }

                foreach (var part in instance.PartStates)
                {
                    count += part.LiveCount;
                }
            }

            return count;
        }
    }

    public VfxHandle Spawn(CompiledVfx effect, in VfxSpawn spawn)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (_live.Count >= MaxInstances)
        {
            return default;
        }

        var instance = _free.Count > 0 ? _free.Pop() : new VfxInstance();
        _seed = unchecked(_seed * 1664525u + 1013904223u);
        instance.Begin(effect, spawn, _seed);
        _live.Add(instance);
        return new VfxHandle(instance);
    }

    public void Update(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
        {
            return;
        }

        seconds = MathF.Min(seconds, MaxStepSeconds);
        for (var index = _live.Count - 1; index >= 0; index--)
        {
            var instance = _live[index];
            instance.Update(seconds, _anchors, _services, this);
            if (!instance.Alive)
            {
                instance.Release();
                _live.RemoveAt(index);
                _free.Push(instance);
            }
        }
    }

    internal VfxHandle SpawnChild(VfxInstance parent, int index, Vector3 point)
    {
        var child = _library?.Child(parent.Effect, index);
        if (child is null)
        {
            return default;
        }

        var layer = parent.Effect.Definition.Children[index];
        var facing = layer.Turn ? parent.Spawn.Facing : 0f;
        return Spawn(child, new VfxSpawn(parent.Spawn.CasterId, parent.Spawn.TargetId, point, facing));
    }

    // A named effect cached by the caller once found (see VfxLibrary.Child: a miss is looked up again).
    internal CompiledVfx? Resolve(string name, ref CompiledVfx? cached, ref bool resolved)
    {
        if (!resolved && _library is not null && _library.TryGet(name, out var effect))
        {
            resolved = true;
            cached = effect;
        }

        return cached;
    }

    // An effect started by a part of another (an impact where a part lands, dust behind it), with its facing.
    internal VfxHandle SpawnFrom(VfxInstance parent, CompiledVfx effect, Vector3 point) =>
        Spawn(effect, new VfxSpawn(parent.Spawn.CasterId, parent.Spawn.TargetId, point, parent.Spawn.Facing));

    public void Clear()
    {
        foreach (var instance in _live)
        {
            instance.Release();
            _free.Push(instance);
        }

        _live.Clear();
    }
}

public sealed class VfxInstance
{
    internal EmitterState[] EmitterStates = [];
    internal TrailState[] TrailStates = [];
    internal ComponentState[] ComponentStates = [];
    internal SequenceState[] SequenceStates = [];
    internal PartState[] PartStates = [];
    private VfxHandle[] _children = [];
    private bool[] _lightsFired = [];
    private bool[] _soundsFired = [];
    private bool[] _childrenFired = [];
    private uint _random;
    private bool _stopped;
    private bool _cut;
    private bool _emissionEnded;
    private bool _travelling;
    private Vector3 _travelFrom;
    private Vector3 _travelTo;
    private float _travelSeconds;
    private float _travelAge;
    private float _arc;

    public CompiledVfx Effect { get; private set; } = null!;
    // Counts the uses of this pooled instance, so a holder can tell its effect from a later one.
    public int Generation { get; private set; }
    public VfxSpawn Spawn { get; private set; }
    public float Age { get; private set; }
    public bool Alive { get; private set; }
    public Vector3 CasterPosition { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public Matrix Orientation { get; private set; }

    // Ends emission now; live particles finish their lives (particles that live until stopped keep only their last
    // seconds).
    public void Stop() => _stopped = true;

    // Ends the effect at once and hides what it drew (a carried glow when its projectile lands).
    public void StopNow()
    {
        _stopped = true;
        _cut = true;
    }

    // Moves the spawn point from one position to another over the given time (projectiles); a slight arc lifts the
    // middle of the flight. The effect stops emitting when it arrives.
    public void Travel(Vector3 from, Vector3 to, float seconds, float arc = 0f)
    {
        _travelFrom = from;
        _travelTo = to;
        _travelSeconds = MathF.Max(0.01f, seconds);
        _travelAge = 0f;
        _arc = arc;
        _travelling = true;
        Spawn = Spawn with { Point = from };
    }

    public bool Travelling => _travelling;

    // Moves the spawn point (a source carried by something else); particles already born stay where they are.
    public void MoveTo(Vector3 point) => Spawn = Spawn with { Point = point };

    // A full orientation instead of the facing yaw (a shot along its flight direction).
    public void SetOrientation(Matrix orientation) => Orientation = orientation;

    internal void Begin(CompiledVfx effect, in VfxSpawn spawn, uint seed)
    {
        Effect = effect;
        Generation++;
        Spawn = spawn;
        Age = 0f;
        Alive = true;
        _stopped = false;
        _cut = false;
        _emissionEnded = false;
        _travelling = false;
        _random = seed | 1u;
        CasterPosition = spawn.Point;
        TargetPosition = spawn.Point;
        Orientation = Matrix.CreateRotationY(spawn.Facing);
        EmitterStates = Resize(EmitterStates, effect.Emitters.Length);
        for (var index = 0; index < effect.Emitters.Length; index++)
        {
            EmitterStates[index] ??= new EmitterState();
            EmitterStates[index].Begin(effect.Emitters[index]);
        }

        TrailStates = Resize(TrailStates, effect.Trails.Length);
        for (var index = 0; index < effect.Trails.Length; index++)
        {
            TrailStates[index] ??= new TrailState();
            TrailStates[index].Begin(effect.Trails[index]);
        }

        ComponentStates = Resize(ComponentStates, effect.Components.Length);
        for (var index = 0; index < effect.Components.Length; index++)
        {
            ComponentStates[index] ??= new ComponentState();
            ComponentStates[index].Begin(effect.Components[index]);
        }

        SequenceStates = Resize(SequenceStates, effect.Sequences.Length);
        for (var index = 0; index < effect.Sequences.Length; index++)
        {
            SequenceStates[index] ??= new SequenceState();
            SequenceStates[index].Begin(effect.Sequences[index]);
        }

        PartStates = Resize(PartStates, effect.Parts.Length);
        for (var index = 0; index < effect.Parts.Length; index++)
        {
            PartStates[index] ??= new PartState();
            PartStates[index].Begin(effect.Parts[index], ref _random);
        }

        var definition = effect.Definition;
        _lightsFired = ResizeCleared(_lightsFired, definition.Lights.Length);
        _soundsFired = ResizeCleared(_soundsFired, definition.Sounds.Length);
        _childrenFired = ResizeCleared(_childrenFired, definition.Children.Length);
        for (var index = 0; index < definition.Children.Length; index++)
        {
            SkipUnpicked(definition.Children, static child => child.Pick, index, _childrenFired, ref _random);
        }

        for (var index = 0; index < definition.Sounds.Length; index++)
        {
            SkipUnpicked(definition.Sounds, static sound => sound.Pick, index, _soundsFired, ref _random);
        }

        if (_children.Length != definition.Children.Length)
        {
            _children = new VfxHandle[definition.Children.Length];
        }
        else
        {
            Array.Clear(_children);
        }
    }

    internal void Release()
    {
        foreach (var emitter in EmitterStates)
        {
            emitter?.Release();
        }

        foreach (var sequence in SequenceStates)
        {
            sequence?.Release();
        }

        foreach (var part in PartStates)
        {
            part?.Release();
        }

        var children = Effect.Definition.Children;
        for (var index = 0; index < _children.Length; index++)
        {
            if (children[index].StopWithParent)
            {
                _children[index].Stop();
            }

            _children[index] = default;
        }
    }

    internal ulong EntityOf(VfxAnchor anchor) => anchor switch
    {
        VfxAnchor.Caster => Spawn.CasterId,
        VfxAnchor.Target => Spawn.TargetId,
        _ => 0u
    };

    internal Vector3 AnchorPosition(VfxAnchor anchor, string bone, Vector3 offset, IVfxAnchors anchors)
    {
        var entity = EntityOf(anchor);
        var basis = anchor switch
        {
            VfxAnchor.Caster => CasterPosition,
            VfxAnchor.Target => TargetPosition,
            _ => Spawn.Point
        };
        if (entity != 0 && bone.Length > 0 && anchors.TryGetBone(entity, bone, out var bonePosition))
        {
            basis = bonePosition;
        }

        return basis + Vector3.TransformNormal(offset, Orientation);
    }

    internal void Update(float dt, IVfxAnchors anchors, IVfxServices? services, VfxSystem system)
    {
        if (_cut)
        {
            CutChildren();
            Alive = false;
            return;
        }

        Age += dt;
        if (_travelling)
        {
            _travelAge += dt;
            var t = MathF.Min(1f, _travelAge / _travelSeconds);
            var point = Vector3.Lerp(_travelFrom, _travelTo, t);
            point.Y += _arc * 4f * t * (1f - t);
            Spawn = Spawn with { Point = point };
            if (t >= 1f)
            {
                _travelling = false;
                _stopped = true;
            }
        }

        if (Spawn.CasterId != 0 && anchors.TryGetPosition(Spawn.CasterId, out var caster))
        {
            CasterPosition = caster;
        }

        if (Spawn.TargetId != 0 && anchors.TryGetPosition(Spawn.TargetId, out var target))
        {
            TargetPosition = target;
        }

        var definition = Effect.Definition;
        var emitting = !_stopped && Age <= definition.Duration;
        if (!emitting && !_emissionEnded)
        {
            _emissionEnded = true;
            foreach (var sequence in SequenceStates)
            {
                sequence.EndUntilStopped();
            }
        }

        // Alive while a layer still runs or a one-shot is still due; an effect whose layers all ended does not wait
        // for its duration.
        var any = emitting && PendingOneShots(definition);
        for (var index = 0; index < EmitterStates.Length; index++)
        {
            any |= EmitterStates[index].Update(this, dt, emitting, anchors, ref _random);
        }

        for (var index = 0; index < TrailStates.Length; index++)
        {
            any |= TrailStates[index].Update(this, dt, emitting, anchors);
        }

        for (var index = 0; index < ComponentStates.Length; index++)
        {
            var state = ComponentStates[index];
            var source = state.Component.Source;
            if (source.Follow || !state.Anchored)
            {
                state.Anchor = AnchorPosition(source.Anchor, source.Bone, Vector3.Zero, anchors);
                state.Anchored = true;
            }

            any |= state.Update(this, dt, emitting, state.Anchor, ref _random);
        }

        for (var index = 0; index < SequenceStates.Length; index++)
        {
            var state = SequenceStates[index];
            var source = state.Sequence.Source;
            var origin = AnchorPosition(source.Anchor, source.Bone, state.Sequence.Offset, anchors);
            any |= state.Update(dt, emitting, origin, ref _random);
        }

        for (var index = 0; index < PartStates.Length; index++)
        {
            any |= PartStates[index].Update(this, dt, emitting, anchors, system, ref _random);
        }

        FireOneShots(definition, anchors, services, system, emitting);
        var children = definition.Children;
        for (var index = 0; index < _children.Length; index++)
        {
            var child = _children[index];
            if (!child.Alive)
            {
                _children[index] = default;
                continue;
            }

            var layer = children[index];
            if (layer.Follow)
            {
                child.MoveTo(AnchorPosition(layer.Anchor, layer.Bone, VfxDefinition.Vec3(layer.Offset), anchors));
            }

            // A child that stops with its parent ends when the parent's emission ends: by Stop or by its duration.
            if (!emitting && layer.StopWithParent)
            {
                child.Stop();
            }

            any = true;
        }

        Alive = any;
    }

    private void CutChildren()
    {
        var children = Effect.Definition.Children;
        for (var index = 0; index < _children.Length; index++)
        {
            if (children[index].StopWithParent)
            {
                _children[index].StopNow();
            }
        }
    }

    private bool PendingOneShots(VfxDefinition definition)
    {
        for (var index = 0; index < _lightsFired.Length; index++)
        {
            if (!_lightsFired[index])
            {
                return true;
            }
        }

        for (var index = 0; index < _soundsFired.Length; index++)
        {
            if (!_soundsFired[index])
            {
                return true;
            }
        }

        for (var index = 0; index < _childrenFired.Length; index++)
        {
            if (!_childrenFired[index])
            {
                return true;
            }
        }

        return definition.Emitters.Length == 0 && definition.Trails.Length == 0 && definition.Components.Length == 0 && definition.Sequences.Length == 0
            && definition.Children.Length == 0 && definition.Parts.Length == 0 && Age <= definition.Duration;
    }

    // Children, lights and sounds start only while the effect emits: once Stop() or its duration ends emission, a one-shot
    // still waiting for its delay never starts.
    private void FireOneShots(VfxDefinition definition, IVfxAnchors anchors, IVfxServices? services, VfxSystem system, bool emitting)
    {
        if (!emitting)
        {
            return;
        }

        for (var index = 0; index < definition.Children.Length; index++)
        {
            var layer = definition.Children[index];
            if (!_childrenFired[index] && Age >= layer.Delay)
            {
                _childrenFired[index] = true;
                var point = AnchorPosition(layer.Anchor, layer.Bone, VfxDefinition.Vec3(layer.Offset), anchors);
                _children[index] = system.SpawnChild(this, index, point);
            }
        }

        if (services is null)
        {
            return;
        }

        for (var index = 0; index < definition.Lights.Length; index++)
        {
            var light = definition.Lights[index];
            if (!_lightsFired[index] && Age >= light.Delay)
            {
                _lightsFired[index] = true;
                var position = AnchorPosition(light.Anchor, "", VfxDefinition.Vec3(light.Offset), anchors);
                services.AddLight(position, new Color(light.Color[0], light.Color[1], light.Color[2]), light.Power, light.Decay);
            }
        }

        for (var index = 0; index < definition.Sounds.Length; index++)
        {
            var sound = definition.Sounds[index];
            if (!_soundsFired[index] && Age >= sound.Delay)
            {
                _soundsFired[index] = true;
                var take = sound.Variants.Length == 0 ? 0 : (int)(NextUnit(ref _random) * (sound.Variants.Length + 1));
                var path = take == 0 ? sound.Path : sound.Variants[Math.Min(take, sound.Variants.Length) - 1];
                services.PlaySound(path, AnchorPosition(VfxAnchor.Caster, "", Vector3.Zero, anchors), sound.Volume);
            }
        }
    }

    // At the first member of a pick group, draws the member that plays and marks the others as already fired.
    private static void SkipUnpicked<T>(T[] layers, Func<T, int> pickOf, int index, bool[] fired, ref uint random)
    {
        var group = pickOf(layers[index]);
        if (group == 0)
        {
            return;
        }

        var members = 0;
        for (var other = 0; other < layers.Length; other++)
        {
            if (pickOf(layers[other]) != group)
            {
                continue;
            }

            if (other < index)
            {
                return;
            }

            members++;
        }

        var chosen = Math.Min(members - 1, (int)(NextUnit(ref random) * members));
        for (var other = index; other < layers.Length; other++)
        {
            if (pickOf(layers[other]) != group)
            {
                continue;
            }

            fired[other] = chosen != 0;
            chosen--;
        }
    }

    internal static float NextUnit(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) / 16777216f;
    }

    private static T[] Resize<T>(T[] array, int length) where T : class
    {
        if (array.Length == length)
        {
            return array;
        }

        var resized = new T[length];
        Array.Copy(array, resized, Math.Min(array.Length, length));
        return resized;
    }

    private static bool[] ResizeCleared(bool[] array, int length)
    {
        if (array.Length != length)
        {
            return new bool[length];
        }

        Array.Clear(array);
        return array;
    }
}

internal sealed class EmitterState
{
    public VfxParticle[] Particles = [];
    public int Count;
    public CompiledEmitter Emitter = null!;
    public Vector3 LastOrigin;
    private float _accumulator;
    private bool _burst;

    public void Begin(CompiledEmitter emitter)
    {
        Emitter = emitter;
        if (Particles.Length < emitter.Source.MaxParticles)
        {
            Release();
            Particles = ArrayPool<VfxParticle>.Shared.Rent(emitter.Source.MaxParticles);
        }

        Count = 0;
        _accumulator = 0f;
        _burst = false;
    }

    public void Release()
    {
        if (Particles.Length > 0)
        {
            ArrayPool<VfxParticle>.Shared.Return(Particles);
            Particles = [];
        }

        Count = 0;
    }

    public bool Update(VfxInstance instance, float dt, bool emitting, IVfxAnchors anchors, ref uint random)
    {
        var source = Emitter.Source;
        var localAge = instance.Age - source.Delay;
        var origin = instance.AnchorPosition(source.Anchor, source.Bone, Emitter.Offset, anchors);
        var shift = origin - LastOrigin;
        LastOrigin = origin;
        var active = emitting && localAge >= 0f && (source.EmitTime <= 0f || localAge <= source.EmitTime);
        if (active && !_burst)
        {
            _burst = true;
            shift = Vector3.Zero;
            for (var index = 0; index < source.Burst; index++)
            {
                Emit(instance, origin, ref random);
            }
        }

        if (active && source.Rate > 0f)
        {
            _accumulator += source.Rate * dt;
            while (_accumulator >= 1f)
            {
                _accumulator -= 1f;
                Emit(instance, origin, ref random);
            }
        }

        var drag = source.Drag > 0f ? MathF.Max(0f, 1f - source.Drag * dt) : 1f;
        var index2 = 0;
        while (index2 < Count)
        {
            ref var particle = ref Particles[index2];
            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                Particles[index2] = Particles[--Count];
                continue;
            }

            particle.Velocity = (particle.Velocity + Emitter.Gravity * dt) * drag;
            particle.Position += particle.Velocity * dt;
            if (source.Local)
            {
                particle.Position += shift;
            }

            particle.Rotation += particle.Spin * dt;
            index2++;
        }

        return Count > 0 || emitting && localAge < 0f || active;
    }

    private void Emit(VfxInstance instance, Vector3 origin, ref uint random)
    {
        if (Count >= Emitter.Source.MaxParticles)
        {
            return;
        }

        var source = Emitter.Source;
        ref var particle = ref Particles[Count++];
        var offset = ShapeOffset(source, ref random);
        particle.Position = origin + Vector3.TransformNormal(offset, instance.Orientation);
        var direction = Vector3.TransformNormal(ConeDirection(Emitter.Direction, Emitter.SpreadCos, source.Shape, offset, ref random), instance.Orientation);
        particle.Velocity = direction * VfxRandom.Range(source.Speed, ref random);
        particle.Age = 0f;
        particle.Lifetime = VfxRandom.Range(source.Lifetime, ref random);
        particle.Rotation = MathHelper.ToRadians(VfxRandom.Range(source.Rotation, ref random));
        particle.Spin = MathHelper.ToRadians(VfxRandom.Range(source.Spin, ref random));
    }

    private static Vector3 ShapeOffset(VfxEmitterDefinition source, ref uint random)
    {
        var radius = VfxRandom.Range(source.Radius, ref random);
        var angle = VfxInstance.NextUnit(ref random) * MathHelper.TwoPi;
        switch (source.Shape)
        {
            case VfxShape.Ring:
                return new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            case VfxShape.Disc:
                var r = radius * MathF.Sqrt(VfxInstance.NextUnit(ref random));
                return new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
            case VfxShape.Sphere:
                return VfxRandom.SphereDirection(VfxInstance.NextUnit(ref random) * 2f - 1f, angle) * radius;
            default:
                return Vector3.Zero;
        }
    }

    // A random direction within the cone; ring and disc shapes with no spread push outwards from the centre.
    private static Vector3 ConeDirection(Vector3 axis, float spreadCos, VfxShape shape, Vector3 offset, ref uint random)
    {
        if (spreadCos >= 0.9999f)
        {
            return shape is VfxShape.Ring or VfxShape.Disc or VfxShape.Sphere && offset.LengthSquared() > 1e-4f ? Vector3.Normalize(offset) : axis;
        }

        var cos = 1f - VfxInstance.NextUnit(ref random) * (1f - spreadCos);
        var sin = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
        var phi = VfxInstance.NextUnit(ref random) * MathHelper.TwoPi;
        var tangent = MathF.Abs(axis.Y) < 0.99f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.Up)) : Vector3.Right;
        var bitangent = Vector3.Cross(axis, tangent);
        return axis * cos + (tangent * MathF.Cos(phi) + bitangent * MathF.Sin(phi)) * sin;
    }
}

internal sealed class TrailState
{
    public readonly Vector3[] Points = new Vector3[VfxSystem.TrailPoints];
    public readonly float[] Ages = new float[VfxSystem.TrailPoints];
    public int Head;
    public int Count;
    public CompiledTrail Trail = null!;
    private float _sinceSample;

    public void Begin(CompiledTrail trail)
    {
        Trail = trail;
        Head = 0;
        Count = 0;
        _sinceSample = float.MaxValue;
    }

    public bool Update(VfxInstance instance, float dt, bool emitting, IVfxAnchors anchors)
    {
        var source = Trail.Source;
        for (var index = 0; index < Count; index++)
        {
            Ages[(Head - index + Points.Length) % Points.Length] += dt;
        }

        while (Count > 0 && Ages[(Head - Count + 1 + Points.Length) % Points.Length] > source.Lifetime)
        {
            Count--;
        }

        var localAge = instance.Age - source.Delay;
        var active = emitting && localAge >= 0f && (source.EmitTime <= 0f || localAge <= source.EmitTime);
        _sinceSample += dt;
        if (active && _sinceSample >= source.Lifetime / Points.Length)
        {
            _sinceSample = 0f;
            Head = (Head + 1) % Points.Length;
            Points[Head] = instance.AnchorPosition(source.Anchor, source.Bone, Trail.Offset, anchors);
            Ages[Head] = 0f;
            Count = Math.Min(Count + 1, Points.Length);
        }

        return Count > 0 || active || emitting && localAge < 0f;
    }
}
