using PristonTale.Assets;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

// A model's sub model on a skeleton of its own: the original draws it in the main model's place for the motions the main
// model has no clip for (most often a death that breaks the model apart), and the main model again for the others.
public static class AlternateModel
{
    // The sub model's model info (its .inx or descriptor), or null when the model has none or it is missing.
    public static string Path(string assetRoot, PristonModelInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.SubModelFile))
        {
            return null;
        }

        var path = PristonModelPaths.Info(AssetFiles.Combine(assetRoot, info.SubModelFile));
        return File.Exists(path) ? path : null;
    }

    // A sub model on the main model's own skeleton is not an alternate.
    public static bool Alternates(PristonModelInfo main, PristonModelInfo sub) =>
        !string.Equals(main.MotionFile, sub.MotionFile, StringComparison.OrdinalIgnoreCase);

    public static bool Shows(PristonModelInfo main, PristonModelInfo alternate, MotionCode motion) =>
        alternate is not null && PristonMotionSelection.Select(main, motion, default) is null && PristonMotionSelection.Select(alternate, motion, default) is not null;
}
