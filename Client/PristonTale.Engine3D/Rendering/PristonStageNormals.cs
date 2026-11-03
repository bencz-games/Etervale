using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// Smooth normals for a stage, computed over the whole stage before its faces are split by material (the stage file
// carries no vertex normals), so the sun lights neighbouring ground triangles alike across material and UV boundaries.
// A corner averages the area-weighted normals of the faces that touch its position and lean at most CreaseDegrees from
// its own face, so ground stays smooth and walls keep their edge with the floor. Faces never drawn (collision-only
// materials) take no part, so a hidden ramp or blocker sharing the ground's vertices does not bend its normal.
internal static class PristonStageNormals
{
    public const float CreaseDegrees = 60f;

    // The stage's corner normals over its drawn faces (a face without a material counts as drawn).
    public static Vector3[] Compute(PristonStage stage) =>
        Compute(stage.Faces, stage.Vertices.Select(static vertex => vertex.Position).ToArray(),
            face => face.MaterialIndex < 0 || face.MaterialIndex >= stage.Materials.Count
                || stage.Materials[face.MaterialIndex] is { IsInvisible: false, HasNoRenderableTexture: false });

    // One normal per face corner: index face * 3 + corner (A, B, C), zero for a face that is skipped, degenerate or not
    // drawn.
    public static Vector3[] Compute(IReadOnlyList<PristonStageFace> faces, IReadOnlyList<Vector3> positions, Func<PristonStageFace, bool> drawn = null)
    {
        var weighted = new Vector3[faces.Count];
        var unit = new Vector3[faces.Count];
        var byPosition = new Dictionary<Vector3, List<int>>();
        for (var face = 0; face < faces.Count; face++)
        {
            if (drawn?.Invoke(faces[face]) == false || !TryCorners(faces[face], positions, out var a, out var b, out var c))
            {
                continue;
            }

            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < 1e-6f)
            {
                continue;
            }

            weighted[face] = cross;
            unit[face] = Vector3.Normalize(cross);
            Touch(byPosition, a, face);
            Touch(byPosition, b, face);
            Touch(byPosition, c, face);
        }

        var crease = MathF.Cos(MathHelper.ToRadians(CreaseDegrees));
        var normals = new Vector3[faces.Count * 3];
        for (var face = 0; face < faces.Count; face++)
        {
            if (unit[face] == Vector3.Zero || !TryCorners(faces[face], positions, out var a, out var b, out var c))
            {
                continue;
            }

            normals[face * 3] = Corner(byPosition[a], face, unit, weighted, crease);
            normals[face * 3 + 1] = Corner(byPosition[b], face, unit, weighted, crease);
            normals[face * 3 + 2] = Corner(byPosition[c], face, unit, weighted, crease);
        }

        return normals;
    }

    private static Vector3 Corner(List<int> touching, int face, Vector3[] unit, Vector3[] weighted, float crease)
    {
        var sum = Vector3.Zero;
        foreach (var other in touching)
        {
            if (Vector3.Dot(unit[face], unit[other]) >= crease)
            {
                sum += weighted[other];
            }
        }

        return sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : unit[face];
    }

    private static void Touch(Dictionary<Vector3, List<int>> byPosition, Vector3 position, int face)
    {
        if (!byPosition.TryGetValue(position, out var faces))
        {
            faces = new List<int>(6);
            byPosition.Add(position, faces);
        }

        faces.Add(face);
    }

    private static bool TryCorners(PristonStageFace face, IReadOnlyList<Vector3> positions, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        var count = positions.Count;
        if ((uint)face.A >= (uint)count || (uint)face.B >= (uint)count || (uint)face.C >= (uint)count)
        {
            a = b = c = default;
            return false;
        }

        a = positions[face.A];
        b = positions[face.B];
        c = positions[face.C];
        return true;
    }
}
