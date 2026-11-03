using System.Text;
using PristonTale.Engine3D.IO;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

public sealed class PristonInxReader
{
    // INX variants differ only in the item code list width: modern uses short[52], original uses byte[52].
    // The remaining container layout is identical.
    private const int ModelInfoSizeShortCodes = 95268; // 172-byte motion record (item code list = short[52])
    private const int ModelInfoSizeByteCodes = 67084; // 120-byte motion record (item code list = byte[52])
    private const int ItemCodeListSizeShortCodes = 104;
    private const int ItemCodeListSizeByteCodes = 52;
    private const int ModelGroupSize = 68;
    private const int MotionInfoOffset = 396;
    private const int ModelInfoTailSize =
        (PristonFormatConstants.NpcMotionInfoCount
         + PristonFormatConstants.MotionRateSelectionCount
         + PristonFormatConstants.TalkMotionInfoCount
         + PristonFormatConstants.TalkMotionFileCount * PristonFormatConstants.MotionRateSelectionCount) * sizeof(int);

    private readonly PristonAssetCache _cache;

    public PristonInxReader()
    {
    }

    public PristonInxReader(PristonAssetCache cache)
    {
        _cache = cache;
    }

    public PristonModelInfo Read(string path)
    {
        if (_cache != null)
        {
            return _cache.GetOrAddModelInfo(path, Parse);
        }

        return Parse(path);
    }

