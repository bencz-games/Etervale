using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

using PristonTale.Engine3D.Rendering.Gltf;

namespace PristonTale.Engine3D.Rendering;

internal sealed record RenderBatch(
    PristonStaticVertex[] Vertices,
    PristonStaticVertex[] BaseVertices,
    PristonGpuMesh Mesh,
    PristonMaterialRuntime Material,
    bool ForceCastsShadow = false,
    bool ShadowCoveredByAggregate = false,
    bool IsSkinned = false,
    VertexBuffer SkinnedBuffer = null,
    IndexBuffer SkinnedIndexBuffer = null,
    int SkinnedPrimitiveCount = 0,
    int SkinnedVertexCount = 0,
    int SkinnedIndexCount = 0,
    BoundingBox SkinnedBounds = default,
    int BoneCount = 0,
    bool IsActor = false,
    // A glTF batch's material (Material is its Queue): drawn with the GLTF variant of Surface.fx.
    GltfMaterialRuntime Gltf = null)
{
    // For non-skinned batches use the static mesh bounds or compute from vertices.
    // For skinned batches use SkinnedBounds (bind-pose AABB — may be slightly off
    // for animated geometry; frustum culling will be conservative).
    public BoundingBox Bounds { get; private set; } =
        IsSkinned ? SkinnedBounds :
        (Mesh?.Bounds ?? CalculateBounds(Vertices));

    public int TriangleCount => IsSkinned ? SkinnedPrimitiveCount : (Mesh?.PrimitiveCount ?? Vertices.Length / 3);

    public PristonSkinPalette SkinPalette { get; private set; }
    public Matrix[] BonePalette => SkinPalette?.Matrices ?? [];

    public DynamicVertexBuffer DynamicBuffer { get; set; }
    public bool DynamicBufferDirty { get; set; }

    public Vector3 ActorColorAdd { get; set; }

    // The hover outline of the actor that owns the batch: display-referred colour, w its strength (0: none).
    public Vector4 Outline { get; set; }

    // The gameplay appearance of the actor that owns the batch (shared by its batches); null for scenery.
    public ActorShade Shade { get; set; }

    // An actor's share of the stage's authored light where it stands (PristonStageLightField); 1 for everything else.
    public float FloorLight { get; set; } = 1f;

    public void UseSkinPalette(PristonSkinPalette palette)
    {
        if (!IsSkinned || palette.Count != BoneCount)
        {
            throw new ArgumentException("A skinned batch must use its actor's matching compact palette.", nameof(palette));
        }

        SkinPalette = palette;
    }

    public void RefreshBounds()
    {
        if (Mesh == null && !IsSkinned)
            Bounds = CalculateBounds(Vertices);
    }

    public void SetWorldBounds(BoundingBox bounds) => Bounds = bounds;

    private static BoundingBox CalculateBounds(PristonStaticVertex[] vertices)
    {
        if (vertices.Length == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new BoundingBox(min, max);
    }
}

internal readonly record struct SkinnedVertKey(Vector3 Position, Vector3 Normal, Vector2 TexCoord, Vector2 TexCoord1, Vector2 TexCoord2, float BoneIndex);

internal sealed record ModelTriangleRef(int ObjectIndex, PristonModelFace Face);

internal sealed record ActorTriangleRef(int ObjectIndex, PristonModelFace Face, int ABoneIndex, int BBoneIndex, int CBoneIndex, Matrix ObjectTransform);
