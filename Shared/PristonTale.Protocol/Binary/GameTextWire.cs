using System.Collections.Immutable;
using PristonTale.Contracts.Texts;

namespace PristonTale.Protocol.Binary;

// A GameText on the wire: {uint16 Id, uint16 Count, Count texts (64 B each)}; at most MaxArgs texts.
public static class GameTextWire
{
    public const int MaxArgs = 4;
    public const int MaxArgBytes = 64;
    public const int MinimumSize = 4;
    public const int MaximumSize = MinimumSize + MaxArgs * (2 + MaxArgBytes);

    public static void Write(ref PacketWriter writer, GameText text)
    {
        var args = text.Args;
        var count = Math.Min(args.Length, MaxArgs);
        writer.UInt16((ushort)text.Id);
        writer.UInt16((ushort)count);
        for (var index = 0; index < count; index++)
        {
            Utf8Text.Write(ref writer, args[index], MaxArgBytes);
        }
    }

    public static int Size(GameText text)
    {
        var size = MinimumSize;
        var args = text.Args;
        for (var index = 0; index < Math.Min(args.Length, MaxArgs); index++)
        {
            size += Utf8Text.Size(args[index], MaxArgBytes);
        }

        return size;
    }

    public static GameText Read(ref PacketReader reader)
    {
        var id = (GameTextId)reader.UInt16();
        var count = reader.UInt16();
        if (count > MaxArgs)
        {
            throw new InvalidDataException("Game text has more arguments than its field allows.");
        }

        if (count == 0)
        {
            return GameText.Of(id);
        }

        var args = ImmutableArray.CreateBuilder<string>(count);
        for (var index = 0; index < count; index++)
        {
            args.Add(Utf8Text.Read(ref reader, MaxArgBytes, "Game text"));
        }

        return new GameText(id, args.MoveToImmutable());
    }
}