    private static PristonModelInfo Parse(string path)
    {
        var data = AssetFiles.ReadAllBytes(path);
        var itemCodeListSize = data.Length switch
        {
            ModelInfoSizeShortCodes => ItemCodeListSizeShortCodes,
            ModelInfoSizeByteCodes => ItemCodeListSizeByteCodes,
            _ => throw new InvalidDataException(
                $"{path} is not a supported Priston INX. Size {data.Length}, expected {ModelInfoSizeShortCodes} or {ModelInfoSizeByteCodes}.")
        };

        var cursor = new BinaryCursor(data);
        var modelFile = cursor.FixedString(64);
        var motionFile = cursor.FixedString(64);
        var subModelFile = cursor.FixedString(64);
        var highModel = ReadModelGroup(cursor);
        var defaultModel = ReadModelGroup(cursor);
        var lowModel = ReadModelGroup(cursor);

        var fileCode = PathHash(ToCanonicalAssetPath(path));
        var motions = new List<PristonMotionInfo>(PristonFormatConstants.MotionInfoCount);
        cursor.Position = MotionInfoOffset;
        for (var i = 0; i < PristonFormatConstants.MotionInfoCount; i++)
        {
            motions.Add(ReadMotion(cursor, itemCodeListSize));
        }

        var motionCount = cursor.UInt32();
        var fileTypeKeyword = cursor.UInt32();
        var linkFileKeyword = cursor.UInt32();
        var linkFile = cursor.FixedString(64);
        var talkLinkFile = cursor.FixedString(64);
        var talkMotionFile = cursor.FixedString(64);

        var talkMotions = new List<PristonMotionInfo>(PristonFormatConstants.TalkMotionInfoCount);
        for (var i = 0; i < PristonFormatConstants.TalkMotionInfoCount; i++)
        {
            talkMotions.Add(ReadMotion(cursor, itemCodeListSize));
        }

        var talkMotionCount = cursor.UInt32();
        ValidateTableCount(motionCount, PristonFormatConstants.MotionInfoCount, "motion");
        ValidateTableCount(talkMotionCount, PristonFormatConstants.TalkMotionInfoCount, "talk motion");
        var motionFramesWereProtected = motionCount > PristonFormatConstants.CharacterMotionBase
            && talkMotionCount <= PristonFormatConstants.CharacterMotionBase;
        var motionKeywordCode = fileCode;
        if (motionFramesWereProtected)
        {
            motionKeywordCode = ResolveMotionKeywordCode(motions, motionCount, fileCode);
            DecodeProtectedMotionFrames(path, motions, motionCount, motionKeywordCode);
        }

        var tailStart = cursor.Position;
        var npcMotionRates = ReadInt32Table(cursor, PristonFormatConstants.NpcMotionInfoCount);
        var npcMotionSelection = ReadInt32Table(cursor, PristonFormatConstants.MotionRateSelectionCount);
        var talkMotionRates = ReadInt32Table(cursor, PristonFormatConstants.TalkMotionInfoCount);
        var talkMotionSelection = new IReadOnlyList<int>[PristonFormatConstants.TalkMotionFileCount];
        for (var file = 0; file < talkMotionSelection.Length; file++)
        {
            talkMotionSelection[file] = ReadInt32Table(cursor, PristonFormatConstants.MotionRateSelectionCount);
        }

        if (cursor.Position - tailStart != ModelInfoTailSize || cursor.Position != cursor.Length)
        {
            throw new InvalidDataException(
                $"{path} has an invalid Win32 model info tail: consumed {cursor.Position - tailStart} of {ModelInfoTailSize} bytes; "
                + $"file position {cursor.Position} of {cursor.Length}.");
        }

        ValidateMotionSelections(path, npcMotionRates, npcMotionSelection, motionCount, "NPC");
        ValidateTalkSelections(path, talkMotionRates, talkMotionSelection, talkMotionCount);
        var keywordValidation = DecodeModelKeywords(fileCode, fileTypeKeyword, linkFileKeyword, linkFile);
        var resolvedMotionFile = motionFile;
        if (string.IsNullOrWhiteSpace(resolvedMotionFile))
        {
            resolvedMotionFile = FindEmbeddedMotionPath(data);
        }

        return new PristonModelInfo(
            path,
            modelFile,
            resolvedMotionFile,
            subModelFile,
            highModel,
            defaultModel,
            lowModel,
            motions,
            motionCount,
            linkFile,
            talkLinkFile,
            talkMotionFile,
            talkMotions,
            talkMotionCount)
        {
            FileTypeKeyword = fileTypeKeyword,
            LinkFileKeyword = linkFileKeyword,
            DecodedFileKeyword = keywordValidation.FileCode,
            DecodedLinkKeyword = keywordValidation.LinkCode,
            ModelKeywordsValid = keywordValidation.IsValid,
            MotionFramesWereProtected = motionFramesWereProtected,
            MotionKeywordsFromOtherPath = motionKeywordCode != fileCode,
            NpcMotionRates = npcMotionRates,
            NpcMotionSelection = npcMotionSelection,
            TalkMotionRates = talkMotionRates,
            TalkMotionSelection = talkMotionSelection
        };
    }

    private static int[] ReadInt32Table(BinaryCursor cursor, int count)
    {
        var values = new int[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = cursor.Int32();
        }

        return values;
    }

    private static void ValidateTableCount(uint count, int capacity, string name)
    {
        if (count > capacity)
        {
            throw new InvalidDataException($"INX {name} count {count} exceeds its Win32 table capacity {capacity}.");
        }
    }

    private static void ValidateMotionSelections(
        string path,
        IReadOnlyList<int> rates,
        IReadOnlyList<int> selection,
        uint motionCount,
        string tableName)
    {
        for (var index = 0; index < rates.Count; index++)
        {
            if (rates[index] is < 0 or > PristonFormatConstants.MotionRateSelectionCount)
            {
                throw new InvalidDataException($"{path} has invalid {tableName} motion rate {rates[index]} at index {index}.");
            }
        }

        if (rates.Count <= PristonFormatConstants.CharacterMotionBase || rates[PristonFormatConstants.CharacterMotionBase] == 0)
        {
            return;
        }

        for (var index = 0; index < selection.Count; index++)
        {
            if (selection[index] < PristonFormatConstants.CharacterMotionBase || (uint)selection[index] >= motionCount)
            {
                throw new InvalidDataException($"{path} has invalid {tableName} motion selection {selection[index]} at slot {index}.");
            }
        }
    }

