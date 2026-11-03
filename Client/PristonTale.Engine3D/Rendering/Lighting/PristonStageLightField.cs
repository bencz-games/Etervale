using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering.Lighting;

// The stage's authored light over its floors, for what stands on them. Stage geometry is tinted by its authored
// per-vertex light, which holds the art's shading: dark under trees, in alleys and under roofs, bright in the open.
// Actors carry no such light, so without it they stay as bright in a tree's shade as in the open while the floor
// around them darkens. The field indexes the stage's floor triangles (facing up) on a grid of the ground plane;
// LightAt finds the highest floor at or just below a point and returns its interpolated vertex light (mean linear
// luminance), averaged over a few points around it so a walk across the triangles does not flicker.
internal sealed class PristonStageLightField
{
    private const float CellSize = 128f;
    private const float FloorFacing = 0.6f;
    private const float AboveTolerance = 40f;
    private const float BelowReach = 400f;
    private const float SpreadRadius = 24f;

    private readonly Vector3[] _positions;
    private readonly float[] _light;
    private readonly int[][] _cells;
    private readonly (int A, int B, int C)[] _floors;
    private readonly float _minX;
    private readonly float _minZ;
    private readonly int _columns;
    private readonly int _rows;

    private PristonStageLightField(
        Vector3[] positions,
        float[] light,
        (int A, int B, int C)[] floors,
        int[][] cells,
        float minX,
        float minZ,
        int columns,
        int rows)
    {
        _positions = positions;
        _light = light;
        _floors = floors;
        _cells = cells;
        _minX = minX;
        _minZ = minZ;
        _columns = columns;
        _rows = rows;
    }

    public static PristonStageLightField Build(PristonStage stage)
    {
        var positions = new Vector3[stage.Vertices.Count];
        var light = new float[stage.Vertices.Count];
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = stage.Vertices[index].Position;
            light[index] = (float)PristonStageBakedLight.LinearLuminance(stage.Vertices[index].Color);
        }

        var floors = new List<(int A, int B, int C)>();
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var face in stage.Faces)
        {
            if (!Valid(face.A, positions.Length) || !Valid(face.B, positions.Length) || !Valid(face.C, positions.Length)
                || !IsFloor(stage, face))
            {
                continue;
            }

            var a = positions[face.A];
            var b = positions[face.B];
            var c = positions[face.C];
            var normal = Vector3.Cross(b - a, c - a);
            var length = normal.Length();
            // Original winding varies: a floor faces up either way.
            if (length < 1e-4f || MathF.Abs(normal.Y) / length < FloorFacing)
            {
                continue;
            }

            floors.Add((face.A, face.B, face.C));
            foreach (var p in new[] { a, b, c })
            {
                min = Vector2.Min(min, new Vector2(p.X, p.Z));
                max = Vector2.Max(max, new Vector2(p.X, p.Z));
            }
        }

        if (floors.Count == 0)
        {
            return new PristonStageLightField(positions, light, [], [], 0f, 0f, 0, 0);
        }

        var columns = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / CellSize) + 1);
        var rows = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / CellSize) + 1);
        var lists = new List<int>[columns * rows];
        for (var index = 0; index < floors.Count; index++)
        {
            var (fa, fb, fc) = floors[index];
            var lo = Vector3.Min(Vector3.Min(positions[fa], positions[fb]), positions[fc]);
            var hi = Vector3.Max(Vector3.Max(positions[fa], positions[fb]), positions[fc]);
            var x0 = Column(lo.X, min.X, columns);
            var x1 = Column(hi.X, min.X, columns);
            var z0 = Column(lo.Z, min.Y, rows);
            var z1 = Column(hi.Z, min.Y, rows);
            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    (lists[z * columns + x] ??= []).Add(index);
                }
            }
        }

        var cells = new int[lists.Length][];
        for (var index = 0; index < lists.Length; index++)
        {
            cells[index] = lists[index]?.ToArray() ?? [];
        }

        return new PristonStageLightField(positions, light, floors.ToArray(), cells, min.X, min.Y, columns, rows);
    }

    // The floor light at a point (an actor's feet), or false where no floor of this stage lies under it.
    public bool TryLightAt(Vector3 point, out float light)
    {
        var sum = 0f;
        var count = 0;
        Span<Vector2> offsets = [Vector2.Zero, new(SpreadRadius, 0f), new(-SpreadRadius, 0f), new(0f, SpreadRadius), new(0f, -SpreadRadius)];
        foreach (var offset in offsets)
        {
            if (TrySample(point + new Vector3(offset.X, 0f, offset.Y), out var sample))
            {
                sum += sample;
                count++;
            }
        }

        light = count > 0 ? sum / count : 0f;
        return count > 0;
    }

    private bool TrySample(Vector3 point, out float light)
    {
        light = 0f;
        if (_columns == 0)
        {
            return false;
        }

        var x = (int)MathF.Floor((point.X - _minX) / CellSize);
        var z = (int)MathF.Floor((point.Z - _minZ) / CellSize);
        if (x < 0 || z < 0 || x >= _columns || z >= _rows)
        {
            return false;
        }

        var bestHeight = float.MinValue;
        foreach (var index in _cells[z * _columns + x])
        {
            var (ia, ib, ic) = _floors[index];
            var a = _positions[ia];
            var b = _positions[ib];
            var c = _positions[ic];
            if (!Barycentric(new Vector2(point.X, point.Z), new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), new Vector2(c.X, c.Z), out var wa, out var wb, out var wc))
            {
                continue;
            }

            var height = wa * a.Y + wb * b.Y + wc * c.Y;
            if (height > point.Y + AboveTolerance || height < point.Y - BelowReach || height <= bestHeight)
            {
                continue;
            }

            bestHeight = height;
            light = wa * _light[ia] + wb * _light[ib] + wc * _light[ic];
        }

        return bestHeight > float.MinValue;
    }

    private static bool Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float wa, out float wb, out float wc)
    {
        var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        wa = wb = wc = 0f;
        if (MathF.Abs(denominator) < 1e-6f)
        {
            return false;
        }

        wa = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / denominator;
        wb = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / denominator;
        wc = 1f - wa - wb;
        const float edge = -1e-4f;
        return wa >= edge && wb >= edge && wc >= edge;
    }

    private static bool IsFloor(PristonStage stage, PristonStageFace face)
    {
        if (face.MaterialIndex < 0 || face.MaterialIndex >= stage.Materials.Count)
        {
            return true;
        }

        // Not foliage or other cut-outs (a bush at an actor's feet is not the ground it stands on), water or glows.
        var material = stage.Materials[face.MaterialIndex];
        return !PristonMaterialRuntimeFactory.UsesAlphaTest(material) && !material.IsInvisible && !material.IsWater
            && !PristonBlendType.IsLate(material.BlendType);
    }

    private static int Column(float value, float origin, int count) => Math.Clamp((int)MathF.Floor((value - origin) / CellSize), 0, count - 1);

    private static bool Valid(int index, int count) => index >= 0 && index < count;
}
