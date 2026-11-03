using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// Authoring format of Etervale's effects (*.vfx.json under <assetRoot>/Effect/Vfx). An effect is a set of layers that
// share one clock: sprite emitters, ribbon trails, transient lights, sounds, components (models, quads and particle
// pools with keyed events), particle sequences and child effects. Emitter and trail colours are linear Rec.709;
// Intensity scales them into the HDR scene relative to the current exposure, so 1 displays the colour as authored and
// higher values bloom. Component and sequence colours are the original 0..255 display values.
public enum VfxAnchor
{
    // The position the effect was spawned at (an impact point, a ground target).
    Point,
    Caster,
    Target
}

// Additive: source x alpha + destination. Alpha: over. Color and AddColor: source + destination (the renderer's one-one
// blend, approximating the original's source x source). Shadow: destination x source. InvShadow: destination x
// (1 - source).
public enum VfxBlend
{
    Additive,
    Alpha,
    Color,
    AddColor,
    Shadow,
    InvShadow
}

public enum VfxShape
{
    Point,
    Sphere,
    Ring,
    Disc
}

public enum VfxAlign
{
    // Camera-facing billboard.
    Camera,
    // Flat on the ground plane (shock rings, magic circles).
    Ground,
    // Stretched along the particle's velocity (sparks, streaks).
    Velocity
}

public sealed record VfxKey(float T, float[] V);

public sealed record VfxEmitterDefinition
{
    public string Texture { get; init; } = "";
    // Sprite-sheet frames (columns x rows) played over the particle's life.
    public int FrameColumns { get; init; } = 1;
    public int FrameRows { get; init; } = 1;
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public float Intensity { get; init; } = 1f;
    // Fade distance in world units against the scene depth; 0 disables the soft-particle fade.
    public float Softness { get; init; } = 16f;
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    // Bone of the anchor's actor ("Bip weapon01", "Bip01 R Hand"); empty uses the anchor's position.
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    // Particles follow a moving anchor instead of staying where they were born.
    public bool Local { get; init; }
    public float Delay { get; init; }
    // Seconds of continuous emission at Rate; 0 emits for the whole effect duration.
    public float EmitTime { get; init; }
    public float Rate { get; init; }
    public int Burst { get; init; }
    public int MaxParticles { get; init; } = 64;
    public float[] Lifetime { get; init; } = [1f, 1f];
    public float[] Speed { get; init; } = [0f, 0f];
    public float[] Direction { get; init; } = [0f, 1f, 0f];
    // Cone half-angle around Direction in degrees; 180 emits in every direction.
    public float Spread { get; init; }
    public VfxShape Shape { get; init; } = VfxShape.Point;
    public float[] Radius { get; init; } = [0f, 0f];
    public float[] Gravity { get; init; } = [0f, 0f, 0f];
    // Fraction of velocity lost per second.
    public float Drag { get; init; }
    // Size over normalized life: keys {t, [size]}.
    public VfxKey[] Size { get; init; } = [new(0f, [10f])];
    // Colour over normalized life: keys {t, [r, g, b, a]}.
    public VfxKey[] Color { get; init; } = [new(0f, [1f, 1f, 1f, 1f])];
    public float[] Rotation { get; init; } = [0f, 0f];
    // Degrees per second.
    public float[] Spin { get; init; } = [0f, 0f];
    public VfxAlign Align { get; init; } = VfxAlign.Camera;
    // Velocity alignment: length added per unit of speed.
    public float Stretch { get; init; } = 0.02f;
}

public sealed record VfxTrailDefinition
{
    public string Texture { get; init; } = "";
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public float Intensity { get; init; } = 1f;
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Caster;
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    public float Width { get; init; } = 12f;
    // Seconds a trail point lives.
    public float Lifetime { get; init; } = 0.25f;
    public float Delay { get; init; }
    public float EmitTime { get; init; }
    // Colour along the trail from head (0) to tail (1).
    public VfxKey[] Color { get; init; } = [new(0f, [1f, 1f, 1f, 1f]), new(1f, [1f, 1f, 1f, 0f])];
}

public sealed record VfxLightDefinition
{
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public float[] Offset { get; init; } = [0f, 30f, 0f];
    public float[] Color { get; init; } = [1f, 1f, 1f];
    // Original light units: power sets the radius (power / 2, at least 64) and brightness, decay the fade per 1/70 s.
    public int Power { get; init; } = 160;
    public int Decay { get; init; } = 4;
    public float Delay { get; init; }
}

public sealed record VfxSoundDefinition
{
    // Path under the asset root, as the original sound tables give it (wav\Effects\...).
    public string Path { get; init; } = "";
    // Further takes of the same sound: each play picks Path or one of these at random.
    public string[] Variants { get; init; } = [];
    public float Delay { get; init; }
    public float Volume { get; init; } = 1f;
    // Sounds sharing a non-zero pick group play one of them, drawn uniformly when the effect starts.
    public int Pick { get; init; }

