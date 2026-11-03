using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// Original model files store one smooth normal per vertex, which is how the original shades a body, but averaged across
// perpendicular faces too, where strong sun and sky light turn it into bright-to-dark gradients across flat faces. So a
// skinned or animated mesh's corner keeps the file's normal while it stays within AuthoredCosine of the normal rebuilt
// from the posed faces around it (the faces at the same position within the crease angle), and takes the rebuilt one
// beyond: a hard edge the file averaged across, or a broken normal. The rebuilt normal returns to its bone's local space,
// where the file's already is and where the vertex shader's bone transform brings it back.
internal static class PristonModelNormals
{
    private static readonly float CreaseCosine = MathF.Cos(MathHelper.ToRadians(PristonStageNormals.CreaseDegrees));
    // 40 degrees: a hard edge's averaged normal lies 45 (two faces) to 55 degrees (a box corner) off each face, the bends
    // of a low-polygon body's smooth surface within it.
    private const float AuthoredCosine = 0.766f;
    // A smooth position leaves out the faces turned away from the face (the back of a two-sided sheet).
    private const float BackFaceCosine = -0.5f;
    private const float PositionWeld = 0.01f;

    public static Dictionary<PristonModelFace, Vector3[]>[] Build(PristonModel model, Func<int, int, Matrix> vertexPose)
    {
        var result = new Dictionary<PristonModelFace, Vector3[]>[model.Objects.Count];
        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            result[objectIndex] = BuildObject(model.Objects[objectIndex], objectIndex, vertexPose);
        }

