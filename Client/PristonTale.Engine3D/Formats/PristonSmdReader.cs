using System.Buffers.Binary;
using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.IO;
using PristonTale.Contracts.Units;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

public sealed class PristonSmdReader
{
    private const int FileHeaderSize = SmdLayout.FileHeaderSize;
    private const int ObjectInfoSize = 40;
    private const int StageLightSize = 28;
    private const int TexLinkSize = 32;
    private const int Obj3DSize = 2236;
    private const int ModelVertexSize = 24;
    private const int ModelFaceSize = 36;
    private const int RotationKeySize = 20;
    private const int PositionKeySize = 16;
    private const int ScaleKeySize = 16;
    private const int MatrixSize = 64;
    private const int PhysiqueNameSize = 32;
    private const int MotionSegmentTableEntries = 32;
    private const uint ObjHeadMagic = 0x41424344;
    private const uint ObjHeadTypeNewNormal = 0x80000000;

    private static ReadOnlySpan<byte> BinaryModelHeader =>
    [
        0xA5, 0x82, 0xAC, 0x58, 0x15, 0x98, 0x29, 0x8C,
        0x85, 0x9F, 0x12, 0xC8, 0x84, 0x1F, 0x74, 0x8C,
        0x8F, 0x42, 0x8F, 0xA8, 0x1B, 0x84, 0xAA, 0x00
    ];

    private readonly PristonAssetCache _cache;

    public PristonSmdReader()
    {
    }

    public PristonSmdReader(PristonAssetCache cache)
    {
        _cache = cache;
    }

    public PristonStage ReadStage(string path)
    {
        if (_cache != null)
        {
            return _cache.GetOrAddStage(path, ParseStage);
        }

        return ParseStage(path);
    }

    public PristonModel ReadModel(string path)
    {
        if (_cache != null)
        {
            return _cache.GetOrAddModel(path, ParseModel);
        }

        return ParseModel(path);
    }

    private static PristonStage ParseStage(string path)
    {
        var data = AssetFiles.ReadAllBytes(path);
        var cursor = new BinaryCursor(data);
        var header = ReadHeader(cursor);
        if (!IsStageHeader(header.Kind))
        {
            throw new InvalidDataException($"{path} is not a stage SMD. Header kind: {header.Kind}.");
        }

        if (LooksLikeModelLayout(data, header, requireObjectHead: true))
        {
            throw new InvalidDataException($"{path} carries a stage signature but its offsets and object records form a model SMD. Load it through ReadModel.");
        }

        cursor.Position = FileHeaderSize;
        var stage = ReadStageHeader(cursor);
        AssertRecordSize(cursor, FileHeaderSize, SmdLayout.StageHeaderSize, "stage header record");

        var materialGroup = header.MaterialCount > 0
            ? ReadMaterialGroup(cursor, header.MaterialCount)
            : MaterialGroupReadResult.Empty;

        var vertices = new List<PristonStageVertex>(stage.VertexCount);
        for (var index = 0; index < stage.VertexCount; index++)
        {
            vertices.Add(ReadStageVertex(cursor));
        }

        var faces = new List<PristonStageFace>(stage.FaceCount);
        for (var index = 0; index < stage.FaceCount; index++)
        {
            faces.Add(ReadStageFace(cursor, stage.TextureLinkPointer32));
        }

        var textureLinks = new List<PristonTexLink>(stage.TextureLinkCount);
        for (var index = 0; index < stage.TextureLinkCount; index++)
        {
            textureLinks.Add(ReadTexLink(cursor, stage.TextureLinkPointer32, stage.TextureLinkCount));
        }

        var lights = new List<PristonStageLight>(stage.LightCount);
        for (var index = 0; index < stage.LightCount; index++)
        {
            lights.Add(ReadStageLight(cursor));
        }

        var areaPayload = ReadStageAreaIndex(data, cursor.Position, stage.OccupiedAreaCells, stage.FaceCount, path);
        return new PristonStage(
            path,
            materialGroup.Materials,
            vertices,
            faces,
            textureLinks,
            lights,
            PristonBinaryUtil.BoundsFrom(vertices.Select(static vertex => vertex.Position)))
        {
            Header = ToPublicHeader(header),
            MaterialGroup = materialGroup.Metadata,
            Layout = stage.Metadata,
            AreaIndex = areaPayload.Index,
            Contrast = stage.Contrast,
            Brightness = stage.Brightness,
            AuthoredLightDirection = stage.AuthoredLightDirection,
            AuthoredMapRectangle = stage.AuthoredMapRectangle,
            DeclaredAreaElementCount = stage.DeclaredAreaElementCount,
            SourceTrailer = ReadSourceTrailer(data, areaPayload.EndOffset)
        };
    }

    private static PristonModel ParseModel(string path)
    {
        var data = AssetFiles.ReadAllBytes(path);
        var cursor = new BinaryCursor(data);
        var header = ReadHeader(cursor);
        var stageSignatureWithModelLayout = IsStageHeader(header.Kind) && header.ObjectCount > 0 && LooksLikeModelLayout(data, header, requireObjectHead: true);
        if (!IsModelHeader(header.Kind) && !stageSignatureWithModelLayout)
        {
            throw new InvalidDataException($"{path} is not a model SMD/SMB. Header kind: {header.Kind}.");
        }

        ValidateModelHeader(data, header, path);
        var infos = new List<ObjectInfo>(header.ObjectCount);
        cursor.Position = FileHeaderSize;
        for (var index = 0; index < header.ObjectCount; index++)
        {
            infos.Add(ReadObjectInfo(cursor));
        }

        if (cursor.Position != header.MaterialOffset)
        {
            throw new InvalidDataException($"{path} object-info table ends at {cursor.Position}, but MatFilePoint is {header.MaterialOffset}.");
        }

        var materialGroup = header.MaterialCount > 0
            ? ReadMaterialGroup(cursor, header.MaterialCount)
            : MaterialGroupReadResult.Empty;
        if (header.MaterialCount > 0 && cursor.Position != header.FirstObjectOffset)
        {
            throw new InvalidDataException($"{path} material block ends at {cursor.Position}, but First_ObjInfoPoint is {header.FirstObjectOffset}.");
        }

        var objects = new List<PristonModelObject>(header.ObjectCount);
        foreach (var info in infos)
        {
            cursor.Position = info.Offset;
            objects.Add(ReadObject(cursor, info, path));
        }

        var lastObjectEnd = infos.Count == 0
            ? cursor.Position
            : infos.Max(static info => checked(info.Offset + info.Length));
        if (lastObjectEnd > data.Length)
        {
            throw new InvalidDataException($"{path} model object blocks end at {lastObjectEnd}, beyond the file length {data.Length}.");
        }

        var bounds = PristonBinaryUtil.BoundsFrom(
            objects.SelectMany(static obj => obj.Vertices.Select(vertex => Vector3.Transform(vertex.Position, obj.Transform))));
        var publicHeader = ToPublicHeader(
            header,
            stageSignatureWithModelLayout ? PristonSmdHeaderKind.StageHeaderWithModelLayout : header.Kind);
        return new PristonModel(path, materialGroup.Materials, objects, EffectiveFrameRanges(header), bounds)
        {
            Header = publicHeader,
            MaterialGroup = materialGroup.Metadata,
            SourceTrailer = ReadSourceTrailer(data, lastObjectEnd)
        };
    }

