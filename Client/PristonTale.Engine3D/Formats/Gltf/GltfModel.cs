using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Formats.Gltf;

// A glTF 2.0 model (.glb or .gltf) as the renderer draws it, in glTF space (right-handed, +Y up, metres): the node
// hierarchy at rest, the triangle primitives of the default scene with up to four joint weights per vertex, the skins,
// keyframed node animations, metallic-roughness materials with the clearcoat, sheen and diffuse transmission layers,
// and the images still encoded (PNG or JPEG). Bounds is the extent drawn at rest.
public sealed record GltfModel(
    string Path,
    IReadOnlyList<GltfNode> Nodes,
    IReadOnlyList<GltfPrimitive> Primitives,
    IReadOnlyList<GltfMaterial> Materials,
    IReadOnlyList<GltfImage> Images,
    IReadOnlyList<GltfSkin> Skins,
    IReadOnlyList<GltfAnimation> Animations,
    BoundingBox Bounds);

// Parent is -1 for a root. The rest pose is the node's own transform: scale, then rotation, then translation.
public sealed record GltfNode(string Name, int Parent, Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public Matrix LocalMatrix => Matrix.CreateScale(Scale) * Matrix.CreateFromQuaternion(Rotation) * Matrix.CreateTranslation(Translation);
}

// A skinned primitive (Skin >= 0) has its vertices in its skin's bind space and follows its joints (GltfVertex.Joints
// index GltfSkin.Joints); a rigid one (Skin -1) has them in the space of its node, which places it. Indices list
// triangles; Material is -1 for the glTF default material.
public sealed record GltfPrimitive(int Node, int Material, int Skin, GltfVertex[] Vertices, int[] Indices)
{
    public bool Skinned => Skin >= 0;
}

// Tangent.w is the bitangent's sign (glTF); a primitive without tangents carries zero. Weights sum to 1 on a skinned
// vertex. Color is linear RGBA, white when the primitive has none.
public struct GltfVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector2 Uv0;
    public Vector2 Uv1;
    public Vector4 Color;
    public Vector4 Joints;
    public Vector4 Weights;
}

// Joints are node indices; InverseBinds take a vertex from bind space to each joint's space.
public sealed record GltfSkin(int[] Joints, Matrix[] InverseBinds);

public sealed record GltfAnimation(string Name, float DurationSeconds, IReadOnlyList<GltfChannel> Channels);

public enum GltfChannelPath
{
    Translation,
    Rotation,
    Scale
}

public enum GltfInterpolation
{
    Linear,
    Step,
    CubicSpline
}

// Values hold one key per time (xyz, or a rotation's xyzw); a cubic spline holds three per time: in-tangent, value,
// out-tangent, the tangents per second (scaled by the interval between the two keys when evaluated).
public sealed record GltfChannel(int Node, GltfChannelPath Path, GltfInterpolation Interpolation, float[] Times, Vector4[] Values);

public enum GltfAlphaMode
{
    Opaque,
    Mask,
    Blend
}

public enum GltfWrap
{
    Repeat,
    Clamp,
    Mirror
}

// Image -1: no texture. TexCoord is the UV set, 0 or 1. Its UVs are transformed by scale, then rotation (radians), then
// offset (KHR_texture_transform).
public readonly record struct GltfTextureRef(int Image, int TexCoord, GltfWrap WrapS, GltfWrap WrapT, Vector2 Offset, Vector2 Scale, float Rotation)
{
    public static GltfTextureRef None { get; } = new(-1, 0, GltfWrap.Repeat, GltfWrap.Repeat, Vector2.Zero, Vector2.One, 0f);

    public bool IsSet => Image >= 0;
}

// The factors multiply their textures, as glTF defines them. The metallic-roughness texture holds roughness in G and
// metallic in B; occlusion is read from R; the clearcoat textures from R (intensity) and G (roughness); sheen roughness
// and diffuse transmission from A.
public sealed record GltfMaterial(string Name)
{
    public GltfAlphaMode Alpha { get; init; }
    // Only a masked material tests its alpha against the cutoff.
    public float AlphaCutoff { get; init; } = 0.5f;
    public bool DoubleSided { get; init; }
    public bool Unlit { get; init; }

    public Vector4 BaseColor { get; init; } = Vector4.One;
    public GltfTextureRef BaseColorTexture { get; init; } = GltfTextureRef.None;
    public float Metallic { get; init; } = 1f;
    public float Roughness { get; init; } = 1f;
    public GltfTextureRef MetallicRoughnessTexture { get; init; } = GltfTextureRef.None;
    public float NormalScale { get; init; } = 1f;
    public GltfTextureRef NormalTexture { get; init; } = GltfTextureRef.None;
    public float OcclusionStrength { get; init; } = 1f;
    public GltfTextureRef OcclusionTexture { get; init; } = GltfTextureRef.None;
    public Vector3 Emissive { get; init; }
    public float EmissiveStrength { get; init; } = 1f;
    public GltfTextureRef EmissiveTexture { get; init; } = GltfTextureRef.None;

    public float Clearcoat { get; init; }
    public GltfTextureRef ClearcoatTexture { get; init; } = GltfTextureRef.None;
    public float ClearcoatRoughness { get; init; }
    public GltfTextureRef ClearcoatRoughnessTexture { get; init; } = GltfTextureRef.None;
    public float ClearcoatNormalScale { get; init; } = 1f;
    public GltfTextureRef ClearcoatNormalTexture { get; init; } = GltfTextureRef.None;

    public Vector3 SheenColor { get; init; }
    public GltfTextureRef SheenColorTexture { get; init; } = GltfTextureRef.None;
    public float SheenRoughness { get; init; }
    public GltfTextureRef SheenRoughnessTexture { get; init; } = GltfTextureRef.None;

    public float DiffuseTransmission { get; init; }
    public GltfTextureRef DiffuseTransmissionTexture { get; init; } = GltfTextureRef.None;
    public Vector3 DiffuseTransmissionColor { get; init; } = Vector3.One;
    public GltfTextureRef DiffuseTransmissionColorTexture { get; init; } = GltfTextureRef.None;
}

public sealed record GltfImage(string MimeType, byte[] Content);
