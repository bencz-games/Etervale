using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;
using SharpGLTF.Schema2;

namespace PristonTale.Tests;

// Draws glTF models as actors in a row beside a legacy NPC (char/npc/TN-003, for scale), each playing its first clip,
// and saves the row and a close view of each glTF actor, twice (--capture-gltf <assets> <content> <out directory> <model
// paths, comma separated: a glTF file, or model info relative to the assets root; "path*scale" for a model in other
// units than metres>). The model "layers" is four spheres built here: a plain red dielectric, the same under a
// smooth clearcoat, a dark blue with a pink sheen and a green that transmits yellow light.
internal sealed class GltfActorCaptureProbe : Game
{
    private const int ViewFrames = 45;
    private const string Reference = "char/npc/TN-003/TN-003.inx";
    private const string LayersModel = "layers";

    private readonly string _root;
    private readonly string _output;
    private readonly string[] _models;
    private PristonRenderer _renderer = null!;
    private PristonCamera _camera = null!;
    private Task<PristonActor[]> _preparation = null!;
    private readonly List<(ActorHandle Handle, Matrix World, BoundingBox Bounds)> _actors = [];
    private int _frames;
    private float _span;

    public GltfActorCaptureProbe(string root, string content, string output, string[] models)
    {
        _root = root;
        _output = output;
        _models = models;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        _renderer = new PristonRenderer(GraphicsDevice, Content) { BackgroundColor = new Color(42, 45, 49) };
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, PristonCameraSettings.Default);
        _preparation = Task.Run(async () =>
        {
            var actors = new List<PristonActor>();
            var reference = new PristonActorLoader().Load(_root, Path.Combine(_root, Reference));
            await _renderer.PrepareActorResourcesAsync(reference);
            actors.Add(reference);
            actors.AddRange(_models.Select(model => model == LayersModel ? PristonActorLoader.LoadGltf(WriteLayers()) : Load(Scaled(model).Path)));
            return actors.ToArray();
        });
    }

    protected override void Update(GameTime gameTime)
    {
        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("glTF actor preparation failed.", _preparation.Exception);
        }

        if (_actors.Count == 0)
        {
            var x = 0f;
            var scales = new[] { 1f }.Concat(_models.Select(static model => Scaled(model).Scale)).ToArray();
            foreach (var (actor, scale) in _preparation.Result.Zip(scales))
            {
                var bounds = new BoundingBox(actor.Mesh.Bounds.Min * scale, actor.Mesh.Bounds.Max * scale);
                var width = MathF.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z);
                var centre = (bounds.Min + bounds.Max) * 0.5f;
                var world = Matrix.CreateScale(scale) * Matrix.CreateTranslation(-centre.X, -bounds.Min.Y, -centre.Z)
                    * Matrix.CreateTranslation(x + width * 0.5f, 0f, 0f);
                _actors.Add((_renderer.AddActor(new PristonActorInstance(actor, world, MotionCode.Stand)), world, bounds));
                Console.WriteLine($"{Path.GetFileName(actor.Mesh.Path)}: bounds {bounds.Min} .. {bounds.Max}");
                x += width + 20f;
            }

            _span = x;
        }

        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_frames / 60d), TimeSpan.FromSeconds(1d / 60)));
        _frames++;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_actors.Count == 0)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        // Each view holds ViewFrames frames, so the exposure settles, and is saved on its last; the views go round twice.
        var views = _actors.Count;
        var step = _frames / ViewFrames;
        var view = step % views;
        if (view == 0)
        {
            var centre = new Vector3(_span * 0.5f, 35f, 0f);
            var distance = MathF.Max(_span * 0.6f, 120f);
            _camera.SetScriptedView(GraphicsDevice, centre + new Vector3(0f, distance * 0.3f, distance), centre);
        }
        else
        {
            var (_, world, bounds) = _actors[view];
            var focus = Vector3.Transform((bounds.Min + bounds.Max) * 0.5f, world);
            var size = bounds.Max.Y - bounds.Min.Y;
            _camera.SetScriptedView(GraphicsDevice, focus + new Vector3(size * 0.2f, size * 0.25f, size * 1.4f), focus);
        }

        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        if (_frames % ViewFrames != ViewFrames - 1)
        {
            return;
        }

        Save($"gltf-{(view == 0 ? "row" : view.ToString())}-{step / views + 1}.png");
        if (step + 1 >= views * 2)
        {
            Exit();
        }
    }

    // A glTF model alone plays its first clip; its model info (a descriptor or .inx, relative to the assets root) its Stand.
    private PristonActor Load(string path) =>
        PristonModelPaths.IsGltf(path) ? PristonActorLoader.LoadGltf(path) : new PristonActorLoader().Load(_root, AssetFiles.Combine(_root, path));

    // "path*scale" draws a model authored in other units than metres at that scale (a centimetre model: 0.01).
    private static (string Path, float Scale) Scaled(string model) =>
        model.LastIndexOf('*') is var star and > 0
            ? (model[..star], float.Parse(model[(star + 1)..], System.Globalization.CultureInfo.InvariantCulture))
            : (model, 1f);

    private string WriteLayers()
    {
        var root = ModelRoot.CreateModel();
        var scene = root.UseScene("layers");
        var materials = new (string Name, System.Numerics.Vector4 Color, string[] Extensions, Action<Material> Set)[]
        {
            ("Plain", new(0.8f, 0.1f, 0.1f, 1f), [], static _ => { }),
            ("Coat", new(0.8f, 0.1f, 0.1f, 1f), ["ClearCoat"], static material =>
            {
                material.FindChannel("ClearCoat")!.Value.SetFactor("ClearCoatFactor", 1f);
                material.FindChannel("ClearCoatRoughness")!.Value.SetFactor("RoughnessFactor", 0.05f);
            }),
            ("Sheen", new(0.05f, 0.05f, 0.3f, 1f), ["Sheen"], static material =>
            {
                var sheen = material.FindChannel("SheenColor")!.Value;
                sheen.Color = new System.Numerics.Vector4(1f, 0.6f, 0.8f, 1f);
                material.FindChannel("SheenRoughness")!.Value.SetFactor("RoughnessFactor", 0.5f);
            }),
            ("Transmission", new(0.1f, 0.6f, 0.1f, 1f), ["DiffuseTransmission"], static material =>
            {
                material.FindChannel("DiffuseTransmissionFactor")!.Value.SetFactor("DiffuseTransmissionFactor", 1f);
                var color = material.FindChannel("DiffuseTransmissionColor")!.Value;
                color.Color = new System.Numerics.Vector4(1f, 1f, 0.2f, 1f);
            })
        };

        var (positions, normals, indices) = Sphere(0.5f, 32, 20);
        for (var index = 0; index < materials.Length; index++)
        {
            var (name, color, extensions, set) = materials[index];
            var material = root.CreateMaterial(name);
            material.InitializePBRMetallicRoughness(extensions);
            var baseColor = material.FindChannel("BaseColor")!.Value;
            baseColor.Color = color;
            material.FindChannel("MetallicRoughness")!.Value.SetFactor("MetallicFactor", 0f);
            material.FindChannel("MetallicRoughness")!.Value.SetFactor("RoughnessFactor", 0.5f);
            set(material);
            var mesh = root.CreateMesh(name);
            mesh.CreatePrimitive()
                .WithVertexAccessor("POSITION", positions)
                .WithVertexAccessor("NORMAL", normals)
                .WithIndicesAccessor(SharpGLTF.Schema2.PrimitiveType.TRIANGLES, indices)
                .WithMaterial(material);
            scene.CreateNode(name).WithLocalTranslation(new System.Numerics.Vector3(index * 1.2f, 0.5f, 0f)).Mesh = mesh;
        }

        var path = Path.Combine(_output, "layers.glb");
        Directory.CreateDirectory(_output);
        root.SaveGLB(path);
        return path;
    }

    private static (System.Numerics.Vector3[] Positions, System.Numerics.Vector3[] Normals, int[] Indices) Sphere(float radius, int slices, int stacks)
    {
        var positions = new List<System.Numerics.Vector3>();
        for (var stack = 0; stack <= stacks; stack++)
        {
            var polar = MathF.PI * stack / stacks;
            for (var slice = 0; slice <= slices; slice++)
            {
                var azimuth = MathF.Tau * slice / slices;
                positions.Add(new System.Numerics.Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Cos(polar), MathF.Sin(polar) * MathF.Sin(azimuth)));
            }
        }

        var indices = new List<int>();
        for (var stack = 0; stack < stacks; stack++)
        {
            for (var slice = 0; slice < slices; slice++)
            {
                var a = stack * (slices + 1) + slice;
                var b = a + slices + 1;
                indices.AddRange([a, a + 1, b, a + 1, b + 1, b]);
            }
        }

        return ([.. positions.Select(normal => normal * radius)], [.. positions], [.. indices]);
    }

    private void Save(string name)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        using var target = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        Directory.CreateDirectory(_output);
        var path = Path.Combine(_output, name);
        using (var stream = File.Create(path))
        {
            target.SaveAsPng(stream, width, height);
        }

        Console.WriteLine($"Capture: {path}");
    }

    protected override void UnloadContent()
    {
        _renderer.Dispose();
    }
}
