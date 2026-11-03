using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Effects.Vfx;

// Quads of the component and sequence layers: camera billboards, ground quads, upright and beam strips and trails.
// Their colours are the original 0..255 display values, decoded to linear here.
public sealed partial class VfxRenderer
{
    private Matrix _inverseView;

    private void AppendComponents(VfxInstance instance, int pass, Vector3 right, Vector3 up, Vector3 camera)
    {
        foreach (var state in instance.ComponentStates)
        {
            var component = state.Component;
            var source = component.Source;
            if (source.Kind == VfxComponentKind.Model || PassOf(source.Blend) != pass || !state.Drawn)
            {
                continue;
            }

            if (source.Kind == VfxComponentKind.Particles)
            {
                var texture = component.Textures[0];
                foreach (ref readonly var particle in state.Particles.AsSpan())
                {
                    if (!particle.Live)
                    {
                        continue;
                    }

                    var color = Decode(particle.Color);
                    if (particle.Beam)
                    {
                        AppendStrip(texture, source.Blend, particle.Position + particle.Direction, particle.Position + particle.Direction * particle.Size.Y,
                            particle.Size.X, color, camera, true);
                    }
                    else
                    {
                        AppendBillboard(texture, source.Blend, particle.Position, particle.Size.X * 0.5f, particle.Size.Y * 0.5f, color, right, up);
                    }
                }

                continue;
            }

            ref readonly var element = ref state.Element;
            var frame = component.Textures[Math.Min(element.TextureFrame, component.Textures.Length - 1)];
            var tint = Decode(element.Color);
            switch (source.Kind)
            {
                case VfxComponentKind.Billboard:
                    AppendBillboard(frame, source.Blend, element.Position, element.Size.X * 0.5f, element.Size.Y * 0.5f, tint, right, up);
                    break;
                case VfxComponentKind.Ground:
                    AppendGround(frame, source.Blend, element.Position, element.Size.X * 0.5f, element.Size.Y * 0.5f, tint, Matrix.Identity);
                    break;
                case VfxComponentKind.Beam:
                    AppendStrip(frame, source.Blend, element.Position + component.BeamFrom, element.Position + component.BeamTo, element.Size.X, tint, camera, true);
                    break;
            }
        }
    }

    private void AppendSequences(VfxInstance instance, int pass, Vector3 right, Vector3 up, Vector3 camera)
    {
        foreach (var state in instance.SequenceStates)
        {
            var sequence = state.Sequence;
            var source = sequence.Source;
            if (PassOf(source.Blend) != pass || state.LiveCount == 0)
            {
                continue;
            }

            var turn = instance.Orientation;
            var texture = sequence.Texture;
            for (var index = 0; index < source.Count; index++)
            {
                if (!state.Alive[index])
                {
                    continue;
                }

                ref readonly var particle = ref state.Particles[index];
                var color = Decode(particle.Color);
                var halfWidth = MathF.Max(0f, particle.Size) * 0.5f;
                var halfHeight = MathF.Max(0f, particle.Stretch != 0f ? particle.Stretch : particle.Size) * 0.5f;
                var position = particle.World + Vector3.TransformNormal(SequenceState.OrbitTurned(particle), turn);
                switch (source.Shape)
                {
                    case VfxSequenceShape.Billboard:
                        if (particle.Angle == Vector3.Zero)
                        {
                            AppendBillboard(texture, source.Blend, position, halfWidth, halfHeight, color, right, up);
                        }
                        else
                        {
                            AppendTurnedBillboard(texture, source.Blend, position, halfWidth, halfHeight, color, VfxAngles.Rotation(particle.Angle));
                        }

                        break;
                    case VfxSequenceShape.Ground:
                        AppendGround(texture, source.Blend, position, halfWidth, halfHeight, color, VfxAngles.Rotation(particle.Angle) * turn);
                        break;
                    case VfxSequenceShape.Upright:
                    {
                        var axis = Vector3.TransformNormal(new Vector3(0f, halfHeight, 0f), VfxAngles.Rotation(particle.Angle) * turn);
                        AppendStrip(texture, source.Blend, position + axis, position - axis, halfWidth, color, camera, true);
                        break;
                    }
                    case VfxSequenceShape.Trail:
                        AppendSequenceTrail(state, index, texture, source.Blend, particle.World, turn, halfWidth, color, camera, up);
                        break;
                }
            }
        }
    }

    private void AppendSequenceTrail(SequenceState state, int index, Texture2D texture, VfxBlend blend, Vector3 world, Matrix turn, float halfWidth,
        Vector4 color, Vector3 camera, Vector3 up)
    {
        var points = state.Trail(index);
        if (points.Length < 2)
        {
            return;
        }

        if (state.Particles[index].Stretch <= 0f)
        {
            // A streak from the birth position to the current one.
            AppendStrip(texture, blend, world + Vector3.TransformNormal(points[0], turn), world + Vector3.TransformNormal(points[1], turn), halfWidth, color,
                camera, true);
            return;
        }

        // Remembered positions joined by quads a screen-vertical band wide, the texture spread along the trail.
        var count = points.Length;
        var band = up * halfWidth;
        for (var segment = 0; segment < count - 1; segment++)
        {
            var a = world + Vector3.TransformNormal(points[segment], turn);
            var b = world + Vector3.TransformNormal(points[segment + 1], turn);
            var v0 = segment / (float)(count - 1);
            var v1 = (segment + 1) / (float)(count - 1);
            AppendCorners(texture, blend, a + band, a - band, b - band, b + band, color, new Vector4(0f, v0, 1f, v1));
        }
    }

