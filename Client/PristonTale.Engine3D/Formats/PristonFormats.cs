using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;

namespace PristonTale.Engine3D.Formats;

public static class PristonFormatConstants
{
    public const int MotionInfoCount = 512;
    public const int TalkMotionInfoCount = 30;
    public const int NpcMotionInfoCount = 30;
    public const int MotionRateSelectionCount = 100;
    public const int TalkMotionFileCount = 2;
    public const int CharacterMotionBase = 10;
    public const int MotionItemCapacity = 52;
    public const int MotionSkillCapacity = 8;
}

public enum PristonSmdHeaderKind
{
    Version62Model,
    BinaryModel,
    StageVersion72,
    StageVersion73,
    StageHeaderWithModelLayout
}

public readonly record struct Short4(short X, short Y, short Z, short W);

public readonly record struct Int3(int X, int Y, int Z);

public readonly record struct PristonIntRectangle(int Left, int Top, int Right, int Bottom);

public sealed record PristonModelBoundsMetadata(
    int MaximumZ,
    int MinimumZ,
    int MaximumY,
    int MinimumY,
    int MaximumX,
    int MinimumX);

public sealed record PristonSmdHeaderMetadata(
    PristonSmdHeaderKind Kind,
    int ObjectCount,
    int MaterialCount,
    int MaterialOffset,
    int FirstObjectOffset,
    int FrameRangeCount);

public readonly record struct PristonSourceTrailerMetadata(int ByteCount, uint Fnv1a32);

public sealed record PristonMaterialGroupMetadata(
    uint Head,
    uint MaterialPointer32,
    uint MaterialCount,
    int ReformTexture,
    int MaximumMaterialCount,
    int LastSearchMaterial,
    string LastSearchName);

public sealed record PristonStageLayoutMetadata(
    uint Head,
    uint AreaListPointer32,
    int AreaListCount,
    int MemoryMode,
    uint SumCount,
    int CalculationSumCount,
    uint VertexPointer32,
    uint FacePointer32,
    uint TextureLinkPointer32,
    uint LightPointer32,
    uint MaterialGroupPointer32,
    uint StageObjectPointer32,
    uint MaterialPointer32,
    int VertexColorCount,
    uint AreaBufferPointer32);

public sealed class PristonStageAreaIndex
{
    public const int Width = SmdLayout.AreaWidth;
    public const int CellCount = Width * Width;

    private readonly int[] _offsets;
    private readonly int[] _faceIndices;

    public static PristonStageAreaIndex Empty { get; } = new(new int[CellCount + 1], [], 0, 0);

    internal PristonStageAreaIndex(int[] offsets, int[] faceIndices, int serializedElementWidth, int occupiedCellCount)
    {
        if (offsets.Length != CellCount + 1)
        {
            throw new ArgumentException($"Stage area offsets must contain {CellCount + 1} entries.", nameof(offsets));
        }

        _offsets = offsets;
        _faceIndices = faceIndices;
        SerializedElementWidth = serializedElementWidth;
        OccupiedCellCount = occupiedCellCount;
    }

    public int SerializedElementWidth { get; }

    public int OccupiedCellCount { get; }

    public int IndexedFaceReferenceCount => _faceIndices.Length;

    public ReadOnlySpan<int> GetFaces(int x, int z)
    {
        var normalizedX = x & (Width - 1);
        var normalizedZ = z & (Width - 1);
        var cell = normalizedX * Width + normalizedZ;
        return _faceIndices.AsSpan(_offsets[cell], _offsets[cell + 1] - _offsets[cell]);
    }
}

[Flags]
public enum PristonMaterialSurfaceFlags : uint
{
    None = 0,
    Move = 0x0001,        // wind sway (the WINDZ1 material script; NOT a UV scroll). The common foliage flag.
    Animating2 = 0x0002,
    Animating4 = 0x0004,
    Animating8 = 0x0008,
    Animating16 = 0x0010,
    MoveZ1 = 0x0020,
    MoveZ2 = 0x0040,
    MoveX1 = 0x0080,
    MoveX2 = 0x0100,
    Water = 0x0200,       // animated water surface
    Invisible = 0x0400,   // never drawn (collision-only geometry)
    Solid = 0x0800,       // solid collision attribute
    WalkThrough = 0x1000, // no-collision attribute
    RenderLast = 0x2000,  // force into the late/transparent pass
    Blinking = 0x4000,
    Ice = 0x8000,
}

