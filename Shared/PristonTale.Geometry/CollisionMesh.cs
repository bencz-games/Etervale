using System.Numerics;

namespace PristonTale.Geometry;

// Stage collision built from faces whose material carries the check-face flag. Coordinates are whatever space the
// caller supplies: the server uses Priston world units (x, y, z), the client its MonoGame space (x, y, -z).
// The mesh is immutable after construction, so concurrent queries are safe; a triangle registered in several grid
// cells may be tested more than once per query, which does not change the nearest hit.
public sealed class CollisionMesh
{
    // A triangle is walkable when its normal's Y component exceeds this.
    // A face whose normal rises at least this much is a floor; the wall slide (WallSlide.FloorNormalY) agrees.
    public const float MinFloorNormalY = 0.25f;

    private readonly FloorTriangle[] _floorTriangles;
    private readonly CollisionTriangle[] _collisionTriangles;
    // The XZ grid targets eight triangles per cell and registers every cell overlapped by each triangle's bounds.
    private readonly int[][] _floorGrid;
    private readonly int[][] _collisionGrid;
    private readonly float _gridMinX;
    private readonly float _gridMinZ;
    private readonly float _cellSize;
    private readonly int _gridCols;
    private readonly int _gridRows;

    public CollisionMesh(IReadOnlyList<Triangle> triangles)
    {
        var floorTriangles = new List<FloorTriangle>();
        var collisionTriangles = new List<CollisionTriangle>(triangles.Count);
        foreach (var triangle in triangles)
        {
            collisionTriangles.Add(new CollisionTriangle(triangle.A, triangle.B, triangle.C));
            var normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            if (normal.LengthSquared() < 0.0001f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            if (MathF.Abs(normal.Y) >= MinFloorNormalY)
            {
                floorTriangles.Add(new FloorTriangle(triangle.A, triangle.B, triangle.C));
            }
        }

        _floorTriangles = floorTriangles.ToArray();
        _collisionTriangles = collisionTriangles.ToArray();

        var minX = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxZ = float.MinValue;
        foreach (var triangle in _collisionTriangles)
        {
            var bounds = triangle.Bounds;
            minX = MathF.Min(minX, bounds.MinX);
            minZ = MathF.Min(minZ, bounds.MinZ);
            maxX = MathF.Max(maxX, bounds.MaxX);
            maxZ = MathF.Max(maxZ, bounds.MaxZ);
        }

        if (maxX <= minX || maxZ <= minZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 1f;
            maxZ = 1f;
        }

        var totalTriangles = Math.Max(_collisionTriangles.Length, 1);
        var cellSize = Math.Clamp(MathF.Sqrt((maxX - minX) * (maxZ - minZ) * 8f / totalTriangles), 32f, 512f);
        // One spare cell on each side keeps boundary triangles off the grid edge.
        _gridMinX = minX - cellSize;
        _gridMinZ = minZ - cellSize;
        _cellSize = cellSize;
        _gridCols = Math.Max(1, (int)MathF.Ceiling((maxX + cellSize - _gridMinX) / cellSize));
        _gridRows = Math.Max(1, (int)MathF.Ceiling((maxZ + cellSize - _gridMinZ) / cellSize));
        _floorGrid = BuildGrid(_floorTriangles.Select(static triangle => triangle.Bounds).ToArray());
        _collisionGrid = BuildGrid(_collisionTriangles.Select(static triangle => triangle.Bounds).ToArray());
    }

    public int FloorTriangleCount => _floorTriangles.Length;

    public int CollisionTriangleCount => _collisionTriangles.Length;

    private int[][] BuildGrid((float MinX, float MinZ, float MaxX, float MaxZ)[] bounds)
    {
        var cells = new List<int>?[_gridCols * _gridRows];
        for (var index = 0; index < bounds.Length; index++)
        {
            var (minX, minZ, maxX, maxZ) = bounds[index];
            for (var row = WorldToRow(minZ); row <= WorldToRow(maxZ); row++)
            {
                for (var col = WorldToCol(minX); col <= WorldToCol(maxX); col++)
                {
                    (cells[row * _gridCols + col] ??= new List<int>(8)).Add(index);
                }
            }
        }

        var grid = new int[cells.Length][];
        for (var index = 0; index < cells.Length; index++)
        {
            grid[index] = cells[index]?.ToArray() ?? [];
        }

        return grid;
    }

    private int WorldToCol(float x) => Math.Clamp((int)MathF.Floor((x - _gridMinX) / _cellSize), 0, _gridCols - 1);

    private int WorldToRow(float z) => Math.Clamp((int)MathF.Floor((z - _gridMinZ) / _cellSize), 0, _gridRows - 1);

    // Highest walkable surface under (x, z).
    public bool TryGetFloorHeight(float x, float z, out float height) => TryGetFloorHeight(x, z, float.PositiveInfinity, 0f, out height);

    // Highest walkable surface under (x, z) that is no more than stepUp above referenceHeight (ceilings are ignored).
    public bool TryGetFloorHeight(float x, float z, float referenceHeight, float stepUp, out float height)
    {
        var cell = _floorGrid[WorldToRow(z) * _gridCols + WorldToCol(x)];
        var found = false;
        var best = float.MinValue;
        foreach (var index in cell)
        {
            if (_floorTriangles[index].TryGetHeight(x, z, out var candidate) && candidate <= referenceHeight + stepUp && (!found || candidate > best))
            {
                best = candidate;
                found = true;
            }
        }

        height = found ? best : 0f;
        return found;
    }

    public bool TryRaycastFloor(Vector3 origin, Vector3 direction, out Vector3 point)
    {
        var visitor = new RaycastVisitor(this, origin, direction, float.MaxValue, floor: true);
        Traverse(origin.X, origin.Z, direction.X, direction.Z, float.MaxValue, ref visitor);
        point = visitor.BestPoint;
        return visitor.Found;
    }

    public bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length < 0.0001f)
        {
            hitPoint = start;
            hitNormal = Vector3.Zero;
            return false;
        }

