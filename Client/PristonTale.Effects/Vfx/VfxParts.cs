using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Scripts;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Units;

namespace PristonTale.Effects.Vfx;

internal sealed class CompiledPart
{
    public CompiledPart(VfxPartDefinition source, BillboardSequence? animation, Texture2D[] textures, PristonMeshEffect? model)
    {
        Source = source;
        Animation = animation;
        Textures = textures;
        Model = model;
        Offset = VfxDefinition.Vec3(source.Offset);
        JitterMin = VfxDefinition.Vec3(source.JitterMin);
        JitterMax = VfxDefinition.Vec3(source.JitterMax);
        Shell = VfxDefinition.Vec3(source.Shell);
        VelocityMin = VfxDefinition.Vec3(source.VelocityMin);
        VelocityMax = VfxDefinition.Vec3(source.VelocityMax);
        GoalOffset = VfxDefinition.Vec3(source.GoalOffset);
        Angles = VfxDefinition.Vec3(source.Angles);
        Tumble = VfxDefinition.Vec3(source.Tumble);
        BladeTip = VfxDefinition.Vec3(source.BladeAxis) * source.BladeLength;
        Color = new Vector4(source.Color[0], source.Color[1], source.Color[2], source.Color[3]);
        Blend = animation is null ? source.Blend : animation.BlendType switch
        {
            BillboardSequence.ColourBlend => VfxBlend.Color,
            BillboardSequence.AlphaBlend => VfxBlend.Alpha,
            BillboardSequence.ShadowBlend => VfxBlend.Shadow,
            _ => VfxBlend.Additive
        };
        Visible = source.Shape == VfxPartShape.Model ? model is not null : textures.Length > 0;
        TrailStride = source.Shape switch
        {
            VfxPartShape.Ribbon => Math.Clamp(source.TrailPoints, 2, MaxTrailPoints),
            VfxPartShape.Blade => Math.Clamp(source.TrailPoints, 2, MaxTrailPoints) * 2,
            _ => 0
        };
    }

    public const int MaxTrailPoints = 64;

    public VfxPartDefinition Source { get; }
    public BillboardSequence? Animation { get; }
    public Texture2D[] Textures { get; }
    public PristonMeshEffect? Model { get; }
    public Vector3 Offset { get; }
    // The blade's far end in bone space: BladeLength along BladeAxis.
    public Vector3 BladeTip { get; }
    public Vector3 JitterMin { get; }
    public Vector3 JitterMax { get; }
    public Vector3 Shell { get; }
    public Vector3 VelocityMin { get; }
    public Vector3 VelocityMax { get; }
    public Vector3 GoalOffset { get; }
    public Vector3 Angles { get; }
    public Vector3 Tumble { get; }
    public Vector4 Color { get; }
    public VfxBlend Blend { get; }
    public bool Visible { get; }
    // Trail slots per part: positions for ribbons, base and tip pairs for blades.
    public int TrailStride { get; }
    public CompiledVfx? EndEffect;
    public bool EndResolved;
    public CompiledVfx? EveryEffect;
    public bool EveryResolved;
    public CompiledVfx? CarryEffect;
    public bool CarryResolved;
}

internal enum PartPhase : byte
{
    Waiting,
    Live,
    Done
}

// One part: where it is relative to its origin, how it moves and how far its animation has played.
internal struct VfxPart
{
    public PartPhase Phase;
    public bool Arrived;
    public bool Exiting;
    public float Delay;
    public float Age;
    public float Life;
    public float ExitAge;
    public Vector3 Origin;
    public Vector3 Local;
    public Vector3 Start;
    public Vector3 Velocity;
    public float Gravity;
    public float MoveTime;
    public float Swirl;
    public float SwirlGrowth;
    public float Size;
    public float Height;
    public float HalfWidth;
    public float Scale;
    public float Rotation;
    public Vector3 Angles;
    public float OrbitAngle;
    public float OrbitRadius;
    public float OrbitSign;
    public BillboardPlayer Player;
    public float TickCarry;
    public float EveryTimer;
    public int TrailHead;
    public int TrailCount;
    public Matrix Attach;