public sealed record PristonMaterial(
    bool InUse,
    uint TextureCounter,
    uint TextureType,
    uint BlendType,
    bool TwoSided,
    Vector3 Diffuse,
    float Transparency,
    uint MeshState,
    PristonMaterialSurfaceFlags SurfaceFlags,
    string TextureName,
    string OpacityName,
    IReadOnlyList<uint> TextureFormStates,
    IReadOnlyList<string> AnimationTextureNames,
    uint FrameMask,
    uint ShiftFrameSpeed,
    uint AnimationFrame,
    float SelfIllum = 0f,
    string SecondTextureName = "",
    string SecondOpacityName = "")
{
    // MULTIMIX (texture type 0) combines its slots as texture stages, each with its own operation and UVs.
    private const uint MultiMix = 0u;

    public bool HasSecondTexture => TextureType == MultiMix
        && TextureCounter > 1
        && !string.IsNullOrWhiteSpace(SecondTextureName);

    public bool HasAnimatedTexture => TextureType == 1 && AnimationTextureNames.Count > 0;

    // The shipped assets combine at most three MULTIMIX stages (diffuse, add/modulate layer, lightmap).
    public const int MaxRenderedStages = 3;

    // Stage 0 is TextureName/OpacityName (or the animation frames); MULTIMIX stages above it follow the slots.
    public int RenderedStageCount => TextureType == MultiMix ? (int)Math.Clamp(TextureCounter, 1u, MaxRenderedStages) : 1;

    public string StageTextureName(int stage) => stage < TextureNames.Count ? TextureNames[stage] : string.Empty;

    public string StageOpacityName(int stage) => stage < OpacityNames.Count ? OpacityNames[stage] : string.Empty;

    public uint StageState(int stage) => stage < TextureStageStates.Count ? TextureStageStates[stage] : 0u;

    public uint StageFormState(int stage) => stage < TextureFormStates.Count ? TextureFormStates[stage] : 0u;

    // MULTIMIX stages above 0 combined in the same pass. The chain stops at a disabled stage (code 1), a missing
    // texture, or a stage with its own opacity mask, which the classic renderer drew in a separate pass.
    public int CombinedLayerCount
    {
        get
        {
            var count = 0;
            for (var stage = 1; stage < RenderedStageCount; stage++)
            {
                var name = StageTextureName(stage);
                if (StageState(stage) == 1
                    || string.IsNullOrWhiteSpace(name)
                    || PristonMaterialRuntimeFactory.IsCutOut(name, StageOpacityName(stage)))
                {
                    break;
                }

                count++;
            }

            return count;
        }
    }

    // Textureless materials are skipped; a white fallback would expose hidden original geometry.
    public bool HasNoRenderableTexture => TextureCounter == 0 && !HasAnimatedTexture;

    public bool IsSelfIllum => SelfIllum != 0f;

    public bool IsInvisible => (SurfaceFlags & PristonMaterialSurfaceFlags.Invisible) != 0;

    public bool RenderLast => (SurfaceFlags & PristonMaterialSurfaceFlags.RenderLast) != 0;

    public bool IsWater => (SurfaceFlags & PristonMaterialSurfaceFlags.Water) != 0;

    public bool IsBlinking => (SurfaceFlags & PristonMaterialSurfaceFlags.Blinking) != 0;

    public IReadOnlyList<uint> TexturePointers32 { get; init; } = [];
    public IReadOnlyList<uint> TextureStageStates { get; init; } = [];
    public IReadOnlyList<uint> AnimatedTexturePointers32 { get; init; } = [];
    public IReadOnlyList<string> TextureNames { get; init; } = [];
    public IReadOnlyList<string> OpacityNames { get; init; } = [];
    public IReadOnlyList<string> AnimationOpacityNames { get; init; } = [];
    public IReadOnlyList<byte> StringTableTrailer { get; init; } = [];
    public bool HasTruncatedStringTable { get; init; }
    public uint InUseCount { get; init; }
    public int ReformTexture { get; init; }
    public int MapOpacity { get; init; }
    public uint Shade { get; init; }
    public uint SerialNumber { get; init; }
    public int TextureSwap { get; init; }
    public int MaterialFrame { get; init; }
    public int TextureClip { get; init; }
    public int UseState { get; init; }
    public int WindMeshBottom { get; init; }
}

