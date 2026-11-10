using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Tests;

internal static class BinaryAssetValidationProbe
{
    public static void Run(string assetRoot)
    {
        var inxReader = new PristonInxReader();
        var smdReader = new PristonSmdReader();
        var result = new ValidationResult();
        foreach (var path in Directory.EnumerateFiles(assetRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".inx" or ".smd" or ".smb" or ".cam"))
            {
                continue;
            }

            try
            {
                if (extension == ".inx")
                {
                    ValidateInx(inxReader.Read(path), result);
                }
                else if (extension == ".cam")
                {
                    ValidateCamera(PristonCameraPath.ReadCam(path), result);
                }
                else
                {
                    ValidateSmdOrSmb(path, smdReader, result);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                result.Failures.Add($"{path}: {exception.Message}");
            }
        }

        Console.WriteLine(
            $"BINARY-ASSETS INX={result.InxCount} SMD/SMB-model={result.ModelCount} stage={result.StageCount} CAM={result.CameraCount} "
            + $"binary-model={result.BinaryModelCount} stage-signature-model={result.StageSignatureModelCount} "
            + $"area-word={result.WordAreaCount} area-dword={result.DwordAreaCount} trailer={result.TrailerByteCount} "
            + $"truncated-materials={result.TruncatedMaterialCount} invalid-model-keywords={result.InvalidModelKeywordCount} "
            + $"camera-positions={result.CameraPositionCount} camera-rotations={result.CameraRotationCount} "
            + $"texture-references={result.TextureReferenceCount} missing-textures={result.MissingTextures.Count} failures={result.Failures.Count}");
        foreach (var (texture, firstReference) in result.MissingTextures)
        {
            Console.WriteLine($"missing texture {texture} (first named by {firstReference})");
        }

        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine(failure);
        }

        if (result.Failures.Count > 0)
        {
            throw new InvalidDataException($"Binary asset validation failed for {result.Failures.Count} files.");
        }
    }

    private static void ValidateInx(PristonModelInfo info, ValidationResult result)
    {
        result.InxCount++;
        if (!info.ModelKeywordsValid)
        {
            result.InvalidModelKeywordCount++;
        }
    }

    private static void ValidateCamera(PristonCameraPath camera, ValidationResult result)
    {
        result.CameraCount++;
        result.CameraPositionCount += camera.PositionKeys.Count;
        result.CameraRotationCount += camera.RotationKeys.Count;
    }

    private static void ValidateSmdOrSmb(string path, PristonSmdReader reader, ValidationResult result)
    {
        if (!HasStageSignature(path))
        {
            AddModel(path, reader.ReadModel(path), result);
            return;
        }

        try
        {
            var stage = reader.ReadStage(path);
            result.StageCount++;
            AddTextureReferences(path, stage.Materials, stage.Faces.Select(static face => face.MaterialIndex), result);
            result.TrailerByteCount += stage.SourceTrailer.ByteCount;
            result.TruncatedMaterialCount += stage.Materials.Count(static material => material.HasTruncatedStringTable);
            if (stage.AreaIndex.SerializedElementWidth == sizeof(ushort))
            {
                result.WordAreaCount++;
            }
            else if (stage.AreaIndex.SerializedElementWidth == sizeof(uint))
            {
                result.DwordAreaCount++;
            }

            return;
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("Load it through ReadModel", StringComparison.Ordinal))
        {
            AddModel(path, reader.ReadModel(path), result);
            return;
        }
    }

    private static void AddModel(string path, PristonModel model, ValidationResult result)
    {
        result.ModelCount++;
        AddTextureReferences(path, model.Materials,
            model.Objects.SelectMany(static modelObject => modelObject.Faces).Select(static face => face.MaterialIndex), result);
        result.TrailerByteCount += model.SourceTrailer.ByteCount;
        result.TruncatedMaterialCount += model.Materials.Count(static material => material.HasTruncatedStringTable);
        if (model.Header.Kind == PristonSmdHeaderKind.BinaryModel)
        {
            result.BinaryModelCount++;
        }
        else if (model.Header.Kind == PristonSmdHeaderKind.StageHeaderWithModelLayout)
        {
            result.StageSignatureModelCount++;
        }
    }

    // Only materials some face draws count. A texture resolves as the renderer looks for it: in the file's own folder,
    // under any of the texture extensions.
    private static void AddTextureReferences(string path, IReadOnlyList<PristonMaterial> materials, IEnumerable<int> faceMaterials,
        ValidationResult result)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        foreach (var index in faceMaterials.Where(index => (uint)index < (uint)materials.Count).Distinct())
        {
            var material = materials[index];
            for (var stage = 0; stage < material.RenderedStageCount; stage++)
            {
                AddTextureReference(path, directory, material.StageTextureName(stage), result);
                AddTextureReference(path, directory, material.StageOpacityName(stage), result);
            }

            if (material.HasAnimatedTexture)
            {
                foreach (var frame in material.AnimationTextureNames)
                {
                    AddTextureReference(path, directory, frame, result);
                }
            }
        }
    }

    private static void AddTextureReference(string path, string directory, string name, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        result.TextureReferenceCount++;
        if (PristonTextureLoader.ResolvePath(directory, name).Length == 0)
        {
            result.MissingTextures.TryAdd(AssetFiles.Combine(directory, name), path);
        }
    }

    private static bool HasStageSignature(string path)
    {
        Span<byte> header = stackalloc byte[SmdLayout.SignatureSize];
        using var stream = AssetFiles.OpenRead(path);
        stream.ReadExactly(header);
        return AssetMask.SmdSignature(header).StartsWith(SmdLayout.StagePrefix, StringComparison.Ordinal);
    }

    private sealed class ValidationResult
    {
        public int InxCount { get; set; }
        public int ModelCount { get; set; }
        public int StageCount { get; set; }
        public int CameraCount { get; set; }
        public int CameraPositionCount { get; set; }
        public int CameraRotationCount { get; set; }
        public int BinaryModelCount { get; set; }
        public int StageSignatureModelCount { get; set; }
        public int WordAreaCount { get; set; }
        public int DwordAreaCount { get; set; }
        public long TrailerByteCount { get; set; }
        public int TruncatedMaterialCount { get; set; }
        public int InvalidModelKeywordCount { get; set; }
        public int TextureReferenceCount { get; set; }
        public SortedDictionary<string, string> MissingTextures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Failures { get; } = [];
    }
}