        direction /= length;
        var visitor = new RaycastVisitor(this, start, direction, length, floor: false);
        Traverse(start.X, start.Z, direction.X, direction.Z, length, ref visitor);
        hitPoint = visitor.BestPoint;
        hitNormal = visitor.BestNormal;
        return visitor.Found;
    }

    // XZ DDA through the grid; vertical rays visit only their starting cell.
    private void Traverse(float ox, float oz, float dx, float dz, float maxT, ref RaycastVisitor visitor)
    {
        var col = WorldToCol(ox);
        var row = WorldToRow(oz);
        if (MathF.Abs(dx) < 1e-9f && MathF.Abs(dz) < 1e-9f)
        {
            visitor.Visit(row * _gridCols + col);
            return;
        }

        var stepCol = dx >= 0f ? 1 : -1;
        var stepRow = dz >= 0f ? 1 : -1;
        var cellLeft = _gridMinX + col * _cellSize;
        var cellBottom = _gridMinZ + row * _cellSize;
        var tMaxX = MathF.Abs(dx) > 1e-9f ? ((dx > 0f ? cellLeft + _cellSize : cellLeft) - ox) / dx : float.MaxValue;
        var tMaxZ = MathF.Abs(dz) > 1e-9f ? ((dz > 0f ? cellBottom + _cellSize : cellBottom) - oz) / dz : float.MaxValue;
        var tDeltaX = MathF.Abs(dx) > 1e-9f ? _cellSize / MathF.Abs(dx) : float.MaxValue;
        var tDeltaZ = MathF.Abs(dz) > 1e-9f ? _cellSize / MathF.Abs(dz) : float.MaxValue;
        while (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
        {
            visitor.Visit(row * _gridCols + col);
            if (tMaxX < tMaxZ)
            {
                if (tMaxX > maxT)
                {
                    break;
                }

                col += stepCol;
                tMaxX += tDeltaX;
            }
            else
            {
                if (tMaxZ > maxT)
                {
                    break;
                }

                row += stepRow;
                tMaxZ += tDeltaZ;
            }
        }
    }

    private struct RaycastVisitor(CollisionMesh mesh, Vector3 origin, Vector3 direction, float maximumDistance, bool floor)
    {
        private float _bestDistance = maximumDistance;

        public Vector3 BestPoint { get; private set; }
        public Vector3 BestNormal { get; private set; }
        public bool Found { get; private set; }

        public void Visit(int cellIndex)
        {
            var cell = floor ? mesh._floorGrid[cellIndex] : mesh._collisionGrid[cellIndex];
            foreach (var index in cell)
            {
                var hit = floor
                    ? mesh._floorTriangles[index].TryRaycast(origin, direction, out var distance)
                    : mesh._collisionTriangles[index].TryRaycast(origin, direction, out distance);
                if (!hit || distance >= _bestDistance)
                {
                    continue;
                }

                _bestDistance = distance;
                BestPoint = origin + direction * distance;
                BestNormal = floor ? Vector3.UnitY : mesh._collisionTriangles[index].Normal;
                Found = true;
            }
        }
    }

    private readonly struct FloorTriangle
    {
        private readonly Vector3 _a;
        private readonly Vector3 _b;
        private readonly Vector3 _c;
        private readonly float _denominator;

        public FloorTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            _a = a;
            _b = b;
            _c = c;
            _denominator = (_b.Z - _c.Z) * (_a.X - _c.X) + (_c.X - _b.X) * (_a.Z - _c.Z);
        }

        public (float MinX, float MinZ, float MaxX, float MaxZ) Bounds => (MathF.Min(_a.X, MathF.Min(_b.X, _c.X)), MathF.Min(_a.Z, MathF.Min(_b.Z, _c.Z)),
            MathF.Max(_a.X, MathF.Max(_b.X, _c.X)), MathF.Max(_a.Z, MathF.Max(_b.Z, _c.Z)));

        public bool TryGetHeight(float x, float z, out float height)
        {
            height = 0f;
            if (MathF.Abs(_denominator) < 0.0001f)
            {
                return false;
            }

            var u = ((_b.Z - _c.Z) * (x - _c.X) + (_c.X - _b.X) * (z - _c.Z)) / _denominator;
            var v = ((_c.Z - _a.Z) * (x - _c.X) + (_a.X - _c.X) * (z - _c.Z)) / _denominator;
            var w = 1f - u - v;
            if (u < -0.001f || v < -0.001f || w < -0.001f)
            {
                return false;
            }

            height = _a.Y * u + _b.Y * v + _c.Y * w;
            return true;
        }

        public bool TryRaycast(Vector3 origin, Vector3 direction, out float distance) => CollisionTriangle.Raycast(_a, _b, _c, origin, direction, out distance);
    }

    private readonly struct CollisionTriangle
    {
        private readonly Vector3 _a;
        private readonly Vector3 _b;
        private readonly Vector3 _c;

        public CollisionTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            _a = a;
            _b = b;
            _c = c;
            var cross = Vector3.Cross(b - a, c - a);
            Normal = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.UnitY;
        }

        // Unoriented: the sign follows the winding, so callers that need a facing normal flip it against their motion.
        public Vector3 Normal { get; }

        public (float MinX, float MinZ, float MaxX, float MaxZ) Bounds => (MathF.Min(_a.X, MathF.Min(_b.X, _c.X)), MathF.Min(_a.Z, MathF.Min(_b.Z, _c.Z)),
            MathF.Max(_a.X, MathF.Max(_b.X, _c.X)), MathF.Max(_a.Z, MathF.Max(_b.Z, _c.Z)));

        public bool TryRaycast(Vector3 origin, Vector3 direction, out float distance) => Raycast(_a, _b, _c, origin, direction, out distance);

        // Möller–Trumbore, accepting hits at non-negative distance.
        public static bool Raycast(Vector3 a, Vector3 b, Vector3 c, Vector3 origin, Vector3 direction, out float distance)
        {
            distance = 0f;
            var edge1 = b - a;
            var edge2 = c - a;
            var h = Vector3.Cross(direction, edge2);
            var determinant = Vector3.Dot(edge1, h);
            if (MathF.Abs(determinant) < 0.000001f)
            {
                return false;
            }

            var inverse = 1f / determinant;
            var s = origin - a;
            var u = inverse * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
            {
                return false;
            }

            var q = Vector3.Cross(s, edge1);
            var v = inverse * Vector3.Dot(direction, q);
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            distance = inverse * Vector3.Dot(edge2, q);
            return distance >= 0f;
        }
    }
}

public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C);
