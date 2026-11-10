using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Formats.Gltf;

namespace PristonTale.Engine3D.Rendering.Gltf;

// A glTF model prepared to be drawn as an actor. Its clips follow one another on a timeline of actor frames (160 ticks,
// FrameTicksPerSecond), each starting on a whole frame, as the motion files of a merged legacy skeleton do: clip k is
// Skeleton.FrameRanges[k], so a motion entry with MotionFrame k + 1 plays it (the renderer's motion frame offset).
// The skeleton's objects are the model's nodes, found by name for attachments; the transforms the animator gives are
// the nodes' worlds followed by every skin's joint matrices, all in actor space (glTF metres scaled by UnitsPerMetre).
// Each primitive is drawn in batches of at most PristonShaderLibrary.MaxBones bones, each with its own compact palette.
public sealed class GltfActorSource
{
    // A legacy NPC stands about 50 units tall: a person of 1.7 m.
    public const float UnitsPerMetre = 30f;

    private readonly object _packLock = new();
    private GltfMaterialPack[] _packs;

    private GltfActorSource(GltfModel model, int[] clipStartFrames, int[] skinOffsets, PristonModel skeleton, PristonModel mesh,
        GltfActorBatch[] batches, GltfMaterialPack[] packs)
    {
        Model = model;
        ClipStartFrames = clipStartFrames;
        SkinOffsets = skinOffsets;
        Skeleton = skeleton;
        Mesh = mesh;
        Batches = batches;
        _packs = packs;
    }

    // Without its images: they live on in the packed maps.
    public GltfModel Model { get; }
    // The first frame of each clip on the timeline.
    public int[] ClipStartFrames { get; }
    // Where each skin's joint matrices start among the animator's transforms (after the nodes).
    public int[] SkinOffsets { get; }
    public int TransformCount => Model.Nodes.Count + Model.Skins.Sum(static skin => skin.Joints.Length);
    public PristonModel Skeleton { get; }
    // No geometry of its own: its bounds are the model's rest extent in actor space.
    public PristonModel Mesh { get; }
    internal GltfActorBatch[] Batches { get; }
    // Per material (a batch's Material indexes them; the last is glTF's default material), for a renderer to upload. A
    // renderer releases them once uploaded (ReleasePacks); asked for again, they are packed again from the file.
    internal GltfMaterialPack[] Packs
    {
        get
        {
            lock (_packLock)
            {
                return _packs ??= PackMaterials(GltfModelReader.Read(Model.Path));
            }
        }
    }

    internal void ReleasePacks()
    {
        lock (_packLock)
        {
            _packs = null;
        }
    }

    public static Matrix Root => Matrix.CreateScale(UnitsPerMetre);

    public static GltfActorSource Load(string path) => FromModel(GltfModelReader.Read(path));

    internal static GltfActorSource FromModel(GltfModel model)
    {
        var starts = new int[model.Animations.Count];
        var next = 0;
        var ranges = new List<PristonFrameRange>();
        for (var clip = 0; clip < starts.Length; clip++)
        {
            starts[clip] = next;
            var frames = Math.Max(1, (int)MathF.Ceiling(model.Animations[clip].DurationSeconds * GameUnits.FrameTicksPerSecond / GameUnits.FrameTicks));
            ranges.Add(new PristonFrameRange(next * GameUnits.FrameTicks, (next + frames) * GameUnits.FrameTicks, 0, 0));
            next += frames + 1;
        }

        var skinOffsets = new int[model.Skins.Count];
        var offset = model.Nodes.Count;
        for (var skin = 0; skin < skinOffsets.Length; skin++)
        {
            skinOffsets[skin] = offset;
            offset += model.Skins[skin].Joints.Length;
        }

        var root = Root;
        var bounds = new BoundingBox(Vector3.Transform(model.Bounds.Min, root), Vector3.Transform(model.Bounds.Max, root));
        var objects = model.Nodes.Select(node => new PristonModelObject(node.Name, node.Parent < 0 ? "" : model.Nodes[node.Parent].Name, false,
            Matrix.Identity, Vector3.Zero, [], [], [], [], [], [], [], [], [], [], [])).ToArray();
        var skeleton = new PristonModel(model.Path, [], objects, ranges, bounds);
        var mesh = new PristonModel(model.Path, [], [], [], bounds);

        var batches = model.Primitives.SelectMany(primitive => Batch(model, primitive, skinOffsets)).ToArray();
        return new GltfActorSource(model with { Images = [] }, starts, skinOffsets, skeleton, mesh, batches, PackMaterials(model));
    }

