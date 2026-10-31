using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Physics;

namespace PristonTale.Engine3D.Rendering.Weather;

// Where rain stops: the highest walkable surface of every 8-unit cell in a square of Cells x Cells around a centre, the
// roofs included, read from the field's collision. The cells are kept toroidally (cell x, z at x & (Cells-1),
// z & (Cells-1)), so a moving centre reads only the cells it uncovers, a bounded number each frame; PristonRain uploads
// Heights to the R32F texture the rain shader reads while Changed. A cell with no surface under it holds NoSurface.
public sealed class PristonRainOcclusion
{
    public const int Cells = 256;
    public const float CellSize = 8f;
    public const float NoSurface = -1.0e8f;
    // Cells read per frame once the map is filled: a full map in 16 frames, a walk's new rows at once.
    public const int CellsPerFrame = Cells * Cells / 16;

    private readonly float[] _heights = new float[Cells * Cells];
    private readonly long[] _keys = new long[Cells * Cells];
    private IPristonCollisionQuery _query;

    public PristonRainOcclusion() => Reset(null);

    // The cells in texture order (row z & (Cells-1), column x & (Cells-1)).
    public float[] Heights => _heights;

    // Set when a cell changed since the last upload (MarkUploaded).
    public bool Changed { get; private set; }

    // The world point the map is centred on, once Update read every cell around it.
    public Vector2 Centre { get; private set; }

    public bool Ready { get; private set; }

    // A new field's collision (null: none); every cell is read again.
    public void Reset(IPristonCollisionQuery query)
    {
        _query = query;
        Array.Fill(_keys, long.MinValue);
        Array.Fill(_heights, NoSurface);
        Ready = false;
        Changed = true;
    }

    public void MarkUploaded() => Changed = false;

    // Reads up to CellsPerFrame cells of the square around centre that hold another place.
    public void Update(Vector3 centre)
    {
        var originX = (int)MathF.Floor(centre.X / CellSize) - Cells / 2;
        var originZ = (int)MathF.Floor(centre.Z / CellSize) - Cells / 2;
        var budget = CellsPerFrame;
        var complete = true;
        for (var row = 0; row < Cells && budget > 0; row++)
        {
            var cellZ = originZ + row;
            for (var column = 0; column < Cells; column++)
            {
                var cellX = originX + column;
                var index = Index(cellX, cellZ);
                var key = Key(cellX, cellZ);
                if (_keys[index] == key)
                {
                    continue;
                }

                if (budget-- == 0)
                {
                    complete = false;
                    break;
                }

                _keys[index] = key;
                _heights[index] = Read(cellX, cellZ);
                Changed = true;
            }
        }

        if (complete)
        {
            Centre = new Vector2((originX + Cells / 2) * CellSize, (originZ + Cells / 2) * CellSize);
            Ready = true;
        }
    }

    // The surface rain falls on at a point; NoSurface outside the map or where nothing is under it.
    public float HeightAt(float x, float z)
    {
        var cellX = (int)MathF.Floor(x / CellSize);
        var cellZ = (int)MathF.Floor(z / CellSize);
        var index = Index(cellX, cellZ);
        return _keys[index] == Key(cellX, cellZ) ? _heights[index] : NoSurface;
    }

    // Under a roof: a surface above the point by more than clearance.
    public bool Sheltered(Vector3 position, float clearance) => HeightAt(position.X, position.Z) > position.Y + clearance;

    private float Read(int cellX, int cellZ)
    {
        var point = new Vector3((cellX + 0.5f) * CellSize, 0f, (cellZ + 0.5f) * CellSize);
        return _query is not null && _query.TryGetFloorHeight(point, float.PositiveInfinity, 0f, out var height) ? height : NoSurface;
    }

    private static int Index(int cellX, int cellZ) => (cellZ & (Cells - 1)) * Cells + (cellX & (Cells - 1));

    private static long Key(int cellX, int cellZ) => ((long)cellX << 32) | (uint)cellZ;
}
