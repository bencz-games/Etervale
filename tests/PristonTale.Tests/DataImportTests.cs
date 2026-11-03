using System.Text;
using PristonTale.DataImport;

namespace PristonTale.Tests;

internal static class DataImportTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("copying another client's folder unmasks its files, keeps the game data's own and refuses a later SMD format", CopyAsync);
    }

    private static Task CopyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"priston-data-import-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var assets = Path.Combine(root, "assets");
        const byte Mask = 0x82;
        try
        {
            var stage = Signed("SMD Stage data Ver 0.72");
            Write(source, "Field/Nest/Nest.smd", Masked(stage, Mask));
            Write(source, "Field/Nest/Later.smd", Masked(Signed("SMD Stage data Ver 2.00"), Mask));
            Write(source, "Field/Nest/ground.bmp", Masked(Bitmap(), Mask));
            Write(source, "Field/Nest/scene.ase", Masked([1, 2, 3], Mask));
            Write(source, "Field/Nest/kept.bmp", Masked(Bitmap(), Mask));
            Write(assets, "Field/Nest/kept.bmp", [7]);

            var result = DataCopier.Copy(source, assets, ["Field/Nest"], Mask);

            Assert.True(result is { Copied: 2, Kept: 1, Skipped: 1, Rejected: 1 },
                $"copied {result.Copied}, kept {result.Kept}, skipped {result.Skipped}, rejected {result.Rejected}");
            Assert.True(File.ReadAllBytes(Path.Combine(assets, "Field/Nest/Nest.smd")).SequenceEqual(stage), "the stage was not unmasked");
            Assert.True(File.ReadAllBytes(Path.Combine(assets, "Field/Nest/kept.bmp")).SequenceEqual(new byte[] { 7 }),
                "a file already in the game data was overwritten");
            Assert.True(!File.Exists(Path.Combine(assets, "Field/Nest/Later.smd")) && result.Report.Any(line => line.Contains("Later.smd")),
                "a stage in a later SMD format was copied or not reported");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private static byte[] Signed(string signature)
    {
        var bytes = new byte[64];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Bitmap()
    {
        var bytes = new byte[60];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        return bytes;
    }

    private static byte[] Masked(byte[] bytes, byte mask) => bytes.Select(value => (byte)(value ^ mask)).ToArray();

    private static void Write(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