    public IEnumerable<string> AllPaths => Variants.Prepend(Path);
}

// Another named effect started by this one at its anchor (the converted original scripts are used this way). Follow
// keeps the child's spawn point on the anchor; Turn gives it this effect's facing (otherwise it keeps world axes).
public sealed record VfxChildDefinition
{
    public string Effect { get; init; } = "";
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    public float Delay { get; init; }
    public bool Follow { get; init; }
    public bool Turn { get; init; }
    // Stops the child when this effect stops (auras, casts); otherwise the child plays out.
    public bool StopWithParent { get; init; } = true;
    // Children sharing a non-zero pick group start one of them, drawn uniformly when the effect starts.
    public int Pick { get; init; }
}

public sealed record VfxDefinition
{
    // Longest effect in seconds (long auras); effects normally end earlier, when their layers have played out.
    public const float MaxDuration = 600f;

    public required string Name { get; init; }
    // Lifetime of the effect in seconds; layers stop emitting then and the effect ends when every particle has died.
    public float Duration { get; init; } = 1f;
    public VfxEmitterDefinition[] Emitters { get; init; } = [];
    public VfxTrailDefinition[] Trails { get; init; } = [];
    public VfxLightDefinition[] Lights { get; init; } = [];
    public VfxSoundDefinition[] Sounds { get; init; } = [];
    public VfxComponentDefinition[] Components { get; init; } = [];
    public VfxSequenceDefinition[] Sequences { get; init; } = [];
    public VfxChildDefinition[] Children { get; init; } = [];
    public VfxPartDefinition[] Parts { get; init; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static VfxDefinition[] ParseFile(string json)
    {
        var definitions = JsonSerializer.Deserialize<VfxDefinition[]>(json, Json) ?? [];
        foreach (var definition in definitions)
        {
            definition.Validate();
        }

        return definitions;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !float.IsFinite(Duration) || Duration <= 0f || Duration > MaxDuration)
        {
            throw new InvalidDataException($"Effect '{Name}' needs a name and a duration between 0 and {MaxDuration} seconds.");
        }

        foreach (var emitter in Emitters)
        {
            if (emitter.MaxParticles is < 1 or > 4096 || emitter.Burst < 0 || emitter.Rate < 0f || emitter.Lifetime.Length != 2
                || emitter.Lifetime[0] <= 0f || emitter.Lifetime[1] < emitter.Lifetime[0] || emitter.Size.Length == 0 || emitter.Color.Length == 0
                || emitter.FrameColumns < 1 || emitter.FrameRows < 1 || !Vec3Ok(emitter.Offset) || !Vec3Ok(emitter.Direction)
                || !Vec3Ok(emitter.Gravity) || !RangeOk(emitter.Speed) || !RangeOk(emitter.Radius) || !RangeOk(emitter.Rotation)
                || !RangeOk(emitter.Spin) || !KeysOk(emitter.Size, 1) || !KeysOk(emitter.Color, 4)
                || !VfxBuiltinTextures.IsKnownOrAsset(emitter.Texture))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid emitter.");
            }
        }

