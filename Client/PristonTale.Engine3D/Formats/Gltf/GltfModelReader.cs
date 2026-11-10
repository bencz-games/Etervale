using Microsoft.Xna.Framework;
using PristonTale.Assets;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace PristonTale.Engine3D.Formats.Gltf;

// Reads a glTF 2.0 model through SharpGLTF into GltfModel. Every file, the .gltf's external buffers and images too, is
// found through AssetFiles. SharpGLTF's validation is skipped: it refuses files whose faults are cosmetic (a byte
// stride on animation data) or that this reader repairs, while the reader itself refuses data that would read out of
// range. Skin weights are normalized to sum 1, keeping the four largest of up to eight; a primitive without normals
// takes flat ones (its vertices split per triangle) and drops its tangents, as glTF requires; missing tangents stay
// zero for the renderer to derive. Spec-gloss materials take their diffuse as base colour, unmetallic, with roughness
// 1 - glossiness.
public static class GltfModelReader
{
    private const int UvSets = 2;

    public static GltfModel Read(string path)
    {
        var resolved = AssetFiles.Resolve(path);
        var directory = Path.GetDirectoryName(resolved) ?? "";
        var context = ReadContext.Create(name => AssetFiles.ReadAllBytes(Path.Combine(directory, Uri.UnescapeDataString(name))));
        context.Validation = ValidationMode.Skip;
        var root = context.ReadSchema2(Path.GetFileName(resolved));

        var nodes = root.LogicalNodes.Select(node => Node(node, resolved)).ToArray();
        var world = WorldMatrices(nodes);
        var skins = new List<Skin>();
        var primitives = new List<GltfPrimitive>();
        foreach (var node in SceneNodes(root))
        {
            if (node.Mesh is null)
            {
                continue;
            }

            var skin = -1;
            if (node.Skin is { } used)
            {
                skin = skins.IndexOf(used);
                if (skin < 0)
                {
                    skins.Add(used);
                    skin = skins.Count - 1;
                }
            }

            foreach (var primitive in node.Mesh.Primitives)
            {
                if (Primitive(node, primitive, skin, node.Skin?.JointsCount ?? 0, resolved) is { } read)
                {
                    primitives.Add(read);
                }
            }
        }

        var readSkins = skins.Select(Skin).ToArray();
        return new GltfModel(
            resolved,
            nodes,
            primitives,
            [.. root.LogicalMaterials.Select(Material)],
            [.. root.LogicalImages.Select(static image => new GltfImage(image.Content.MimeType ?? "", image.Content.Content.ToArray()))],
            readSkins,
            [.. root.LogicalAnimations.Select(Animation)],
            Bounds(world, readSkins, primitives));
    }

