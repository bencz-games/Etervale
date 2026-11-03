using System.Buffers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Effects.Vfx;

internal sealed class CompiledSequence
{
    public const int MaxTrailPoints = 32;

    public CompiledSequence(VfxSequenceDefinition source, Texture2D texture)
    {
        Source = source;
        Texture = texture;
        Offset = VfxDefinition.Vec3(source.Offset);
        RadiusMin = VfxDefinition.Vec3(source.RadiusMin);
        RadiusMax = VfxDefinition.Vec3(source.RadiusMax);
        GravityMin = VfxDefinition.Vec3(source.GravityMin);
        GravityMax = VfxDefinition.Vec3(source.GravityMax);
        // OrderBy is stable: events at the same time keep their authored order.
        var events = source.Events.OrderBy(static e => e.T).ToArray();
        Events = events;
        Min = events.Select(static e => VfxKeys.ToVector4(e.Min)).ToArray();
        Max = events.Select(static e => VfxKeys.ToVector4(e.Max)).ToArray();
        NextRamp = VfxKeys.NextRamps(events.Length, end => events[end].Ramp, (start, end) => events[end].Set == events[start].Set);
    }

    public VfxSequenceDefinition Source { get; }
    public Texture2D Texture { get; }
    public Vector3 Offset { get; }
    public Vector3 RadiusMin { get; }
    public Vector3 RadiusMax { get; }
    public Vector3 GravityMin { get; }
    public Vector3 GravityMax { get; }
    public VfxSequenceEvent[] Events { get; }
    public Vector4[] Min { get; }
    public Vector4[] Max { get; }
    public int[] NextRamp { get; }
}

public struct VfxSequenceParticle
{
    public float Age;
    public float Lifetime;
    public float EventTimer;
    public float Size;
    public float SizeStep;
    public float Stretch;
    public float StretchStep;
    public Vector4 Color;
    public Vector4 ColorStep;
    // Source position and the offset from it (effect space: the turn and orbit are applied when drawn).
    public Vector3 World;
    public Vector3 Local;
    public Vector3 Velocity;
    public Vector3 VelocityStep;
    public Vector3 Angle;
    public Vector3 AngleStep;
    public Vector3 Orbit;
    public Vector3 OrbitStep;
    public int TrailCount;
    internal int NextEvent;
}

// A particle stream of the original particle scripts (see VfxSequenceDefinition). Particles live in a pooled array;
// trail positions in a second pooled array of MaxTrailPoints per particle.
internal sealed class SequenceState
{
    public CompiledSequence Sequence = null!;
    public VfxSequenceParticle[] Particles = [];
    public bool[] Alive = [];
    public Vector3[] Trails = [];
    public int LiveCount;
    public bool Finished;
    public Vector3 Source;
    private float _time;
    private float _excess;
    private long _born;
    private int _lastNew;

    // Particle scripts give particles that live until their effect stops a lifetime of a thousand seconds; when the effect
    // stops, the original fast-forwards them to their last three seconds.
    private const float UntilStoppedLifetime = 60f;
    private const float StopTailSeconds = 3f;

    public void EndUntilStopped()
    {
        for (var index = 0; index < Sequence.Source.Count; index++)
        {
            ref var particle = ref Particles[index];
            if (Alive[index] && particle.Lifetime >= UntilStoppedLifetime && particle.Lifetime - particle.Age > StopTailSeconds)
            {
                particle.Age = particle.Lifetime - StopTailSeconds;
            }
        }
    }

    public void Begin(CompiledSequence sequence)
    {
        Sequence = sequence;
        var count = sequence.Source.Count;
        if (Particles.Length < count)
        {
            Release();
            Particles = ArrayPool<VfxSequenceParticle>.Shared.Rent(count);
            Alive = ArrayPool<bool>.Shared.Rent(count);
            if (sequence.Source.Shape == VfxSequenceShape.Trail)
            {
                Trails = ArrayPool<Vector3>.Shared.Rent(count * CompiledSequence.MaxTrailPoints);
            }
        }
        else if (sequence.Source.Shape == VfxSequenceShape.Trail && Trails.Length < count * CompiledSequence.MaxTrailPoints)
        {
            if (Trails.Length > 0)
            {
                ArrayPool<Vector3>.Shared.Return(Trails);
            }

            Trails = ArrayPool<Vector3>.Shared.Rent(count * CompiledSequence.MaxTrailPoints);
        }

        Array.Clear(Alive, 0, Alive.Length);
        LiveCount = 0;
        Finished = false;
        _time = 0f;
        _excess = 0f;
        _born = 0;
        _lastNew = 0;
    }

    public void Release()
    {
        if (Particles.Length > 0)
        {
            ArrayPool<VfxSequenceParticle>.Shared.Return(Particles);
            ArrayPool<bool>.Shared.Return(Alive);
            Particles = [];
            Alive = [];
        }

        if (Trails.Length > 0)
        {
            ArrayPool<Vector3>.Shared.Return(Trails);
            Trails = [];
        }

        LiveCount = 0;
    }