    public readonly Vector3 Position => Origin + Local;
}

// The parts of one layer of a running effect. Parts wait for their delay, live until their life, animation, model clip
// or flight ends, and draw from the renderer. Ribbon and blade trails keep their samples in one array per layer.
internal sealed class PartState
{
    public CompiledPart Part = null!;
    public VfxPart[] Parts = [];
    public Vector3[] Trail = [];
    public float Timer;
    private Matrix _turn;
    // The carried effect of each part.
    private VfxHandle[] _carried = [];

    public void Begin(CompiledPart part, ref uint random)
    {
        Part = part;
        Timer = 0f;
        var source = part.Source;
        var count = Math.Max(1, source.Count);
        if (Parts.Length != count)
        {
            Parts = new VfxPart[count];
        }
        else
        {
            Array.Clear(Parts);
        }

        if (_carried.Length != count)
        {
            _carried = new VfxHandle[count];
        }
        else
        {
            Array.Clear(_carried);
        }

        var trail = count * part.TrailStride;
        if (Trail.Length < trail)
        {
            Trail = new Vector3[trail];
        }

        for (var index = 0; index < count; index++)
        {
            Parts[index].Delay = source.Delay + source.DelayStep * index + source.DelayRandom * VfxInstance.NextUnit(ref random);
        }
    }

