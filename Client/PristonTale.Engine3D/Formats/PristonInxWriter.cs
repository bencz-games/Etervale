using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

// Writes a model-info file (.inx) PristonInxReader reads back: the layout with short item codes (95268 bytes), names in
// CP949. Its keywords come from the hash of the path it is written to (char\...\name.inx, under the assets root): the
// model keyword, with the linked model's own when it borrows one, and, when it has character motions and no talk
// motions, every character motion's start and end frame split with it (a frame 0 stays plain).
public static class PristonInxWriter
{
    private const int Size = 95268;
    private const int NameSize = 64;
    private const int GroupNameSize = 16;
    private const int GroupNames = 4;
    private const int EventFrames = 4;

    // Compiles a descriptor (.ini, .inf) to the model-info file beside it; returns that file.
    public static string Compile(string descriptor)
    {
        // An .inx already there in another letter case is replaced, not doubled.
        var path = AssetFiles.Resolve(PristonInxReader.CompiledPath(AssetFiles.Resolve(descriptor)));
        File.WriteAllBytes(path, Write(PristonModelText.Read(descriptor), path));
        return path;
    }

    public static byte[] Write(PristonModelInfo info, string path)
    {
        var code = PristonInxReader.PathHash(PristonInxReader.ToCanonicalAssetPath(path));
        var protect = info.MotionCount > PristonFormatConstants.CharacterMotionBase && info.TalkMotionCount <= PristonFormatConstants.CharacterMotionBase;
        using var stream = new MemoryStream(Size);
        using var writer = new BinaryWriter(stream);
        Name(writer, info.ModelFile, NameSize);
        Name(writer, info.MotionFile, NameSize);
        Name(writer, info.SubModelFile, NameSize);
        foreach (var group in new[] { info.HighModel, info.DefaultModel, info.LowModel })
        {
            writer.Write(group.ModelNameCount);
            for (var name = 0; name < GroupNames; name++)
            {
                Name(writer, group.ModelNames.ElementAtOrDefault(name) ?? "", GroupNameSize);
            }
        }

        Table(writer, info.Motions, PristonFormatConstants.MotionInfoCount, slot =>
            protect && slot >= PristonFormatConstants.CharacterMotionBase && slot < info.MotionCount ? code : null);
        writer.Write(info.MotionCount);
        var (fileKeyword, linkKeyword) = ModelKeywords(code, info);
        writer.Write(fileKeyword);
        writer.Write(linkKeyword);
        Name(writer, info.LinkFile, NameSize);
        Name(writer, info.TalkLinkFile, NameSize);
        Name(writer, info.TalkMotionFile, NameSize);
        Table(writer, info.TalkMotions, PristonFormatConstants.TalkMotionInfoCount, static _ => null);
        writer.Write(info.TalkMotionCount);
        Ints(writer, info.NpcMotionRates, PristonFormatConstants.NpcMotionInfoCount);
        Ints(writer, info.NpcMotionSelection, PristonFormatConstants.MotionRateSelectionCount);
        Ints(writer, info.TalkMotionRates, PristonFormatConstants.TalkMotionInfoCount);
        for (var file = 0; file < PristonFormatConstants.TalkMotionFileCount; file++)
        {
            Ints(writer, info.TalkMotionSelection.ElementAtOrDefault(file) ?? [], PristonFormatConstants.MotionRateSelectionCount);
        }

        return stream.ToArray();
    }

    private static void Name(BinaryWriter writer, string name, int size)
    {
        var bytes = Cp949.Encoding.GetBytes(name ?? "");
        if (bytes.Length >= size)
        {
            throw new InvalidDataException($"'{name}' is longer than a model-info name of {size} bytes holds ({size - 1}).");
        }

        writer.Write(bytes);
        writer.Write(new byte[size - bytes.Length]);
    }

    private static void Ints(BinaryWriter writer, IReadOnlyList<int> values, int count)
    {
        for (var index = 0; index < count; index++)
        {
            writer.Write(index < values.Count ? values[index] : 0);
        }
    }

