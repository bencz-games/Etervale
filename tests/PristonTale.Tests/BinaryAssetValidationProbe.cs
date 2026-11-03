using System.Text;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
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
            + $"camera-positions={result.CameraPositionCount} camera-rotations={result.CameraRotationCount} failures={result.Failures.Count}");
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
            AddModel(reader.ReadModel(path), result);
            return;
        }

        try
        {
            var stage = reader.ReadStage(path);
            result.StageCount++;
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
            AddModel(reader.ReadModel(path), result);
            return;
        }
    }

    private static void AddModel(PristonModel model, ValidationResult result)
    {
        result.ModelCount++;
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

    private static bool HasStageSignature(string path)
    {
        Span<byte> header = stackalloc byte[SmdLayout.SignatureSize];
        using var stream = AssetFiles.OpenRead(path);
        stream.ReadExactly(header);
        var terminator = header.IndexOf((byte)0);
        if (terminator < 0)
        {
            terminator = header.Length;
        }

        return Encoding.ASCII.GetString(header[..terminator]).StartsWith(SmdLayout.StagePrefix, StringComparison.Ordinal);
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
        public List<string> Failures { get; } = [];
    }
}
