using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.GltfExport;

namespace PristonTale.Tests;

internal static class RenderTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("unused material catalogs do not enter streaming texture preparation", MaterialUsageAsync);
        yield return ("off-camera geometry remains eligible to cast shadows", ShadowAsync);
        yield return ("static mesh preparation computes topology and normals without a GPU", MeshAsync);
        yield return ("stage normals are smoothed across materials and faces sharing a position, not across a wall", StageNormalsAsync);
        yield return ("a stage face that is never drawn does not bend the drawn ground's normals", HiddenStageNormalsAsync);
        yield return ("CPU image decoding handles invalid data without GPU resources", DecodeAsync);
        yield return ("run-length TGAs decode repeated and stored packets, plain or with the masked header", RunLengthTgaAsync);
        yield return ("UI bitmaps of 32 bits keep their coverage and skip the black key; zeroed coverage stays opaque", BmpCoverageAsync);
        yield return ("PNG export cache separates opacity variants and basename collisions", ExportAsync);
    }

    private static Task MaterialUsageAsync()
    {
        var used = new PristonMaterial(true, 1, 0, 1, false, Vector3.One, 0, 0,
            PristonMaterialSurfaceFlags.None, "visible.bmp", "", [], [], 0, 0, 0);
        var unused = used with { TextureName = "nonexistent-unused.bmp" };
        var stage = new PristonStage("fixture.smd", [used, unused],
            [new(Vector3.Zero, Color.White), new(Vector3.UnitX, Color.White), new(Vector3.UnitZ, Color.White)],
            [new(0, 1, 2, 0, -1), new(0, 1, 2, 0, -1)], [], [], default);
        var selected = PristonMaterialUsage.ForStage(stage).ToArray();
        Assert.True(selected.Length == 1 && selected[0] == used, "unused/duplicate material slots generated streaming work");
        var high = new PristonModelObject("High", "", false, Matrix.Identity, Vector3.Zero,
            [], [new(0, 1, 2, 0, -1)], [], [], [], [], [], [], [], [], []);
        var low = high with { Name = "Low", Faces = new[] { new PristonModelFace(0, 1, 2, 1, -1) } };
        var model = new PristonModel("fixture", [used, unused], [high, low], [], default);
        var actor = new PristonActor(null!, model, model, ["HIGH"]);
        var actorMaterials = PristonMaterialUsage.ForActor(actor).ToArray();
        Assert.True(actorMaterials.Length == 1 && actorMaterials[0] == used, "actor prepared textures belonging to another LOD/armour");
        var allObjects = new PristonActor(null!, model, model, []);
        Assert.True(PristonMaterialUsage.ForActor(allObjects).Count() == 2, "actor without a render filter lost materials");
        return Task.CompletedTask;
    }

    private static Task ShadowAsync()
    {
        var material = new PristonMaterialRuntime(
            [],
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            false,
            false,
            0,
            0);
        var batch = new RenderBatch(
            [],
            [],
            null!,
            material,
            ForceCastsShadow: true,
            ShadowCoveredByAggregate: true,
            IsSkinned: true,
            SkinnedBounds: new BoundingBox(new Vector3(100, 100, 100), new Vector3(101, 101, 101)));
        var aggregate = new RenderBatch(
            [],
            [],
            null!,
            material,
            ForceCastsShadow: true,
            IsSkinned: true,
            SkinnedBounds: new BoundingBox(new Vector3(100, 100, 100), new Vector3(101, 101, 101)));
        var frame = new PristonFrameContext(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Zero,
            0,
            PristonLighting.Default,
            Matrix.Identity,
            null!,
            true,
            1);
        var builder = new RenderQueueBuilder();
        Assert.True(builder.Build([batch], [aggregate], frame) == 1 && builder.Queue.Count == 0, "off-camera geometry entered colour pass");
        Assert.True(builder.Queue.ShadowCasters.Count == 1, "aggregate duplicated or lost the covered shadow caster");
        builder.Build([batch], [aggregate], frame, includeShadowCasters: false);
        Assert.True(builder.Queue.ShadowCasters.Count == 0, "shadow queue was rebuilt on a frame that reuses the previous maps");
        return Task.CompletedTask;
    }

    private static Task StageNormalsAsync()
    {
        // Two ground faces in different materials meeting on a 20-degree ridge, with their own vertices at the shared
        // positions, and a wall standing on the far edge of the second.
        Vector3[] positions =
        [
            new(0, 0, 0), new(100, 0, 0), new(0, 0, 100),
            new(100, 0, 0), new(100, 0, 100), new(0, 0, 100), new(200, 36, 50),
            new(100, 0, 0), new(100, 100, 0), new(100, 0, 100)
        ];
        PristonStageFace[] faces =
        [
            new(0, 2, 1, 0, -1),
            new(3, 5, 6, 1, -1),
            new(7, 9, 8, 2, -1)
        ];
        var normals = PristonStageNormals.Compute(faces, positions);
        var ground = normals[0];
        var shared = normals[2];
        Assert.True(ground.Y > 0.99f && shared.Y > 0.9f && shared.X < -0.01f, $"the ridge corner was not smoothed toward the second face: {shared}");
        Assert.True(Vector3.Distance(normals[2], normals[3]) < 1e-4f, "the same position in two materials kept different normals");
        Assert.True(MathF.Abs(normals[6].Y) < 1e-4f && MathF.Abs(normals[6].X) > 0.99f, $"the wall took the ground's normal: {normals[6]}");
        return Task.CompletedTask;
    }

    private static Task HiddenStageNormalsAsync()
    {
        // A flat ground face and, on its far edge, a collision-only ramp rising 30 degrees.
        Vector3[] positions = [new(0, 0, 0), new(100, 0, 0), new(0, 0, 100), new(100, 0, 100), new(200, 57.7f, 0), new(200, 57.7f, 100)];
        PristonStageFace[] faces = [new(0, 2, 1, 0, -1), new(1, 3, 4, 1, -1), new(4, 3, 5, 1, -1)];
        var all = PristonStageNormals.Compute(faces, positions);
        var drawn = PristonStageNormals.Compute(faces, positions, static face => face.MaterialIndex == 0);
        Assert.True(all[2].Y < 0.999f, $"the fixture's ramp does not bend the ground when drawn: {all[2]}");
        Assert.True(drawn[2].Y > 0.9999f, $"a hidden ramp bent the ground's corner: {drawn[2]}");
        Assert.True(drawn[3] == Vector3.Zero, "a face that is not drawn received a normal");
        return Task.CompletedTask;
    }

    private static Task MeshAsync()
    {
        var a = new PristonCornerVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector2.One, Vector2.Zero);
        var b = new PristonCornerVertex(Vector3.UnitZ, Color.White, Vector2.UnitY, Vector2.One, Vector2.Zero);
        var c = new PristonCornerVertex(Vector3.UnitX, Color.White, Vector2.UnitX, Vector2.One, Vector2.Zero);
        var builder = new PristonGpuMeshBuilder(null!);
        var prepared = builder.PrepareStaticMesh([a, b, c, a, b, c]);
        Assert.True(prepared.Vertices.Length == 3 && prepared.Indices.Length == 6, "mesh topology/deduplication changed");
        Assert.True(prepared.Vertices.All(vertex => vertex.Normal == Vector3.Up), "prepared normals changed");
        Assert.True(prepared.Vertices.All(vertex => vertex.TextureCoordinate1 == Vector2.One), "stage 1 UVs were lost");
        Assert.True(prepared.Bounds.Max == new Vector3(1, 0, 1), "prepared bounds changed");
        var otherLayerUv = a with { Uv1 = Vector2.Zero };
        Assert.True(builder.PrepareStaticMesh([a, b, c, otherLayerUv, b, c]).Vertices.Length == 4, "corners with different stage UVs were welded");
        return Task.CompletedTask;
    }

    private static Task BmpCoverageAsync()
    {
        var covered = Bmp32((0, 0, 0, 255), (4, 4, 4, 0), (90, 60, 30, 128));
        var world = PristonImageDecoder.Decode(covered, ".bmp");
        Assert.True(!world.HasSourceAlpha && world.Pixels.All(static pixel => pixel.A == 255), "a world bitmap took coverage");
        var ui = PristonImageDecoder.Decode(covered, ".bmp", bmpCoverage: true);
        Assert.True(ui.HasSourceAlpha && ui.Pixels[1].A == 0 && ui.Pixels[2].A == 128, "the coverage was not read");
        PristonUiPixels.Prepare(ui, PristonUiColorKey.Black);
        Assert.True(ui.Pixels[0] == new Color(0, 0, 0, 255) && ui.Pixels[1].A == 0, "the black key replaced the coverage");

        var zeroed = PristonImageDecoder.Decode(Bmp32((0, 0, 0, 0), (50, 50, 50, 0), (1, 2, 3, 0)), ".bmp", bmpCoverage: true);
        PristonUiPixels.Prepare(zeroed, PristonUiColorKey.Black);
        Assert.True(!zeroed.HasSourceAlpha && zeroed.Pixels[0].A == 0 && zeroed.Pixels[1].A == 255, "zeroed coverage did not fall back to the key");
        return Task.CompletedTask;
    }

    private static byte[] Bmp32(params (byte R, byte G, byte B, byte A)[] row)
    {
        var bytes = new byte[54 + row.Length * 4];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(row.Length).CopyTo(bytes, 18);
        BitConverter.GetBytes(1).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)32).CopyTo(bytes, 28);
        for (var x = 0; x < row.Length; x++)
        {
            bytes[54 + x * 4] = row[x].B;
            bytes[55 + x * 4] = row[x].G;
            bytes[56 + x * 4] = row[x].R;
            bytes[57 + x * 4] = row[x].A;
        }

        return bytes;
    }

    private static Task DecodeAsync()
    {
        Assert.True(!PristonImageDecoder.Decode([], ".tga").IsValid, "empty TGA accepted");
        var image = PristonImageDecoder.Decode(Tga(20, 40, 60), ".tga");
        Assert.True(image.IsValid && image.Width == 1 && image.Height == 1, "valid TGA rejected");
        Assert.True(image.Pixels[0] == new Color(20, 40, 60, 255), "TGA channel order changed");
        var huge = Tga(0, 0, 0);
        huge[12] = 255;
        huge[13] = 255;
        huge[14] = 255;
        huge[15] = 255;
        Assert.True(!PristonImageDecoder.Decode(huge, ".tga").IsValid, "oversized/truncated TGA accepted");
        return Task.CompletedTask;
    }

    private static Task RunLengthTgaAsync()
    {
        // A 3x2 bottom-up image: a run of four greys, then two stored pixels.
        byte[] packets = [0x83, 9, 9, 9, 0x01, 1, 2, 3, 4, 5, 6];
        var plain = new byte[18 + packets.Length];
        plain[2] = 10;
        plain[12] = 3;
        plain[14] = 2;
        plain[16] = 24;
        packets.CopyTo(plain, 18);
        var masked = (byte[])plain.Clone();
        for (var i = 0; i < 18; i++)
        {
            masked[i] = unchecked((byte)(masked[i] + i * i));
        }

        masked[0] = (byte)'G';
        masked[1] = (byte)'8';
        foreach (var bytes in new[] { plain, masked })
        {
            var image = PristonImageDecoder.Decode(bytes, ".tga");
            Assert.True(image.IsValid && image.Width == 3 && image.Height == 2, "run-length TGA rejected");
            Assert.True(image.Pixels[3] == new Color(9, 9, 9, 255) && image.Pixels[0] == new Color(9, 9, 9, 255)
                && image.Pixels[1] == new Color(3, 2, 1, 255) && image.Pixels[2] == new Color(6, 5, 4, 255),
                "run-length packets landed in the wrong pixels");
        }

        Assert.True(!PristonImageDecoder.Decode(plain[..^3], ".tga").IsValid, "truncated run-length TGA accepted");
        return Task.CompletedTask;
    }

    private static Task ExportAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "base.tga"), Tga(200, 180, 160));
            File.WriteAllBytes(Path.Combine(directory, "white.tga"), Tga(255, 255, 255));
            File.WriteAllBytes(Path.Combine(directory, "black.tga"), Tga(0, 0, 0));
            var material = new PristonMaterial(
                true,
                1,
                0,
                1,
                false,
                Vector3.One,
                0,
                0,
                PristonMaterialSurfaceFlags.None,
                "base.tga",
                "white.tga",
                [],
                [],
                0,
                0,
                0);
            var factory = new MaterialFactory(directory, directory);
            factory.Create(material, 0);
            factory.Create(material with { OpacityName = "black.tga" }, 1);
            Assert.True(factory.GeneratedPngs == 2, "opacity variants reused one PNG");
            var images = Directory.GetFiles(directory, "*.png");
            Assert.True(images.Length == 2, "variant filenames collided");
            var alphas = images.Select(path => PristonImageDecoder.Decode(File.ReadAllBytes(path), ".png").Pixels[0].A).Order().ToArray();
            Assert.True(alphas.SequenceEqual(new byte[] { 0, 255 }), "different opacity variants have identical pixels");
            factory.Create(material, 2);
            Assert.True(factory.GeneratedPngs == 2, "identical image was regenerated");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static byte[] Tga(byte red, byte green, byte blue)
    {
        var bytes = new byte[21];
        bytes[2] = 2;
        bytes[12] = 1;
        bytes[14] = 1;
        bytes[16] = 24;
        bytes[17] = 0x20;
        bytes[18] = blue;
        bytes[19] = green;
        bytes[20] = red;
        return bytes;
    }
}
