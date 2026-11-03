using PristonTale.Effects.Vfx;

namespace PristonTale.VfxImport;

// Converts an original effect group script (Effect/NewEffect/<name>.lua) to an effect with component layers. The
// script is a list of calls: Begin("Parent") opens it (its InitPos moves only components declared before it, so
// nothing in practice); Begin("<Kind>") .. End() declares a component; Init* calls set its properties and Event* calls
// add keyed colour and size changes. Defaults, accepted calls and quirks follow the original loader.
internal static class EffectScriptConverter
{
    public const string Prefix = "script.";
    // Resources named by a script are relative to this folder.
    public const string Folder = "Effect/NewEffect/";

    public static VfxDefinition Convert(string name, string text, AssetPaths assets, ICollection<string> report)
    {
        var components = new List<VfxComponentDefinition>();
        Builder? current = null;
        var inParent = false;
        foreach (var (call, arguments) in ScriptCalls.Read(text, name))
        {
            var lower = call.ToLowerInvariant();
            if (lower == "begin")
            {
                var kind = Text(arguments, 0, call, name);
                if (kind.Equals("Parent", StringComparison.OrdinalIgnoreCase))
                {
                    // A second Parent starts a new group; the components before it are dropped as in the original.
                    if (components.Count > 0)
                    {
                        report.Add($"{name}: a second Parent group drops {components.Count} earlier components");
                        components.Clear();
                    }

                    inParent = true;
                    current = null;
                    continue;
                }

                inParent = false;
                current = new Builder(Kind(kind, name));
                continue;
            }

            if (lower == "end")
            {
                if (current is not null && current.Build(name, assets, report) is { } component)
                {
                    components.Add(component);
                }

                current = null;
                inParent = false;
                continue;
            }

            if (inParent)
            {
                if (lower != "initpos")
                {
                    report.Add($"{name}: {call} on the Parent group has no effect");
                }

                continue;
            }

            if (current is null)
            {
                throw new InvalidDataException($"Effect script {name} calls {call} outside a component.");
            }

            current.Apply(lower, call, arguments, name, report);
        }

        if (current is not null)
        {
            throw new InvalidDataException($"Effect script {name} ends inside a component.");
        }

        return new VfxDefinition
        {
            Name = Prefix + name,
            Duration = VfxDefinition.MaxDuration,
            Components = components.ToArray()
        };
    }

    private static VfxComponentKind Kind(string text, string name) => text.ToLowerInvariant() switch
    {
        "mesh" => VfxComponentKind.Model,
        "billboard" => VfxComponentKind.Billboard,
        "createmesh" => VfxComponentKind.Ground,
        "billboardaxial" => VfxComponentKind.Beam,
        "particlesystem" => VfxComponentKind.Particles,
        _ => throw new InvalidDataException($"Effect script {name} declares unknown component {text}.")
    };

    private static string Text(IReadOnlyList<object> arguments, int index, string call, string name) =>
        index < arguments.Count && arguments[index] is string value ? value : throw new InvalidDataException($"Effect script {name}: {call} argument {index + 1} is not a string.");

    // A missing argument reads as 0, as the scripting language passes nil to a number.
    private static float Number(IReadOnlyList<object> arguments, int index, string call, string name) =>
        index >= arguments.Count ? 0f
            : arguments[index] is float value ? value : throw new InvalidDataException($"Effect script {name}: {call} argument {index + 1} is not a number.");

    // Original +Z is effect space -Z.
    private static float[] Point(float x, float y, float z) => [x, y, z == 0f ? 0f : -z];

    private sealed class Builder(VfxComponentKind kind)
    {
        private readonly List<VfxComponentEvent> _events = [];
        private float[] _offset = [0f, 0f, 0f];
        private string _texture = "";
        private int _frames;
        private float _frameTime = 0.5f;
        private string _model = "";
        private string _skeleton = "";
        private float _modelFrames;
        private int? _loop;
        private float[]? _color;
        private float _delay;
        private float[]? _endTime;
        private float[]? _sizeMin;
        private float[]? _sizeMax;
        private string _blend = "Lamp";
        private float[] _beamFrom = [0f, 0f, 0f];
        private float[] _beamTo = [0f, 100f, 0f];
        private VfxSpawnShape _spawn = VfxSpawnShape.Point;
        private float[] _spawnMin = [0f, 0f, 0f];
        private float[] _spawnMax = [0f, 0f, 0f];
        private float _count = 50f;
        private float _rate = 30f;
        private float[] _velocityMin = [-10f, -10f, -10f];
        private float[] _velocityMax = [10f, 10f, 10f];
        private bool _outward;
        private bool _beams;

