using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace PristonTale.Engine3D.Rendering.Gltf;

// The vertex of the GLTF variant of Surface.fx: four bone slots of the batch's palette (BLENDINDICES as uint4) with their
// weights, the tangent (w the bitangent's sign), linear colour and two UV sets.
internal struct GltfGpuVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.Color, 0),
        new VertexElement(40, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(56, VertexElementFormat.Vector4, VertexElementUsage.Tangent, 0),
        new VertexElement(72, VertexElementFormat.Byte4, VertexElementUsage.BlendIndices, 0),
        new VertexElement(76, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0));

    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
    public Vector2 Uv0;
    public Vector2 Uv1;
    public Vector4 Tangent;
    public Byte4 Bones;
    public Vector4 Weights;

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
