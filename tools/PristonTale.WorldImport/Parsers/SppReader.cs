using System.Buffers.Binary;

namespace PristonTale.WorldImport.Parsers;

// Reads one field .spp binary spawn-point file into the supplied ParsedField.
// Layout: a fixed array of 200 spawn-point records { int state; int x; int z; } (12 bytes each, no
// header) = 2400 bytes total.
// Confirmed against real assets (every *.spp on disk is exactly 2400 bytes).
//
// A point is "live" when its state != 0; we import every record (state carried) and let the
// factory map state != 0 → SpawnPoint.Active. Coords are world units (FieldSpawner.PointToWorld takes them as
// they are).
public sealed class SppReader
{
    private const int RecordSize = 12;   // 3 × int32
    private const int RecordCount = 200;

    public void Read(string path, ParsedField field)
    {
        var bytes = File.ReadAllBytes(path);
        // Trust real bytes: derive the count from the file length, but never exceed the documented 200.
        var count = Math.Min(bytes.Length / RecordSize, RecordCount);
        for (var i = 0; i < count; i++)
        {
            var off = i * RecordSize;
            var state = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off, 4));
            var x = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + 4, 4));
            var z = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(off + 8, 4));
            // Skip empty slots: state==0 AND x==0 AND z==0 is an unused entry (the array is fixed-size).
            if (state == 0 && x == 0 && z == 0)
                continue;
            field.SpawnPoints.Add((i, state, x, z));
        }
    }
}