        public void Apply(string lower, string call, IReadOnlyList<object> a, string name, ICollection<string> report)
        {
            var particles = kind == VfxComponentKind.Particles;
            switch (lower)
            {
                case "initpos":
                    _offset = Point(Number(a, 0, call, name), Number(a, 1, call, name), Number(a, 2, call, name));
                    break;
                case "initmeshname":
                    _model = Text(a, 0, call, name);
                    _skeleton = a.Count > 1 ? Text(a, 1, call, name) : "";
                    break;
                case "initmaxframe":
                    _modelFrames = Number(a, 0, call, name);
                    break;
                case "initloop":
                    _loop = (int)Number(a, 0, call, name);
                    break;
                case "initstartdelaytime":
                    if (particles)
                    {
                        report.Add($"{name}: a particle system ignores its start delay");
                    }
                    else
                    {
                        _delay = Number(a, 0, call, name);
                    }

                    break;
                case "inittexturename":
                    _texture = Text(a, 0, call, name);
                    break;
                case "initanitexturename":
                    _texture = Text(a, 0, call, name);
                    _frames = (int)Number(a, 1, call, name);
                    _frameTime = Number(a, 2, call, name);
                    break;
                case "initcolor":
                    _color = [Number(a, 0, call, name), Number(a, 1, call, name), Number(a, 2, call, name), Number(a, 3, call, name)];
                    break;
                case "initendtime":
                    var first = Number(a, 0, call, name);
                    _endTime = [first, a.Count > 1 ? Number(a, 1, call, name) : first];
                    break;
                case "initsize":
                    if (a.Count >= 4)
                    {
                        _sizeMin = [Number(a, 0, call, name), Number(a, 2, call, name)];
                        _sizeMax = [Number(a, 1, call, name), Number(a, 3, call, name)];
                    }
                    else
                    {
                        _sizeMin = [Number(a, 0, call, name), Number(a, 1, call, name)];
                        _sizeMax = [.. _sizeMin];
                    }

                    break;
                case "initblendtype":
                    _blend = Text(a, 0, call, name);
                    break;
                case "initaxialpos":
                    _beamFrom = Point(Number(a, 0, call, name), Number(a, 1, call, name), Number(a, 2, call, name));
                    _beamTo = Point(Number(a, 3, call, name), Number(a, 4, call, name), Number(a, 5, call, name));
                    break;
                case "initspawnboundingbox":
                    _spawn = VfxSpawnShape.Box;
                    _spawnMin = [Number(a, 0, call, name), Number(a, 2, call, name), -Number(a, 5, call, name)];
                    _spawnMax = [Number(a, 1, call, name), Number(a, 3, call, name), -Number(a, 4, call, name)];
                    break;
                case "initspawnboundingsphere":
                    _spawn = VfxSpawnShape.Sphere;
                    _spawnMin = [Number(a, 0, call, name), 0f, 0f];
                    _spawnMax = [Number(a, 1, call, name), 0f, 0f];
                    break;
                case "initspawnboundingdoughnut":
                    _spawn = VfxSpawnShape.Doughnut;
                    _spawnMin = [Number(a, 0, call, name), Number(a, 2, call, name), 0f];
                    _spawnMax = [Number(a, 1, call, name), Number(a, 3, call, name), 0f];
                    break;
                case "initparticlenum":
                    _count = Number(a, 0, call, name);
                    break;
                case "initemitrate":
                    _rate = Number(a, 0, call, name);
                    break;
                case "initvelocity":
                    _velocityMin = [Number(a, 0, call, name), Number(a, 2, call, name), -Number(a, 5, call, name)];
                    _velocityMax = [Number(a, 1, call, name), Number(a, 3, call, name), -Number(a, 4, call, name)];
                    break;
                case "initvelocitytype":
                    _outward = Text(a, 0, call, name).Equals("CurPos", StringComparison.OrdinalIgnoreCase);
                    break;
                case "initparticletype":
                    _beams = Text(a, 0, call, name).Equals("BillboardAxial", StringComparison.OrdinalIgnoreCase);
                    break;
                case "eventcolor":
                case "eventfadecolor":
                    if (a.Count == 5)
                    {
                        _events.Add(new VfxComponentEvent(Number(a, 0, call, name), VfxComponentEventKind.Color, lower == "eventfadecolor",
                            [Number(a, 1, call, name), Number(a, 2, call, name), Number(a, 3, call, name), Number(a, 4, call, name)]));
                    }

                    break;
                case "eventsize":
                case "eventfadesize":
                    var ramp = lower == "eventfadesize";
                    // Fixed sizes are not keyed on particle systems; ramped sizes are not keyed on models.
                    if (a.Count == 4 && (ramp ? kind != VfxComponentKind.Model : !particles))
                    {
                        _events.Add(new VfxComponentEvent(Number(a, 0, call, name), VfxComponentEventKind.Size, ramp,
                            [Number(a, 1, call, name), Number(a, 2, call, name), Number(a, 3, call, name)]));
                    }
                    else
                    {
                        report.Add($"{name}: {call} on a {kind} component has no effect");
                    }

                    break;
                default:
                    throw new InvalidDataException($"Effect script {name} calls unknown {call}.");
            }
        }

