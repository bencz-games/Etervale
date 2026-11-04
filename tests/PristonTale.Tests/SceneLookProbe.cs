using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Application.Services;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Look regressions of the photometric renderer on real assets (needs the game data and the compiled content):
//  - "charselect": the character-select stage with its braziers, their green flames and a roster character, as the screen
//    builds them;
//    fails when more than BlownLimit of the frame is clipped to white (the statues beside the braziers were), or more
//    than SaturatedLimit has a channel clipped (the flames and their glow have one; statues burnt to orange would exceed it);
//    then each class of both lineups opens the face selection, whose portrait fails when it draws fewer than
//    PortraitPixels pixels over the world's frame;
//  - "actor": NPCs in daylight on a flat background, front and back; fails when the ring of pixels along a silhouette is
//    brighter than the actor's interior by more than RimLimit (a grey, metallic rim from grazing specular);
//  - "opening": the login screen's scene (the opening flythrough with its meteors and fires), as the screen builds it;
//    the sky there is authored red, so only the town below it is measured: it fails when the lower third of the frame is
//    on average more than TintLimit times redder than it is blue (a red cast over the daylight town);
//  - "scenery": an animated scenery model against the same surface on the stage (SceneLookProbe.Scenery.cs);
//  - "shade": an actor in a tree's shade against the floor around it (SceneLookProbe.Shade.cs).
internal sealed partial class SceneLookProbe : Game
{
    private const float BlownLimit = 0.02f;
    private const float SaturatedLimit = 0.05f;
    private const float RimLimit = 1.25f;
    private const float TintLimit = 1.8f;
    private const int PortraitPixels = 2000;
    private static readonly CharacterBrood[] FaceBroods = [CharacterBrood.Tempskron, CharacterBrood.Morion];
    private readonly string _root;
    private readonly string _output;
    private readonly string _mode;
    private readonly Stopwatch _timeout = Stopwatch.StartNew();
    private PristonRenderer _renderer = null!;
    private PresentationSettings _presentation = null!;
    private PristonCamera _camera = null!;
    private CharacterSelectCamera _selectCamera = null!;
    private CharacterSelectScene _selectScene = null!;
    private Task _preparation = null!;
    private CharacterSummary[] _roster = [];
    private readonly List<(ActorHandle Handle, Matrix World)> _actors = [];
    private PristonActor[] _actorAssets = [];
    private OpeningCinematic? _opening;
    private PristonSkyLibrary? _sky;
    private EffectLibrary? _effects;
    private VfxSystem? _flameSystem;
    private VfxRenderer? _flameRenderer;
    private CharSelectFlames? _flames;
    private bool _built;
    private CharacterSelectPortrait? _portrait;
    private int _faceBrood = -1;
    private int _faceJob;
    private bool _faceOpen;
    private int _faceWait;
    private int _frames;

    public SceneLookProbe(string mode, string root, string content, string output)
    {
        _mode = mode;
        _root = root;
        _output = output;
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
        _presentation = new PresentationSettings();
        _presentation.ApplyRendering(Shaded);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _presentation);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
        var cache = new PristonAssetCache();
        if (_mode == "scenery")
        {
            LoadScenery();
            return;
        }

        if (_mode == "shade")
        {
            LoadShade(cache);
            return;
        }

