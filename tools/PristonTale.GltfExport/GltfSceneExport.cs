using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;

using StageMesh = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexColor1Texture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using StageVB = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexColor1Texture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using RigidMesh = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using RigidVB = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using SkinMesh = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexJoints4>;
using SkinVB = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexJoints4>;

namespace PristonTale.GltfExport;

internal sealed record ExportOptions(
    bool FlipWinding,
    bool FlipV,
    bool Recenter = false,
    float Scale = 1f,
    bool NoAnims = false,
    bool AnimationsOnly = false,
    uint JobMask = 0,
    bool StageObject = false,
    bool TPose = false)
{
    public float ClipTicksPerSecond => StageObject
        ? GltfSceneExport.StageObjectTicksPerSecond
        : GltfSceneExport.TicksPerSecond;

    public bool HasRootTransform => Recenter || Scale != 1f;

    public Matrix RootTransform(BoundingBox bounds)
    {
        var center = Recenter ? (bounds.Min + bounds.Max) * 0.5f : Vector3.Zero;
        return Matrix.CreateTranslation(-center) * Matrix.CreateScale(Scale);
    }
}

internal static class GltfSceneExport
{
    internal const float TicksPerSecond = GameUnits.FrameTicksPerSecond;

    // Animated stage objects advance at the renderer's rate and, after the first pass, wrap playback into the
    // [160, MaxFrame] range.
    internal const float StageObjectTicksPerSecond = PristonRenderer.StageObjectTicksPerSecond;
    private const int StageObjectLoopStartTicks = GameUnits.FrameTicks;

    private const int SampleStepTicks = GameUnits.FrameTicks;
    private const int MaxSurfacesPerMesh = 200;
    private const float TransformTolerance = 5e-3f;
    private const double CollapsedAxisScale = 1e-5;
    // Tighter than the glTF validator's TRS check so static node matrices always pass it.
    private const float StaticShearTolerance = 1e-5f;
    // Animated bones whose shear exceeds this are exported through an exact SVD carrier instead of TRS.
    private const float AnimatedShearTolerance = 1e-4f;
    private const string ExportRootName = "__PT_EXPORT_ROOT__";
    private const string TransformCarrierSuffix = "__PT_SCALE_TRANSLATION";

    public static SceneBuilder BuildStage(
        PristonStage stage,
        MaterialFactory materials,
        ExportOptions options)
    {
        var scene = new SceneBuilder();
        var normals = PristonStageNormals.Compute(stage);
        var root = options.RootTransform(stage.Bounds).ToAffineNumerics();
        var meshes = new List<StageMesh>();
        var materialMeshes = new Dictionary<MaterialBuilder, StageMesh>();
        StageMesh? currentMesh = null;
        var primitiveCount = 0;

        StageMesh MeshFor(MaterialBuilder material)
        {
            if (materialMeshes.TryGetValue(material, out var existing))
            {
                return existing;
            }

            if (currentMesh is null || primitiveCount >= MaxSurfacesPerMesh)
            {
                currentMesh = new StageMesh($"stage{meshes.Count}");
                meshes.Add(currentMesh);
                primitiveCount = 0;
            }

            materialMeshes.Add(material, currentMesh);
            primitiveCount++;
            return currentMesh;
        }

        for (var faceIndex = 0; faceIndex < stage.Faces.Count; faceIndex++)
        {
            var face = stage.Faces[faceIndex];
            if (!face.FitsIn(stage.Vertices.Count))
            {
                continue;
            }

            var sourceMaterial = face.MaterialIndex >= 0 && face.MaterialIndex < stage.Materials.Count
                ? stage.Materials[face.MaterialIndex]
                : null;
            if (sourceMaterial is null)
            {
                continue;
            }

            var material = materials.Create(sourceMaterial, face.MaterialIndex);
            if (material is null)
            {
                continue;
            }

            var textureCoordinates = TextureCoordinatesOf(stage.TexLinks, face.TexLinkIndex);
            var primitive = MeshFor(material).UsePrimitive(material);

            StageVB BuildVertex(int vertexIndex, int corner, Vector2 uv)
            {
                var vertex = stage.Vertices[vertexIndex];
                var normal = normals[faceIndex * 3 + corner];
                var color = vertex.Color;
                var exportedColor = new System.Numerics.Vector4(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f,
                    color.A / 255f);
                return new StageVB(
                    new VertexPositionNormal(
                        vertex.Position.ToNumerics(),
                        (normal == Vector3.Zero ? Vector3.Up : normal).ToNumerics()),
                    new VertexColor1Texture1(exportedColor, ConvertUv(uv, options)));
            }

            AddTriangle(
                primitive,
                BuildVertex(face.A, 0, textureCoordinates.A),
                BuildVertex(face.B, 1, textureCoordinates.B),
                BuildVertex(face.C, 2, textureCoordinates.C),
                options);
        }

        foreach (var mesh in meshes)
        {
            scene.AddRigidMesh(mesh, root);
        }

        return scene;
    }

    public static SceneBuilder BuildModel(
        PristonModel model,
        MaterialFactory materials,
        ExportOptions options)
    {
        var scene = new SceneBuilder();
        var animator = new PristonModelAnimator(model);
        var absoluteBind = Copy(animator.Evaluate(0f));
        var wrapper = options.HasRootTransform
            ? new NodeBuilder(ExportRootName)
            {
                LocalMatrix = options.RootTransform(model.Bounds).ToAffineNumerics()
            }
            : null;
        IReadOnlyList<ClipPlan> clips = model.IsAnimated && !options.NoAnims && model.LastFrame > 0
            ? [PlanModelClip(model.LastFrame, options)]
            : [];
        var tree = new NodeTree(model, absoluteBind, animator, clips, wrapper);
        var bindNormals = BuildSmoothNormals(model);

        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var modelObject = model.Objects[objectIndex];
            if (modelObject.Faces.Count == 0)
            {
                continue;
            }

            var mesh = new RigidMesh(tree.Names[objectIndex]);
            var emitted = false;
            foreach (var face in modelObject.Faces)
            {
                if (!face.FitsIn(modelObject.Vertices.Count))
                {
                    continue;
                }

                var sourceMaterial = face.MaterialIndex >= 0 && face.MaterialIndex < model.Materials.Count
                    ? model.Materials[face.MaterialIndex]
                    : null;
                if (sourceMaterial is null)
                {
                    continue;
                }

                var material = materials.Create(sourceMaterial, face.MaterialIndex);
                if (material is null)
                {
                    continue;
                }

                var textureCoordinates = TextureCoordinatesOf(modelObject.TexLinks, face.TexLinkIndex);
                var primitive = mesh.UsePrimitive(material);

                RigidVB BuildVertex(int vertexIndex, Vector2 uv)
                {
                    var position = modelObject.Vertices[vertexIndex].Position;
                    var normal = bindNormals[objectIndex][vertexIndex];
                    if (tree.VertexBake[objectIndex] is { } bake)
                    {
                        position = Vector3.Transform(position, bake);
                        normal = BakeNormal(normal, bake);
                    }

                    return new RigidVB(
                        new VertexPositionNormal(position.ToNumerics(), normal.ToNumerics()),
                        new VertexTexture1(ConvertUv(uv, options)));
                }

                AddTriangle(
                    primitive,
                    BuildVertex(face.A, textureCoordinates.A),
                    BuildVertex(face.B, textureCoordinates.B),
                    BuildVertex(face.C, textureCoordinates.C),
                    options);
                emitted = true;
            }

            if (emitted)
            {
                scene.AddRigidMesh(mesh, tree.Nodes[objectIndex]);
            }
        }

