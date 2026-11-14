using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering.Gltf;

namespace PristonTale.Gameplay.World.Streaming;

// Visual-only scenery supplement. Collision continues to come solely from the Stage.
internal static class ModernForestOverlay
{
    private const string MaskedStageSha256 = "5df14edaff73c9189e35bcac7eafc0aa39b62913caba8c80f4614f69e4625d7e";
    private const string StageAsset = "Field/forest/fore-1.smd";
    private const string ManifestAsset = "Field/forest/fore-1.modern-forest.json";
    private const string ModelAsset = "Field/forest/fore-1-modern-LOD1.glb";
    private const string FarModelAsset = "Field/forest/fore-1-modern-LOD2.glb";
    private const string ManifestFormat = "ETERVALE_MODERN_FOREST_99_V1";
    private const int InstanceCount = 99;

    public static PristonActorInstance[] Prepare(string assetRoot, string stagePath, IClientLog log)
    {
        var expectedStage = AssetFiles.Combine(assetRoot, StageAsset);
        if (!Path.GetFullPath(stagePath).Equals(Path.GetFullPath(expectedStage), StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        using var stageStream = File.OpenRead(stagePath);
        var actualStageSha256 = Convert.ToHexString(SHA256.HashData(stageStream)).ToLowerInvariant();
        if (actualStageSha256 != MaskedStageSha256)
        {
            return [];
        }

        var manifestPath = AssetFiles.Combine(assetRoot, ManifestAsset);
        var modelPath = AssetFiles.Combine(assetRoot, ModelAsset);
        var farModelPath = AssetFiles.Combine(assetRoot, FarModelAsset);
        if (!File.Exists(manifestPath) || !File.Exists(modelPath) || !File.Exists(farModelPath))
        {
            throw new FileNotFoundException("The modified forest Stage requires both the modern forest manifest and glTF model.",
                !File.Exists(manifestPath) ? manifestPath : modelPath);
        }

        using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = json.RootElement;
        if (root.GetProperty("format").GetString() != ManifestFormat
            || root.GetProperty("stage_sha256").GetString() != MaskedStageSha256
            || root.GetProperty("model").GetString() != Path.GetFileName(ModelAsset)
            || root.GetProperty("lod_far_model").GetString() != Path.GetFileName(FarModelAsset))
        {
            throw new InvalidDataException("Modern forest manifest does not match the experimental Stage and model.");
        }

        using (var modelStream = File.OpenRead(modelPath))
        {
            var digest = Convert.ToHexString(SHA256.HashData(modelStream)).ToLowerInvariant();
            if (root.GetProperty("model_sha256").GetString() != digest)
            {
                throw new InvalidDataException("Modern forest model hash differs from its manifest.");
            }
        }

        // Both models must match the signed 99-placement manifest before any GPU publication.
        using (var farStream = File.OpenRead(farModelPath))
        {
            var digest = Convert.ToHexString(SHA256.HashData(farStream)).ToLowerInvariant();
            if (root.GetProperty("lod_far_sha256").GetString() != digest)
            {
                throw new InvalidDataException("Modern forest far LOD hash differs from its manifest.");
            }
        }

        var records = root.GetProperty("instances");
        if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() != InstanceCount)
        {
            throw new InvalidDataException($"Modern forest requires exactly {InstanceCount} unique instances.");
        }

        // A single actor/source is reused by all 99 instances; the renderer shares its GPU buffers and textures.
        var actor = PristonActorLoader.LoadGltf(TrialModelOrDefault(modelPath));
        if (Environment.GetEnvironmentVariable("ETV_FOREST_TEST_MODEL") is { Length: > 0 } trial)
            log.Info($"[ETV] M1.5.4 forest test model={trial}; same model for both distance tiers.");
        var result = new PristonActorInstance[InstanceCount];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var record in records.EnumerateArray())
        {
            var id = record.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                throw new InvalidDataException("Modern forest has a blank or repeated instance ID.");
            }

            var position = record.GetProperty("render_position");
            if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 3)
            {
                throw new InvalidDataException($"Modern forest instance {id} has no 3D position.");
            }

            var x = ReadFinite(position[0], "x");
            var y = ReadFinite(position[1], "y");
            var z = ReadFinite(position[2], "z");
            var height = ReadFinite(record.GetProperty("height"), "height");
            var angle = ReadFinite(record.GetProperty("yaw_radians"), "yaw_radians");
            // Catch centred Blender preview coordinates before they reach the GPU.
            // fore-1.smd original vertex bounds, measured in game units (fixed / 256):
            // X [-398.461, 7767.492], Y [-137.520, 1022.172],
            // game Z [-15122.012, -6222.043]. WorldSpace.Mirror flips Z.
            // 32-unit safety margin mirrors the Python preparation validator.
            if (x < -430.461f || x > 7799.492f
                || y < -169.520f || y > 1054.172f
                || z < 6190.043f || z > 15154.012f)
            {
                throw new InvalidDataException($"Modern forest instance {id} lies outside the forest Stage bounds.");
            }

            if (height < 10f || height > 1200f)
            {
                throw new InvalidDataException($"Modern forest instance {id} has an invalid height.");
            }

            // Exported GLB is centred at the base and exactly one metre high; GltfActorSource scales metres to game units.
            var world = Matrix.CreateScale(height / GltfActorSource.UnitsPerMetre)
                * Matrix.CreateRotationY(angle)
                * Matrix.CreateTranslation(x, y, z);
            result[index++] = new PristonActorInstance(actor, world, MotionCode.Stand);
        }

        log.Info($"[ETV] Prepared {result.Length} modern tree placements for fore-1 (dual LOD from LOD0; distance-based GPU residency).");
        return result;
    }

    internal static PristonActor LoadFar(string assetRoot)
    {
        return PristonActorLoader.LoadGltf(TrialModelOrDefault(AssetFiles.Combine(assetRoot, FarModelAsset)));
    }

    // M1.5.4: both original assets and the 99-placement manifest remain verified.
    // Only an explicit, hash-locked test model overrides the two LOD sources.
    private static string TrialModelOrDefault(string original)
    {
        var path = Environment.GetEnvironmentVariable("ETV_FOREST_TEST_MODEL");
        if (string.IsNullOrWhiteSpace(path)) return original;
        var expected = Environment.GetEnvironmentVariable("ETV_FOREST_TEST_MODEL_SHA256");
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
            || expected is null || expected.Length != 64)
            throw new InvalidDataException("M1.5.4 requires an absolute GLB path and SHA-256 digest.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M1.5.4 test model SHA-256 mismatch.");
        return path;
    }

    private static float ReadFinite(JsonElement value, string label)
    {
        var number = value.GetSingle();
        return float.IsFinite(number) ? number : throw new InvalidDataException($"Modern forest has non-finite {label}.");
    }
}
