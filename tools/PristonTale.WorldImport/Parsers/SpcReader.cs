using System.Buffers.Binary;
using PristonTale.Assets;

namespace PristonTale.WorldImport.Parsers;

// Reads one field .spc binary fixed-NPC file into the supplied ParsedField: 100 records of 504 bytes, little-endian, no
// header (every file is 50400 bytes). A record is a placement snapshot of a whole character, and the original opens the
// NPC from the definition file it names, over the snapshot; the fields the importer reads are the position and those the
// definition file does not always set:
//
//   +4    int32     live when non-zero
//   +104  char[60]  CP949 path of the NPC definition file (the NPC is the file, matched by name)
//   +180  int32     shadow size level (1 in every record)
//   +392  uint16    class, the *계급 of a file that has none: a quest code from 1000 (0 none)
//   +476/+480/+484  int32 x, y, z in 1/256 world units; +492 int32 heading (4096 per turn)
public sealed class SpcReader
{
    private const int RecordSize = 504;
    private const int RecordCount = 100;
    private const int OffCode = 4;
    private const int OffSource = 104;
    private const int SourceLength = 60;
    private const int OffSizeLevel = 180;
    private const int OffClass = 392;
    private const int OffX = 476;
    private const int OffY = 480;
    private const int OffZ = 484;
    private const int OffHeading = 492;

    public void Read(string path, ParsedField field)
    {
        var bytes = File.ReadAllBytes(path);
        foreach (var (index, source) in Records(bytes))
        {
            var record = bytes.AsSpan(index * RecordSize, RecordSize);
            field.FixedNpcs.Add(new ParsedNpcPlacement(
                index,
                source,
                BinaryPrimitives.ReadInt32LittleEndian(record[OffX..]),
                BinaryPrimitives.ReadInt32LittleEndian(record[OffY..]),
                BinaryPrimitives.ReadInt32LittleEndian(record[OffZ..]),
                BinaryPrimitives.ReadInt32LittleEndian(record[OffHeading..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[OffClass..]),
                BinaryPrimitives.ReadInt32LittleEndian(record[OffSizeLevel..])));
        }
    }

    private static IEnumerable<(int Index, string Source)> Records(byte[] bytes)
    {
        var count = Math.Min(bytes.Length / RecordSize, RecordCount);
        for (var index = 0; index < count; index++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * RecordSize + OffCode, 4)) != 0)
            {
                yield return (index, SourceOf(bytes, index));
            }
        }
    }

    private static string SourceOf(byte[] bytes, int index) =>
        Path.GetFileName(Cp949.GetNullTerminated(bytes.AsSpan(index * RecordSize + OffSource, SourceLength)).Replace('\\', '/'));
}
