using System.Numerics;
using PristonTale.Engine3D.Formats.Gltf;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace PristonTale.Tests;

internal static class GltfModelTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a glTF model reads its skin with normalized weights, its rigid parts, linear, step and cubic channels, the "
            + "metallic-roughness material with clearcoat, sheen and diffuse transmission, texture transforms and wraps, and "
            + "gives flat normals where they are missing", SyntheticAsync);
        yield return ("a hand-written .gltf with its buffer beside it places a child listed before its parent, binds a skin without "
            + "inverse binds as identity, keeps the four largest of eight weights, follows the first joint when none weighs, "
            + "reads a spec-gloss material as unmetallic with roughness 1 - glossiness, and refuses a triangle past its vertices", HandWrittenAsync);
        yield return ("generated tangents follow the normal map's UV set, orthogonal to the normal, with the bitangent's sign from a "
            + "mirrored V", TangentsAsync);
        yield return ("the Mythcreature's GLB reads with its eight primitives, 145 joints, one animation and its eight materials", MythcreatureAsync);
        yield return ("the test models read: the knight's 75 joints and 21 clips with step keys, the orc's tangents, the static props' "
            + "normal, metallic-roughness and emissive maps", TestModelsAsync);
    }

    // A 1x1 opaque white PNG.
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    private static Task SyntheticAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "priston-gltf-" + Guid.NewGuid().ToString("N") + ".glb");
        try
        {
            // Unnormalized weights are what the reader must repair; the writer would refuse them.
            Build().SaveGLB(path, new WriteSettings { Validation = SharpGLTF.Validation.ValidationMode.Skip });
            Check(GltfModelReader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static ModelRoot Build()
    {
        var root = ModelRoot.CreateModel();
        var scene = root.UseScene("scene");
        var hip = scene.CreateNode("Hip").WithLocalTranslation(new Vector3(0, 1, 0));
        var tail = hip.CreateNode("Tail").WithLocalTranslation(new Vector3(0, 0, -1));
        var prop = tail.CreateNode("Prop").WithLocalTranslation(new Vector3(2, 0, 0));

        var coat = root.CreateMaterial("Coat");
        coat.InitializePBRMetallicRoughness("ClearCoat", "Sheen", "DiffuseTransmission");
        coat.Alpha = AlphaMode.MASK;
        coat.AlphaCutoff = 0.3f;
        coat.DoubleSided = true;
        var image = root.UseImage(new MemoryImage(Png));
        var baseColor = coat.FindChannel("BaseColor")!.Value;
        baseColor.Color = new Vector4(0.5f, 0.25f, 1f, 0.75f);
        baseColor.SetTexture(1, image, null, TextureWrapMode.CLAMP_TO_EDGE, TextureWrapMode.MIRRORED_REPEAT);
        baseColor.SetTransform(new Vector2(0.25f, 0.5f), new Vector2(2f, 3f), 0.5f);
        var metallicRoughness = coat.FindChannel("MetallicRoughness")!.Value;
        metallicRoughness.SetFactor("MetallicFactor", 0.2f);
        metallicRoughness.SetFactor("RoughnessFactor", 0.6f);
        coat.FindChannel("ClearCoat")!.Value.SetFactor("ClearCoatFactor", 0.7f);
        coat.FindChannel("ClearCoatRoughness")!.Value.SetFactor("RoughnessFactor", 0.15f);
        var sheen = coat.FindChannel("SheenColor")!.Value;
        sheen.Color = new Vector4(0.9f, 0.1f, 0.2f, 1f);
        coat.FindChannel("SheenRoughness")!.Value.SetFactor("RoughnessFactor", 0.4f);
        coat.FindChannel("DiffuseTransmissionFactor")!.Value.SetFactor("DiffuseTransmissionFactor", 0.35f);

        var skinned = root.CreateMesh("Body");
        skinned.CreatePrimitive()
            .WithVertexAccessor("POSITION", new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) })
            .WithVertexAccessor("NORMAL", new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ })
            .WithVertexAccessor("JOINTS_0", new[] { new Vector4(0, 1, 0, 0), new Vector4(1, 0, 0, 0), new Vector4(0, 0, 0, 0) })
            .WithVertexAccessor("WEIGHTS_0", new[] { new Vector4(2, 2, 0, 0), new Vector4(1, 0, 0, 0), new Vector4(0.5f, 0, 0, 0) })
            .WithIndicesAccessor(PrimitiveType.TRIANGLES, new[] { 0, 1, 2 })
            .WithMaterial(coat);
        scene.CreateNode("BodyNode").WithSkinnedMesh(skinned, Matrix4x4.Identity, hip, tail);

        var rigid = root.CreateMesh("Prop");
        rigid.CreatePrimitive()
            .WithVertexAccessor("POSITION", new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1) })
            .WithIndicesAccessor(PrimitiveType.TRIANGLES, new[] { 0, 1, 2 });
        prop.Mesh = rigid;

        var animation = root.CreateAnimation("Move");
        animation.CreateTranslationChannel(hip, new Dictionary<float, Vector3> { [0f] = new(0, 1, 0), [1f] = new(0, 2, 0) }, linear: false);
        animation.CreateScaleChannel(tail, new Dictionary<float, Vector3> { [0f] = Vector3.One, [2f] = new(2, 2, 2) });
        animation.CreateRotationChannel(tail, new Dictionary<float, (Quaternion, Quaternion, Quaternion)>
        {
            [0f] = (default, Quaternion.Identity, default),
            [1.5f] = (default, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f), default)
        });
        return root;
    }

    private static void Check(GltfModel model)
    {
        Assert.True(model.Nodes.Count == 4 && model.Nodes[1].Name == "Tail" && model.Nodes[1].Parent == 0 && model.Nodes[2].Parent == 1,
            "the node hierarchy differs");
        Assert.True(model.Skins is [{ Joints: [0, 1], InverseBinds.Length: 2 }], "the skin's joints differ");

        var body = model.Primitives.Single(static primitive => primitive.Skinned);
        Assert.True(body.Indices is [0, 1, 2] && body.Material == 0, "the skinned primitive's triangles or material differ");
        Assert.True(body.Vertices[0].Weights == new Microsoft.Xna.Framework.Vector4(0.5f, 0.5f, 0, 0)
            && body.Vertices[2].Weights == new Microsoft.Xna.Framework.Vector4(1, 0, 0, 0) && body.Vertices[0].Joints.Y == 1f,
            "the weights were not normalized or the joints differ");

        var prop = model.Primitives.Single(static primitive => !primitive.Skinned);
        Assert.True(model.Nodes[prop.Node].Name == "Prop" && prop.Material == -1 && prop.Skin == -1, "the rigid primitive is not on its node");
        Assert.True(prop.Vertices.Length == 3 && prop.Vertices.All(static vertex => MathF.Abs(vertex.Normal.Y - 1f) < 1e-4f),
            "the missing normals were not made flat");
        Assert.True(MathF.Abs(model.Bounds.Max.X - 3f) < 1e-4f && MathF.Abs(model.Bounds.Min.Z + 2f) < 1e-4f,
            $"the rigid part was not placed by its node: {model.Bounds}");

        var material = model.Materials[0];
        Assert.True(material is { Alpha: GltfAlphaMode.Mask, AlphaCutoff: 0.3f, DoubleSided: true } && material.BaseColor.W == 0.75f
            && MathF.Abs(material.Metallic - 0.2f) < 1e-6f && MathF.Abs(material.Roughness - 0.6f) < 1e-6f, "the base material differs");
        Assert.True(material.BaseColorTexture is { Image: 0, TexCoord: 1, WrapS: GltfWrap.Clamp, WrapT: GltfWrap.Mirror, Rotation: 0.5f }
            && material.BaseColorTexture.Offset.X == 0.25f && material.BaseColorTexture.Scale.Y == 3f, "the texture reference differs");
        Assert.True(MathF.Abs(material.Clearcoat - 0.7f) < 1e-6f && MathF.Abs(material.ClearcoatRoughness - 0.15f) < 1e-6f
            && MathF.Abs(material.SheenColor.X - 0.9f) < 1e-6f && MathF.Abs(material.SheenRoughness - 0.4f) < 1e-6f
            && MathF.Abs(material.DiffuseTransmission - 0.35f) < 1e-6f, "the clearcoat, sheen or diffuse transmission differ");
        Assert.True(model.Images is [{ MimeType: "image/png" } png] && png.Content.Length == Png.Length, "the embedded image differs");

        var move = model.Animations.Single();
        Assert.True(move.Name == "Move" && MathF.Abs(move.DurationSeconds - 2f) < 1e-6f && move.Channels.Count == 3, "the animation differs");
        Assert.True(move.Channels.Single(static channel => channel.Path == GltfChannelPath.Translation) is { Interpolation: GltfInterpolation.Step, Times: [0f, 1f] }
            && move.Channels.Single(static channel => channel.Path == GltfChannelPath.Scale).Interpolation == GltfInterpolation.Linear,
            "the step or linear channel differs");
        var rotation = move.Channels.Single(static channel => channel.Path == GltfChannelPath.Rotation);
        Assert.True(rotation is { Interpolation: GltfInterpolation.CubicSpline, Node: 1, Times.Length: 2, Values.Length: 6 }
            && MathF.Abs(rotation.Values[4].Y - MathF.Sin(0.5f)) < 1e-5f, "the cubic rotation keys differ");
    }

    private static Task HandWrittenAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-gltf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var model = GltfModelReader.Read(WriteHandWritten(directory, badIndex: false));
            Assert.True(model.Nodes[0] is { Name: "Leaf", Parent: 2 } && model.Nodes[2].Parent == 1, "the hierarchy listed child first differs");
            var leaf = model.Primitives.Single(static primitive => !primitive.Skinned);
            Assert.True(leaf.Vertices.Length == 3 && leaf.Vertices.All(static vertex => vertex.Normal == Microsoft.Xna.Framework.Vector3.UnitZ),
                "the rigid triangle without normals is not flat");
            Assert.True(model.Bounds.Min.Y == -100f && model.Bounds.Max.X == 11f && model.Bounds.Max.Z == 5f && model.Bounds.Max.Y == 3f
                && model.Bounds.Min.Z == 0f, $"the rest bounds differ: {model.Bounds}");
            Assert.True(model.Skins is [{ Joints: [4, 5, 6, 7, 8] } skin] && skin.InverseBinds.All(static bind => bind == Microsoft.Xna.Framework.Matrix.Identity),
                "the skin without inverse binds differs");

            var body = model.Primitives.Single(static primitive => primitive.Skinned).Vertices;
            Assert.True(body[0].Joints.X == 4f && MathF.Abs(body[0].Weights.X - 0.6f / 0.9f) < 1e-5f
                && MathF.Abs(body[0].Weights.Y - 0.1f / 0.9f) < 1e-5f, $"the four largest of eight weights differ: {body[0].Joints} {body[0].Weights}");
            Assert.True(body[1].Weights == Microsoft.Xna.Framework.Vector4.UnitX && body[1].Joints.X == 0f && body[2].Joints.X == 4f
                && body[2].Weights.X == 1f, "an unweighted vertex does not follow its first joint");

            var material = model.Materials.Single();
            Assert.True(material.BaseColor == new Microsoft.Xna.Framework.Vector4(0.2f, 0.4f, 0.6f, 1f) && material.Metallic == 0f
                && MathF.Abs(material.Roughness - 0.75f) < 1e-6f, "the spec-gloss material differs");

            var refused = false;
            try
            {
                GltfModelReader.Read(WriteHandWritten(directory, badIndex: true));
            }
            catch (InvalidDataException)
            {
                refused = true;
            }

            Assert.True(refused, "a triangle past its vertices was read");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    // Nodes: Leaf (child of Mid, listed first), Root, Mid, the skinned Body, joints J0..J4 (J4 at 1, 2, 3).
    private static string WriteHandWritten(string directory, bool badIndex)
    {
        var data = new MemoryStream();
        var writer = new BinaryWriter(data);
        var views = new List<(int Offset, int Length)>();
        void View(Action write)
        {
            var start = (int)data.Position;
            write();
            views.Add((start, (int)data.Position - start));
        }

        void Floats(params float[] values)
        {
            foreach (var value in values)
            {
                writer.Write(value);
            }
        }

        View(() => Floats(0, 0, 0, 1, 0, 0, 0, 1, 0));
        View(() => Floats(0, 0, 0, 1, 0, 0, 0, 1, 0));
        View(() => writer.Write(new byte[] { 0, 1, 2, 3, 0, 0, 0, 0, 4, 0, 0, 0 }));
        View(() => Floats(0.1f, 0.1f, 0.1f, 0.1f, 0, 0, 0, 0, 1, 0, 0, 0));
        View(() => writer.Write(new byte[] { 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        View(() => Floats(0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        View(() => { writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)7); });
        File.WriteAllBytes(Path.Combine(directory, "data.bin"), data.ToArray());

        var viewJson = string.Join(",", views.Select(static view => $$"""{"buffer":0,"byteOffset":{{view.Offset}},"byteLength":{{view.Length}}}"""));
        var leafMesh = badIndex
            ? """{"primitives":[{"attributes":{"POSITION":0},"indices":6}]}"""
            : """{"primitives":[{"attributes":{"POSITION":0}}]}""";
        var json = $$$$"""
            {
              "asset":{"version":"2.0"},
              "extensionsUsed":["KHR_materials_pbrSpecularGlossiness"],
              "scene":0,
              "scenes":[{"nodes":[1,3,4,5,6,7,8]}],
              "nodes":[
                {"name":"Leaf","mesh":0,"translation":[10,0,0]},
                {"name":"Root","children":[2],"translation":[0,-100,0]},
                {"name":"Mid","children":[0],"translation":[0,0,5]},
                {"name":"Body","mesh":1,"skin":0},
                {"name":"J0"},{"name":"J1"},{"name":"J2"},{"name":"J3"},
                {"name":"J4","translation":[1,2,3]}
              ],
              "skins":[{"joints":[4,5,6,7,8]}],
              "meshes":[
                {{{{leafMesh}}}},
                {"primitives":[{"attributes":{"POSITION":1,"JOINTS_0":2,"WEIGHTS_0":3,"JOINTS_1":4,"WEIGHTS_1":5},"material":0}]}
              ],
              "materials":[{"extensions":{"KHR_materials_pbrSpecularGlossiness":{"diffuseFactor":[0.2,0.4,0.6,1],"glossinessFactor":0.25}}}],
              "accessors":[
                {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
                {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
                {"bufferView":2,"componentType":5121,"count":3,"type":"VEC4"},
                {"bufferView":3,"componentType":5126,"count":3,"type":"VEC4"},
                {"bufferView":4,"componentType":5121,"count":3,"type":"VEC4"},
                {"bufferView":5,"componentType":5126,"count":3,"type":"VEC4"},
                {"bufferView":6,"componentType":5123,"count":3,"type":"SCALAR"}
              ],
              "bufferViews":[{{{{viewJson}}}}],
              "buffers":[{"uri":"data.bin","byteLength":{{{{data.Length}}}}}]
            }
            """;
        var path = Path.Combine(directory, badIndex ? "bad.gltf" : "model.gltf");
        File.WriteAllText(path, json);
        return path;
    }

    private static Task TestModelsAsync()
    {
        if (TestAssets.Find(@"gltf\Solus_the_knight.glb") is not { } knightPath)
        {
            Console.WriteLine("  (skipped: no tests/assets/gltf)");
            return Task.CompletedTask;
        }

        var knight = GltfModelReader.Read(knightPath);
        Assert.True(knight.Skins is [{ Joints.Length: 75 }] && knight.Animations.Count == 21
            && knight.Animations.Any(static clip => clip.Channels.Any(static channel => channel.Interpolation == GltfInterpolation.Step)),
            "the knight's skin or clips differ");
        var orc = GltfModelReader.Read(TestAssets.Find(@"gltf\FrostfangOrcShaman.glb")!);
        Assert.True(orc.Skins is [{ Joints.Length: 28 }] && orc.Primitives.All(static primitive => primitive.Vertices.Any(static vertex => vertex.Tangent.W != 0f)),
            "the orc's skin or tangents differ");
        // The tangents GltfTangents derives agree with the ones the orc's exporter wrote.
        var authored = orc.Primitives[0].Vertices;
        var derived = (GltfVertex[])authored.Clone();
        GltfTangents.Generate(derived, orc.Primitives[0].Indices, uvSet: 0);
        var agree = Enumerable.Range(0, authored.Length).Count(index => authored[index].Tangent.W == derived[index].Tangent.W
            && Microsoft.Xna.Framework.Vector4.Dot(authored[index].Tangent with { W = 0 }, derived[index].Tangent with { W = 0 }) > 0.7f);
        Assert.True(agree > authored.Length * 0.9, $"derived tangents agree with the authored ones on {agree} of {authored.Length} vertices");
        var pbr = GltfModelReader.Read(TestAssets.Find(@"gltf\base_basic_pbr.glb")!);
        Assert.True(pbr.Skins.Count == 0 && pbr.Materials is [{ NormalTexture.IsSet: true, MetallicRoughnessTexture.IsSet: true }],
            "the PBR prop's maps differ");
        var shaded = GltfModelReader.Read(TestAssets.Find(@"gltf\base_basic_shaded.glb")!);
        Assert.True(shaded.Materials is [{ EmissiveTexture.IsSet: true }], "the shaded prop's emissive map differs");
        return Task.CompletedTask;
    }

    // A quad in the XY plane facing +Z: U grows along +X; V along +Y, or along -Y when mirrored.
    private static Task TangentsAsync()
    {
        foreach (var (mirrored, sign) in new[] { (false, -1f), (true, 1f) })
        {
            var v = mirrored ? -1f : 1f;
            var vertices = new[] { (0f, 0f), (1f, 0f), (1f, 1f), (0f, 1f) }
                .Select(corner => new GltfVertex
                {
                    Position = new Microsoft.Xna.Framework.Vector3(corner.Item1, corner.Item2, 0),
                    Normal = Microsoft.Xna.Framework.Vector3.UnitZ,
                    Uv1 = new Microsoft.Xna.Framework.Vector2(corner.Item1, corner.Item2 * v)
                })
                .ToArray();
            GltfTangents.Generate(vertices, [0, 1, 2, 0, 2, 3], uvSet: 1);
            Assert.True(vertices.All(vertex => Microsoft.Xna.Framework.Vector3.Distance(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z),
                Microsoft.Xna.Framework.Vector3.UnitX) < 1e-5f && vertex.Tangent.W == sign),
                $"the tangents of the {(mirrored ? "mirrored" : "plain")} quad differ: {vertices[0].Tangent}");
        }

        return Task.CompletedTask;
    }

    private static Task MythcreatureAsync()
    {
        if (PristonTale.Assets.AssetFiles.FindAssetsRoot() is not { } root
            || PristonTale.Assets.AssetFiles.ResolveFile(root, @"char\npc\Mythcreature\Mythcreature.glb") is not { } path)
        {
            Console.WriteLine("  (skipped: no Mythcreature GLB in the assets)");
            return Task.CompletedTask;
        }

        var model = GltfModelReader.Read(path);
        Assert.True(model.Primitives.Count == 8 && model.Primitives.All(static primitive => primitive.Skinned), "the primitives differ");
        Assert.True(model.Skins is [{ Joints.Length: 145 }] && model.Animations is [{ Name: "Arachne" } animation] && animation.DurationSeconds > 33f,
            "the skin or the animation differs");
        Assert.True(model.Materials.Count == 8 && model.Materials.Count(static material => material.Alpha == GltfAlphaMode.Blend) == 2
            && model.Materials.Single(static material => material.Name == "MAT_Eyes").EmissiveTexture.IsSet, "the materials differ");
        Assert.True(model.Images.Count == 18 && model.Primitives.All(static primitive => primitive.Vertices.All(static vertex =>
            MathF.Abs(vertex.Weights.X + vertex.Weights.Y + vertex.Weights.Z + vertex.Weights.W - 1f) < 1e-3f)), "the images or weights differ");
        return Task.CompletedTask;
    }
}