        foreach (var trail in Trails)
        {
            if (trail.Width <= 0f || trail.Lifetime <= 0f || !Vec3Ok(trail.Offset) || !KeysOk(trail.Color, 4)
                || !VfxBuiltinTextures.IsKnownOrAsset(trail.Texture))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid trail.");
            }
        }

        foreach (var light in Lights)
        {
            if (light.Power <= 0 || light.Decay <= 0 || light.Color.Length != 3 || !Vec3Ok(light.Offset))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid light.");
            }
        }

        foreach (var component in Components)
        {
            if (!ComponentOk(component))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid {component.Kind} component.");
            }
        }

        foreach (var sequence in Sequences)
        {
            if (!SequenceOk(sequence))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid particle sequence.");
            }
        }

        foreach (var part in Parts)
        {
            if (!PartOk(part))
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid {part.Shape} part.");
            }
        }

        foreach (var child in Children)
        {
            if (string.IsNullOrWhiteSpace(child.Effect) || child.Effect.Equals(Name, StringComparison.OrdinalIgnoreCase) || !Vec3Ok(child.Offset)
                || !float.IsFinite(child.Delay) || child.Delay < 0f || child.Pick < 0)
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid child effect.");
            }
        }

        foreach (var sound in Sounds)
        {
            if (string.IsNullOrWhiteSpace(sound.Path) || !float.IsFinite(sound.Delay) || sound.Delay < 0f || sound.Pick < 0)
            {
                throw new InvalidDataException($"Effect '{Name}' has an invalid sound.");
            }
        }
    }

    private static bool Vec3Ok(float[] value) => value.Length == 3 && value.All(float.IsFinite);

    private static bool ComponentOk(VfxComponentDefinition component)
    {
        var textured = component.Kind != VfxComponentKind.Model;
        return Vec3Ok(component.Offset) && Vec3Ok(component.BeamFrom) && Vec3Ok(component.BeamTo) && Vec3Ok(component.SpawnMin)
            && Vec3Ok(component.SpawnMax) && Vec3Ok(component.VelocityMin) && Vec3Ok(component.VelocityMax) && component.Color.Length == 4
            && component.Color.All(float.IsFinite) && component.SizeMin.Length == 2 && component.SizeMax.Length == 2 && component.EndTime.Length == 2
            && component.SizeMin.Concat(component.SizeMax).Concat(component.EndTime).All(float.IsFinite) && component.Loop >= 0
            && float.IsFinite(component.Delay) && component.Delay >= 0f && component.Frames >= 0 && float.IsFinite(component.FrameTime)
            && float.IsFinite(component.ModelFrames) && component.ModelFrames >= 0f && float.IsFinite(component.Count) && float.IsFinite(component.Rate)
            && (textured ? component.Texture.Length > 0 : component.Model.Length > 0)
            && component.Events.All(static e => float.IsFinite(e.T) && e.Value.All(float.IsFinite)
                && e.Value.Length == (e.Kind == VfxComponentEventKind.Color ? 4 : 3));
    }

    private static bool SequenceOk(VfxSequenceDefinition sequence)
    {
        return sequence.Texture.Length > 0 && sequence.Count is >= 1 and <= 4096 && sequence.Loops >= 0 && (long)sequence.Count * sequence.Loops <= 1 << 20
            && RangeOk(sequence.Rate) && RangeOk(sequence.Lifetime) && sequence.Lifetime[1] <= 1e6f && Vec3Ok(sequence.RadiusMin)
            && Vec3Ok(sequence.RadiusMax) && Vec3Ok(sequence.GravityMin) && Vec3Ok(sequence.GravityMax) && Vec3Ok(sequence.Offset)
            && float.IsFinite(sequence.Delay) && sequence.Delay is >= 0f and <= 60f
            && sequence.Events.All(static e => float.IsFinite(e.T) && e.Min.Length == e.Max.Length && e.Min.Length is >= 1 and <= 4
                && e.Min.Concat(e.Max).All(float.IsFinite));
    }

    private static bool PartOk(VfxPartDefinition part)
    {
        var vectors = new[]
        {
            part.Offset, part.JitterMin, part.JitterMax, part.Shell, part.VelocityMin, part.VelocityMax, part.GoalOffset, part.Angles, part.Tumble, part.BeamTo
        };
        var ranges = new[] { part.Lifetime, part.Size, part.Height, part.Rotation, part.Width, part.Scale, part.Pulse, part.Ring, part.Radial, part.Rise, part.Burst,
            part.Gravity, part.Swirl, part.SwirlGrowth, part.MoveTime };
        var scalars = new[] { part.Delay, part.DelayStep, part.DelayRandom, part.FrameTime, part.ModelFrames, part.ModelSpeed, part.ModelStart, part.Growth,
            part.Spin, part.FadeIn, part.FadeOut, part.PulsePeriod, part.BladeLength, part.LineSeconds, part.TrailFade, part.RingStart, part.RingStep,
            part.Speed, part.Reach, part.Lift, part.OrbitRadius, part.OrbitSpeed, part.OrbitTilt, part.OrbitRise, part.OrbitGrowth, part.OrbitPhase,
            part.OrbitPhaseStep, part.Every, part.LoopFrom, part.SlideFrom };
        var drawn = part.Shape != VfxPartShape.Model || part.Model.Length > 0;
        return part.Count is >= 1 and <= 1024 && part.Delay >= 0f && part.DelayStep >= 0f && part.DelayRandom >= 0f && part.Frames >= 0 && part.FrameTime > 0f
            && part.TrailPoints is >= 2 and <= CompiledPart.MaxTrailPoints && part.Color.Length == 4 && part.Color.All(float.IsFinite)
            && vectors.All(Vec3Ok) && ranges.All(RangeOk)
            && part.Lifetime[0] >= 0f && part.Lifetime[1] <= MaxDuration && scalars.All(float.IsFinite) && part.Speed >= 0f && part.Every >= 0f
            && (part.Goal == VfxPartGoal.None || part.Speed > 0f || part.Shape == VfxPartShape.Beam || part.OrbitRadius > 0f) && drawn
            && part.LoopFrom >= 0f && (part.LoopFrom < part.ModelFrames || part.LoopFrom == 0f) && (part.Shape != VfxPartShape.Blade || part.Bone.Length > 0);
    }

    private static bool RangeOk(float[] value) => value.Length == 2 && value.All(float.IsFinite) && value[1] >= value[0];

    private static bool KeysOk(VfxKey[] keys, int width)
    {
        var previous = -1f;
        foreach (var key in keys)
        {
            if (key.V.Length != width || !float.IsFinite(key.T) || key.T < previous || key.T < 0f || key.T > 1f || !key.V.All(float.IsFinite))
            {
                return false;
            }

            previous = key.T;
        }

        return true;
    }

    internal static Vector3 Vec3(float[] value) => new(value[0], value[1], value[2]);
}