        if (_mode == "opening")
        {
            var log = PristonTale.Engine3D.Diagnostics.ConsoleClientLog.Instance;
            _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _root, log);
            _effects = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _root, log);
            _opening = new OpeningCinematic(GraphicsDevice, Content, log, relative => Path.Combine(_root, relative), _root, _sky,
                _presentation, _effects);
            _opening.Load();
            _preparation = Task.CompletedTask;
            _built = true;
            return;
        }

        if (_mode == "charselect")
        {
            CharacterSelectScreen.ConfigureLighting(_renderer);
            var stage = new PristonSmdReader(cache).ReadStage(Path.Combine(_root, CharacterSelectScreen.LoginStagePath));
            _renderer.AddStage(stage, Array.Empty<PristonModel>());
            _selectCamera = new CharacterSelectCamera(GraphicsDevice, 0);
            _selectScene = new CharacterSelectScene(_renderer, new PristonActorLoader(cache), _root, _selectCamera, stage,
                PristonTale.Engine3D.Diagnostics.ConsoleClientLog.Instance);
            _roster = [new CharacterSummary("Probe", PlayerClass.Fighter, 10, 0), new CharacterSummary("Probe2", PlayerClass.Atalanta, 10, 0)];
            _effects = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _root, PristonTale.Engine3D.Diagnostics.ConsoleClientLog.Instance);
            var library = _effects.Vfx.Library;
            library.Register(CharSelectEffects.GreenBeamDefinition());
            Assert.True(library.TryGet(CharSelectEffects.GreenBeam, out var beam), "the brazier flames did not compile");
            _flameSystem = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(_renderer.TransientLights), library);
            _flameRenderer = new VfxRenderer(GraphicsDevice, Content);
            _flames = new CharSelectFlames(_flameSystem, beam, CharacterSelectScreen.BrazierPositions);
            _portrait = new CharacterSelectPortrait(GraphicsDevice);
            _preparation = Task.WhenAll(_selectScene.PrepareRosterAsync(_roster, CancellationToken.None),
                _selectScene.PrepareJobLineupAsync(CharacterBrood.Tempskron, CancellationToken.None),
                _selectScene.PrepareJobLineupAsync(CharacterBrood.Morion, CancellationToken.None));
            return;
        }

        _renderer.BackgroundColor = new Color(30, 32, 36);
        _renderer.SetTimeOfDay(12f);
        _renderer.DayLengthSeconds = 0f;
        _preparation = Task.Run(async () =>
        {
            var loader = new PristonActorLoader(cache);
            var actors = new List<PristonActor>();
            foreach (var name in new[] { "TN-003", "TN-004", "TN-007" })
            {
                var actor = loader.Load(_root, Path.Combine(_root, $"char/npc/{name}/{name}.inx"));
                await _renderer.PrepareActorResourcesAsync(actor);
                actors.Add(actor);
            }

            _actorAssets = actors.ToArray();
        });
    }

    protected override void Update(GameTime gameTime)
    {
        if (_timeout.Elapsed > TimeSpan.FromSeconds(120))
        {
            throw new TimeoutException("Scene look probe did not finish.");
        }

        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Scene look probe preparation failed.", _preparation.Exception);
        }

        if (!_built)
        {
            if (_mode == "charselect")
            {
                _selectScene.BuildRoster(_roster);
            }
            else if (_mode == "shade")
            {
                BuildShade();
            }
            else
            {
                for (var i = 0; i < _actorAssets.Length; i++)
                {
                    var world = Matrix.CreateTranslation((i - 1) * 90f, 0f, 0f);
                    _actors.Add((_renderer.AddActor(new PristonActorInstance(_actorAssets[i], world, MotionCode.Stand)), world));
                }
            }

            _built = true;
        }

        var step = TimeSpan.FromSeconds(1d / 60);
        if (_opening is not null)
        {
            _opening.Update(new GameTime(TimeSpan.FromSeconds(_frames / 60d), step));
            _frames++;
            return;
        }

        if (_mode == "scenery")
        {
            UpdateScenery();
            return;
        }

        if (_mode == "charselect")
        {
            _selectCamera.Update(GraphicsDevice, 0, 1f / 60f);
            _selectScene.UpdateRun(1f / 60f);
            _flames?.Update(1f / 60f);
        }

        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_frames / 60d), step));
        _frames++;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!_built)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        if (_opening is not null)
        {
            DrawOpening(_opening);
            return;
        }

        if (_mode == "scenery")
        {
            DrawScenery();
            return;
        }

        if (_mode == "shade")
        {
            DrawShade();
            return;
        }

        var camera = _mode == "charselect" ? _selectCamera.Camera : _camera;
        if (_mode != "charselect")
        {
            // Front for the first half of the frames, then from behind.
            var behind = _frames > 150;
            _camera.SetScriptedView(GraphicsDevice, new Vector3(0f, 90f, behind ? 170f : -170f), new Vector3(0f, 55f, 0f));
        }

        _renderer.Draw(camera);
        DrawFlames(camera);
        _renderer.ComposeScene();
        if (_mode == "charselect" && _frames == 150)
        {
            Blown(Capture(camera, "charselect"));
        }
        else if (_mode == "charselect" && _frames > 150)
        {
            CheckFace();
        }
        else if (_mode != "charselect" && (_frames == 140 || _frames == 290))
        {
            var name = _frames == 140 ? "actor-front" : "actor-back";
            Check(camera, name);
            if (_frames == 290)
            {
                Exit();
            }
        }
    }

    private Color[] Capture(PristonCamera camera, string name)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        using var target = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        if (_mode != "charselect")
        {
            // No history across the background change.
            _renderer.ResetTemporalState();
        }

        _renderer.Draw(camera);
        DrawFlames(camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        Directory.CreateDirectory(_output);
        using (var stream = File.Create(Path.Combine(_output, name + ".png")))
        {
            target.SaveAsPng(stream, width, height);
        }

        var pixels = new Color[width * height];
        target.GetData(pixels);
        return pixels;
    }

    // The flythrough a few seconds in (the meteors have fallen and set their fires), then later along the path.
    private void DrawOpening(OpeningCinematic opening)
    {
        opening.Draw();
        if (_frames is not (360 or 900))
        {
            return;
        }

        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        using var target = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        opening.Draw();
        GraphicsDevice.SetRenderTarget(null);
        var name = $"opening-{_frames}";
        Directory.CreateDirectory(_output);
        using (var stream = File.Create(Path.Combine(_output, name + ".png")))
        {
            target.SaveAsPng(stream, width, height);
        }

        var pixels = new Color[width * height];
        target.GetData(pixels);
        pixels = pixels[(width * height * 2 / 3)..];
        var red = pixels.Average(static p => (double)p.R);
        var green = pixels.Average(static p => (double)p.G);
        var blue = pixels.Average(static p => (double)p.B);
        var tint = red / Math.Max(blue, 1.0);
        Console.WriteLine($"LOOK {name}: town mean colour {red:F1} {green:F1} {blue:F1} (red over blue {tint:F2})");
        Assert.True(tint <= TintLimit, $"{name}: the login scene's town is {tint:F2} times redder than blue");
        if (_frames == 900)
        {
            Exit();
        }
    }

    // One class per step: its face selection is opened, and a frame later its portrait is drawn after the world's frame,
    // as the screen draws it.
    private void CheckFace()
    {
        if (!_faceOpen)
        {
            if (_faceBrood < 0 || _faceJob + 1 >= _selectScene.JobCount)
            {
                if (++_faceBrood >= FaceBroods.Length)
                {
                    Exit();
                    return;
                }

                _selectScene.BuildJobLineup(FaceBroods[_faceBrood]);
                _faceJob = 0;
            }
            else
            {
                _faceJob++;
            }

            _selectScene.OpenFacePreview(_faceJob, 0);
            _faceOpen = true;
            _faceWait = 0;
            return;
        }

        const int width = 256;
        const int height = 384;
        var playerClass = _selectScene.JobClass(_faceJob);
        var texture = _portrait!.Render(_renderer, _selectScene.FacePreviewHandles, width, height);
        if (texture is null)
        {
            Assert.True(++_faceWait < 60, $"the {playerClass} face portrait never had bounds");
            return;
        }

        var pixels = new Color[width * height];
        texture.GetData(pixels);
        Directory.CreateDirectory(_output);
        using (var stream = File.Create(Path.Combine(_output, $"face-{playerClass}.png")))
        {
            texture.SaveAsPng(stream, width, height);
        }

        var drawn = pixels.Count(static p => p.A > 0);
        Console.WriteLine($"LOOK face {playerClass}: {drawn} pixels drawn");
        Assert.True(drawn >= PortraitPixels, $"the {playerClass} face portrait drew {drawn} pixels");
        _faceOpen = false;
    }

    private static void Blown(Color[] pixels)
    {
        var clipped = pixels.Count(static p => p.R >= 250 && p.G >= 250 && p.B >= 250);
        var share = (float)clipped / pixels.Length;
        var saturated = (float)pixels.Count(static p => Math.Max(p.R, Math.Max(p.G, p.B)) >= 250) / pixels.Length;
        Console.WriteLine($"LOOK charselect clipped share {share:P2}, a channel clipped {saturated:P2}");
        Assert.True(share <= BlownLimit, $"character select: {share:P2} of the frame clipped to white");
        Assert.True(saturated <= SaturatedLimit, $"character select: {saturated:P2} of the frame has a channel clipped");
    }

    private void Finish(Color[] pixels, Action<Color[]> check)
    {
        check(pixels);
        Exit();
    }

    // The shaded frame of the real pipeline (fixed exposure: the frames must differ only where intended), and a mask of
    // the actors from their albedo on a black and a white background without post-processing: what stays is actor. The
    // ring is the actor pixels within two pixels of the background, the interior those farther than five.
    private ClientSettings Shaded => new ClientSettings() with { AutoExposure = _mode is not ("actor" or "scenery" or "shade") };

    private ClientSettings MaskSettings => Shaded with
    {
        Quality = GraphicsQuality.Custom,
        AntiAliasing = AntiAliasingMode.Off,
        Bloom = BloomQuality.Off,
        Sharpening = 0f,
        DebugView = PristonDebugView.Albedo
    };

    private void Check(PristonCamera camera, string name)
    {
        _renderer.BackgroundColor = new Color(30, 32, 36);
        var dark = Capture(camera, name);
        _presentation.ApplyRendering(MaskSettings);
        _renderer.BackgroundColor = Color.Black;
        var maskDark = Capture(camera, name + "-mask-black");
        _renderer.BackgroundColor = Color.White;
        var light = Capture(camera, name + "-mask-white");
        _renderer.BackgroundColor = new Color(30, 32, 36);
        _presentation.ApplyRendering(Shaded);
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var actor = new bool[dark.Length];
        for (var i = 0; i < dark.Length; i++)
        {
            actor[i] = Math.Abs(maskDark[i].R - light[i].R) + Math.Abs(maskDark[i].G - light[i].G) + Math.Abs(maskDark[i].B - light[i].B) < 6;
        }

        double ring = 0;
        double interior = 0;
        var ringCount = 0;
        var interiorCount = 0;
        for (var y = 6; y < height - 6; y++)
        {
            for (var x = 6; x < width - 6; x++)
            {
                var index = y * width + x;
                if (!actor[index])
                {
                    continue;
                }

                var distance = DistanceToBackground(actor, width, x, y, 6);
                var luma = 0.2126 * dark[index].R + 0.7152 * dark[index].G + 0.0722 * dark[index].B;
                if (distance <= 2)
                {
                    ring += luma;
                    ringCount++;
                }
                else if (distance > 5)
                {
                    interior += luma;
                    interiorCount++;
                }
            }
        }

        var ratio = ring / Math.Max(ringCount, 1) / Math.Max(interior / Math.Max(interiorCount, 1), 1e-3);
        Console.WriteLine($"LOOK {name}: silhouette ring {ring / Math.Max(ringCount, 1):F1} against interior {interior / Math.Max(interiorCount, 1):F1} (ratio {ratio:F2}, {ringCount} / {interiorCount} px)");
        Assert.True(ringCount > 200 && interiorCount > 200, $"{name}: the actors were not found in the frame");
        Assert.True(ratio <= RimLimit, $"{name}: the silhouettes are {ratio:F2} times brighter than the actors' interior");
    }

    private static int DistanceToBackground(bool[] actor, int width, int x, int y, int limit)
    {
        for (var d = 1; d <= limit; d++)
        {
            if (!actor[(y - d) * width + x] || !actor[(y + d) * width + x] || !actor[y * width + x - d] || !actor[y * width + x + d])
            {
                return d;
            }
        }

        return limit + 1;
    }

    private void DrawFlames(PristonCamera camera)
    {
        if (_flameSystem is not null)
        {
            _flameRenderer!.Draw(_flameSystem, camera.View, camera.Projection, camera.Position, _renderer.SceneLinearDepth);
        }
    }

    protected override void UnloadContent()
    {
        _flameRenderer?.Dispose();
        _portrait?.Dispose();
        _opening?.Dispose();
        _effects?.Dispose();
        _sky?.Dispose();
        _renderer.Dispose();
    }
}
