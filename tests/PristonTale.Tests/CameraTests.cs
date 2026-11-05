using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Tests;

internal static class CameraTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("camera movement stops at physical obstacles across resident fields", ResidentCollisionAsync);
        yield return ("camera respects ground clearance at low pitch and initial placement", ClearanceAsync);
        yield return ("high camera ignores portal and pole occlusion", OcclusionAsync);
        yield return ("camera left behind a wall the target crossed re-anchors on its arm", StrandedAsync);
        yield return ("wide windows cap the horizontal field of view instead of stretching the edges", FieldOfViewAsync);
        yield return ("camera lateral edge orbit is timed and respects viewport and focus", EdgeOrbitAsync);
        yield return ("camera arrows orbit horizontally and vertically", KeyboardOrbitAsync);
        yield return ("camera follow is time based and teleport reanchoring preserves framing", FollowAsync);
        yield return ("camera uses middle drag and respects UI pointer ownership", InputAsync);
    }

    private static PristonCamera CreateCamera()
    {
        return new PristonCamera(Vector3.Zero, Vector3.Forward,
            new PristonCameraSettings { DefaultDistance = 100, DefaultPitch = 0 });
    }

    private static Task ResidentCollisionAsync()
    {
        var far = Wall(80);
        var near = Wall(50);
        var fields = new List<LoadedField> { far, near };
        var collision = new FieldCollisionQuery(fields);
        var camera = CreateCamera();
        camera.UpdateFollow(Vector3.Zero, 1f / 120, collision);
        Assert.Near(100, camera.Position.Z);

        var start = new Vector3(0, 100, 0);
        var destination = new Vector3(0, 100, 100);
        var reached = PristonCameraCollision.Resolve(start, destination, 30, PristonCameraSettings.Default, collision);
        Assert.Near(26, reached.Z, 0.001f);
        Assert.Near(100, reached.Y);
        fields.Remove(near);
        reached = PristonCameraCollision.Resolve(reached, destination, 30, PristonCameraSettings.Default, collision);
        Assert.Near(56, reached.Z, 0.001f);
        fields.Clear();
        reached = PristonCameraCollision.Resolve(reached, destination, 30, PristonCameraSettings.Default, collision);
        Assert.True(reached == destination, "removed field still blocks physical camera movement");
        return Task.CompletedTask;
    }

    private static Task ClearanceAsync()
    {
        var floor = Stage(
            [new(-200, 0, -200), new(-200, 0, 200), new(200, 0, 200), new(200, 0, -200)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)]);
        var result = PristonCameraCollision.Resolve(new Vector3(0, 100, 100), new Vector3(0, -20, 100), 30,
            PristonCameraSettings.Default, floor.Collision);
        Assert.Near(32, result.Y);
        Assert.Near(100, result.Z);
        result = PristonCameraCollision.Resolve(new Vector3(0, -20, 100), new Vector3(0, -20, 100), 30,
            PristonCameraSettings.Default, floor.Collision);
        Assert.Near(32, result.Y);
        return Task.CompletedTask;
    }

    private static Task OcclusionAsync()
    {
        var roof = Stage(
            [new(-200, 110, 30), new(-200, 110, 80), new(200, 110, 80), new(200, 110, 30)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)]);
        var pole = Stage(
            [new(-4, 0, 50), new(-4, 180, 50), new(4, 180, 50), new(4, 0, 50)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)]);
        var fields = new List<LoadedField> { roof, pole };
        var query = new FieldCollisionQuery(fields);
        var settings = new PristonCameraSettings { DefaultPitch = 1f, DefaultDistance = 200 };
        var camera = new PristonCamera(Vector3.Zero, Vector3.Forward, settings);
        var unobstructed = new PristonCamera(Vector3.Zero, Vector3.Forward, settings);
        Assert.True(query.TryTraceSegment(camera.Target, camera.Position, out _, out _), "fixture does not occlude the player");
        for (var frame = 0; frame < 360; frame++)
        {
            var target = new Vector3(100 * MathF.Sin(frame * 0.025f), 0, 0);
            camera.UpdateFollow(target, 1f / 120, query);
            unobstructed.UpdateFollow(target, 1f / 120);
            Assert.True(Vector3.Distance(camera.Position, unobstructed.Position) < 0.001f, "portal or pole pulled the high camera toward its focus");
            Assert.True(float.IsFinite(camera.View.Determinant()), "camera view became invalid");
        }

        return Task.CompletedTask;
    }

    private static Task StrandedAsync()
    {
        var query = new FieldCollisionQuery([Wall(-50)]);
        var camera = CreateCamera();
        camera.UpdateFollow(Vector3.Zero, 1f / 60, query);
        Assert.Near(100, camera.Position.Z);
        for (var frame = 1; frame <= 60; frame++)
        {
            camera.UpdateFollow(new Vector3(0, 0, -5f * frame), 1f / 60, query);
        }

        for (var frame = 0; frame < 60; frame++)
        {
            camera.UpdateFollow(new Vector3(0, 0, -300), 1f / 60, query);
        }

        Assert.True(camera.Position.Z < -50, $"camera stayed behind the wall the target crossed (z={camera.Position.Z:F1})");
        Assert.True(!query.TryTraceSegment(camera.Target, camera.Position, out _, out _), "camera lost sight of its focus");
        Assert.Near(-200, camera.Position.Z, 1f);
        return Task.CompletedTask;
    }

    private static Task FieldOfViewAsync()
    {
        var vertical = MathHelper.ToRadians(65f);
        foreach (var aspect in new[] { 16f / 9f, 1920f / 1009f, 1912f / 922f, 21f / 9f })
        {
            var capped = PristonCamera.VerticalFieldOfView(vertical, 90f, aspect);
            var horizontal = 2f * MathF.Atan(MathF.Tan(capped * 0.5f) * aspect);
            Assert.Near(90f, MathHelper.ToDegrees(horizontal), 0.01f);
        }

        Assert.Near(vertical, PristonCamera.VerticalFieldOfView(vertical, 90f, 4f / 3f));
        // The side view of an action field keeps its narrower angle on every common window shape.
        foreach (var aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f })
        {
            Assert.Near(FieldActionCamera.FieldOfViewRadians, PristonCamera.VerticalFieldOfView(FieldActionCamera.FieldOfViewRadians, 90f, aspect));
        }

        return Task.CompletedTask;
    }

    private static Task FollowAsync()
    {
        foreach (var rate in new[] { 30, 60, 120, 144 })
        {
            var camera = CreateCamera();
            var destination = new Vector3(100, 8, 0);
            camera.UpdateFollow(destination, 1f / rate);
            Assert.True(camera.Target.Y > 30 && camera.Target.Y < 38, "step height instantly changed the camera focus");
            for (var frame = 1; frame < rate; frame++)
            {
                camera.UpdateFollow(destination, 1f / rate);
            }

            Assert.Near(100 * (1f - MathF.Exp(-14)), camera.Target.X, 0.001f);
            var offset = camera.Position - camera.Target;
            camera.SnapFollow(new Vector3(8000, 200, 9000));
            Assert.Near(8000, camera.Target.X);
            Assert.Near(230, camera.Target.Y);
            Assert.Near(9000, camera.Target.Z);
            Assert.True(Vector3.Distance(offset, camera.Position - camera.Target) < 0.001f, "teleport changed framing");
        }

        return Task.CompletedTask;
    }

    private static Task InputAsync()
    {
        var camera = CreateCamera();
        camera.UpdateOrbit(Mouse(0, 0));
        var original = camera.Position;
        camera.UpdateOrbit(Mouse(100, 100, right: ButtonState.Pressed));
        camera.UpdateOrbit(Mouse(150, 150, right: ButtonState.Pressed));
        camera.UpdateFollow(Vector3.Zero, 0);
        Assert.True(camera.Position == original, "right-button skill input changed the camera orbit");
        camera.UpdateOrbit(Mouse(200, 200, middle: ButtonState.Pressed));
        camera.UpdateFollow(Vector3.Zero, 0);
        Assert.True(camera.Position == original, "first middle-button press used a stale pointer delta");
        camera.UpdateOrbit(Mouse(210, 205, middle: ButtonState.Pressed));
        camera.UpdateFollow(Vector3.Zero, 0);
        Assert.True(camera.Position != original, "middle drag did not orbit");
        var orbited = camera.Position;
        camera.UpdateOrbit(Mouse(600, 600, middle: ButtonState.Pressed, wheel: 120), pointerCaptured: true);
        camera.UpdateFollow(Vector3.Zero, 0);
        Assert.True(camera.Position == orbited, "UI drag or scroll changed the camera");
        camera.UpdateOrbit(Mouse(600, 600, middle: ButtonState.Pressed, wheel: 120));
        camera.UpdateFollow(Vector3.Zero, 0);
        Assert.True(camera.Position == orbited, "releasing UI capture replayed old pointer motion");
        camera.SnapFollow(new Vector3(1000, 0, 1000));
        Assert.True(Vector3.Distance(orbited - new Vector3(0, 30, 0), camera.Position - camera.Target) < 0.001f,
            "teleport reset the player's chosen orbit");
        return Task.CompletedTask;
    }

    private static Task EdgeOrbitAsync()
    {
        foreach (var width in new[] { 800, 1280, 2560 })
        {
            var viewport = new Rectangle(20, 10, width, 720);
            foreach (var rate in new[] { 30, 60, 120, 144 })
            {
                foreach (var left in new[] { false, true })
                {
                    var camera = CreateCamera();
                    camera.UpdateOrbit(Mouse(viewport.Center.X, viewport.Center.Y));
                    var edge = Mouse(left ? viewport.Left : viewport.Right - 1, viewport.Center.Y);
                    for (var frame = 0; frame < rate; frame++)
                    {
                        camera.UpdateOrbit(edge, viewport: viewport, deltaSeconds: 1f / rate);
                    }

                    camera.UpdateFollow(Vector3.Zero, 0);
                    Assert.Near(left ? 1f : -1f, camera.FlatForward.X, 0.0001f);
                    Assert.Near(0, camera.FlatForward.Z, 0.0001f);
                }
            }

            var paused = CreateCamera();
            paused.UpdateOrbit(Mouse(viewport.Center.X, viewport.Center.Y));
            var initial = paused.Position;
            foreach (var outside in new[]
            {
                Mouse(viewport.Left - 1, viewport.Center.Y),
                Mouse(viewport.Right, viewport.Center.Y),
                Mouse(viewport.Left, viewport.Top - 1),
                Mouse(viewport.Right - 1, viewport.Bottom),
                Mouse(viewport.Left + 8, viewport.Center.Y),
                Mouse(viewport.Right - 8, viewport.Center.Y)
            })
            {
                paused.UpdateOrbit(outside, viewport: viewport, deltaSeconds: 1);
                paused.UpdateFollow(Vector3.Zero, 0);
                Assert.True(paused.Position == initial, "cursor outside the edge bands rotated the camera");
            }

            var border = Mouse(viewport.Left, viewport.Center.Y);
            paused.UpdateOrbit(border, pointerCaptured: true, viewport: viewport, deltaSeconds: 1);
            paused.UpdateFollow(Vector3.Zero, 0);
            Assert.True(paused.Position == initial, "HUD capture allowed edge rotation");
            paused.UpdateOrbit(border, viewport: viewport, deltaSeconds: 1, inputEnabled: false);
            paused.UpdateFollow(Vector3.Zero, 0);
            Assert.True(paused.Position == initial, "inactive window allowed edge rotation");
            paused.UpdateOrbit(border, viewport: viewport, deltaSeconds: 1);
            paused.UpdateFollow(Vector3.Zero, 0);
            Assert.True(paused.Position == initial, "focus regain replayed stale edge input");
        }

        return Task.CompletedTask;
    }

    private static Task KeyboardOrbitAsync()
    {
        var camera = CreateCamera();
        camera.UpdateOrbit(Mouse(400, 300));
        camera.UpdateFollow(Vector3.Zero, 0);
        var initialDistance = camera.Position.Length();
        camera.UpdateOrbit(Mouse(400, 300), new KeyboardState(Keys.Left, Keys.Up), deltaSeconds: 1f);
        camera.UpdateFollow(Vector3.Zero, 0);

        Assert.Near(1f, camera.FlatForward.X, 0.0001f);
        Assert.Near(0, camera.FlatForward.Z, 0.0001f);
        Assert.True(camera.Position.Length() < initialDistance, "up arrow did not move the camera closer");
        camera.UpdateOrbit(Mouse(400, 300), new KeyboardState(Keys.Right, Keys.Down), deltaSeconds: 2f);
        camera.UpdateFollow(Vector3.Zero, 0);
        var fartherDistance = camera.Position.Length();
        Assert.True(fartherDistance > initialDistance, "down arrow did not move the camera away");
        camera.UpdateOrbit(Mouse(400, 300), new KeyboardState(Keys.Up), pointerCaptured: true, deltaSeconds: 1f);
        camera.UpdateFollow(Vector3.Zero, 0);
        Assert.True(camera.Position.Length() < fartherDistance, "up arrow was blocked by mouse capture");

        var responsive = CreateCamera();
        responsive.UpdateOrbit(Mouse(400, 300));
        responsive.UpdateFollow(Vector3.Zero, 0);
        var frameDistance = responsive.Position.Length();
        responsive.UpdateOrbit(Mouse(400, 300), new KeyboardState(Keys.Up), deltaSeconds: 1f / 60f);
        responsive.UpdateFollow(Vector3.Zero, 0);
        Assert.True(responsive.Position.Length() < frameDistance - 1f, "up arrow zoom is too slow for a frame-sized update");
        return Task.CompletedTask;
    }

    private static MouseState Mouse(int x, int y, ButtonState middle = ButtonState.Released, ButtonState right = ButtonState.Released, int wheel = 0)
    {
        return new MouseState(x, y, wheel, ButtonState.Released, middle, right, ButtonState.Released, ButtonState.Released);
    }

    private static LoadedField Wall(float z)
    {
        return Stage(
            [new(-200, -200, z), new(-200, 200, z), new(200, 200, z), new(200, -200, z)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)]);
    }

    private static LoadedField Stage(Vector3[] positions, PristonStageFace[] faces)
    {
        var vertices = positions.Select(position => new PristonStageVertex(position, Color.White)).ToArray();
        var material = new PristonMaterial(true, 1, 0, 1, false, Vector3.One, 0, 1,
            PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var stage = new PristonStage("camera-fixture.smd", [material], vertices, faces, [], [], default);
        return new LoadedField(RicartenField.Definition, stage, new PristonCollisionWorld(stage), default);
    }
}