    // The nodes of the default scene (every node when the file names none), parents before children.
    private static IEnumerable<Node> SceneNodes(ModelRoot root)
    {
        if (root.DefaultScene is not { } scene)
        {
            foreach (var node in root.LogicalNodes)
            {
                yield return node;
            }

            yield break;
        }

        var pending = new Stack<Node>(scene.VisualChildren);
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.VisualChildren)
            {
                pending.Push(child);
            }
        }
    }

    private static GltfNode Node(Node node, string path)
    {
        var transform = node.LocalTransform;
        System.Numerics.Vector3 scale;
        System.Numerics.Quaternion rotation;
        System.Numerics.Vector3 translation;
        if (transform.IsSRT)
        {
            (scale, rotation, translation) = (transform.Scale, transform.Rotation, transform.Translation);
        }
        else if (!System.Numerics.Matrix4x4.Decompose(node.LocalMatrix, out scale, out rotation, out translation))
        {
            throw new InvalidDataException($"{path}: node {node.LogicalIndex} has a sheared matrix, which does not decompose.");
        }

        return new GltfNode(node.Name ?? "", node.VisualParent?.LogicalIndex ?? -1, translation, rotation, scale);
    }

    private static Matrix[] WorldMatrices(GltfNode[] nodes)
    {
        var world = new Matrix[nodes.Length];
        var done = new bool[nodes.Length];
        for (var index = 0; index < nodes.Length; index++)
        {
            WorldOf(nodes, index, world, done);
        }

        return world;
    }

    private static Matrix WorldOf(GltfNode[] nodes, int index, Matrix[] world, bool[] done)
    {
        if (!done[index])
        {
            var parent = nodes[index].Parent;
            world[index] = parent < 0 ? nodes[index].LocalMatrix : nodes[index].LocalMatrix * WorldOf(nodes, parent, world, done);
            done[index] = true;
        }

        return world[index];
    }

    // Inverse binds the file leaves out are the identity.
    private static GltfSkin Skin(Skin skin)
    {
        var joints = new int[skin.JointsCount];
        var inverseBinds = new Matrix[skin.JointsCount];
        var stored = skin.InverseBindMatrices;
        for (var index = 0; index < joints.Length; index++)
        {
            joints[index] = skin.Joints[index].LogicalIndex;
            inverseBinds[index] = index < stored.Count ? stored[index] : Matrix.Identity;
        }

        return new GltfSkin(joints, inverseBinds);
    }

    private static GltfPrimitive Primitive(Node node, MeshPrimitive primitive, int skin, int jointCount, string path)
    {
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions is null)
        {
            return null;
        }

        var count = positions.Count;
        var indices = new List<int>();
        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count)
            {
                throw new InvalidDataException($"{path}: a triangle of mesh {node.Mesh.LogicalIndex} indexes past its {count} vertices.");
            }

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        if (indices.Count == 0)
        {
            return null;
        }

        var normals = Attribute(primitive, "NORMAL", count, path)?.AsVector3Array();
        var tangents = normals is null ? null : Attribute(primitive, "TANGENT", count, path)?.AsVector4Array();
        var uv0 = Attribute(primitive, "TEXCOORD_0", count, path)?.AsVector2Array();
        var uv1 = Attribute(primitive, "TEXCOORD_1", count, path)?.AsVector2Array();
        var colors = Attribute(primitive, "COLOR_0", count, path)?.AsColorArray(1f);
        var joints0 = skin < 0 ? null : Attribute(primitive, "JOINTS_0", count, path)?.AsVector4Array();
        var weights0 = skin < 0 ? null : Attribute(primitive, "WEIGHTS_0", count, path)?.AsVector4Array();
        var joints1 = joints0 is null ? null : Attribute(primitive, "JOINTS_1", count, path)?.AsVector4Array();
        var weights1 = joints1 is null ? null : Attribute(primitive, "WEIGHTS_1", count, path)?.AsVector4Array();
        if (joints0 is null || weights0 is null)
        {
            skin = -1;
        }

        var vertices = new GltfVertex[count];
        Span<(float Weight, float Joint)> influences = stackalloc (float, float)[8];
        for (var index = 0; index < count; index++)
        {
            ref var vertex = ref vertices[index];
            vertex.Position = positions[index];
            vertex.Normal = normals is null ? Vector3.Zero : normals[index];
            vertex.Tangent = tangents is null ? Vector4.Zero : tangents[index];
            vertex.Uv0 = uv0 is null ? Vector2.Zero : uv0[index];
            vertex.Uv1 = uv1 is null ? Vector2.Zero : uv1[index];
            vertex.Color = colors is null ? Vector4.One : colors[index];
            if (skin >= 0)
            {
                (vertex.Joints, vertex.Weights) = Influences(joints0[index], weights0[index], joints1?[index], weights1?[index], jointCount, influences, path);
            }
        }

        if (normals is null)
        {
            (vertices, indices) = FlatShaded(vertices, indices);
        }

        return new GltfPrimitive(node.LogicalIndex, primitive.Material?.LogicalIndex ?? -1, skin, vertices, [.. indices]);
    }

    private static Accessor Attribute(MeshPrimitive primitive, string name, int count, string path)
    {
        var accessor = primitive.GetVertexAccessor(name);
        if (accessor is not null && accessor.Count != count)
        {
            throw new InvalidDataException($"{path}: {name} of mesh {primitive.LogicalParent.LogicalIndex} has {accessor.Count} values for {count} vertices.");
        }

        return accessor;
    }

    // The four largest weights of up to eight, summing to 1; a vertex weighed by none follows its first joint (the skin's
    // first when that is out of range).
    private static (Vector4 Joints, Vector4 Weights) Influences(System.Numerics.Vector4 joints0, System.Numerics.Vector4 weights0,
        System.Numerics.Vector4? joints1, System.Numerics.Vector4? weights1, int jointCount, Span<(float Weight, float Joint)> influences, string path)
    {
        influences[0] = (weights0.X, joints0.X);
        influences[1] = (weights0.Y, joints0.Y);
        influences[2] = (weights0.Z, joints0.Z);
        influences[3] = (weights0.W, joints0.W);
        var used = 4;
        if (joints1 is { } j && weights1 is { } w)
        {
            influences[4] = (w.X, j.X);
            influences[5] = (w.Y, j.Y);
            influences[6] = (w.Z, j.Z);
            influences[7] = (w.W, j.W);
            used = 8;
        }

        var set = influences[..used];
        foreach (var (weight, joint) in set)
        {
            if (weight > 0f && joint >= jointCount)
            {
                throw new InvalidDataException($"{path}: a vertex is weighed by joint {joint} of a skin with {jointCount}.");
            }
        }

        set.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));
        var sum = set[0].Weight + set[1].Weight + set[2].Weight + set[3].Weight;
        var jointsOut = new Vector4(set[0].Joint, set[1].Joint, set[2].Joint, set[3].Joint);
        return sum > 0f
            ? (jointsOut, new Vector4(set[0].Weight, set[1].Weight, set[2].Weight, set[3].Weight) / sum)
            : (new Vector4(joints0.X < jointCount ? joints0.X : 0f, 0, 0, 0), Vector4.UnitX);
    }

    // Three vertices per triangle, each with the triangle's normal and no tangent.
    private static (GltfVertex[] Vertices, List<int> Indices) FlatShaded(GltfVertex[] shared, List<int> indices)
    {
        var vertices = new GltfVertex[indices.Count];
        var flat = new List<int>(indices.Count);
        for (var index = 0; index < indices.Count; index += 3)
        {
            var (a, b, c) = (shared[indices[index]], shared[indices[index + 1]], shared[indices[index + 2]]);
            var face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            var normal = face.LengthSquared() > 0f ? Vector3.Normalize(face) : Vector3.UnitY;
            vertices[index] = a with { Normal = normal, Tangent = Vector4.Zero };
            vertices[index + 1] = b with { Normal = normal, Tangent = Vector4.Zero };
            vertices[index + 2] = c with { Normal = normal, Tangent = Vector4.Zero };
            flat.Add(index);
            flat.Add(index + 1);
            flat.Add(index + 2);
        }

        return (vertices, flat);
    }

    private static GltfMaterial Material(Material material)
    {
        var read = new GltfMaterial(material.Name ?? "")
        {
            Alpha = material.Alpha switch
            {
                AlphaMode.MASK => GltfAlphaMode.Mask,
                AlphaMode.BLEND => GltfAlphaMode.Blend,
                _ => GltfAlphaMode.Opaque
            },
            AlphaCutoff = material.AlphaCutoff,
            DoubleSided = material.DoubleSided,
            Unlit = material.Unlit
        };

        if (material.FindChannel("BaseColor") is { } baseColor)
        {
            read = read with { BaseColor = baseColor.Color, BaseColorTexture = Texture(baseColor) };
        }
        else if (material.FindChannel("Diffuse") is { } diffuse)
        {
            var glossiness = material.FindChannel("SpecularGlossiness")?.GetFactor("GlossinessFactor") ?? 1f;
            read = read with { BaseColor = diffuse.Color, BaseColorTexture = Texture(diffuse), Metallic = 0f, Roughness = 1f - glossiness };
        }

        if (material.FindChannel("MetallicRoughness") is { } metallicRoughness)
        {
            read = read with
            {
                Metallic = metallicRoughness.GetFactor("MetallicFactor"),
                Roughness = metallicRoughness.GetFactor("RoughnessFactor"),
                MetallicRoughnessTexture = Texture(metallicRoughness)
            };
        }

        if (material.FindChannel("Normal") is { } normal)
        {
            read = read with { NormalScale = normal.GetFactor("NormalScale"), NormalTexture = Texture(normal) };
        }

        if (material.FindChannel("Occlusion") is { } occlusion)
        {
            read = read with { OcclusionStrength = occlusion.GetFactor("OcclusionStrength"), OcclusionTexture = Texture(occlusion) };
        }

        if (material.FindChannel("Emissive") is { } emissive)
        {
            read = read with
            {
                Emissive = Rgb(emissive),
                EmissiveStrength = emissive.GetFactor("EmissiveStrength"),
                EmissiveTexture = Texture(emissive)
            };
        }

        if (material.FindChannel("ClearCoat") is { } clearcoat)
        {
            read = read with { Clearcoat = clearcoat.GetFactor("ClearCoatFactor"), ClearcoatTexture = Texture(clearcoat) };
        }

        if (material.FindChannel("ClearCoatRoughness") is { } clearcoatRoughness)
        {
            read = read with
            {
                ClearcoatRoughness = clearcoatRoughness.GetFactor("RoughnessFactor"),
                ClearcoatRoughnessTexture = Texture(clearcoatRoughness)
            };
        }

        if (material.FindChannel("ClearCoatNormal") is { } clearcoatNormal)
        {
            read = read with { ClearcoatNormalScale = clearcoatNormal.GetFactor("NormalScale"), ClearcoatNormalTexture = Texture(clearcoatNormal) };
        }

        if (material.FindChannel("SheenColor") is { } sheenColor)
        {
            read = read with { SheenColor = Rgb(sheenColor), SheenColorTexture = Texture(sheenColor) };
        }

        if (material.FindChannel("SheenRoughness") is { } sheenRoughness)
        {
            read = read with { SheenRoughness = sheenRoughness.GetFactor("RoughnessFactor"), SheenRoughnessTexture = Texture(sheenRoughness) };
        }

        if (material.FindChannel("DiffuseTransmissionFactor") is { } transmission)
        {
            read = read with
            {
                DiffuseTransmission = transmission.GetFactor("DiffuseTransmissionFactor"),
                DiffuseTransmissionTexture = Texture(transmission)
            };
        }

        if (material.FindChannel("DiffuseTransmissionColor") is { } transmissionColor)
        {
            read = read with { DiffuseTransmissionColor = Rgb(transmissionColor), DiffuseTransmissionColorTexture = Texture(transmissionColor) };
        }

        return read;
    }

    private static Vector3 Rgb(MaterialChannel channel)
    {
        var color = channel.Color;
        return new Vector3(color.X, color.Y, color.Z);
    }

    // The texture's PNG or JPEG image: the primary one, or its fallback when an extension put another format first.
    private static GltfTextureRef Texture(MaterialChannel channel)
    {
        if (channel.Texture is not { } texture)
        {
            return GltfTextureRef.None;
        }

        var image = Decodable(texture.PrimaryImage) ?? Decodable(texture.FallbackImage)
            ?? throw new InvalidDataException($"{channel.LogicalParent.Name}: texture {texture.LogicalIndex} has no PNG or JPEG image.");
        var transform = channel.TextureTransform;
        var texCoord = transform?.TextureCoordinateOverride ?? channel.TextureCoordinate;
        if (texCoord >= UvSets)
        {
            throw new InvalidDataException($"{channel.LogicalParent.Name}: texture {texture.LogicalIndex} reads UV set {texCoord}; {UvSets} are read.");
        }

        var sampler = texture.Sampler;
        return new GltfTextureRef(
            image.LogicalIndex,
            texCoord,
            Wrap(sampler?.WrapS),
            Wrap(sampler?.WrapT),
            transform?.Offset ?? Vector2.Zero,
            transform?.Scale ?? Vector2.One,
            transform?.Rotation ?? 0f);
    }

    private static Image Decodable(Image image) => image is not null && (image.Content.IsPng || image.Content.IsJpg) ? image : null;

    private static GltfWrap Wrap(TextureWrapMode? mode) => mode switch
    {
        TextureWrapMode.CLAMP_TO_EDGE => GltfWrap.Clamp,
        TextureWrapMode.MIRRORED_REPEAT => GltfWrap.Mirror,
        _ => GltfWrap.Repeat
    };

    private static GltfAnimation Animation(Animation animation)
    {
        var channels = new List<GltfChannel>();
        foreach (var channel in animation.Channels)
        {
            if (channel.TargetNode is not { } node)
            {
                continue;
            }

            var read = channel.TargetNodePath switch
            {
                PropertyPath.translation => Channel(node, GltfChannelPath.Translation, channel.GetTranslationSampler(), static value => new Vector4(value, 0f)),
                PropertyPath.scale => Channel(node, GltfChannelPath.Scale, channel.GetScaleSampler(), static value => new Vector4(value, 0f)),
                PropertyPath.rotation => Channel(node, GltfChannelPath.Rotation, channel.GetRotationSampler(),
                    static value => new Vector4(value.X, value.Y, value.Z, value.W)),
                _ => null
            };

            if (read is not null)
            {
                channels.Add(read);
            }
        }

        return new GltfAnimation(animation.Name ?? "", animation.Duration, channels);
    }

    private static GltfChannel Channel<T>(Node node, GltfChannelPath path, IAnimationSampler<T> sampler, Func<T, Vector4> value)
    {
        if (sampler is null)
        {
            return null;
        }

        var times = new List<float>();
        var values = new List<Vector4>();
        var interpolation = sampler.InterpolationMode switch
        {
            AnimationInterpolationMode.STEP => GltfInterpolation.Step,
            AnimationInterpolationMode.CUBICSPLINE => GltfInterpolation.CubicSpline,
            _ => GltfInterpolation.Linear
        };

        if (interpolation == GltfInterpolation.CubicSpline)
        {
            foreach (var (time, (incoming, key, outgoing)) in sampler.GetCubicKeys())
            {
                times.Add(time);
                values.Add(value(incoming));
                values.Add(value(key));
                values.Add(value(outgoing));
            }
        }
        else
        {
            foreach (var (time, key) in sampler.GetLinearKeys())
            {
                times.Add(time);
                values.Add(value(key));
            }
        }

        return times.Count == 0 ? null : new GltfChannel(node.LogicalIndex, path, interpolation, [.. times], [.. values]);
    }

    // The extent drawn at rest: a skinned vertex at the weighted sum of its joints' rest transforms
    // (inverse bind x joint world), a rigid one placed by its node.
    private static BoundingBox Bounds(Matrix[] world, GltfSkin[] skins, List<GltfPrimitive> primitives)
    {
        var joints = skins.Select(skin => skin.Joints.Select((node, index) => skin.InverseBinds[index] * world[node]).ToArray()).ToArray();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var primitive in primitives)
        {
            foreach (var vertex in primitive.Vertices)
            {
                var position = primitive.Skinned ? Skinned(vertex, joints[primitive.Skin]) : Vector3.Transform(vertex.Position, world[primitive.Node]);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
        }

        return primitives.Count == 0 ? default : new BoundingBox(min, max);
    }

    private static Vector3 Skinned(in GltfVertex vertex, Matrix[] joints) =>
        Vector3.Transform(vertex.Position, joints[(int)vertex.Joints.X]) * vertex.Weights.X
        + Vector3.Transform(vertex.Position, joints[(int)vertex.Joints.Y]) * vertex.Weights.Y
        + Vector3.Transform(vertex.Position, joints[(int)vertex.Joints.Z]) * vertex.Weights.Z
        + Vector3.Transform(vertex.Position, joints[(int)vertex.Joints.W]) * vertex.Weights.W;
}
