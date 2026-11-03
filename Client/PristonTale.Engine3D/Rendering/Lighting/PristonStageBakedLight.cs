using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Engine3D.Rendering.Lighting;

// The light a stage's art was authored under, for the scenery models placed on it. Stage geometry carries its authored
// per-vertex light in its vertex colours (Passes/Surface.fx tints the surface by it); the scenery models (windmills,
// cranes, flags) share the stage's materials but have no vertex colours, so drawn at full white they show the same
// texture several times brighter than the stage beside them (a crane's metal at about 3.5 times the stage's). A model
// surface takes the mean linear luminance of the stage's vertex light on the faces with the same texture, or of the
// whole stage for a texture the stage does not use, as a neutral grey vertex colour.
internal sealed class PristonStageBakedLight
{
    public static readonly PristonStageBakedLight None = new(new Dictionary<string, Color>(), Color.White);

    private readonly Dictionary<string, Color> _byTexture;
    private readonly Color _mean;

    private PristonStageBakedLight(Dictionary<string, Color> byTexture, Color mean)
    {
        _byTexture = byTexture;
        _mean = mean;
    }

    public Color Mean => _mean;

    public static PristonStageBakedLight For(PristonStage stage)
    {
        if (stage.Vertices.Count == 0)
        {
            return None;
        }

        var sums = new Dictionary<string, (double Sum, int Count)>(StringComparer.OrdinalIgnoreCase);
        double total = 0;
        foreach (var vertex in stage.Vertices)
        {
            total += LinearLuminance(vertex.Color);
        }

        foreach (var face in stage.Faces)
        {
            if (face.MaterialIndex < 0 || face.MaterialIndex >= stage.Materials.Count)
            {
                continue;
            }

            var name = stage.Materials[face.MaterialIndex].TextureName;
            if (name.Length == 0 || !ValidCorner(stage, face.A) || !ValidCorner(stage, face.B) || !ValidCorner(stage, face.C))
            {
                continue;
            }

            var light = LinearLuminance(stage.Vertices[face.A].Color)
                + LinearLuminance(stage.Vertices[face.B].Color)
                + LinearLuminance(stage.Vertices[face.C].Color);
            sums.TryGetValue(name, out var entry);
            sums[name] = (entry.Sum + light, entry.Count + 3);
        }

        var byTexture = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, entry) in sums)
        {
            byTexture[name] = Grey(entry.Sum / entry.Count);
        }

        return new PristonStageBakedLight(byTexture, Grey(total / stage.Vertices.Count));
    }

    public Color For(PristonMaterial material) =>
        material.TextureName.Length > 0 && _byTexture.TryGetValue(material.TextureName, out var light) ? light : _mean;

    public Color For(IReadOnlyList<PristonMaterial> materials, int materialIndex) =>
        materialIndex >= 0 && materialIndex < materials.Count ? For(materials[materialIndex]) : _mean;

    private static bool ValidCorner(PristonStage stage, int index) => index >= 0 && index < stage.Vertices.Count;

    internal static double LinearLuminance(Color colour) =>
        PristonPhotometry.Luminance(new Vector3(PristonColorSpace.SrgbToLinear(colour.R / 255f), PristonColorSpace.SrgbToLinear(colour.G / 255f), PristonColorSpace.SrgbToLinear(colour.B / 255f)));

    private static Color Grey(double linear)
    {
        var encoded = PristonColorSpace.LinearToSrgb((float)Math.Clamp(linear, 0.0, 1.0));
        var value = (byte)MathF.Round(encoded * 255f);
        return new Color(value, value, value);
    }
}
