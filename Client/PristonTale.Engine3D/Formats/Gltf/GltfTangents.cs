using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Formats.Gltf;

// Tangents for the vertices of a primitive that has none, from its triangles and the UV set its normal map reads
// (Lengyel): per vertex the U direction of its triangles summed and made orthogonal to the normal, with w the sign glTF
// gives the bitangent (bitangent = cross(normal, tangent) * w), which points toward decreasing V since glTF's UV origin
// is the image's top left. A vertex whose UVs are degenerate takes any direction across its normal.
public static class GltfTangents
{
    public static void Generate(GltfVertex[] vertices, int[] indices, int uvSet)
    {
        var tangents = new Vector3[vertices.Length];
        var bitangents = new Vector3[vertices.Length];
        for (var index = 0; index < indices.Length; index += 3)
        {
            var (a, b, c) = (indices[index], indices[index + 1], indices[index + 2]);
            var (uvA, uvB, uvC) = (Uv(vertices[a], uvSet), Uv(vertices[b], uvSet), Uv(vertices[c], uvSet));
            var edge1 = vertices[b].Position - vertices[a].Position;
            var edge2 = vertices[c].Position - vertices[a].Position;
            var (du1, dv1) = (uvB.X - uvA.X, uvB.Y - uvA.Y);
            var (du2, dv2) = (uvC.X - uvA.X, uvC.Y - uvA.Y);
            var determinant = du1 * dv2 - du2 * dv1;
            if (MathF.Abs(determinant) < 1e-12f)
            {
                continue;
            }

            var inverse = 1f / determinant;
            var tangent = (edge1 * dv2 - edge2 * dv1) * inverse;
            var bitangent = (edge2 * du1 - edge1 * du2) * inverse;
            foreach (var vertex in (ReadOnlySpan<int>)[a, b, c])
            {
                tangents[vertex] += tangent;
                bitangents[vertex] += bitangent;
            }
        }

        for (var vertex = 0; vertex < vertices.Length; vertex++)
        {
            var normal = vertices[vertex].Normal;
            var tangent = tangents[vertex] - normal * Vector3.Dot(normal, tangents[vertex]);
            if (tangent.LengthSquared() < 1e-12f)
            {
                tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX);
            }

            tangent = Vector3.Normalize(tangent);
            var sign = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[vertex]) > 0f ? -1f : 1f;
            vertices[vertex].Tangent = new Vector4(tangent, sign);
        }
    }

    private static Vector2 Uv(in GltfVertex vertex, int uvSet) => uvSet == 0 ? vertex.Uv0 : vertex.Uv1;
}