    private static SmdFileHeader ReadHeader(BinaryCursor cursor)
    {
        var headerBytes = cursor.ReadSpan(24);
        var kind = ClassifyHeader(headerBytes);
        var objectCount = cursor.Int32();
        var materialCount = cursor.Int32();
        var materialOffset = cursor.Int32();
        var firstObjectOffset = cursor.Int32();
        var frameRangeCount = cursor.Int32();
        var frameRanges = new PristonFrameRange[MotionSegmentTableEntries];
        for (var index = 0; index < frameRanges.Length; index++)
        {
            frameRanges[index] = new PristonFrameRange(cursor.Int32(), cursor.Int32(), cursor.Int32(), cursor.Int32());
        }

        AssertRecordSize(cursor, 0, FileHeaderSize, "file header record");
        return new SmdFileHeader(kind, objectCount, materialCount, materialOffset, firstObjectOffset, frameRangeCount, frameRanges);
    }

    private static PristonSmdHeaderKind ClassifyHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.SequenceEqual(BinaryModelHeader))
        {
            return PristonSmdHeaderKind.BinaryModel;
        }

        var headerName = DecodeFixedString(headerBytes);
        if (string.Equals(headerName, SmdLayout.ModelSignature, StringComparison.Ordinal))
        {
            return PristonSmdHeaderKind.Version62Model;
        }

        if (string.Equals(headerName, SmdLayout.StageSignature, StringComparison.Ordinal))
        {
            return PristonSmdHeaderKind.StageVersion72;
        }

        if (string.Equals(headerName, SmdLayout.StageSignature73, StringComparison.Ordinal))
        {
            return PristonSmdHeaderKind.StageVersion73;
        }

        throw new InvalidDataException($"Unknown Priston SMD header: {Convert.ToHexString(headerBytes)}.");
    }

    private static string DecodeFixedString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end < 0)
        {
            end = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes[..end]);
    }

    private static bool IsModelHeader(PristonSmdHeaderKind kind)
    {
        return kind is PristonSmdHeaderKind.Version62Model or PristonSmdHeaderKind.BinaryModel;
    }

    private static bool IsStageHeader(PristonSmdHeaderKind kind)
    {
        return kind is PristonSmdHeaderKind.StageVersion72 or PristonSmdHeaderKind.StageVersion73;
    }

    private static PristonSmdHeaderMetadata ToPublicHeader(SmdFileHeader header, PristonSmdHeaderKind? kind = null)
    {
        return new PristonSmdHeaderMetadata(
            kind ?? header.Kind,
            header.ObjectCount,
            header.MaterialCount,
            header.MaterialOffset,
            header.FirstObjectOffset,
            header.FrameRangeCount);
    }

    private static void ValidateModelHeader(byte[] data, SmdFileHeader header, string path)
    {
        ValidateCount(header.ObjectCount, $"{path} model objects");
        ValidateCount(header.MaterialCount, $"{path} model materials");
        if (header.FrameRangeCount is < 0 or > MotionSegmentTableEntries)
        {
            throw new InvalidDataException($"{path} has invalid TmFrameCounter {header.FrameRangeCount}.");
        }

        // The original model loader never checks the object record's Head field; some costume exporters overwrote it with
        // material string bytes, so a model signature only needs a consistent object range.
        if (!LooksLikeModelLayout(data, header, requireObjectHead: !IsModelHeader(header.Kind)))
        {
            throw new InvalidDataException($"{path} does not have a complete Win32 model layout with valid object offsets and lengths.");
        }
    }

    private static bool LooksLikeModelLayout(byte[] data, SmdFileHeader header, bool requireObjectHead)
    {
        try
        {
            if (header.ObjectCount < 0 || header.ObjectCount > 100_000 || header.MaterialCount < 0 || header.MaterialCount > 10_000)
            {
                return false;
            }

            var infoTableEnd = checked(FileHeaderSize + header.ObjectCount * ObjectInfoSize);
            if (header.MaterialOffset != infoTableEnd || header.FirstObjectOffset < header.MaterialOffset || header.FirstObjectOffset > data.Length)
            {
                return false;
            }

            if (header.ObjectCount == 0)
            {
                return header.MaterialCount == 0 && header.MaterialOffset == data.Length && header.FirstObjectOffset == data.Length;
            }

            var cursor = new BinaryCursor(data)
            {
                Position = FileHeaderSize
            };
            var previousEnd = header.FirstObjectOffset;
            for (var index = 0; index < header.ObjectCount; index++)
            {
                var info = ReadObjectInfo(cursor);
                var objectEnd = checked(info.Offset + info.Length);
                if (info.Length < Obj3DSize || info.Offset != previousEnd || objectEnd > data.Length)
                {
                    return false;
                }

                var head = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(info.Offset, sizeof(uint)));
                if (requireObjectHead && (head & ~ObjHeadTypeNewNormal) != ObjHeadMagic)
                {
                    return false;
                }

                previousEnd = objectEnd;
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static IReadOnlyList<PristonFrameRange> EffectiveFrameRanges(SmdFileHeader header)
    {
        if (header.FrameRangeCount == 0)
        {
            return [];
        }

        var ranges = new PristonFrameRange[header.FrameRangeCount];
        Array.Copy(header.FrameRanges, ranges, ranges.Length);
        return ranges;
    }

    private static ObjectInfo ReadObjectInfo(BinaryCursor cursor)
    {
        var start = cursor.Position;
        var info = new ObjectInfo(cursor.FixedString(32), cursor.Int32(), cursor.Int32());
        AssertRecordSize(cursor, start, ObjectInfoSize, "object info record");
        return info;
    }

    private static StageInfo ReadStageHeader(BinaryCursor cursor)
    {
        var start = cursor.Position;
        var head = cursor.UInt32();
        var occupiedAreaCells = new bool[PristonStageAreaIndex.CellCount];
        for (var index = 0; index < occupiedAreaCells.Length; index++)
        {
            occupiedAreaCells[index] = cursor.UInt32() != 0;
        }

        var areaListPointer32 = cursor.UInt32();
        var areaListCount = cursor.Int32();
        var memoryMode = cursor.Int32();
        var sumCount = cursor.UInt32();
        var calculationSumCount = cursor.Int32();
        var vertexPointer32 = cursor.UInt32();
        var facePointer32 = cursor.UInt32();
        var textureLinkPointer32 = cursor.UInt32();
        var lightPointer32 = cursor.UInt32();
        var materialGroupPointer32 = cursor.UInt32();
        var stageObjectPointer32 = cursor.UInt32();
        var materialPointer32 = cursor.UInt32();
        var vertexCount = cursor.Int32();
        var faceCount = cursor.Int32();
        var textureLinkCount = cursor.Int32();
        var lightCount = cursor.Int32();
        var vertexColorCount = cursor.Int32();
        var contrast = cursor.Int32();
        var brightness = cursor.Int32();
        var lightX = cursor.Int32() / GameUnits.FixedScale;
        var lightY = cursor.Int32() / GameUnits.FixedScale;
        var lightZ = cursor.Int32() / GameUnits.FixedScale;
        var areaBufferPointer32 = cursor.UInt32();
        var declaredAreaElementCount = cursor.Int32();
        var mapLeft = cursor.Int32();
        var mapTop = cursor.Int32();
        var mapRight = cursor.Int32();
        var mapBottom = cursor.Int32();
        AssertRecordSize(cursor, start, SmdLayout.StageHeaderSize, "stage header record");

        ValidateCount(vertexCount, "stage vertices");
        ValidateCount(faceCount, "stage faces");
        ValidateCount(textureLinkCount, "stage texture links");
        ValidateCount(lightCount, "stage lights");
        ValidateCount(vertexColorCount, "stage vertex colors");

        var metadata = new PristonStageLayoutMetadata(
            head,
            areaListPointer32,
            areaListCount,
            memoryMode,
            sumCount,
            calculationSumCount,
            vertexPointer32,
            facePointer32,
            textureLinkPointer32,
            lightPointer32,
            materialGroupPointer32,
            stageObjectPointer32,
            materialPointer32,
            vertexColorCount,
            areaBufferPointer32);
        return new StageInfo(
            vertexCount,
            faceCount,
            textureLinkCount,
            lightCount,
            textureLinkPointer32,
            contrast,
            brightness,
            ToMonoGameStagePosition(lightX, lightY, lightZ),
            declaredAreaElementCount,
            new PristonIntRectangle(mapLeft, mapTop, mapRight, mapBottom),
            occupiedAreaCells,
            metadata);
    }

    private static MaterialGroupReadResult ReadMaterialGroup(BinaryCursor cursor, int headerMaterialCount)
    {
        var start = cursor.Position;
        var head = cursor.UInt32();
        var materialPointer32 = cursor.UInt32();
        var materialCount = cursor.UInt32();
        var reformTexture = cursor.Int32();
        var maximumMaterialCount = cursor.Int32();
        var lastSearchMaterial = cursor.Int32();
        var lastSearchName = cursor.FixedString(64);
        AssertRecordSize(cursor, start, SmdLayout.MaterialGroupSize, "material group record");

        if (materialCount > 10_000)
        {
            throw new InvalidDataException($"Invalid Priston material count {materialCount} at {start}.");
        }

        var materials = new List<PristonMaterial>((int)materialCount);
        for (var index = 0; index < materialCount; index++)
        {
            materials.Add(ReadMaterial(cursor));
        }

        if (headerMaterialCount != materialCount)
        {
            throw new InvalidDataException($"SMD header declares {headerMaterialCount} materials, but its material group record contains {materialCount}.");
        }

        var metadata = new PristonMaterialGroupMetadata(
            head,
            materialPointer32,
            materialCount,
            reformTexture,
            maximumMaterialCount,
            lastSearchMaterial,
            lastSearchName);
        return new MaterialGroupReadResult(materials, metadata);
    }

    private static PristonMaterial ReadMaterial(BinaryCursor cursor)
    {
        var start = cursor.Position;
        var inUseCount = cursor.UInt32();
        var textureCounter = cursor.UInt32();
        var texturePointers32 = ReadUInt32Array(cursor, 8);
        var textureStageStates = ReadUInt32Array(cursor, 8);
        var textureFormStates = ReadUInt32Array(cursor, 8);
        var reformTexture = cursor.Int32();
        var mapOpacity = cursor.Int32();
        var textureType = cursor.UInt32();
        var blendType = cursor.UInt32();
        var shade = cursor.UInt32();
        var twoSided = cursor.UInt32() != 0;
        var serialNumber = cursor.UInt32();
        var diffuse = new Vector3(cursor.Single(), cursor.Single(), cursor.Single());
        var transparency = cursor.Single();
        var selfIllum = cursor.Single();
        var textureSwap = cursor.Int32();
        var materialFrame = cursor.Int32();
        var textureClip = cursor.Int32();
        var useState = cursor.Int32();
        var meshState = cursor.Int32();
        var windMeshBottom = cursor.Int32();
        var animatedTexturePointers32 = ReadUInt32Array(cursor, 32);
        var animationTextureCounter = cursor.UInt32();
        var frameMask = cursor.UInt32();
        var shiftFrameSpeed = cursor.UInt32();
        var animationFrame = cursor.UInt32();
        AssertRecordSize(cursor, start, SmdLayout.MaterialSize, "material record");

        if (textureCounter > texturePointers32.Length)
        {
            throw new InvalidDataException($"Material at {start} declares {textureCounter} texture slots; capacity is {texturePointers32.Length}.");
        }

        if (animationTextureCounter > animatedTexturePointers32.Length)
        {
            throw new InvalidDataException($"Material at {start} declares {animationTextureCounter} animation texture slots; capacity is {animatedTexturePointers32.Length}.");
        }

        var textureNames = new List<string>((int)textureCounter);
        var opacityNames = new List<string>((int)textureCounter);
        var animationNames = new List<string>((int)animationTextureCounter);
        var animationOpacityNames = new List<string>((int)animationTextureCounter);
        byte[] stringTableTrailer = [];
        var hasTruncatedStringTable = false;
        if (inUseCount != 0)
        {
            var stringTableLength = cursor.Int32();
            if (stringTableLength < 0 || stringTableLength > cursor.Length - cursor.Position)
            {
                throw new InvalidDataException($"Invalid material string table length {stringTableLength} at {cursor.Position - sizeof(int)}.");
            }

            var stringTableEnd = cursor.Position + stringTableLength;
            for (var index = 0; index < textureCounter; index++)
            {
                var textureName = ReadNullString(cursor, stringTableEnd);
                var opacityName = ReadNullString(cursor, stringTableEnd);
                hasTruncatedStringTable |= !textureName.Terminated || !opacityName.Terminated;
                textureNames.Add(PristonBinaryUtil.BaseName(textureName.Value));
                opacityNames.Add(PristonBinaryUtil.BaseName(RecoverTruncatedCompanionName(textureName, opacityName)));
            }

            for (var index = 0; index < animationTextureCounter; index++)
            {
                var animationName = ReadNullString(cursor, stringTableEnd);
                var animationOpacityName = ReadNullString(cursor, stringTableEnd);
                hasTruncatedStringTable |= !animationName.Terminated || !animationOpacityName.Terminated;
                animationNames.Add(PristonBinaryUtil.BaseName(animationName.Value));
                animationOpacityNames.Add(PristonBinaryUtil.BaseName(RecoverTruncatedCompanionName(animationName, animationOpacityName)));
            }

            stringTableTrailer = cursor.Slice(stringTableEnd - cursor.Position);
        }

        var slot0Texture = textureNames.FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;
        var slot0Opacity = opacityNames.FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;
        var slot1Texture = textureNames.Count > 1 ? textureNames[1] : string.Empty;
        var slot1Opacity = opacityNames.Count > 1 ? opacityNames[1] : string.Empty;
        return new PristonMaterial(
            inUseCount != 0,
            textureCounter,
            textureType,
            blendType,
            twoSided,
            diffuse,
            transparency,
            unchecked((uint)meshState),
            (PristonMaterialSurfaceFlags)unchecked((uint)useState),
            slot0Texture,
            slot0Opacity,
            textureFormStates,
            animationNames,
            frameMask,
            shiftFrameSpeed,
            animationFrame,
            selfIllum,
            slot1Texture,
            slot1Opacity)
        {
            InUseCount = inUseCount,
            TexturePointers32 = texturePointers32,
            TextureStageStates = textureStageStates,
            AnimatedTexturePointers32 = animatedTexturePointers32,
            TextureNames = textureNames,
            OpacityNames = opacityNames,
            AnimationOpacityNames = animationOpacityNames,
            StringTableTrailer = stringTableTrailer,
            HasTruncatedStringTable = hasTruncatedStringTable,
            ReformTexture = reformTexture,
            MapOpacity = mapOpacity,
            Shade = shade,
            SerialNumber = serialNumber,
            TextureSwap = textureSwap,
            MaterialFrame = materialFrame,
            TextureClip = textureClip,
            UseState = useState,
            WindMeshBottom = windMeshBottom
        };
    }

    private static MaterialString ReadNullString(BinaryCursor cursor, int end)
    {
        var start = cursor.Position;
        var terminated = false;
        while (cursor.Position < end)
        {
            if (cursor.Byte() == 0)
            {
                terminated = true;
                break;
            }
        }

        var count = cursor.Position - start;
        if (terminated)
        {
            count--;
        }

        if (count == 0)
        {
            return new MaterialString(string.Empty, terminated);
        }

        return new MaterialString(Encoding.ASCII.GetString(cursor.SliceFrom(start, count)), terminated);
    }

    private static string RecoverTruncatedCompanionName(MaterialString primary, MaterialString companion)
    {
        if (!companion.Terminated
            && companion.Value.Length > 0
            && primary.Terminated
            && primary.Value.StartsWith(companion.Value, StringComparison.OrdinalIgnoreCase))
        {
            return primary.Value;
        }

        return companion.Value;
    }

    private static PristonStageVertex ReadStageVertex(BinaryCursor cursor)
    {
        var start = cursor.Position;
        var sum = cursor.UInt32();
        var renderVertexPointer32 = cursor.UInt32();
        var x = cursor.Int32() / GameUnits.FixedScale;
        var y = cursor.Int32() / GameUnits.FixedScale;
        var z = cursor.Int32() / GameUnits.FixedScale;
        var blue = cursor.Int16();
        var green = cursor.Int16();
        var red = cursor.Int16();
        var alpha = cursor.Int16();
        AssertRecordSize(cursor, start, SmdLayout.StageVertexSize, "stage vertex record");
        return new PristonStageVertex(ToMonoGameStagePosition(x, y, z), PristonBinaryUtil.StageColor(blue, green, red, alpha))
        {
            Sum = sum,
            RenderVertexPointer32 = renderVertexPointer32
        };
    }

    private static PristonStageLight ReadStageLight(BinaryCursor cursor)
    {
        var start = cursor.Position;
        var type = cursor.Int32();
        var x = cursor.Int32() / GameUnits.FixedScale;
        var y = cursor.Int32() / GameUnits.FixedScale;
        var z = cursor.Int32() / GameUnits.FixedScale;
        var range = cursor.Int32() / GameUnits.FixedScale;
        var red = cursor.Int16();
        var green = cursor.Int16();
        var blue = cursor.Int16();
        var alignmentPadding = cursor.Int16();
        AssertRecordSize(cursor, start, StageLightSize, "stage light record");
        var color = new Vector3(
            Math.Clamp(red, (short)0, (short)255) / 255f,
            Math.Clamp(green, (short)0, (short)255) / 255f,
            Math.Clamp(blue, (short)0, (short)255) / 255f);
        return new PristonStageLight(type, ToMonoGameStagePosition(x, y, z), range, color)
        {
            AlignmentPadding16 = alignmentPadding
        };
    }

    private static PristonStageFace ReadStageFace(BinaryCursor cursor, uint textureLinkBase)
    {
        var start = cursor.Position;
        var sum = cursor.UInt32();
        var calculationSum = cursor.Int32();
        var a = cursor.UInt16();
        var b = cursor.UInt16();
        var c = cursor.UInt16();
        var material = cursor.UInt16();
        var textureLinkPointer32 = cursor.UInt32();
        var normal = new Short4(cursor.Int16(), cursor.Int16(), cursor.Int16(), cursor.Int16());
        AssertRecordSize(cursor, start, SmdLayout.StageFaceSize, "stage face record");
        return new PristonStageFace(a, b, c, material, TexLinkIndex(textureLinkPointer32, textureLinkBase))
        {
            Sum = sum,
            CalculationSum = calculationSum,
            TextureLinkPointer32 = textureLinkPointer32,
            AuthoredNormal = normal
        };
    }

    private static PristonTexLink ReadTexLink(BinaryCursor cursor, uint textureLinkBase, int textureLinkCount)
    {
        var start = cursor.Position;
        var u0 = cursor.Single();
        var u1 = cursor.Single();
        var u2 = cursor.Single();
        var v0 = cursor.Single();
        var v1 = cursor.Single();
        var v2 = cursor.Single();
        var texturePointer32 = cursor.UInt32();
        var nextPointer32 = cursor.UInt32();
        AssertRecordSize(cursor, start, TexLinkSize, "texture link record");
        var nextIndex = TexLinkIndex(nextPointer32, textureLinkBase);
        return new PristonTexLink(new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u2, v2), nextPointer32)
        {
            TexturePointer32 = texturePointer32,
            NextIndex = nextIndex < textureLinkCount ? nextIndex : -1
        };
    }

    private static PristonModelObject ReadObject(BinaryCursor cursor, ObjectInfo info, string path)
    {
        var objectStart = cursor.Position;
        var head = cursor.UInt32();

        var vertexPointer32 = cursor.UInt32();
        var facePointer32 = cursor.UInt32();
        var textureLinkPointer32 = cursor.UInt32();
        var physiquePointer32 = cursor.UInt32();
        var zeroVertexPositionFixed = ReadInt3(cursor);
        var zeroVertexNormalFixed = ReadInt3(cursor);
        var maximumZ = cursor.Int32();
        var minimumZ = cursor.Int32();
        var maximumY = cursor.Int32();
        var minimumY = cursor.Int32();
        var maximumX = cursor.Int32();
        var minimumX = cursor.Int32();
        var distanceBound = cursor.Int32();
        var bound = cursor.Int32();
        var maximumVertexCount = cursor.Int32();
        var maximumFaceCount = cursor.Int32();
        var vertexCount = cursor.Int32();
        var faceCount = cursor.Int32();
        var textureLinkCount = cursor.Int32();
        var colorEffect = cursor.Int32();
        var clipStates = cursor.UInt32();
        var authoredPosition = ReadInt3(cursor);
        var authoredCameraPosition = ReadInt3(cursor);
        var authoredAngles = ReadInt3(cursor);
        var trigonometryTable = ReadInt32Array(cursor, 8);
        var nodeName = cursor.FixedString(32);
        var parentName = cursor.FixedString(32);
        var parentPointer32 = cursor.UInt32();
        var transform = PristonBinaryUtil.ReadFixedMatrix(cursor);
        var inverseTransform = PristonBinaryUtil.ReadFixedMatrix(cursor);
        var frameTransform = PristonBinaryUtil.ReadFloatMatrix(cursor);
        var baseRotation = PristonBinaryUtil.ReadFixedMatrix(cursor);
        var worldTransform = PristonBinaryUtil.ReadFixedMatrix(cursor);
        var localTransform = PristonBinaryUtil.ReadFixedMatrix(cursor);
        var currentFrame = cursor.Int32();
        var rawQuaternion = new Quaternion(cursor.Single(), cursor.Single(), cursor.Single(), cursor.Single());
        var baseQuaternion = ToMonoGameModelRotation(rawQuaternion);
        var baseScaleFixed = ReadInt3(cursor);
        var basePositionFixed = ReadInt3(cursor);
        var rotationKeysPointer32 = cursor.UInt32();
        var positionKeysPointer32 = cursor.UInt32();
        var scaleKeysPointer32 = cursor.UInt32();
        var previousRotationPointer32 = cursor.UInt32();
        var rotationKeyCount = cursor.Int32();
        var positionKeyCount = cursor.Int32();
        var scaleKeyCount = cursor.Int32();
        var allRotationSegments = ReadMotionSegments(cursor);
        var allPositionSegments = ReadMotionSegments(cursor);
        var allScaleSegments = ReadMotionSegments(cursor);
        var frameSegmentCount = cursor.Int32();
        AssertRecordSize(cursor, objectStart, Obj3DSize, "object record");

        ValidateCount(vertexCount, $"{path}:{nodeName} vertices");
        ValidateCount(faceCount, $"{path}:{nodeName} faces");
        ValidateCount(textureLinkCount, $"{path}:{nodeName} texture links");
        ValidateCount(rotationKeyCount, $"{path}:{nodeName} rotation keys");
        ValidateCount(positionKeyCount, $"{path}:{nodeName} position keys");
        ValidateCount(scaleKeyCount, $"{path}:{nodeName} scale keys");
        if (frameSegmentCount is < 0 or > MotionSegmentTableEntries)
        {
            throw new InvalidDataException($"{path}:{nodeName} has invalid TmFrameCnt {frameSegmentCount}.");
        }

        var readsPhysique = physiquePointer32 != 0;
        ValidateObjectLength(path, info, vertexCount, faceCount, textureLinkCount, rotationKeyCount, positionKeyCount, scaleKeyCount, readsPhysique);

        var vertices = new List<PristonModelVertex>(vertexCount);
        for (var index = 0; index < vertexCount; index++)
        {
            vertices.Add(ReadModelVertex(cursor, (head & ObjHeadTypeNewNormal) != 0));
        }

        var faces = new List<PristonModelFace>(faceCount);
        for (var index = 0; index < faceCount; index++)
        {
            faces.Add(ReadModelFace(cursor, textureLinkPointer32));
        }

        var textureLinks = new List<PristonTexLink>(textureLinkCount);
        for (var index = 0; index < textureLinkCount; index++)
        {
            textureLinks.Add(ReadTexLink(cursor, textureLinkPointer32, textureLinkCount));
        }

        var rotationKeys = new List<PristonRotationKey>(rotationKeyCount);
        for (var index = 0; index < rotationKeyCount; index++)
        {
            var frame = cursor.Int32();
            var rotation = ToMonoGameModelRotation(new Quaternion(cursor.Single(), cursor.Single(), cursor.Single(), cursor.Single()));
            rotationKeys.Add(new PristonRotationKey(frame, rotation));
        }

        var positionKeys = new List<PristonPositionKey>(positionKeyCount);
        for (var index = 0; index < positionKeyCount; index++)
        {
            var frame = cursor.Int32();
            positionKeys.Add(new PristonPositionKey(frame, ToMonoGameModelPosition(cursor.Single(), cursor.Single(), cursor.Single())));
        }

        var scaleKeys = new List<PristonScaleKey>(scaleKeyCount);
        for (var index = 0; index < scaleKeyCount; index++)
        {
            var frame = cursor.Int32();
            var scaleFixed = ReadInt3(cursor);
            scaleKeys.Add(new PristonScaleKey(frame, ToMonoGameModelScale(scaleFixed)));
        }

        var previousRotationMatrices = new List<Matrix>(rotationKeyCount);
        for (var index = 0; index < rotationKeyCount; index++)
        {
            previousRotationMatrices.Add(PristonBinaryUtil.ReadFloatMatrix(cursor));
        }

        var physiqueBoneNames = new List<string>();
        if (readsPhysique)
        {
            physiqueBoneNames = new List<string>(vertexCount);
            for (var index = 0; index < vertexCount; index++)
            {
                physiqueBoneNames.Add(cursor.FixedString(PhysiqueNameSize));
            }
        }

        var expectedObjectEnd = checked(info.Offset + info.Length);
        if (cursor.Position != expectedObjectEnd)
        {
            throw new InvalidDataException($"{path}:{nodeName} consumed {cursor.Position - objectStart} bytes, but the object info record Length declares {info.Length}.");
        }

        var basePosition = ToMonoGameModelPosition(
            basePositionFixed.X / GameUnits.FixedScale,
            basePositionFixed.Y / GameUnits.FixedScale,
            basePositionFixed.Z / GameUnits.FixedScale);
        return new PristonModelObject(
            nodeName,
            parentName,
            readsPhysique,
            transform,
            basePosition,
            vertices,
            faces,
            textureLinks,
            rotationKeys,
            previousRotationMatrices,
            positionKeys,
            scaleKeys,
            physiqueBoneNames,
            SliceSegments(allRotationSegments, frameSegmentCount),
            SliceSegments(allPositionSegments, frameSegmentCount),
            SliceSegments(allScaleSegments, frameSegmentCount))
        {
            BaseRotation = baseRotation,
            Head = head,
            VertexPointer32 = vertexPointer32,
            FacePointer32 = facePointer32,
            TextureLinkPointer32 = textureLinkPointer32,
            PhysiquePointer32 = physiquePointer32,
            ZeroVertexPositionFixed = zeroVertexPositionFixed,
            ZeroVertexNormalFixed = zeroVertexNormalFixed,
            AuthoredBoundsFixed = new PristonModelBoundsMetadata(maximumZ, minimumZ, maximumY, minimumY, maximumX, minimumX),
            MaximumVertexCount = maximumVertexCount,
            MaximumFaceCount = maximumFaceCount,
            DistanceBound = distanceBound,
            Bound = bound,
            ColorEffect = colorEffect,
            ClipStates = clipStates,
            AuthoredPosition = authoredPosition,
            AuthoredCameraPosition = authoredCameraPosition,
            AuthoredAngles = authoredAngles,
            TrigonometryTable = trigonometryTable,
            ParentPointer32 = parentPointer32,
            InverseTransform = inverseTransform,
            FrameTransform = frameTransform,
            WorldTransform = worldTransform,
            LocalTransform = localTransform,
            CurrentFrame = currentFrame,
            BaseQuaternion = baseQuaternion,
            BaseScale = ToMonoGameModelScale(baseScaleFixed),
            RotationKeysPointer32 = rotationKeysPointer32,
            PositionKeysPointer32 = positionKeysPointer32,
            ScaleKeysPointer32 = scaleKeysPointer32,
            PreviousRotationPointer32 = previousRotationPointer32,
            SourceOffset = info.Offset,
            SourceLength = info.Length
        };
    }

    // With ObjHeadTypeNewNormal in the object's head the normal field holds the normal's end point (position + normal),
    // in the position's fixed-point units.
    private static PristonModelVertex ReadModelVertex(BinaryCursor cursor, bool normalEndpoints)
    {
        var start = cursor.Position;
        var fixedX = cursor.Int32();
        var fixedY = cursor.Int32();
        var fixedZ = cursor.Int32();
        var position = ToMonoGameModelPosition(fixedX / GameUnits.FixedScale, fixedY / GameUnits.FixedScale,
            fixedZ / GameUnits.FixedScale);
        var normalX = (long)cursor.Int32();
        var normalY = (long)cursor.Int32();
        var normalZ = (long)cursor.Int32();
        if (normalEndpoints)
        {
            normalX -= fixedX;
            normalY -= fixedY;
            normalZ -= fixedZ;
        }

        var normal = ToMonoGameModelPosition(normalX, normalY, normalZ);
        if (normal.LengthSquared() > 0.0001f)
        {
            normal.Normalize();
        }

        AssertRecordSize(cursor, start, ModelVertexSize, "model vertex record");
        return new PristonModelVertex(position, normal);
    }

    private static PristonModelFace ReadModelFace(BinaryCursor cursor, uint textureLinkBase)
    {
        var start = cursor.Position;
        var a = cursor.UInt16();
        var b = cursor.UInt16();
        var c = cursor.UInt16();
        var material = cursor.UInt16();
        var uvA = new Vector2(cursor.Single(), cursor.Single());
        var uvB = new Vector2(cursor.Single(), cursor.Single());
        var uvC = new Vector2(cursor.Single(), cursor.Single());
        var textureLinkPointer32 = cursor.UInt32();
        AssertRecordSize(cursor, start, ModelFaceSize, "model face record");
        return new PristonModelFace(a, b, c, material, TexLinkIndex(textureLinkPointer32, textureLinkBase))
        {
            AuthoredUvA = uvA,
            AuthoredUvB = uvB,
            AuthoredUvC = uvC,
            TextureLinkPointer32 = textureLinkPointer32
        };
    }

    private static void ValidateObjectLength(
        string path,
        ObjectInfo info,
        int vertexCount,
        int faceCount,
        int textureLinkCount,
        int rotationKeyCount,
        int positionKeyCount,
        int scaleKeyCount,
        bool readsPhysique)
    {
        var expectedLength = checked(
            (long)Obj3DSize
            + (long)vertexCount * ModelVertexSize
            + (long)faceCount * ModelFaceSize
            + (long)textureLinkCount * TexLinkSize
            + (long)rotationKeyCount * RotationKeySize
            + (long)positionKeyCount * PositionKeySize
            + (long)scaleKeyCount * ScaleKeySize
            + (long)rotationKeyCount * MatrixSize
            + (readsPhysique ? (long)vertexCount * PhysiqueNameSize : 0));
        if (expectedLength != info.Length)
        {
            throw new InvalidDataException($"{path}:{info.Name} declares object length {info.Length}, but its Win32 counts require {expectedLength} bytes.");
        }
    }

    private static StageAreaReadResult ReadStageAreaIndex(byte[] data, int dataOffset, bool[] occupiedCells, int faceCount, string path)
    {
        var occupiedCellCount = occupiedCells.Count(static occupied => occupied);
        if (occupiedCellCount == 0)
        {
            return new StageAreaReadResult(PristonStageAreaIndex.Empty, dataOffset);
        }

        if (TryReadStageAreaIndex(data, dataOffset, occupiedCells, faceCount, sizeof(uint), out var dwordIndex, out var dwordEnd))
        {
            return new StageAreaReadResult(dwordIndex, dwordEnd);
        }

        if (TryReadStageAreaIndex(data, dataOffset, occupiedCells, faceCount, sizeof(ushort), out var wordIndex, out var wordEnd))
        {
            return new StageAreaReadResult(wordIndex, wordEnd);
        }

        throw new InvalidDataException(
            $"{path} has an invalid stage-area payload at offset {dataOffset}; neither the DWORD EU layout nor the WORD classic layout validates every occupied cell.");
    }

    private static bool TryReadStageAreaIndex(
        byte[] data,
        int dataOffset,
        bool[] occupiedCells,
        int faceCount,
        int elementWidth,
        out PristonStageAreaIndex areaIndex,
        out int endOffset)
    {
        areaIndex = PristonStageAreaIndex.Empty;
        endOffset = dataOffset;
        try
        {
            var cursor = new BinaryCursor(data)
            {
                Position = dataOffset
            };
            var recordOffsets = new int[PristonStageAreaIndex.CellCount];
            var faceCounts = new int[PristonStageAreaIndex.CellCount];
            var totalFaceReferences = 0;
            var occupiedCellCount = 0;
            for (var z = 0; z < PristonStageAreaIndex.Width; z++)
            {
                for (var x = 0; x < PristonStageAreaIndex.Width; x++)
                {
                    var cell = x * PristonStageAreaIndex.Width + z;
                    if (!occupiedCells[cell])
                    {
                        continue;
                    }

                    occupiedCellCount++;
                    var serializedLength = cursor.Int32();
                    if (serializedLength <= 0 || serializedLength > faceCount + 1)
                    {
                        return false;
                    }

                    var serializedFaceCount = ReadStageAreaElement(cursor, elementWidth);
                    if (serializedFaceCount != (uint)(serializedLength - 1))
                    {
                        return false;
                    }

                    recordOffsets[cell] = cursor.Position;
                    faceCounts[cell] = serializedLength - 1;
                    totalFaceReferences = checked(totalFaceReferences + serializedLength - 1);
                    for (var index = 1; index < serializedLength; index++)
                    {
                        if (ReadStageAreaElement(cursor, elementWidth) >= (uint)faceCount)
                        {
                            return false;
                        }
                    }
                }
            }

            endOffset = cursor.Position;

            var offsets = new int[PristonStageAreaIndex.CellCount + 1];
            for (var cell = 0; cell < PristonStageAreaIndex.CellCount; cell++)
            {
                offsets[cell + 1] = checked(offsets[cell] + faceCounts[cell]);
            }

            if (offsets[^1] != totalFaceReferences)
            {
                return false;
            }

            var faceIndices = new int[totalFaceReferences];
            for (var cell = 0; cell < PristonStageAreaIndex.CellCount; cell++)
            {
                if (faceCounts[cell] == 0)
                {
                    continue;
                }

                cursor.Position = recordOffsets[cell];
                var destinationOffset = offsets[cell];
                for (var index = 0; index < faceCounts[cell]; index++)
                {
                    faceIndices[destinationOffset + index] = checked((int)ReadStageAreaElement(cursor, elementWidth));
                }
            }

            areaIndex = new PristonStageAreaIndex(offsets, faceIndices, elementWidth, occupiedCellCount);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            areaIndex = PristonStageAreaIndex.Empty;
            endOffset = dataOffset;
            return false;
        }
    }

    private static uint ReadStageAreaElement(BinaryCursor cursor, int elementWidth)
    {
        if (elementWidth == sizeof(uint))
        {
            return cursor.UInt32();
        }

        if (elementWidth == sizeof(ushort))
        {
            return cursor.UInt16();
        }

        throw new ArgumentOutOfRangeException(nameof(elementWidth), elementWidth, "Stage-area elements must be WORD or DWORD values.");
    }

    private static PristonSourceTrailerMetadata ReadSourceTrailer(byte[] data, int offset)
    {
        const uint fnvOffsetBasis = 2166136261u;
        const uint fnvPrime = 16777619u;
        var hash = fnvOffsetBasis;
        foreach (var value in data.AsSpan(offset))
        {
            hash = unchecked((hash ^ value) * fnvPrime);
        }

        return new PristonSourceTrailerMetadata(data.Length - offset, hash);
    }

    private static PristonMotionSegment[] ReadMotionSegments(BinaryCursor cursor)
    {
        var entries = new PristonMotionSegment[MotionSegmentTableEntries];
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = new PristonMotionSegment(cursor.Int32(), cursor.Int32(), cursor.Int32(), cursor.Int32());
        }

        return entries;
    }

    private static IReadOnlyList<PristonMotionSegment> SliceSegments(PristonMotionSegment[] all, int count)
    {
        if (count == 0)
        {
            return [];
        }

        var result = new PristonMotionSegment[count];
        Array.Copy(all, result, count);
        return result;
    }

    private static uint[] ReadUInt32Array(BinaryCursor cursor, int count)
    {
        var values = new uint[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = cursor.UInt32();
        }

        return values;
    }

    private static int[] ReadInt32Array(BinaryCursor cursor, int count)
    {
        var values = new int[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = cursor.Int32();
        }

        return values;
    }

    private static Int3 ReadInt3(BinaryCursor cursor)
    {
        return new Int3(cursor.Int32(), cursor.Int32(), cursor.Int32());
    }

    private static Quaternion ToMonoGameModelRotation(Quaternion rotation)
    {
        var converted = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W);
        if (converted.LengthSquared() <= 0.0001f)
        {
            return Quaternion.Identity;
        }

        converted.Normalize();
        return converted;
    }

    private static Vector3 ToMonoGameModelScale(Int3 scaleFixed)
    {
        return new Vector3(
            scaleFixed.X / GameUnits.FixedScale,
            scaleFixed.Z / GameUnits.FixedScale,
            scaleFixed.Y / GameUnits.FixedScale);
    }

    private static Vector3 ToMonoGameStagePosition(float x, float y, float z)
    {
        return WorldSpace.Mirror(new Vector3(x, y, z));
    }

    private static Vector3 ToMonoGameModelPosition(float x, float y, float z)
    {
        return new Vector3(x, z, -y);
    }

    private static int TexLinkIndex(uint pointer32, uint basePointer32)
    {
        if (pointer32 == 0 || basePointer32 == 0 || pointer32 < basePointer32)
        {
            return -1;
        }

        var difference = pointer32 - basePointer32;
        if (difference % TexLinkSize != 0)
        {
            return -1;
        }

        return checked((int)(difference / TexLinkSize));
    }

    private static void ValidateCount(int count, string name)
    {
        if (count < 0 || count > 10_000_000)
        {
            throw new InvalidDataException($"Invalid {name} count: {count}.");
        }
    }

    private static void AssertRecordSize(BinaryCursor cursor, int start, int expectedSize, string recordName)
    {
        var actualSize = cursor.Position - start;
        if (actualSize != expectedSize)
        {
            throw new InvalidDataException($"{recordName} consumed {actualSize} bytes; the Win32 layout requires {expectedSize}.");
        }
    }

    private sealed record SmdFileHeader(
        PristonSmdHeaderKind Kind,
        int ObjectCount,
        int MaterialCount,
        int MaterialOffset,
        int FirstObjectOffset,
        int FrameRangeCount,
        PristonFrameRange[] FrameRanges);

    private sealed record ObjectInfo(string Name, int Length, int Offset);

    private sealed record StageInfo(
        int VertexCount,
        int FaceCount,
        int TextureLinkCount,
        int LightCount,
        uint TextureLinkPointer32,
        int Contrast,
        int Brightness,
        Vector3 AuthoredLightDirection,
        int DeclaredAreaElementCount,
        PristonIntRectangle AuthoredMapRectangle,
        bool[] OccupiedAreaCells,
        PristonStageLayoutMetadata Metadata);

    private sealed record MaterialGroupReadResult(
        IReadOnlyList<PristonMaterial> Materials,
        PristonMaterialGroupMetadata Metadata)
    {
        public static MaterialGroupReadResult Empty { get; } = new(
            [],
            new PristonMaterialGroupMetadata(0, 0, 0, 0, 0, 0, string.Empty));
    }

    private sealed record StageAreaReadResult(PristonStageAreaIndex Index, int EndOffset);

    private readonly record struct MaterialString(string Value, bool Terminated);
}

internal static class BinaryCursorExtensions
{
    public static byte[] SliceFrom(this BinaryCursor cursor, int start, int count)
    {
        var oldPosition = cursor.Position;
        cursor.Position = start;
        var bytes = cursor.Slice(count);
        cursor.Position = oldPosition;
        return bytes;
    }
}
