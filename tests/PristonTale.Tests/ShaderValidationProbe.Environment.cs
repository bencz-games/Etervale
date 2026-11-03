using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

// GPU check of the image-based lighting: a uniform grey dome gives an irradiance whose upward value is the light
// model's sky light, whatever the dome's own level and the windowing of its bands. The specular atlas's scale is that
// ratio when the dome is brighter than the sky light; a darker dome keeps its visible level (a scale of 1) and a
// uniform fill of the sky light's colour makes up the rest.
internal sealed partial class ShaderValidationProbe
{
    private void VerifyEnvironment()
    {
        using var triangle = new PristonFullscreenTriangle(GraphicsDevice);
        var radiance = PristonColorSpace.SrgbToLinear(128f / 255f);
        foreach (var sky in new[] { new Vector3(0.2f, 0.25f, 0.3f), new Vector3(0.1f, 0.12f, 0.15f) })
        {
            using var effect = Content.Load<Effect>(PristonEnvironmentLighting.Asset).Clone();
            using var environment = new PristonEnvironmentLighting(GraphicsDevice, triangle, effect);
            var ground = new Vector3(0.05f);
            environment.BeginFaces(new Color(128, 128, 128));
            environment.Build(0, 1f, sky, ground);

            GraphicsDevice.SetRenderTarget(null);
            var sh = new Vector4[PristonEnvironmentLighting.ShEntries];
            environment.Sh.GetData<Vector4>(sh.AsSpan());
            // Upward irradiance: band 0 and the y term (the uniform dome has no band-2 term).
            var up = new Vector3(sh[0].X, sh[0].Y, sh[0].Z) * 0.282095f + new Vector3(sh[1].X, sh[1].Y, sh[1].Z) * (0.488603f * 2f / 3f);
            // The ground adds nothing to an upward surface; the sky light's colour is 35 % the dome's grey, 65 % the
            // model's, in the dome's share and the fill's.
            var level = 0.2126f * sky.X + 0.7152f * sky.Y + 0.0722f * sky.Z;
            var expectedScale = MathF.Min(1f, level / radiance);
            var domeShare = MathF.Min(1f, radiance / level);
            var expectedUp = new Vector3(level) * 0.35f + sky * 0.65f;
            var expectedFill = expectedUp * (1f - domeShare);
            var fill = new Vector3(sh[9].Y, sh[9].Z, sh[9].W);
            // Order-2 harmonics underestimate a uniform hemisphere at the zenith by about 3 %, which the fill makes up.
            Assert.True(MathF.Abs(sh[9].X / expectedScale - 1f) < 0.05f, $"environment scale {sh[9].X}, expected {expectedScale}");
            Assert.True(Vector3.Distance(fill, expectedFill) < 0.04f * sky.Length(), $"environment fill {fill}, expected {expectedFill}");
            Assert.True(Vector3.Distance(up, expectedUp) < 0.01f, $"environment upward irradiance {up}, expected {expectedUp}");
            _verified += 3;
        }
    }
}