    private static GltfMaterialPack[] PackMaterials(GltfModel model)
    {
        var images = new DecodedPristonImage[model.Images.Count];
        DecodedPristonImage Image(int index) => images[index] ??= PristonImageDecoder.Decode(model.Images[index].Content,
            model.Images[index].MimeType == "image/png" ? ".png" : ".jpg");
        return [.. model.Materials.Append(new GltfMaterial("default")).Select(material => GltfMaterialPacking.Pack(material, Image))];
    }

    // The primitive's triangles grouped so no batch uses more than MaxBones transforms; a rigid primitive follows its node.
    private static List<GltfActorBatch> Batch(GltfModel model, GltfPrimitive primitive, int[] skinOffsets)
    {
        var batches = new List<GltfActorBatch>();
        var material = primitive.Material < 0 ? model.Materials.Count : primitive.Material;
        var vertices = primitive.Vertices;
        // Both normal maps share the packed texture's UV set (GltfMaterialPacking), the surface's first.
        if (model.Materials.ElementAtOrDefault(primitive.Material) is { } normalMapped
            && (normalMapped.NormalTexture.IsSet ? normalMapped.NormalTexture : normalMapped.ClearcoatNormalTexture) is { IsSet: true } normals
            && vertices.All(static vertex => vertex.Tangent == Vector4.Zero))
        {
            vertices = (GltfVertex[])vertices.Clone();
            GltfTangents.Generate(vertices, primitive.Indices, normals.TexCoord);
        }

        int Transform(int joint) => primitive.Skinned ? skinOffsets[primitive.Skin] + joint : primitive.Node;
        var slots = new Dictionary<int, int>();
        var bones = new List<int>();
        var used = new Dictionary<int, int>();
        var gpu = new List<GltfGpuVertex>();
        var indices = new List<int>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        Span<int> triangle = stackalloc int[12];
        for (var index = 0; index < primitive.Indices.Length; index += 3)
        {
            var needed = 0;
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = vertices[primitive.Indices[index + corner]];
                for (var influence = 0; influence < 4; influence++)
                {
                    var transform = Transform((int)vertex.Joints.Index(influence));
                    if ((primitive.Skinned ? vertex.Weights.Index(influence) > 0f : influence == 0) && !slots.ContainsKey(transform)
                        && triangle[..needed].IndexOf(transform) < 0)
                    {
                        triangle[needed++] = transform;
                    }
                }
            }

            if (bones.Count + needed > PristonShaderLibrary.MaxBones)
            {
                batches.Add(Finish(material, gpu, indices, bones, min, max));
                (slots, bones, used, gpu, indices) = (new(), new(), new(), new(), new());
                (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
                index -= 3;
                continue;
            }

            for (var corner = 0; corner < 3; corner++)
            {
                var source = primitive.Indices[index + corner];
                if (!used.TryGetValue(source, out var target))
                {
                    var vertex = vertices[source];
                    target = gpu.Count;
                    used[source] = target;
                    gpu.Add(new GltfGpuVertex
                    {
                        Position = vertex.Position,
                        Normal = vertex.Normal,
                        Color = vertex.Color,
                        Uv0 = vertex.Uv0,
                        Uv1 = vertex.Uv1,
                        Tangent = vertex.Tangent,
                        Bones = new Byte4(Slot(vertex, 0), Slot(vertex, 1), Slot(vertex, 2), Slot(vertex, 3)),
                        Weights = primitive.Skinned ? vertex.Weights : Vector4.UnitX
                    });
                    min = Vector3.Min(min, vertex.Position);
                    max = Vector3.Max(max, vertex.Position);
                }

                indices.Add(target);
            }

            int Slot(GltfVertex vertex, int influence)
            {
                if (primitive.Skinned && vertex.Weights.Index(influence) <= 0f || !primitive.Skinned && influence > 0)
                {
                    return 0;
                }

                var transform = Transform((int)vertex.Joints.Index(influence));
                if (!slots.TryGetValue(transform, out var slot))
                {
                    slot = bones.Count;
                    slots[transform] = slot;
                    bones.Add(transform);
                }

                return slot;
            }
        }

        if (indices.Count > 0)
        {
            batches.Add(Finish(material, gpu, indices, bones, min, max));
        }

        return batches;
    }

    private static GltfActorBatch Finish(int material, List<GltfGpuVertex> vertices, List<int> indices, List<int> bones, Vector3 min, Vector3 max) =>
        new(material, [.. vertices], [.. indices], [.. bones], new BoundingBox(min, max));
}

// Vertices in bind space (a rigid primitive's in its node's space); Bones maps the batch's palette slots to the
// animator's transforms.
internal sealed record GltfActorBatch(int Material, GltfGpuVertex[] Vertices, int[] Indices, int[] Bones, BoundingBox Bounds);

internal static class GltfVectorExtensions
{
    public static float Index(this Vector4 vector, int index) => index switch
    {
        0 => vector.X,
        1 => vector.Y,
        2 => vector.Z,
        _ => vector.W
    };
}
