using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Lighting;

// The six faces of a cube view: +X, -X, +Y, -Y, +Z, -Z, each looking along its axis with its up. The shaders hold the
// same table (Shaders/Lighting/CubeFaces.hlsli).
internal static class PristonCubeFaces
{
    public const int Count = 6;

    private static readonly Vector3[] Forward =
    [
        Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ
    ];

    private static readonly Vector3[] Up =
    [
        Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY
    ];

    public static Matrix View(int face, Vector3 position) => Matrix.CreateLookAt(position, position + Forward[face], Up[face]);
}