    private static void Table(BinaryWriter writer, IReadOnlyList<PristonMotionInfo> motions, int count, Func<int, uint?> protection)
    {
        if (motions.Count > count)
        {
            throw new InvalidDataException($"A model-info table holds {count} motions, not {motions.Count}.");
        }

        for (var slot = 0; slot < count; slot++)
        {
            Motion(writer, slot < motions.Count ? motions[slot] : null, protection(slot));
        }
    }

    private static void Motion(BinaryWriter writer, PristonMotionInfo motion, uint? code)
    {
        if (motion is null)
        {
            writer.Write(new byte[172]);
            return;
        }

        if (motion.EventFrames.Count > EventFrames || motion.ItemCodeCount is < 0 or > PristonFormatConstants.MotionItemCapacity
            || motion.SkillCodes.Count > PristonFormatConstants.MotionSkillCapacity)
        {
            throw new InvalidDataException("A motion has more events, items or skills than a model-info file holds.");
        }

        var (startKeyword, start) = code is { } startCode ? ProtectStart(motion.StartFrame, startCode) : (0u, motion.StartFrame);
        var (endKeyword, end) = code is { } endCode ? ProtectEnd(motion.EndFrame, endCode) : (0u, motion.EndFrame);
        writer.Write(motion.State);
        writer.Write(startKeyword);
        writer.Write(start);
        writer.Write(endKeyword);
        writer.Write(end);
        for (var index = 0; index < EventFrames; index++)
        {
            writer.Write(index < motion.EventFrames.Count ? motion.EventFrames[index] : 0u);
        }

        writer.Write(motion.ItemCodeCount);
        var items = motion.ItemCodeTable.Count == PristonFormatConstants.MotionItemCapacity ? motion.ItemCodeTable : motion.ItemIndices;
        for (var index = 0; index < PristonFormatConstants.MotionItemCapacity; index++)
        {
            writer.Write((short)(index < items.Count ? items[index] : 0));
        }

        writer.Write(motion.JobCodeBit);
        for (var index = 0; index < PristonFormatConstants.MotionSkillCapacity; index++)
        {
            writer.Write(index < motion.SkillCodes.Count ? motion.SkillCodes[index] : (byte)0);
        }

        writer.Write(motion.MapPosition);
        writer.Write(motion.Repeat);
        writer.Write((uint)motion.KeyCode);
        writer.Write(motion.MotionFrame);
    }

    // The model keyword holds the file's hash; with a linked model, its keyword pair holds both hashes.
    private static (uint File, uint Link) ModelKeywords(uint code, PristonModelInfo info)
    {
        if (string.IsNullOrEmpty(info.LinkFile))
        {
            return ((code & 0x0000FF00u) << 16 | (code & 0xFF000000u) >> 8 | (code & 0x000000FFu) << 8 | (code & 0x00FF0000u) >> 16,
                info.LinkFileKeyword);
        }

        var link = PristonInxReader.PathHash(PristonInxReader.CompiledPath(info.LinkFile));
        return ((code & 0x00FF0000u) << 8 | (link & 0x000000FFu) << 16 | (code & 0x000000FFu) << 8 | (link & 0xFF000000u) >> 24,
            (code & 0xFF000000u) | (link & 0x0000FF00u) << 8 | (code & 0x0000FF00u) | (link & 0x00FF0000u) >> 16);
    }

    // The inverse of PristonInxReader's start-frame decoding: the frame's bytes split between keyword and value, the
    // code's bytes filling the rest.
    private static (uint Keyword, uint Value) ProtectStart(uint frame, uint code) => frame == 0 ? (0u, 0u) :
        ((code & 0xFF000000u) | (frame & 0x0000FF00u) << 8 | (code & 0x00FF0000u) >> 8 | frame & 0x000000FFu,
         (code & 0x000000FFu) << 24 | frame & 0x00FF0000u | code & 0x0000FF00u | frame >> 24);

    private static (uint Keyword, uint Value) ProtectEnd(uint frame, uint code) => frame == 0 ? (0u, 0u) :
        ((code & 0x000000FFu) << 24 | (frame & 0xFF000000u) >> 8 | code & 0x0000FF00u | (frame & 0x00FF0000u) >> 16,
         (code & 0x00FF0000u) << 8 | (frame & 0x0000FF00u) << 8 | (code & 0xFF000000u) >> 16 | frame & 0x000000FFu);
}
