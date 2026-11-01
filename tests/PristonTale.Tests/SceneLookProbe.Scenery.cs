using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Tests;

// "scenery": the same Ricarten metal texture on a stage quad, whose vertex colours carry the authored stage light, and
// on an animated scenery model beside it with the same orientation (drawn by the skinned surface path, as actors are),
// under the noon and the 17:00 sun. Fails when the animated quad's luminance or saturation differs from the stage
// quad's (the animated scenery drawn at full white beside the darker stage read as grey chrome), or when its specular
// light alone (the Specular debug view) is brighter than the stage quad's: no chrome on a original non-metal.
internal sealed partial class SceneLookProbe
{
    private const string SceneryTexture = "metal_w.BMP";
    private const float SceneryLuminanceTolerance = 0.08f;
    private const float ScenerySaturationTolerance = 0.05f;
    private const float ScenerySpecularLimit = 1.25f;
    private static readonly Color SceneryStageLight = new(150, 150, 150);
    private static readonly Vector3 SceneryNormal = Vector3.Normalize(new Vector3(0f, 0.6f, -0.8f));
    private static readonly Vector3 StaticCentre = new(-60f, 60f, 0f);
    private static readonly Vector3 AnimatedCentre = new(60f, 60f, 0f);
    private float _sceneryHour = 12f;

    private void LoadScenery()
    {
        var reader = new PristonSmdReader(new PristonAssetCache());
        var ricarten = reader.ReadStage(AssetFiles.Resolve(Path.Combine(_root, "Field/Ricarten/village-2.smd")));
        var material = ricarten.Materials.First(m => m.InUse && string.Equals(m.TextureName, SceneryTexture, StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(_root, "Field/Ricarten/scenery-probe.smd");
        var (corners, uvs) = SceneryQuad(StaticCentre);
        var stage = new PristonStage(
            path,
            [material],
            corners.Select(p => new PristonStageVertex(p, SceneryStageLight)).ToArray(),
            [new PristonStageFace(0, 1, 2, 0, 0), new PristonStageFace(0, 2, 3, 0, 1)],
            [new PristonTexLink(uvs[0], uvs[1], uvs[2], 0), new PristonTexLink(uvs[0], uvs[2], uvs[3], 0)],
            [],
            new BoundingBox(StaticCentre - new Vector3(50f), StaticCentre + new Vector3(50f)));
        var (local, _) = SceneryQuad(Vector3.Zero);

        var animated = new PristonModelObject(
            "probe",
            "",
            false,
            Matrix.CreateTranslation(AnimatedCentre),
            AnimatedCentre,
            local.Select(p => new PristonModelVertex(p, SceneryNormal)).ToArray(),
            [new PristonModelFace(0, 1, 2, 0, 0), new PristonModelFace(0, 2, 3, 0, 1)],
            [new PristonTexLink(uvs[0], uvs[1], uvs[2], 0), new PristonTexLink(uvs[0], uvs[2], uvs[3], 0)],
            [],
            [],
            [new PristonPositionKey(0, AnimatedCentre), new PristonPositionKey(160, AnimatedCentre)],
            [],
            [],
            [],
            [],
            []);
        var model = new PristonModel(path, [material], [animated], [], new BoundingBox(AnimatedCentre - new Vector3(50f), AnimatedCentre + new Vector3(50f)));
        Assert.True(model.IsAnimated, "the probe's scenery model is not animated");
        _renderer.BackgroundColor = new Color(30, 32, 36);
        _renderer.DayLengthSeconds = 0f;
        _renderer.SetTimeOfDay(_sceneryHour);
        _renderer.AddStage(stage, [model]);
        _preparation = Task.CompletedTask;
        _built = true;
    }

    // A square of 80 units facing SceneryNormal, its texture once across it.
    private static (Vector3[] Corners, Vector2[] Uvs) SceneryQuad(Vector3 centre)
    {
        var across = Vector3.UnitX;
        var up = Vector3.Normalize(Vector3.Cross(SceneryNormal, across));
        Vector3[] corners =
        [
            centre - across * 40f - up * 40f,
            centre + across * 40f - up * 40f,
            centre + across * 40f + up * 40f,
            centre - across * 40f + up * 40f
        ];
        return (corners, [new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f)]);
    }

    private void UpdateScenery()
    {
        if (_frames == 160)
        {
            _sceneryHour = 20f;
            _renderer.SetTimeOfDay(_sceneryHour);
            _renderer.ResetTemporalState();
        }

        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_frames / 60d), TimeSpan.FromSeconds(1d / 60)));
        _frames++;
    }

    private void DrawScenery()
    {
        var middle = (StaticCentre + AnimatedCentre) * 0.5f;
        _camera.SetScriptedView(GraphicsDevice, middle + SceneryNormal * 260f, middle);
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        if (_frames is not (150 or 310))
        {
            return;
        }

        var name = $"scenery-h{_sceneryHour:00}";
        var shaded = Capture(_camera, name);
        _presentation.ApplyRendering(Shaded with { Quality = GraphicsQuality.Custom, DebugView = PristonDebugView.Specular });
        var specular = Capture(_camera, name + "-specular");
        _presentation.ApplyRendering(Shaded);
        var (staticLuminance, staticSaturation) = Sample(shaded, StaticCentre);
        var (animatedLuminance, animatedSaturation) = Sample(shaded, AnimatedCentre);
        var (staticSpecular, _) = Sample(specular, StaticCentre);
        var (animatedSpecular, _) = Sample(specular, AnimatedCentre);
        Console.WriteLine($"LOOK {name}: stage quad luminance {staticLuminance:F1} saturation {staticSaturation:F3} specular {staticSpecular:F1}; "
            + $"animated quad {animatedLuminance:F1} {animatedSaturation:F3} {animatedSpecular:F1}");
        Assert.True(staticLuminance > 5.0, $"{name}: the stage quad is not lit ({staticLuminance:F1})");
        Assert.True(Math.Abs(animatedLuminance / staticLuminance - 1.0) <= SceneryLuminanceTolerance,
            $"{name}: the animated scenery is {animatedLuminance / staticLuminance:F2} times as bright as the same stage surface");
        Assert.True(Math.Abs(animatedSaturation - staticSaturation) <= ScenerySaturationTolerance,
            $"{name}: the animated scenery's saturation {animatedSaturation:F3} differs from the stage's {staticSaturation:F3}");
        Assert.True(animatedSpecular <= staticSpecular * ScenerySpecularLimit + 2.0,
            $"{name}: the animated scenery reflects {animatedSpecular:F1} against the stage's {staticSpecular:F1} (chrome)");
        if (_frames == 310)
        {
            Exit();
        }
    }

    // Mean Rec. 709 luminance and saturation ((max - min) / max) of the pixels within a radius around a world point.
    private (double Luminance, double Saturation) Sample(Color[] pixels, Vector3 world, int radius = 12)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var clip = Vector4.Transform(new Vector4(world, 1f), _camera.View * _camera.Projection);
        var x = (int)((clip.X / clip.W * 0.5f + 0.5f) * width);
        var y = (int)((0.5f - clip.Y / clip.W * 0.5f) * height);
        double luminance = 0;
        double saturation = 0;
        var count = 0;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var pixel = pixels[Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)];
                var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                luminance += 0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B;
                saturation += max > 0 ? (max - min) / (double)max : 0.0;
                count++;
            }
        }

        return (luminance / count, saturation / count);
    }
}