    public bool Update(float dt, bool emitting, Vector3 source, ref uint random)
    {
        if (Finished)
        {
            return false;
        }

        var definition = Sequence.Source;
        Source = source;
        if (_time > definition.Delay)
        {
            var trail = definition.Shape == VfxSequenceShape.Trail;
            for (var index = 0; index < definition.Count; index++)
            {
                if (!Alive[index])
                {
                    continue;
                }

                ref var particle = ref Particles[index];
                // A particle that lives until its effect stops is part of the effect (a monster's ground circle, which
                // the original traces on the monster's bone): it moves with the effect's anchor.
                if (definition.Follow || particle.Lifetime >= UntilStoppedLifetime)
                {
                    particle.World = source;
                }

                particle.Age += dt;
                particle.EventTimer += dt;
                if (particle.Age >= particle.Lifetime)
                {
                    Alive[index] = false;
                    LiveCount--;
                    continue;
                }

                if (trail)
                {
                    RecordTrail(index, ref particle);
                }

                particle.Local += particle.Velocity * dt;
                particle.Color += particle.ColorStep * dt;
                particle.Velocity += particle.VelocityStep * dt;
                particle.Size += particle.SizeStep * dt;
                particle.Angle += particle.AngleStep * dt;
                particle.Orbit += particle.OrbitStep * dt;
                particle.Stretch += particle.StretchStep * dt;
                // Gravity is drawn anew every frame.
                particle.Velocity += VfxRandom.Box(Sequence.GravityMin, Sequence.GravityMax, ref random) * dt;
                RunEvents(ref particle, ref random);
            }

            var rate = VfxRandom.Range(definition.Rate[0], definition.Rate[1], ref random);
            var newParticles = (int)(rate * dt);
            _excess += rate * dt - newParticles;
            if (_excess > 1f)
            {
                newParticles += (int)_excess;
                _excess -= (int)_excess;
            }

            if (definition.Loops > 0 && _born + newParticles > (long)definition.Loops * definition.Count)
            {
                newParticles = (int)((long)definition.Loops * definition.Count - _born);
                if (newParticles <= 0)
                {
                    newParticles = 0;
                    if (LiveCount <= 0)
                    {
                        Finished = true;
                    }
                }
            }

            if (emitting)
            {
                for (var made = 0; made < newParticles && LiveCount < definition.Count; made++)
                {
                    Create(source, ref random);
                }
            }
            else if (LiveCount <= 0)
            {
                Finished = true;
            }
        }
        else if (!emitting)
        {
            Finished = true;
        }

        _time += dt;
        return !Finished;
    }

    public ReadOnlySpan<Vector3> Trail(int index) => Trails.AsSpan(index * CompiledSequence.MaxTrailPoints, Particles[index].TrailCount);

    private void Create(Vector3 source, ref uint random)
    {
        var count = Sequence.Source.Count;
        var slot = _lastNew;
        for (var probe = 0; probe < count && Alive[slot]; probe++)
        {
            slot = slot + 1 >= count ? 0 : slot + 1;
        }

        if (Alive[slot])
        {
            return;
        }

        _lastNew = slot;
        Alive[slot] = true;
        LiveCount++;
        _born++;
        ref var particle = ref Particles[slot];
        particle = new VfxSequenceParticle
        {
            Size = 1f,
            Color = Vector4.One,
            Lifetime = VfxRandom.Range(Sequence.Source.Lifetime[0], Sequence.Source.Lifetime[1], ref random),
            Local = VfxRandom.Box(Sequence.RadiusMin, Sequence.RadiusMax, ref random),
            World = source
        };
        if (Sequence.Source.Shape == VfxSequenceShape.Trail)
        {
            Trails[slot * CompiledSequence.MaxTrailPoints] = particle.Local;
            particle.TrailCount = 1;
        }

        var events = Sequence.Events;
        var at = 0;
        while (at < events.Length && events[at].T == 0f)
        {
            Visit(at, ref particle, ref random);
            at++;
        }

        particle.NextEvent = at;
    }

    // Trails remember Stretch past positions newest first; without a length they keep the birth position and the last.
    private void RecordTrail(int index, ref VfxSequenceParticle particle)
    {
        var points = Trails.AsSpan(index * CompiledSequence.MaxTrailPoints, CompiledSequence.MaxTrailPoints);
        if (particle.Stretch > 0f)
        {
            var limit = Math.Min(CompiledSequence.MaxTrailPoints, (int)MathF.Ceiling(particle.Stretch));
            var kept = Math.Min(particle.TrailCount, limit - 1);
            for (var at = kept; at > 0; at--)
            {
                points[at] = points[at - 1];
            }

            points[0] = OrbitTurned(particle);
            particle.TrailCount = kept + 1;
            return;
        }

        if (particle.TrailCount == 1)
        {
            points[1] = points[0];
            points[0] = particle.Local;
            particle.TrailCount = 2;
        }
        else if (particle.TrailCount == 2)
        {
            points[0] = particle.Local;
        }
    }

