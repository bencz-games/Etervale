using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Formats.Gltf;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Gltf;

namespace PristonTale.Tests;

internal static class GltfRenderingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a glTF animator poses step, linear (rotations by slerp) and cubic-spline channels, holds the ends, keeps the rest "
            + "of what a clip does not animate, places children by their parents listed after them, and builds skin matrices", PoseAsync);
        yield return ("a glTF material's maps pack into their channels, resampled to the largest of each texture, neutral where a map "
            + "is missing, a map read otherwise than the texture's first dropped, and no texture where the material has none of its maps",
            PackAsync);
        yield return ("a glTF texture transform maps UVs as SharpGLTF's KHR_texture_transform matrix does", TextureTransformAsync);
        yield return ("a glTF actor source splits a skinned primitive at 64 bones per batch keeping every triangle, gives a rigid "
            + "primitive its node, and lays its clips on the actor timeline from whole frames", SourceAsync);
    }

    private static GltfModel Model()
    {
        GltfNode[] nodes =
        [
            new("Child", 1, new Vector3(0, 1, 0), Quaternion.Identity, Vector3.One),
            new("Root", -1, Vector3.Zero, Quaternion.Identity, Vector3.One),
            new("Still", -1, new Vector3(7, 0, 0), Quaternion.Identity, Vector3.One)
        ];
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
        GltfChannel[] channels =
        [
            new(1, GltfChannelPath.Translation, GltfInterpolation.Linear, [0f, 2f], [Vector4.Zero, new Vector4(10, 0, 0, 0)]),
            new(1, GltfChannelPath.Scale, GltfInterpolation.Step, [0f, 1f], [new Vector4(1, 1, 1, 0), new Vector4(2, 2, 2, 0)]),
            new(0, GltfChannelPath.Rotation, GltfInterpolation.Linear, [0f, 1f],
                [new Vector4(0, 0, 0, 1), new Vector4(quarter.X, quarter.Y, quarter.Z, quarter.W)]),
            new(0, GltfChannelPath.Translation, GltfInterpolation.CubicSpline, [0f, 1f],
                [Vector4.Zero, new Vector4(0, 1, 0, 0), new Vector4(0, 4, 0, 0), Vector4.Zero, new Vector4(0, 3, 0, 0), Vector4.Zero])
        ];
        return new GltfModel("synthetic", nodes, [], [], [], [new GltfSkin([0], [Matrix.CreateTranslation(0, -1, 0)])],
            [new GltfAnimation("Walk", 2f, channels)], default);
    }

    private static Task PoseAsync()
    {
        var animator = new GltfAnimator(Model());
        Span<Matrix> skin = stackalloc Matrix[1];
        animator.SkinMatrices(0, skin);
        Assert.True(Near(skin[0], Matrix.Identity) && animator.NodeWorld[2].Translation == new Vector3(7, 0, 0),
            "the rest pose's skin matrix is not the identity or a still node moved");

        animator.Pose(0, 0.5f);
        var root = animator.NodeWorld[1];
        var child = animator.NodeWorld[0];
        Assert.True(Near(root.Translation, new Vector3(2.5f, 0, 0)) && Near(root.Right.Length(), 1f), $"linear translation or step scale at 0.5 s: {root}");
        // Cubic: (2t3 - 3t2 + 1) 1 + (t3 - 2t2 + t) 4 + (-2t3 + 3t2) 3 at t = 0.5 = 0.5 + 0.5 + 1.5.
        Assert.True(Near(child.Translation, new Vector3(2.5f, 2.5f, 0)), $"the cubic spline or the parent's place differs: {child.Translation}");
        Assert.True(Near(Vector3.TransformNormal(Vector3.UnitX, child), Vector3.Normalize(new Vector3(1, 0, -1))),
            "the rotation did not slerp to 45 degrees");

        animator.Pose(0, 1.5f);
        Assert.True(Near(animator.NodeWorld[1].Right.Length(), 2f) && Near(animator.NodeWorld[1].Translation, new Vector3(7.5f, 0, 0)),
            "the step did not hold its later key");
        animator.Pose(0, 9f);
        Assert.True(Near(animator.NodeWorld[1].Translation, new Vector3(10, 0, 0)) && Near(animator.NodeWorld[0].Translation, new Vector3(10, 6, 0)),
            "the last keys did not hold after the clip");
        animator.SkinMatrices(0, skin);
        Assert.True(Near(Vector3.Transform(new Vector3(0, 1, 0), skin[0]), animator.NodeWorld[0].Translation),
            "a bind-space vertex at the joint did not follow it");
        animator.Pose(-1, 0f);
        Assert.True(Near(animator.NodeWorld[0].Translation, new Vector3(0, 1, 0)), "the rest pose did not come back");
        return Task.CompletedTask;
    }

    private static Task PackAsync()
    {
        static DecodedPristonImage Solid(int size, Color color) => new(size, size, Enumerable.Repeat(color, size * size).ToArray(), true);
        DecodedPristonImage[] images =
        [
            Solid(2, new Color(9, 100, 200, 255)),
            Solid(1, new Color(50, 0, 0, 255)),
            Solid(4, new Color(255, 128, 0, 255)),
            Solid(1, new Color(30, 60, 90, 255)),
            Solid(2, new Color(0, 0, 0, 40))
        ];
        static GltfTextureRef Map(int image, int set = 0) => GltfTextureRef.None with { Image = image, TexCoord = set };
        var material = new GltfMaterial("Packed")
        {
            MetallicRoughnessTexture = Map(0, set: 1),
            OcclusionTexture = Map(1),
            NormalTexture = Map(2),
            SheenColorTexture = Map(3),
            DiffuseTransmissionTexture = Map(4)
        };
        var pack = GltfMaterialPacking.Pack(material, index => images[index]);

        // The occlusion map reads UV set 0, the metallic-roughness map (first in the surface texture) set 1: it is dropped.
        var surface = pack[GltfPackedTexture.Surface];
        Assert.True(surface is { Width: 2, Height: 2, Srgb: false } && surface.Read.TexCoord == 1 && surface.Levels[0][3] == new Color(255, 100, 200, 255)
            && surface.Levels.Length == 2, $"the surface texture differs: {surface?.Levels[0][3]}");
        // Read as the surface's, occlusion and clearcoat pack beside it; clearcoat roughness goes to the emissive alpha.
        var coated = GltfMaterialPacking.Pack(material with { OcclusionTexture = Map(1, set: 1), ClearcoatTexture = Map(1, set: 1),
            ClearcoatRoughnessTexture = Map(3) }, index => images[index]);
        Assert.True(coated[GltfPackedTexture.Surface].Levels[0][3] == new Color(50, 100, 200, 50),
            $"occlusion and clearcoat were not packed with the surface: {coated[GltfPackedTexture.Surface].Levels[0][3]}");
        Assert.True(coated[GltfPackedTexture.Emissive] is { Srgb: true } emissive && emissive.Levels[0][0] == new Color(255, 255, 255, 60),
            "the clearcoat roughness was not packed in the emissive alpha");
        var normals = pack[GltfPackedTexture.Normals];
        Assert.True(normals is { Width: 4 } && normals.Levels[0][0] == new Color(255, 128, 128, 128), "the normals texture differs");
        Assert.True(pack[GltfPackedTexture.Sheen] is { Srgb: true } sheen && sheen.Levels[0][0] == new Color(30, 60, 90, 255),
            "the sheen texture differs");
        Assert.True(pack[GltfPackedTexture.Transmission] is { Width: 2 } transmission && transmission.Levels[0][0] == new Color(255, 255, 255, 40),
            "the transmission texture differs");
        Assert.True(pack[GltfPackedTexture.BaseColor] is null && pack[GltfPackedTexture.Emissive] is null,
            "a texture without maps was packed");
        return Task.CompletedTask;
    }

    private static Task SourceAsync()
    {
        const int joints = 70;
        var nodes = Enumerable.Range(0, joints + 1).Select(static index => new GltfNode($"Node{index}", -1, Vector3.Zero, Quaternion.Identity, Vector3.One))
            .ToList();
        var skinned = new List<GltfVertex>();
        for (var joint = 0; joint < joints; joint++)
        {
            foreach (var corner in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY })
            {
                skinned.Add(new GltfVertex
                {
                    Position = corner + new Vector3(0, 0, joint),
                    Normal = Vector3.UnitZ,
                    Color = Vector4.One,
                    Joints = new Vector4(joint, 0, 0, 0),
                    Weights = Vector4.UnitX
                });
            }
        }

        GltfVertex[] rigid = [.. skinned.Take(3).Select(static vertex => vertex with { Joints = Vector4.Zero, Weights = Vector4.Zero })];
        var move = new GltfChannel(0, GltfChannelPath.Translation, GltfInterpolation.Linear, [0f, 0.5f], [Vector4.Zero, new Vector4(10, 0, 0, 0)]);
        var model = new GltfModel("synthetic", nodes,
            [new GltfPrimitive(-1, -1, 0, [.. skinned], [.. Enumerable.Range(0, skinned.Count)]), new GltfPrimitive(joints, -1, -1, rigid, [0, 1, 2])],
            [], [], [new GltfSkin([.. Enumerable.Range(0, joints)], [.. Enumerable.Repeat(Matrix.Identity, joints)])],
            [new GltfAnimation("Idle", 1f, []), new GltfAnimation("Walk", 0.5f, [move])],
            new BoundingBox(Vector3.Zero, new Vector3(joints, 1, 0)));
        var source = GltfActorSource.FromModel(model);

        var batches = source.Batches;
        var offset = source.SkinOffsets[0];
        Assert.True(batches.Length == 3 && batches[0].Bones.Length == PristonShaderLibrary.MaxBones && batches[1].Bones.Length == joints - PristonShaderLibrary.MaxBones
            && batches[0].Indices.Length + batches[1].Indices.Length == skinned.Count, $"the skinned primitive split as {string.Join(", ", batches.Select(static batch => batch.Bones.Length))}");
        foreach (var batch in batches.Take(2))
        {
            Assert.True(batch.Vertices.All(vertex => vertex.Bones.ToVector4().X < batch.Bones.Length
                    && batch.Bones[(int)vertex.Bones.ToVector4().X] == offset + (int)vertex.Position.Z),
                "a vertex's palette slot does not lead to its own joint");
        }

        Assert.True(batches[2] is { Bones: [joints] } && batches[2].Vertices.All(static vertex => vertex.Weights == Vector4.UnitX),
            "the rigid primitive does not follow its node");

        // Idle's 1 s takes ceil(30.3) frames, so Walk starts a frame later at 32.
        Assert.True(source.ClipStartFrames is [0, 32] && source.Skeleton.FrameRanges[1].StartFrame == 32 * GameUnits.FrameTicks,
            $"the clips start at {string.Join(", ", source.ClipStartFrames)}");
        var animator = new GltfTimelineAnimator(source);
        var walking = animator.Evaluate(32 * GameUnits.FrameTicks + 0.25f * GameUnits.FrameTicksPerSecond, 0f);
        Assert.True(Near(walking[0].Translation, new Vector3(5f * GltfActorSource.UnitsPerMetre, 0, 0)) && animator.ObjectCount == source.TransformCount,
            $"Walk at 0.25 s placed its node at {walking[0].Translation}");
        Assert.True(Near(animator.Evaluate(16 * GameUnits.FrameTicks, 0f)[0].Translation, Vector3.Zero), "Idle moved the node Walk animates");
        return Task.CompletedTask;
    }

    private static Task TextureTransformAsync()
    {
        var root = SharpGLTF.Schema2.ModelRoot.CreateModel();
        var material = root.CreateMaterial("Turned");
        material.InitializePBRMetallicRoughness();
        var channel = material.FindChannel("BaseColor")!.Value;
        channel.SetTexture(0, root.UseImage(new SharpGLTF.Memory.MemoryImage(GltfModelTests.Png)));
        foreach (var rotation in new[] { 0.5f, MathHelper.PiOver2, -2f })
        {
            var (offset, scale) = (new System.Numerics.Vector2(0.25f, -0.5f), new System.Numerics.Vector2(2f, 3f));
            channel.SetTransform(offset, scale, rotation);
            var expected = channel.TextureTransform.Matrix;
            var rows = GltfMaterialRuntime.Transform(GltfTextureRef.None with { Image = 0, Offset = offset, Scale = scale, Rotation = rotation });
            foreach (var uv in new[] { new Vector2(1f, 0f), new Vector2(0.3f, 0.7f) })
            {
                var want = System.Numerics.Vector2.Transform(new System.Numerics.Vector2(uv.X, uv.Y), expected);
                var got = new Vector2(Vector2.Dot(uv, new Vector2(rows.X, rows.Y)), Vector2.Dot(uv, new Vector2(rows.Z, rows.W))) + offset;
                Assert.True(Near(got.X, want.X) && Near(got.Y, want.Y), $"rotation {rotation}: {uv} maps to {got}, SharpGLTF to {want}");
            }
        }

        return Task.CompletedTask;
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-4f;

    private static bool Near(Matrix a, Matrix b)
    {
        for (var index = 0; index < 16; index++)
        {
            if (MathF.Abs(a[index] - b[index]) > 1e-4f)
            {
                return false;
            }
        }

        return true;
    }
}
