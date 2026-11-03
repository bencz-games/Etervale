using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Scripts;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Effects.Vfx;

// What compiling an effect needs from the game data: textures and effect models by asset-relative path.
public interface IVfxResources
{
    // Null when the file is missing; the library then draws the fallback texture.
    Texture2D? Texture(string path);

    // An effect model baked at the given number of frames (30 per second; 1 for a static model); null when it cannot be
    // read.
    PristonMeshEffect? Model(string path, string skeleton, int frames);

    // An animated billboard by file name (Effect/AnimationData); null when it is missing or unreadable.
    BillboardSequence? Animation(string name) => null;
}

// Named effects compiled against their textures and models. A name registered twice keeps the later definition.
public sealed class VfxLibrary
{
    private readonly Dictionary<string, CompiledVfx> _effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly IVfxResources? _resources;
    private readonly Func<string, Texture2D?> _textures;
    private readonly Texture2D _fallback;
    private readonly VfxBuiltinTextures? _builtins;

    // textures resolves an asset-relative path; fallback is used when it returns null (a missing texture never hides
    // the effect's lights and sounds). "builtin:" paths resolve to the procedural textures.
    public VfxLibrary(Func<string, Texture2D?> textures, Texture2D fallback, VfxBuiltinTextures? builtins = null)
    {
        _textures = textures;
        _fallback = fallback;
        _builtins = builtins;
    }

    public VfxLibrary(IVfxResources resources, Texture2D fallback, VfxBuiltinTextures? builtins = null)
        : this(resources.Texture, fallback, builtins)
    {
        _resources = resources;
    }

    public int Count => _effects.Count;

    public IReadOnlyCollection<string> Names => _effects.Keys;

    public CompiledVfx Register(VfxDefinition definition)
    {
        definition.Validate();
        var emitters = definition.Emitters.Select(emitter => new CompiledEmitter(emitter, Texture(emitter.Texture))).ToArray();
        var trails = definition.Trails.Select(trail => new CompiledTrail(trail, Texture(trail.Texture))).ToArray();
        var components = definition.Components.Select(Compile).ToArray();
        var sequences = definition.Sequences.Select(sequence => new CompiledSequence(sequence, Texture(sequence.Texture))).ToArray();
        var parts = definition.Parts.Select(Compile).ToArray();
        var compiled = new CompiledVfx(definition, emitters, trails, components, sequences, parts);
        _effects[definition.Name] = compiled;
        return compiled;
    }

    public bool TryGet(string name, out CompiledVfx effect) => _effects.TryGetValue(name, out effect!);

    // Only a found child is cached: definitions register over several frames, so one missing now may arrive later.
    internal CompiledVfx? Child(CompiledVfx parent, int index)
    {
        if (!parent.ChildrenResolved[index] && TryGet(parent.Definition.Children[index].Effect, out var child))
        {
            parent.ChildrenResolved[index] = true;
            parent.Children[index] = child;
        }

        return parent.Children[index];
    }

    private CompiledPart Compile(VfxPartDefinition part)
    {
        if (part.Shape == VfxPartShape.Model)
        {
            var frames = Math.Clamp((int)MathF.Ceiling(part.ModelFrames), 1, PristonMeshEffect.MaxFrames);
            return new CompiledPart(part, null, [], part.Model.Length > 0 ? _resources?.Model(part.Model, part.Skeleton, frames) : null);
        }

        if (part.Animation.Length > 0)
        {
            var animation = _resources?.Animation(part.Animation);
            return animation is null ? new CompiledPart(part, null, [], null) : new CompiledPart(part, animation, animation.Images.Select(Texture).ToArray(), null);
        }

        if (part.Texture.Length == 0)
        {
            return new CompiledPart(part, null, [], null);
        }

        return new CompiledPart(part, null, VfxCatalog.Frames(part.Texture, part.Frames).Select(Texture).ToArray(), null);
    }

    private CompiledComponent Compile(VfxComponentDefinition component)
    {
        if (component.Kind == VfxComponentKind.Model)
        {
            var frames = Math.Clamp((int)MathF.Ceiling(component.ModelFrames), 1, PristonMeshEffect.MaxFrames);
            return new CompiledComponent(component, [], _resources?.Model(component.Model, component.Skeleton, frames));
        }

        if (component.Frames <= 0)
        {
            return new CompiledComponent(component, [Texture(component.Texture)], null);
        }

        var textures = new Texture2D[component.Frames];
        for (var index = 0; index < textures.Length; index++)
        {
            textures[index] = Texture(VfxCatalog.FrameName(component.Texture, index + 1));
        }

        return new CompiledComponent(component, textures, null);
    }

    // A texture by asset-relative path (the fallback texture when it is missing).
    public Texture2D GetTexture(string path) => Texture(path);

    private Texture2D Texture(string path)
    {
        if (path.Length == 0)
        {
            return _fallback;
        }

        if (_builtins is not null && _builtins.TryGet(path, out var builtin))
        {
            return builtin;
        }

        return path.StartsWith(VfxBuiltinTextures.Prefix, StringComparison.OrdinalIgnoreCase) ? _fallback : _textures(path) ?? _fallback;
    }
}