        tree.AddClips();
        return scene;
    }

    public static SceneBuilder BuildActor(
        PristonActor actor,
        MaterialFactory materials,
        ExportOptions options)
    {
        var skeleton = actor.Skeleton;
        if (skeleton.Objects.Count == 0)
        {
            throw new InvalidDataException("The actor skeleton has no bones.");
        }

        var scene = new SceneBuilder();
        var animator = new PristonModelAnimator(skeleton);
        var absoluteBind = Copy(animator.Evaluate(0f));
        if (options.TPose)
        {
            ApplyTPose(skeleton, absoluteBind);
        }

        var armatureRoot = new NodeBuilder(ExportRootName);
        var tree = new NodeTree(
            skeleton,
            absoluteBind,
            animator,
            options.NoAnims ? [] : PlanActorClips(actor, options),
            armatureRoot,
            options.Scale);
        var skeletonByName = BuildObjectNameIndex(skeleton);
        var lastSkeletonIndex = skeleton.Objects.Count - 1;
        var renderFilter = actor.RenderObjectNames.Count > 0
            ? new HashSet<string>(actor.RenderObjectNames, StringComparer.OrdinalIgnoreCase)
            : null;
        if (renderFilter is not null && !actor.Mesh.Objects.Any(modelObject => renderFilter.Contains(modelObject.Name)))
        {
            // The client draws nothing in this case (e.g. tmh-b10c names TmhB10c while its mesh holds TmhB09c).
            Console.Error.WriteLine(
                $"warning: INX model group [{string.Join(", ", actor.RenderObjectNames)}] matches no mesh object; exporting every object.");
            renderFilter = null;
        }
        if (options.AnimationsOnly)
        {
            scene.AddNode(armatureRoot);
            tree.AddClips();
            return scene;
        }

        var bindNormals = BuildSmoothNormals(actor.Mesh);
        var normalMatrices = new Matrix?[absoluteBind.Length];
        var mesh = new SkinMesh("mesh");
        var emitted = false;

        for (var objectIndex = 0; objectIndex < actor.Mesh.Objects.Count; objectIndex++)
        {
            var modelObject = actor.Mesh.Objects[objectIndex];
            if (renderFilter is not null && !renderFilter.Contains(modelObject.Name))
            {
                continue;
            }

            foreach (var face in modelObject.Faces)
            {
                if (!face.FitsIn(modelObject.Vertices.Count))
                {
                    continue;
                }

                var sourceMaterial = face.MaterialIndex >= 0 && face.MaterialIndex < actor.Mesh.Materials.Count
                    ? actor.Mesh.Materials[face.MaterialIndex]
                    : null;
                if (sourceMaterial is null)
                {
                    continue;
                }

                var material = materials.Create(sourceMaterial, face.MaterialIndex);
                if (material is null)
                {
                    continue;
                }

                var textureCoordinates = TextureCoordinatesOf(modelObject.TexLinks, face.TexLinkIndex);
                var primitive = mesh.UsePrimitive(material);

                SkinVB BuildVertex(int vertexIndex, Vector2 uv)
                {
                    var bone = EffectiveBone(
                        modelObject,
                        vertexIndex,
                        skeletonByName,
                        objectIndex,
                        lastSkeletonIndex);
                    var bind = absoluteBind[bone];
                    var position = Vector3.Transform(modelObject.Vertices[vertexIndex].Position, bind)
                        * options.Scale;
                    var normalMatrix = normalMatrices[bone] ??= Matrix.Transpose(Matrix.Invert(bind));
                    var normal = Vector3.TransformNormal(bindNormals[objectIndex][vertexIndex], normalMatrix);
                    if (normal.LengthSquared() > 1e-8f)
                    {
                        normal.Normalize();
                    }

                    return new SkinVB(
                        new VertexPositionNormal(position.ToNumerics(), normal.ToNumerics()),
                        new VertexTexture1(ConvertUv(uv, options)),
                        new VertexJoints4(bone));
                }

                AddTriangle(
                    primitive,
                    BuildVertex(face.A, textureCoordinates.A),
                    BuildVertex(face.B, textureCoordinates.B),
                    BuildVertex(face.C, textureCoordinates.C),
                    options);
                emitted = true;
            }
        }

        if (!emitted)
        {
            throw new InvalidDataException("The actor contains no renderable triangles.");
        }

        scene.AddSkinnedMesh(
            mesh,
            System.Numerics.Matrix4x4.Identity,
            tree.Nodes);

        tree.AddClips();
        return scene;
    }

    // Rebinds a Character Studio biped with horizontal arms. Clips store absolute node transforms, so
    // only the rest pose changes; siblings of the upper arm (shoulder guards) follow the arm swing.
    internal static void ApplyTPose(PristonModel skeleton, Matrix[] bind)
    {
        var byName = BuildObjectNameIndex(skeleton);
        var children = new List<int>[skeleton.Objects.Count];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = [];
        }

        for (var index = 0; index < skeleton.Objects.Count; index++)
        {
            if (byName.TryGetValue(skeleton.Objects[index].ParentName, out var parent) && parent != index)
            {
                children[parent].Add(index);
            }
        }

        if (!byName.TryGetValue("Bip01 L UpperArm", out var leftUpperArm)
            || !byName.TryGetValue("Bip01 R UpperArm", out var rightUpperArm))
        {
            throw new InvalidDataException("--t-pose requires a biped skeleton with 'Bip01 L/R UpperArm' bones.");
        }

        var lateral = bind[leftUpperArm].Translation - bind[rightUpperArm].Translation;
        lateral.Y = 0f;
        if (lateral.LengthSquared() < 1e-6f)
        {
            throw new InvalidDataException("--t-pose could not derive the skeleton's lateral axis.");
        }

        lateral.Normalize();
        StraightenArm(skeleton, bind, byName, children, "L", lateral);
        StraightenArm(skeleton, bind, byName, children, "R", -lateral);
    }

    private static void StraightenArm(
        PristonModel skeleton,
        Matrix[] bind,
        IReadOnlyDictionary<string, int> byName,
        List<int>[] children,
        string side,
        Vector3 target)
    {
        if (!byName.TryGetValue($"Bip01 {side} UpperArm", out var upperArm)
            || !byName.TryGetValue($"Bip01 {side} Forearm", out var forearm)
            || !byName.TryGetValue($"Bip01 {side} Hand", out var hand))
        {
            throw new InvalidDataException($"--t-pose requires the 'Bip01 {side} UpperArm/Forearm/Hand' chain.");
        }

        var upperArmRotation = AlignBone(bind, children, upperArm, bind[forearm].Translation, target);
        if (byName.TryGetValue(skeleton.Objects[upperArm].ParentName, out var clavicle))
        {
            foreach (var sibling in children[clavicle])
            {
                if (sibling != upperArm)
                {
                    RotateSubtree(bind, children, sibling, upperArmRotation, bind[sibling].Translation);
                }
            }
        }

        AlignBone(bind, children, forearm, bind[hand].Translation, target);
        var fingers = children[hand]
            .Where(child => skeleton.Objects[child].Name.Contains("Finger", StringComparison.OrdinalIgnoreCase)
                && !skeleton.Objects[child].Name.EndsWith("Finger0", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (fingers.Count > 0)
        {
            var fingerCenter = fingers.Aggregate(Vector3.Zero, (sum, finger) => sum + bind[finger].Translation) / fingers.Count;
            AlignBone(bind, children, hand, fingerCenter, target);
        }
    }

    private static Matrix AlignBone(
        Matrix[] bind,
        List<int>[] children,
        int bone,
        Vector3 childPosition,
        Vector3 target)
    {
        var pivot = bind[bone].Translation;
        var direction = childPosition - pivot;
        if (direction.LengthSquared() < 1e-8f)
        {
            return Matrix.Identity;
        }

        direction.Normalize();
        var axis = Vector3.Cross(direction, target);
        var dot = Math.Clamp(Vector3.Dot(direction, target), -1f, 1f);
        if (axis.LengthSquared() < 1e-10f)
        {
            if (dot > 0f)
            {
                return Matrix.Identity;
            }

            axis = Vector3.Cross(direction, Vector3.Up).LengthSquared() > 1e-6f
                ? Vector3.Cross(direction, Vector3.Up)
                : Vector3.Cross(direction, Vector3.Forward);
        }

        axis.Normalize();
        var rotation = Matrix.CreateFromAxisAngle(axis, MathF.Acos(dot));
        RotateSubtree(bind, children, bone, rotation, pivot);
        return rotation;
    }

    private static void RotateSubtree(
        Matrix[] bind,
        List<int>[] children,
        int bone,
        Matrix rotation,
        Vector3 pivot)
    {
        var pivotRotation = Matrix.CreateTranslation(-pivot) * rotation * Matrix.CreateTranslation(pivot);
        var pending = new Stack<int>();
        pending.Push(bone);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            bind[current] *= pivotRotation;
            foreach (var child in children[current])
            {
                pending.Push(child);
            }
        }
    }

    private static IReadOnlyList<ClipPlan> PlanActorClips(PristonActor actor, ExportOptions options)
    {
        if (options.StageObject)
        {
            return actor.Skeleton.LastFrame > 0
                ? [PlanModelClip(actor.Skeleton.LastFrame, options)]
                : [];
        }

        return EnumerateClips(actor, options.JobMask)
            .Select(clip => new ClipPlan(clip.Name, clip.StartTicks, clip.EndTicks, true, options.ClipTicksPerSecond))
            .ToList();
    }

    private static ClipPlan PlanModelClip(int lastFrame, ExportOptions options)
    {
        var startTicks = options.StageObject && lastFrame > StageObjectLoopStartTicks
            ? StageObjectLoopStartTicks
            : 0;
        return new ClipPlan("Animation", startTicks, lastFrame, false, options.ClipTicksPerSecond);
    }

    // Exclusive clips hold their last source tick until EndTicks; inclusive clips sample EndTicks itself.
    private readonly record struct ClipPlan(
        string Name,
        int StartTicks,
        int EndTicks,
        bool Exclusive,
        float TicksPerSecond)
    {
        public int LastSourceTick => Exclusive ? EndTicks - 1 : EndTicks;
    }

    public static ModelRoot ToModel(SceneBuilder scene, ExportOptions options)
    {
        var model = scene.ToGltf2();
        if (options.AnimationsOnly)
        {
            AttachSkeletonSkin(model);
        }

        return model;
    }

    // Importers such as Blender only build an armature from skin joints; without a skin every bone
    // becomes an unrelated empty and the clips cannot be applied to the skinned armor exports.
    private static void AttachSkeletonSkin(ModelRoot model)
    {
        var root = model.LogicalNodes.Single(static node => node.Name == ExportRootName);
        var joints = model.LogicalNodes
            .Where(node => node != root
                && !(node.Name ?? string.Empty).Contains(TransformCarrierSuffix, StringComparison.Ordinal)
                && IsDescendantOf(node, root))
            .ToArray();
        if (joints.Length == 0)
        {
            throw new InvalidDataException("The actor skeleton has no exportable joints.");
        }

        model.CreateSkin("armature").BindJoints(System.Numerics.Matrix4x4.Identity, joints);
    }

    private static bool IsDescendantOf(Node node, Node ancestor)
    {
        for (var parent = node.VisualParent; parent is not null; parent = parent.VisualParent)
        {
            if (parent == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, int> BuildObjectNameIndex(PristonModel model)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < model.Objects.Count; index++)
        {
            var name = model.Objects[index].Name;
            if (name.Length > 0 && !result.ContainsKey(name))
            {
                result.Add(name, index);
            }
        }

        return result;
    }

    private static int EffectiveBone(
        PristonModelObject modelObject,
        int vertexIndex,
        IReadOnlyDictionary<string, int> skeletonByName,
        int objectIndex,
        int lastSkeletonIndex)
    {
        if (modelObject.HasPhysique && vertexIndex < modelObject.PhysiqueBoneNames.Count)
        {
            var boneName = modelObject.PhysiqueBoneNames[vertexIndex];
            if (!string.IsNullOrWhiteSpace(boneName)
                && skeletonByName.TryGetValue(boneName, out var boneIndex))
            {
                return Math.Min(boneIndex, lastSkeletonIndex);
            }
        }

        return Math.Min(objectIndex, lastSkeletonIndex);
    }

    private static IEnumerable<MotionClip> EnumerateClips(PristonActor actor, uint jobMask)
    {
        var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var actorInfo = actor.Info;
        foreach (var (index, motion) in actorInfo.UsableMotions())
        {

            // Same job filter as PristonMotionSelection: a zero mask on either side matches every class.
            if (motion.JobCodeBit != 0 && jobMask != 0 && (motion.JobCodeBit & jobMask) == 0)
            {
                continue;
            }

            // Motion record frames are signed in the client; a decoded 0xFFFFFFF8 end frame is -8, an empty range.
            var offset = MotionFrameOffsetTicks(actor, motion) / SampleStepTicks;
            var startTicksLong = ((long)unchecked((int)motion.StartFrame) + offset) * SampleStepTicks;
            var endTicksLong = ((long)unchecked((int)motion.EndFrame) + offset) * SampleStepTicks;
            if (endTicksLong <= startTicksLong || startTicksLong < 0 || endTicksLong > int.MaxValue)
            {
                Console.Error.WriteLine(
                    $"warning: motion {index} (state 0x{motion.State:X}) has an unusable frame range {unchecked((int)motion.StartFrame)}..{unchecked((int)motion.EndFrame)}; skipped.");
                continue;
            }

            var startTicks = (int)startTicksLong;
            var endTicks = (int)endTicksLong;

            var baseName = Enum.IsDefined(typeof(MotionCode), motion.State)
                ? ((MotionCode)motion.State).ToString()
                : $"State_0x{motion.State:X}";
            var name = baseName;
            if (usedNames.TryGetValue(baseName, out var duplicateCount))
            {
                name = $"{baseName}.{index:000}";
            }

            usedNames[baseName] = duplicateCount + 1;
            yield return new MotionClip(name, startTicks, endTicks);
        }
    }

    private static int MotionFrameOffsetTicks(
        PristonActor actor,
        PristonMotionInfo motion)
    {
        var rangeIndex = motion.MotionFrame - 1;
        if (rangeIndex < 0 || rangeIndex >= actor.Skeleton.FrameRanges.Count)
        {
            return 0;
        }

        return actor.Skeleton.FrameRanges[rangeIndex].StartFrame;
    }

    private readonly record struct MotionClip(
        string Name,
        int StartTicks,
        int EndTicks);

    private sealed class NodeTree
    {
        private readonly PristonModel _model;
        private readonly int[] _parents;
        private readonly NodeBuilder? _wrapper;
        private readonly float _scale;
        private readonly NodeBuilder?[] _transformCarriers;
        private readonly Matrix[] _effectiveAbsolute;
        private readonly PristonModelAnimator _animator;
        private readonly IReadOnlyList<ClipPlan> _clips;
        private readonly bool[] _sheared;
        private readonly SvdTransform[] _bindSvd;

        public NodeTree(
            PristonModel model,
            Matrix[] absoluteBind,
            PristonModelAnimator animator,
            IReadOnlyList<ClipPlan> clips,
            NodeBuilder? wrapper = null,
            float scale = 1f)
        {
            _model = model;
            _wrapper = wrapper;
            _scale = scale;
            _animator = animator;
            _clips = clips;

            var count = model.Objects.Count;
            Names = BuildUniqueNames(model, wrapper is null ? null : ExportRootName);
            Nodes = new NodeBuilder[count];
            _parents = new int[count];
            _transformCarriers = new NodeBuilder?[count];
            _effectiveAbsolute = Copy(absoluteBind);
            _sheared = new bool[count];
            _bindSvd = new SvdTransform[count];
            VertexBake = new Matrix?[count];
            var carrierNames = BuildCarrierNames(Names, wrapper is null ? null : ExportRootName);

            var objectsByName = BuildObjectNameIndex(model);
            for (var index = 0; index < count; index++)
            {
                var parentName = model.Objects[index].ParentName;
                _parents[index] = parentName.Length > 0
                    && objectsByName.TryGetValue(parentName, out var parentIndex)
                    && parentIndex != index
                        ? parentIndex
                        : -1;
            }

            var resolved = new bool[count];
            var visiting = new bool[count];
            for (var index = 0; index < count; index++)
            {
                ResolveStaticNode(index, absoluteBind, resolved, visiting);
            }

            FindShearedNodes(absoluteBind);

            var creating = new bool[count];
            for (var index = 0; index < count; index++)
            {
                CreateNode(index, absoluteBind, creating, carrierNames);
            }
        }

        public string[] Names { get; }
        public NodeBuilder[] Nodes { get; }

        // Static nodes whose original matrix carries shear keep only their TRS part; the residual linear
        // part is baked into their vertices so glTF node matrices stay TRS-decomposable.
        public Matrix?[] VertexBake { get; }

        public void AddClips()
        {
            foreach (var clip in _clips)
            {
                AddClip(clip);
            }
        }

        private void AddClip(ClipPlan clip)
        {
            if (clip.EndTicks <= clip.StartTicks && clip.Exclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(clip), $"Clip '{clip.Name}' ends before it starts.");
            }

            var state = new SampleState(Nodes.Length, _bindSvd);
            foreach (var sourceTicks in SampleTicks(clip.StartTicks, clip.LastSourceTick))
            {
                var seconds = (sourceTicks - clip.StartTicks) / clip.TicksPerSecond;
                AddSample(clip.Name, sourceTicks, seconds, state);
            }

            var duration = (clip.EndTicks - clip.StartTicks) / clip.TicksPerSecond;
            AddSample(clip.Name, clip.LastSourceTick, duration, state);
        }

        private IEnumerable<int> ProbeTicks()
        {
            var ticks = new SortedSet<int>();
            foreach (var clip in _clips)
            {
                ticks.UnionWith(SampleTicks(clip.StartTicks, clip.LastSourceTick));
                ticks.Add(clip.LastSourceTick);
            }

            return ticks;
        }

        private SortedSet<int> SampleTicks(int startTicks, int terminalTicks)
        {
            var ticks = new SortedSet<int>
            {
                startTicks
            };
            for (var sample = (long)startTicks + SampleStepTicks;
                 sample < terminalTicks;
                 sample += SampleStepTicks)
            {
                ticks.Add((int)sample);
            }

            foreach (var modelObject in _model.Objects)
            {
                AddKeyTicks(ticks, modelObject.RotationKeys, startTicks, terminalTicks);
                AddKeyTicks(ticks, modelObject.PositionKeys, startTicks, terminalTicks);
                AddKeyTicks(ticks, modelObject.ScaleKeys, startTicks, terminalTicks);
            }

            return ticks;
        }

        private static void AddKeyTicks<T>(
            ISet<int> ticks,
            IReadOnlyList<T> keys,
            int startTicks,
            int terminalTicks)
        {
            foreach (var key in keys)
            {
                var frame = key switch
                {
                    PristonRotationKey rotation => rotation.Frame,
                    PristonPositionKey position => position.Frame,
                    PristonScaleKey scale => scale.Frame,
                    _ => throw new InvalidOperationException(
                        $"Unsupported animation key type {typeof(T).Name}.")
                };
                if (frame > startTicks && frame < terminalTicks)
                {
                    ticks.Add(frame);
                }
            }
        }

        private void ResolveStaticNode(int index, Matrix[] absoluteBind, bool[] resolved, bool[] visiting)
        {
            if (resolved[index] || visiting[index])
            {
                return;
            }

            visiting[index] = true;
            var parentIndex = _parents[index];
            if (parentIndex >= 0)
            {
                ResolveStaticNode(parentIndex, absoluteBind, resolved, visiting);
            }

            if (!_model.Objects[index].IsAnimated)
            {
                var local = LocalOf(absoluteBind, index);
                var trs = NearestTrs(local);
                if (!MatricesNearlyEqual(local.ToAffineNumerics(), trs.ToAffineNumerics(), StaticShearTolerance))
                {
                    VertexBake[index] = local * Matrix.Invert(trs);
                    _effectiveAbsolute[index] = parentIndex >= 0
                        ? trs * _effectiveAbsolute[parentIndex]
                        : trs;
                }
            }

            visiting[index] = false;
            resolved[index] = true;
        }

        // Original bones may combine a parent's non-uniform scale with their own rotation. glTF TRS cannot
        // hold the resulting shear, so those bones are found up front and exported through an SVD carrier.
        private void FindShearedNodes(Matrix[] absoluteBind)
        {
            var pending = new List<int>();
            for (var index = 0; index < _model.Objects.Count; index++)
            {
                if (!_model.Objects[index].IsAnimated)
                {
                    continue;
                }

                if (IsTrs(LocalOf(absoluteBind, index), index))
                {
                    pending.Add(index);
                }
                else
                {
                    _sheared[index] = true;
                }
            }

            if (pending.Count == 0 || _clips.Count == 0)
            {
                return;
            }

            foreach (var ticks in ProbeTicks())
            {
                var absolute = _animator.Evaluate(ticks);
                for (var position = pending.Count - 1; position >= 0; position--)
                {
                    var index = pending[position];
                    if (!IsTrs(LocalOf(absolute, index), index))
                    {
                        _sheared[index] = true;
                        pending.RemoveAt(position);
                    }
                }

                if (pending.Count == 0)
                {
                    return;
                }
            }
        }

        private Matrix LocalOf(Matrix[] absolute, int index)
        {
            var parentIndex = _parents[index];
            return parentIndex >= 0
                ? RelativeToParent(absolute[index], EffectiveAbsolute(absolute, parentIndex), _model.Objects[index].Name)
                : absolute[index];
        }

        private NodeBuilder CreateNode(
            int index,
            Matrix[] absoluteBind,
            bool[] creating,
            string[] carrierNames)
        {
            if (Nodes[index] is not null)
            {
                return Nodes[index];
            }

            if (creating[index])
            {
                throw new InvalidDataException(
                    $"Model hierarchy contains a cycle at object '{_model.Objects[index].Name}'.");
            }

            creating[index] = true;
            var parentIndex = _parents[index];
            var parent = parentIndex >= 0
                ? CreateNode(parentIndex, absoluteBind, creating, carrierNames)
                : _wrapper;
            var local = LocalOf(absoluteBind, index);

            NodeBuilder node;
            if (!_model.Objects[index].IsAnimated)
            {
                local = NearestTrs(local);
                node = parent is null
                    ? new NodeBuilder(Names[index])
                    : parent.CreateNode(Names[index]);
                if (_scale != 1f)
                {
                    local.Translation *= _scale;
                }

                node.LocalMatrix = local.ToAffineNumerics();
            }
            else if (_sheared[index])
            {
                var bind = DecomposeSvd(local, null, "BindPose", Names[index], 0);
                _bindSvd[index] = bind;
                var carrier = parent is null
                    ? new NodeBuilder(carrierNames[index])
                    : parent.CreateNode(carrierNames[index]);
                carrier.LocalMatrix = CreateScaleRotationTranslation(bind.Scale, bind.Outer, bind.Translation * _scale);
                node = carrier.CreateNode(Names[index]);
                node.LocalMatrix = System.Numerics.Matrix4x4.CreateFromQuaternion(bind.Inner);
                _transformCarriers[index] = carrier;
            }
            else
            {
                DecomposeRotationScale(
                    local,
                    "BindPose",
                    Names[index],
                    0,
                    out var translation,
                    out var rotation,
                    out var scale);
                translation *= _scale;

                if (RequiresRotationScaleCarrier(_model.Objects[index]))
                {
                    var carrier = parent is null
                        ? new NodeBuilder(carrierNames[index])
                        : parent.CreateNode(carrierNames[index]);
                    carrier.LocalMatrix = CreateScaleTranslation(scale, translation);
                    node = carrier.CreateNode(Names[index]);
                    node.LocalMatrix = System.Numerics.Matrix4x4.CreateFromQuaternion(rotation);
                    _transformCarriers[index] = carrier;
                }
                else
                {
                    node = parent is null
                        ? new NodeBuilder(Names[index])
                        : parent.CreateNode(Names[index]);
                    node.LocalMatrix = CreateScaleRotationTranslation(
                        scale,
                        rotation,
                        translation);
                }
            }

            Nodes[index] = node;
            creating[index] = false;
            return node;
        }

        private void AddSample(
            string clipName,
            int sourceTicks,
            float outputSeconds,
            SampleState state)
        {
            var absolute = _animator.Evaluate(sourceTicks);
            for (var index = 0; index < Nodes.Length; index++)
            {
                if (!_model.Objects[index].IsAnimated)
                {
                    continue;
                }

                var local = LocalOf(absolute, index);
                var carrier = _transformCarriers[index];
                if (_sheared[index])
                {
                    var decomposed = DecomposeSvd(local, state.Svd[index], clipName, Names[index], sourceTicks);
                    state.Svd[index] = decomposed;
                    var outer = state.CarrierRotations.MakeContinuous(index, decomposed.Outer);
                    var inner = state.Rotations.MakeContinuous(index, decomposed.Inner);
                    carrier!.UseTranslation(clipName).WithPoint(outputSeconds, decomposed.Translation * _scale);
                    carrier.UseRotation(clipName).WithPoint(outputSeconds, outer);
                    carrier.UseScale(clipName).WithPoint(outputSeconds, decomposed.Scale);
                    Nodes[index].UseRotation(clipName).WithPoint(outputSeconds, inner);
                    continue;
                }

                DecomposeRotationScale(
                    local,
                    clipName,
                    Names[index],
                    sourceTicks,
                    out var translation,
                    out var rotation,
                    out var scale);
                translation *= _scale;

                rotation = state.Rotations.MakeContinuous(index, rotation);
                if (carrier is null)
                {
                    Nodes[index].UseTranslation(clipName).WithPoint(outputSeconds, translation);
                    Nodes[index].UseScale(clipName).WithPoint(outputSeconds, scale);
                }
                else
                {
                    carrier.UseTranslation(clipName).WithPoint(outputSeconds, translation);
                    carrier.UseScale(clipName).WithPoint(outputSeconds, scale);
                }

                Nodes[index].UseRotation(clipName).WithPoint(outputSeconds, rotation);
            }
        }

        private Matrix EffectiveAbsolute(Matrix[] absolute, int index)
        {
            return VertexBake[index] is { } bake
                ? Matrix.Invert(bake) * absolute[index]
                : absolute[index];
        }

        // Carrier bones are written as rotation-then-scale (R*S); plain glTF nodes apply S*R, so both
        // orders must reproduce the original matrix before a bone may skip the SVD carrier.
        private bool IsTrs(Matrix local, int index)
        {
            // Translation is copied exactly, so only the linear part decides whether shear is present.
            local.Translation = Vector3.Zero;
            try
            {
                DecomposeRotationScale(
                    local,
                    string.Empty,
                    string.Empty,
                    0,
                    out var translation,
                    out var rotation,
                    out var scale,
                    AnimatedShearTolerance);
                if (RequiresRotationScaleCarrier(_model.Objects[index]))
                {
                    return true;
                }

                return MatricesNearlyEqual(
                    local.ToAffineNumerics(),
                    CreateScaleRotationTranslation(scale, rotation, translation),
                    AnimatedShearTolerance);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        private static Matrix NearestTrs(Matrix local)
        {
            try
            {
                DecomposeRotationScale(local, "BindPose", string.Empty, 0, out var translation, out var rotation, out var scale);
                var trs = CreateScaleRotationTranslation(scale, rotation, translation);
                return new Matrix(
                    trs.M11, trs.M12, trs.M13, 0f,
                    trs.M21, trs.M22, trs.M23, 0f,
                    trs.M31, trs.M32, trs.M33, 0f,
                    trs.M41, trs.M42, trs.M43, 1f);
            }
            catch (InvalidDataException)
            {
                return Matrix.CreateTranslation(local.Translation);
            }
        }

        private sealed class SampleState
        {
            public SampleState(int count, SvdTransform[] bind)
            {
                Rotations = new RotationContinuityState(count);
                CarrierRotations = new RotationContinuityState(count);
                Svd = bind.Select(static transform => (SvdTransform?)transform).ToArray();
            }

            public RotationContinuityState Rotations { get; }
            public RotationContinuityState CarrierRotations { get; }
            public SvdTransform?[] Svd { get; }
        }
    }

    // Row-vector linear part L = Inner * diag(Scale) * Outer, exported as bone (Inner) under a carrier
    // (Translation, Outer, Scale). U and V are the singular bases kept for frame-to-frame continuity.
    private readonly record struct SvdTransform(
        System.Numerics.Vector3 Translation,
        System.Numerics.Quaternion Outer,
        System.Numerics.Vector3 Scale,
        System.Numerics.Quaternion Inner,
        double[,] U,
        double[,] V);

    private static readonly int[][] AxisPermutations =
    [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0]
    ];

    private static SvdTransform DecomposeSvd(
        Matrix source,
        SvdTransform? previous,
        string clipName,
        string nodeName,
        int sourceTicks)
    {
        var matrix = source.ToAffineNumerics();
        var linear = new double[3, 3]
        {
            { matrix.M11, matrix.M12, matrix.M13 },
            { matrix.M21, matrix.M22, matrix.M23 },
            { matrix.M31, matrix.M32, matrix.M33 }
        };

        // M = L^T = U * Sigma * V^T, so L = V * Sigma * U^T with Inner = V and Outer = U^T.
        var symmetric = new double[3, 3];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                for (var k = 0; k < 3; k++)
                {
                    symmetric[row, column] += linear[row, k] * linear[column, k];
                }
            }
        }

        JacobiEigen(symmetric, out var eigenvalues, out var v);
        var sigma = new double[3];
        var largest = 0d;
        for (var axis = 0; axis < 3; axis++)
        {
            sigma[axis] = Math.Sqrt(Math.Max(eigenvalues[axis], 0d));
            largest = Math.Max(largest, sigma[axis]);
        }

        // Original rigs hide parts by scaling bones to zero; collapsed axes keep a tiny scale so the
        // hierarchy stays invertible while remaining visually identical.
        var floor = Math.Max(largest * CollapsedAxisScale, 1e-9);
        if (Determinant(v) < 0d)
        {
            NegateColumn(v, 2);
        }

        var u = new double[3, 3];
        var valid = new bool[3];
        for (var axis = 0; axis < 3; axis++)
        {
            if (sigma[axis] <= floor)
            {
                sigma[axis] = floor;
                continue;
            }

            valid[axis] = true;
            for (var row = 0; row < 3; row++)
            {
                var value = 0d;
                for (var k = 0; k < 3; k++)
                {
                    value += linear[k, row] * v[k, axis];
                }

                u[row, axis] = value / sigma[axis];
            }
        }

        CompleteOrthonormalBasis(u, valid);

        if (Determinant(u) < 0d)
        {
            NegateColumn(u, 2);
            sigma[2] = -sigma[2];
        }

        if (previous is { } prior)
        {
            AlignSingularBases(u, v, sigma, prior.U, prior.V);
        }

        var inner = QuaternionFromRows(v, transpose: false);
        var outer = QuaternionFromRows(u, transpose: true);
        var scale = new System.Numerics.Vector3((float)sigma[0], (float)sigma[1], (float)sigma[2]);
        var reconstructed = System.Numerics.Matrix4x4.CreateFromQuaternion(inner)
            * System.Numerics.Matrix4x4.CreateScale(scale)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(outer);
        reconstructed.Translation = matrix.Translation;
        if (!MatricesNearlyEqual(matrix, reconstructed))
        {
            throw NonDecomposableAnimation(clipName, nodeName, sourceTicks);
        }

        return new SvdTransform(matrix.Translation, outer, scale, inner, u, v);
    }

    private static Matrix RegularizeCollapsed(Matrix matrix, string objectName)
    {
        var decomposed = DecomposeSvd(matrix, null, "BindPose", objectName, 0);
        var regular = System.Numerics.Matrix4x4.CreateFromQuaternion(decomposed.Inner)
            * System.Numerics.Matrix4x4.CreateScale(decomposed.Scale)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(decomposed.Outer);
        return new Matrix(
            regular.M11, regular.M12, regular.M13, 0f,
            regular.M21, regular.M22, regular.M23, 0f,
            regular.M31, regular.M32, regular.M33, 0f,
            matrix.M41, matrix.M42, matrix.M43, 1f);
    }

    private static void CompleteOrthonormalBasis(double[,] basis, bool[] valid)
    {
        var validCount = valid.Count(static value => value);
        if (validCount == 3)
        {
            return;
        }

        if (validCount == 0)
        {
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    basis[row, column] = row == column ? 1d : 0d;
                }
            }

            return;
        }

        var first = Array.IndexOf(valid, true);
        if (validCount == 1)
        {
            var axis = Column(basis, first);
            var helper = Math.Abs(axis[0]) < 0.9 ? new[] { 1d, 0d, 0d } : new[] { 0d, 1d, 0d };
            var second = Normalize(Cross(axis, helper));
            var next = (first + 1) % 3;
            SetColumn(basis, next, second);
            valid[next] = true;
        }

        var missing = Array.IndexOf(valid, false);
        var a = Column(basis, (missing + 1) % 3);
        var b = Column(basis, (missing + 2) % 3);
        SetColumn(basis, missing, Normalize(Cross(a, b)));
    }

    private static double[] Column(double[,] m, int column) => [m[0, column], m[1, column], m[2, column]];

    private static void SetColumn(double[,] m, int column, double[] value)
    {
        for (var row = 0; row < 3; row++)
        {
            m[row, column] = value[row];
        }
    }

    private static double[] Cross(double[] a, double[] b) =>
        [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double[] Normalize(double[] value)
    {
        var length = Math.Sqrt(value[0] * value[0] + value[1] * value[1] + value[2] * value[2]);
        return length <= 1e-300 ? [1d, 0d, 0d] : [value[0] / length, value[1] / length, value[2] / length];
    }

    private static void AlignSingularBases(double[,] u, double[,] v, double[] sigma, double[,] priorU, double[,] priorV)
    {
        var bestScore = double.NegativeInfinity;
        int[]? bestPermutation = null;
        var bestSigns = 0;
        foreach (var permutation in AxisPermutations)
        {
            for (var signs = 0; signs < 8; signs++)
            {
                var candidate = new double[3, 3];
                for (var axis = 0; axis < 3; axis++)
                {
                    var sign = (signs & (1 << axis)) != 0 ? -1d : 1d;
                    for (var row = 0; row < 3; row++)
                    {
                        candidate[row, axis] = sign * v[row, permutation[axis]];
                    }
                }

                if (Determinant(candidate) <= 0d)
                {
                    continue;
                }

                var score = 0d;
                for (var axis = 0; axis < 3; axis++)
                {
                    var sign = (signs & (1 << axis)) != 0 ? -1d : 1d;
                    for (var row = 0; row < 3; row++)
                    {
                        score += sign * (v[row, permutation[axis]] * priorV[row, axis] + u[row, permutation[axis]] * priorU[row, axis]);
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPermutation = permutation;
                    bestSigns = signs;
                }
            }
        }

        if (bestPermutation is null)
        {
            return;
        }

        var alignedU = new double[3, 3];
        var alignedV = new double[3, 3];
        var alignedSigma = new double[3];
        for (var axis = 0; axis < 3; axis++)
        {
            var sign = (bestSigns & (1 << axis)) != 0 ? -1d : 1d;
            alignedSigma[axis] = sigma[bestPermutation[axis]];
            for (var row = 0; row < 3; row++)
            {
                alignedU[row, axis] = sign * u[row, bestPermutation[axis]];
                alignedV[row, axis] = sign * v[row, bestPermutation[axis]];
            }
        }

        Array.Copy(alignedU, u, 9);
        Array.Copy(alignedV, v, 9);
        Array.Copy(alignedSigma, sigma, 3);
    }

    private static void JacobiEigen(double[,] symmetric, out double[] eigenvalues, out double[,] eigenvectors)
    {
        var a = (double[,])symmetric.Clone();
        var vectors = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (var sweep = 0; sweep < 64; sweep++)
        {
            var offDiagonal = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (offDiagonal <= 1e-30)
            {
                break;
            }

            foreach (var (p, q) in new[] { (0, 1), (0, 2), (1, 2) })
            {
                if (Math.Abs(a[p, q]) <= 1e-300)
                {
                    continue;
                }

                var theta = (a[q, q] - a[p, p]) / (2d * a[p, q]);
                var t = Math.Sign(theta == 0d ? 1d : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1d));
                var c = 1d / Math.Sqrt(t * t + 1d);
                var s = t * c;
                for (var k = 0; k < 3; k++)
                {
                    var akp = a[k, p];
                    var akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }

                for (var k = 0; k < 3; k++)
                {
                    var apk = a[p, k];
                    var aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }

                for (var k = 0; k < 3; k++)
                {
                    var vkp = vectors[k, p];
                    var vkq = vectors[k, q];
                    vectors[k, p] = c * vkp - s * vkq;
                    vectors[k, q] = s * vkp + c * vkq;
                }
            }
        }

        eigenvalues = [a[0, 0], a[1, 1], a[2, 2]];
        eigenvectors = vectors;
    }

    private static double Determinant(double[,] m)
    {
        return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
            - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
            + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }

    private static void NegateColumn(double[,] m, int column)
    {
        for (var row = 0; row < 3; row++)
        {
            m[row, column] = -m[row, column];
        }
    }

    private static System.Numerics.Quaternion QuaternionFromRows(double[,] m, bool transpose)
    {
        float At(int row, int column) => (float)(transpose ? m[column, row] : m[row, column]);
        var rotation = new System.Numerics.Matrix4x4(
            At(0, 0), At(0, 1), At(0, 2), 0f,
            At(1, 0), At(1, 1), At(1, 2), 0f,
            At(2, 0), At(2, 1), At(2, 2), 0f,
            0f, 0f, 0f, 1f);
        return System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.CreateFromRotationMatrix(rotation));
    }

    private sealed class RotationContinuityState
    {
        private readonly bool[] _hasValue;
        private readonly System.Numerics.Quaternion[] _values;

        public RotationContinuityState(int count)
        {
            _hasValue = new bool[count];
            _values = new System.Numerics.Quaternion[count];
        }

        public System.Numerics.Quaternion MakeContinuous(
            int index,
            System.Numerics.Quaternion value)
        {
            if (_hasValue[index]
                && System.Numerics.Quaternion.Dot(_values[index], value) < 0f)
            {
                value = new System.Numerics.Quaternion(
                    -value.X,
                    -value.Y,
                    -value.Z,
                    -value.W);
            }

            _hasValue[index] = true;
            _values[index] = value;
            return value;
        }
    }

    private static Vector3 BakeNormal(Vector3 normal, Matrix bake)
    {
        var determinant = bake.Determinant();
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= 1e-12f)
        {
            return normal;
        }

        var baked = Vector3.TransformNormal(normal, Matrix.Transpose(Matrix.Invert(bake)));
        return baked.LengthSquared() > 1e-12f ? Vector3.Normalize(baked) : normal;
    }

    private static Matrix RelativeToParent(
        Matrix absolute,
        Matrix parentAbsolute,
        string objectName)
    {
        var determinant = parentAbsolute.Determinant();
        if (!float.IsFinite(determinant))
        {
            throw new InvalidDataException(
                $"Object '{objectName}' has a non-finite parent transform.");
        }

        if (MathF.Abs(determinant) <= 1e-8f)
        {
            parentAbsolute = RegularizeCollapsed(parentAbsolute, objectName);
        }

        return absolute * Matrix.Invert(parentAbsolute);
    }

    private static void DecomposeRotationScale(
        Matrix source,
        string clipName,
        string nodeName,
        int sourceTicks,
        out System.Numerics.Vector3 translation,
        out System.Numerics.Quaternion rotation,
        out System.Numerics.Vector3 scale,
        float tolerance = TransformTolerance)
    {
        var matrix = source.ToAffineNumerics();
        translation = matrix.Translation;
        scale = new System.Numerics.Vector3(
            MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M21 * matrix.M21) + (matrix.M31 * matrix.M31)),
            MathF.Sqrt((matrix.M12 * matrix.M12) + (matrix.M22 * matrix.M22) + (matrix.M32 * matrix.M32)),
            MathF.Sqrt((matrix.M13 * matrix.M13) + (matrix.M23 * matrix.M23) + (matrix.M33 * matrix.M33)));
        if (scale.X <= 1e-8f || scale.Y <= 1e-8f || scale.Z <= 1e-8f)
        {
            throw NonDecomposableAnimation(clipName, nodeName, sourceTicks);
        }

        if (matrix.GetDeterminant() < 0f)
        {
            scale.X = -scale.X;
        }

        var rotationMatrix = new System.Numerics.Matrix4x4(
            matrix.M11 / scale.X,
            matrix.M12 / scale.Y,
            matrix.M13 / scale.Z,
            0f,
            matrix.M21 / scale.X,
            matrix.M22 / scale.Y,
            matrix.M23 / scale.Z,
            0f,
            matrix.M31 / scale.X,
            matrix.M32 / scale.Y,
            matrix.M33 / scale.Z,
            0f,
            0f,
            0f,
            0f,
            1f);
        rotation = System.Numerics.Quaternion.CreateFromRotationMatrix(rotationMatrix);
        rotation = System.Numerics.Quaternion.Normalize(rotation);
        var reconstructed = System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
            * System.Numerics.Matrix4x4.CreateScale(scale)
            * System.Numerics.Matrix4x4.CreateTranslation(translation);
        if (!MatricesNearlyEqual(matrix, reconstructed, tolerance))
        {
            throw NonDecomposableAnimation(clipName, nodeName, sourceTicks);
        }
    }

    private static InvalidDataException NonDecomposableAnimation(
        string clipName,
        string nodeName,
        int sourceTicks)
    {
        return new InvalidDataException(
            $"Animation '{clipName}' node '{nodeName}' at tick {sourceTicks} contains shear or another transform that glTF TRS channels cannot represent.");
    }

    private static System.Numerics.Matrix4x4 CreateScaleTranslation(
        System.Numerics.Vector3 scale,
        System.Numerics.Vector3 translation)
    {
        var matrix = System.Numerics.Matrix4x4.CreateScale(scale);
        matrix.Translation = translation;
        return matrix;
    }

    private static System.Numerics.Matrix4x4 CreateScaleRotationTranslation(
        System.Numerics.Vector3 scale,
        System.Numerics.Quaternion rotation,
        System.Numerics.Vector3 translation)
    {
        return System.Numerics.Matrix4x4.CreateScale(scale)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
            * System.Numerics.Matrix4x4.CreateTranslation(translation);
    }

    private static bool RequiresRotationScaleCarrier(PristonModelObject modelObject)
    {
        foreach (var key in modelObject.ScaleKeys)
        {
            var largestMagnitude = MathF.Max(
                1f,
                MathF.Max(
                    MathF.Abs(key.Scale.X),
                    MathF.Max(MathF.Abs(key.Scale.Y), MathF.Abs(key.Scale.Z))));
            var largestDifference = MathF.Max(
                MathF.Abs(key.Scale.X - key.Scale.Y),
                MathF.Max(
                    MathF.Abs(key.Scale.X - key.Scale.Z),
                    MathF.Abs(key.Scale.Y - key.Scale.Z)));
            if (largestDifference > largestMagnitude * TransformTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatricesNearlyEqual(
        System.Numerics.Matrix4x4 left,
        System.Numerics.Matrix4x4 right,
        float tolerance = TransformTolerance)
    {
        var leftValues = MatrixValues(left);
        var rightValues = MatrixValues(right);
        var largestMagnitude = 1f;
        var largestDifference = 0f;

        for (var index = 0; index < leftValues.Length; index++)
        {
            if (!float.IsFinite(leftValues[index]) || !float.IsFinite(rightValues[index]))
            {
                return false;
            }

            largestMagnitude = MathF.Max(largestMagnitude, MathF.Abs(leftValues[index]));
            largestDifference = MathF.Max(
                largestDifference,
                MathF.Abs(leftValues[index] - rightValues[index]));
        }

        return largestDifference <= largestMagnitude * tolerance;
    }

    private static float[] MatrixValues(System.Numerics.Matrix4x4 matrix)
    {
        return
        [
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M14,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M24,
            matrix.M31,
            matrix.M32,
            matrix.M33,
            matrix.M34,
            matrix.M41,
            matrix.M42,
            matrix.M43,
            matrix.M44
        ];
    }

    private static string[] BuildUniqueNames(
        PristonModel model,
        string? reservedName)
    {
        var names = new string[model.Objects.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reservedName is not null)
        {
            used.Add(reservedName);
        }

        for (var index = 0; index < names.Length; index++)
        {
            var baseName = SafeName(model.Objects[index].Name, index);
            var name = baseName;
            var suffix = 1;
            while (!used.Add(name))
            {
                name = $"{baseName}.{suffix:000}";
                suffix++;
            }

            names[index] = name;
        }

        return names;
    }

    private static string[] BuildCarrierNames(
        IReadOnlyList<string> nodeNames,
        string? reservedName)
    {
        var names = new string[nodeNames.Count];
        var used = new HashSet<string>(nodeNames, StringComparer.OrdinalIgnoreCase);
        if (reservedName is not null)
        {
            used.Add(reservedName);
        }

        for (var index = 0; index < names.Length; index++)
        {
            var baseName = $"{nodeNames[index]}{TransformCarrierSuffix}";
            var name = baseName;
            var suffix = 1;
            while (!used.Add(name))
            {
                name = $"{baseName}.{suffix:000}";
                suffix++;
            }

            names[index] = name;
        }

        return names;
    }

    private static void AddTriangle<TVertexGeometry, TVertexMaterial, TVertexSkinning>(
        IPrimitiveBuilder primitive,
        VertexBuilder<TVertexGeometry, TVertexMaterial, TVertexSkinning> first,
        VertexBuilder<TVertexGeometry, TVertexMaterial, TVertexSkinning> second,
        VertexBuilder<TVertexGeometry, TVertexMaterial, TVertexSkinning> third,
        ExportOptions options)
        where TVertexGeometry : struct, IVertexGeometry
        where TVertexMaterial : struct, IVertexMaterial
        where TVertexSkinning : struct, IVertexSkinning
    {
        var builder = (PrimitiveBuilder<MaterialBuilder, TVertexGeometry, TVertexMaterial, TVertexSkinning>)primitive;
        if (options.FlipWinding)
        {
            builder.AddTriangle(first, third, second);
        }
        else
        {
            builder.AddTriangle(first, second, third);
        }
    }

    private static System.Numerics.Vector2 ConvertUv(
        Vector2 uv,
        ExportOptions options)
    {
        return new System.Numerics.Vector2(
            uv.X,
            options.FlipV ? 1f - uv.Y : uv.Y);
    }

    private static PristonTexLink TextureCoordinatesOf(
        IReadOnlyList<PristonTexLink> links,
        int index)
    {
        return index >= 0 && index < links.Count
            ? links[index]
            : PristonTexLink.Empty;
    }


    private static Matrix[] Copy(Matrix[] source)
    {
        var destination = new Matrix[source.Length];
        Array.Copy(source, destination, source.Length);
        return destination;
    }

    private static string SafeName(string name, int index)
    {
        return string.IsNullOrWhiteSpace(name)
            ? $"obj{index}"
            : name;
    }

    private static Vector3[][] BuildSmoothNormals(PristonModel model)
    {
        var result = new Vector3[model.Objects.Count][];
        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var modelObject = model.Objects[objectIndex];
            var normals = new Vector3[modelObject.Vertices.Count];
            var needsFallback = false;
            for (var vertexIndex = 0; vertexIndex < normals.Length; vertexIndex++)
            {
                var authored = modelObject.Vertices[vertexIndex].Normal;
                if (authored.LengthSquared() > 1e-6f)
                {
                    normals[vertexIndex] = authored;
                }
                else
                {
                    needsFallback = true;
                }
            }

            if (needsFallback)
            {
                var averaged = new Vector3[modelObject.Vertices.Count];
                foreach (var face in modelObject.Faces)
                {
                    if (!face.FitsIn(modelObject.Vertices.Count))
                    {
                        continue;
                    }

                    var normal = Vector3.Cross(
                        modelObject.Vertices[face.B].Position - modelObject.Vertices[face.A].Position,
                        modelObject.Vertices[face.C].Position - modelObject.Vertices[face.A].Position);
                    if (normal.LengthSquared() < 1e-6f)
                    {
                        continue;
                    }

                    normal.Normalize();
                    averaged[face.A] += normal;
                    averaged[face.B] += normal;
                    averaged[face.C] += normal;
                }

                for (var vertexIndex = 0; vertexIndex < normals.Length; vertexIndex++)
                {
                    if (normals[vertexIndex].LengthSquared() <= 1e-6f)
                    {
                        normals[vertexIndex] = averaged[vertexIndex].LengthSquared() < 1e-6f
                            ? Vector3.Up
                            : Vector3.Normalize(averaged[vertexIndex]);
                    }
                }
            }

            result[objectIndex] = normals;
        }

        return result;
    }
}