public sealed record PristonTexLink(Vector2 A, Vector2 B, Vector2 C, uint NextPointer)
{
    public static readonly PristonTexLink Empty = new(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, 0);
    // A MULTIMIX stage without its own link samples texture coordinate (0, 0), as the reference renderer does.
    public static readonly PristonTexLink Zero = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, 0);
    public uint TexturePointer32 { get; init; }
    // Index of the next stage's link (the texture link record's next pointer) in the same list, or -1 at the end of the chain.
    public int NextIndex { get; init; } = -1;

    public Vector2 Corner(int corner) => corner switch
    {
        0 => A,
        1 => B,
        _ => C
    };
}

public sealed record PristonStageVertex(Vector3 Position, Color Color)
{
    public uint Sum { get; init; }
    public uint RenderVertexPointer32 { get; init; }
}

public sealed record PristonStageFace(int A, int B, int C, int MaterialIndex, int TexLinkIndex)
{
    public uint Sum { get; init; }
    public int CalculationSum { get; init; }
    public uint TextureLinkPointer32 { get; init; }
    public Short4 AuthoredNormal { get; init; }

    // Whether its three corners name vertices of a list this long.
    public bool FitsIn(int vertexCount) => (uint)A < (uint)vertexCount && (uint)B < (uint)vertexCount && (uint)C < (uint)vertexCount;
}

// Decoded from the Win32 stage light record (28 bytes). Its final WORD is compiler alignment after the
// three signed colour shorts. It is preserved so no byte of the serialized structure is discarded.
public sealed record PristonStageLight(int Type, Vector3 Position, float Range, Vector3 Color)
{
    private const int NightFlag = 0x00001;
    private const int LensFlag = 0x00002;
    private const int DynamicFlag = 0x80000;

    public short AlignmentPadding16 { get; init; }

    public bool Night => (Type & NightFlag) != 0;

    // A lens light is drawn as a glow; it lights the ground only when it is also dynamic.
    public bool Lens => (Type & LensFlag) != 0;

    public bool Dynamic => (Type & DynamicFlag) != 0;
}

public sealed record PristonStage(
    string Path,
    IReadOnlyList<PristonMaterial> Materials,
    IReadOnlyList<PristonStageVertex> Vertices,
    IReadOnlyList<PristonStageFace> Faces,
    IReadOnlyList<PristonTexLink> TexLinks,
    IReadOnlyList<PristonStageLight> Lights,
    BoundingBox Bounds)
{
    public PristonSmdHeaderMetadata Header { get; init; }
    public PristonMaterialGroupMetadata MaterialGroup { get; init; }
    public PristonStageLayoutMetadata Layout { get; init; }
    public PristonStageAreaIndex AreaIndex { get; init; } = PristonStageAreaIndex.Empty;
    public int Contrast { get; init; }
    public int Brightness { get; init; }
    public Vector3 AuthoredLightDirection { get; init; }
    public PristonIntRectangle AuthoredMapRectangle { get; init; }
    public int DeclaredAreaElementCount { get; init; }
    public PristonSourceTrailerMetadata SourceTrailer { get; init; }
}

public sealed record PristonModelVertex(Vector3 Position, Vector3 Normal);

public sealed record PristonModelFace(int A, int B, int C, int MaterialIndex, int TexLinkIndex)
{
    public Vector2 AuthoredUvA { get; init; }
    public Vector2 AuthoredUvB { get; init; }
    public Vector2 AuthoredUvC { get; init; }
    public uint TextureLinkPointer32 { get; init; }

    // Whether its three corners name vertices of a list this long.
    public bool FitsIn(int vertexCount) => (uint)A < (uint)vertexCount && (uint)B < (uint)vertexCount && (uint)C < (uint)vertexCount;
}