    public int LiveCount
    {
        get
        {
            var count = 0;
            foreach (ref readonly var part in Parts.AsSpan())
            {
                if (part.Phase == PartPhase.Live)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool Update(VfxInstance instance, float dt, bool emitting, IVfxAnchors anchors, VfxSystem system, ref uint random)
    {
        var source = Part.Source;
        _turn = source.Turn ? instance.Orientation : Matrix.Identity;
        Timer += dt;
        var any = false;
        for (var index = 0; index < Parts.Length; index++)
        {
            ref var part = ref Parts[index];
            if (part.Phase == PartPhase.Waiting)
            {
                if (!emitting)
                {
                    part.Phase = PartPhase.Done;
                    continue;
                }

                if (Timer < part.Delay)
                {
                    any = true;
                    continue;
                }

                Birth(ref part, index, instance, anchors, ref random);
            }

            if (part.Phase != PartPhase.Live)
            {
                continue;
            }

            Step(ref part, index, instance, dt, emitting, anchors, system);
            if (source.CarryEffect.Length > 0)
            {
                Carry(ref part, index, instance, system);
            }

            any |= part.Phase == PartPhase.Live;
        }

        return any;
    }

    private void Carry(ref VfxPart part, int index, VfxInstance instance, VfxSystem system)
    {
        var carried = _carried[index];

        // As the original hides a projectile's glow when it lands, the carried effect ends with its part.
        if (part.Phase != PartPhase.Live)
        {
            carried.StopNow();
            _carried[index] = default;
            return;
        }

        if (carried.Alive)
        {
            carried.MoveTo(part.Position);
            return;
        }

        // A carried effect starts within the part's first update.
        if (part.Age <= VfxSystem.MaxStepSeconds && system.Resolve(Part.Source.CarryEffect, ref Part.CarryEffect, ref Part.CarryResolved) is { } effect)
        {
            _carried[index] = system.SpawnFrom(instance, effect, part.Position);
        }
    }

    // The effect ends: carried effects stop with it.
    public void Release()
    {
        for (var index = 0; index < _carried.Length; index++)
        {
            _carried[index].StopNow();
            _carried[index] = default;
        }
    }

    public Matrix Turn => _turn;

    private Vector3 Anchor(VfxInstance instance, IVfxAnchors anchors, ref VfxPart part)
    {
        var source = Part.Source;
        if (source.BoneWorld && source.Bone.Length > 0)
        {
            var entity = instance.EntityOf(source.Anchor);
            if (entity != 0 && anchors.TryGetBoneWorld(entity, source.Bone, out var bone))
            {
                part.Attach = Matrix.CreateTranslation(Part.Offset) * bone;
                return part.Attach.Translation;
            }
        }

        var basis = instance.AnchorPosition(source.Anchor, source.Bone, Vector3.Zero, anchors);
        part.Attach = Matrix.CreateTranslation(basis);
        return basis + Vector3.TransformNormal(Part.Offset, _turn);
    }

    private void Birth(ref VfxPart part, int index, VfxInstance instance, IVfxAnchors anchors, ref uint random)
    {
        var source = Part.Source;
        part.Phase = PartPhase.Live;
        part.Age = 0f;
        part.Origin = Anchor(instance, anchors, ref part);
        var local = VfxRandom.Box(Part.JitterMin, Part.JitterMax, ref random);
        var velocity = VfxRandom.Box(Part.VelocityMin, Part.VelocityMax, ref random);
        var ringed = source.Ring[1] > 0f || source.RingStep != 0f || source.RingRandom;
        var angle = source.RingRandom || !ringed ? VfxInstance.NextUnit(ref random) * 360f : source.RingStart + source.RingStep * index;
        var direction = new Vector3(MathF.Cos(MathHelper.ToRadians(angle)), 0f, MathF.Sin(MathHelper.ToRadians(angle)));
        if (ringed)
        {
            local += direction * VfxRandom.Range(source.Ring, ref random);
        }

        velocity += direction * VfxRandom.Range(source.Radial, ref random);
        velocity.Y += VfxRandom.Range(source.Rise, ref random);
        if (Part.Shell != Vector3.Zero || source.Burst[1] != 0f)
        {
            var shell = RandomDirection(ref random);
            local += shell * Part.Shell;
            velocity += shell * VfxRandom.Range(source.Burst, ref random);
        }

        part.Local = Vector3.TransformNormal(local, _turn);
        part.Velocity = Vector3.TransformNormal(velocity, _turn);
        if (source.Lift != 0f)
        {
            part.Velocity.Y = source.Lift;
        }

        part.Start = part.Position;
        part.Gravity = VfxRandom.Range(source.Gravity, ref random);
        part.MoveTime = Draw(source.MoveTime, ref random);
        part.Swirl = Draw(source.Swirl, ref random);
        part.SwirlGrowth = Draw(source.SwirlGrowth, ref random);
        part.Life = VfxRandom.Range(source.Lifetime, ref random);
        part.Size = VfxRandom.Range(source.Size, ref random);
        part.Height = source.Height[1] > 0f ? VfxRandom.Range(source.Height, ref random) : part.Size;
        part.HalfWidth = VfxRandom.Range(source.Width, ref random);
        part.Scale = VfxRandom.Range(source.Scale, ref random);
        part.Rotation = VfxRandom.Range(source.Rotation, ref random);
        part.Angles = Part.Angles;
        part.OrbitAngle = source.OrbitPhase + source.OrbitPhaseStep * index;
        part.OrbitSign = source.OrbitAlternate && (index & 1) == 1 ? -1f : 1f;
        if (Part.Animation is { } animation)
        {
            part.Player = BillboardPlayer.Start(animation, part.Size, source.Sized);
        }

        if (source.Shape == VfxPartShape.Beam)
        {
            part.Start = source.Goal != VfxPartGoal.None ? Goal(instance) : part.Position + Vector3.TransformNormal(VfxDefinition.Vec3(source.BeamTo), _turn);
        }

        if (Part.TrailStride > 0)
        {
            part.TrailHead = 0;
            part.TrailCount = 0;
            Sample(ref part, index);
        }

        if (source.OrbitRadius > 0f)
        {
            if (source.Goal != VfxPartGoal.None)
            {
                var toGoal = Goal(instance) - part.Origin;
                part.OrbitRadius = MathF.Max(source.OrbitRadius, new Vector2(toGoal.X, toGoal.Z).Length());
                part.OrbitAngle += MathHelper.ToDegrees(MathF.Atan2(toGoal.Z, toGoal.X));
            }
            else
            {
                part.OrbitRadius = source.OrbitRadius;
            }

            Orbit(ref part);
        }
    }

    private Vector3 Goal(VfxInstance instance)
    {
        var source = Part.Source;
        return source.Goal switch
        {
            VfxPartGoal.Caster => instance.CasterPosition + Vector3.TransformNormal(Part.GoalOffset, _turn),
            VfxPartGoal.Target => instance.TargetPosition + Vector3.TransformNormal(Part.GoalOffset, _turn),
            _ => instance.Spawn.Point + Vector3.TransformNormal(Part.GoalOffset, _turn)
        };
    }

    private void Step(ref VfxPart part, int index, VfxInstance instance, float dt, bool emitting, IVfxAnchors anchors, VfxSystem system)
    {
        var source = Part.Source;
        part.Age += dt;
        if (source.Follow || source.BoneWorld)
        {
            part.Origin = Anchor(instance, anchors, ref part);
        }

        var arrived = false;
        if (source.OrbitRadius > 0f)
        {
            Orbit(ref part);
        }
        else if (source.Shape == VfxPartShape.Beam)
        {
            part.Velocity = Vector3.Zero;
            if (source.Goal != VfxPartGoal.None)
            {
                part.Start = Goal(instance);
            }
        }
        else if (source.Goal != VfxPartGoal.None && !part.Arrived)
        {
            arrived = Fly(ref part, instance, dt);
        }
        else if (!(part.Arrived && source.End == VfxPartEnd.Hold) && (part.MoveTime <= 0f || part.Age <= part.MoveTime))
        {
            part.Velocity.Y += part.Gravity * dt;
            part.Local += part.Velocity * dt;
            if (part.Swirl != 0f || part.SwirlGrowth != 0f)
            {
                var turn = Matrix.CreateRotationY(MathHelper.ToRadians(part.Swirl * dt));
                part.Local = Vector3.Transform(part.Local, turn);
                if (source.Spiral)
                {
                    part.Velocity = Vector3.TransformNormal(part.Velocity, turn);
                }

                part.Swirl += part.SwirlGrowth * dt;
            }
        }
        else if (source.End == VfxPartEnd.Hold && part.MoveTime > 0f && !part.Arrived)
        {
            part.Arrived = true;
            End(ref part, instance, system);
            Exit(ref part);
        }

        part.Size += source.Growth * dt;
        part.Height += source.Growth * dt;
        part.Rotation += source.Spin * dt;
        part.Angles += Part.Tumble * dt;
        part.TickCarry += dt * GameUnits.TicksPerSecond;
        while (part.TickCarry >= 1f)
        {
            part.TickCarry -= 1f;
            if (Part.Animation is { } animation)
            {
                part.Player.Tick(animation, source.Loop ? BillboardPlayback.Loop : BillboardPlayback.Once);
            }

            if (Part.TrailStride > 0)
            {
                Sample(ref part, index);
            }
        }

        if (source.Every > 0f && source.EveryEffect.Length > 0)
        {
            part.EveryTimer += dt;
            while (part.EveryTimer >= source.Every)
            {
                part.EveryTimer -= source.Every;
                if (system.Resolve(source.EveryEffect, ref Part.EveryEffect, ref Part.EveryResolved) is { } every)
                {
                    system.SpawnFrom(instance, every, part.Position);
                }
            }
        }

        if (arrived)
        {
            part.Arrived = true;
            End(ref part, instance, system);
            switch (source.End)
            {
                case VfxPartEnd.Vanish:
                    part.Phase = PartPhase.Done;
                    return;
                case VfxPartEnd.Hold:
                    part.Velocity = Vector3.Zero;
                    Exit(ref part);
                    break;
                default:
                    Exit(ref part);
                    break;
            }
        }

        if (Ended(ref part, emitting))
        {
            if (source.Goal == VfxPartGoal.None && part.Life > 0f && part.Age >= part.Life)
            {
                End(ref part, instance, system);
            }

            part.Phase = PartPhase.Done;
        }
    }

    private void End(ref VfxPart part, VfxInstance instance, VfxSystem system)
    {
        var source = Part.Source;
        if (source.EndEffect.Length > 0 && system.Resolve(source.EndEffect, ref Part.EndEffect, ref Part.EndResolved) is { } end)
        {
            system.SpawnFrom(instance, end, part.Position);
        }
    }

    private void Exit(ref VfxPart part)
    {
        if (part.Exiting)
        {
            return;
        }

        part.Exiting = true;
        part.ExitAge = part.Age;
        if (Part.Animation is { } animation)
        {
            part.Player.Exit(animation);
        }
    }

    private bool Ended(ref VfxPart part, bool emitting)
    {
        var source = Part.Source;
        if (part.Age > VfxDefinition.MaxDuration)
        {
            return true;
        }

        if (part.Life > 0f && part.Age >= part.Life)
        {
            return true;
        }

        if (part.Exiting)
        {
            return Part.Animation is not null ? part.Player.Finished : part.Age - part.ExitAge >= source.FadeOut;
        }

        if (Part.Animation is not null && part.Player.Finished)
        {
            return true;
        }

        var flying = source.Goal != VfxPartGoal.None && !part.Arrived && source.Shape != VfxPartShape.Beam && source.OrbitRadius <= 0f;
        if (part.Life <= 0f && !flying)
        {
            if (Part.Animation is null && source.Shape == VfxPartShape.Model && !source.Loop && source.ModelFrames > 0f
                && source.ModelStart + part.Age * source.ModelSpeed >= source.ModelFrames)
            {
                return true;
            }

            if (Part.Animation is null && source.Shape != VfxPartShape.Model && source.Frames > 1 && !source.Loop
                && part.Age >= source.Frames * source.FrameTime * (source.PingPong ? 2f : 1f))
            {
                return true;
            }
        }

        if (!emitting && part.Life <= 0f && !flying)
        {
            // A part that would play until the effect stops ends with it; a looped animation plays its last frame.
            if (Part.Animation is not null && source.Loop)
            {
                Exit(ref part);
                return false;
            }

            return true;
        }

        return false;
    }

    // Moves towards the goal; returns whether the part arrived this frame.
    private bool Fly(ref VfxPart part, VfxInstance instance, float dt)
    {
        var source = Part.Source;
        var goal = Goal(instance);
        var position = part.Position;
        var toGoal = goal - position;
        if (source.Lift != 0f)
        {
            var across = new Vector3(toGoal.X, 0f, toGoal.Z);
            var distance = across.Length();
            var step = source.Speed * dt;
            var move = distance > step && distance > 1e-4f ? across / distance * step : across;
            part.Velocity = new Vector3(move.X / dt, part.Velocity.Y + part.Gravity * dt, move.Z / dt);
            part.Local += new Vector3(move.X, part.Velocity.Y * dt, move.Z);
            return part.Velocity.Y < 0f && part.Position.Y <= goal.Y || distance <= source.Reach && MathF.Abs(toGoal.Y) <= source.Reach;
        }

        var length = toGoal.Length();
        var travel = source.Speed * dt;
        if (length <= MathF.Max(source.Reach, travel))
        {
            if (length > 1e-4f)
            {
                part.Velocity = toGoal / length * source.Speed;
            }

            part.Local = goal - part.Origin;
            return true;
        }

        part.Velocity = toGoal / length * source.Speed;
        part.Local += part.Velocity * dt;
        return false;
    }

    private void Orbit(ref VfxPart part)
    {
        var source = Part.Source;
        var angle = MathHelper.ToRadians(part.OrbitAngle + source.OrbitSpeed * part.OrbitSign * part.Age);
        var radius = part.OrbitRadius + source.OrbitGrowth * part.Age;
        var ring = new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
        ring = Vector3.Transform(ring, Matrix.CreateRotationX(MathHelper.ToRadians(source.OrbitTilt)));
        ring.Y += source.OrbitRise * part.Age;
        var previous = part.Local;
        part.Local = Vector3.TransformNormal(ring, _turn);
        if (part.Age > 0f)
        {
            part.Velocity = part.Local - previous;
        }
    }

    private void Sample(ref VfxPart part, int index)
    {
        var source = Part.Source;
        var stride = Part.TrailStride;
        var points = source.Shape == VfxPartShape.Blade ? stride / 2 : stride;
        part.TrailHead = (part.TrailHead + 1) % points;
        part.TrailCount = Math.Min(part.TrailCount + 1, points);
        var at = index * stride;
        if (source.Shape == VfxPartShape.Blade)
        {
            Trail[at + part.TrailHead * 2] = part.Attach.Translation;
            Trail[at + part.TrailHead * 2 + 1] = Vector3.Transform(Part.BladeTip, part.Attach);
            return;
        }

        Trail[at + part.TrailHead] = part.Position;
    }

    // Opacity multiplier of the part's fade and exit (0..1).
    public float Fade(in VfxPart part)
    {
        var source = Part.Source;
        var fade = 1f;
        if (part.Life > 0f)
        {
            var f = part.Age / part.Life;
            fade = source.Fade switch
            {
                VfxPartFade.Rise => f < 0.255f ? f / 0.255f : MathF.Max(0f, 1f - (f - 0.255f) / 0.255f),
                VfxPartFade.Fall => f < 0.745f ? 1f : MathF.Max(0f, (1f - f) / 0.255f),
                _ => 1f
            };
        }

        if (source.Fade == VfxPartFade.InOut)
        {
            if (source.FadeIn > 0f)
            {
                fade = MathF.Min(fade, part.Age / source.FadeIn);
            }

            if (source.FadeOut > 0f && part.Life > 0f)
            {
                fade = MathF.Min(fade, (part.Life - part.Age) / source.FadeOut);
            }
        }

        if (part.Exiting && Part.Animation is null && source.FadeOut > 0f)
        {
            fade = MathF.Min(fade, 1f - (part.Age - part.ExitAge) / source.FadeOut);
        }

        return Math.Clamp(fade, 0f, 1f);
    }

    // The texture to draw: the animation's current image, the numbered frame, or the single texture.
    public Texture2D? Texture(in VfxPart part)
    {
        var textures = Part.Textures;
        if (textures.Length == 0)
        {
            return null;
        }

        if (Part.Animation is { } animation)
        {
            return textures[Math.Clamp(part.Player.Image(animation), 0, textures.Length - 1)];
        }

        var frames = textures.Length;
        if (frames == 1)
        {
            return textures[0];
        }

        var step = (int)(part.Age / MathF.Max(1e-3f, Part.Source.FrameTime));
        if (Part.Source.PingPong)
        {
            var cycle = Math.Max(1, frames * 2 - 2);
            step %= cycle;
            return textures[step < frames ? step : cycle - step];
        }

        return textures[Part.Source.Loop ? step % frames : Math.Min(step, frames - 1)];
    }

    public float ModelFrame(in VfxPart part)
    {
        var source = Part.Source;
        var frame = source.ModelStart + part.Age * source.ModelSpeed;
        if (source.ModelFrames <= 0f)
        {
            return 0f;
        }

        if (!source.Loop || frame < source.ModelFrames)
        {
            return MathF.Min(frame, source.ModelFrames - 0.001f);
        }

        var span = MathF.Max(1e-3f, source.ModelFrames - source.LoopFrom);
        return source.LoopFrom + (frame - source.LoopFrom) % span;
    }

    public float PulseScale(in VfxPart part)
    {
        var source = Part.Source;
        if (source.PulsePeriod <= 0f)
        {
            return 1f;
        }

        var phase = part.Age / source.PulsePeriod % 1f;
        var triangle = phase < 0.5f ? phase * 2f : 2f - phase * 2f;
        return source.Pulse[0] + (source.Pulse[1] - source.Pulse[0]) * triangle;
    }

    // A range drawn only when it spans values: a constant range takes no number from the random sequence.
    private static float Draw(float[] range, ref uint random) => range[1] > range[0] ? VfxRandom.Range(range, ref random) : range[0];

    private static Vector3 RandomDirection(ref uint random)
    {
        var z = VfxInstance.NextUnit(ref random) * 2f - 1f;
        return VfxRandom.SphereDirection(z, VfxInstance.NextUnit(ref random) * MathHelper.TwoPi);
    }
}