    public static Vector3 OrbitTurned(in VfxSequenceParticle particle)
    {
        if (particle.Orbit == Vector3.Zero)
        {
            return particle.Local;
        }

        return Vector3.Transform(particle.Local, VfxAngles.Rotation(particle.Orbit));
    }

    private void RunEvents(ref VfxSequenceParticle particle, ref uint random)
    {
        var events = Sequence.Events;
        var index = particle.NextEvent;
        // A clock event that sends the clock back to itself would loop forever; a frame applies a bounded number.
        var budget = events.Length * 4;
        while (index < events.Length && events[index].T <= particle.EventTimer && budget-- > 0)
        {
            var before = particle.EventTimer;
            Visit(index, ref particle, ref random);
            if (particle.EventTimer != before)
            {
                // The event clock moved: continue from the first event at or after the new time.
                var restart = 0;
                while (restart < events.Length && events[restart].T < particle.EventTimer)
                {
                    restart++;
                }

                index = restart;
                continue;
            }

            index++;
        }

        particle.NextEvent = index;
    }

    private void Visit(int index, ref VfxSequenceParticle particle, ref uint random)
    {
        var e = Sequence.Events[index];
        var value = VfxRandom.Box(Sequence.Min[index], Sequence.Max[index], ref random);
        var next = Sequence.NextRamp[index];
        var span = 1f;
        var target = Vector4.Zero;
        if (next >= 0)
        {
            span = VfxKeys.Span(e.T, Sequence.Events[next].T);
            target = VfxRandom.Box(Sequence.Min[next], Sequence.Max[next], ref random);
        }

        switch (e.Set)
        {
            case VfxSequenceProperty.Size:
                Scalar(ref particle.Size, ref particle.SizeStep, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Stretch:
                Scalar(ref particle.Stretch, ref particle.StretchStep, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Color:
                if (!e.Ramp)
                {
                    particle.Color = value;
                }

                if (next >= 0)
                {
                    particle.ColorStep = (target - particle.Color) / span;
                }

                break;
            case VfxSequenceProperty.Red:
                Scalar(ref particle.Color.X, ref particle.ColorStep.X, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Green:
                Scalar(ref particle.Color.Y, ref particle.ColorStep.Y, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Blue:
                Scalar(ref particle.Color.Z, ref particle.ColorStep.Z, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Alpha:
                Scalar(ref particle.Color.W, ref particle.ColorStep.W, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Velocity:
                if (!e.Ramp)
                {
                    particle.Velocity = e.Mode switch
                    {
                        VfxVelocityMode.Outward => VfxRandom.SafeNormalize(particle.Local) * value.X,
                        VfxVelocityMode.Inward => -VfxRandom.SafeNormalize(particle.Local) * value.X,
                        _ => new Vector3(value.X, value.Y, value.Z)
                    };
                }

                if (next >= 0)
                {
                    particle.VelocityStep = (new Vector3(target.X, target.Y, target.Z) - particle.Velocity) / span;
                }

                break;
            case VfxSequenceProperty.VelocityX:
                Scalar(ref particle.Velocity.X, ref particle.VelocityStep.X, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.VelocityY:
                Scalar(ref particle.Velocity.Y, ref particle.VelocityStep.Y, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.VelocityZ:
                Scalar(ref particle.Velocity.Z, ref particle.VelocityStep.Z, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Angle:
                Vector(ref particle.Angle, ref particle.AngleStep, e.Ramp, value, next >= 0, target, span);
                break;
            case VfxSequenceProperty.AngleX:
                Scalar(ref particle.Angle.X, ref particle.AngleStep.X, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.AngleY:
                Scalar(ref particle.Angle.Y, ref particle.AngleStep.Y, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.AngleZ:
                Scalar(ref particle.Angle.Z, ref particle.AngleStep.Z, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Orbit:
                Vector(ref particle.Orbit, ref particle.OrbitStep, e.Ramp, value, next >= 0, target, span);
                break;
            case VfxSequenceProperty.OrbitY:
                Scalar(ref particle.Orbit.Y, ref particle.OrbitStep.Y, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.OrbitZ:
                Scalar(ref particle.Orbit.Z, ref particle.OrbitStep.Z, e.Ramp, value.X, next >= 0, target.X, span);
                break;
            case VfxSequenceProperty.Clock:
                particle.EventTimer = value.X;
                break;
        }
    }

    private static void Scalar(ref float value, ref float step, bool ramp, float drawn, bool hasNext, float target, float span)
    {
        if (!ramp)
        {
            value = drawn;
        }

        if (hasNext)
        {
            step = (target - value) / span;
        }
    }

    private static void Vector(ref Vector3 value, ref Vector3 step, bool ramp, Vector4 drawn, bool hasNext, Vector4 target, float span)
    {
        if (!ramp)
        {
            value = new Vector3(drawn.X, drawn.Y, drawn.Z);
        }

        if (hasNext)
        {
            step = (new Vector3(target.X, target.Y, target.Z) - value) / span;
        }
    }

}