        return result;
    }

    private static Dictionary<PristonModelFace, Vector3[]> BuildObject(PristonModelObject obj, int objectIndex, Func<int, int, Matrix> vertexPose)
    {
        var poses = new Matrix[obj.Vertices.Count];
        var posed = new Vector3[obj.Vertices.Count];
        var welded = new int[obj.Vertices.Count];
        var welds = new Dictionary<(int X, int Y, int Z), int>();
        for (var vertexIndex = 0; vertexIndex < obj.Vertices.Count; vertexIndex++)
        {
            poses[vertexIndex] = vertexPose(objectIndex, vertexIndex);
            posed[vertexIndex] = Vector3.Transform(obj.Vertices[vertexIndex].Position, poses[vertexIndex]);
            var key = (Quantize(posed[vertexIndex].X), Quantize(posed[vertexIndex].Y), Quantize(posed[vertexIndex].Z));
            if (!welds.TryGetValue(key, out var weld))
            {
                weld = welds.Count;
                welds.Add(key, weld);
            }

            welded[vertexIndex] = weld;
        }

        var faces = new List<PristonModelFace>(obj.Faces.Count);
        var areaNormals = new List<Vector3>(obj.Faces.Count);
        var unitNormals = new List<Vector3>(obj.Faces.Count);
        var facesAtPosition = new List<int>[welds.Count];
        foreach (var face in obj.Faces)
        {
            if (!IsValid(face, obj.Vertices.Count))
            {
                continue;
            }

            var areaNormal = Vector3.Cross(posed[face.B] - posed[face.A], posed[face.C] - posed[face.A]);
            var faceIndex = faces.Count;
            faces.Add(face);
            areaNormals.Add(areaNormal);
            unitNormals.Add(areaNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(areaNormal) : Vector3.Zero);
            foreach (var vertexIndex in (ReadOnlySpan<int>)[face.A, face.B, face.C])
            {
                (facesAtPosition[welded[vertexIndex]] ??= []).Add(faceIndex);
            }
        }

        var facesAtEdge = new Dictionary<(int, int), List<int>>();
        for (var faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            foreach (var edge in Edges(faces[faceIndex], welded))
            {
                if (!facesAtEdge.TryGetValue(edge, out var list))
                {
                    facesAtEdge.Add(edge, list = []);
                }

                list.Add(faceIndex);
            }
        }

        // A face creased against the faces across all three of its edges is a fold of a low-polygon body (a monster's
        // neck), not a hard surface, where every facet keeps a triangle in its plane across its diagonal: alone it shaded
        // as one flat, darker triangle. The positions of the fold and of the faces across its edges are smooth, every face
        // there taking all of them, as the file's own smooth normals are, so the bend shades without a facet.
        var smooth = new bool[welds.Count];
        for (var faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            var own = unitNormals[faceIndex];
            if (own == Vector3.Zero || Edges(faces[faceIndex], welded).Any(edge =>
                    facesAtEdge[edge].Any(neighbour => neighbour != faceIndex && Vector3.Dot(unitNormals[neighbour], own) >= CreaseCosine)))
            {
                continue;
            }

            foreach (var edge in Edges(faces[faceIndex], welded))
            {
                foreach (var neighbour in facesAtEdge[edge])
                {
                    var face = faces[neighbour];
                    smooth[welded[face.A]] = smooth[welded[face.B]] = smooth[welded[face.C]] = true;
                }
            }
        }

        var corners = new Dictionary<PristonModelFace, Vector3[]>(faces.Count, ReferenceEqualityComparer.Instance);
        for (var faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            var face = faces[faceIndex];
            var own = unitNormals[faceIndex];
            var normals = new Vector3[3];
            var vertices = (ReadOnlySpan<int>)[face.A, face.B, face.C];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertexIndex = vertices[corner];
                var position = welded[vertexIndex];
                var sum = Vector3.Zero;
                if (own != Vector3.Zero)
                {
                    foreach (var neighbour in facesAtPosition[position])
                    {
                        var cosine = Vector3.Dot(unitNormals[neighbour], own);
                        if (cosine >= CreaseCosine || smooth[position] && cosine > BackFaceCosine)
                        {
                            sum += areaNormals[neighbour];
                        }
                    }
                }

                normals[corner] = CornerNormal(sum, poses[vertexIndex], obj.Vertices[vertexIndex].Normal);
            }

            corners[face] = normals;
        }

        return corners;
    }

    private static Vector3 CornerNormal(Vector3 posedNormal, Matrix pose, Vector3 authored)
    {
        if (authored.LengthSquared() > 1e-6f && posedNormal.LengthSquared() > 1e-12f)
        {
            var posedAuthored = Vector3.TransformNormal(authored, pose);
            if (posedAuthored.LengthSquared() > 1e-12f &&
                Vector3.Dot(Vector3.Normalize(posedAuthored), Vector3.Normalize(posedNormal)) >= AuthoredCosine)
            {
                return Vector3.Normalize(authored);
            }
        }

        return ToBoneSpace(posedNormal, pose, authored);
    }

    private static Vector3 ToBoneSpace(Vector3 posedNormal, Matrix pose, Vector3 authored)
    {
        if (posedNormal.LengthSquared() > 1e-12f && MathF.Abs(pose.Determinant()) > 1e-12f)
        {
            var local = Vector3.TransformNormal(posedNormal, Matrix.Invert(pose));
            if (local.LengthSquared() > 1e-12f)
            {
                return Vector3.Normalize(local);
            }
        }

        return authored.LengthSquared() > 1e-6f ? Vector3.Normalize(authored) : Vector3.Up;
    }

    private static IEnumerable<(int, int)> Edges(PristonModelFace face, int[] welded)
    {
        var a = welded[face.A];
        var b = welded[face.B];
        var c = welded[face.C];
        yield return (Math.Min(a, b), Math.Max(a, b));
        yield return (Math.Min(b, c), Math.Max(b, c));
        yield return (Math.Min(c, a), Math.Max(c, a));
    }

    private static int Quantize(float value) => (int)MathF.Round(value / PositionWeld);

    private static bool IsValid(PristonModelFace face, int vertexCount) =>
        (uint)face.A < (uint)vertexCount && (uint)face.B < (uint)vertexCount && (uint)face.C < (uint)vertexCount;
}