public sealed record PristonRotationKey(int Frame, Quaternion Rotation);

public sealed record PristonPositionKey(int Frame, Vector3 Position);

public sealed record PristonScaleKey(int Frame, Vector3 Scale);

public sealed record PristonFrameRange(int StartFrame, int EndFrame, int PositionIndex, int PositionCount);

public readonly record struct PristonMotionSegment(int StartFrame, int EndFrame, int KeyIndex, int KeyCount);

public sealed record PristonModelObject(
    string Name,
    string ParentName,
    bool HasPhysique,
    Matrix Transform,
    Vector3 BasePosition,
    IReadOnlyList<PristonModelVertex> Vertices,
    IReadOnlyList<PristonModelFace> Faces,
    IReadOnlyList<PristonTexLink> TexLinks,
    IReadOnlyList<PristonRotationKey> RotationKeys,
    IReadOnlyList<Matrix> PreviousRotationMatrices,
    IReadOnlyList<PristonPositionKey> PositionKeys,
    IReadOnlyList<PristonScaleKey> ScaleKeys,
    IReadOnlyList<string> PhysiqueBoneNames,
    IReadOnlyList<PristonMotionSegment> RotationSegments,
    IReadOnlyList<PristonMotionSegment> PositionSegments,
    IReadOnlyList<PristonMotionSegment> ScaleSegments)
{
    public Matrix BaseRotation { get; init; } = Matrix.Identity;
    public uint Head { get; init; }
    public uint VertexPointer32 { get; init; }
    public uint FacePointer32 { get; init; }
    public uint TextureLinkPointer32 { get; init; }
    public uint PhysiquePointer32 { get; init; }
    public Int3 ZeroVertexPositionFixed { get; init; }
    public Int3 ZeroVertexNormalFixed { get; init; }
    public PristonModelBoundsMetadata AuthoredBoundsFixed { get; init; }
    public int MaximumVertexCount { get; init; }
    public int MaximumFaceCount { get; init; }
    public int DistanceBound { get; init; }
    public int Bound { get; init; }
    public int ColorEffect { get; init; }
    public uint ClipStates { get; init; }
    public Int3 AuthoredPosition { get; init; }
    public Int3 AuthoredCameraPosition { get; init; }
    public Int3 AuthoredAngles { get; init; }
    public IReadOnlyList<int> TrigonometryTable { get; init; } = [];
    public uint ParentPointer32 { get; init; }
    public Matrix InverseTransform { get; init; } = Matrix.Identity;
    public Matrix FrameTransform { get; init; } = Matrix.Identity;
    public Matrix WorldTransform { get; init; } = Matrix.Identity;
    public Matrix LocalTransform { get; init; } = Matrix.Identity;
    public int CurrentFrame { get; init; }
    public Quaternion BaseQuaternion { get; init; } = Quaternion.Identity;
    public Vector3 BaseScale { get; init; } = Vector3.One;
    public uint RotationKeysPointer32 { get; init; }
    public uint PositionKeysPointer32 { get; init; }
    public uint ScaleKeysPointer32 { get; init; }
    public uint PreviousRotationPointer32 { get; init; }
    public int SourceOffset { get; init; }
    public int SourceLength { get; init; }

    public bool IsAnimated { get; } = RotationKeys.Count > 1 || PositionKeys.Count > 1 || ScaleKeys.Count > 1;
    public int LastFrame { get; } = Math.Max(
        RotationKeys.Count > 0 ? RotationKeys[^1].Frame : 0,
        Math.Max(
            PositionKeys.Count > 0 ? PositionKeys[^1].Frame : 0,
            ScaleKeys.Count > 0 ? ScaleKeys[^1].Frame : 0));
}

