using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Effects.Vfx;

// A keyed curve over normalized life, sampled without allocation (linear between keys, clamped at the ends).
public sealed class VfxCurve
{
    private readonly float[] _times;
    private readonly Vector4[] _values;

    public VfxCurve(VfxKey[] keys)
    {
        _times = new float[keys.Length];
        _values = new Vector4[keys.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            _times[index] = keys[index].T;
            _values[index] = VfxKeys.ToVector4(keys[index].V);
        }
    }

    public Vector4 Sample(float t)
    {
        if (t <= _times[0] || _times.Length == 1)
        {
            return _values[0];
        }

        for (var index = 1; index < _times.Length; index++)
        {
            if (t <= _times[index])
            {
                var span = _times[index] - _times[index - 1];
                var f = span > 0f ? (t - _times[index - 1]) / span : 1f;
                return Vector4.Lerp(_values[index - 1], _values[index], f);
            }
        }

        return _values[^1];
    }
}

internal sealed class CompiledEmitter
{
    public CompiledEmitter(VfxEmitterDefinition source, Texture2D texture)
    {
        Source = source;
        Texture = texture;
        Offset = VfxDefinition.Vec3(source.Offset);
        Direction = source.Direction.All(value => value == 0f) ? Vector3.Up : Vector3.Normalize(VfxDefinition.Vec3(source.Direction));
        Gravity = VfxDefinition.Vec3(source.Gravity);
        Size = new VfxCurve(source.Size);
        Color = new VfxCurve(source.Color);
        SpreadCos = MathF.Cos(MathHelper.ToRadians(Math.Clamp(source.Spread, 0f, 180f)));
    }

    public VfxEmitterDefinition Source { get; }
    public Texture2D Texture { get; }
    public Vector3 Offset { get; }
    public Vector3 Direction { get; }
    public Vector3 Gravity { get; }
    public VfxCurve Size { get; }
    public VfxCurve Color { get; }
    public float SpreadCos { get; }
}

internal sealed class CompiledTrail
{
    public CompiledTrail(VfxTrailDefinition source, Texture2D texture)
    {
        Source = source;
        Texture = texture;
        Offset = VfxDefinition.Vec3(source.Offset);
        Color = new VfxCurve(source.Color);
    }

    public VfxTrailDefinition Source { get; }
    public Texture2D Texture { get; }
    public Vector3 Offset { get; }
    public VfxCurve Color { get; }
}

// A definition with its textures, models and curves resolved, shared by every instance. Child effects are looked up
// by name on first use, so a child may be defined in any file.
public sealed class CompiledVfx
{
    internal CompiledVfx(VfxDefinition definition, CompiledEmitter[] emitters, CompiledTrail[] trails, CompiledComponent[] components,
        CompiledSequence[] sequences, CompiledPart[] parts)
    {
        Parts = parts;
        Definition = definition;
        Emitters = emitters;
        Trails = trails;
        Components = components;
        Sequences = sequences;
        Children = new CompiledVfx?[definition.Children.Length];
        ChildrenResolved = new bool[definition.Children.Length];
    }

    public VfxDefinition Definition { get; }
    public string Name => Definition.Name;
    internal CompiledEmitter[] Emitters { get; }
    internal CompiledTrail[] Trails { get; }
    internal CompiledComponent[] Components { get; }
    internal CompiledSequence[] Sequences { get; }
    internal CompiledPart[] Parts { get; }
    internal CompiledVfx?[] Children { get; }
    internal bool[] ChildrenResolved { get; }
}
