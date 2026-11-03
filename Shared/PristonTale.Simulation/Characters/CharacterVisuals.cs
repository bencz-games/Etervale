using System.Text.RegularExpressions;

namespace PristonTale.Simulation.Characters;

public sealed record CharacterAssetRef(string InxPath)
{
    public string InxExtensionPath => Path.ChangeExtension(InxPath, ".inx");
}

public static class CharacterFace
{
    public static string ResolveHeadPath(CharacterVisualSet visuals, int faceIndex)
    {
        return visuals.FaceIndex == faceIndex
            ? visuals.Head.InxExtensionPath
            : ResolveHeadPath(visuals.Head.InxExtensionPath, faceIndex);
    }

    public const int FaceCount = 3;

    // Face is the rightmost 01–03 token in the filename stem; a variant suffix may follow it.
    private static readonly Regex FaceNumberPattern =
        new(@"0(?<n>[1-3])(?![^.\\/]*[0-9])", RegexOptions.Compiled | RegexOptions.RightToLeft);

    public static bool IsValidIndex(int faceIndex) => faceIndex is >= 0 and < FaceCount;

    public static string ResolveHeadPath(string defaultHeadPath, int faceIndex)
    {
        ArgumentNullException.ThrowIfNull(defaultHeadPath);
        if (!IsValidIndex(faceIndex))
            throw new ArgumentOutOfRangeException(nameof(faceIndex), faceIndex, $"faceIndex must be 0..{FaceCount - 1}");

        var match = FaceNumberPattern.Match(defaultHeadPath);
        if (!match.Success)
            return defaultHeadPath;

        var replacement = $"0{(char)('1' + faceIndex)}";
        return string.Concat(
            defaultHeadPath.AsSpan(0, match.Index),
            replacement.AsSpan(),
            defaultHeadPath.AsSpan(match.Index + match.Length));
    }
}

public sealed record CharacterVisualSet(
    CharacterAssetRef Body,
    CharacterAssetRef Head,
    int FaceIndex,
    int HairIndex,
    int ArmorTableIndex)
{
    public string WeaponKey { get; init; } = "";
    public string OffHandKey { get; init; } = "";
    public bool InVillage { get; init; }
    public uint JobMask { get; init; }

    public const int FaceModelCount = 7;
    public const int HairPotionCount = 13;
}