public sealed record PristonModel(
    string Path,
    IReadOnlyList<PristonMaterial> Materials,
    IReadOnlyList<PristonModelObject> Objects,
    IReadOnlyList<PristonFrameRange> FrameRanges,
    BoundingBox Bounds)
{
    public PristonSmdHeaderMetadata Header { get; init; }
    public PristonMaterialGroupMetadata MaterialGroup { get; init; }
    public PristonSourceTrailerMetadata SourceTrailer { get; init; }
    public bool IsAnimated => Objects.Any(o => o.IsAnimated);
    public int LastFrame => Objects.Count == 0 ? 0 : Objects.Max(o => o.LastFrame);

    // A bone or object is found by name, case-insensitively; the first object of a name wins.
    public int ObjectIndex(string name)
    {
        for (var index = 0; index < Objects.Count; index++)
        {
            if (Objects[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    // ObjectIndex for every named object at once.
    public Dictionary<string, int> ObjectIndexByName()
    {
        var byName = new Dictionary<string, int>(Objects.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Objects.Count; index++)
        {
            var name = Objects[index].Name;
            if (name.Length > 0)
            {
                byName.TryAdd(name, index);
            }
        }

        return byName;
    }
}

public sealed record PristonMotionInfo(
    uint State,
    uint StartFrame,
    uint EndFrame,
    IReadOnlyList<uint> EventFrames,
    int ItemCodeCount,
    uint JobCodeBit,
    int MapPosition,
    uint Repeat,
    byte KeyCode,
    int MotionFrame)
{
    public IReadOnlyList<int> ItemIndices { get; init; } = [];
    public IReadOnlyList<int> ItemCodeTable { get; init; } = [];
    public IReadOnlyList<byte> SkillCodes { get; init; } = [];
    public uint MotionKeyword1 { get; init; }
    public uint MotionKeyword2 { get; init; }
    public uint RawStartFrame { get; init; }
    public uint RawEndFrame { get; init; }
    public bool StartFrameDecoded { get; init; }
    public bool EndFrameDecoded { get; init; }

    // A real clip: it has a state and at least one frame.
    public bool IsUsable => State != 0 && EndFrame > StartFrame;
}

public sealed record PristonModelGroup(
    int ModelNameCount,
    IReadOnlyList<string> ModelNames);

public sealed record PristonModelInfo(
    string Path,
    string ModelFile,
    string MotionFile,
    string SubModelFile,
    PristonModelGroup HighModel,
    PristonModelGroup DefaultModel,
    PristonModelGroup LowModel,
    IReadOnlyList<PristonMotionInfo> Motions,
    uint MotionCount,
    string LinkFile,
    string TalkLinkFile,
    string TalkMotionFile,
    IReadOnlyList<PristonMotionInfo> TalkMotions,
    uint TalkMotionCount)
{
    // The usable clips of the character motion table (from slot CharacterMotionBase), with their slots.
    public IEnumerable<(int Slot, PristonMotionInfo Motion)> UsableMotions()
    {
        for (var slot = PristonFormatConstants.CharacterMotionBase; slot < Motions.Count; slot++)
        {
            if (Motions[slot].IsUsable)
            {
                yield return (slot, Motions[slot]);
            }
        }
    }

    public bool HasUsableMotions => UsableMotions().Any();

    public uint FileTypeKeyword { get; init; }

    public uint LinkFileKeyword { get; init; }

    public uint DecodedFileKeyword { get; init; }

    public uint DecodedLinkKeyword { get; init; }

    public bool ModelKeywordsValid { get; init; }

    public bool MotionFramesWereProtected { get; init; }

    public bool MotionKeywordsFromOtherPath { get; init; }

    public IReadOnlyList<int> NpcMotionRates { get; init; } = [];

    public IReadOnlyList<int> NpcMotionSelection { get; init; } = [];

    public IReadOnlyList<int> TalkMotionRates { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<int>> TalkMotionSelection { get; init; } = [];
}

public sealed record PristonActor(
    PristonModelInfo Info,
    PristonModel Mesh,
    PristonModel Skeleton,
    // Empty renders all objects; shared armor meshes require the INX high-LOD names.
    IReadOnlyList<string> RenderObjectNames);

public sealed record PristonActorInstance(
    PristonActor Actor,
    Matrix World,
    MotionCode Motion)
{
    public PristonMotionContext MotionContext { get; init; } = new(-1, 0, 0);

    public bool VisibleInMainPass { get; init; } = true;
}

// World supplements baked placement for scenery authored at the origin.
public sealed record PristonSceneryInstance(
    PristonModel Model,
    Matrix World);