    private static void ValidateTalkSelections(
        string path,
        IReadOnlyList<int> rates,
        IReadOnlyList<IReadOnlyList<int>> selections,
        uint motionCount)
    {
        ValidateMotionSelections(path, rates, [], motionCount, "talk");
        if (rates.Count <= PristonFormatConstants.CharacterMotionBase || rates[PristonFormatConstants.CharacterMotionBase] == 0)
        {
            return;
        }

        foreach (var selection in selections)
        {
            for (var index = 0; index < selection.Count; index++)
            {
                if (selection[index] < PristonFormatConstants.CharacterMotionBase || (uint)selection[index] >= motionCount)
                {
                    throw new InvalidDataException($"{path} has invalid talk motion selection {selection[index]} at slot {index}.");
                }
            }
        }
    }

    private static string FindEmbeddedMotionPath(byte[] data)
    {
        for (var i = 0; i < data.Length - 8; i++)
        {
            if (!IsEmbeddedAssetPathStart(data, i))
            {
                continue;
            }

            var end = i;
            while (end < data.Length && data[end] >= 32 && data[end] <= 126)
            {
                end++;
            }

            var candidate = Encoding.ASCII.GetString(data, i, end - i);
            if (candidate.EndsWith(".in", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static bool IsEmbeddedAssetPathStart(byte[] data, int index)
    {
        return
            data[index] == (byte)'c' &&
            data[index + 1] == (byte)'h' &&
            data[index + 2] == (byte)'a' &&
            data[index + 3] == (byte)'r' &&
            (data[index + 4] == (byte)'\\' || data[index + 4] == (byte)'/');
    }

    private static PristonModelGroup ReadModelGroup(BinaryCursor cursor)
    {
        var count = cursor.Int32();
        if (count is < 0 or > 4)
        {
            throw new InvalidDataException($"INX model group count {count} exceeds its Win32 capacity 4.");
        }

        var names = new List<string>(4);
        for (var i = 0; i < 4; i++)
        {
            names.Add(cursor.FixedString(16));
        }

        return new PristonModelGroup(count, names);
    }

    private static PristonMotionInfo ReadMotion(BinaryCursor cursor, int itemCodeListSize)
    {
        var state = cursor.UInt32();
        var motionKeyWord1 = cursor.UInt32();
        var startFrame = cursor.UInt32();
        var motionKeyWord2 = cursor.UInt32();
        var endFrame = cursor.UInt32();
        var rawStartFrame = startFrame;
        var rawEndFrame = endFrame;
        var eventFrames = new List<uint>(4);
        for (var i = 0; i < 4; i++)
        {
            eventFrames.Add(cursor.UInt32());
        }

        var itemCodeCount = cursor.Int32();
        if (itemCodeCount is < 0 or > PristonFormatConstants.MotionItemCapacity)
        {
            throw new InvalidDataException("INX motion item count exceeds its fixed table.");
        }

        var itemCodeTable = new int[PristonFormatConstants.MotionItemCapacity];
        for (var item = 0; item < PristonFormatConstants.MotionItemCapacity; item++)
        {
            itemCodeTable[item] = itemCodeListSize == ItemCodeListSizeShortCodes
                ? cursor.Int16()
                : cursor.Byte();
        }

        var itemIndices = itemCodeTable.AsSpan(0, itemCodeCount).ToArray();

        var jobCodeBit = cursor.UInt32();
        var skillCodes = cursor.ReadSpan(PristonFormatConstants.MotionSkillCapacity).ToArray();
        var mapPosition = cursor.Int32();
        var repeat = cursor.UInt32();
        var keyCode = cursor.Byte();
        var keyPadding0 = cursor.Byte();
        var keyPadding1 = cursor.Byte();
        var keyPadding2 = cursor.Byte();
        if (keyPadding0 != 0 || keyPadding1 != 0 || keyPadding2 != 0)
        {
            throw new InvalidDataException("INX motion key padding contains unsupported nonzero data.");
        }

        var motionFrame = cursor.Int32();

        return new PristonMotionInfo(
            state,
            startFrame,
            endFrame,
            eventFrames,
            itemCodeCount,
            jobCodeBit,
            mapPosition,
            repeat,
            keyCode,
            motionFrame)
        {
            ItemIndices = itemIndices,
            ItemCodeTable = itemCodeTable,
            SkillCodes = skillCodes,
            MotionKeyword1 = motionKeyWord1,
            MotionKeyword2 = motionKeyWord2,
            RawStartFrame = rawStartFrame,
            RawEndFrame = rawEndFrame,
            StartFrameDecoded = false,
            EndFrameDecoded = false
        };
    }

    // Motion keywords embed the path hash (PathHash) of the path the INX was encoded under. Some shipped INX
    // files were copied to another folder, so a strict per-path check rejects them; when every protected frame
    // carries one identical keyword the frames are intact and that keyword is accepted instead.
    private static uint ResolveMotionKeywordCode(List<PristonMotionInfo> motions, uint motionCount, uint fileCode)
    {
        uint? sharedKeyword = null;
        var matchesFile = true;
        for (var index = PristonFormatConstants.CharacterMotionBase; index < motionCount; index++)
        {
            var motion = motions[index];
            var keywords = new List<uint>(2);
            if (motion.MotionKeyword1 != 0 || motion.RawStartFrame != 0)
            {
                keywords.Add(StartFrameKeyword(motion.MotionKeyword1, motion.RawStartFrame));
            }

            if (motion.MotionKeyword2 != 0 || motion.RawEndFrame != 0)
            {
                keywords.Add(EndFrameKeyword(motion.MotionKeyword2, motion.RawEndFrame));
            }

            foreach (var keyword in keywords)
            {
                matchesFile &= keyword == fileCode;
                if (sharedKeyword is null)
                {
                    sharedKeyword = keyword;
                }
                else if (sharedKeyword != keyword)
                {
                    return fileCode;
                }
            }
        }

        return matchesFile || sharedKeyword is null ? fileCode : sharedKeyword.Value;
    }

    private static void DecodeProtectedMotionFrames(string path, List<PristonMotionInfo> motions, uint motionCount, uint fileCode)
    {
        for (var index = PristonFormatConstants.CharacterMotionBase; index < motionCount; index++)
        {
            var motion = motions[index];
            var startFrame = motion.RawStartFrame;
            var endFrame = motion.RawEndFrame;
            var startFrameDecoded = TryDecodeStartFrame(fileCode, motion.MotionKeyword1, motion.RawStartFrame, out var decodedStartFrame);
            var endFrameDecoded = TryDecodeEndFrame(fileCode, motion.MotionKeyword2, motion.RawEndFrame, out var decodedEndFrame);
            if (motion.MotionKeyword1 != 0 || motion.RawStartFrame != 0)
            {
                if (!startFrameDecoded)
                {
                    throw new InvalidDataException($"{path} motion {index} has an invalid Win32 start-frame keyword.");
                }

                startFrame = decodedStartFrame;
            }

            if (motion.MotionKeyword2 != 0 || motion.RawEndFrame != 0)
            {
                if (!endFrameDecoded)
                {
                    throw new InvalidDataException($"{path} motion {index} has an invalid Win32 end-frame keyword.");
                }

                endFrame = decodedEndFrame;
            }

            motions[index] = motion with
            {
                StartFrame = startFrame,
                EndFrame = endFrame,
                StartFrameDecoded = startFrameDecoded,
                EndFrameDecoded = endFrameDecoded
            };
        }
    }

    private static ModelKeywordValidation DecodeModelKeywords(uint fileCode, uint fileKeyword, uint linkKeyword, string linkFile)
    {
        if (string.IsNullOrWhiteSpace(linkFile))
        {
            var decodedFileCode =
                ((fileKeyword & 0xFF000000u) >> 16) |
                ((fileKeyword & 0x00FF0000u) << 8) |
                ((fileKeyword & 0x0000FF00u) >> 8) |
                ((fileKeyword & 0x000000FFu) << 16);
            return new ModelKeywordValidation(decodedFileCode, 0, decodedFileCode == fileCode);
        }

        var decodedFileCodeWithLink =
            (linkKeyword & 0xFF000000u) |
            ((fileKeyword & 0xFF000000u) >> 8) |
            (linkKeyword & 0x0000FF00u) |
            ((fileKeyword & 0x0000FF00u) >> 8);
        var decodedLinkCode =
            ((fileKeyword & 0x000000FFu) << 24) |
            ((linkKeyword & 0x000000FFu) << 16) |
            ((linkKeyword & 0x00FF0000u) >> 8) |
            ((fileKeyword & 0x00FF0000u) >> 16);
        var canonicalLinkFile = Path.ChangeExtension(linkFile, ".inx") ?? linkFile;
        var expectedLinkCode = PathHash(canonicalLinkFile);
        return new ModelKeywordValidation(
            decodedFileCodeWithLink,
            decodedLinkCode,
            decodedFileCodeWithLink == fileCode && decodedLinkCode == expectedLinkCode);
    }

    private static uint StartFrameKeyword(uint keyword, uint encodedFrame)
    {
        return (keyword & 0xFF000000u) |
            ((keyword & 0x0000FF00u) << 8) |
            (encodedFrame & 0x0000FF00u) |
            ((encodedFrame & 0xFF000000u) >> 24);
    }

    private static uint EndFrameKeyword(uint keyword, uint encodedFrame)
    {
        return ((encodedFrame & 0x0000FF00u) << 16) |
            ((encodedFrame & 0xFF000000u) >> 8) |
            (keyword & 0x0000FF00u) |
            ((keyword & 0xFF000000u) >> 24);
    }

    private static bool TryDecodeStartFrame(uint code, uint keyword, uint encodedFrame, out uint frame)
    {
        var decodedKeyword = StartFrameKeyword(keyword, encodedFrame);

        frame =
            ((encodedFrame & 0x000000FFu) << 24) |
            (encodedFrame & 0x00FF0000u) |
            ((keyword & 0x00FF0000u) >> 8) |
            (keyword & 0x000000FFu);

        return (keyword != 0 || encodedFrame != 0) && decodedKeyword == code;
    }

    private static bool TryDecodeEndFrame(uint code, uint keyword, uint encodedFrame, out uint frame)
    {
        var decodedKeyword = EndFrameKeyword(keyword, encodedFrame);

        frame =
            ((keyword & 0x00FF0000u) << 8) |
            ((keyword & 0x000000FFu) << 16) |
            ((encodedFrame & 0x00FF0000u) >> 8) |
            (encodedFrame & 0x000000FFu);

        return (keyword != 0 || encodedFrame != 0) && decodedKeyword == code;
    }

    public static uint PathHash(string name)
    {
        uint sum2 = 0;
        uint sum = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var ch = (byte)name[i];
            if (ch == 0)
            {
                break;
            }

            if (ch >= 'a' && ch <= 'z')
            {
                ch = (byte)(ch - 0x20);
            }

            var position = unchecked((uint)i);
            sum2 = unchecked(sum2 + (uint)ch * (position + 1u));
            sum = unchecked(sum + (uint)ch * position * position);
        }

        return unchecked((sum << 24) | ((uint)name.Length << 16) | sum2);
    }

    public static string ToCanonicalAssetPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        var marker = "\\assets\\";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return normalized[(index + marker.Length)..];
        }

        if (normalized.StartsWith("assets\\", StringComparison.OrdinalIgnoreCase))
        {
            return normalized[7..];
        }

        return normalized;
    }

    private readonly record struct ModelKeywordValidation(uint FileCode, uint LinkCode, bool IsValid);
}
