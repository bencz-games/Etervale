namespace PristonTale.Protocol.Binary;

// {uint16 Version, uint16 Flags = 0} at the head of a message body. A newer version only appends fields, so a reader
// accepts its minimum version and above; the flags are reserved.
public static class WireHeader
{
    public const int Size = 4;

    public static void Write(ref PacketWriter writer, ushort version)
    {
        writer.UInt16(version);
        writer.UInt16(0);
    }

    // The received version, for a reader that fills the fields a newer version appended.
    public static ushort Read(ref PacketReader reader, string name, ushort minimumVersion = 1)
    {
        var version = ReadVersion(ref reader, name, minimumVersion);
        reader.Skip(2);
        return version;
    }

    // The version word alone, for a body whose second word carries data instead of the flags.
    public static ushort ReadVersion(ref PacketReader reader, string name, ushort minimumVersion = 1)
    {
        var version = reader.UInt16();
        if (version < minimumVersion)
        {
            throw new InvalidDataException(minimumVersion == 1 ? $"{name} has version 0." : $"{name} has an older layout.");
        }

        return version;
    }
}
