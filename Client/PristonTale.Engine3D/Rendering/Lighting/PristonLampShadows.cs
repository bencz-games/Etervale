using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Omnidirectional shadows that actors cast from the stage lamps nearest the camera focus: each lamp keeps a row of an
// R32F atlas with its six cube faces (90-degree views along the axes, Shaders/Lighting/Shading.hlsli LampShadow),
// holding the distance to the lamp over its radius. A lamp keeps its row while it stays among the nearest; its faces are
// redrawn when it is new and on every frame an actor is within its radius (once more after the last one left, to clear
// its shadow), so the shadows follow the actors smoothly and empty lamps cost nothing.
internal sealed class PristonLampShadows : IDisposable
{
    public const int MaxLamps = 4;
    private const float NearPlane = 10f;

    private readonly GraphicsDevice _device;
    private readonly IClientLog _log;
    private readonly Vector3[] _rowPositions = new Vector3[MaxLamps];
    private readonly float[] _rowRadii = new float[MaxLamps];
    private readonly bool[] _rowUsed = new bool[MaxLamps];
    private readonly bool[] _rowClaimed = new bool[MaxLamps];
    private readonly bool[] _rowOccupied = new bool[MaxLamps];
    private RenderTarget2D _atlas;

    public PristonLampShadows(GraphicsDevice device, IClientLog log)
    {
        _device = device;
        _log = log;
    }

    public int Lamps { get; private set; }
    public int FaceSize { get; private set; }
    public RenderTarget2D Atlas => _atlas;

    // Rows of the frame's local lights (-1: no shadow), parallel to the light list.
    public int[] Rows { get; } = new int[PristonShaderLibrary.MaxLocalLightCount];

    // Without a device (tests) only the row assignment works.
    public void Configure(int lamps, int faceSize)
    {
        lamps = Math.Clamp(lamps, 0, MaxLamps);
        if (lamps == Lamps && faceSize == FaceSize && (_atlas is not null || lamps == 0))
        {
            return;
        }

        _atlas?.Dispose();
        _atlas = null;
        Lamps = lamps;
        FaceSize = faceSize;
        Array.Clear(_rowUsed);
        if (lamps == 0)
        {
            return;
        }

        if (_device is null)
        {
            return;
        }

        try
        {
            _atlas = new RenderTarget2D(_device, faceSize * PristonCubeFaces.Count, faceSize * lamps, false, SurfaceFormat.Single, DepthFormat.Depth24, 0,
                RenderTargetUsage.PreserveContents);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            _log.Warn($"[Rendering] Lamp shadow atlas {faceSize * PristonCubeFaces.Count}x{faceSize * lamps} allocation failed; lamp shadows off: {exception.Message}");
            Lamps = 0;
        }
    }

    // Gives the nearest lamps (lights from firstLamp on, nearest first) a row, keeping the rows of lamps that stay.
    // Returns the rows to draw this frame as a bit mask: new lamps, and lamps whose sphere holds a caster now or held one
    // at the last drawing.
    public int Assign(IReadOnlyList<PristonPointLight> lights, int firstLamp, Func<Vector3, float, bool> hasCasters = null)
    {
        Array.Fill(Rows, -1);
        if (Lamps == 0)
        {
            return 0;
        }

        Array.Clear(_rowClaimed);
        var lampCount = Math.Min(Lamps, Math.Max(lights.Count - firstLamp, 0));
        var redraw = 0;
        for (var i = 0; i < lampCount; i++)
        {
            var light = lights[firstLamp + i];
            var row = FindRow(light);
            if (row < 0)
            {
                row = FreeRow();
                _rowPositions[row] = light.Position;
                _rowRadii[row] = light.Radius;
                _rowUsed[row] = true;
                redraw |= 1 << row;
            }

            _rowClaimed[row] = true;
            Rows[firstLamp + i] = row;
        }

        for (var row = 0; row < Lamps; row++)
        {
            _rowUsed[row] = _rowClaimed[row];
            var occupied = _rowClaimed[row] && hasCasters is not null && hasCasters(_rowPositions[row], _rowRadii[row]);
            if (occupied || _rowClaimed[row] && _rowOccupied[row])
            {
                redraw |= 1 << row;
            }

            _rowOccupied[row] = occupied;
        }

        return redraw;
    }

    private int FindRow(PristonPointLight light)
    {
        for (var row = 0; row < Lamps; row++)
        {
            if (_rowUsed[row] && !_rowClaimed[row] && _rowPositions[row] == light.Position && _rowRadii[row] == light.Radius)
            {
                return row;
            }
        }

        return -1;
    }

    private int FreeRow()
    {
        for (var row = 0; row < Lamps; row++)
        {
            if (!_rowClaimed[row] && !_rowUsed[row])
            {
                return row;
            }
        }

        for (var row = 0; row < Lamps; row++)
        {
            if (!_rowClaimed[row])
            {
                return row;
            }
        }

        throw new InvalidOperationException("More lamps than shadow rows.");
    }

    public Vector3 RowPosition(int row) => _rowPositions[row];

    public float RowRadius(int row) => _rowRadii[row];

    public Rectangle FaceRectangle(int row, int face) => new(face * FaceSize, row * FaceSize, FaceSize, FaceSize);

    // The face's view-projection, as LampShadow in Shaders/Lighting/Shading.hlsli inverts it.
    public static Matrix FaceViewProjection(Vector3 position, float radius, int face)
    {
        var view = PristonCubeFaces.View(face, position);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1f, NearPlane, MathF.Max(radius, NearPlane * 2f));
        return view * projection;
    }

    public void Dispose() => _atlas?.Dispose();
}
