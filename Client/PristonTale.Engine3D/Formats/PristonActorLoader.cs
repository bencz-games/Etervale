using PristonTale.Engine3D.IO;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

public sealed class PristonActorLoader
{
    private readonly PristonInxReader _inxReader;
    private readonly PristonSmdReader _smdReader;

    public PristonActorLoader() : this(null) { }

    public PristonActorLoader(PristonAssetCache cache)
    {
        _inxReader = cache != null ? new PristonInxReader(cache) : new PristonInxReader();
        _smdReader = cache != null ? new PristonSmdReader(cache) : new PristonSmdReader();
    }

    public PristonActor Load(string assetRoot, string inxPath)
    {
        var info = _inxReader.Read(inxPath);
        var meshPath = ResolveCachePath(assetRoot, info.ModelFile, ".smd");
        var motionInfo = ResolveMotionInfo(assetRoot, info);
        var motionPath = ResolveMotionPath(assetRoot, motionInfo.MotionFile);
        var skeleton = _smdReader.ReadModel(motionPath);
        var mesh = PristonPhysiqueRepair.Repair(_smdReader.ReadModel(meshPath), skeleton);
        // Shared armor meshes require the INX high-LOD filter; models without groups render all objects.
        var renderObjectNames = SelectRenderObjectNames(info);
        return new PristonActor(motionInfo, mesh, skeleton, renderObjectNames);
    }

    public PristonActor LoadSkinned(string meshSmdPath, string skeletonSmbPath)
    {
        var skeleton = _smdReader.ReadModel(skeletonSmbPath);
        var mesh = PristonPhysiqueRepair.Repair(_smdReader.ReadModel(meshSmdPath), skeleton);
        return new PristonActor(CreateSyntheticIdleInfo(skeleton.LastFrame), mesh, skeleton, []);
    }

    public PristonActor LoadRigid(string smdPath)
    {
        var mesh = _smdReader.ReadModel(smdPath);
        return new PristonActor(CreateSyntheticIdleInfo(GameUnits.FrameTicks), mesh, mesh, []);
    }

    // Synthetic opening actors need a non-empty Stand motion spanning the full skeleton clip.
    private static PristonModelInfo CreateSyntheticIdleInfo(int skeletonLastFrameTicks)
    {
        var endFrame = (uint)Math.Max(1, skeletonLastFrameTicks / GameUnits.FrameTicks);
        var idle = new PristonMotionInfo(
            State: (uint)MotionCode.Stand,
            StartFrame: 0,
            EndFrame: endFrame,
            EventFrames: [],
            ItemCodeCount: 0,
            JobCodeBit: 0,
            MapPosition: 0,
            Repeat: 1,
            KeyCode: 0,
            MotionFrame: 0);

        var motions = new PristonMotionInfo[PristonFormatConstants.CharacterMotionBase + 1];
        for (var i = 0; i < motions.Length; i++)
            motions[i] = idle;

        var empty = new PristonModelGroup(0, []);
        return new PristonModelInfo("", "", "", "", empty, empty, empty,
            motions, (uint)motions.Length, "", "", "", [], 0);
    }

    private static IReadOnlyList<string> SelectRenderObjectNames(PristonModelInfo info)
    {
        var high = NonEmptyNames(info.HighModel);
        if (high.Count > 0)
        {
            return high;
        }

        return NonEmptyNames(info.DefaultModel);
    }

    private static List<string> NonEmptyNames(PristonModelGroup group)
    {
        var names = new List<string>(group.ModelNames.Count);
        foreach (var name in group.ModelNames)
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        return names;
    }

    private string ResolveMotionPath(string assetRoot, string sourcePath)
    {
        string directPath;
        if (TryResolveCachePath(assetRoot, sourcePath, ".smb", out directPath))
            return directPath;

        string linkPath;
        if (TryResolveCachePath(assetRoot, sourcePath, ".inx", out linkPath))
        {
            var linkInfo = _inxReader.Read(linkPath);
            if (!string.IsNullOrWhiteSpace(linkInfo.MotionFile))
                return ResolveCachePath(assetRoot, linkInfo.MotionFile, ".smb");
        }

        return ResolveCachePath(assetRoot, sourcePath, ".smb");
    }

    private PristonModelInfo ResolveMotionInfo(string assetRoot, PristonModelInfo visualInfo)
    {
        if (visualInfo.HasUsableMotions)
            return visualInfo;

        string linkPath;
        if (TryResolveCachePath(assetRoot, visualInfo.MotionFile, ".inx", out linkPath))
        {
            var linkInfo = _inxReader.Read(linkPath);
            if (linkInfo.HasUsableMotions)
                return linkInfo;
        }

        return visualInfo;
    }

    public static string ResolveCachePath(string assetRoot, string sourcePath, string extension)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidDataException("Empty Priston actor source path.");

        var changed = Path.ChangeExtension(sourcePath, extension);
        var resolved = AssetFiles.Resolve(Path.Combine(assetRoot, changed));
        if (File.Exists(resolved))
            return resolved;

        throw new FileNotFoundException($"Cannot resolve Priston actor cache '{sourcePath}' as '{extension}'.", resolved);
    }

    private static bool TryResolveCachePath(string assetRoot, string sourcePath, string extension, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        var changed = Path.ChangeExtension(sourcePath, extension);
        var resolved = AssetFiles.Resolve(Path.Combine(assetRoot, changed));
        if (!File.Exists(resolved))
            return false;

        path = resolved;
        return true;
    }
}