    // A camera-facing quad; half extents along the view's right and up.
    private void AppendBillboard(Texture2D texture, VfxBlend blend, Vector3 center, float halfWidth, float halfHeight, Vector4 color, Vector3 right, Vector3 up)
    {
        var x = right * halfWidth;
        var y = up * halfHeight;
        AppendCorners(texture, blend, center - x + y, center + x + y, center + x - y, center - x - y, color, new Vector4(0f, 0f, 1f, 1f));
    }

    // A quad turned in view space (X, Y and Z angles) before facing the camera.
    private void AppendTurnedBillboard(Texture2D texture, VfxBlend blend, Vector3 center, float halfWidth, float halfHeight, Vector4 color, Matrix rotation)
    {
        var basis = rotation * _inverseView;
        var x = Vector3.TransformNormal(new Vector3(halfWidth, 0f, 0f), basis);
        var y = Vector3.TransformNormal(new Vector3(0f, halfHeight, 0f), basis);
        AppendCorners(texture, blend, center - x + y, center + x + y, center + x - y, center - x - y, color, new Vector4(0f, 0f, 1f, 1f));
    }

    // A quad flat on the ground plane; V runs from the back (-Z, forward) edge to the front.
    private void AppendGround(Texture2D texture, VfxBlend blend, Vector3 center, float halfWidth, float halfDepth, Vector4 color, Matrix rotation)
    {
        var x = Vector3.TransformNormal(new Vector3(halfWidth, 0f, 0f), rotation);
        var z = Vector3.TransformNormal(new Vector3(0f, 0f, halfDepth), rotation);
        AppendCorners(texture, blend, center - x + z, center + x + z, center + x - z, center - x - z, color, new Vector4(0f, 0f, 1f, 1f));
    }

    // A camera-facing strip from one point to another, halfWidth to each side; V runs from start (0) to end (1). The
    // original extends the strip behind its start by its half width.
    private void AppendStrip(Texture2D texture, VfxBlend blend, Vector3 start, Vector3 end, float halfWidth, Vector4 color, Vector3 camera, bool extend)
    {
        var along = end - start;
        if (along.LengthSquared() < 1e-6f)
        {
            return;
        }

        var side = Vector3.Cross(along, camera - start);
        if (side.LengthSquared() < 1e-8f)
        {
            return;
        }

        side = Vector3.Normalize(side) * halfWidth;
        if (extend)
        {
            start -= Vector3.Normalize(along) * halfWidth;
        }

        AppendCorners(texture, blend, start - side, start + side, end + side, end - side, color, new Vector4(0f, 0f, 1f, 1f));
    }

    // Corners top-left, top-right, bottom-right, bottom-left with uv (u0, v0) .. (u1, v1).
    private void AppendCorners(Texture2D texture, VfxBlend blend, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color, Vector4 uv)
    {
        if (!BeginQuad(texture, blend, out var at))
        {
            return;
        }

        var parameters = new Vector2(VfxSystem.StripSoftness, 0f);
        _scratch[at] = new VfxVertex { Position = a, Color = color, TexCoord = new Vector2(uv.X, uv.Y), Params = parameters };
        _scratch[at + 1] = new VfxVertex { Position = b, Color = color, TexCoord = new Vector2(uv.Z, uv.Y), Params = parameters };
        _scratch[at + 2] = new VfxVertex { Position = c, Color = color, TexCoord = new Vector2(uv.Z, uv.W), Params = parameters };
        _scratch[at + 3] = new VfxVertex { Position = d, Color = color, TexCoord = new Vector2(uv.X, uv.W), Params = parameters };
    }

    // Original colours are display values 0..255: rgb decode to linear, alpha stays linear.
    internal static Vector4 Decode(Vector4 color) =>
        new(SrgbToLinear(color.X / 255f), SrgbToLinear(color.Y / 255f), SrgbToLinear(color.Z / 255f), Math.Clamp(color.W / 255f, 0f, 1f));

    // The sRGB curve sampled at 1024 steps and read with linear interpolation (within 1e-6 of the curve): every particle
    // decodes its colour each frame, so the decode must not cost a Pow per channel.
    private const int SrgbSteps = 1024;
    private static readonly float[] SrgbTable = BuildSrgbTable();

    private static float SrgbToLinear(float value)
    {
        var scaled = Math.Clamp(value, 0f, 1f) * SrgbSteps;
        var index = Math.Min((int)scaled, SrgbSteps - 1);
        return SrgbTable[index] + (SrgbTable[index + 1] - SrgbTable[index]) * (scaled - index);
    }

    private static float[] BuildSrgbTable()
    {
        var table = new float[SrgbSteps + 1];
        for (var index = 0; index <= SrgbSteps; index++)
        {
            var value = index / (float)SrgbSteps;
            table[index] = PristonColorSpace.SrgbToLinear(value);
        }

        return table;
    }
}