        public VfxComponentDefinition? Build(string name, AssetPaths assets, ICollection<string> report)
        {
            var particles = kind == VfxComponentKind.Particles;
            var blend = Blend(_blend, name, report);
            if (kind != VfxComponentKind.Model && blend != VfxBlend.Additive)
            {
                // Quads and particles always draw with the lamp blend in the original.
                report.Add($"{name}: blend {_blend} on a {kind} component has no effect");
                blend = VfxBlend.Additive;
            }

            string model = "";
            string skeleton = "";
            string texture = "";
            if (kind == VfxComponentKind.Model)
            {
                if (!assets.TryResolveModel(Folder + _model, out model))
                {
                    report.Add($"{name}: model {_model} is missing; the component is left out");
                    return null;
                }

                if (_skeleton.Length > 0 && !assets.TryResolveModel(Folder + _skeleton, out skeleton))
                {
                    report.Add($"{name}: skeleton {_skeleton} is missing; the model plays without it");
                    skeleton = "";
                }
            }
            else
            {
                if (!ResolveTexture(name, assets, report, out texture))
                {
                    return null;
                }
            }

            var defaultSize = kind switch
            {
                VfxComponentKind.Beam => new[] { 10f, 100f },
                VfxComponentKind.Particles => [5f, 5f],
                _ => [20f, 20f]
            };
            return new VfxComponentDefinition
            {
                Kind = kind,
                Offset = _offset,
                OffsetTurn = particles ? VfxOffsetTurn.Reverse : VfxOffsetTurn.None,
                Texture = texture,
                Frames = _frames,
                FrameTime = _frameTime,
                Model = model,
                Skeleton = skeleton,
                ModelFrames = _modelFrames,
                Loop = _loop ?? (particles ? 1 : 0),
                Delay = _delay,
                EndTime = _endTime ?? (particles ? [1f, 2f] : [0f, 0f]),
                Color = _color ?? [255f, 255f, 255f, 255f],
                SizeMin = _sizeMin ?? defaultSize,
                SizeMax = _sizeMax ?? (particles ? [10f, 10f] : defaultSize),
                Blend = blend,
                BeamFrom = _beamFrom,
                BeamTo = _beamTo,
                Spawn = _spawn,
                SpawnMin = _spawnMin,
                SpawnMax = _spawnMax,
                Count = _count,
                Rate = _rate,
                VelocityMin = _velocityMin,
                VelocityMax = _velocityMax,
                Outward = _outward,
                Beams = _beams,
                Events = [.. _events]
            };
        }

        private bool ResolveTexture(string name, AssetPaths assets, ICollection<string> report, out string texture)
        {
            if (!assets.TryResolve(Folder + _texture, out texture) && _frames <= 0)
            {
                report.Add($"{name}: texture {_texture} is missing; the {kind} component is left out (the original draws nothing)");
                return false;
            }

            if (_frames > 0)
            {
                // Numbered frames are named from the stem; the stem file itself need not exist.
                var normalized = AssetPaths.Normalize(Folder + _texture);
                var dot = normalized.LastIndexOf('.');
                var extension = dot > 0 ? normalized[dot..] : "";
                if (!assets.TryResolve(VfxCatalog.FrameName(normalized, 1), out var firstFrame))
                {
                    report.Add($"{name}: animated texture {_texture} is missing; the {kind} component is left out");
                    return false;
                }

                texture = firstFrame[..^(1 + extension.Length)] + extension;
                for (var frame = 2; frame <= _frames; frame++)
                {
                    if (!assets.TryResolve(VfxCatalog.FrameName(normalized, frame), out _))
                    {
                        report.Add($"{name}: frame {frame} of {_texture} is missing");
                    }
                }
            }

            return true;
        }

        private static VfxBlend Blend(string text, string name, ICollection<string> report) => text.ToLowerInvariant() switch
        {
            "lamp" => VfxBlend.Additive,
            "alpha" => VfxBlend.Alpha,
            "color" => VfxBlend.Color,
            "addcolor" => VfxBlend.AddColor,
            "shadow" => VfxBlend.Shadow,
            "invshadow" => VfxBlend.InvShadow,
            _ => Unknown(text, name, report)
        };

        private static VfxBlend Unknown(string text, string name, ICollection<string> report)
        {
            report.Add($"{name}: unknown blend {text}; lamp is used");
            return VfxBlend.Additive;
        }
    }
}
